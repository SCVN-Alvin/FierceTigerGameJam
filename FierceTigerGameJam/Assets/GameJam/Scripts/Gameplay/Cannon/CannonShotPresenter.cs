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

        [Tooltip("State name inside the pack's Cannon.controller. Their spelling, not ours.")]
        [SerializeField] private string mountedShotState = "Armature|Shoting";

        private string hashedShotState;
        private int mountedShotStateHash;

        public void PlayShot()
        {
            // The mounted model owns the shot when there is one: every pack cannon animates its
            // own recoil, and the barrel underneath is hidden while it stands there, so playing
            // the legacy state as well would animate something nobody can see.
            Animator mountedAnimator = mount != null ? mount.CurrentAnimator : null;
            if (mountedAnimator != null && mountedAnimator.gameObject.activeInHierarchy)
            {
                mountedAnimator.Play(MountedShotStateHash, 0, 0f);
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
        /// Hashed once per distinct state name rather than once per shot: the name is a
        /// serialized field somebody can retype in the inspector between shots, so it cannot be a
        /// static readonly, and hashing a string on every trigger pull is work the shot does not
        /// need.
        /// </summary>
        private int MountedShotStateHash
        {
            get
            {
                if (!string.Equals(hashedShotState, mountedShotState, StringComparison.Ordinal))
                {
                    hashedShotState = mountedShotState;
                    mountedShotStateHash = Animator.StringToHash(mountedShotState);
                }

                return mountedShotStateHash;
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
