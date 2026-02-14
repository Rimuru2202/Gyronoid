using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Config/PlayerWeaponConfig", fileName = "PlayerWeaponConfig")]
    public sealed class PlayerWeaponConfig : ScriptableObject
    {
        [Header("Base weapon stats (used when item stats are unavailable)")]
        [Min(0.01f)] public float BaseDamage = 1f;
        [Min(0.01f)] public float BaseFireRate = 0.2f;

        [Header("Projectile")]
        [Min(0.01f)] public float ProjectileSpeed = 12f;
        [Min(0.05f)] public float ProjectileLifetime = 2f;
        [Min(1)] public int PoolPreload = 40;

        [Header("Global tuning")]
        public bool UseEquippedWeaponItemStats = true;

        [Min(0f)] public float DamageMultiplier = 1f;
        [Min(0f)] public float FireRateMultiplier = 10f;
        [Min(0f)] public float ProjectileSpeedMultiplier = 1f;
        [Min(0f)] public float ProjectileLifetimeMultiplier = 1f;

        [Min(0f)] public float MinDamage = 0.01f;
        [Min(0.01f)] public float MinFireRate = 0.5f;

        [Tooltip("0 = unlimited")]
        [Min(0f)] public float MaxFireRate = 8f;

        public float ResolveDamage(float rawDamage)
        {
            var dmg = Mathf.Max(0f, rawDamage);
            dmg *= Mathf.Max(0f, DamageMultiplier);
            return Mathf.Max(MinDamage, dmg);
        }

        public float ResolveFireRate(float rawFireRate)
        {
            var fireRate = Mathf.Max(0f, rawFireRate);
            fireRate *= Mathf.Max(0f, FireRateMultiplier);
            fireRate = Mathf.Max(MinFireRate, fireRate);

            if (MaxFireRate > 0f)
                fireRate = Mathf.Min(fireRate, MaxFireRate);

            return fireRate;
        }

        public float ResolveProjectileSpeed()
        {
            return Mathf.Max(0.01f, ProjectileSpeed * Mathf.Max(0f, ProjectileSpeedMultiplier));
        }

        public float ResolveProjectileLifetime()
        {
            return Mathf.Max(0.05f, ProjectileLifetime * Mathf.Max(0f, ProjectileLifetimeMultiplier));
        }
    }
}
