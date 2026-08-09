using UnityEngine;

namespace GameJam.Gameplay.Cannon
{
    public sealed class CannonShotPresenter : MonoBehaviour
    {
        private static readonly int CannonShotState = Animator.StringToHash("Cannon_Shot");

        [SerializeField] private Animator _cannonAnimator;
        [SerializeField] private ParticleSystem _smokeBurstParticle;
        [SerializeField] private ParticleSystem[] _smokeParticleSystems;

        public void PlayShot()
        {
            if (_cannonAnimator != null && _cannonAnimator.gameObject.activeInHierarchy)
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
