using UnityEngine;

namespace Gironoid._Project.Code.Core.Config
{
    [CreateAssetMenu(menuName = "Gironoid/Config/GameConfig", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Core Configs")]
        public EnergyConfig Energy;
        public StarMapConfig StarMap;
        public SystemContentConfig SystemContent;
        public PlayerWeaponConfig PlayerWeapon;

        [Header("Meta Catalogs")]
        public ShipCatalog ShipCatalog;
        public ItemCatalog ItemCatalog;

        [Header("Resources path (Boot will load this if no override)")]
        public string ResourcesPath = "Gironoid/GameConfig";
    }
}
