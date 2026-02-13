using System;
using System.Reflection;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Gameplay
{
    public static class GameplayActivityResolver
    {
        public enum ActivityType
        {
            Unknown = 0,
            Level = 1,
            Challenge = 2
        }

        public readonly struct ResolvedActivity
        {
            public readonly ActivityType Type;
            public readonly string PlanetId;

            public readonly string ActivityId;
            public readonly string NameRu;

            public readonly int EnergyCost;
            public readonly int TimeLimitSeconds;
            public readonly string ObjectiveRu;

            public ResolvedActivity(
                ActivityType type,
                string planetId,
                string activityId,
                string nameRu,
                int energyCost,
                int timeLimitSeconds,
                string objectiveRu)
            {
                Type = type;
                PlanetId = planetId;
                ActivityId = activityId;
                NameRu = nameRu;
                EnergyCost = energyCost;
                TimeLimitSeconds = timeLimitSeconds;
                ObjectiveRu = objectiveRu;
            }
        }

        public static bool TryResolve(GameConfig cfg, PlayerProfile profile, out ResolvedActivity activity)
        {
            activity = default;

            if (cfg == null || cfg.SystemContent == null || profile == null)
                return false;

            var planetId = string.IsNullOrWhiteSpace(profile.CurrentPlanetId) ? "Earth" : profile.CurrentPlanetId;

            // 1) Пытаемся прочитать выбор активности из профиля (reflection-safe)
            var chosenType = ReadSelectedActivityType(profile, out var chosenId);

            // 2) fallback для туториала: если не нашли выбор — Earth_CH_01
            if (chosenType == ActivityType.Unknown || string.IsNullOrWhiteSpace(chosenId))
            {
                if (string.Equals(planetId, "Earth", StringComparison.OrdinalIgnoreCase))
                {
                    chosenType = ActivityType.Challenge;
                    chosenId = "EARTH_CH_01";
                }
                else
                {
                    // для не-Земли: первый Level, если есть; иначе первый Challenge
                    chosenType = ActivityType.Level;
                    chosenId = null;
                }
            }

            if (!cfg.SystemContent.TryGetPlanet(planetId, out var planet))
                return false;

            // Земля по ТЗ: только Challenges
            if (string.Equals(planetId, "Earth", StringComparison.OrdinalIgnoreCase))
            {
                chosenType = ActivityType.Challenge;
            }

            if (chosenType == ActivityType.Level)
            {
                // Если id пустой — берём первый уровень
                if (string.IsNullOrWhiteSpace(chosenId))
                {
                    if (planet.Levels == null || planet.Levels.Length == 0)
                        return false;

                    var def0 = planet.Levels[0];
                    activity = new ResolvedActivity(
                        ActivityType.Level,
                        planetId,
                        def0.Id,
                        def0.NameRu,
                        def0.EnergyCost,
                        def0.TimeLimitSeconds,
                        def0.ObjectiveRu
                    );
                    return true;
                }

                if (planet.Levels != null)
                {
                    for (int i = 0; i < planet.Levels.Length; i++)
                    {
                        var def = planet.Levels[i];
                        if (def.Id == chosenId)
                        {
                            activity = new ResolvedActivity(
                                ActivityType.Level,
                                planetId,
                                def.Id,
                                def.NameRu,
                                def.EnergyCost,
                                def.TimeLimitSeconds,
                                def.ObjectiveRu
                            );
                            return true;
                        }
                    }
                }

                return false;
            }

            if (chosenType == ActivityType.Challenge)
            {
                // Если id пустой — берём первый челлендж
                if (string.IsNullOrWhiteSpace(chosenId))
                {
                    if (planet.Challenges == null || planet.Challenges.Length == 0)
                        return false;

                    var def0 = planet.Challenges[0];
                    activity = new ResolvedActivity(
                        ActivityType.Challenge,
                        planetId,
                        def0.Id,
                        def0.NameRu,
                        def0.EnergyCost,
                        def0.TimeLimitSeconds,
                        def0.ObjectiveRu
                    );
                    return true;
                }

                if (planet.Challenges != null)
                {
                    for (int i = 0; i < planet.Challenges.Length; i++)
                    {
                        var def = planet.Challenges[i];
                        if (def.Id == chosenId)
                        {
                            activity = new ResolvedActivity(
                                ActivityType.Challenge,
                                planetId,
                                def.Id,
                                def.NameRu,
                                def.EnergyCost,
                                def.TimeLimitSeconds,
                                def.ObjectiveRu
                            );
                            return true;
                        }
                    }
                }

                return false;
            }

            return false;
        }

        private static ActivityType ReadSelectedActivityType(PlayerProfile profile, out string id)
        {
            id = null;
            if (profile == null) return ActivityType.Unknown;

            // Вариант 1: простые поля
            // SelectedActivityType (string) + SelectedActivityId (string)
            var simpleType = ReadStringMember(profile, "SelectedActivityType");
            var simpleId = ReadStringMember(profile, "SelectedActivityId");

            if (!string.IsNullOrWhiteSpace(simpleType) && !string.IsNullOrWhiteSpace(simpleId))
            {
                id = simpleId;
                return ParseType(simpleType);
            }

            // Вариант 2: ActivitySelection объект (Id/Type)
            var selectionObj = ReadObjectMember(profile, "ActivitySelection");
            if (selectionObj != null)
            {
                var selId = ReadStringMember(selectionObj, "Id") ?? ReadStringMember(selectionObj, "ActivityId");
                var selTypeStr = ReadStringMember(selectionObj, "Type") ?? ReadStringMember(selectionObj, "ActivityType");

                if (!string.IsNullOrWhiteSpace(selId))
                    id = selId;

                if (!string.IsNullOrWhiteSpace(selTypeStr))
                    return ParseType(selTypeStr);

                // Если Type — enum
                var enumTypeObj = ReadObjectMember(selectionObj, "Type") ?? ReadObjectMember(selectionObj, "ActivityType");
                if (enumTypeObj != null && enumTypeObj.GetType().IsEnum)
                    return ParseType(enumTypeObj.ToString());
            }

            // Вариант 3: в некоторых проектах могло называться иначе
            var altId = ReadStringMember(profile, "SelectedLevelId");
            if (!string.IsNullOrWhiteSpace(altId))
            {
                id = altId;
                return ActivityType.Level;
            }

            var altCh = ReadStringMember(profile, "SelectedChallengeId");
            if (!string.IsNullOrWhiteSpace(altCh))
            {
                id = altCh;
                return ActivityType.Challenge;
            }

            return ActivityType.Unknown;
        }

        private static ActivityType ParseType(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return ActivityType.Unknown;

            if (string.Equals(s, "Level", StringComparison.OrdinalIgnoreCase))
                return ActivityType.Level;

            if (string.Equals(s, "Challenge", StringComparison.OrdinalIgnoreCase))
                return ActivityType.Challenge;

            // иногда пишут "1/2"
            if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase))
                return ActivityType.Level;

            if (string.Equals(s, "2", StringComparison.OrdinalIgnoreCase))
                return ActivityType.Challenge;

            return ActivityType.Unknown;
        }

        private static string ReadStringMember(object obj, string member)
        {
            var v = ReadObjectMember(obj, member);
            return v as string;
        }

        private static object ReadObjectMember(object obj, string member)
        {
            if (obj == null || string.IsNullOrWhiteSpace(member))
                return null;

            var t = obj.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var p = t.GetProperty(member, flags);
            if (p != null && p.CanRead)
            {
                try { return p.GetValue(obj); }
                catch { return null; }
            }

            var f = t.GetField(member, flags);
            if (f != null)
            {
                try { return f.GetValue(obj); }
                catch { return null; }
            }

            return null;
        }
    }
}
