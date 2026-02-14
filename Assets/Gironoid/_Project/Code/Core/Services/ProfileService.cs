using System;
using UnityEngine;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Yandex;

namespace Gironoid._Project.Code.Core.Services
{
    public sealed class ProfileService
    {
        private readonly GameConfig _cfg;
        private readonly YandexBridgeBehaviour _yandex;
        private readonly EnergyService _energy;
        private readonly ProfileRepository _repo;

        private bool _dirty;
        private float _autosaveTimer;

        // NEW: облако может быть не готово на старте — мы не должны затирать данные.
        private CloudSyncState _cloudState = CloudSyncState.Unknown;
        private bool _cloudAdoptAttempted;

        private enum CloudSyncState
        {
            Unknown = 0,
            Pending = 1,   // ожидаем готовности облака/SDK
            Loaded = 2,    // профиль загружен из облака
            Empty = 3      // облако готово, но профиля там нет (новый пользователь)
        }

        public PlayerProfile Profile { get; private set; }

        public ProfileService(GameConfig cfg, YandexBridgeBehaviour yandex, EnergyService energy, ProfileRepository repo)
        {
            _cfg = cfg;
            _yandex = yandex;
            _energy = energy;
            _repo = repo;
        }

        public bool IsReady => Profile != null;

        public long NowMs
        {
            get
            {
                long t = _yandex != null ? _yandex.GetServerTimeMsSafe() : 0;
                if (t <= 0) t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return t;
            }
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (Profile == null || _cfg == null) return;

            // NEW: если на старте облако было не готово — пытаемся подхватить облачный профиль позже,
            // и только если он реально новее локального.
            TryAdoptCloudProfileIfBecameReady();

            var beforeEnergy = Profile.Energy;

            if (_cfg.Energy != null)
                _energy?.ApplyRegen(Profile, _cfg.Energy, NowMs);

            if (Profile.Energy != beforeEnergy)
                MarkDirty();

            _autosaveTimer += unscaledDeltaTime;

            // Автосейв локально (без облака)
            if (_dirty && _autosaveTimer >= 2f)
            {
                _autosaveTimer = 0f;
                Save(flushToServer: false);
            }
        }

        public void Apply(Action<PlayerProfile> mutator, bool flushToServer = false)
        {
            if (Profile == null || mutator == null) return;

            mutator(Profile);
            Profile.Normalize();

            EnsureDefaults(Profile);

            MarkDirty();
            Save(flushToServer);
        }

        public void CreateOrSet(PlayerProfile profile, bool flushToServer = false)
        {
            Profile = profile ?? PlayerProfile.CreateDefault();
            Profile.Normalize();

            EnsureDefaults(Profile);

            MarkDirty();
            Save(flushToServer);
        }

        public void LoadOrCreate()
        {
            bool loadedFromCloud = false;

            // 1) Облако (если авторизован)
            if (_yandex != null && _yandex.CanUseCloud)
            {
                // ВАЖНО: не полагаемся только на HasCloudProfile — на старте поля могут быть пустыми
                // до завершения загрузки SDK. Поэтому:
                // - если сейчас удалось прочитать json -> грузим
                // - если не удалось и облако не готово -> помечаем Pending и потом подхватим в Tick()
                if (_yandex.TryLoadProfileFromCloud(out var cloudJson, out var cloudUpdated))
                {
                    var p = ProfileJson.FromJsonSafe(cloudJson);
                    if (p != null)
                    {
                        if (cloudUpdated > 0) p.UpdatedUtcMs = cloudUpdated;
                        Profile = p;
                        loadedFromCloud = true;
                        _cloudState = CloudSyncState.Loaded;
                    }
                }
                else
                {
                    _cloudState = _yandex.IsCloudReady ? CloudSyncState.Empty : CloudSyncState.Pending;
                }
            }

            // 2) Локально (fallback)
            if (Profile == null && _repo != null && _repo.TryLoad(out var localJson, out var localUpdated))
            {
                var p = ProfileJson.FromJsonSafe(localJson);
                if (p != null)
                {
                    if (p.UpdatedUtcMs <= 0 && localUpdated > 0)
                        p.UpdatedUtcMs = localUpdated;

                    Profile = p;
                }
            }

            // 3) Дефолт
            if (Profile == null)
                Profile = PlayerProfile.CreateDefault();

            Profile.Normalize();
            EnsureDefaults(Profile);

            // Реген энергии на старте
            if (_energy != null && _cfg != null && _cfg.Energy != null)
                _energy.ApplyRegen(Profile, _cfg.Energy, NowMs);

            // Локально сохраняем всегда (без облака). В облако — только по явным действиям (flushToServer=true).
            MarkDirty();
            Save(flushToServer: false);

            // если реально загрузились из облака — отлично, Pending уже не нужен
            if (loadedFromCloud)
                _cloudAdoptAttempted = true;
        }

        public long GetMsUntilNextEnergy()
        {
            if (Profile == null || _cfg == null || _energy == null || _cfg.Energy == null) return 0;
            return _energy.GetMsUntilNextEnergy(Profile, _cfg.Energy, NowMs);
        }

        public void Save(bool flushToServer)
        {
            if (Profile == null) return;

            Profile.UpdatedUtcMs = NowMs;

            var json = ProfileJson.ToJson(Profile);
            var updated = Profile.UpdatedUtcMs;

            _repo?.Save(json, updated);

            // ВАЖНОЕ ИЗМЕНЕНИЕ:
            // В облако пишем ТОЛЬКО если flushToServer=true.
            // Это защищает от затирания облака дефолтом, когда SDK ещё не догрузил данные.
            if (flushToServer && _yandex != null && _yandex.CanUseCloud)
            {
                _yandex.SaveProfileToCloud(json, updated, flush: true);
            }

            _dirty = false;
        }

        private void TryAdoptCloudProfileIfBecameReady()
        {
            if (_yandex == null) return;
            if (!_yandex.CanUseCloud) return;

            // Если в LoadOrCreate() мы попали в Pending — попробуем подхватить облако позже,
            // один раз после готовности.
            if (_cloudAdoptAttempted) return;

            if (_cloudState != CloudSyncState.Pending)
                return;

            if (!_yandex.IsCloudReady)
                return;

            _cloudAdoptAttempted = true;

            if (_yandex.TryLoadProfileFromCloud(out var cloudJson, out var cloudUpdated))
            {
                var cloud = ProfileJson.FromJsonSafe(cloudJson);
                if (cloud != null)
                {
                    if (cloudUpdated > 0) cloud.UpdatedUtcMs = cloudUpdated;

                    // Подхватываем облако только если оно новее текущего профиля,
                    // иначе не трогаем (чтобы не потерять локальный прогресс).
                    if (cloud.UpdatedUtcMs > (Profile != null ? Profile.UpdatedUtcMs : 0))
                    {
                        Profile = cloud;
                        Profile.Normalize();
                        EnsureDefaults(Profile);

                        // сохранить локально, без отправки обратно в облако
                        MarkDirty();
                        Save(flushToServer: false);

                        _cloudState = CloudSyncState.Loaded;
                        return;
                    }
                }
            }

            // Облако готово, но профиля нет / не удалось распарсить
            _cloudState = CloudSyncState.Empty;
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void EnsureDefaults(PlayerProfile p)
        {
            if (p == null) return;

            if (!p.StarterResourcesGranted)
            {
                bool likelyNew =
                    p.UpdatedUtcMs <= 0 ||
                    ((p.Tokens == 0 && p.Iron == 0 && p.Copper == 0 && p.Silver == 0) &&
                     (p.OwnedShips == null || p.OwnedShips.Count == 0) &&
                     (p.Inventory == null || p.Inventory.Count == 0));

                if (likelyNew)
                {
                    p.Tokens += 50;
                    p.Iron += 50;
                }

                p.StarterResourcesGranted = true;
                if (p.Version < 3) p.Version = 3;
            }
            else
            {
                if (p.Version < 3) p.Version = 3;
            }

            // Миграция старых профилей: в раннем онбординге должно быть 50 токенов
            // для покупки первого корабля, а не старые 750.
            if (!p.FirstShipTutorialBudgetApplied)
            {
                bool noShips = p.OwnedShips == null || p.OwnedShips.Count == 0;
                bool earlyTutorial = p.TutorialStep <= 1;
                bool noMetaProgress =
                    (p.CompletedLevelIds == null || p.CompletedLevelIds.Count == 0) &&
                    (p.CompletedChallengeIds == null || p.CompletedChallengeIds.Count == 0);

                if (noShips && earlyTutorial && noMetaProgress)
                {
                    p.Tokens = 50;
                    if (p.Iron < 50) p.Iron = 50;
                }

                p.FirstShipTutorialBudgetApplied = true;
                if (p.Version < 3) p.Version = 3;
            }

            if (_cfg != null && _cfg.StarMap != null && _cfg.StarMap.Planets != null && _cfg.StarMap.Planets.Length > 0)
            {
                var firstPlanetId = _cfg.StarMap.Planets[0].Id ?? "";

                if (string.IsNullOrEmpty(p.CurrentPlanetId))
                    p.CurrentPlanetId = firstPlanetId;

                if (string.IsNullOrEmpty(p.SelectedPlanetId))
                    p.SelectedPlanetId = p.CurrentPlanetId;
            }
            else
            {
                if (p.CurrentPlanetId == null) p.CurrentPlanetId = "";
                if (p.SelectedPlanetId == null) p.SelectedPlanetId = "";
            }

            if (p.OwnedShips == null) p.OwnedShips = new System.Collections.Generic.List<ShipOwnership>(8);

            if (!string.IsNullOrEmpty(p.SelectedShipId) && p.GetShip(p.SelectedShipId) == null)
                p.SelectedShipId = "";

            if (string.IsNullOrEmpty(p.SelectedShipId))
            {
                for (int i = 0; i < p.OwnedShips.Count; i++)
                {
                    var s = p.OwnedShips[i];
                    if (s != null && !string.IsNullOrEmpty(s.ShipId))
                    {
                        p.SelectedShipId = s.ShipId;
                        break;
                    }
                }
            }

            if (_cfg != null && _cfg.Energy != null && _energy != null)
            {
                int cap = _energy.GetEnergyCap(p, _cfg.Energy);
                if (cap < 0) cap = 0;

                if (p.LastEnergyServerTimeMs <= 0)
                {
                    if (p.Energy <= 0 && cap > 0)
                        p.Energy = cap;

                    p.LastEnergyServerTimeMs = NowMs;
                }

                if (p.Energy < 0) p.Energy = 0;
                if (cap > 0 && p.Energy > cap) p.Energy = cap;

                if (p.Energy >= cap && p.LastEnergyServerTimeMs <= 0)
                    p.LastEnergyServerTimeMs = NowMs;
            }
        }
    }
}
