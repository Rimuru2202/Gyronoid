#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gironoid._Project.Code.Editor
{
    // Fallback-инспектор: применяется только если у ScriptableObject нет более специфичного CustomEditor.
    [CustomEditor(typeof(ScriptableObject), true, isFallback = true)]
    public sealed class GironoidConfigInspector : UnityEditor.Editor
    {
        private readonly List<string> _drawnRoots = new List<string>(64);

        public override void OnInspectorGUI()
        {
            if (target == null)
            {
                base.OnInspectorGUI();
                return;
            }

            var fullName = target.GetType().FullName ?? string.Empty;

            // Ограничиваемся только вашими конфигами, чтобы не влиять на чужие ассеты.
            if (!fullName.StartsWith("Gironoid._Project.Code.Core.Config.", StringComparison.Ordinal))
            {
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();
            _drawnRoots.Clear();

            DrawScriptField();

            EditorGUILayout.Space(6);

            // Роутинг по имени типа (без compile-time зависимостей).
            if (fullName.EndsWith(".GameConfig", StringComparison.Ordinal))
                DrawGameConfig();
            else if (fullName.EndsWith(".EnergyConfig", StringComparison.Ordinal))
                DrawEnergyConfig();
            else if (fullName.EndsWith(".ItemCatalog", StringComparison.Ordinal))
                DrawItemCatalog();
            else if (fullName.EndsWith(".ShipCatalog", StringComparison.Ordinal))
                DrawShipCatalog();
            else if (fullName.EndsWith(".StarMapConfig", StringComparison.Ordinal))
                DrawStarMapConfig();
            else if (fullName.EndsWith(".SystemContentConfig", StringComparison.Ordinal))
                DrawSystemContentConfig();
            else
            {
                // На случай, если добавятся новые конфиги.
                EditorGUILayout.HelpBox(
                    "Конфиг Gironoid: для этого типа пока нет русифицированного инспектора. Отрисован стандартный.",
                    MessageType.Info);
                DrawDefaultInspectorSafe();
            }

            // Рисуем все остальные свойства, которые не были отрисованы вручную.
            DrawRemainingProperties();

            serializedObject.ApplyModifiedProperties();
        }

        // -------------------- Per-config drawers --------------------

        private void DrawGameConfig()
        {
            EditorGUILayout.LabelField("GameConfig (главный конфиг игры)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Сюда подключаются остальные каталоги/конфиги. Обычно в сцене Boot этот asset грузится первым.",
                MessageType.None);

            DrawPropAny(new[] { "Energy", "EnergyConfig" },
                "Энергия (бак / реген)",
                "Настройки энергии игрока: кап бака по уровню TankLevel и интервал регена по уровню CapacitorLevel.");

            DrawPropAny(new[] { "StarMap", "StarMapConfig" },
                "Звёздная карта (планеты / маршруты)",
                "Список планет и маршрутов между ними. Стоимость перелёта берётся из Routes (TravelEnergyCost).");

            DrawPropAny(new[] { "SystemContent", "SystemContentConfig" },
                "Контент планет (уровни / испытания)",
                "Уровни и испытания для каждой планеты. Если тут пусто — экран уровней покажет LIST IS EMPTY.");

            DrawPropAny(new[] { "PlayerWeapon", "PlayerWeaponConfig" },
                "Оружие игрока (бой)",
                "Единый ScriptableObject для скорострельности, урона и параметров снаряда корабля в бою.");

            DrawPropAny(new[] { "ShipCatalog" },
                "Каталог кораблей",
                "Описание кораблей, стартовый корабль, ранги Mk, слоты, базовые статы, стоимости апгрейдов.");

            DrawPropAny(new[] { "ItemCatalog" },
                "Каталог предметов (оружие/двигатель/щит/модификатор)",
                "Определения предметов и правила разборки (salvage). Стартовые ID берутся из Starter*DefinitionId.");

            EditorGUILayout.Space(6);
        }

        private void DrawEnergyConfig()
        {
            EditorGUILayout.LabelField("EnergyConfig (энергия игрока)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "TankLevel увеличивает максимальную энергию (cap).\n" +
                "CapacitorLevel уменьшает интервал регена (сколько времени на +1 энергию).",
                MessageType.None);

            DrawPropAny(new[] { "DefaultEnergyCap", "StartEnergyCap", "BaseCap" },
                "Базовый кап энергии",
                "Если используются тировые настройки — этот параметр может быть не нужен.");

            DrawPropAny(new[] { "StartEnergy", "DefaultEnergy", "InitialEnergy" },
                "Стартовая энергия",
                "Сколько энергии выдавать новому профилю на старте. Если 0 — игрок стартует с 0/Cap.");

            DrawPropAny(new[] { "TankTiers", "TankCaps", "CapByTankLevel", "EnergyCaps" },
                "Тиры бака (TankLevel → Cap)",
                "Таблица капа энергии по уровню бака. TankLevel обычно начинается с 0.");

            DrawPropAny(new[] { "CapacitorTiers", "RegenIntervals", "RegenIntervalMinutes", "RegenByCapacitorLevel" },
                "Тиры конденсатора (CapacitorLevel → реген)",
                "Интервал регена энергии. Чем меньше интервал — тем быстрее реген.");

            DrawPreviewForEnergyConfig();

            EditorGUILayout.Space(6);
        }

        private void DrawItemCatalog()
        {
            EditorGUILayout.LabelField("ItemCatalog (каталог предметов)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Важно: StarterWeaponDefinitionId/StarterEngineDefinitionId должны существовать в списке Items.\n" +
                "Если ID не найден — стартовый кит не создастся, и инвентарь будет пустой (LIST IS EMPTY).",
                MessageType.None);

            var starterWpn = DrawPropAny(new[] { "StarterWeaponDefinitionId" },
                "Стартовое оружие (DefinitionId)",
                "ID определения оружия, которое выдаётся игроку автоматически (как защищённый стартовый предмет).");

            var starterEng = DrawPropAny(new[] { "StarterEngineDefinitionId" },
                "Стартовый двигатель (DefinitionId)",
                "ID определения двигателя, которое выдаётся игроку автоматически (как защищённый стартовый предмет).");

            var itemsProp = FindPropAny(new[] { "Items", "Definitions", "Catalog", "AllItems" });
            if (itemsProp != null)
            {
                EditorGUILayout.Space(4);
                DrawProp(itemsProp,
                    "Предметы каталога",
                    "Список всех определений предметов. У каждого должен быть уникальный Id.");
            }
            else
            {
                EditorGUILayout.HelpBox("Не найдено поле Items/Definitions в этом ItemCatalog. Отрисован стандартный инспектор ниже.", MessageType.Warning);
            }

            // Валидация Starter ID
            ValidateStarterId(itemsProp, starterWpn, "StarterWeaponDefinitionId");
            ValidateStarterId(itemsProp, starterEng, "StarterEngineDefinitionId");

            // Salvage
            DrawPropAny(new[] { "SalvageRules", "Salvage", "SalvageTable" },
                "Правила разборки (salvage)",
                "Что получает игрок при разборке предметов, в зависимости от типа и качества.");

            EditorGUILayout.Space(6);
        }

        private void DrawShipCatalog()
        {
            EditorGUILayout.LabelField("ShipCatalog (каталог кораблей)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "StarterShipId — какой корабль гарантированно есть у игрока.\n" +
                "Ships — список определений кораблей (Id, слоты, Mk-ранги, статы, стоимости апгрейдов).",
                MessageType.None);

            DrawPropAny(new[] { "StarterShipId" },
                "Стартовый корабль (ShipId)",
                "ID корабля, который выдаётся игроку автоматически при первом запуске/нормализации профиля.");

            DrawPropAny(new[] { "Ships", "Items", "Definitions" },
                "Корабли",
                "Список всех кораблей. У каждого должен быть уникальный Id.");

            EditorGUILayout.Space(6);
        }

        private void DrawStarMapConfig()
        {
            EditorGUILayout.LabelField("StarMapConfig (звёздная карта)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Planets — список планет (Id, имя, доступность).\n" +
                "Routes — маршруты между планетами (From/To + TravelEnergyCost).\n" +
                "Если перелёт стоит 0 — обычно не найден route по Id (проверьте совпадение строк From/To с Id планет).",
                MessageType.None);

            var planetsProp = FindPropAny(new[] { "Planets", "Systems" });
            if (planetsProp != null)
            {
                DrawProp(planetsProp,
                    "Планеты",
                    "Список планет. Поле Id должно совпадать с тем, что вы используете в профиле (CurrentPlanetId).");
            }

            var routesProp = FindPropAny(new[] { "Routes", "Links", "Edges" });
            if (routesProp != null)
            {
                DrawProp(routesProp,
                    "Маршруты (перелёты)",
                    "Каждый маршрут: FromPlanetId/ToPlanetId и стоимость TravelEnergyCost.");
            }

            ValidateStarMap(planetsProp, routesProp);

            EditorGUILayout.Space(6);
        }

        private void DrawSystemContentConfig()
        {
            EditorGUILayout.LabelField("SystemContentConfig (контент планет)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Здесь хранится список уровней и испытаний для каждой планеты.\n" +
                "Если PlanetId пустой или Levels/Challenges пустые — экран уровней покажет LIST IS EMPTY.",
                MessageType.None);

            var planetsProp = FindPropAny(new[] { "Planets", "PlanetContents", "Content", "Definitions" });
            if (planetsProp != null)
            {
                DrawProp(planetsProp,
                    "Планеты (контент)",
                    "Для каждой планеты: PlanetId (обязательно), Levels, Challenges.");
                ValidateSystemContent(planetsProp);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Не найден список планет/контента (Planets/PlanetContents). Отрисован стандартный инспектор ниже.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(6);
        }

        // -------------------- Validation / Preview --------------------

        private void ValidateStarterId(SerializedProperty itemsProp, SerializedProperty starterProp, string starterName)
        {
            if (starterProp == null) return;

            var id = starterProp.stringValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                EditorGUILayout.HelpBox($"{starterName}: пустой ID. Стартовый предмет не будет выдан.", MessageType.Warning);
                return;
            }

            if (itemsProp == null || !itemsProp.isArray)
                return;

            if (!ContainsItemDefinitionId(itemsProp, id))
            {
                EditorGUILayout.HelpBox(
                    $"{starterName}: '{id}' не найден в Items.\n" +
                    "Итог: стартовый кит не создастся, инвентарь будет пустым.",
                    MessageType.Error);
            }
        }

        private void ValidateStarMap(SerializedProperty planetsProp, SerializedProperty routesProp)
        {
            // Мягкая валидация: не ломаем инспектор, просто предупреждаем.
            if (planetsProp != null && planetsProp.isArray && planetsProp.arraySize == 0)
                EditorGUILayout.HelpBox("Planets: список пуст. На карте нечего выбирать.", MessageType.Warning);

            if (routesProp != null && routesProp.isArray && routesProp.arraySize == 0)
                EditorGUILayout.HelpBox("Routes: список пуст. Перелёты невозможны.", MessageType.Warning);

            // Проверка на пустые Id у планет
            if (planetsProp != null && planetsProp.isArray)
            {
                for (int i = 0; i < planetsProp.arraySize; i++)
                {
                    var el = planetsProp.GetArrayElementAtIndex(i);
                    var idProp = el.FindPropertyRelative("Id") ?? el.FindPropertyRelative("PlanetId") ?? el.FindPropertyRelative("SystemId");
                    if (idProp != null && string.IsNullOrWhiteSpace(idProp.stringValue))
                    {
                        EditorGUILayout.HelpBox($"Planets[{i}]: пустой Id. Такая планета будет плохо резолвиться по профилю.", MessageType.Warning);
                        break;
                    }
                }
            }

            // Проверка маршрутов: from/to пустые или совпадают
            if (routesProp != null && routesProp.isArray)
            {
                for (int i = 0; i < routesProp.arraySize; i++)
                {
                    var r = routesProp.GetArrayElementAtIndex(i);
                    var from = r.FindPropertyRelative("FromPlanetId")
                               ?? r.FindPropertyRelative("FromSystemId")
                               ?? r.FindPropertyRelative("From");
                    var to = r.FindPropertyRelative("ToPlanetId")
                             ?? r.FindPropertyRelative("ToSystemId")
                             ?? r.FindPropertyRelative("To");

                    var cost = r.FindPropertyRelative("TravelEnergyCost") ?? r.FindPropertyRelative("Cost");

                    var fromV = from != null ? (from.stringValue ?? "") : "";
                    var toV = to != null ? (to.stringValue ?? "") : "";

                    if (string.IsNullOrWhiteSpace(fromV) || string.IsNullOrWhiteSpace(toV))
                    {
                        EditorGUILayout.HelpBox($"Routes[{i}]: From/To пустые. Такой маршрут никогда не будет найден.", MessageType.Warning);
                        break;
                    }

                    if (string.Equals(fromV, toV, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorGUILayout.HelpBox($"Routes[{i}]: From == To ('{fromV}'). Стоимость перелёта будет выглядеть странно.", MessageType.Info);
                        break;
                    }

                    if (cost != null && cost.propertyType == SerializedPropertyType.Integer && cost.intValue < 0)
                    {
                        EditorGUILayout.HelpBox($"Routes[{i}]: TravelEnergyCost < 0. Это почти всегда ошибка данных.", MessageType.Warning);
                        break;
                    }
                }
            }
        }

        private void ValidateSystemContent(SerializedProperty planetsProp)
        {
            if (planetsProp == null || !planetsProp.isArray) return;

            if (planetsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Контент планет пуст. Экран уровней будет пустым (LIST IS EMPTY).", MessageType.Warning);
                return;
            }

            for (int i = 0; i < planetsProp.arraySize; i++)
            {
                var p = planetsProp.GetArrayElementAtIndex(i);

                var planetId = p.FindPropertyRelative("PlanetId") ?? p.FindPropertyRelative("Id") ?? p.FindPropertyRelative("SystemId");
                var levels = p.FindPropertyRelative("Levels");
                var challenges = p.FindPropertyRelative("Challenges");

                if (planetId != null && string.IsNullOrWhiteSpace(planetId.stringValue))
                {
                    EditorGUILayout.HelpBox($"SystemContent.Planets[{i}]: PlanetId пуст. Такой контент не будет найден по текущей планете.", MessageType.Error);
                    break;
                }

                if (levels != null && levels.isArray && levels.arraySize == 0 &&
                    challenges != null && challenges.isArray && challenges.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(
                        $"SystemContent.Planets[{i}]: Levels и Challenges пустые. На этой планете нечего запускать.",
                        MessageType.Warning);
                    break;
                }
            }
        }

        private void DrawPreviewForEnergyConfig()
        {
            // Превью пытаемся получить через методы, если они есть (GetEnergyCap / GetRegenIntervalMs).
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Превью (для проверки данных)", EditorStyles.boldLabel);

            var cfgObj = target;
            if (cfgObj == null) return;

            int cap0 = TryCallInt(cfgObj, "GetEnergyCap", 0, out var cap0Ok);
            long int0 = TryCallLong(cfgObj, "GetRegenIntervalMs", 0, out var int0Ok);

            if (!cap0Ok && !int0Ok)
            {
                EditorGUILayout.HelpBox("Не найдено методов GetEnergyCap(int) / GetRegenIntervalMs(int). Превью скрыто.", MessageType.Info);
                return;
            }

            string capText = cap0Ok ? cap0.ToString() : "—";
            string regText = int0Ok ? FormatInterval(int0) : "—";

            EditorGUILayout.HelpBox(
                $"TankLevel=0 → Cap: {capText}\n" +
                $"CapacitorLevel=0 → Интервал регена: {regText}",
                MessageType.None);
        }

        private static string FormatInterval(long ms)
        {
            if (ms <= 0) return "—";
            var sec = ms / 1000.0;
            if (sec < 60) return $"{sec:0.#} сек";
            var min = sec / 60.0;
            return $"{min:0.#} мин";
        }

        private static int TryCallInt(object obj, string method, int arg0, out bool ok)
        {
            ok = false;
            try
            {
                var mi = obj.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi == null) return 0;
                var v = mi.Invoke(obj, new object[] { arg0 });
                if (v is int i) { ok = true; return i; }
                if (v is long l) { ok = true; return (int)l; }
            }
            catch { }
            return 0;
        }

        private static long TryCallLong(object obj, string method, int arg0, out bool ok)
        {
            ok = false;
            try
            {
                var mi = obj.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi == null) return 0;
                var v = mi.Invoke(obj, new object[] { arg0 });
                if (v is long l) { ok = true; return l; }
                if (v is int i) { ok = true; return i; }
            }
            catch { }
            return 0;
        }

        private static bool ContainsItemDefinitionId(SerializedProperty itemsArray, string id)
        {
            if (itemsArray == null || !itemsArray.isArray) return false;
            if (string.IsNullOrEmpty(id)) return false;

            for (int i = 0; i < itemsArray.arraySize; i++)
            {
                var el = itemsArray.GetArrayElementAtIndex(i);
                if (el == null) continue;

                var idProp = el.FindPropertyRelative("Id")
                          ?? el.FindPropertyRelative("DefinitionId")
                          ?? el.FindPropertyRelative("ItemId");

                if (idProp != null && string.Equals(idProp.stringValue, id, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // -------------------- Property drawing helpers --------------------

        private void DrawScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                var script = MonoScript.FromScriptableObject(target as ScriptableObject);
                EditorGUILayout.ObjectField(new GUIContent("Script", "Скрипт, который описывает этот asset"), script, typeof(MonoScript), false);
            }
        }

        private SerializedProperty DrawPropAny(string[] names, string label, string tooltip)
        {
            var p = FindPropAny(names);
            if (p == null) return null;
            DrawProp(p, label, tooltip);
            return p;
        }

        private SerializedProperty FindPropAny(string[] names)
        {
            if (names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                var p = serializedObject.FindProperty(names[i]);
                if (p != null) return p;
            }
            return null;
        }

        private void DrawProp(SerializedProperty p, string label, string tooltip)
        {
            if (p == null) return;
            _drawnRoots.Add(p.propertyPath);
            EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), includeChildren: true);
            EditorGUILayout.Space(2);
        }

        private void DrawDefaultInspectorSafe()
        {
            // На случай, если Unity в этой версии не любит вызов DrawDefaultInspector из fallback-редактора.
            var it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.name == "m_Script") continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }

        private void DrawRemainingProperties()
        {
            var it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (it.name == "m_Script")
                    continue;

                var path = it.propertyPath;
                if (IsCovered(path))
                    continue;

                EditorGUILayout.PropertyField(it, true);
            }
        }

        private bool IsCovered(string path)
        {
            for (int i = 0; i < _drawnRoots.Count; i++)
            {
                var root = _drawnRoots[i];
                if (string.IsNullOrEmpty(root)) continue;

                if (path == root) return true;

                // Дочерние свойства / элементы массивов
                if (path.StartsWith(root + ".", StringComparison.Ordinal)) return true;
                if (path.StartsWith(root + "[", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
#endif
