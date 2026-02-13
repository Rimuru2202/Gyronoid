// Assets/Gironoid/_Project/Code/Core/Profile/PlayerProfile.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Profile
{
    [Serializable]
    public sealed class PlayerProfile
    {
        public int Version = 1;

        public long UpdatedUtcMs;

        // Метапрогресс
        [Min(1)] public int PlayerLevel = 1;

        // Энергия
        public int Energy;
        public int TankLevel;
        public int CapacitorLevel;
        public long LastEnergyServerTimeMs;

        // Карта/выбор
        public string CurrentPlanetId;
        public string SelectedPlanetId;

        // Выбор активности (уровень/испытание) для входа в Gameplay
        public ActivitySelection ActivitySelection;

        // Ресурсы
        public int Tokens;
        public int Iron;
        public int Copper;
        public int Silver;

        // Корабли/инвентарь
        public string SelectedShipId;
        public List<ShipOwnership> OwnedShips = new List<ShipOwnership>(8);
        public List<ItemInstance> Inventory = new List<ItemInstance>(64);

        // Прогресс активностей/уровней
        public List<string> CompletedLevelIds = new List<string>(64);
        public List<string> CompletedChallengeIds = new List<string>(64);

        // --------- NEW (safe to add) ---------
        // Выдача стартового пакета должна происходить один раз (для новых профилей).
        public bool StarterResourcesGranted;

        // Онбординг/обучение в ангаре:
        // 0 = не начинали (в меню подсказка "Открой ангар")
        // 1 = в ангаре → магазин
        // 2 = купить корабль
        // 3 = крафт оружия
        // 4 = крафт двигателя
        // 5 = установить двигатель
        // 6 = установить оружие
        // 7 = вылет
        public int TutorialStep;

        public static PlayerProfile CreateDefault()
        {
            return new PlayerProfile
            {
                Version = 2,
                PlayerLevel = 1,

                Energy = 0,
                TankLevel = 0,
                CapacitorLevel = 0,
                LastEnergyServerTimeMs = 0,

                CurrentPlanetId = "",
                SelectedPlanetId = "",

                ActivitySelection = Gironoid._Project.Code.Core.Profile.ActivitySelection.None,

                Tokens = 750,
                Iron = 50,
                Copper = 0,
                Silver = 0,

                SelectedShipId = "",
                OwnedShips = new List<ShipOwnership>(8),
                Inventory = new List<ItemInstance>(64),

                CompletedLevelIds = new List<string>(64),
                CompletedChallengeIds = new List<string>(64),

                StarterResourcesGranted = true,
                TutorialStep = 0,
            };
        }

        public void Normalize()
        {
            if (PlayerLevel < 1) PlayerLevel = 1;

            if (CurrentPlanetId == null) CurrentPlanetId = "";
            if (SelectedPlanetId == null) SelectedPlanetId = "";
            if (SelectedShipId == null) SelectedShipId = "";

            // ActivitySelection: убираем null-строки (особенно важно для старых сейвов)
            var sel = ActivitySelection;
            if (sel.PlanetId == null) sel.PlanetId = "";
            if (sel.ActivityId == null) sel.ActivityId = "";
            ActivitySelection = sel;

            if (OwnedShips == null) OwnedShips = new List<ShipOwnership>(8);
            if (Inventory == null) Inventory = new List<ItemInstance>(64);

            if (CompletedLevelIds == null) CompletedLevelIds = new List<string>(64);
            if (CompletedChallengeIds == null) CompletedChallengeIds = new List<string>(64);

            if (TutorialStep < 0) TutorialStep = 0;

            for (int i = 0; i < OwnedShips.Count; i++)
                OwnedShips[i]?.Normalize();

            for (int i = 0; i < Inventory.Count; i++)
                Inventory[i]?.Normalize();
        }

        public ShipOwnership GetShip(string shipId)
        {
            if (string.IsNullOrEmpty(shipId) || OwnedShips == null) return null;
            for (int i = 0; i < OwnedShips.Count; i++)
                if (OwnedShips[i] != null && OwnedShips[i].ShipId == shipId)
                    return OwnedShips[i];
            return null;
        }

        public ShipOwnership GetOrCreateShip(string shipId)
        {
            var s = GetShip(shipId);
            if (s != null) return s;

            s = new ShipOwnership { ShipId = shipId ?? "", Rank = 1 };
            s.Normalize();
            OwnedShips.Add(s);
            return s;
        }

        public bool TryGetItem(string instanceId, out ItemInstance item)
        {
            item = null;
            if (string.IsNullOrEmpty(instanceId) || Inventory == null) return false;

            for (int i = 0; i < Inventory.Count; i++)
            {
                var it = Inventory[i];
                if (it != null && it.InstanceId == instanceId)
                {
                    item = it;
                    return true;
                }
            }
            return false;
        }

        public bool IsLevelCompleted(string levelId)
        {
            if (string.IsNullOrEmpty(levelId) || CompletedLevelIds == null) return false;
            for (int i = 0; i < CompletedLevelIds.Count; i++)
                if (CompletedLevelIds[i] == levelId)
                    return true;
            return false;
        }

        public bool IsChallengeCompleted(string challengeId)
        {
            if (string.IsNullOrEmpty(challengeId) || CompletedChallengeIds == null) return false;
            for (int i = 0; i < CompletedChallengeIds.Count; i++)
                if (CompletedChallengeIds[i] == challengeId)
                    return true;
            return false;
        }

        public void MarkLevelCompleted(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;
            if (CompletedLevelIds == null) CompletedLevelIds = new List<string>(64);
            if (!IsLevelCompleted(levelId))
                CompletedLevelIds.Add(levelId);
        }

        public void MarkChallengeCompleted(string challengeId)
        {
            if (string.IsNullOrEmpty(challengeId)) return;
            if (CompletedChallengeIds == null) CompletedChallengeIds = new List<string>(64);
            if (!IsChallengeCompleted(challengeId))
                CompletedChallengeIds.Add(challengeId);
        }

        public bool IsItemEquippedAnywhere(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || OwnedShips == null) return false;

            for (int i = 0; i < OwnedShips.Count; i++)
            {
                var s = OwnedShips[i];
                if (s != null && s.ContainsInstance(instanceId))
                    return true;
            }
            return false;
        }
    }
}
