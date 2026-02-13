using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.UI.Shared;
using Gironoid._Project.Code.Core.Services;

namespace Gironoid._Project.Code.UI.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument _ui;

        private VisualElement _root;
        private TutorialOverlay _tutorial;

        private Button _btnStarMap;
        private Button _btnHangar;
        private Button _btnSettings;
        private Button _btnAchievements;

        private Label _lblEnergy;
        private Label _lblEnergyTimer;

        private Label _lblHint;

        private float _nextUiRefreshAt;
        private const float UiRefreshInterval = 0.25f;

        private void Reset()
        {
            _ui = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _root = GetRootSafe();
            if (_root == null) return;

            CacheUi(_root);
            CreateTutorialOverlay(_root);

            WireUi();
            RefreshUi(force: true);
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
            if (Time.unscaledTime < _nextUiRefreshAt) return;
            _nextUiRefreshAt = Time.unscaledTime + UiRefreshInterval;

            RefreshUi(force: false);
            UpdateTutorialOverlay();
        }

        private VisualElement GetRootSafe()
        {
            if (_ui == null) _ui = GetComponent<UIDocument>();
            if (_ui == null)
            {
                Debug.LogError($"{nameof(MenuScreenController)}: UIDocument is missing.");
                return null;
            }
            return _ui.rootVisualElement;
        }

        private void CacheUi(VisualElement root)
        {
            _btnStarMap = root.Q<Button>("btnStarMap") ?? root.Q<Button>("btnLobby");
            _btnHangar = root.Q<Button>("btnHangar") ?? root.Q<Button>("btnBase");
            _btnSettings = root.Q<Button>("btnSettings");
            _btnAchievements = root.Q<Button>("btnAchievements");

            _lblEnergy = root.Q<Label>("lblEnergy") ?? root.Q<Label>("fuelLabel");
            _lblEnergyTimer = root.Q<Label>("lblEnergyTimer") ?? root.Q<Label>("fuelTimer");

            _lblHint = root.Q<Label>("lblHint");
        }

        private void CreateTutorialOverlay(VisualElement root)
        {
            var layer = root.Q<VisualElement>("tutorialLayer");
            _tutorial = new TutorialOverlay(root, layer);
        }

        private void WireUi()
        {
            if (_btnStarMap != null) _btnStarMap.clicked += OnStarMapClicked;
            if (_btnHangar != null) _btnHangar.clicked += OnHangarClicked;
            if (_btnSettings != null) _btnSettings.clicked += OnSettingsClicked;
            if (_btnAchievements != null) _btnAchievements.clicked += OnAchievementsClicked;
        }

        private void UnwireUi()
        {
            if (_btnStarMap != null) _btnStarMap.clicked -= OnStarMapClicked;
            if (_btnHangar != null) _btnHangar.clicked -= OnHangarClicked;
            if (_btnSettings != null) _btnSettings.clicked -= OnSettingsClicked;
            if (_btnAchievements != null) _btnAchievements.clicked -= OnAchievementsClicked;
        }

        private void RefreshUi(bool force)
        {
            if (!GironoidApp.IsReady) return;

            var ps = GironoidApp.ProfileService;
            var cfg = GironoidApp.Config;
            var energySvc = GironoidApp.Energy;

            var p = ps != null ? ps.Profile : null;
            if (p == null) return;

            var energy = p.Energy;

            int cap = 0;
            if (cfg != null && cfg.Energy != null && energySvc != null)
                cap = energySvc.GetEnergyCap(p, cfg.Energy);

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
                    var ms = ps != null ? ps.GetMsUntilNextEnergy() : 0;
                    if (ms <= 0)
                        _lblEnergyTimer.text = "Синхронизация...";
                    else
                        _lblEnergyTimer.text = $"До следующей энергии: {FormatMmSs(ms)}";
                }
            }

            if (_lblHint != null)
            {
                if (p.TutorialStep < 8)
                    _lblHint.text = "Обучение: настройте корабль в ангаре (покупка → крафт → установка).";
                else if (p.TutorialStep < 11)
                    _lblHint.text = "Обучение: откройте звёздную карту и войдите в орбиту Земли (выбор испытания).";
                else
                    _lblHint.text = "";
            }
        }

        private void UpdateTutorialOverlay()
        {
            if (!GironoidApp.IsReady || _tutorial == null)
                return;

            var p = GironoidApp.Profile;
            if (p == null)
                return;

            // До завершения ангара — ведём в ангар
            if (p.TutorialStep < 8)
            {
                if (_btnHangar != null)
                    _tutorial.Show(_btnHangar, "Нажмите «Ангар». Во время обучения можно нажимать только подсвеченные элементы.", padding: 12f);
                return;
            }

            // После ангара и до вылета — ведём в звёздную карту
            if (p.TutorialStep >= 8 && p.TutorialStep < 11)
            {
                if (_btnStarMap != null)
                    _tutorial.Show(_btnStarMap, "Нажмите «Звёздная карта». Далее нужно войти в орбиту планеты и выбрать испытание.", padding: 12f);
                return;
            }

            _tutorial.Hide();
        }

        private static string FormatMmSs(long ms)
        {
            if (ms < 0) ms = 0;
            var totalSec = ms / 1000;
            var m = totalSec / 60;
            var s = totalSec % 60;
            return $"{m:00}:{s:00}";
        }

        private void OnStarMapClicked()
        {
            // Если обучение не завершено до ангара — путь через ангар.
            if (GironoidApp.IsReady && GironoidApp.Profile != null && GironoidApp.Profile.TutorialStep < 8)
            {
                OnHangarClicked();
                return;
            }

            SceneManager.LoadScene(ResolveScene("StarMap", "20_StarMap"));
        }

        private void OnHangarClicked()
        {
            // Помечаем, что игрок начал обучение
            var ps = GironoidApp.ProfileService;
            if (ps != null)
            {
                ps.Apply(pp =>
                {
                    if (pp.TutorialStep < 1)
                        pp.TutorialStep = 1;
                }, flushToServer: false);
            }

            SceneManager.LoadScene(ResolveScene("Hangar", "30_Hangar"));
        }

        private void OnSettingsClicked()
        {
            SceneManager.LoadScene(ResolveScene("Settings", "10_Menu"));
        }

        private void OnAchievementsClicked()
        {
            var scene = ResolveScene("Achievements", string.Empty);
            if (!string.IsNullOrEmpty(scene))
                SceneManager.LoadScene(scene);
        }

        private static string ResolveScene(string constName, string fallback)
        {
            try
            {
                var t = typeof(GironoidScenes);
                var f = t.GetField(constName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = (string)f.GetValue(null);
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
            }
            catch { }
            return fallback;
        }
    }
}
