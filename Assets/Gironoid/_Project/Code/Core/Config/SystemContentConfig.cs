using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Config/SystemContentConfig", fileName = "SystemContentConfig")]
    public sealed class SystemContentConfig : ScriptableObject
    {
        [Header("Per-planet content")]
        public PlanetContentDefinition[] Planets;

        public bool TryGetPlanet(string planetId, out PlanetContentDefinition planet)
        {
            planet = default;
            if (Planets == null || string.IsNullOrWhiteSpace(planetId))
                return false;

            var wanted = planetId.Trim();

            for (int i = 0; i < Planets.Length; i++)
            {
                var id = Planets[i].PlanetId;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (string.Equals(id.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    planet = Planets[i];
                    return true;
                }
            }

            return false;
        }

        public static SystemContentConfig CreateDefaultRuntime()
        {
            var cfg = CreateInstance<SystemContentConfig>();

            cfg.Planets = new[]
            {
                new PlanetContentDefinition
                {
                    PlanetId = "Earth",
                    Levels = System.Array.Empty<LevelDefinition>(),
                    Challenges = new[]
                    {
                        new ChallengeDefinition
                        {
                            Id = "EARTH_CH_01",
                            NameRu = "Защита планеты I",
                            UnlockPlayerLevel = 1,
                            EnergyCost = 3,
                            TimeLimitSeconds = 60,
                            ObjectiveRu = "Выживи 60 секунд. Уничтожь 30 метеоритов.",
                        },
                        new ChallengeDefinition
                        {
                            Id = "EARTH_CH_02",
                            NameRu = "Защита планеты II",
                            UnlockPlayerLevel = 5,
                            EnergyCost = 5,
                            TimeLimitSeconds = 75,
                            ObjectiveRu = "Выживи 75 секунд. Уничтожь 45 метеоритов.",
                        },
                        new ChallengeDefinition
                        {
                            Id = "EARTH_CH_03",
                            NameRu = "Защита планеты III",
                            UnlockPlayerLevel = 10,
                            EnergyCost = 7,
                            TimeLimitSeconds = 90,
                            ObjectiveRu = "Выживи 90 секунд. Уничтожь 60 метеоритов.",
                        },
                    }
                },

                new PlanetContentDefinition
                {
                    PlanetId = "Efilon",
                    Levels = new[]
                    {
                        new LevelDefinition
                        {
                            Id = "EFILON_LV_01",
                            NameRu = "Эфилон: Уровень 1",
                            EnergyCost = 4,
                            TimeLimitSeconds = 60,
                            ObjectiveRu = "Уничтожь 30 метеоритов.",
                        },
                        new LevelDefinition
                        {
                            Id = "EFILON_LV_02",
                            NameRu = "Эфилон: Уровень 2",
                            EnergyCost = 5,
                            TimeLimitSeconds = 60,
                            ObjectiveRu = "Уничтожь 45 метеоритов.",
                        },
                        new LevelDefinition
                        {
                            Id = "EFILON_LV_03",
                            NameRu = "Эфилон: Уровень 3",
                            EnergyCost = 6,
                            TimeLimitSeconds = 75,
                            ObjectiveRu = "Уничтожь 60 метеоритов.",
                        },
                    },
                    Challenges = System.Array.Empty<ChallengeDefinition>()
                }
            };

            return cfg;
        }
    }

    [System.Serializable]
    public struct PlanetContentDefinition
    {
        public string PlanetId;

        [Header("Level chain (open in order)")]
        public LevelDefinition[] Levels;

        [Header("Timed challenges (unlock by PlayerLevel)")]
        public ChallengeDefinition[] Challenges;
    }

    [System.Serializable]
    public struct LevelDefinition
    {
        public string Id;
        public string NameRu;

        [Min(0)] public int EnergyCost;

        [Tooltip("0 = no timer")]
        [Min(0)] public int TimeLimitSeconds;

        [TextArea] public string ObjectiveRu;
    }

    [System.Serializable]
    public struct ChallengeDefinition
    {
        public string Id;
        public string NameRu;

        [Min(1)] public int UnlockPlayerLevel;
        [Min(0)] public int EnergyCost;

        [Min(0)] public int TimeLimitSeconds;

        [TextArea] public string ObjectiveRu;
    }
}
