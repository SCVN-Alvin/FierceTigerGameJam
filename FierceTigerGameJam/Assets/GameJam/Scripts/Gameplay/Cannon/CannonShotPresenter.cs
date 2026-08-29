using System;
using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class CannonShotPresenter : MonoBehaviour
    {
        private static readonly int CannonShotState = Animator.StringToHash("Cannon_Shot");

        [SerializeField] private Animator _cannonAnimator;
        [SerializeField] private ParticleSystem _smokeBurstParticle;
        [SerializeField] private ParticleSystem[] _smokeParticleSystems;

        [Tooltip("Where the current vehicle model comes from. With a mounted model its own Animator "
                 + "plays the pack's shot; without one the legacy cannon animation still fires.")]
        [SerializeField] private VehicleMount mount;

        [Tooltip("Trigger inside VehicleCannon.controller that takes the mounted model from its "
                 + "idle rest pose through one pass of the pack's firing clip.")]
        [SerializeField] private string mountedShotTrigger = "Shot";

        private string hashedShotTrigger;
        private int mountedShotTriggerHash;

        public void PlayShot()
        {
            // The mounted model owns the shot when there is one: every pack cannon animates its
            // own recoil, and the legacy state drives the tank meshes the mount switched off, so
            // playing it as well would only animate something nobody can see.
            Animator mountedAnimator = mount != null ? mount.CurrentAnimator : null;
            if (mountedAnimator != null && mountedAnimator.gameObject.activeInHierarchy)
            {
                // A trigger rather than a Play: our controller idles by default and returns to
                // idle after one pass, so the shot is a request the state machine answers once
                // instead of a state somebody has to remember to leave.
                mountedAnimator.SetTrigger(MountedShotTriggerHash);
            }
            else if (_cannonAnimator != null && _cannonAnimator.gameObject.activeInHierarchy)
            {
                _cannonAnimator.Play(CannonShotState, 0, 0f);
            }

            Play(_smokeBurstParticle);
            if (_smokeParticleSystems == null)
            {
                return;
            }

            for (int i = 0; i < _smokeParticleSystems.Length; i++)
            {
                Play(_smokeParticleSystems[i]);
            }
        }

        public void ResetPresentation()
        {
            // A trigger set on the frame a run ended survives into the next one and fires the
            // cannon at a player who has not touched anything yet, so it is cleared here.
            Animator mountedAnimator = mount != null ? mount.CurrentAnimator : null;
            if (mountedAnimator != null && mountedAnimator.gameObject.activeInHierarchy)
            {
                mountedAnimator.ResetTrigger(MountedShotTriggerHash);
            }

            Stop(_smokeBurstParticle);
            if (_smokeParticleSystems == null)
            {
                return;
            }

            for (int i = 0; i < _smokeParticleSystems.Length; i++)
            {
                Stop(_smokeParticleSystems[i]);
            }
        }

        /// <summary>
        /// Hashed once per distinct trigger name rather than once per shot: the name is a
        /// serialized field somebody can retype in the inspector between shots, so it cannot be a
        /// static readonly, and hashing a string on every trigger pull is work the shot does not
        /// need.
        /// </summary>
        private int MountedShotTriggerHash
        {
            get
            {
                if (!string.Equals(hashedShotTrigger, mountedShotTrigger, StringComparison.Ordinal))
                {
                    hashedShotTrigger = mountedShotTrigger;
                    mountedShotTriggerHash = Animator.StringToHash(mountedShotTrigger);
                }

                return mountedShotTriggerHash;
            }
        }

        private static void Play(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(true);
        }

        private static void Stop(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
