using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Config/EnergyConfig", fileName = "EnergyConfig")]
    public sealed class EnergyConfig : ScriptableObject
    {
        [Header("Base")]
        [Min(1)] public int BaseEnergyCap = 20;

        [Tooltip("Базовый интервал регена в секундах (уровень конденсатора 0). По ТЗ: 180 (3 минуты).")]
        [Min(1)] public int BaseRegenIntervalSeconds = 180;

        [Header("Upgrades (levels start at 0)")]
        [Tooltip("Добавка к капу по уровню бака. Индекс = уровень бака. Пример: [0]=0, [1]=+5, [2]=+10 ...")]
        public int[] TankCapBonusByLevel = { 0, 5, 10, 15, 20 };

        [Tooltip("Интервал регена (сек) по уровню конденсатора. Индекс = уровень конденсатора. Если массива не хватает, используется последний.")]
        public int[] CapacitorRegenIntervalSecondsByLevel = { 180, 165, 150, 135, 120 };

        [Header("Safety")]
        [Min(10)] public int MinRegenIntervalSeconds = 30;

        public int GetEnergyCap(int tankLevel)
        {
            var bonus = GetArrayValueOrLast(TankCapBonusByLevel, tankLevel);
            var cap = BaseEnergyCap + bonus;
            return cap < 1 ? 1 : cap;
        }

        public int GetRegenIntervalSeconds(int capacitorLevel)
        {
            var interval = GetArrayValueOrLast(CapacitorRegenIntervalSecondsByLevel, capacitorLevel);
            if (interval < MinRegenIntervalSeconds) interval = MinRegenIntervalSeconds;
            return interval < 1 ? 1 : interval;
        }

        private static int GetArrayValueOrLast(int[] arr, int index)
        {
            if (arr == null || arr.Length == 0) return 0;
            if (index < 0) index = 0;
            if (index >= arr.Length) return arr[arr.Length - 1];
            return arr[index];
        }
    }
}