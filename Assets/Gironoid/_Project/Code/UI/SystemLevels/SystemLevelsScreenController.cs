// Assets/Gironoid/_Project/Code/UI/SystemLevels/SystemLevelsScreenController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;
using Gironoid._Project.Code.UI.Shared;

namespace Gironoid._Project.Code.UI.SystemLevels
{
    [DisallowMultipleComponent]
    public sealed class SystemLevelsScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument _ui;

        private VisualElement _root;
        private TutorialOverlay _tutorial;

        private Button _btnBack;
        private Button _btnStart;

        private Label _lblTitle;
        private Label _lblPlanet;
        private Label _lblEnergy;
        private Label _lblPlayerLevel;
        private Label _lblDetails;

        private ListView _levelsList;
        private ListView _challengesList;

        private readonly List<ActivityVm> _levels = new List<ActivityVm>(capacity: 32);
        private readonly List<ActivityVm> _challenges = new List<ActivityVm>(capacity: 32);

        private int _selectedLevelIndex = -1;
        private int _selectedChallengeIndex = -1;

        private float _nextUiRefreshAt;
        private const float UiRefreshInterval = 0.25f;

        private void Reset()
        {
            _ui = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = GetRootSafe();
            if (_root == null)
                return;

            CacheUi(_root);
            CreateTutorialOverlay(_root);

            UnwireUi();
            WireUi();

            _nextUiRefreshAt = 0f;
            RefreshAll(force: true);
            UpdateTutorialOverlay();
        }

        private void OnDisable()
        {
            UnwireUi();

            if (_tutorial != null)
            {
                _tutorial.Dispose();
                _tutorial = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUiRefreshAt)
                return;

            _nextUiRefreshAt = Time.unscaledTime + UiRefreshInterval;
            RefreshAll(force: false);
            UpdateTutorialOverlay();
        }

        // ---------------- UI ----------------

        private VisualElement GetRootSafe()
        {
            if (_ui == null)
                _ui = GetComponent<UIDocument>();

            if (_ui == null)
            {
                Debug.LogError($"{nameof(SystemLevelsScreenController)}: UIDocument is missing.");
                return null;
            }

            return _ui.rootVisualElement;
        }

        private void CreateTutorialOverlay(VisualElement root)
        {
            var layer = root.Q<VisualElement>("tutorialLayer");
            _tutorial = new TutorialOverlay(root, layer);
        }

        private void CacheUi(VisualElement root)
        {
            _btnBack = root.Q<Button>("btnBack");
            _btnStart = root.Q<Button>("btnStart");

            _lblTitle = root.Q<Label>("lblTitle") ?? root.Q<Label>("title");
            _lblPlanet = root.Q<Label>("lblPlanet") ?? root.Q<Label>("planet");
            _lblEnergy = root.Q<Label>("lblEnergy") ?? root.Q<Label>("energy");
            _lblPlayerLevel = root.Q<Label>("lblPlayerLevel") ?? root.Q<Label>("playerLevel");
            _lblDetails = root.Q<Label>("lblDetails") ?? root.Q<Label>("details");

            _levelsList = root.Q<ListView>("levelsList") ?? root.Q<ListView>("levels");
            _challengesList = root.Q<ListView>("challengesList") ?? root.Q<ListView>("challenges");
        }

        private void WireUi()
        {
            if (_btnBack != null) _btnBack.clicked += OnBackClicked;
            if (_btnStart != null) _btnStart.clicked += OnStartClicked;

            SetupList(_levelsList, _levels, isLevels: true);
            SetupList(_challengesList, _challenges, isLevels: false);
        }

        private void UnwireUi()
        {
            if (_btnBack != null) _btnBack.clicked -= OnBackClicked;
            if (_btnStart != null) _btnStart.clicked -= OnStartClicked;

            if (_levelsList != null) _levelsList.selectionChanged -= OnLevelsSelectionChanged;
            if (_challengesList != null) _challengesList.selectionChanged -= OnChallengesSelectionChanged;
        }

        private void SetupList(ListView lv, List<ActivityVm> data, bool isLevels)
        {
            if (lv == null)
                return;

            lv.selectionType = SelectionType.Single;
            lv.itemsSource = data;

            lv.makeItem = () =>
            {
                var label = new Label();
                label.AddToClassList("list-item");
                label.style.whiteSpace = WhiteSpace.Normal;
                return label;
            };

            lv.bindItem = (ve, idx) =>
            {
                var label = (Label)ve;
                if ((uint)idx >= (uint)data.Count)
                {
                    label.text = string.Empty;
                    return;
                }

                var vm = data[idx];

                // префиксы статуса
                string status;
                if (vm.IsLocked) status = "🔒 ";
                else if (vm.IsCompleted) status = "✔ ";
                else status = "• ";

                if (vm.RequiredPlayerLevel > 0)
                    label.text = $"{status}{vm.Title} (энергия {vm.EnergyCost}) [треб. ур. {vm.RequiredPlayerLevel}]";
                else
                    label.text = $"{status}{vm.Title} (энергия {vm.EnergyCost})";
            };

            // важно: не накапливать подписки при re-enable
            if (isLevels)
            {
                lv.selectionChanged -= OnLevelsSelectionChanged;
                lv.selectionChanged += OnLevelsSelectionChanged;
            }
            else
            {
                lv.selectionChanged -= OnChallengesSelectionChanged;
                lv.selectionChanged += OnChallengesSelectionChanged;
            }
        }

        private void OnLevelsSelectionChanged(IEnumerable<object> selected)
        {
            _selectedChallengeIndex = -1;
            _selectedLevelIndex = _levelsList != null ? _levelsList.selectedIndex : -1;

            if (_challengesList != null) _challengesList.ClearSelection();
            UpdateDetailsLabel();
            UpdateTutorialOverlay();
        }

        private void OnChallengesSelectionChanged(IEnumerable<object> selected)
        {
            _selectedLevelIndex = -1;
            _selectedChallengeIndex = _challengesList != null ? _challengesList.selectedIndex : -1;

            if (_levelsList != null) _levelsList.ClearSelection();

            // Обучение: шаг 9 -> выбрать Испытание 1 (первый элемент)
            TryAdvanceTutorialAfterChallengePick();

            UpdateDetailsLabel();
            UpdateTutorialOverlay();
        }

        private void TryAdvanceTutorialAfterChallengePick()
        {
            if (!GironoidApp.IsReady) return;

            var p = GironoidApp.Profile;
            if (p == null) return;

            // Step 9: ждём, что игрок выберет первое испытание
            if (p.TutorialStep >= 9 && p.TutorialStep < 10)
            {
                if (_selectedChallengeIndex == 0)
                    SetTutorialStepAtLeast(10);
            }
        }

        private void UpdateDetailsLabel()
        {
            if (_lblDetails == null)
                return;

            var vm = GetSelectedVm();
            if (vm == null)
            {
                _lblDetails.text = "Выберите уровень или испытание.";
                return;
            }

            if (vm.IsLocked)
            {
                _lblDetails.text = "Недоступно: недостаточный уровень игрока.";
                return;
            }

            // Мини-описание для первого испытания Земли (MVP-текст)
            if (IsEarthPlanet() && vm.TypeName == "Challenge" && _selectedChallengeIndex == 0)
            {
                _lblDetails.text =
                    "Испытание 1\n" +
                    "Цель: уничтожайте астероиды и не допускайте их падения на планету.\n" +
                    "Условие победы: выжить 60 секунд.\n" +
                    "Условие поражения: потерять 3 жизни.\n" +
                    "Нажмите «Вылет», чтобы начать.";
                return;
            }

            _lblDetails.text = "Нажмите «Вылет», чтобы начать выбранную активность.";
        }

        // ---------------- Logic ----------------

        private void RefreshAll(bool force)
        {
            var profileService = AppRef.GetService("ProfileService");
            var profile = AppRef.GetProfile(profileService);

            if (_lblTitle != null) _lblTitle.text = "Орбита планеты";

            var planetId = AppRef.ReadString(profile, "CurrentPlanetId")
                       ?? AppRef.ReadString(profile, "CurrentSystemId")
                       ?? "--";

            if (_lblPlanet != null)
                _lblPlanet.text = $"Планета: {planetId}";

            var energy = AppRef.ReadInt(profile, "Energy", defaultValue: 0);
            var cap = AppRef.GetEnergyCapSafe(profileService, profile);

            if (_lblEnergy != null) _lblEnergy.text = $"Энергия: {energy}/{cap}";
            if (_lblPlayerLevel != null) _lblPlayerLevel.text = $"Уровень игрока: {AppRef.ReadInt(profile, "PlayerLevel", defaultValue: 1)}";

            // Текст кнопки: по ТЗ это "Вылет"
            if (_btnStart != null)
                _btnStart.text = "Вылет";

            BuildActivities(profile);

            if (_levelsList != null) _levelsList.Rebuild();
            if (_challengesList != null) _challengesList.Rebuild();

            if (force)
                UpdateDetailsLabel();
        }

        private void BuildActivities(PlayerProfile profile)
        {
            _levels.Clear();
            _challenges.Clear();

            var config = AppRef.GetService("Config");
            var content = AppRef.GetMemberValue(config, "SystemContent") ?? AppRef.GetMemberValue(config, "SystemContentConfig");
            if (content == null)
            {
                Debug.LogWarning($"{nameof(SystemLevelsScreenController)}: SystemContent is null in Config.");
                return;
            }

            var planetId = AppRef.ReadString(profile, "CurrentPlanetId") ?? AppRef.ReadString(profile, "CurrentSystemId");
            if (string.IsNullOrWhiteSpace(planetId))
                planetId = "Earth";

            if (!TryGetPlanetDef(content, planetId, out var planetDef))
            {
                Debug.LogWarning($"{nameof(SystemLevelsScreenController)}: Planet '{planetId}' not found in SystemContentConfig (TryGetPlanet/GetPlanet failed).");
                return;
            }

            var playerLevel = AppRef.ReadInt(profile, "PlayerLevel", defaultValue: 1);
            var completed = BuildCompletedSet(profile);

            // По ТЗ: на Земле — только испытания (уровни не показываем)
            if (!string.Equals(planetId, "Earth", StringComparison.OrdinalIgnoreCase))
                FillListFromPlanet(planetDef, "Levels", "Level", _levels, playerLevel, completed);

            FillListFromPlanet(planetDef, "Challenges", "Challenge", _challenges, playerLevel, completed);

            // По ТЗ: первое испытание Земли всегда открыто (страховка)
            if (string.Equals(planetId, "Earth", StringComparison.OrdinalIgnoreCase) && _challenges.Count > 0)
            {
                _challenges[0].RequiredPlayerLevel = 0;
                _challenges[0].IsLocked = false;
            }
        }

        private static bool TryGetPlanetDef(object content, string planetId, out object planetDef)
        {
            planetDef = null;
            if (content == null || string.IsNullOrWhiteSpace(planetId))
                return false;

            var t = content.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) Попытка GetPlanet(string) -> PlanetContentDefinition
            var mGet = t.GetMethod("GetPlanet", flags, binder: null, types: new[] { typeof(string) }, modifiers: null);
            if (mGet != null && mGet.ReturnType != typeof(void))
            {
                try
                {
                    var r = mGet.Invoke(content, new object[] { planetId });
                    if (r != null)
                    {
                        planetDef = r;
                        return true;
                    }
                }
                catch { /* ignore */ }
            }

            // 2) Попытка TryGetPlanet(string, out T) -> bool
            var mTry = t.GetMethod("TryGetPlanet", flags);
            if (mTry != null)
            {
                var ps = mTry.GetParameters();
                if (ps.Length == 2 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].IsOut)
                {
                    var outType = ps[1].ParameterType.GetElementType();
                    object outVal = null;

                    try
                    {
                        if (outType != null)
                            outVal = Activator.CreateInstance(outType);
                    }
                    catch
                    {
                        outVal = null;
                    }

                    var args = new object[] { planetId, outVal };
                    try
                    {
                        var okObj = mTry.Invoke(content, args);
                        if (okObj is bool ok && ok)
                        {
                            planetDef = args[1];
                            return planetDef != null;
                        }
                    }
                    catch { /* ignore */ }
                }
            }

            return false;
        }

        private static HashSet<string> BuildCompletedSet(PlayerProfile profile)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            // В этой версии профиля CompletedLevelIds / CompletedChallengeIds — отдельные списки
            if (profile != null)
            {
                if (profile.CompletedLevelIds != null)
                {
                    for (int i = 0; i < profile.CompletedLevelIds.Count; i++)
                    {
                        var id = profile.CompletedLevelIds[i];
                        if (!string.IsNullOrEmpty(id))
                            set.Add($"Level:{id}");
                    }
                }

                if (profile.CompletedChallengeIds != null)
                {
                    for (int i = 0; i < profile.CompletedChallengeIds.Count; i++)
                    {
                        var id = profile.CompletedChallengeIds[i];
                        if (!string.IsNullOrEmpty(id))
                            set.Add($"Challenge:{id}");
                    }
                }
            }

            return set;
        }

        private static void FillListFromPlanet(
            object planetDef,
            string listMemberName,
            string typeName,
            List<ActivityVm> target,
            int playerLevel,
            HashSet<string> completed)
        {
            var listObj = AppRef.GetMemberValue(planetDef, listMemberName);
            if (!(listObj is IEnumerable enumerable))
                return;

            foreach (var def in enumerable)
            {
                if (def == null) continue;

                var id = AppRef.ReadString(def, "Id")
                      ?? AppRef.ReadString(def, "LevelId")
                      ?? Guid.NewGuid().ToString("N");

                // В ваших дефах используется NameRu (а не TitleRu)
                var title = AppRef.ReadString(def, "NameRu")
                        ?? AppRef.ReadString(def, "TitleRu")
                        ?? AppRef.ReadString(def, "Title")
                        ?? id;

                var cost = AppRef.ReadInt(def, "EnergyCost", defaultValue: 0);

                // Для Challenges: UnlockPlayerLevel (а не RequiredPlayerLevel)
                var reqLevel = AppRef.ReadInt(def, "UnlockPlayerLevel", defaultValue: 0);
                if (reqLevel <= 0)
                    reqLevel = AppRef.ReadInt(def, "RequiredPlayerLevel", defaultValue: 0);

                var vm = new ActivityVm
                {
                    TypeName = typeName,
                    Id = id,
                    Title = title,
                    EnergyCost = cost,
                    RequiredPlayerLevel = reqLevel,
                    IsLocked = reqLevel > 0 && playerLevel < reqLevel,
                    IsCompleted = completed.Contains($"{typeName}:{id}")
                };

                target.Add(vm);
            }
        }

        private ActivityVm GetSelectedVm()
        {
            if ((uint)_selectedLevelIndex < (uint)_levels.Count)
                return _levels[_selectedLevelIndex];

            if ((uint)_selectedChallengeIndex < (uint)_challenges.Count)
                return _challenges[_selectedChallengeIndex];

            return null;
        }

        private bool IsEarthPlanet()
        {
            if (!GironoidApp.IsReady) return false;
            var p = GironoidApp.Profile;
            if (p == null) return false;
            return string.Equals(p.CurrentPlanetId ?? "", "Earth", StringComparison.OrdinalIgnoreCase);
        }

        private void OnBackClicked()
        {
            SceneManager.LoadScene(AppRef.ResolveScene("StarMap", fallback: "20_StarMap"));
        }

        private void OnStartClicked()
        {
            var vm = GetSelectedVm();
            if (vm == null)
            {
                if (_lblDetails != null) _lblDetails.text = "Сначала выберите активность.";
                return;
            }

            if (vm.IsLocked)
            {
                if (_lblDetails != null) _lblDetails.text = "Недоступно: недостаточный уровень игрока.";
                return;
            }

            // Обучение: шаг 10 -> нажать Вылет
            if (GironoidApp.IsReady)
            {
                var prof = GironoidApp.Profile;
                if (prof != null && prof.TutorialStep >= 10 && prof.TutorialStep < 11)
                    SetTutorialStepAtLeast(11);
            }

            var profileService = AppRef.GetService("ProfileService");
            var profile = AppRef.GetProfile(profileService);

            var energy = AppRef.ReadInt(profile, "Energy", defaultValue: 0);
            if (vm.EnergyCost > 0 && energy < vm.EnergyCost)
            {
                if (_lblDetails != null) _lblDetails.text = "Недостаточно энергии.";
                return;
            }

            var flush = AppRef.ShouldFlushToCloud();
            var ok = AppRef.TryApply(profileService, p =>
            {
                if (vm.EnergyCost > 0)
                {
                    var e = AppRef.ReadInt(p, "Energy", defaultValue: 0);
                    AppRef.WriteInt(p, "Energy", Math.Max(0, e - vm.EnergyCost));
                }

                AppRef.TrySetActivitySelection(p, vm.TypeName, vm.Id);
            }, flush);

            if (!ok)
            {
                if (_lblDetails != null) _lblDetails.text = "Не удалось применить профиль (ProfileService.Apply не найден).";
                return;
            }

            SceneManager.LoadScene(AppRef.ResolveScene("Gameplay", fallback: "50_Gameplay"));
        }

        private void SetTutorialStepAtLeast(int step)
        {
            if (!GironoidApp.IsReady) return;

            var ps = GironoidApp.ProfileService;
            if (ps == null) return;

            ps.Apply(p =>
            {
                if (p.TutorialStep < step)
                    p.TutorialStep = step;
            }, flushToServer: false);
        }

        private void UpdateTutorialOverlay()
        {
            if (_tutorial == null || !GironoidApp.IsReady)
                return;

            var p = GironoidApp.Profile;
            if (p == null)
                return;

            // Step 9: выбрать "Испытание 1"
            if (p.TutorialStep >= 9 && p.TutorialStep < 10)
            {
                if (_challengesList != null)
                    _tutorial.Show(_challengesList, "Выберите «Испытание 1» на планете Земля.", padding: 12f);
                return;
            }

            // Step 10: нажать "Вылет"
            if (p.TutorialStep >= 10 && p.TutorialStep < 11)
            {
                if (_btnStart != null)
                    _tutorial.Show(_btnStart, "Нажмите «Вылет». Игра откроет геймплей и покажет цель испытания.", padding: 12f);
                return;
            }

            _tutorial.Hide();
        }

        // ---------------- Data ----------------

        private sealed class ActivityVm
        {
            public string TypeName;
            public string Id;
            public string Title;
            public int EnergyCost;
            public int RequiredPlayerLevel;
            public bool IsCompleted;
            public bool IsLocked;
        }

        // ---------------- Reflection Access ----------------

        private static class AppRef
        {
            private static readonly Type AppType = typeof(GironoidApp);

            private readonly struct MemberKey : IEquatable<MemberKey>
            {
                public readonly Type Type;
                public readonly string Name;
                public readonly bool IsStatic;

                public MemberKey(Type type, string name, bool isStatic)
                {
                    Type = type;
                    Name = name;
                    IsStatic = isStatic;
                }

                public bool Equals(MemberKey other) =>
                    Type == other.Type && IsStatic == other.IsStatic && string.Equals(Name, other.Name, StringComparison.Ordinal);

                public override bool Equals(object obj) => obj is MemberKey other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        var h = Type != null ? Type.GetHashCode() : 0;
                        h = (h * 397) ^ (IsStatic ? 1 : 0);
                        h = (h * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                        return h;
                    }
                }
            }

            private static readonly Dictionary<MemberKey, MemberInfo> MemberCache = new Dictionary<MemberKey, MemberInfo>(capacity: 64);

            public static object GetService(string appMemberName)
            {
                var mi = GetMemberInfo(AppType, appMemberName, isStatic: true);
                if (mi == null) return null;
                return ReadMemberValue(null, mi);
            }

            public static PlayerProfile GetProfile(object profileService)
            {
                if (profileService == null)
                    return null;

                var t = profileService.GetType();

                var v = GetMemberValue(profileService, t, "Current")
                     ?? GetMemberValue(profileService, t, "Profile")
                     ?? GetMemberValue(profileService, t, "CurrentProfile");

                return v as PlayerProfile;
            }

            public static bool TryApply(object profileService, Action<PlayerProfile> mutator, bool flushToCloud)
            {
                if (profileService == null || mutator == null)
                    return false;

                var t = profileService.GetType();
                var m = t.GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                {
                    var ps = m.GetParameters();

                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Action<PlayerProfile>))
                    {
                        m.Invoke(profileService, new object[] { mutator });
                        return true;
                    }

                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(Action<PlayerProfile>) && ps[1].ParameterType == typeof(bool))
                    {
                        m.Invoke(profileService, new object[] { mutator, flushToCloud });
                        return true;
                    }
                }

                var m2 = t.GetMethod("ApplyProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m2 != null)
                {
                    var ps = m2.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Action<PlayerProfile>))
                    {
                        m2.Invoke(profileService, new object[] { mutator });
                        return true;
                    }
                }

                return false;
            }

            public static bool ShouldFlushToCloud()
            {
                try
                {
                    if (GironoidApp.IsReady && GironoidApp.Yandex != null)
                        return GironoidApp.Yandex.CanUseCloud;
                }
                catch { }

                var bridge = GetService("Bridge") ?? GetService("YandexBridge");
                if (bridge == null) return false;

                var isAuth = ReadBool(bridge, "IsAuthorized", defaultValue: false)
                          || ReadBool(bridge, "Authorized", defaultValue: false);

                return isAuth;
            }

            public static int GetEnergyCapSafe(object profileService, PlayerProfile profile)
            {
                var cap = ReadInt(profile, "EnergyCap", defaultValue: -1);
                if (cap > 0) return cap;

                var config = GetService("Config");
                if (config != null)
                {
                    var energyCfg = GetMemberValue(config, config.GetType(), "Energy")
                                 ?? GetMemberValue(config, config.GetType(), "EnergyConfig");

                    if (energyCfg != null)
                    {
                        // В вашем профиле это TankLevel (не EnergyTankLevel)
                        var tankLevel = ReadInt(profile, "TankLevel", defaultValue: 0);
                        var capObj = Call(energyCfg, "GetEnergyCap", tankLevel);
                        if (capObj is int i && i > 0) return i;
                    }
                }

                return 20;
            }

            public static string ResolveScene(string constName, string fallback)
            {
                var scenesType = typeof(GironoidScenes);
                var f = scenesType.GetField(constName, BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = (string)f.GetValue(null);
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
                return fallback;
            }

            public static object GetMemberValue(object instance, string name)
            {
                if (instance == null) return null;
                return GetMemberValue(instance, instance.GetType(), name);
            }

            private static object GetMemberValue(object instance, Type type, string name)
            {
                var mi = GetMemberInfo(type, name, isStatic: false);
                if (mi == null) return null;
                return ReadMemberValue(instance, mi);
            }

            public static object Call(object instance, string methodName, params object[] args)
            {
                if (instance == null) return null;

                var t = instance.GetType();
                var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return null;

                try { return m.Invoke(instance, args); }
                catch { return null; }
            }

            public static string ReadString(object instance, string memberName)
            {
                if (instance == null) return null;
                var v = GetMemberValue(instance, memberName);
                return v as string;
            }

            public static int ReadInt(object instance, string memberName, int defaultValue)
            {
                if (instance == null) return defaultValue;
                var v = GetMemberValue(instance, memberName);
                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (v is float f) return (int)f;
                return defaultValue;
            }

            public static bool ReadBool(object instance, string memberName, bool defaultValue)
            {
                if (instance == null) return defaultValue;
                var v = GetMemberValue(instance, memberName);
                if (v is bool b) return b;
                return defaultValue;
            }

            public static void WriteInt(object instance, string memberName, int value)
            {
                if (instance == null) return;

                var mi = GetMemberInfo(instance.GetType(), memberName, isStatic: false);
                if (mi == null) return;

                try
                {
                    if (mi is PropertyInfo pi && pi.CanWrite)
                    {
                        if (pi.PropertyType == typeof(int)) pi.SetValue(instance, value);
                        else if (pi.PropertyType == typeof(long)) pi.SetValue(instance, (long)value);
                        return;
                    }

                    if (mi is FieldInfo fi)
                    {
                        if (fi.FieldType == typeof(int)) fi.SetValue(instance, value);
                        else if (fi.FieldType == typeof(long)) fi.SetValue(instance, (long)value);
                    }
                }
                catch { }
            }

            public static void TrySetActivitySelection(PlayerProfile profile, string typeName, string id)
            {
                if (profile == null || string.IsNullOrEmpty(id))
                    return;

                var t = profile.GetType();
                var selMi = GetMemberInfo(t, "ActivitySelection", isStatic: false);

                if (selMi != null)
                {
                    var selType = GetMemberType(selMi);
                    if (selType != null)
                    {
                        object sel;
                        try { sel = Activator.CreateInstance(selType); }
                        catch { sel = null; }

                        if (sel != null)
                        {
                            // поддерживаем оба варианта: Id и ActivityId
                            SetMember(sel, "Id", id);
                            SetMember(sel, "ActivityId", id);

                            // планета (если поле есть)
                            var planetId = profile.CurrentPlanetId ?? "";
                            SetMember(sel, "PlanetId", planetId);

                            var typeMember = GetMemberInfo(selType, "Type", isStatic: false)
                                          ?? GetMemberInfo(selType, "ActivityType", isStatic: false);

                            if (typeMember != null)
                            {
                                var enumType = GetMemberType(typeMember);
                                if (enumType != null && enumType.IsEnum)
                                {
                                    object enumVal;
                                    try { enumVal = Enum.Parse(enumType, typeName, ignoreCase: true); }
                                    catch { enumVal = Activator.CreateInstance(enumType); }

                                    WriteMemberValue(sel, typeMember, enumVal);
                                }
                                else
                                {
                                    SetMember(sel, "Type", typeName);
                                }
                            }

                            WriteMemberValue(profile, selMi, sel);
                            return;
                        }
                    }
                }

                // fallback для старых профилей/схем
                SetMember(profile, "SelectedActivityId", id);
                SetMember(profile, "SelectedActivityType", typeName);
            }

            private static void SetMember(object instance, string memberName, object value)
            {
                if (instance == null) return;

                var t = instance.GetType();
                var mi = GetMemberInfo(t, memberName, isStatic: false);
                if (mi == null) return;

                WriteMemberValue(instance, mi, value);
            }

            private static MemberInfo GetMemberInfo(Type type, string name, bool isStatic)
            {
                if (type == null || string.IsNullOrEmpty(name))
                    return null;

                var key = new MemberKey(type, name, isStatic);
                if (MemberCache.TryGetValue(key, out var cached))
                    return cached;

                var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);

                var pi = type.GetProperty(name, flags);
                if (pi != null)
                {
                    MemberCache[key] = pi;
                    return pi;
                }

                var fi = type.GetField(name, flags);
                if (fi != null)
                {
                    MemberCache[key] = fi;
                    return fi;
                }

                MemberCache[key] = null;
                return null;
            }

            private static Type GetMemberType(MemberInfo mi)
            {
                if (mi is PropertyInfo pi) return pi.PropertyType;
                if (mi is FieldInfo fi) return fi.FieldType;
                return null;
            }

            private static object ReadMemberValue(object instance, MemberInfo mi)
            {
                if (mi is PropertyInfo pi)
                {
                    try { return pi.GetValue(instance); }
                    catch { return null; }
                }

                if (mi is FieldInfo fi)
                {
                    try { return fi.GetValue(instance); }
                    catch { return null; }
                }

                return null;
            }

            private static void WriteMemberValue(object instance, MemberInfo mi, object value)
            {
                if (mi is PropertyInfo pi && pi.CanWrite)
                {
                    try
                    {
                        if (value != null && !pi.PropertyType.IsInstanceOfType(value))
                        {
                            if (pi.PropertyType == typeof(string)) value = value.ToString();
                            else if (pi.PropertyType == typeof(long) && value is int i) value = (long)i;
                            else if (pi.PropertyType == typeof(int) && value is long l) value = (int)l;
                            else if (pi.PropertyType.IsEnum && value is string s)
                                value = Enum.Parse(pi.PropertyType, s, ignoreCase: true);
                        }
                        pi.SetValue(instance, value);
                    }
                    catch { }
                    return;
                }

                if (mi is FieldInfo fi)
                {
                    try
                    {
                        if (value != null && !fi.FieldType.IsInstanceOfType(value))
                        {
                            if (fi.FieldType == typeof(string)) value = value.ToString();
                            else if (fi.FieldType == typeof(long) && value is int i) value = (long)i;
                            else if (fi.FieldType == typeof(int) && value is long l) value = (int)l;
                            else if (fi.FieldType.IsEnum && value is string s)
                                value = Enum.Parse(fi.FieldType, s, ignoreCase: true);
                        }
                        fi.SetValue(instance, value);
                    }
                    catch { }
                }
            }
        }
    }
}
