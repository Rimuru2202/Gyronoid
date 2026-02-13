using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Catalogs/ShipCatalog", fileName = "ShipCatalog")]
    public sealed class ShipCatalog : ScriptableObject
    {
        [Header("Starter")]
        public string StarterShipId = "AEGIS";

        [Header("Ships (7 total in MVP)")]
        public ShipDefinition[] Ships;

        public bool TryGet(string shipId, out ShipDefinition def)
        {
            def = default;
            if (Ships == null || string.IsNullOrEmpty(shipId)) return false;

            for (int i = 0; i < Ships.Length; i++)
            {
                if (Ships[i].Id == shipId)
                {
                    def = Ships[i];
                    return true;
                }
            }
            return false;
        }

        public bool TryGetTier(string shipId, int mk, out ShipTier tier)
        {
            tier = default;
            if (!TryGet(shipId, out var def)) return false;
            return def.TryGetTier(mk, out tier);
        }

        public static ShipCatalog CreateDefaultRuntime()
        {
            var c = CreateInstance<ShipCatalog>();
            c.StarterShipId = "AEGIS";

            c.Ships = new[]
            {
                MakeShip_Aegis(),
                MakeShip_Astra(),
                MakeShip_Helix(),
                MakeShip_NovaFrame(),
                MakeShip_IonLancer(),
                MakeShip_Railwing(),
                MakeShip_CoreRunner(),
            };

            return c;
        }

        private static ShipDefinition MakeShip_Aegis()
        {
            return new ShipDefinition
            {
                Id = "AEGIS",
                NameRu = "Aegis",
                UnlockPlayerLevel = 1,
                PriceTokens = 750,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(120, 0.85f, 6.0f, 9.5f, 1.00f, 1.25f), Slots=Slots(1,1,1,1), Upgrade=Cost(0, 0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(145, 0.88f, 6.6f, 10.0f, 1.05f, 1.35f), Slots=Slots(1,2,1,1), Upgrade=Cost(120, 30,10,0) },
                    new ShipTier { Mk=3, Stats=ShipStats(170, 0.90f, 7.2f, 10.6f, 1.10f, 1.45f), Slots=Slots(2,2,1,2), Upgrade=Cost(220, 55,25,5) },
                    new ShipTier { Mk=4, Stats=ShipStats(200, 0.92f, 7.8f, 11.2f, 1.15f, 1.60f), Slots=Slots(2,3,2,2), Upgrade=Cost(360, 85,40,15) },
                }
            };
        }

        private static ShipDefinition MakeShip_Astra()
        {
            return new ShipDefinition
            {
                Id = "ASTRA",
                NameRu = "Astra",
                UnlockPlayerLevel = 3,
                PriceTokens = 1110,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(105, 0.95f, 6.8f, 10.2f, 1.05f, 1.05f), Slots=Slots(1,1,1,1), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(120, 0.98f, 7.4f, 10.8f, 1.12f, 1.10f), Slots=Slots(2,1,1,1), Upgrade=Cost(140, 35,12,0) },
                    new ShipTier { Mk=3, Stats=ShipStats(135, 1.00f, 8.0f, 11.4f, 1.18f, 1.15f), Slots=Slots(2,2,1,2), Upgrade=Cost(250, 60,25,8) },
                    new ShipTier { Mk=4, Stats=ShipStats(150, 1.02f, 8.6f, 12.0f, 1.25f, 1.20f), Slots=Slots(3,2,2,2), Upgrade=Cost(420, 95,45,18) },
                }
            };
        }

        private static ShipDefinition MakeShip_Helix()
        {
            return new ShipDefinition
            {
                Id = "HELIX",
                NameRu = "Helix",
                UnlockPlayerLevel = 6,
                PriceTokens = 1470,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(90, 1.10f, 7.8f, 12.0f, 1.00f, 0.95f), Slots=Slots(1,0,1,1), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(100, 1.15f, 8.6f, 12.8f, 1.05f, 1.00f), Slots=Slots(1,1,1,1), Upgrade=Cost(160, 35,15,0) },
                    new ShipTier { Mk=3, Stats=ShipStats(112, 1.20f, 9.4f, 13.6f, 1.10f, 1.05f), Slots=Slots(2,1,2,1), Upgrade=Cost(280, 65,28,10) },
                    new ShipTier { Mk=4, Stats=ShipStats(125, 1.25f, 10.2f, 14.4f, 1.15f, 1.10f), Slots=Slots(2,2,2,2), Upgrade=Cost(450, 100,50,20) },
                }
            };
        }

        private static ShipDefinition MakeShip_NovaFrame()
        {
            return new ShipDefinition
            {
                Id = "NOVAFRAME",
                NameRu = "NovaFrame",
                UnlockPlayerLevel = 9,
                PriceTokens = 1830,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(140, 0.80f, 5.8f, 9.2f, 1.05f, 1.30f), Slots=Slots(1,2,1,0), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(165, 0.82f, 6.2f, 9.6f, 1.12f, 1.40f), Slots=Slots(2,2,1,1), Upgrade=Cost(200, 55,20,5) },
                    new ShipTier { Mk=3, Stats=ShipStats(190, 0.84f, 6.6f, 10.0f, 1.20f, 1.55f), Slots=Slots(2,3,1,2), Upgrade=Cost(340, 85,35,15) },
                    new ShipTier { Mk=4, Stats=ShipStats(220, 0.86f, 7.0f, 10.4f, 1.28f, 1.70f), Slots=Slots(3,3,2,2), Upgrade=Cost(520, 120,60,25) },
                }
            };
        }

        private static ShipDefinition MakeShip_IonLancer()
        {
            return new ShipDefinition
            {
                Id = "IONLANCER",
                NameRu = "Ion Lancer",
                UnlockPlayerLevel = 12,
                PriceTokens = 2190,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(95, 0.95f, 6.8f, 10.6f, 1.25f, 0.95f), Slots=Slots(2,0,1,1), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(110, 0.98f, 7.4f, 11.2f, 1.35f, 1.00f), Slots=Slots(2,1,1,1), Upgrade=Cost(240, 60,25,8) },
                    new ShipTier { Mk=3, Stats=ShipStats(125, 1.00f, 8.0f, 11.8f, 1.45f, 1.05f), Slots=Slots(3,1,1,2), Upgrade=Cost(400, 95,45,18) },
                    new ShipTier { Mk=4, Stats=ShipStats(140, 1.02f, 8.6f, 12.4f, 1.60f, 1.10f), Slots=Slots(4,2,2,2), Upgrade=Cost(620, 140,70,30) },
                }
            };
        }

        private static ShipDefinition MakeShip_Railwing()
        {
            return new ShipDefinition
            {
                Id = "RAILWING",
                NameRu = "Railwing",
                UnlockPlayerLevel = 15,
                PriceTokens = 2550,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(85, 1.05f, 7.6f, 13.0f, 1.10f, 0.90f), Slots=Slots(1,0,2,1), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(95, 1.10f, 8.2f, 13.8f, 1.18f, 0.95f), Slots=Slots(2,0,2,1), Upgrade=Cost(260, 60,28,10) },
                    new ShipTier { Mk=3, Stats=ShipStats(108, 1.15f, 8.8f, 14.6f, 1.25f, 1.00f), Slots=Slots(2,1,3,2), Upgrade=Cost(430, 100,50,20) },
                    new ShipTier { Mk=4, Stats=ShipStats(122, 1.20f, 9.4f, 15.4f, 1.32f, 1.05f), Slots=Slots(3,1,4,2), Upgrade=Cost(660, 150,75,35) },
                }
            };
        }

        private static ShipDefinition MakeShip_CoreRunner()
        {
            return new ShipDefinition
            {
                Id = "CORERUNNER",
                NameRu = "CoreRunner",
                UnlockPlayerLevel = 18,
                PriceTokens = 2910,
                Tiers = new[]
                {
                    new ShipTier { Mk=1, Stats=ShipStats(100, 1.00f, 7.0f, 11.0f, 1.00f, 1.00f), Slots=Slots(1,1,1,2), Upgrade=Cost(0,0,0,0) },
                    new ShipTier { Mk=2, Stats=ShipStats(115, 1.02f, 7.6f, 11.6f, 1.05f, 1.05f), Slots=Slots(1,2,1,2), Upgrade=Cost(300, 70,30,12) },
                    new ShipTier { Mk=3, Stats=ShipStats(130, 1.04f, 8.2f, 12.2f, 1.10f, 1.10f), Slots=Slots(2,2,2,3), Upgrade=Cost(480, 110,55,25) },
                    new ShipTier { Mk=4, Stats=ShipStats(150, 1.06f, 8.8f, 12.8f, 1.15f, 1.15f), Slots=Slots(2,3,2,4), Upgrade=Cost(720, 160,80,40) },
                }
            };
        }

        private static ShipStats ShipStats(int hp, float maneuver, float accel, float speed, float attack, float defense)
        {
            return new ShipStats
            {
                Hull = hp,
                Maneuver = maneuver,
                Acceleration = accel,
                Speed = speed,
                Attack = attack,
                Defense = defense
            };
        }

        private static ShipSlots Slots(int weapon, int shield, int engine, int mod)
        {
            return new ShipSlots
            {
                WeaponSlots = Mathf.Clamp(weapon, 0, 4),
                ShieldSlots = Mathf.Clamp(shield, 0, 4),
                EngineSlots = Mathf.Clamp(engine, 0, 4),
                ModSlots = Mathf.Clamp(mod, 0, 4),
            };
        }

        private static ShipUpgradeCost Cost(int tokens, int iron, int copper, int silver)
        {
            return new ShipUpgradeCost
            {
                Tokens = Mathf.Max(0, tokens),
                Iron = Mathf.Max(0, iron),
                Copper = Mathf.Max(0, copper),
                Silver = Mathf.Max(0, silver),
            };
        }
    }

    [System.Serializable]
    public struct ShipDefinition
    {
        public string Id;
        public string NameRu;

        [Min(1)] public int UnlockPlayerLevel;

        [Header("Price to buy ship (tokens). If 0 -> UI will auto-calc safe price.")]
        public int PriceTokens;

        [Header("Mk tiers (1..4)")]
        public ShipTier[] Tiers;

        public bool TryGetTier(int mk, out ShipTier tier)
        {
            tier = default;
            if (Tiers == null) return false;

            mk = Mathf.Clamp(mk, 1, 4);

            for (int i = 0; i < Tiers.Length; i++)
            {
                if (Tiers[i].Mk == mk)
                {
                    tier = Tiers[i];
                    return true;
                }
            }

            // fallback: first tier
            if (Tiers.Length > 0)
            {
                tier = Tiers[0];
                return true;
            }

            return false;
        }
    }

    [System.Serializable]
    public struct ShipTier
    {
        [Range(1, 4)] public int Mk;

        public ShipStats Stats;
        public ShipSlots Slots;

        [Header("Cost to upgrade FROM previous Mk to this Mk")]
        public ShipUpgradeCost Upgrade;
    }

    [System.Serializable]
    public struct ShipStats
    {
        public int Hull;

        [Tooltip("Turn responsiveness multiplier (future)")]
        public float Maneuver;

        public float Acceleration;
        public float Speed;

        [Tooltip("Damage multiplier base (future)")]
        public float Attack;

        [Tooltip("Damage reduction base (future)")]
        public float Defense;
    }

    [System.Serializable]
    public struct ShipSlots
    {
        [Range(0, 4)] public int WeaponSlots;
        [Range(0, 4)] public int ShieldSlots;
        [Range(0, 4)] public int EngineSlots;
        [Range(0, 4)] public int ModSlots;
    }

    [System.Serializable]
    public struct ShipUpgradeCost
    {
        public int Tokens;
        public int Iron;
        public int Copper;
        public int Silver;
    }
}
