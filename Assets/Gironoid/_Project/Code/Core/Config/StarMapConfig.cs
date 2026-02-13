using System;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Config/StarMapConfig", fileName = "StarMapConfig")]
    public sealed class StarMapConfig : ScriptableObject
    {
        [Header("Planets")]
        public PlanetDefinition[] Planets = new[]
        {
            new PlanetDefinition { Id = "Earth",  NameRu = "Земля",  HasLevels = false },
            new PlanetDefinition { Id = "Efilon", NameRu = "Эфилон", HasLevels = true  },
        };

        [Header("Routes")]
        public StarRouteDefinition[] Routes = new[]
        {
            new StarRouteDefinition { FromPlanetId = "Earth",  ToPlanetId = "Efilon", TravelEnergyCost = 5 },
            new StarRouteDefinition { FromPlanetId = "Efilon", ToPlanetId = "Earth",  TravelEnergyCost = 5 },
        };

        /// <summary>
        /// Пытается найти планету по Id ИЛИ по NameRu (устойчиво: trim/ignoreCase/обрезает "(...)").
        /// </summary>
        public PlanetDefinition? TryGetPlanet(string idOrNameRu)
        {
            if (!TryResolvePlanetId(idOrNameRu, out var resolvedId)) return null;
            return TryGetPlanetById(resolvedId);
        }

        /// <summary>
        /// Ищет планету строго по Id, но сравнение устойчивое (trim/ignoreCase/обрезает "(...)").
        /// </summary>
        public PlanetDefinition? TryGetPlanetById(string id)
        {
            if (Planets == null || Planets.Length == 0) return null;

            var key = NormalizeKey(id);
            if (string.IsNullOrEmpty(key)) return null;

            for (int i = 0; i < Planets.Length; i++)
            {
                var p = Planets[i];
                if (KeyEquals(key, NormalizeKey(p.Id)))
                    return p;
            }

            return null;
        }

        /// <summary>
        /// Преобразует "что угодно похожее на планету" в реальный Planet.Id:
        /// - принимает Id (Earth/Efilon)
        /// - принимает NameRu ("Земля", "Эфилон")
        /// - обрезает суффиксы вида "(Старт)" и лишние пробелы
        /// - сравнение без учёта регистра
        /// </summary>
        public bool TryResolvePlanetId(string idOrNameRu, out string resolvedId)
        {
            resolvedId = string.Empty;

            // Если fromId пустой — берём стартовую планету: первую в списке (у вас Earth).
            var key = NormalizeKey(idOrNameRu);
            if (string.IsNullOrEmpty(key))
            {
                if (Planets != null && Planets.Length > 0 && !string.IsNullOrWhiteSpace(Planets[0].Id))
                {
                    resolvedId = Planets[0].Id;
                    return true;
                }
                return false;
            }

            // 1) Сначала пробуем как Id.
            if (Planets != null)
            {
                for (int i = 0; i < Planets.Length; i++)
                {
                    var p = Planets[i];
                    if (KeyEquals(key, NormalizeKey(p.Id)))
                    {
                        resolvedId = p.Id;
                        return true;
                    }
                }
            }

            // 2) Потом пробуем как NameRu (например, если UI передал "Земля (Старт)").
            if (Planets != null)
            {
                for (int i = 0; i < Planets.Length; i++)
                {
                    var p = Planets[i];
                    if (KeyEquals(key, NormalizeKey(p.NameRu)))
                    {
                        resolvedId = p.Id;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Возвращает стоимость маршрута. Если маршрут не найден — вернёт false и cost=0.
        /// Устойчиво к пробелам/регистру/суффиксам "(...)" и русским названиям планет.
        /// </summary>
        public bool TryGetRouteCost(string fromIdOrNameRu, string toIdOrNameRu, out int cost)
        {
            cost = 0;

            if (Routes == null || Routes.Length == 0) return false;

            if (!TryResolvePlanetId(fromIdOrNameRu, out var fromId)) return false;
            if (!TryResolvePlanetId(toIdOrNameRu, out var toId)) return false;

            // На текущую планету перелёт всегда 0.
            if (KeyEquals(NormalizeKey(fromId), NormalizeKey(toId)))
            {
                cost = 0;
                return true;
            }

            var fromKey = NormalizeKey(fromId);
            var toKey = NormalizeKey(toId);

            for (int i = 0; i < Routes.Length; i++)
            {
                var r = Routes[i];
                if (KeyEquals(fromKey, NormalizeKey(r.FromPlanetId)) &&
                    KeyEquals(toKey, NormalizeKey(r.ToPlanetId)))
                {
                    cost = r.TravelEnergyCost < 0 ? 0 : r.TravelEnergyCost;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            s = s.Trim();

            // Обрезаем всё после "(" — чтобы "Земля (Старт)" => "Земля"
            var idx = s.IndexOf('(');
            if (idx >= 0)
                s = s.Substring(0, idx).Trim();

            // Убираем двойные пробелы по краям (внутренние не трогаем специально).
            return s;
        }

        private static bool KeyEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public struct PlanetDefinition
    {
        public string Id;
        public string NameRu;
        public bool HasLevels;
    }

    [Serializable]
    public struct StarRouteDefinition
    {
        public string FromPlanetId;
        public string ToPlanetId;

        [Min(0)]
        public int TravelEnergyCost;
    }
}
