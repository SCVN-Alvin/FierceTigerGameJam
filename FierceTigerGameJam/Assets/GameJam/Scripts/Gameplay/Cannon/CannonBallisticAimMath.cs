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
