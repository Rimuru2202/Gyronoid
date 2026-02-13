using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Visual;
using Gironoid._Project.Code.Core.Services;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayBootstrapper : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private Camera _camera;
        [SerializeField] private GameplayHudView _hud;

        [SerializeField] private PlayerShipController2D _player;
        [SerializeField] private WeaponShooter _shooter;

        [SerializeField] private AsteroidSpawner _spawner;
        [SerializeField] private AsteroidKillZone _killZone;

        [Header("Optional ship visuals")]
        [SerializeField] private ShipVisualView _shipVisual;

        [Header("Rules")]
        [SerializeField] private int _livesStart = 3;

        private GameConfig _cfg;
        private PlayerProfile _profile;

        private GameplayActivityResolver.ResolvedActivity _activity;

        private bool _running;
        private bool _finished;

        private int _lives;
        private int _kills;
        private int _killTarget;
        private float _timeLeft;

        private void Reset()
        {
            _camera = Camera.main;
        }

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;

            if (_hud == null)
                _hud = FindFirstObjectByType<GameplayHudView>();

            if (_player == null)
                _player = FindFirstObjectByType<PlayerShipController2D>();

            if (_shooter == null)
                _shooter = FindFirstObjectByType<WeaponShooter>();

            if (_spawner == null)
                _spawner = FindFirstObjectByType<AsteroidSpawner>();

            if (_killZone == null)
                _killZone = FindFirstObjectByType<AsteroidKillZone>();

            if (_shipVisual == null && _player != null)
                _shipVisual = _player.GetComponentInChildren<ShipVisualView>();

            if (_killZone != null)
                _killZone.SetBootstrapper(this);
        }

        private void OnEnable()
        {
            if (!TryResolveRuntime())
            {
                Debug.LogError("GameplayBootstrapper: GironoidApp/Config/Profile is not ready. Ensure Boot scene executed and services installed.", this);
                enabled = false;
                return;
            }

            if (!GameplayActivityResolver.TryResolve(_cfg, _profile, out _activity))
            {
                Debug.LogError("GameplayBootstrapper: Cannot resolve activity from profile/config.", this);
                enabled = false;
                return;
            }

            PrepareSessionFromActivity(_activity);

            WireHud();
            ConfigureSystems();

            EnterIntroState();
        }

        private void OnDisable()
        {
            UnwireHud();
        }

        private bool TryResolveRuntime()
        {
            if (!GironoidApp.IsReady)
                return false;

            _cfg = GironoidApp.Config;
            if (_cfg == null)
                return false;

            _profile = ProfileServiceUtil.GetProfileSafe();
            if (_profile == null)
                return false;

            return true;
        }

        private void WireHud()
        {
            if (_hud == null) return;

            _hud.StartClicked -= OnHudStartClicked;
            _hud.RetryClicked -= OnHudRetryClicked;
            _hud.ExitClicked -= OnHudExitClicked;

            _hud.StartClicked += OnHudStartClicked;
            _hud.RetryClicked += OnHudRetryClicked;
            _hud.ExitClicked += OnHudExitClicked;
        }

        private void UnwireHud()
        {
            if (_hud == null) return;

            _hud.StartClicked -= OnHudStartClicked;
            _hud.RetryClicked -= OnHudRetryClicked;
            _hud.ExitClicked -= OnHudExitClicked;
        }

        private void ConfigureSystems()
        {
            if (_spawner != null)
                _spawner.Configure(OnAsteroidDespawned);

            // Подстроим границы спавна под камеру (если орто)
            if (_camera != null && _camera.orthographic && _spawner != null)
            {
                var halfH = _camera.orthographicSize;
                var halfW = halfH * _camera.aspect;

                // Немного “внутрь” от краёв
                SetSpawnerXRange(_spawner, -halfW + 0.6f, halfW - 0.6f);

                // spawnY чуть выше верхней границы
                SetSpawnerSpawnY(_spawner, _camera.transform.position.y + halfH + 0.8f);
            }
        }

        private void PrepareSessionFromActivity(GameplayActivityResolver.ResolvedActivity act)
        {
            _lives = Mathf.Max(1, _livesStart);
            _kills = 0;

            _timeLeft = act.TimeLimitSeconds > 0 ? act.TimeLimitSeconds : 60;

            // Если в ObjectiveRu есть число — считаем это целью по убийствам (например "Уничтожь 30 метеоритов")
            _killTarget = ExtractFirstInt(act.ObjectiveRu);

            if (_hud != null)
            {
                _hud.SetTopBar((int)_timeLeft, _lives, _kills, _killTarget);
            }
        }

        private void EnterIntroState()
        {
            _running = false;
            _finished = false;

            if (_player != null) _player.SetRunning(false);
            if (_shooter != null) _shooter.SetRunning(false);
            if (_spawner != null) _spawner.SetRunning(false);

            var title = string.IsNullOrWhiteSpace(_activity.NameRu) ? "Испытание" : _activity.NameRu;
            var objective = BuildObjectiveText(_activity, _killTarget);

            if (_hud != null)
            {
                _hud.HideResult();
                _hud.ShowIntro(title, objective, "Вылет");
            }

            // Применим визуал корабля и реальные статы движения/стрельбы из экипировки
            ApplyShipStatsAndVisuals();
        }

        private void StartRun()
        {
            if (_finished) return;

            _running = true;

            if (_hud != null)
                _hud.HideIntro();

            if (_player != null) _player.SetRunning(true);
            if (_shooter != null) _shooter.SetRunning(true);
            if (_spawner != null) _spawner.SetRunning(true);
        }

        private void FinishRun(bool win, string reason)
        {
            if (_finished) return;

            _finished = true;
            _running = false;

            if (_player != null) _player.SetRunning(false);
            if (_shooter != null) _shooter.SetRunning(false);
            if (_spawner != null) _spawner.SetRunning(false);

            var title = win ? "Победа" : "Поражение";
            var details =
                $"{_activity.NameRu}\n" +
                $"Причина: {reason}\n" +
                $"Время: {Mathf.Max(0, Mathf.CeilToInt(_timeLeft))} сек.\n" +
                $"Метеориты: {_kills}" + (_killTarget > 0 ? $"/{_killTarget}" : "") + "\n";

            if (_hud != null)
                _hud.ShowResult(title, details);

            if (win)
                SaveCompletion();

            AdvanceTutorialIfNeeded();
        }

        private void Update()
        {
            if (!_running || _finished)
                return;

            _timeLeft -= Time.deltaTime;
            if (_timeLeft < 0f) _timeLeft = 0f;

            if (_hud != null)
                _hud.SetTopBar((int)Mathf.CeilToInt(_timeLeft), _lives, _kills, _killTarget);

            // Условия победы:
            // 1) Выжить по таймеру
            // 2) Если killTarget задан — нужно набрать kills >= killTarget
            if (_timeLeft <= 0f)
            {
                if (_killTarget <= 0 || _kills >= _killTarget)
                    FinishRun(win: true, reason: "Цель выполнена");
                else
                    FinishRun(win: false, reason: "Не достигнута цель по метеоритам");
            }

            if (_lives <= 0)
            {
                FinishRun(win: false, reason: "Потеряны все жизни");
            }
        }

        // вызывается из KillZone
        public void OnAsteroidMissed(Asteroid asteroid)
        {
            if (asteroid == null) return;
            asteroid.Missed();
        }

        private void OnAsteroidDespawned(Asteroid asteroid, Asteroid.AsteroidDespawnReason reason)
        {
            if (asteroid == null) return;
            if (_finished) return;

            switch (reason)
            {
                case Asteroid.AsteroidDespawnReason.Killed:
                    _kills++;
                    break;

                case Asteroid.AsteroidDespawnReason.Missed:
                case Asteroid.AsteroidDespawnReason.HitPlayer:
                    _lives--;
                    break;
            }

            if (_hud != null)
                _hud.SetTopBar((int)Mathf.CeilToInt(_timeLeft), _lives, _kills, _killTarget);

            // Проверка на раннюю победу по killTarget (если без таймера — тоже может быть полезно)
            if (_killTarget > 0 && _kills >= _killTarget && _activity.TimeLimitSeconds <= 0)
                FinishRun(win: true, reason: "Цель по метеоритам выполнена");

            if (_lives <= 0)
                FinishRun(win: false, reason: "Потеряны все жизни");
        }

        private void OnHudStartClicked()
        {
            if (_finished) return;
            StartRun();
        }

        private void OnHudRetryClicked()
        {
            var scene = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(scene);
        }

        private void OnHudExitClicked()
        {
            // Возврат в орбиту
            var systemLevelsScene = SceneUtil.ResolveSceneName("SystemLevels", "40_SystemLevels");
            SceneManager.LoadScene(systemLevelsScene);
        }

        private void SaveCompletion()
        {
            var flush = ProfileServiceUtil.ShouldFlushToCloud();

            ProfileServiceUtil.TryApply(p =>
            {
                if (_activity.Type == GameplayActivityResolver.ActivityType.Level)
                {
                    if (p.CompletedLevelIds == null)
                        p.CompletedLevelIds = new System.Collections.Generic.List<string>(64);

                    if (!p.CompletedLevelIds.Contains(_activity.ActivityId))
                        p.CompletedLevelIds.Add(_activity.ActivityId);
                }
                else if (_activity.Type == GameplayActivityResolver.ActivityType.Challenge)
                {
                    if (p.CompletedChallengeIds == null)
                        p.CompletedChallengeIds = new System.Collections.Generic.List<string>(64);

                    if (!p.CompletedChallengeIds.Contains(_activity.ActivityId))
                        p.CompletedChallengeIds.Add(_activity.ActivityId);
                }

                p.UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }, flushToServer: flush);
        }

        private void AdvanceTutorialIfNeeded()
        {
            // Ваша логика туториала была: до 11 — вылет.
            // После завершения первого боя логично поставить 12, чтобы туториал дальше не блокировал UI.
            ProfileServiceUtil.TryApply(p =>
            {
                if (p.TutorialStep < 12)
                    p.TutorialStep = 12;
            }, flushToServer: false);
        }

        private void ApplyShipStatsAndVisuals()
        {
            if (_profile == null || _cfg == null || _cfg.ShipCatalog == null)
                return;

            var shipId = _profile.SelectedShipId;
            if (string.IsNullOrWhiteSpace(shipId))
                shipId = _cfg.ShipCatalog.StarterShipId;

            var ship = _profile.GetShip(shipId);
            var mk = 1;

            if (ship != null)
                mk = Mathf.Clamp(ship.Rank, 1, 4);

            // Визуал
            if (_shipVisual != null)
                _shipVisual.Apply(_profile, _cfg, shipId, mk);

            // Базовые статы из ShipCatalog tier
            float baseSpeed = 6f;
            float baseAccel = 16f;

            if (_cfg.ShipCatalog.TryGetTier(shipId, mk, out var tier))
            {
                baseSpeed = Mathf.Max(0.1f, tier.Stats.Speed);
                baseAccel = Mathf.Max(0.1f, tier.Stats.Acceleration);
            }

            // Бонусы от двигателя (slot 0)
            float bonusSpeed = 0f;
            float bonusAccel = 0f;

            if (ship != null && ship.TryGet(ItemType.Engine, 0, out var engInstanceId))
            {
                if (!string.IsNullOrWhiteSpace(engInstanceId) && _profile.TryGetItem(engInstanceId, out var eng) && eng != null)
                {
                    bonusSpeed = eng.MoveSpeedBonus;
                    bonusAccel = eng.AccelerationBonus;
                }
            }

            if (_player != null)
                _player.SetStats(baseSpeed + bonusSpeed, baseAccel + bonusAccel);

            // Оружие — перестроить из профиля
            if (_shooter != null)
                _shooter.RebuildFromProfile(_profile, _cfg, shipId);
        }

        private static int ExtractFirstInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            try
            {
                var m = Regex.Match(text, @"\d+");
                if (!m.Success)
                    return 0;

                if (int.TryParse(m.Value, out var v))
                    return Mathf.Max(0, v);
            }
            catch { }

            return 0;
        }

        private static string BuildObjectiveText(GameplayActivityResolver.ResolvedActivity a, int killTarget)
        {
            var tl = a.TimeLimitSeconds > 0 ? a.TimeLimitSeconds : 60;

            var s = "";
            s += $"Планета: {a.PlanetId}\n";
            s += $"Таймер: {tl} сек.\n";
            s += "Поражение: потерять 3 жизни.\n";

            if (killTarget > 0)
                s += $"Цель: уничтожить {killTarget} метеоритов и выжить.\n";
            else
                s += "Цель: выжить до конца таймера.\n";

            if (!string.IsNullOrWhiteSpace(a.ObjectiveRu))
                s += $"\nОписание:\n{a.ObjectiveRu}";

            return s;
        }

        private static void SetSpawnerXRange(AsteroidSpawner spawner, float minX, float maxX)
        {
            if (spawner == null) return;

            var t = spawner.GetType();
            var fMin = t.GetField("_minX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fMax = t.GetField("_maxX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (fMin != null) fMin.SetValue(spawner, minX);
            if (fMax != null) fMax.SetValue(spawner, maxX);
        }

        private static void SetSpawnerSpawnY(AsteroidSpawner spawner, float spawnY)
        {
            if (spawner == null) return;

            var t = spawner.GetType();
            var f = t.GetField("_spawnY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(spawner, spawnY);
        }
    }
}
