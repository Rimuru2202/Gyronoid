using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gironoid._Project.Code.Core.Profile
{
    [Serializable]
    public struct EquippedSlot
    {
        public ItemType Type;
        public int SlotIndex;
        public string InstanceId;
    }

    [Serializable]
    public sealed class ShipOwnership
    {
        public string ShipId;

        [Min(1)] public int Rank = 1;

        // Универсальный список экипировки: (тип, индекс) -> instanceId
        public List<EquippedSlot> Equipped = new List<EquippedSlot>(8);

        public void Normalize()
        {
            if (ShipId == null) ShipId = "";
            if (Rank < 1) Rank = 1;
            if (Equipped == null) Equipped = new List<EquippedSlot>(8);
        }

        public bool ContainsInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || Equipped == null) return false;
            for (int i = 0; i < Equipped.Count; i++)
                if (Equipped[i].InstanceId == instanceId)
                    return true;
            return false;
        }

        /// <summary>
        /// Снимает предмет instanceId со всех слотов этого корабля (если он где-то установлен).
        /// </summary>
        public bool RemoveInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || Equipped == null) return false;

            bool changed = false;
            for (int i = 0; i < Equipped.Count; i++)
            {
                var s = Equipped[i];
                if (s.InstanceId == instanceId)
                {
                    s.InstanceId = "";
                    Equipped[i] = s;
                    changed = true;
                }
            }
            return changed;
        }

        public bool TryGet(ItemType type, int slotIndex, out string instanceId)
        {
            instanceId = null;
            if (Equipped == null) return false;

            for (int i = 0; i < Equipped.Count; i++)
            {
                var s = Equipped[i];
                if (s.Type == type && s.SlotIndex == slotIndex)
                {
                    instanceId = s.InstanceId;
                    return !string.IsNullOrEmpty(instanceId);
                }
            }
            return false;
        }

        public void Set(ItemType type, int slotIndex, string instanceId)
        {
            if (Equipped == null) Equipped = new List<EquippedSlot>(8);

            for (int i = 0; i < Equipped.Count; i++)
            {
                var s = Equipped[i];
                if (s.Type == type && s.SlotIndex == slotIndex)
                {
                    s.InstanceId = instanceId;
                    Equipped[i] = s;
                    return;
                }
            }

            Equipped.Add(new EquippedSlot { Type = type, SlotIndex = slotIndex, InstanceId = instanceId });
        }

        public void Clear(ItemType type, int slotIndex)
        {
            if (Equipped == null) return;

            for (int i = 0; i < Equipped.Count; i++)
            {
                var s = Equipped[i];
                if (s.Type == type && s.SlotIndex == slotIndex)
                {
                    s.InstanceId = "";
                    Equipped[i] = s;
                    return;
                }
            }
        }
    }
}
