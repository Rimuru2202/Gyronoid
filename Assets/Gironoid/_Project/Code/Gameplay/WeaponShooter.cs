// Assets/Gironoid/_Project/Code/Gameplay/WeaponShooter.cs
using UnityEngine;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class WeaponShooter : MonoBehaviour
    {
        private const string DefaultWeaponConfigPath = "Gironoid/PlayerWeaponConfig";

        [Header("Prefabs")]
        [SerializeField] private GameObject _projectilePrefab;

        [Header("Single source of truth (optional override)")]
        [SerializeField] private PlayerWeaponConfig _weaponConfig;

        [Header("Shoot params (legacy fallback)")]
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

        private PlayerWeaponConfig _resolvedConfig;

        // cached runtime stats
        private float _damage;
        private float _shotsPerSecond;
        private float _projectileSpeedRuntime;
        private float _projectileLifeRuntime;

        public void SetRunning(bool on)
        {
            _running = on;
            if (!on) _cooldown = 0f;
        }

        public void RebuildFromProfile(PlayerProfile p, GameConfig cfg, string shipId)
        {
            var weaponCfg = ResolveWeaponConfig(cfg);
            ApplyBaseStats(weaponCfg);

            var canUseEquippedWeapon = weaponCfg == null || weaponCfg.UseEquippedWeaponItemStats;

            if (canUseEquippedWeapon &&
                p != null &&
                cfg != null &&
                cfg.ItemCatalog != null &&
                !string.IsNullOrWhiteSpace(shipId))
            {
                var ship = p.GetShip(shipId);
                if (ship != null &&
                    ship.TryGet(ItemType.Weapon, 0, out var instanceId) &&
                    !string.IsNullOrWhiteSpace(instanceId) &&
                    p.TryGetItem(instanceId, out var item) &&
                    item != null)
                {
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
            }

            ApplyFinalClamps(weaponCfg);
        }

        private void Awake()
        {
            if (_projectilePrefab == null)
            {
                Debug.LogError("WeaponShooter: Projectile prefab is not assigned.", this);
                enabled = false;
                return;
            }

            var weaponCfg = ResolveWeaponConfig(null);
            var preload = weaponCfg != null
                ? Mathf.Max(1, weaponCfg.PoolPreload)
                : Mathf.Max(1, _preload);

            ApplyBaseStats(weaponCfg);
            ApplyFinalClamps(weaponCfg);

            // FIX: пул пуль не должен быть дочерним объектом игрока/шутера,
            // иначе попадания по пуле могут считаться попаданиями по игроку через GetComponentInParent<PlayerShipController2D>().
            _poolRoot = new GameObject("ProjectilePool").transform;
            _poolRoot.SetParent(null, worldPositionStays: false);

            _pool = new SimplePool(_projectilePrefab, _poolRoot, preload);
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

            pr.Launch(Vector2.up, _damage, _projectileSpeedRuntime, _projectileLifeRuntime, DespawnProjectile);
        }

        private void DespawnProjectile(Projectile p)
        {
            if (p == null) return;
            _pool.Despawn(p.gameObject);
        }

        private PlayerWeaponConfig ResolveWeaponConfig(GameConfig cfg)
        {
            if (cfg != null && cfg.PlayerWeapon != null)
            {
                _resolvedConfig = cfg.PlayerWeapon;
                return _resolvedConfig;
            }

            if (_resolvedConfig != null)
                return _resolvedConfig;

            if (_weaponConfig != null)
            {
                _resolvedConfig = _weaponConfig;
                return _resolvedConfig;
            }

            _resolvedConfig = Resources.Load<PlayerWeaponConfig>(DefaultWeaponConfigPath);
            return _resolvedConfig;
        }

        private void ApplyBaseStats(PlayerWeaponConfig weaponCfg)
        {
            if (weaponCfg == null)
            {
                _damage = _fallbackDamage;
                _shotsPerSecond = _fallbackFireRate;
                _projectileSpeedRuntime = _projectileSpeed;
                _projectileLifeRuntime = _projectileLife;
                return;
            }

            _damage = weaponCfg.BaseDamage;
            _shotsPerSecond = weaponCfg.BaseFireRate;
            _projectileSpeedRuntime = weaponCfg.ResolveProjectileSpeed();
            _projectileLifeRuntime = weaponCfg.ResolveProjectileLifetime();
        }

        private void ApplyFinalClamps(PlayerWeaponConfig weaponCfg)
        {
            if (weaponCfg != null)
            {
                _damage = weaponCfg.ResolveDamage(_damage);
                _shotsPerSecond = weaponCfg.ResolveFireRate(_shotsPerSecond);
                _projectileSpeedRuntime = weaponCfg.ResolveProjectileSpeed();
                _projectileLifeRuntime = weaponCfg.ResolveProjectileLifetime();
                return;
            }

            _damage = Mathf.Max(0.01f, _damage);
            _shotsPerSecond = Mathf.Max(0.1f, _shotsPerSecond);
            _projectileSpeedRuntime = Mathf.Max(0.01f, _projectileSpeedRuntime);
            _projectileLifeRuntime = Mathf.Max(0.05f, _projectileLifeRuntime);
        }
    }
}

