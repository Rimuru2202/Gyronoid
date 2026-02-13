// Assets/Gironoid/_Project/Code/Gameplay/Projectile.cs
using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField] private float _speed = 12f;
        [SerializeField] private float _lifeSeconds = 2.5f;

        private float _lifeLeft;
        private float _damage;
        private System.Action<Projectile> _despawn;

        public void Launch(Vector2 dir, float damage, float speed, float lifeSeconds, System.Action<Projectile> despawn)
        {
            _damage = Mathf.Max(0f, damage);
            _speed = Mathf.Max(0.01f, speed);
            _lifeSeconds = Mathf.Max(0.05f, lifeSeconds);
            _lifeLeft = _lifeSeconds;
            _despawn = despawn;

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = dir.normalized * _speed;
            }
        }

        private void Update()
        {
            _lifeLeft -= Time.deltaTime;
            if (_lifeLeft <= 0f)
                DespawnSelf();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!isActiveAndEnabled) return;
            if (other == null) return;

            var asteroid = other.GetComponentInParent<Asteroid>();
            if (asteroid != null)
            {
                asteroid.TakeDamage(_damage);
                DespawnSelf();
            }
        }

        private void DespawnSelf()
        {
            if (_despawn != null)
                _despawn(this);
            else
                gameObject.SetActive(false);
        }
    }
}