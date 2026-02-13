// Assets/Gironoid/_Project/Code/Core/Visual/ShipVisualView.cs
using UnityEngine;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Visual
{
    [DisallowMultipleComponent]
    public sealed class ShipVisualView : MonoBehaviour
    {
        [Header("Optional overrides (can be empty, auto-created)")]
        [SerializeField] private SpriteRenderer _hull;

        [SerializeField] private SpriteRenderer[] _weapons = new SpriteRenderer[4];
        [SerializeField] private SpriteRenderer[] _engines = new SpriteRenderer[4];
        [SerializeField] private SpriteRenderer[] _shields = new SpriteRenderer[4];
        [SerializeField] private SpriteRenderer[] _mods = new SpriteRenderer[4];

        [Header("Layout (fallback if auto-created)")]
        [SerializeField] private float _slotScale = 0.8f;

        // -------------------- runtime state --------------------
        private bool _initialized;

        // For each group: if TRUE => we auto-create renderers and can apply fallback layout.
        // If FALSE => user provided manual overrides, we must not move/scale them.
        private bool _autoWeapons;
        private bool _autoEngines;
        private bool _autoShields;
        private bool _autoMods;

        private bool _layoutApplied;

        private void Awake()
        {
            EnsureRenderers();
        }

        public void Apply(PlayerProfile p, GameConfig cfg, string shipId, int mk)
        {
            EnsureRenderers();

            if (p == null || cfg == null || string.IsNullOrEmpty(shipId))
            {
                SetAllEnabled(false);
                if (_hull != null) _hull.sprite = null;
                return;
            }

            // Hull sprite:
            // - prefer Resources sprite if exists
            // - if not found, keep the prefab-assigned sprite (do not null it)
            var loadedHull = ShipSpriteResolver.LoadHullSprite(shipId);
            if (loadedHull != null)
            {
                _hull.sprite = loadedHull;
                _hull.enabled = true;
            }
            else
            {
                // keep existing hull sprite if any
                _hull.enabled = (_hull != null && _hull.sprite != null);
            }

            // Slot counts from ShipCatalog (if available)
            int wSlots = 0, eSlots = 0, sSlots = 0, mSlots = 0;
            if (cfg.ShipCatalog != null && cfg.ShipCatalog.TryGetTier(shipId, mk, out var tier))
            {
                wSlots = Mathf.Clamp(tier.Slots.WeaponSlots, 0, 4);
                eSlots = Mathf.Clamp(tier.Slots.EngineSlots, 0, 4);
                sSlots = Mathf.Clamp(tier.Slots.ShieldSlots, 0, 4);
                mSlots = Mathf.Clamp(tier.Slots.ModSlots, 0, 4);
            }

            var ship = p.GetShip(shipId);
            if (ship == null)
            {
                SetAllEnabled(false);
                return;
            }

            ApplyType(p, cfg, ship, ItemType.Weapon, wSlots, _weapons);
            ApplyType(p, cfg, ship, ItemType.Engine, eSlots, _engines);
            ApplyType(p, cfg, ship, ItemType.Shield, sSlots, _shields);
            ApplyType(p, cfg, ship, ItemType.Modifier, mSlots, _mods);
        }

        private void ApplyType(PlayerProfile p, GameConfig cfg, ShipOwnership ship, ItemType type, int slots, SpriteRenderer[] arr)
        {
            for (int i = 0; i < 4; i++)
            {
                var r = arr != null && i < arr.Length ? arr[i] : null;
                if (r == null) continue;

                if (i >= slots)
                {
                    r.sprite = null;
                    r.enabled = false;
                    continue;
                }

                string instanceId = null;
                ship.TryGet(type, i, out instanceId);

                if (string.IsNullOrEmpty(instanceId) || !p.TryGetItem(instanceId, out var item) || item == null)
                {
                    r.sprite = null;
                    r.enabled = false;
                    continue;
                }

                // Icon from ItemCatalog
                Sprite icon = null;
                if (cfg.ItemCatalog != null && cfg.ItemCatalog.TryGet(item.DefinitionId, out var def))
                    icon = def.Icon;

                r.sprite = icon;
                r.enabled = icon != null;
            }
        }

        private void EnsureRenderers()
        {
            // Ensure arrays are always length 4 (safe for inspector + runtime)
            EnsureArraySize(ref _weapons);
            EnsureArraySize(ref _engines);
            EnsureArraySize(ref _shields);
            EnsureArraySize(ref _mods);

            // First-time mode detection:
            // If any renderer is assigned in inspector => treat group as MANUAL (do not auto layout).
            if (!_initialized)
            {
                _autoWeapons = !HasAnyAssigned(_weapons);
                _autoEngines = !HasAnyAssigned(_engines);
                _autoShields = !HasAnyAssigned(_shields);
                _autoMods = !HasAnyAssigned(_mods);

                _initialized = true;
            }

            // Hull: create only if missing
            if (_hull == null)
            {
                var go = new GameObject("Hull");
                go.transform.SetParent(transform, false);
                _hull = go.AddComponent<SpriteRenderer>();
                _hull.sortingOrder = 0;
            }

            // Auto-create slot renderers only for groups without manual overrides
            if (_autoWeapons) EnsureSlotArrayAuto(ref _weapons, "WeaponSlots", sortingOrder: 1);
            if (_autoEngines) EnsureSlotArrayAuto(ref _engines, "EngineSlots", sortingOrder: 1);
            if (_autoShields) EnsureSlotArrayAuto(ref _shields, "ShieldSlots", sortingOrder: 1);
            if (_autoMods) EnsureSlotArrayAuto(ref _mods, "ModSlots", sortingOrder: 1);

            // Apply fallback layout ONLY once and ONLY for auto-created groups.
            if (!_layoutApplied)
            {
                ApplyDefaultLayoutForAutoGroups();
                _layoutApplied = true;
            }
        }

        private static void EnsureArraySize(ref SpriteRenderer[] arr)
        {
            if (arr == null)
            {
                arr = new SpriteRenderer[4];
                return;
            }

            if (arr.Length != 4)
            {
                var old = arr;
                arr = new SpriteRenderer[4];

                var n = Mathf.Min(old.Length, 4);
                for (int i = 0; i < n; i++)
                    arr[i] = old[i];
            }
        }

        private static bool HasAnyAssigned(SpriteRenderer[] arr)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null)
                    return true;
            return false;
        }

        private void EnsureSlotArrayAuto(ref SpriteRenderer[] arr, string rootName, int sortingOrder)
        {
            // Auto mode: we are allowed to create hierarchy and apply scale/layout
            Transform root = transform.Find(rootName);
            if (root == null)
            {
                var rootGo = new GameObject(rootName);
                rootGo.transform.SetParent(transform, false);
                root = rootGo.transform;
            }

            for (int i = 0; i < 4; i++)
            {
                if (arr[i] != null)
                {
                    // Even if element exists, in auto mode we keep it, but we won't move it here.
                    // Layout is applied in ApplyDefaultLayoutForAutoGroups().
                    continue;
                }

                Transform t = root.Find($"{i}");
                if (t == null)
                {
                    var go = new GameObject($"{i}");
                    go.transform.SetParent(root, false);
                    t = go.transform;
                }

                var sr = t.GetComponent<SpriteRenderer>();
                if (sr == null) sr = t.gameObject.AddComponent<SpriteRenderer>();

                sr.sortingOrder = sortingOrder;
                sr.enabled = false;

                t.localScale = Vector3.one * _slotScale;

                arr[i] = sr;
            }
        }

        private void ApplyDefaultLayoutForAutoGroups()
        {
            // Important:
            // Only place rows for the auto-created groups.
            // Manual overrides (assigned in inspector) keep their prefab positions.

            if (_autoWeapons) PlaceRow(_weapons, y: 0.20f, x0: -0.30f, dx: 0.20f);
            if (_autoEngines) PlaceRow(_engines, y: -0.25f, x0: -0.20f, dx: 0.20f);
            if (_autoShields) PlaceRow(_shields, y: 0.00f, x0: -0.35f, dx: 0.23f);
            if (_autoMods) PlaceRow(_mods, y: 0.38f, x0: -0.30f, dx: 0.20f);
        }

        private static void PlaceRow(SpriteRenderer[] arr, float y, float x0, float dx)
        {
            if (arr == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (arr[i] == null) continue;
                arr[i].transform.localPosition = new Vector3(x0 + dx * i, y, 0f);
            }
        }

        private void SetAllEnabled(bool on)
        {
            if (_hull != null) _hull.enabled = on && _hull.sprite != null;

            SetArray(_weapons, on);
            SetArray(_engines, on);
            SetArray(_shields, on);
            SetArray(_mods, on);
        }

        private static void SetArray(SpriteRenderer[] arr, bool on)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null)
                    arr[i].enabled = on && arr[i].sprite != null;
        }
    }
}
