using UnityEngine;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Services
{
    public static class HangarLogic
    {
        public struct Result
        {
            public bool Ok;
            public string Error;

            public static Result Success() => new Result { Ok = true, Error = "" };
            public static Result Fail(string error) => new Result { Ok = false, Error = string.IsNullOrEmpty(error) ? "Ошибка" : error };
        }

        public struct DismantleGain
        {
            public int Tokens;
            public int Iron;
            public int Copper;
            public int Silver;
        }

        // ВАЖНО: старое поведение "выдать стартовый корабль/оружие/двигатель" УБРАНО по ТЗ.
        // Метод сохранён для совместимости: он теперь лишь нормализует выбор корабля и может пригодиться в будущем.
        public static bool EnsureStarterKit(PlayerProfile p, GameConfig cfg)
        {
            if (p == null || cfg == null) return false;

            bool changed = false;

            if (p.OwnedShips == null)
            {
                p.OwnedShips = new System.Collections.Generic.List<ShipOwnership>(8);
                changed = true;
            }

            // если выбранный корабль некорректен — сбросим
            if (!string.IsNullOrEmpty(p.SelectedShipId) && p.GetShip(p.SelectedShipId) == null)
            {
                p.SelectedShipId = "";
                changed = true;
            }

            // если есть корабли — выбираем первый (если ничего не выбрано)
            if (string.IsNullOrEmpty(p.SelectedShipId) && p.OwnedShips.Count > 0)
            {
                for (int i = 0; i < p.OwnedShips.Count; i++)
                {
                    var s = p.OwnedShips[i];
                    if (s != null && !string.IsNullOrEmpty(s.ShipId))
                    {
                        p.SelectedShipId = s.ShipId;
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        public static Result TryPurchaseShip(PlayerProfile p, GameConfig cfg, string shipId, int costTokens)
        {
            if (p == null) return Result.Fail("Профиль не задан.");
            if (cfg == null) return Result.Fail("Конфиг не задан.");
            if (string.IsNullOrEmpty(shipId)) return Result.Fail("ShipId пуст.");

            if (cfg.ShipCatalog == null) return Result.Fail("ShipCatalog не задан.");
            if (!cfg.ShipCatalog.TryGet(shipId, out _)) return Result.Fail("Корабль не найден в каталоге.");

            if (p.GetShip(shipId) != null) return Result.Fail("Корабль уже куплен.");

            if (costTokens < 0) costTokens = 0;
            if (p.Tokens < costTokens) return Result.Fail("Недостаточно жетонов.");

            p.Tokens -= costTokens;

            var own = new ShipOwnership { ShipId = shipId, Rank = 1 };
            own.Normalize();

            if (p.OwnedShips == null) p.OwnedShips = new System.Collections.Generic.List<ShipOwnership>(8);
            p.OwnedShips.Add(own);

            if (string.IsNullOrEmpty(p.SelectedShipId))
                p.SelectedShipId = shipId;

            return Result.Success();
        }

        public static Result TryCraftItem(PlayerProfile p, GameConfig cfg, string definitionId, int ironCost, int tokenCost, out ItemInstance crafted)
        {
            crafted = null;

            if (p == null) return Result.Fail("Профиль не задан.");
            if (cfg == null) return Result.Fail("Конфиг не задан.");
            if (string.IsNullOrEmpty(definitionId)) return Result.Fail("DefinitionId пуст.");

            var items = cfg.ItemCatalog;
            if (items == null) return Result.Fail("ItemCatalog не задан.");

            if (!items.TryGet(definitionId, out var def))
                return Result.Fail("Предмет не найден в каталоге.");

            if (ironCost < 0) ironCost = 0;
            if (tokenCost < 0) tokenCost = 0;

            if (p.Iron < ironCost) return Result.Fail("Недостаточно железа.");
            if (p.Tokens < tokenCost) return Result.Fail("Недостаточно жетонов.");

            p.Iron -= ironCost;
            p.Tokens -= tokenCost;

            int level = RollCraftLevel();
            var quality = RollCraftQuality();

            var inst = def.CreateInstance(level: level, quality: quality, isProtected: false);

            // Рандомизация статов (на основе базовых из ItemDefinition)
            ApplyCraftRandomization(inst);

            if (p.Inventory == null) p.Inventory = new System.Collections.Generic.List<ItemInstance>(64);
            p.Inventory.Add(inst);

            crafted = inst;
            return Result.Success();
        }

        public static Result TryEquip(PlayerProfile p, GameConfig cfg, string shipId, int slotIndex, ItemType type, string instanceId)
        {
            if (p == null) return Result.Fail("Профиль не задан.");
            if (cfg == null) return Result.Fail("Конфиг не задан.");
            if (string.IsNullOrEmpty(shipId)) return Result.Fail("ShipId пуст.");
            if (slotIndex < 0) return Result.Fail("SlotIndex < 0.");
            if (type == ItemType.None) return Result.Fail("Тип предмета не задан.");
            if (string.IsNullOrEmpty(instanceId)) return Result.Fail("InstanceId пуст.");

            var ship = p.GetShip(shipId);
            if (ship == null) return Result.Fail("Корабль не найден.");

            if (!p.TryGetItem(instanceId, out var item) || item == null)
                return Result.Fail("Предмет не найден в инвентаре.");

            if (item.Type != type)
                return Result.Fail("Тип предмета не соответствует слоту.");

            // Если предмет уже стоит в ЭТОМ ЖЕ слоте — считаем успехом (идемпотентно).
            if (ship.TryGet(type, slotIndex, out var alreadyInSlot) && alreadyInSlot == instanceId)
                return Result.Success();

            // Нельзя экипировать один и тот же предмет в несколько мест.
            // Но если он уже экипирован в ЭТОМ ЖЕ корабле — позволяем "перекинуть" (снимаем с прежнего слота).
            if (p.IsItemEquippedAnywhere(instanceId))
            {
                if (ship.ContainsInstance(instanceId))
                {
                    ship.RemoveInstance(instanceId);
                }
                else
                {
                    return Result.Fail("Предмет уже экипирован.");
                }
            }

            ship.Set(type, slotIndex, instanceId);
            return Result.Success();
        }

        public static Result TryDismantle(PlayerProfile p, GameConfig cfg, string instanceId, out DismantleGain gain)
        {
            gain = default;

            if (p == null) return Result.Fail("Профиль не задан.");
            if (cfg == null) return Result.Fail("Конфиг не задан.");
            if (string.IsNullOrEmpty(instanceId)) return Result.Fail("InstanceId пуст.");

            if (!p.TryGetItem(instanceId, out var item) || item == null)
                return Result.Fail("Предмет не найден.");

            if (item.IsProtected)
                return Result.Fail("Стартовые предметы нельзя разбирать.");

            if (p.IsItemEquippedAnywhere(instanceId))
                return Result.Fail("Сначала снимите предмет с корабля.");

            // Удаляем из инвентаря
            for (int i = 0; i < p.Inventory.Count; i++)
            {
                if (p.Inventory[i] != null && p.Inventory[i].InstanceId == instanceId)
                {
                    p.Inventory.RemoveAt(i);
                    break;
                }
            }

            // Выдача ресурсов по salvage rules
            var catalog = cfg.ItemCatalog;
            if (catalog != null && catalog.TryGetSalvage(item.Type, item.Quality, out var sg))
            {
                gain.Tokens = sg.Tokens;
                gain.Iron = sg.Iron;
                gain.Copper = sg.Copper;
                gain.Silver = sg.Silver;

                p.Tokens += gain.Tokens;
                p.Iron += gain.Iron;
                p.Copper += gain.Copper;
                p.Silver += gain.Silver;
            }

            return Result.Success();
        }

        // -------------------- Craft RNG --------------------

        private static int RollCraftLevel()
        {
            // MVP: 1..3
            // 60% Lv1, 30% Lv2, 10% Lv3
            float r = Random.value;
            if (r < 0.60f) return 1;
            if (r < 0.90f) return 2;
            return 3;
        }

        private static ItemQuality RollCraftQuality()
        {
            // MVP распределение:
            // Common 70%, Uncommon 20%, Rare 8%, Epic 1.8%, Legendary 0.2%
            float r = Random.value;

            if (r < 0.70f) return ItemQuality.Common;
            if (r < 0.90f) return ItemQuality.Uncommon;
            if (r < 0.98f) return ItemQuality.Rare;
            if (r < 0.998f) return ItemQuality.Epic;
            return ItemQuality.Legendary;
        }

        private static float QualityFactor(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Common: return 1.00f;
                case ItemQuality.Uncommon: return 1.08f;
                case ItemQuality.Rare: return 1.18f;
                case ItemQuality.Epic: return 1.32f;
                case ItemQuality.Legendary: return 1.50f;
                default: return 1.00f;
            }
        }

        private static void ApplyCraftRandomization(ItemInstance it)
        {
            if (it == null) return;

            float q = QualityFactor(it.Quality);
            float lvl = 1f + 0.07f * Mathf.Max(0, it.Level - 1);

            // небольшая “персональная” рандомизация
            float rA = Random.Range(0.92f, 1.08f);
            float rB = Random.Range(0.92f, 1.08f);

            if (it.Type == ItemType.Weapon)
            {
                it.Damage *= q * lvl * rA;
                it.FireRate *= q * (1f + 0.03f * Mathf.Max(0, it.Level - 1)) * rB;

                it.DamageMultiplier *= 1f + 0.05f * (int)it.Quality;
                it.FireRateMultiplier *= 1f + 0.03f * (int)it.Quality;
            }
            else if (it.Type == ItemType.Engine)
            {
                it.MoveSpeedBonus *= q * lvl * rA;
                it.AccelerationBonus *= q * lvl * rB;
            }
            else if (it.Type == ItemType.Shield)
            {
                it.ShieldMaxBonus *= q * lvl * rA;
                it.ShieldRegenBonus *= q * lvl * rB;
            }
            else if (it.Type == ItemType.Modifier)
            {
                it.DamageMultiplier *= 1f + 0.04f * (int)it.Quality;
                it.FireRateMultiplier *= 1f + 0.02f * (int)it.Quality;
            }

            it.Normalize();
        }
    }
}
