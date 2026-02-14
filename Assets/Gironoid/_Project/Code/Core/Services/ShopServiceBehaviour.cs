using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;

namespace Gironoid._Project.Code.Core.Services
{
    /// <summary>
    /// Сервис магазина:
    /// - Держит список товаров
    /// - Покупка кораблей за Tokens (добавляет в OwnedShips)
    /// - Покупка ресурсов за Tokens
    /// - Даёт Tokens за rewarded-рекламу
    /// - Сохраняет профиль через GironoidApp.ProfileService (без reflection-магии)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopServiceBehaviour : MonoBehaviour
    {
        [Header("External (optional)")]
        [Tooltip("Можно оставить пустым. Магазин использует GironoidApp.ProfileService напрямую.")]
        [SerializeField] private UnityEngine.Object _profileService;

        [Tooltip("AdsServiceBehaviour, который реально показывает рекламу.")]
        [SerializeField] private AdsServiceBehaviour _ads;

        [Header("Rewarded settings")]
        [SerializeField] private int _rewardedTokensAmount = 10;
        [SerializeField] private string _rewardedPlacement = "shop_reward_tokens";
        [SerializeField, Range(60f, 1800f)] private float _rewardedCooldownSeconds = 300f;
        [SerializeField] private bool _disableRewardedInGameplay = true;

        [Header("Interstitial settings")]
        [SerializeField] private bool _showInterstitialOnClose = true;
        [SerializeField] private string _interstitialPlacement = "shop_close";

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = false;

        private readonly List<ShopProduct> _products = new List<ShopProduct>(32);
        private bool _builtFromConfig;
        private long _lastRewardedGrantedUnixSeconds;
        private const string RewardedCooldownPrefKey = "gyronoid_rewarded_last_unix";

        public event Action<PlayerProfile> OnProfileChanged;

        public IReadOnlyList<ShopProduct> Products => _products;

        public bool ShowInterstitialOnClose => _showInterstitialOnClose;
        public string InterstitialPlacement => _interstitialPlacement;

        public int RewardedTokensAmount => _rewardedTokensAmount;
        public string RewardedPlacement => _rewardedPlacement;

        private void Awake()
        {
            if (_ads == null)
                _ads = FindObjectOfType<AdsServiceBehaviour>(true);

            try
            {
                var s = PlayerPrefs.GetString(RewardedCooldownPrefKey, "0");
                long.TryParse(s, out _lastRewardedGrantedUnixSeconds);
            }
            catch
            {
                _lastRewardedGrantedUnixSeconds = 0;
            }

            // базовый список (минимум) — пока конфиг может быть ещё не готов
            BuildFallbackProducts();
        }

        private void Update()
        {
            EnsureProductsUpToDate();
        }

        public void EnsureProductsUpToDate()
        {
            if (_builtFromConfig) return;
            if (!GironoidApp.IsReady) return;

            var cfg = GironoidApp.Config;
            if (cfg == null || cfg.ShipCatalog == null)
                return;

            BuildProductsFromConfig(cfg);
            _builtFromConfig = true;

            LogVerbose("Products rebuilt from config (ships + packs).");
        }

        public bool TryGetProfile(out PlayerProfile profile)
        {
            profile = null;
            if (!GironoidApp.IsReady) return false;

            profile = GironoidApp.Profile;
            return profile != null;
        }

        public int GetTokensSafe()
        {
            if (!TryGetProfile(out var profile) || profile == null) return 0;
            return Mathf.Max(0, profile.Tokens);
        }

        public void Buy(ShopProductId id, Action<ShopPurchaseResult> done)
        {
            EnsureProductsUpToDate();

            var p = _products.FirstOrDefault(x => x.Id == id);
            if (p.Id == ShopProductId.None)
            {
                done?.Invoke(ShopPurchaseResult.Fail("product_not_found"));
                return;
            }

            if (!GironoidApp.IsReady || GironoidApp.ProfileService == null || GironoidApp.Profile == null)
            {
                done?.Invoke(ShopPurchaseResult.Fail("profile_not_available"));
                return;
            }

            if (p.Kind == ShopProductKind.RewardedTokens)
            {
                TryGrantRewardedTokens(done);
                return;
            }

            var profile = GironoidApp.Profile;

            if (p.PriceTokens <= 0)
            {
                done?.Invoke(ShopPurchaseResult.Fail("invalid_price"));
                return;
            }

            if (profile.Tokens < p.PriceTokens)
            {
                done?.Invoke(ShopPurchaseResult.Fail("not_enough_tokens"));
                return;
            }

            // Покупка корабля
            if (p.Kind == ShopProductKind.Ship)
            {
                var shipId = p.PayloadId ?? "";
                if (string.IsNullOrEmpty(shipId))
                {
                    done?.Invoke(ShopPurchaseResult.Fail("ship_not_found"));
                    return;
                }

                if (profile.PlayerLevel < Mathf.Max(1, p.RequiredPlayerLevel))
                {
                    done?.Invoke(ShopPurchaseResult.Fail("locked"));
                    return;
                }

                if (profile.GetShip(shipId) != null)
                {
                    done?.Invoke(ShopPurchaseResult.Fail("already_owned"));
                    return;
                }

                var flush = ShouldFlushToServer();

                GironoidApp.ProfileService.Apply(pr =>
                {
                    pr.Tokens -= p.PriceTokens;
                    pr.GetOrCreateShip(shipId);

                    if (string.IsNullOrEmpty(pr.SelectedShipId))
                        pr.SelectedShipId = shipId;
                }, flushToServer: flush);

                OnProfileChanged?.Invoke(GironoidApp.Profile);
                done?.Invoke(ShopPurchaseResult.Ok(p));
                return;
            }

            // Покупка паков ресурсов
            {
                var flush = ShouldFlushToServer();

                GironoidApp.ProfileService.Apply(pr =>
                {
                    pr.Tokens -= p.PriceTokens;

                    if (p.AddIron > 0) pr.Iron += p.AddIron;
                    if (p.AddCopper > 0) pr.Copper += p.AddCopper;
                    if (p.AddSilver > 0) pr.Silver += p.AddSilver;
                }, flushToServer: flush);

                OnProfileChanged?.Invoke(GironoidApp.Profile);
                done?.Invoke(ShopPurchaseResult.Ok(p));
            }
        }

        public void TryGrantRewardedTokens(Action<ShopPurchaseResult> done)
        {
            if (_ads == null)
            {
                done?.Invoke(ShopPurchaseResult.Fail("ads_service_missing"));
                return;
            }

            if (!GironoidApp.IsReady || GironoidApp.ProfileService == null || GironoidApp.Profile == null)
            {
                done?.Invoke(ShopPurchaseResult.Fail("profile_not_available"));
                return;
            }

            if (_disableRewardedInGameplay && IsGameplaySceneActive())
            {
                done?.Invoke(ShopPurchaseResult.Fail("rewarded_not_allowed_in_gameplay"));
                return;
            }

            if (!IsRewardedCooldownReady())
            {
                done?.Invoke(ShopPurchaseResult.Fail("rewarded_cooldown"));
                return;
            }

            _ads.ShowRewarded(
                _rewardedPlacement,
                onRewarded: () =>
                {
                    int add = Mathf.Max(0, _rewardedTokensAmount);
                    var flush = ShouldFlushToServer();

                    _lastRewardedGrantedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    try
                    {
                        PlayerPrefs.SetString(RewardedCooldownPrefKey, _lastRewardedGrantedUnixSeconds.ToString());
                        PlayerPrefs.Save();
                    }
                    catch
                    {
                        // ignore
                    }

                    GironoidApp.ProfileService.Apply(pr =>
                    {
                        pr.Tokens += add;
                    }, flushToServer: flush);

                    OnProfileChanged?.Invoke(GironoidApp.Profile);
                    done?.Invoke(ShopPurchaseResult.Ok(ShopProduct.Rewarded(add)));
                },
                onClosed: () =>
                {
                    // закрытие без награды — ок
                },
                onError: err =>
                {
                    done?.Invoke(ShopPurchaseResult.Fail(err ?? "rewarded_error"));
                }
            );
        }

        public void ShowInterstitialIfAllowed(Action onClosed, Action<string> onError)
        {
            if (_ads == null)
            {
                onError?.Invoke("ads_service_missing");
                return;
            }

            _ads.ShowInterstitial(_interstitialPlacement, onClosed, onError);
        }

        // --------------------------- Products ---------------------------

        private void BuildFallbackProducts()
        {
            _products.Clear();

            // Rewarded — токены за просмотр рекламы
            _products.Add(ShopProduct.RewardedTokens("Токены за рекламу", "Посмотрите видео и получите токены.", 0));

            // Fallback starter ship (на случай, если конфиг ещё не готов).
            _products.Add(ShopProduct.Ship(
                ShipIdToProductId("AEGIS"),
                "Эгида",
                "Треб. уровень: 1",
                priceTokens: 50,
                shipId: "AEGIS",
                requiredPlayerLevel: 1));

            // fallback packs
            _products.Add(ShopProduct.Pack(ShopProductId.BuyIronPack, "Железо", "Набор железа для крафта и улучшений.", priceTokens: 35, addIron: 120, addCopper: 0, addSilver: 0));
            _products.Add(ShopProduct.Pack(ShopProductId.BuyCopperPack, "Медь", "Набор меди для крафта и улучшений.", priceTokens: 35, addIron: 0, addCopper: 120, addSilver: 0));
            _products.Add(ShopProduct.Pack(ShopProductId.BuySilverPack, "Серебро", "Редкий ресурс для апгрейдов.", priceTokens: 55, addIron: 0, addCopper: 0, addSilver: 18));
            _products.Add(ShopProduct.Pack(ShopProductId.BuyMixedPack, "Смешанный набор", "Понемногу всего — удобно в начале.", priceTokens: 60, addIron: 90, addCopper: 90, addSilver: 10));
        }

        private void BuildProductsFromConfig(GameConfig cfg)
        {
            _products.Clear();

            // Rewarded — токены за просмотр рекламы
            _products.Add(ShopProduct.RewardedTokens("Токены за рекламу", "Посмотрите видео и получите токены.", 0));

            // 1) Ships
            var sc = cfg != null ? cfg.ShipCatalog : null;
            bool hasAnyShipProduct = false;
            if (sc != null && sc.Ships != null)
            {
                for (int i = 0; i < sc.Ships.Length; i++)
                {
                    var def = sc.Ships[i];
                    if (string.IsNullOrEmpty(def.Id))
                        continue;

                    var pid = ShipIdToProductId(def.Id);
                    if (pid == ShopProductId.None)
                        pid = DynamicShipProductId(def.Id, i);

                    int price = ResolveShipPriceTokens(sc, def, i);
                    string title = string.IsNullOrWhiteSpace(def.NameRu) ? def.Id : def.NameRu;

                    // описание: требования + краткие статы Mk1 (если есть)
                    string desc = $"Треб. уровень: {Mathf.Max(1, def.UnlockPlayerLevel)}";
                    if (sc.TryGetTier(def.Id, 1, out var tier))
                    {
                        desc += $"\nHP {tier.Stats.Hull} • Speed {tier.Stats.Speed:0.0} • Accel {tier.Stats.Acceleration:0.0}";
                    }

                    _products.Add(ShopProduct.Ship(
                        pid,
                        title,
                        desc,
                        priceTokens: price,
                        shipId: def.Id,
                        requiredPlayerLevel: Mathf.Max(1, def.UnlockPlayerLevel)
                    ));

                    hasAnyShipProduct = true;
                }
            }

            if (!hasAnyShipProduct)
            {
                string starterId = (sc != null && !string.IsNullOrWhiteSpace(sc.StarterShipId))
                    ? sc.StarterShipId.Trim()
                    : "AEGIS";

                int starterPrice = 50;
                int starterReqLevel = 1;
                string starterName = starterId;
                string starterDesc = "Треб. уровень: 1";

                if (sc != null && sc.TryGet(starterId, out var starterDef))
                {
                    starterPrice = ResolveShipPriceTokens(sc, starterDef, 0);
                    starterReqLevel = Mathf.Max(1, starterDef.UnlockPlayerLevel);
                    starterName = string.IsNullOrWhiteSpace(starterDef.NameRu) ? starterDef.Id : starterDef.NameRu;
                    starterDesc = $"Треб. уровень: {starterReqLevel}";
                }

                _products.Add(ShopProduct.Ship(
                    DynamicShipProductId(starterId, 0),
                    starterName,
                    starterDesc,
                    priceTokens: starterPrice,
                    shipId: starterId,
                    requiredPlayerLevel: starterReqLevel));
            }

            // 2) Resource packs
            _products.Add(ShopProduct.Pack(ShopProductId.BuyIronPack, "Железо", "Набор железа для крафта и улучшений.", priceTokens: 35, addIron: 120, addCopper: 0, addSilver: 0));
            _products.Add(ShopProduct.Pack(ShopProductId.BuyCopperPack, "Медь", "Набор меди для крафта и улучшений.", priceTokens: 35, addIron: 0, addCopper: 120, addSilver: 0));
            _products.Add(ShopProduct.Pack(ShopProductId.BuySilverPack, "Серебро", "Редкий ресурс для апгрейдов.", priceTokens: 55, addIron: 0, addCopper: 0, addSilver: 18));
            _products.Add(ShopProduct.Pack(ShopProductId.BuyMixedPack, "Смешанный набор", "Понемногу всего — удобно в начале.", priceTokens: 60, addIron: 90, addCopper: 90, addSilver: 10));
        }

        private static ShopProductId ShipIdToProductId(string shipId)
        {
            if (string.IsNullOrEmpty(shipId)) return ShopProductId.None;

            switch (shipId.Trim().ToUpperInvariant())
            {
                case "AEGIS": return ShopProductId.BuyShip_AEGIS;
                case "ASTRA": return ShopProductId.BuyShip_ASTRA;
                case "HELIX": return ShopProductId.BuyShip_HELIX;
                case "NOVAFRAME": return ShopProductId.BuyShip_NOVAFRAME;
                case "IONLANCER": return ShopProductId.BuyShip_IONLANCER;
                case "RAILWING": return ShopProductId.BuyShip_RAILWING;
                case "CORERUNNER": return ShopProductId.BuyShip_CORERUNNER;
                default: return ShopProductId.None;
            }
        }

        private static ShopProductId DynamicShipProductId(string shipId, int index)
        {
            unchecked
            {
                int hash = 17;
                string src = string.IsNullOrWhiteSpace(shipId) ? $"SHIP_{index}" : shipId.Trim().ToUpperInvariant();
                for (int i = 0; i < src.Length; i++)
                    hash = (hash * 31) + src[i];

                hash ^= (index + 1) * 397;
                int raw = 1000 + Mathf.Abs(hash % 1000000);
                return (ShopProductId)raw;
            }
        }

        private static bool ShouldFlushToServer()
        {
            var y = GironoidApp.Yandex;
            return y != null && y.CanUseCloud;
        }

        private bool IsRewardedCooldownReady()
        {
            if (_rewardedCooldownSeconds <= 0f)
                return true;

            if (_lastRewardedGrantedUnixSeconds <= 0)
                return true;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var elapsed = now - _lastRewardedGrantedUnixSeconds;
            return elapsed >= (long)Mathf.CeilToInt(_rewardedCooldownSeconds);
        }

        private static int ResolveShipPriceTokens(ShipCatalog catalog, ShipDefinition def, int index)
        {
            if (catalog != null &&
                !string.IsNullOrEmpty(catalog.StarterShipId) &&
                string.Equals(def.Id, catalog.StarterShipId, StringComparison.OrdinalIgnoreCase))
            {
                return 50;
            }

            if (def.PriceTokens > 0)
                return def.PriceTokens;

            // Safety fallback for old assets where PriceTokens wasn't serialized yet.
            return Mathf.Max(100, 300 + Mathf.Max(0, index) * 250);
        }

        private static bool IsGameplaySceneActive()
        {
            var active = SceneManager.GetActiveScene().name ?? "";
            if (active.Length == 0)
                return false;

            if (string.Equals(active, "50_Gameplay", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                if (string.Equals(active, GironoidScenes.Gameplay, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // ignore
            }

            return active.IndexOf("Gameplay", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogVerbose(string msg)
        {
            if (_verboseLogs)
                Debug.Log($"[ShopServiceBehaviour] {msg}", this);
        }
    }

    public enum ShopProductKind
    {
        None = 0,
        RewardedTokens = 1,
        TokenPurchase = 2,
        Ship = 3
    }

    public enum ShopProductId
    {
        None = 0,
        RewardedTokens = 1,

        // Resource packs
        BuyIronPack = 2,
        BuyCopperPack = 3,
        BuySilverPack = 4,
        BuyMixedPack = 5,

        // Ships (from ShipCatalog)
        BuyShip_AEGIS = 100,
        BuyShip_ASTRA = 101,
        BuyShip_HELIX = 102,
        BuyShip_NOVAFRAME = 103,
        BuyShip_IONLANCER = 104,
        BuyShip_RAILWING = 105,
        BuyShip_CORERUNNER = 106,
    }

    [Serializable]
    public struct ShopProduct
    {
        public ShopProductId Id;
        public ShopProductKind Kind;

        public string Title;
        public string Description;

        public int PriceTokens;

        // Resource packs
        public int AddTokens;
        public int AddIron;
        public int AddCopper;
        public int AddSilver;

        // NEW: payload for ships (и на будущее для других товаров)
        public string PayloadId;

        // NEW: требования
        public int RequiredPlayerLevel;

        public static ShopProduct RewardedTokens(string title, string desc, int tokensAmount)
        {
            return new ShopProduct
            {
                Id = ShopProductId.RewardedTokens,
                Kind = ShopProductKind.RewardedTokens,
                Title = title,
                Description = desc,
                PriceTokens = 0,
                AddTokens = tokensAmount,
                AddIron = 0,
                AddCopper = 0,
                AddSilver = 0,
                PayloadId = "",
                RequiredPlayerLevel = 1
            };
        }

        public static ShopProduct Rewarded(int tokensAmount)
        {
            return new ShopProduct
            {
                Id = ShopProductId.RewardedTokens,
                Kind = ShopProductKind.RewardedTokens,
                Title = "Rewarded",
                Description = "Rewarded tokens",
                PriceTokens = 0,
                AddTokens = tokensAmount,
                AddIron = 0,
                AddCopper = 0,
                AddSilver = 0,
                PayloadId = "",
                RequiredPlayerLevel = 1
            };
        }

        public static ShopProduct Pack(ShopProductId id, string title, string desc, int priceTokens, int addIron, int addCopper, int addSilver)
        {
            return new ShopProduct
            {
                Id = id,
                Kind = ShopProductKind.TokenPurchase,
                Title = title,
                Description = desc,
                PriceTokens = Mathf.Max(0, priceTokens),
                AddTokens = 0,
                AddIron = Mathf.Max(0, addIron),
                AddCopper = Mathf.Max(0, addCopper),
                AddSilver = Mathf.Max(0, addSilver),
                PayloadId = "",
                RequiredPlayerLevel = 1
            };
        }

        public static ShopProduct Ship(ShopProductId id, string title, string desc, int priceTokens, string shipId, int requiredPlayerLevel)
        {
            return new ShopProduct
            {
                Id = id,
                Kind = ShopProductKind.Ship,
                Title = title,
                Description = desc,
                PriceTokens = Mathf.Max(0, priceTokens),
                AddTokens = 0,
                AddIron = 0,
                AddCopper = 0,
                AddSilver = 0,
                PayloadId = shipId ?? "",
                RequiredPlayerLevel = Mathf.Max(1, requiredPlayerLevel)
            };
        }
    }

    public struct ShopPurchaseResult
    {
        public bool Success;
        public string Error;
        public ShopProduct Product;

        public static ShopPurchaseResult Ok(ShopProduct p)
        {
            return new ShopPurchaseResult
            {
                Success = true,
                Error = null,
                Product = p
            };
        }

        public static ShopPurchaseResult Fail(string error)
        {
            return new ShopPurchaseResult
            {
                Success = false,
                Error = error,
                Product = default
            };
        }
    }
}
