// Assets/Gironoid/_Project/Code/Gameplay/WeaponShooter.cs
using System;
using UnityEngine;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class WeaponShooter : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject _projectilePrefab;

        [Header("Shoot params (fallback)")]
        [SerializeField] private float _fallbackDamage = 1f;
        [SerializeField] private float _fallbackFireRate = 4.0f;

        [SerializeField] private float _projectileSpeed = 12f;
        [SerializeField] private float _projectileLife = 2.0f;

        [Header("Pool")]
        [SerializeField] private int _preload = 40;

        private SimplePool _pool;
        private Transform _poolRoot;

        private bool _running;
        private float _cooldown;

        // cached weapon stats (from equipped weapon)
        private float _damage;
        private float _shotsPerSecond;

        public void SetRunning(bool on)
        {
            _running = on;
            if (!on) _cooldown = 0f;
        }

        public void RebuildFromProfile(PlayerProfile p, GameConfig cfg, string shipId)
        {
            _damage = _fallbackDamage;
            _shotsPerSecond = _fallbackFireRate;

            if (p == null || cfg == null || cfg.ItemCatalog == null || string.IsNullOrWhiteSpace(shipId))
                return;

            var ship = p.GetShip(shipId);
            if (ship == null)
                return;

            if (!ship.TryGet(ItemType.Weapon, 0, out var instanceId))
                return;

            if (string.IsNullOrWhiteSpace(instanceId))
                return;

            if (!p.TryGetItem(instanceId, out var item) || item == null)
                return;

            var dmg = item.Damage;
            var fr = item.FireRate;

            var dmgMul = item.DamageMultiplier;
            var frMul = item.FireRateMultiplier;

            if (dmgMul <= 0f) dmgMul = 1f;
            if (frMul <= 0f) frMul = 1f;

            dmg *= dmgMul;
            fr *= frMul;

            if (dmg > 0.01f) _damage = dmg;
            if (fr > 0.01f) _shotsPerSecond = fr;
        }

        private void Awake()
        {
            if (_projectilePrefab == null)
            {
                Debug.LogError("WeaponShooter: Projectile prefab is not assigned.", this);
                enabled = false;
                return;
            }

            // FIX: пул пуль не должен быть дочерним объектом игрока/шутера,
            // иначе попадания по пуле могут считаться попаданиями по игроку через GetComponentInParent<PlayerShipController2D>().
            _poolRoot = new GameObject("ProjectilePool").transform;
            _poolRoot.SetParent(null, worldPositionStays: false);

            _pool = new SimplePool(_projectilePrefab, _poolRoot, _preload);

            _damage = _fallbackDamage;
            _shotsPerSecond = _fallbackFireRate;
        }

        private void Update()
        {
            if (!_running)
                return;

            _cooldown -= Time.deltaTime;

            var fire = Input.GetButton("Fire1") || Input.GetKey(KeyCode.Space);
            if (!fire)
                return;

            if (_cooldown > 0f)
                return;

            ShootOnce();
            _cooldown = 1f / Mathf.Max(0.1f, _shotsPerSecond);
        }

        private void ShootOnce()
        {
            var pos = transform.position + new Vector3(0f, 0.35f, 0f);
            var go = _pool.Spawn(pos, Quaternion.identity);
            if (go == null) return;

            var pr = go.GetComponent<Projectile>();
            if (pr == null)
            {
                Debug.LogError("WeaponShooter: Projectile component missing on prefab.", this);
                _pool.Despawn(go);
                return;
            }

            pr.Launch(Vector2.up, _damage, _projectileSpeed, _projectileLife, DespawnProjectile);
        }

        private void DespawnProjectile(Projectile p)
        {
            if (p == null) return;
            _pool.Despawn(p.gameObject);
        }
    }
}
