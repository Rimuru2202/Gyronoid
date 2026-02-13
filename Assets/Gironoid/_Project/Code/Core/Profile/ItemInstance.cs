using System;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Profile
{
    [Serializable]
    public sealed class ItemInstance
    {
        public string InstanceId;
        public string DefinitionId;

        public ItemType Type;

        [Min(1)] public int Level = 1;
        public ItemQuality Quality = ItemQuality.Common;

        public bool IsProtected; // стартовые предметы нельзя уничтожить/разобрать

        // Базовые статы (можно расширять безопасно — добавлением)
        public float Damage;
        public float FireRate;
        public float EnergyPerShot;

        public float ShieldMaxBonus;
        public float ShieldRegenBonus;

        public float MoveSpeedBonus;
        public float AccelerationBonus;

        public float DamageMultiplier = 1f;
        public float FireRateMultiplier = 1f;

        public static ItemInstance Create(string definitionId, ItemType type, int level, ItemQuality quality, bool isProtected)
        {
            return new ItemInstance
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                DefinitionId = definitionId ?? "",
                Type = type,
                Level = Mathf.Max(1, level),
                Quality = quality,
                IsProtected = isProtected
            };
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(InstanceId))
                InstanceId = Guid.NewGuid().ToString("N");

            if (DefinitionId == null)
                DefinitionId = "";

            if (Level < 1)
                Level = 1;

            if (DamageMultiplier <= 0f)
                DamageMultiplier = 1f;

            if (FireRateMultiplier <= 0f)
                FireRateMultiplier = 1f;
        }
    }
}