using System.Collections.Generic;
using UnityEngine;

namespace GameJam.Gameplay.Wall
{
    /// <summary>
    /// Turns the structure while the player drags, by sweeping the blocks' own kinematic bodies
    /// around a vertical axis through the structure centre.
    ///
    /// It used to rotate this transform with <c>RotateAround</c> in <c>Update</c>, which was the
    /// whole of the drag-collapse bug: a transform write teleports a kinematic body, so once a
    /// shot had made some blocks dynamic, every drag frame shoved the still-kinematic wall into
    /// its loose neighbours at a fake relative velocity. That tripped
    /// <c>collisionActivationVelocity</c> block by block, and each activation released the column
    /// above it, so a drag read as an explosion.
    ///
    /// Task brief 22 proposed fixing that by putting one kinematic Rigidbody on this root and
    /// driving it with MovePosition/MoveRotation. That cannot work here, and the brief's own
    /// compound-collider note says why without drawing the conclusion: every block and every wall
    /// carries its own Rigidbody, so a body on this root would own no colliders at all. A body
    /// with no shapes has no contacts, imparts no friction, and moving it would still drag the
    /// blocks along by the transform hierarchy - which is the same teleport, one step later. So
    /// the sweep is applied where the colliders actually live, on the blocks themselves, and this
    /// transform now stays still.
    ///
    /// Only un-activated (kinematic) blocks are swept. Anything already knocked loose is dynamic
    /// and is left entirely to physics, so falling pieces keep falling and a loose block resting
    /// on the structure is carried by real friction against a real surface velocity.
    /// </summary>
    public class SpinOnAxis : MonoBehaviour
    {
        [SerializeField] private Transform rotationCenter;
        [SerializeField] private float speed;

        private Vector3 restPosition;
        private Quaternion restRotation;

        /// <summary>
        /// Reused across drags so the per-drag refresh does not allocate. The query list is only
        /// a landing pad for <c>GetComponentsInChildren</c>; the swept list is the filtered set
        /// this component actually drives and owns the interpolation of.
        /// </summary>
        private readonly List<Rigidbody> bodyQuery = new List<Rigidbody>();
        private readonly List<Rigidbody> sweptBodies = new List<Rigidbody>();

        /// <summary>
        /// Where each swept body stood when the drag began, as an offset from the pivot, and how
        /// it was turned. Every step's target is computed from these and the total angle rather
        /// than from the body's current pose.
        ///
        /// Stepping a body from where it currently is looks equivalent and is not: each of the
        /// several hundred bodies then integrates its own rotation independently, and the
        /// floating-point error in rotating a position vector accumulates at a different rate for
        /// each one. The radius from the pivot drifts, blocks separate and interpenetrate, and a
        /// structure that used to be rigid because a single parent transform carried it visibly
        /// shears apart over a long drag. Absolute targets cannot drift: the pose is a pure
        /// function of the start pose and one angle.
        /// </summary>
        private readonly List<Vector3> sweptOffsets = new List<Vector3>();

        private readonly List<Quaternion> sweptRotations = new List<Quaternion>();

        /// <summary>Total turn since the drag began. The one accumulating value, and it is a scalar.</summary>
        private float sweptAngle;

        private bool sweeping;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<Collider> colliderAudit = new List<Collider>();
        private bool hasAuditedStructure;
#endif

        private void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        /// <summary>
        /// Leaving the blocks interpolating - or worse, mid-sweep - because this component was
        /// switched off would outlive the drag, so a disable ends the sweep like a release does.
        /// </summary>
        private void OnDisable()
        {
            EndSweep();
        }

        public void SetSpeed(float value)
        {
            speed = value;

            if (Mathf.Approximately(value, 0f))
            {
                EndSweep();
                return;
            }

            BeginSweep();
        }

        /// <summary>
        /// Puts the transform back where it started and stops it.
        ///
        /// The transform writes are kept although the sweep no longer moves this object: a scene
        /// or a test rig that rotated the root by hand still gets squared up, and they cost
        /// nothing. There is no body on this root to re-sync afterwards - see the class note on
        /// why one is not added - so the write cannot leave a stale physics pose behind for a
        /// later Move* to sweep across the map.
        ///
        /// The blocks themselves are not un-rotated, and do not need to be: this runs between
        /// runs, and the next BuildMap destroys every block and rebuilds it at its authored local
        /// position, so the structure always comes back square.
        ///
        /// Called while a drag is in flight it is still safe - the sweep is ended first, so no
        /// further target is issued - but any block already knocked loose is dynamic and stays
        /// where physics left it rather than snapping back with the structure. That is latent
        /// rather than live: the only caller is GameFlowController.ResetPlayfield, which runs
        /// between runs when the map is about to be rebuilt anyway.
        /// </summary>
        public void ResetRotation()
        {
            speed = 0f;
            EndSweep();
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Each run gets one audit, on its first rotating step, because each run builds a new
            // structure to audit.
            hasAuditedStructure = false;
#endif
        }

        public void SetRotationCenter(Transform center)
        {
            rotationCenter = center;
        }

        private void FixedUpdate()
        {
            if (!sweeping)
            {
                // A speed authored in the inspector rather than set by a drag still spins, which
                // is how the test scenes use this. One float compare per step.
                if (Mathf.Approximately(speed, 0f))
                {
                    return;
                }

                BeginSweep();
            }

            if (rotationCenter == null)
            {
                return;
            }

            float angle = speed * Time.fixedDeltaTime;
            if (Mathf.Approximately(angle, 0f))
            {
                return;
            }

            // The only thing that accumulates is this scalar. Each body's target is then a pure
            // function of where it started and how far the drag has turned, so no per-body error
            // can build up and the structure stays exactly as rigid as its parent used to make it.
            sweptAngle += angle;

            Quaternion turn = Quaternion.AngleAxis(sweptAngle, Vector3.up);
            Vector3 pivot = rotationCenter.position;

            for (int i = 0; i < sweptBodies.Count; i++)
            {
                Rigidbody body = sweptBodies[i];
                if (body == null)
                {
                    continue;
                }

                if (!body.isKinematic)
                {
                    // Knocked loose since the drag began. It belongs to physics now, so it is
                    // dropped from the sweep and handed straight back to the interpolation
                    // setting KnockdownBlock wants for a falling block.
                    body.interpolation = RigidbodyInterpolation.None;
                    sweptBodies[i] = null;
                    continue;
                }

                // RotateAround expressed as a kinematic target rather than a teleport. PhysX
                // moves the body to this pose over the step, so contacts see a genuine surface
                // velocity: a loose block resting on the structure is carried by friction, and
                // the activation thresholds only ever see speeds that really happened.
                body.MovePosition(pivot + (turn * sweptOffsets[i]));
                body.MoveRotation(turn * sweptRotations[i]);
            }
        }

        /// <summary>
        /// Collects the bodies this drag will sweep and lends them interpolation for its
        /// duration.
        ///
        /// The set is rebuilt per drag rather than per step, which is safe because no kinematic
        /// body can appear under the structure while a run is in progress. The only two things
        /// that add bodies mid-run both add dynamic ones: BreakableWall.SpawnCell activates every
        /// cell it spawns, and a block's debris is loose by definition. Blocks that leave the set
        /// - activated, or despawned by the fall zone - are dropped by the per-step guards above,
        /// so the list only ever goes stale in the harmless direction.
        /// </summary>
        private void BeginSweep()
        {
            if (sweeping)
            {
                return;
            }

            sweeping = true;

            // The List overload writes into the list's existing capacity, so this costs a
            // hierarchy walk on drag start and no garbage.
            GetComponentsInChildren(false, bodyQuery);

            sweptBodies.Clear();
            sweptOffsets.Clear();
            sweptRotations.Clear();
            sweptAngle = 0f;

            // Read once: every offset below is measured from this same point, so the structure
            // keeps its shape even if the pivot object itself is later moved.
            Vector3 pivot = rotationCenter != null ? rotationCenter.position : transform.position;

            for (int i = 0; i < bodyQuery.Count; i++)
            {
                Rigidbody body = bodyQuery[i];
                if (body == null || !body.isKinematic)
                {
                    continue;
                }

                sweptBodies.Add(body);
                sweptOffsets.Add(body.position - pivot);
                sweptRotations.Add(body.rotation);

                // A deliberate, drag-scoped exception to the rule in
                // KnockdownBlock.ApplyRuntimeBodySettings that blocks never interpolate. That
                // rule is about a collapse, when hundreds of bodies are awake and simulating at
                // once and interpolation is what melts a mid-range phone. This is the opposite
                // case: these bodies are kinematic and merely being carried, there is no
                // simulation to pay for, and without interpolation the structure would visibly
                // step at the 50 Hz physics rate now that the smooth per-frame transform write
                // is gone. EndSweep puts every one of them back to None, and so does the
                // per-step guard the moment a block is knocked loose, so the rule is back in
                // force for exactly the situation it was written for.
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }

            bodyQuery.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AuditUnsweptColliders();
#endif
        }

        /// <summary>
        /// Ends the drag and hands every borrowed interpolation setting back. Only bodies this
        /// component actually changed are touched, so a projectile resting inside the structure
        /// keeps the continuous, interpolated settings it needs.
        /// </summary>
        private void EndSweep()
        {
            if (!sweeping)
            {
                return;
            }

            sweeping = false;

            for (int i = 0; i < sweptBodies.Count; i++)
            {
                Rigidbody body = sweptBodies[i];
                if (body != null)
                {
                    body.interpolation = RigidbodyInterpolation.None;
                }
            }

            sweptBodies.Clear();
            sweptOffsets.Clear();
            sweptRotations.Clear();
            sweptAngle = 0f;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Asserts the assumption the sweep rests on: that every collider under the structure is
        /// owned by a Rigidbody, and so is either swept with the structure or left to physics on
        /// purpose. A collider with no body anywhere above it belongs to neither group and would
        /// stand still while the rest of the map turned.
        ///
        /// Brief 22 asked for this check against a compound collider forming on the root. That
        /// reading is moot now no body is added there, but the underlying question is the same
        /// one and is still worth asking, so the check is kept and re-aimed. attachedRigidbody is
        /// exactly "the nearest body at or above this collider", which answers it outright rather
        /// than by walking parents.
        ///
        /// Runtime rather than editor-time: at editor time GeneratedLayoutBlocks is empty, so an
        /// editor-time assert would inspect nothing at all.
        /// </summary>
        private void AuditUnsweptColliders()
        {
            if (hasAuditedStructure)
            {
                return;
            }

            hasAuditedStructure = true;
            GetComponentsInChildren(false, colliderAudit);

            int unowned = 0;
            for (int i = 0; i < colliderAudit.Count; i++)
            {
                Collider candidate = colliderAudit[i];
                if (candidate == null || candidate.attachedRigidbody != null)
                {
                    continue;
                }

                unowned++;
                if (unowned <= 8)
                {
                    Debug.LogWarning(
                        $"{nameof(SpinOnAxis)}: \"{candidate.name}\" has a collider with no "
                        + "Rigidbody above it, so it will not turn with the structure.",
                        candidate);
                }
            }

            if (unowned > 8)
            {
                Debug.LogWarning(
                    $"{nameof(SpinOnAxis)}: {unowned} colliders under the structure have no "
                    + "Rigidbody above them; the first 8 are listed.",
                    this);
            }

            colliderAudit.Clear();
        }
#endif
    }
}
