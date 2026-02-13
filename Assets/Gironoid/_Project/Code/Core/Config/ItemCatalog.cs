using System;
using UnityEngine;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Catalogs/ItemCatalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [Header("Starter definitions")]
        public string StarterWeaponDefinitionId = "WPN_START_PULSE";
        public string StarterEngineDefinitionId = "ENG_START_ION";

        [Header("All item definitions")]
        public ItemDefinition[] Items;

        [Header("Salvage rules by type+quality (used for dismantle)")]
        public SalvageRule[] SalvageRules;

        public bool TryGet(string definitionId, out ItemDefinition def)
        {
            def = default;
            if (Items == null || string.IsNullOrEmpty(definitionId)) return false;

            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].Id == definitionId)
                {
                    def = Items[i];
                    return true;
                }
            }
            return false;
        }

        public bool TryGetSalvage(ItemType type, ItemQuality quality, out SalvageGain gain)
        {
            gain = default;
            if (SalvageRules == null) return false;

            for (int i = 0; i < SalvageRules.Length; i++)
            {
                var r = SalvageRules[i];
                if (r.Type == type && r.Quality == quality)
                {
                    gain = r.Gain;
                    return true;
                }
            }
            return false;
        }

        [Serializable]
        public struct ItemDefinition
        {
            public string Id;
            public ItemType Type;

            [Header("UI")]
            public string NameRu;
            public Sprite Icon;

            [Header("Base stats")]
            public float Damage;
            public float FireRate;
            public float EnergyPerShot;

            public float ShieldMaxBonus;
            public float ShieldRegenBonus;

            public float MoveSpeedBonus;
            public float AccelerationBonus;

            public float DamageMultiplier;
            public float FireRateMultiplier;

            public ItemInstance CreateInstance(int level, ItemQuality quality, bool isProtected)
            {
                var it = ItemInstance.Create(Id, Type, level, quality, isProtected);

                it.Damage = Damage;
                it.FireRate = FireRate;
                it.EnergyPerShot = EnergyPerShot;

                it.ShieldMaxBonus = ShieldMaxBonus;
                it.ShieldRegenBonus = ShieldRegenBonus;

                it.MoveSpeedBonus = MoveSpeedBonus;
                it.AccelerationBonus = AccelerationBonus;

                it.DamageMultiplier = (DamageMultiplier <= 0f) ? 1f : DamageMultiplier;
                it.FireRateMultiplier = (FireRateMultiplier <= 0f) ? 1f : FireRateMultiplier;

                it.Normalize();
                return it;
            }
        }

        [Serializable]
        public struct SalvageGain
        {
            public int Tokens;
            public int Iron;
            public int Copper;
            public int Silver;
        }

        [Serializable]
        public struct SalvageRule
        {
            public ItemType Type;
            public ItemQuality Quality;
            public SalvageGain Gain;
        }
    }
}
