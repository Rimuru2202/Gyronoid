// Assets/Gironoid/_Project/Code/UI/Shared/ShopModalController.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core.Services;

// OPTIONAL: если GironoidApp есть в проекте (почти наверняка есть).
// Мы используем его ТОЛЬКО через reflection-safe участки (не ломающие сборку из-за сигнатур).
using Gironoid._Project.Code.Core;

namespace Gironoid._Project.Code.UI.Shared
{
    /// <summary>
    /// UI Toolkit контроллер модального магазина.
    ///
    /// Поддерживает ДВА варианта разметки:
    /// A) "простая" (старая):
    /// - shopModal (VisualElement), btnShopClose (Button), lblShopTokens (Label), shopList (VisualElement)
    /// - btnWatchAdTokens (Button, optional), lblShopStatus (Label, optional)
    ///
    /// B) "экран магазина" (ваш текущий UXML), который можно вложить внутрь shopModal:
    /// - shopModal (VisualElement) [в ангаре]
    /// - (внутри) ListView shopShipsList
    /// - lblShopBalance (Label) баланс
    /// - btnShopBuy (Button)
    /// - btnWatchAd (Button)
    /// - lblShopShipName / lblShopShipReq / lblShopShipStats / lblShopShipPrice / lblShopShipStatus (Label, optional)
    /// - btnBack можно использовать как Close (если btnShopClose отсутствует)
    /// </summary>
    public sealed class ShopModalController
    {
        // ---- Modal root ----
        private const string NameModalRoot = "shopModal";

        // ---- Close button (preferred) ----
        private const string NameBtnClose = "btnShopClose";
        // fallback for your Shop UXML topbar
        private const string NameBtnBack = "btnBack";

        // ---- Tokens label ----
        private const string NameLblTokensSimple = "lblShopTokens";
        private const string NameLblBalance = "lblShopBalance";

        // ---- Simple list container (old) ----
        private const string NameListSimple = "shopList";
        private const string NameLblStatusSimple = "lblShopStatus";
        private const string NameBtnWatchAdSimple = "btnWatchAdTokens";

        // ---- Advanced shop UXML (your current) ----
        private const string NameShopScreenRoot = "shopScreen";
        private const string NameShipsList = "shopShipsList";
        private const string NameBtnBuy = "btnShopBuy";
        private const string NameBtnWatchAd = "btnWatchAd";
        private const string NameLblShipName = "lblShopShipName";
        private const string NameLblShipReq = "lblShopShipReq";
        private const string NameLblShipStats = "lblShopShipStats";
        private const string NameLblShipPrice = "lblShopShipPrice";
        private const string NameLblShipStatus = "lblShopShipStatus";

        // ---- Modal overlay classes (from your UXML) ----
        private const string ClassModalBackground = "modal-bg";
        private const string ClassModalBackdrop = "modal-backdrop";
        private const string ClassModalWindow = "modal-window";
        private const string ClassIsOpen = "is-open";

        // ---- State ----
        private VisualElement _uiRoot;
        private VisualElement _modalRoot;

        private Button _btnClose;
        private Label _lblTokensOrBalance;

        // Simple mode
        private VisualElement _simpleList;
        private Button _btnWatchAdSimple;
        private Label _lblStatusSimple;

        // Advanced mode
        private VisualElement _shopScreenRoot;
        private ListView _shipsList;
        private Button _btnBuy;
        private Button _btnWatchAd;

        private Label _lblShipName;
        private Label _lblShipReq;
        private Label _lblShipStats;
        private Label _lblShipPrice;
        private Label _lblShipStatus;

        // Overlay specifics (for hangar modal)
        private VisualElement _modalBg;
        private VisualElement _modalBackdrop;
        private VisualElement _modalWindow;
        private bool _overlayConfigured;

        private ShopServiceBehaviour _shop;
        private bool _useAdvanced;
        private bool _isBusy;

        private readonly List<ProductVm> _vms = new List<ProductVm>(32);
        private ProductVm _selected;

        public bool IsBound { get; private set; }
        public bool IsOpen { get; private set; }

        public void Bind(VisualElement uiRoot, ShopServiceBehaviour shopService)
        {
            _uiRoot = uiRoot;
            _shop = shopService;

            // 1) Ищем корень модалки (для ангара это обязательно).
            // 2) Если его нет, но есть shopScreen/shopShipsList — значит это отдельный экран магазина (разрешаем).
            _modalRoot = _uiRoot?.Q<VisualElement>(NameModalRoot);
            if (_modalRoot == null)
            {
                // fallback: отдельный экран магазина (ваш UXML)
                var screen = _uiRoot?.Q<VisualElement>(NameShopScreenRoot);
                var list = _uiRoot?.Q<ListView>(NameShipsList);
                if (screen != null || list != null)
                {
                    _modalRoot = screen ?? _uiRoot; // трактуем как "открыто всегда"
                }
                else
                {
                    Debug.LogError($"[ShopModalController] UXML element '{NameModalRoot}' not found (and no shop screen markers found).");
                    IsBound = false;
                    return;
                }
            }

            // ---- Common bindings ----
            _btnClose = _modalRoot.Q<Button>(NameBtnClose) ?? _modalRoot.Q<Button>(NameBtnBack);
            _lblTokensOrBalance = _modalRoot.Q<Label>(NameLblBalance) ?? _modalRoot.Q<Label>(NameLblTokensSimple);

            if (_btnClose != null)
                _btnClose.clicked += Close;

            // ---- Overlay bind (only if it's actually a hangar modal) ----
            BindOverlayPartsIfAny();
            EnsureOverlayConfiguredIfNeeded();

            // ---- Try advanced layout ----
            _shopScreenRoot = _modalRoot.Q<VisualElement>(NameShopScreenRoot) ?? _modalRoot; // в модалке shopScreen может быть вложен или отсутствовать
            _shipsList = _shopScreenRoot.Q<ListView>(NameShipsList);
            _btnBuy = _shopScreenRoot.Q<Button>(NameBtnBuy);
            _btnWatchAd = _shopScreenRoot.Q<Button>(NameBtnWatchAd);

            _lblShipName = _shopScreenRoot.Q<Label>(NameLblShipName);
            _lblShipReq = _shopScreenRoot.Q<Label>(NameLblShipReq);
            _lblShipStats = _shopScreenRoot.Q<Label>(NameLblShipStats);
            _lblShipPrice = _shopScreenRoot.Q<Label>(NameLblShipPrice);
            _lblShipStatus = _shopScreenRoot.Q<Label>(NameLblShipStatus);

            _useAdvanced = (_shipsList != null && _btnBuy != null);
            if (_useAdvanced)
            {
                SetupAdvancedList();
                if (_btnBuy != null) _btnBuy.clicked += OnBuySelectedClicked;
                if (_btnWatchAd != null) _btnWatchAd.clicked += OnWatchAdClickedAdvanced;
            }

            // ---- Simple layout fallback ----
            _simpleList = _modalRoot.Q<VisualElement>(NameListSimple);
            _btnWatchAdSimple = _modalRoot.Q<Button>(NameBtnWatchAdSimple);
            _lblStatusSimple = _modalRoot.Q<Label>(NameLblStatusSimple);

            if (!_useAdvanced)
            {
                if (_btnWatchAdSimple != null) _btnWatchAdSimple.clicked += OnWatchAdClickedSimple;
            }

            if (_shop != null)
                _shop.OnProfileChanged += OnProfileChanged;

            // Первичная отрисовка
            RebuildUi();
            RefreshTokens();
            SetStatus(null);

            // Для модалки в ангаре — стартуем скрытой
            if (_uiRoot != null && _uiRoot.Q<VisualElement>(NameModalRoot) != null)
            {
                EnsureOverlayConfiguredIfNeeded();
                _modalRoot.RemoveFromClassList(ClassIsOpen);
                _modalRoot.style.display = DisplayStyle.None;
                IsOpen = false;
            }
            else
            {
                // отдельный экран: считаем открытым
                IsOpen = true;
            }

            IsBound = true;
        }

        public void Open()
        {
            if (!IsBound) return;

            RebuildUi();
            RefreshTokens();
            SetStatus(null);

            // Если это модалка — показываем. Если это экран — он и так видим.
            if (_uiRoot != null && _uiRoot.Q<VisualElement>(NameModalRoot) != null)
            {
                EnsureOverlayConfiguredIfNeeded();
                BringModalToFront();
                _modalRoot.AddToClassList(ClassIsOpen);
                _modalRoot.style.display = DisplayStyle.Flex;
                IsOpen = true;
            }
        }

        public void Close()
        {
            if (!IsBound) return;

            // перед закрытием — форсируем сохранение (безопасно)
            TryForceCloudFlush();

            // По желанию — interstitial при закрытии магазина
            if (_shop != null && _shop.ShowInterstitialOnClose)
            {
                _shop.ShowInterstitialIfAllowed(
                    onClosed: () => { },
                    onError: err => { }
                );
            }

            // Если модалка — скрываем. Если это отдельный экран — просто игнорируем.
            if (_uiRoot != null && _uiRoot.Q<VisualElement>(NameModalRoot) != null)
            {
                _modalRoot.RemoveFromClassList(ClassIsOpen);
                _modalRoot.style.display = DisplayStyle.None;
                IsOpen = false;
            }
        }

        public void Dispose()
        {
            if (_btnClose != null) _btnClose.clicked -= Close;
            if (_modalBackdrop != null) _modalBackdrop.UnregisterCallback<PointerDownEvent>(OnBackdropPointerDown);

            if (_useAdvanced)
            {
                if (_btnBuy != null) _btnBuy.clicked -= OnBuySelectedClicked;
                if (_btnWatchAd != null) _btnWatchAd.clicked -= OnWatchAdClickedAdvanced;
                if (_shipsList != null) _shipsList.selectionChanged -= OnSelectionChanged;
            }
            else
            {
                if (_btnWatchAdSimple != null) _btnWatchAdSimple.clicked -= OnWatchAdClickedSimple;
            }

            if (_shop != null) _shop.OnProfileChanged -= OnProfileChanged;

            IsBound = false;
            IsOpen = false;
        }

        // ------------------- Build / Refresh -------------------

        private void RebuildUi()
        {
            if (_shop == null)
            {
                SetStatus("Магазин недоступен (ShopServiceBehaviour не найден).");
                SetBuyEnabled(false);
                return;
            }

            if (_useAdvanced)
            {
                BuildAdvancedList();
                EnsureSelection();
                RefreshDetails();
            }
            else
            {
                BuildSimpleList();
            }
        }

        private void RefreshTokens()
        {
            if (_lblTokensOrBalance == null) return;

            int tokens = 0;
            if (_shop != null) tokens = _shop.GetTokensSafe();

            // В вашем UXML: "Жетоны: --"
            if (_lblTokensOrBalance.name == NameLblBalance)
                _lblTokensOrBalance.text = $"Жетоны: {tokens}";
            else
                _lblTokensOrBalance.text = tokens.ToString();
        }

        private void SetStatus(string text)
        {
            // Advanced: выводим в lblShopShipStatus (если есть), иначе в simple lblShopStatus
            var target = _lblShipStatus != null ? _lblShipStatus : _lblStatusSimple;
            if (target == null) return;

            if (string.IsNullOrWhiteSpace(text))
            {
                target.text = "";
                target.style.display = DisplayStyle.None;
            }
            else
            {
                target.text = text;
                target.style.display = DisplayStyle.Flex;
            }
        }

        private void SetBuyEnabled(bool enabled)
        {
            if (_btnBuy != null) _btnBuy.SetEnabled(enabled);
        }

        // ------------------- Advanced layout (ListView + details) -------------------

        private void SetupAdvancedList()
        {
            _shipsList.itemsSource = _vms;
            _shipsList.selectionType = SelectionType.Single;
            _shipsList.fixedItemHeight = 64;

            _shipsList.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.paddingLeft = 12;
                row.style.paddingRight = 12;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;

                var title = new Label { name = "title" };
                title.style.unityFontStyleAndWeight = FontStyle.Bold;

                var sub = new Label { name = "subtitle" };
                sub.style.opacity = 0.85f;

                row.Add(title);
                row.Add(sub);
                return row;
            };

            _shipsList.bindItem = (ve, i) =>
            {
                if ((uint)i >= (uint)_vms.Count) return;
                var vm = _vms[i];
                var title = ve.Q<Label>("title");
                var sub = ve.Q<Label>("subtitle");
                if (title != null) title.text = vm.Title;
                if (sub != null) sub.text = vm.Subtitle;
            };

            _shipsList.selectionChanged += OnSelectionChanged;
        }

        private void BuildAdvancedList()
        {
            _vms.Clear();
            if (_shop == null) return;

            var products = _shop.Products;
            for (int i = 0; i < products.Count; i++)
            {
                ShopProduct p = products[i];

                // Rewarded показываем отдельной кнопкой btnWatchAd (если она есть),
                // поэтому в список кораблей можно не добавлять.
                if (p.Kind == ShopProductKind.RewardedTokens) continue;

                var price = Mathf.Max(0, p.PriceTokens);
                var title = string.IsNullOrWhiteSpace(p.Title) ? "Товар" : p.Title;

                _vms.Add(new ProductVm
                {
                    Id = p.Id,
                    Kind = p.Kind,
                    Title = title,
                    Description = p.Description ?? "",
                    PriceTokens = price,
                    Subtitle = price <= 0 ? "Бесплатно" : $"Цена: {price}"
                });
            }

            try { _shipsList?.Rebuild(); }
            catch { try { _shipsList?.RefreshItems(); } catch { } }
        }

        private void EnsureSelection()
        {
            if (_shipsList == null) return;

            if (_selected.IsValid)
            {
                var idx = IndexOf(_selected.Id);
                if (idx >= 0)
                {
                    // Unity 6: SetSelectionWithoutNotify ожидает IEnumerable<int>
                    try { _shipsList.SetSelectionWithoutNotify(new[] { idx }); } catch { }
                    return;
                }
            }

            if (_vms.Count > 0)
            {
                _selected = _vms[0];
                try { _shipsList.SetSelectionWithoutNotify(new[] { 0 }); } catch { }
            }
            else
            {
                _selected = default;
                try { _shipsList.ClearSelection(); } catch { }
            }
        }

        private void OnSelectionChanged(IEnumerable<object> selected)
        {
            _selected = default;
            if (selected != null)
            {
                foreach (var o in selected)
                {
                    if (o is ProductVm vm)
                    {
                        _selected = vm;
                        break;
                    }
                }
            }
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            RefreshTokens();

            if (!_selected.IsValid)
            {
                if (_lblShipName != null) _lblShipName.text = "Выберите корабль";
                if (_lblShipReq != null) _lblShipReq.text = "";
                if (_lblShipStats != null) _lblShipStats.text = "";
                if (_lblShipPrice != null) _lblShipPrice.text = "";
                SetStatus(null);
                SetBuyEnabled(false);
                return;
            }

            if (_lblShipName != null) _lblShipName.text = _selected.Title;
            if (_lblShipReq != null) _lblShipReq.text = ""; // оставлено под требования
            if (_lblShipStats != null) _lblShipStats.text = string.IsNullOrWhiteSpace(_selected.Description) ? "" : _selected.Description;
            if (_lblShipPrice != null) _lblShipPrice.text = _selected.PriceTokens <= 0 ? "Цена: бесплатно" : $"Цена: {_selected.PriceTokens}";

            // Простая логика доступности по токенам
            var tokens = _shop != null ? _shop.GetTokensSafe() : 0;
            bool canBuy = !_isBusy && (_selected.PriceTokens <= 0 || tokens >= _selected.PriceTokens);
            SetBuyEnabled(canBuy);

            if (!canBuy && !_isBusy && _selected.PriceTokens > 0 && tokens < _selected.PriceTokens)
                SetStatus("Недостаточно жетонов.");
            else
                SetStatus(null);
        }

        private void OnBuySelectedClicked()
        {
            if (_shop == null)
            {
                SetStatus("Магазин недоступен.");
                return;
            }

            if (!_selected.IsValid)
            {
                SetStatus("Выберите корабль.");
                return;
            }

            if (_isBusy) return;

            _isBusy = true;
            SetStatus(null);
            SetBuyEnabled(false);

            _shop.Buy(_selected.Id, result =>
            {
                _isBusy = false;

                if (!result.Success)
                {
                    SetStatus(MapError(result.Error));
                    RefreshTokens();
                    RefreshDetails();
                    return;
                }

                SetStatus("Покупка успешна. Сохранение…");
                RefreshTokens();

                // Важно: форсируем flush в облако после успешной покупки
                TryForceCloudFlush();

                // Перестроим список/детали
                RebuildUi();
                RefreshDetails();

                SetStatus("Покупка успешна.");
            });
        }

        private void OnWatchAdClickedAdvanced()
        {
            GrantRewardedTokens();
        }

        // ------------------- Simple layout fallback (programmatic rows) -------------------

        private void BuildSimpleList()
        {
            if (_simpleList == null || _shop == null) return;

            _simpleList.Clear();

            var products = _shop.Products;
            for (int i = 0; i < products.Count; i++)
            {
                ShopProduct p = products[i];

                // Rewarded можно оставить отдельной кнопкой (btnWatchAdTokens)
                if (p.Kind == ShopProductKind.RewardedTokens && _btnWatchAdSimple != null) continue;

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;
                row.style.marginBottom = 6;

                VisualElement left = new VisualElement();
                left.style.flexDirection = FlexDirection.Column;
                left.style.flexGrow = 1;

                Label title = new Label(p.Title ?? "Item");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;

                Label desc = new Label(p.Description ?? "");
                desc.style.opacity = 0.85f;

                left.Add(title);
                left.Add(desc);

                Button btnBuy = new Button();
                btnBuy.text = MakePriceText(p);
                btnBuy.clicked += () => OnBuyClickedSimple(p.Id);

                row.Add(left);
                row.Add(btnBuy);

                _simpleList.Add(row);
            }
        }

        private void OnBuyClickedSimple(ShopProductId id)
        {
            if (_shop == null)
            {
                SetStatus("Магазин недоступен.");
                return;
            }

            SetStatus(null);

            _shop.Buy(id, result =>
            {
                if (!result.Success)
                {
                    SetStatus(MapError(result.Error));
                    RefreshTokens();
                    return;
                }

                SetStatus("Покупка успешна. Сохранение…");
                RefreshTokens();
                TryForceCloudFlush();
                SetStatus("Покупка успешна.");

                BuildSimpleList();
            });
        }

        private void OnWatchAdClickedSimple()
        {
            GrantRewardedTokens();
        }

        private void GrantRewardedTokens()
        {
            if (_shop == null)
            {
                SetStatus("Реклама недоступна.");
                return;
            }

            SetStatus(null);

            _shop.TryGrantRewardedTokens(result =>
            {
                if (!result.Success)
                {
                    SetStatus(MapError(result.Error));
                    RefreshTokens();
                    return;
                }

                SetStatus($"Начислено: +{_shop.RewardedTokensAmount} токенов. Сохранение…");
                RefreshTokens();
                TryForceCloudFlush();
                SetStatus($"Начислено: +{_shop.RewardedTokensAmount} токенов.");
            });
        }

        private static string MakePriceText(ShopProduct p)
        {
            if (p.Kind == ShopProductKind.RewardedTokens) return "Смотреть";
            int price = Mathf.Max(0, p.PriceTokens);
            return price <= 0 ? "Купить" : $"Купить за {price}";
        }

        // ------------------- Events / Saving -------------------

        private void OnProfileChanged(Gironoid._Project.Code.Core.Profile.PlayerProfile profile)
        {
            RefreshTokens();
            if (_useAdvanced) RefreshDetails();
        }

        /// <summary>
        /// Гарантированный "пинок" сохранения:
        /// 1) Пытаемся вызвать ProfileService.Apply(no-op, flushToServer=true)
        /// 2) Если нет — пытаемся вызвать ProfileService.Save(flushToServer=true)
        /// Всё через reflection, чтобы не ломать сборку при отличающихся сигнатурах.
        /// </summary>
        private void TryForceCloudFlush()
        {
            try
            {
                object ps = null;
                var appType = typeof(GironoidApp);
                var prop = appType.GetProperty("ProfileService", BindingFlags.Public | BindingFlags.Static);
                if (prop != null) ps = prop.GetValue(null);
                if (ps == null) return;

                var psType = ps.GetType();
                var apply = FindApplyMethod(psType);
                if (apply != null)
                {
                    var action = MakeNoOpProfileAction(apply.GetParameters()[0].ParameterType);
                    if (action != null)
                    {
                        apply.Invoke(ps, new object[] { action, true });
                        return;
                    }
                }

                var save = FindSaveBoolMethod(psType);
                if (save != null)
                {
                    save.Invoke(ps, new object[] { true });
                    return;
                }
            }
            catch
            {
                // безопасно игнорируем: это лишь усиление надежности
            }
        }

        private static MethodInfo FindApplyMethod(Type psType)
        {
            var methods = psType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name != "Apply") continue;
                var p = m.GetParameters();
                if (p.Length != 2) continue;
                if (p[1].ParameterType != typeof(bool)) continue;
                return m;
            }
            return null;
        }

        private static MethodInfo FindSaveBoolMethod(Type psType)
        {
            var methods = psType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name != "Save") continue;
                var p = m.GetParameters();
                if (p.Length != 1) continue;
                if (p[0].ParameterType != typeof(bool)) continue;
                return m;
            }
            return null;
        }

        private static object MakeNoOpProfileAction(Type actionType)
        {
            try
            {
                var invoke = actionType.GetMethod("Invoke");
                if (invoke == null) return null;
                var args = invoke.GetParameters();
                if (args.Length != 1) return null;
                var paramType = args[0].ParameterType;

                var helper = typeof(ShopModalController).GetMethod(nameof(NoOpProfileGeneric), BindingFlags.NonPublic | BindingFlags.Static);
                if (helper == null) return null;

                var generic = helper.MakeGenericMethod(paramType);
                return Delegate.CreateDelegate(actionType, generic);
            }
            catch
            {
                return null;
            }
        }

        private static void NoOpProfileGeneric<T>(T profile) { }

        // ------------------- Error mapping -------------------

        private static string MapError(string err)
        {
            if (string.IsNullOrWhiteSpace(err)) return "Ошибка.";

            switch (err)
            {
                case "not_enough_tokens": return "Недостаточно токенов.";
                case "profile_not_available": return "Профиль недоступен.";
                case "save_failed": return "Не удалось сохранить профиль.";
                case "rewarded_not_available": return "Rewarded-реклама недоступна.";
                case "interstitial_not_available": return "Interstitial-реклама недоступна.";
                case "interstitial_cooldown": return "Рекламу можно показать позже.";
                case "ads_service_missing": return "AdsService не найден.";
                case "already_owned": return "Уже куплено.";
                default: return $"Ошибка: {err}";
            }
        }

        private int IndexOf(ShopProductId id)
        {
            for (int i = 0; i < _vms.Count; i++)
                if (_vms[i].Id.Equals(id))
                    return i;
            return -1;
        }

        private struct ProductVm
        {
            public ShopProductId Id;
            public ShopProductKind Kind;
            public string Title;
            public string Description;
            public int PriceTokens;
            public string Subtitle;
            public bool IsValid => !EqualityComparer<ShopProductId>.Default.Equals(Id, default);
        }

        // ------------------- Overlay / Modal correctness -------------------
        // Эти методы НИЧЕГО не меняют в API магазина, только чинят поведение "модалка ниже/не перекрывает".

        private bool IsHangarModal()
        {
            // Мы конфигурируем overlay ТОЛЬКО когда _modalRoot действительно shopModal из ангара.
            // В режиме "отдельный экран" трогать position/absolute нельзя.
            return _modalRoot != null && _uiRoot != null && _modalRoot.name == NameModalRoot && _uiRoot.Q<VisualElement>(NameModalRoot) != null;
        }

        private void BindOverlayPartsIfAny()
        {
            _modalBg = null;
            _modalBackdrop = null;
            _modalWindow = null;

            if (_modalRoot == null) return;

            // Ищем по class как в вашем UXML: modal-bg / modal-backdrop / modal-window
            _modalBg = _modalRoot.Q<VisualElement>(className: ClassModalBackground);
            _modalBackdrop = _modalRoot.Q<VisualElement>(className: ClassModalBackdrop);
            _modalWindow = _modalRoot.Q<VisualElement>(className: ClassModalWindow);

            if (_modalBackdrop != null)
            {
                // Чтобы клики не проходили "сквозь" модалку
                _modalBackdrop.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);
            }
        }

        private bool CanCloseNow()
        {
            // Если кнопка закрытия выключена (tutorial) — backdrop не закрывает.
            if (_btnClose == null) return true;
            return _btnClose.enabledSelf;
        }

        private void OnBackdropPointerDown(PointerDownEvent evt)
        {
            // Перехватываем ввод, чтобы ангар под модалкой не получал клики
            evt.StopImmediatePropagation();

            if (!CanCloseNow())
                return;

            Close();
        }

        private void EnsureOverlayConfiguredIfNeeded()
        {
            if (!IsHangarModal()) return;
            if (_overlayConfigured) return;

            // Корень должен быть "якорем" для absolute-детей.
            _uiRoot.style.position = Position.Relative;

            // Делаем модалку полноэкранным overlay.
            _modalRoot.style.position = Position.Absolute;
            _modalRoot.style.left = 0;
            _modalRoot.style.top = 0;
            _modalRoot.style.right = 0;
            _modalRoot.style.bottom = 0;

            // Важно: НЕ центрируем, а растягиваем (full-screen).
            _modalRoot.style.flexDirection = FlexDirection.Column;
            _modalRoot.style.alignItems = Align.Stretch;
            _modalRoot.style.justifyContent = Justify.FlexStart;

            // Гарантия перехвата ввода
            _modalRoot.pickingMode = PickingMode.Position;

            // Фоновая картинка (если есть)
            if (_modalBg != null)
            {
                _modalBg.style.position = Position.Absolute;
                _modalBg.style.left = 0;
                _modalBg.style.top = 0;
                _modalBg.style.right = 0;
                _modalBg.style.bottom = 0;
                _modalBg.pickingMode = PickingMode.Ignore;
            }

            if (_modalBackdrop != null)
            {
                _modalBackdrop.style.position = Position.Absolute;
                _modalBackdrop.style.left = 0;
                _modalBackdrop.style.top = 0;
                _modalBackdrop.style.right = 0;
                _modalBackdrop.style.bottom = 0;
                _modalBackdrop.pickingMode = PickingMode.Position;
            }

            if (_modalWindow != null)
            {
                // Окно поверх backdrop и занимает весь экран.
                _modalWindow.style.position = Position.Relative;
                _modalWindow.style.flexGrow = 1;
                _modalWindow.style.width = new Length(100, LengthUnit.Percent);
                _modalWindow.style.height = new Length(100, LengthUnit.Percent);
                _modalWindow.style.alignSelf = Align.Stretch;
            }

            _overlayConfigured = true;
        }

        private void BringModalToFront()
        {
            if (!IsHangarModal()) return;

            // Самый надежный способ быть "поверх" без z-index:
            // сделать элемент последним ребёнком в дереве.
            var parent = _modalRoot.parent;
            if (parent == null) return;

            parent.Remove(_modalRoot);
            parent.Add(_modalRoot);
        }
    }
}
