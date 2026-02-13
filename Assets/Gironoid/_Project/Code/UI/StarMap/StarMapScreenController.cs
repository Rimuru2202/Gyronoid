using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;
using Gironoid._Project.Code.UI.Shared;

namespace Gironoid._Project.Code.UI.StarMap
{
    [DisallowMultipleComponent]
    public sealed class StarMapScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument _ui;

        private VisualElement _root;
        private TutorialOverlay _tutorial;

        private Button _btnBack;
        private Button _btnHangar;

        private Button _btnPlanetEarth;
        private Button _btnPlanetEfilon;
        private Button _btnPrimary;

        private Label _lblEnergy;
        private Label _lblEnergyTimer;
        private Label _lblCurrentPlanet;
        private Label _lblHint;

        private string _selectedPlanetId;

        private float _nextUiRefreshAt;
        private const float UiRefreshInterval = 0.25f;

        // Services/config
        private GameConfig _config;
        private StarMapConfig _starMap;
        private ProfileService _profileService;
        private EnergyService _energyService;

        // Anti-spam hint
        private string _lastHintKey;

        // UI init (important for WebGL / UIDocument build timing)
        private bool _uiInitialized;
        private int _uiInitAttempts;
        private const int UiInitMaxAttempts = 120; // ~120 кадров на то, чтобы UIDocument построил дерево
        private bool _isActive;

        private void Reset()
        {
            _ui = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _isActive = true;

            _root = GetRootSafe();
            if (_root == null)
                return;

            if (!TryResolveRuntime(out var error))
            {
                Debug.LogError(error, this);
                return;
            }

            // Важно: не пытаемся биндинг/подписки делать сразу.
            // В WebGL/некоторых порядках исполнения UIDocument может построить дерево позже.
            _uiInitialized = false;
            _uiInitAttempts = 0;

            ScheduleUiInit();
        }

        private void OnDisable()
        {
            _isActive = false;

            UnwireUi();

            if (_tutorial != null)
            {
                _tutorial.Dispose();
                _tutorial = null;
            }

            _uiInitialized = false;
        }

        private void Update()
        {
            if (!_uiInitialized)
                return;

            if (Time.unscaledTime < _nextUiRefreshAt)
                return;

            _nextUiRefreshAt = Time.unscaledTime + UiRefreshInterval;
            RefreshUi(force: false);
            UpdateTutorialOverlay();
        }

        private void ScheduleUiInit()
        {
            if (_root == null)
                return;

            _root.schedule.Execute(TryInitUiNow).ExecuteLater(0);
        }

        private void TryInitUiNow()
        {
            if (!_isActive)
                return;

            // Пробуем найти элементы
            BindUi(_root);

            // Минимальный набор, без которого сцена бессмысленна
            if (_btnPrimary == null || _btnBack == null || _lblHint == null || _lblCurrentPlanet == null)
            {
                _uiInitAttempts++;

                if (_uiInitAttempts >= UiInitMaxAttempts)
                {
                    Debug.LogError(
                        "StarMapScreenController: UI elements not found. " +
                        "Проверьте, что UIDocument ссылается на правильный UXML, и имена совпадают: " +
                        "btnPrimary, btnBack, lblHint, lblCurrentPlanet.",
                        this
                    );
                    return;
                }

                // Подождём следующий кадр
                ScheduleUiInit();
                return;
            }

            // UI найден — инициализируемся один раз
            if (!_uiInitialized)
            {
                CreateTutorialOverlay(_root);

                UnwireUi();
                WireUi();

                // Восстановим выбор после того, как UI готов
                var p = _profileService.Profile;
                var current = GetCurrentPlanetIdSafe(p) ?? "Earth";

                var savedSelected = (p != null && !string.IsNullOrWhiteSpace(p.SelectedPlanetId)) ? p.SelectedPlanetId : current;

                if (string.IsNullOrWhiteSpace(_selectedPlanetId))
                    _selectedPlanetId = savedSelected;

                // Во время обучения после ангара (step=8) принудительно держим выбор на текущей планете,
                // чтобы btnPrimary был "Войти в орбиту", а не перелёт.
                if (p != null && p.TutorialStep >= 8 && p.TutorialStep < 9)
                    _selectedPlanetId = current;

                RefreshUi(force: true);
                UpdateTutorialOverlay();

                _uiInitialized = true;
            }
        }

        private VisualElement GetRootSafe()
        {
            if (_ui == null)
                _ui = GetComponent<UIDocument>();

            if (_ui == null)
            {
                Debug.LogError("StarMapScreenController: UIDocument is not assigned.", this);
                return null;
            }

            var root = _ui.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("StarMapScreenController: UIDocument.rootVisualElement is null.", this);
                return null;
            }

            return root;
        }

        private void CreateTutorialOverlay(VisualElement root)
        {
            var layer = root.Q<VisualElement>("tutorialLayer");
            _tutorial = new TutorialOverlay(root, layer);
        }

        private bool TryResolveRuntime(out string error)
        {
            error = null;

            if (!GironoidApp.IsReady)
            {
                error = "StarMapScreenController: GironoidApp is not ready. Проверь, что сцена 00_Boot отработала и GironoidApp.Install(...) вызван.";
                return false;
            }

            _config = GironoidApp.Config;
            _profileService = GironoidApp.ProfileService;
            _energyService = GironoidApp.Energy;

            if (_config == null)
            {
                error = "StarMapScreenController: GameConfig is null (GironoidApp.Config).";
                return false;
            }

            _starMap = _config.StarMap;
            if (_starMap == null)
            {
                error = "StarMapScreenController: StarMapConfig is null (GameConfig.StarMap).";
                return false;
            }

            if (_profileService == null)
            {
                error = "StarMapScreenController: ProfileService is null (GironoidApp.ProfileService).";
                return false;
            }

            // _energyService может быть null — тогда покажем энергию без cap/таймера.
            return true;
        }

        private void BindUi(VisualElement root)
        {
            _btnBack = root.Q<Button>("btnBack");
            _btnHangar = root.Q<Button>("btnHangar");

            _btnPlanetEarth = root.Q<Button>("btnPlanetEarth");
            _btnPlanetEfilon = root.Q<Button>("btnPlanetEfilon");
            _btnPrimary = root.Q<Button>("btnPrimary");

            _lblEnergy = root.Q<Label>("lblEnergy");
            _lblEnergyTimer = root.Q<Label>("lblEnergyTimer");
            _lblCurrentPlanet = root.Q<Label>("lblCurrentPlanet");
            _lblHint = root.Q<Label>("lblHint");
        }

        private void WireUi()
        {
            if (_btnBack != null) _btnBack.clicked += OnBackClicked;
            if (_btnHangar != null) _btnHangar.clicked += OnHangarClicked;

            if (_btnPlanetEarth != null) _btnPlanetEarth.clicked += OnPlanetEarthClicked;
            if (_btnPlanetEfilon != null) _btnPlanetEfilon.clicked += OnPlanetEfilonClicked;

            if (_btnPrimary != null) _btnPrimary.clicked += OnPrimaryClicked;
        }

        private void UnwireUi()
        {
            if (_btnBack != null) _btnBack.clicked -= OnBackClicked;
            if (_btnHangar != null) _btnHangar.clicked -= OnHangarClicked;

            if (_btnPlanetEarth != null) _btnPlanetEarth.clicked -= OnPlanetEarthClicked;
            if (_btnPlanetEfilon != null) _btnPlanetEfilon.clicked -= OnPlanetEfilonClicked;

            if (_btnPrimary != null) _btnPrimary.clicked -= OnPrimaryClicked;
        }

        private void OnPlanetEarthClicked() => OnPlanetClicked("Earth");
        private void OnPlanetEfilonClicked() => OnPlanetClicked("Efilon");

        private void OnPlanetClicked(string planetId)
        {
            _selectedPlanetId = planetId;

            // Сохраняем выбор в профиль (это НЕ перелёт, просто выбор в UI)
            if (_profileService != null && _profileService.Profile != null)
            {
                _profileService.Apply(p =>
                {
                    p.SelectedPlanetId = planetId ?? "";
                }, flushToServer: false);
            }

            RefreshUi(force: true);
            UpdateTutorialOverlay();
        }

        private void OnPrimaryClicked()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[StarMap] btnPrimary clicked", this);
#endif

            var p = _profileService?.Profile;
            if (p == null)
                return;

            var currentPlanetId = GetCurrentPlanetIdSafe(p) ?? "Earth";
            var targetPlanetId = string.IsNullOrWhiteSpace(_selectedPlanetId) ? currentPlanetId : _selectedPlanetId;

            // Если выбранная планета = текущей, то btnPrimary = "Войти в орбиту планеты"
            if (IsSameId(currentPlanetId, targetPlanetId))
            {
                // шаг обучения: после ангара ведём в орбиту
                if (p.TutorialStep >= 8 && p.TutorialStep < 9)
                {
                    _profileService.Apply(pp =>
                    {
                        if (pp.TutorialStep < 9)
                            pp.TutorialStep = 9;
                    }, flushToServer: false);
                }

                var scene = ResolveSceneNameFallback("40_SystemLevels", "SystemLevels", "Scene_SystemLevels", "S40_SystemLevels");
                LoadSceneSafe(scene, "Не удалось загрузить сцену уровней. Проверь Build Settings.");
                return;
            }

            if (!TryGetTravelCost(_starMap, currentPlanetId, targetPlanetId, out var cost))
            {
                SetHint($"Нет маршрута {currentPlanetId} → {targetPlanetId}. Проверь StarMapConfig.Routes (From/To IDs).");
                RefreshUi(force: true);
                return;
            }

            if (cost < 0) cost = 0;

            if (!CanAffordEnergy(p, cost))
            {
                SetHint("Недостаточно энергии для перелёта.");
                RefreshUi(force: true);
                return;
            }

            var now = _profileService.NowMs;

            _profileService.Apply(pp =>
            {
                // Списываем энергию
                pp.Energy = Mathf.Max(0, pp.Energy - cost);

                // Фиксируем текущее местоположение и выбор
                pp.CurrentPlanetId = targetPlanetId ?? "";
                pp.SelectedPlanetId = targetPlanetId ?? "";

                // После траты энергии “перезапускаем” таймер регена
                if (now > 0)
                {
                    int cap = 0;
                    if (_energyService != null && _config != null && _config.Energy != null)
                        cap = _energyService.GetEnergyCap(pp, _config.Energy);

                    if (cap > 0 && pp.Energy < cap)
                        pp.LastEnergyServerTimeMs = now;
                }
            }, flushToServer: ShouldFlushToServer());

            RefreshUi(force: true);
            UpdateTutorialOverlay();
        }

        private void LoadSceneSafe(string sceneName, string userHintOnFail)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                SetHint(userHintOnFail ?? "Не удалось загрузить сцену (пустое имя).");
                Debug.LogError("LoadSceneSafe: sceneName is null/empty.", this);
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SetHint($"{userHintOnFail} Имя: {sceneName}");
                Debug.LogError($"LoadSceneSafe: Scene '{sceneName}' is not in Build Settings or cannot be loaded.", this);
                return;
            }

            SceneManager.LoadScene(sceneName);
        }

        private void OnBackClicked()
        {
            var scene = ResolveSceneNameFallback("10_Menu", "Menu", "Scene_Menu", "S10_Menu");
            LoadSceneSafe(scene, "Не удалось загрузить меню. Проверь Build Settings.");
        }

        private void OnHangarClicked()
        {
            var scene = ResolveSceneNameFallback("30_Hangar", "Hangar", "Scene_Hangar", "S30_Hangar");
            LoadSceneSafe(scene, "Не удалось загрузить ангар. Проверь Build Settings.");
        }

        private void RefreshUi(bool force)
        {
            var p = _profileService?.Profile;
            if (p == null)
                return;

            var currentPlanetId = GetCurrentPlanetIdSafe(p) ?? "Earth";

            // Во время обучения (step=8) фиксируем выбор на текущей планете
            if (p.TutorialStep >= 8 && p.TutorialStep < 9)
                _selectedPlanetId = currentPlanetId;

            var targetPlanetId = string.IsNullOrWhiteSpace(_selectedPlanetId) ? currentPlanetId : _selectedPlanetId;

            var currentName = GetPlanetDisplayNameSafe(_starMap, currentPlanetId);
            var targetName = GetPlanetDisplayNameSafe(_starMap, targetPlanetId);

            if (_lblCurrentPlanet != null)
                _lblCurrentPlanet.text = $"Текущая планета: {currentName}";

            if (_btnPlanetEarth != null) _btnPlanetEarth.text = GetPlanetDisplayNameSafe(_starMap, "Earth");
            if (_btnPlanetEfilon != null) _btnPlanetEfilon.text = GetPlanetDisplayNameSafe(_starMap, "Efilon");

            ApplyPlanetSelectionVisuals();

            // Energy UI
            var energy = p.Energy;

            int cap = 0;
            if (_energyService != null && _config != null && _config.Energy != null)
                cap = _energyService.GetEnergyCap(p, _config.Energy);

            if (_lblEnergy != null)
                _lblEnergy.text = cap > 0 ? $"Энергия: {energy}/{cap}" : $"Энергия: {energy}";

            if (_lblEnergyTimer != null)
            {
                if (cap > 0 && energy >= cap)
                {
                    _lblEnergyTimer.text = "Энергия на максимуме";
                }
                else
                {
                    var ms = _profileService != null ? _profileService.GetMsUntilNextEnergy() : 0;
                    _lblEnergyTimer.text = ms > 0 ? $"До +1 энергии: {FormatMs(ms)}" : "";
                }
            }

            // Primary button state: Orbit vs Travel
            if (IsSameId(currentPlanetId, targetPlanetId))
            {
                SetHint($"Вы на {targetName}. Нажмите «Войти в орбиту планеты», чтобы выбрать испытание.");
                SetPrimaryState(enabled: true, text: "Войти в орбиту планеты");
                return;
            }

            // Travel hint + primary button state
            if (!TryGetTravelCost(_starMap, currentPlanetId, targetPlanetId, out var cost))
            {
                SetHint($"Нет маршрута {currentPlanetId} → {targetPlanetId}. Проверь StarMapConfig.Routes (From/To IDs).");
                SetPrimaryState(enabled: false, text: "Перелёт");
                return;
            }

            SetHint($"Перелёт на {targetName} стоит {cost} энергии.");

            var canAfford = CanAffordEnergy(p, cost);
            SetPrimaryState(enabled: canAfford, text: canAfford ? $"Перелёт ({cost})" : $"Нужно {cost}");
        }

        private void UpdateTutorialOverlay()
        {
            if (_tutorial == null || _profileService == null)
                return;

            var p = _profileService.Profile;
            if (p == null)
                return;

            // Step 8: после ангара — ведём в орбиту
            if (p.TutorialStep >= 8 && p.TutorialStep < 9)
            {
                if (_btnPrimary != null)
                    _tutorial.Show(_btnPrimary, "Нажмите «Войти в орбиту планеты». Во время обучения можно нажимать только подсвеченный элемент.", padding: 12f);
                return;
            }

            _tutorial.Hide();
        }

        private void ApplyPlanetSelectionVisuals()
        {
            const string selectedClass = "btn--selected";

            void Mark(Button b, bool on)
            {
                if (b == null) return;
                b.EnableInClassList(selectedClass, on);
            }

            Mark(_btnPlanetEarth, IsSameId(_selectedPlanetId, "Earth"));
            Mark(_btnPlanetEfilon, IsSameId(_selectedPlanetId, "Efilon"));
        }

        private void SetPrimaryState(bool enabled, string text)
        {
            if (_btnPrimary == null)
                return;

            _btnPrimary.SetEnabled(enabled);

            if (!string.IsNullOrEmpty(text))
                _btnPrimary.text = text;
        }

        private void SetHint(string text)
        {
            if (_lblHint == null)
                return;

            var key = text ?? string.Empty;
            if (_lastHintKey == key)
                return;

            _lastHintKey = key;
            _lblHint.text = text ?? string.Empty;
        }

        private static bool CanAffordEnergy(PlayerProfile profile, int cost)
        {
            if (profile == null)
                return false;

            if (cost <= 0)
                return true;

            return profile.Energy >= cost;
        }

        private static bool ShouldFlushToServer()
        {
            var y = GironoidApp.Yandex;
            return y != null && y.CanUseCloud;
        }

        private static string GetCurrentPlanetIdSafe(PlayerProfile profile)
        {
            if (profile == null) return null;
            return profile.CurrentPlanetId;
        }

        // ----------------------------
        // Travel cost resolving (reflection-safe)
        // ----------------------------

        private static bool TryGetTravelCost(StarMapConfig map, string fromId, string toId, out int cost)
        {
            cost = 0;
            if (map == null)
                return false;

            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return false;

            if (IsSameId(fromId, toId))
            {
                cost = 0;
                return true;
            }

            var routesObj = GetFieldOrPropertyValue(map, "Routes");
            if (routesObj is not IEnumerable routesEnum)
                return false;

            if (TryFindRouteCost(routesEnum, fromId, toId, out cost))
                return true;

            if (TryFindRouteCost(routesEnum, toId, fromId, out cost))
                return true;

            return false;
        }

        private static bool TryFindRouteCost(IEnumerable routesEnum, string fromId, string toId, out int cost)
        {
            cost = 0;

            foreach (var r in routesEnum)
            {
                if (r == null)
                    continue;

                var rFrom = GetStringMember(r, "FromPlanetId", "FromId", "From", "A", "PlanetAId");
                var rTo = GetStringMember(r, "ToPlanetId", "ToId", "To", "B", "PlanetBId");

                if (!IsSameId(rFrom, fromId) || !IsSameId(rTo, toId))
                    continue;

                cost = GetIntMember(r, "TravelEnergyCost", "EnergyCost", "Cost", "TravelCost", "TravelCostEnergy");
                return true;
            }

            return false;
        }

        // ----------------------------
        // Planet names (reflection-safe)
        // ----------------------------

        private static string GetPlanetDisplayNameSafe(StarMapConfig map, string planetId)
        {
            if (map == null || string.IsNullOrWhiteSpace(planetId))
                return planetId ?? string.Empty;

            var planetsObj = GetFieldOrPropertyValue(map, "Planets");
            if (planetsObj is not IEnumerable planetsEnum)
                return planetId;

            foreach (var p in planetsEnum)
            {
                if (p == null)
                    continue;

                var id = GetStringMember(p, "Id", "PlanetId", "SystemId");
                if (!IsSameId(id, planetId))
                    continue;

                var ru = GetStringMember(p, "NameRu", "TitleRu", "DisplayNameRu", "Name");
                if (!string.IsNullOrWhiteSpace(ru))
                    return ru;

                return planetId;
            }

            return planetId;
        }

        // ----------------------------
        // Scene name resolving
        // ----------------------------

        private static string ResolveSceneNameFallback(string fallback, params string[] constCandidates)
        {
            try
            {
                var t = typeof(GironoidScenes);
                constCandidates ??= Array.Empty<string>();

                for (int i = 0; i < constCandidates.Length; i++)
                {
                    var name = constCandidates[i];
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        var v = f.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(v))
                            return v;
                    }

                    var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                    if (p != null && p.PropertyType == typeof(string))
                    {
                        var v = p.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(v))
                            return v;
                    }
                }
            }
            catch { /* ignore */ }

            return fallback;
        }

        // ----------------------------
        // Formatting
        // ----------------------------

        private static string FormatMs(long ms)
        {
            if (ms < 0) ms = 0;
            var totalSec = ms / 1000;
            var m = totalSec / 60;
            var s = totalSec % 60;
            return $"{m:00}:{s:00}";
        }

        private static bool IsSameId(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return true;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------
        // Reflection utilities
        // ----------------------------

        private static object GetFieldOrPropertyValue(object host, string name)
        {
            if (host == null || string.IsNullOrWhiteSpace(name))
                return null;

            var t = host.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var f = t.GetField(name, flags);
            if (f != null)
                return f.GetValue(host);

            var p = t.GetProperty(name, flags);
            if (p != null && p.CanRead)
                return p.GetValue(host);

            return null;
        }

        private static string GetStringMember(object host, params string[] names)
        {
            if (host == null || names == null) return null;

            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                if (string.IsNullOrWhiteSpace(n)) continue;

                var v = GetFieldOrPropertyValue(host, n);
                if (v is string s)
                    return s;
            }

            return null;
        }

        private static int GetIntMember(object host, params string[] names)
        {
            if (host == null || names == null) return 0;

            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                if (string.IsNullOrWhiteSpace(n)) continue;

                var v = GetFieldOrPropertyValue(host, n);
                if (v is int ii) return ii;
                if (v is long ll) return (int)ll;
                if (v is float ff) return Mathf.RoundToInt(ff);
                if (v is double dd) return (int)Math.Round(dd);
            }

            return 0;
        }
    }
}
