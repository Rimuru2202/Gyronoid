using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class AsteroidKillZone : MonoBehaviour
    {
        [SerializeField] private GameplayBootstrapper _bootstrapper;

        private void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c != null) c.isTrigger = true;
        }

        public void SetBootstrapper(GameplayBootstrapper b)
        {
            _bootstrapper = b;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null) return;

            var asteroid = other.GetComponentInParent<Asteroid>();
            if (asteroid != null)
            {
                if (_bootstrapper != null)
                    _bootstrapper.OnAsteroidMissed(asteroid);
                else
                    asteroid.Missed();
            }
        }
    }
}