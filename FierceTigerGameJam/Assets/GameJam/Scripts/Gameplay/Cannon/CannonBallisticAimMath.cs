using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public static class CannonBallisticAimMath
    {
        private const float MinHorizontalDistance = 0.001f;
        private const float MinGravity = 0.001f;
        private const float MinDirectionSqrMagnitude = 0.0001f;
        private const float AnalyticMissToleranceSqrMagnitude = 0.25f;
        private const int SimulationAngleSteps = 45;
        private const int SimulationMaxSteps = 250;
        private const float SimulationFallBelowOrigin = 30f;
        private const float MaxLaunchAngleDegrees = 35f;
        private const float DefaultFixedDeltaTime = 0.02f;

        /// <summary>
        /// Floor under the crest the lob is solved for. Only ever reached by a caller that asks
        /// for a flat arc; it keeps both halves of the flight strictly positive, which is what
        /// makes every target solvable rather than a square root of zero.
        /// </summary>
        private const float MinApexHeight = 0.01f;

        /// <summary>
        /// The velocity to launch at so the shot crests <paramref name="apexHeight"/> above the
        /// higher of <paramref name="origin"/> and <paramref name="target"/> and then falls onto
        /// the target. The opposite trade from <see cref="TryGetLaunchDirection"/>: that one fixes
        /// the speed and lets the shape of the arc fall out of the distance, so a near tap is a
        /// flat shove and a far one a lob, and some aims have no solution at all. This one fixes
        /// the shape and lets the speed fall out, so every shot reads as the same rocket, every
        /// target is reachable, and a tap on the top of a tower is still lobbed over rather than
        /// driven through it.
        ///
        /// <paramref name="gravity"/> is a magnitude, and must be the same number the shot will
        /// actually fall at - the caller's world gravity times whatever it multiplies it by. If
        /// the two ever disagree the shot quietly misses the tap, which is the one thing this
        /// method exists to prevent.
        ///
        /// Assumes gravity points straight down, since it splits the flight into a vertical solve
        /// and a horizontal one. A project that tilts <see cref="Physics.gravity"/> off the Y axis
        /// would need the whole solve done in the gravity's own frame; today's is (0, -9.81, 0).
        /// </summary>
        public static Vector3 GetLobVelocity(Vector3 origin, Vector3 target, float apexHeight, float gravity)
        {
            float g = Mathf.Max(gravity, MinGravity);
            float apex = Mathf.Max(apexHeight, MinApexHeight);

            float apexY = Mathf.Max(origin.y, target.y) + apex;
            float riseHeight = apexY - origin.y;
            float dropHeight = apexY - target.y;

            // Both strictly positive by construction - the crest is above both ends - so neither
            // root below can go imaginary and the flight time can never be zero.
            float verticalSpeed = Mathf.Sqrt(2f * g * riseHeight);
            float timeUp = verticalSpeed / g;
            float timeDown = Mathf.Sqrt(2f * dropHeight / g);
            float flightTime = timeUp + timeDown;

            Vector3 velocity;
            velocity.x = (target.x - origin.x) / flightTime;
            velocity.z = (target.z - origin.z) / flightTime;

            // Half a step of gravity added back, which is not in the closed form above and is the
            // one deliberate departure from it. PhysX integrates semi-implicitly - velocity first,
            // then position from the new velocity - so a body's height after n steps is the
            // continuous answer minus g*dt*t/2: it falls ahead of the maths by half a step's
            // acceleration, growing with the flight. At the defaults that is 25cm of drop by the
            // time a far shot arrives, which lands it about a quarter of a metre short of the tap.
            // Adding g*dt/2 to the launch makes the stepped path pass exactly through the
            // continuous one at every step; simulated, it takes the landing error from ~0.25 u to
            // under a millimetre and makes the crest hit the solved apex instead of 0.11 u under
            // it. Horizontal motion needs no such term - with no acceleration on it, the stepped
            // and continuous answers are already identical.
            velocity.y = verticalSpeed + (g * ResolveFixedDeltaTime() * 0.5f);
            return velocity;
        }

        public static bool TryGetLaunchDirection(Vector3 origin, Vector3 target, float speed, out Vector3 direction)
        {
            direction = Vector3.forward;

            Vector3 delta = target - origin;
            float horizontalSqr = delta.x * delta.x + delta.z * delta.z;
            float horizontalDistance = Mathf.Sqrt(horizontalSqr);

            if (horizontalDistance < MinHorizontalDistance)
            {
                direction = delta.y >= 0f ? Vector3.up : Vector3.down;
                return true;
            }

            float gravity = Mathf.Abs(Physics.gravity.y);
            if (gravity < MinGravity || speed <= 0f)
            {
                direction = NormalizeDirection(delta);
                return true;
            }

            Vector3 horizontalDirection = new Vector3(
                delta.x / horizontalDistance,
                0f,
                delta.z / horizontalDistance);

            float speedSqr = speed * speed;
            float discriminant = speedSqr * speedSqr
                - gravity * (gravity * horizontalSqr + 2f * delta.y * speedSqr);

            if (discriminant >= 0f
                && TryBuildAnalyticDirection(
                    horizontalDirection,
                    horizontalDistance,
                    speedSqr,
                    gravity,
                    discriminant,
                    out direction))
            {
                float analyticAngle = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f));
                if (EvaluateTrajectoryMissSqr(origin, target, horizontalDirection, speed, analyticAngle)
                    <= AnalyticMissToleranceSqrMagnitude)
                {
                    return true;
                }
            }

            return TryGetLaunchDirectionBySimulation(origin, target, horizontalDirection, speed, out direction);
        }

        private static bool TryBuildAnalyticDirection(
            Vector3 horizontalDirection,
            float horizontalDistance,
            float speedSqr,
            float gravity,
            float discriminant,
            out Vector3 direction)
        {
            direction = Vector3.forward;

            float sqrtDiscriminant = Mathf.Sqrt(discriminant);

            // The minus root on purpose. The two ballistic solutions differ only in the sign in
            // front of the square root, and this one is the smaller tangent, so it is the shallower
            // of the two: the direct lob a player reads as a shot, rather than the mortar arc that
            // climbs out of frame and drops on the same block. There is no toggle because a mortar
            // is never what this cannon wants.
            float tanTheta = (speedSqr - sqrtDiscriminant) / (gravity * horizontalDistance);
            float cosTheta = 1f / Mathf.Sqrt(1f + tanTheta * tanTheta);
            float sinTheta = tanTheta * cosTheta;

            direction.x = horizontalDirection.x * cosTheta;
            direction.y = sinTheta;
            direction.z = horizontalDirection.z * cosTheta;
            return direction.sqrMagnitude >= MinDirectionSqrMagnitude;
        }

        private static bool TryGetLaunchDirectionBySimulation(
            Vector3 origin,
            Vector3 target,
            Vector3 horizontalDirection,
            float speed,
            out Vector3 direction)
        {
            direction = horizontalDirection;
            float bestMissSqr = float.MaxValue;
            float bestAngleRad = 45f * Mathf.Deg2Rad;

            for (int i = 1; i <= SimulationAngleSteps; i++)
            {
                float angleRad = (i / (float)SimulationAngleSteps) * MaxLaunchAngleDegrees * Mathf.Deg2Rad;
                float missSqr = EvaluateTrajectoryMissSqr(origin, target, horizontalDirection, speed, angleRad);

                if (missSqr >= bestMissSqr)
                {
                    continue;
                }

                bestMissSqr = missSqr;
                bestAngleRad = angleRad;
            }

            // No out-of-reach warning here any more. The gameplay cannon solves its arc with
            // GetLobVelocity, which can always reach the tap, so this capped search is only
            // reached by the fixed-speed demo path - where a warning about raising the projectile
            // speed would be aimed at nobody.
            return TryBuildDirectionFromAngle(horizontalDirection, bestAngleRad, out direction);
        }

        private static float EvaluateTrajectoryMissSqr(
            Vector3 origin,
            Vector3 target,
            Vector3 horizontalDirection,
            float speed,
            float angleRad)
        {
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);
            Vector3 velocity = horizontalDirection * (speed * cos);
            velocity.y = speed * sin;
            Vector3 position = origin;
            float fixedDeltaTime = ResolveFixedDeltaTime();
            float closestSqr = float.MaxValue;
            float fallLimitY = origin.y - SimulationFallBelowOrigin;
            Vector3 gravity = Physics.gravity;

            for (int i = 0; i < SimulationMaxSteps; i++)
            {
                float missSqr = (position - target).sqrMagnitude;
                if (missSqr < closestSqr)
                {
                    closestSqr = missSqr;
                }

                velocity += gravity * fixedDeltaTime;
                position += velocity * fixedDeltaTime;

                if (position.y < fallLimitY)
                {
                    break;
                }
            }

            return closestSqr;
        }

        private static bool TryBuildDirectionFromAngle(Vector3 horizontalDirection, float angleRad, out Vector3 direction)
        {
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);
            direction.x = horizontalDirection.x * cos;
            direction.y = sin;
            direction.z = horizontalDirection.z * cos;
            return direction.sqrMagnitude >= MinDirectionSqrMagnitude;
        }

        private static float ResolveFixedDeltaTime()
        {
            return Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : DefaultFixedDeltaTime;
        }

        private static Vector3 NormalizeDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                return Vector3.forward;
            }

            return direction.normalized;
        }
    }
}
