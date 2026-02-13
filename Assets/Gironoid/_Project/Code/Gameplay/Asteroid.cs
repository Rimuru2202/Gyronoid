// Assets/Gironoid/_Project/Code/Gameplay/Asteroid.cs
using System;
using UnityEngine;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class Asteroid : MonoBehaviour
    {
        [SerializeField] private float _baseHp = 3f;
        [SerializeField] private float _speed = 3.5f;

        private float _hp;
        private bool _alive;

        private Action<Asteroid, AsteroidDespawnReason> _despawn;

        public enum AsteroidDespawnReason
        {
            Killed = 0,
            Missed = 1,
            HitPlayer = 2
        }

        public void Activate(float hp, float speed, Action<Asteroid, AsteroidDespawnReason> despawn)
        {
            _hp = Mathf.Max(0.1f, hp <= 0f ? _baseHp : hp);
            _speed = Mathf.Max(0.1f, speed <= 0f ? _speed : speed);
            _despawn = despawn;
            _alive = true;

            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.down * _speed;
        }

        public void TakeDamage(float amount)
        {
            if (!_alive) return;

            _hp -= Mathf.Max(0f, amount);
            if (_hp <= 0f)
                Kill();
        }

        private void Kill()
        {
            if (!_alive) return;
            _alive = false;

            _despawn?.Invoke(this, AsteroidDespawnReason.Killed);
        }

        public void Missed()
        {
            if (!_alive) return;
            _alive = false;

            _despawn?.Invoke(this, AsteroidDespawnReason.Missed);
        }

        public void HitPlayer()
        {
            if (!_alive) return;
            _alive = false;

            _despawn?.Invoke(this, AsteroidDespawnReason.HitPlayer);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!_alive) return;
            if (other == null) return;

            // FIX: пуля обычно находится в иерархии игрока (WeaponShooter -> ProjectilePool -> Projectile),
            // поэтому GetComponentInParent<PlayerShipController2D>() на пуле возвращает игрока.
            // Это приводило к ложному HitPlayer при уничтожении метеоритов.
            var projectile = other.GetComponentInParent<Projectile>();
            if (projectile != null)
                return;

            var player = other.GetComponentInParent<PlayerShipController2D>();
            if (player != null)
            {
                HitPlayer();
            }
        }
    }
}
