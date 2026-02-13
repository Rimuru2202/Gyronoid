using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core;
using Gironoid._Project.Code.Core.Config;
using Gironoid._Project.Code.Core.Profile;
using Gironoid._Project.Code.Core.Services;
using Gironoid._Project.Code.Core.Visual;
using Gironoid._Project.Code.UI.Shared;

namespace Gironoid._Project.Code.UI.Hangar
{
    [DisallowMultipleComponent]
    public sealed class HangarScreenController : MonoBehaviour
    {
        private const float UiTickInterval = 0.25f;

        [SerializeField] private UIDocument _ui;

        // OPTIONAL: если магазин сделан отдельным UIDocument (как у вас сейчас),
        // можно указать его здесь. Если не указано — попробуем найти автоматически.
        [Header("Optional external shop UI (separate UIDocument)")]
        [SerializeField] private UIDocument _shopUi;
        [SerializeField] private bool _autoFindExternalShopUi = true;

        private VisualElement _root;
        private TutorialOverlay _tutorial;
        private ShopModalController _shopModal;
        private ShopServiceBehaviour _shopService;

        // Modal root (when shop is embedded into Hangar UXML)
        private VisualElement _shopModalRoot;
        private Button _btnShopClose;

        // External shop root (when shop is separate UIDocument)
        private VisualElement _shopExternalRoot;
        private Button _btnShopExternalBack;
        private bool _externalShopOpen;

        private Button _btnBack;
        private Button _btnFinish;

        private Button _btnPrevShip;
        private Button _btnNextShip;
        private Button _btnUpgradeShip;

        private Button _btnShop;
        private Button _btnCraft;
        private Button _btnDismantle;

        // OPTIONAL (если добавите в UXML) — кнопка "Установить"
        private Button _btnEquip;

        private Label _lblTokens;
        private Label _lblShipName;
        private Label _lblShipMk;
        private Label _lblShipStats;
        private Label _lblShipAction;

        private Label _lblFilter;
        private Label _lblItemDetails;

        private ListView _inventoryList;

        private ShipVisualView _shipVisual;

        // Slot buttons (UXML: btnW0..btnW3, btnS0..btnS3, btnE0..btnE3, btnM0..btnM3)
        private readonly Button[] _weaponBtns = new Button[4];
        private readonly Button[] _shieldBtns = new Button[4];
        private readonly Button[] _engineBtns = new Button[4];
        private readonly Button[] _modifierBtns = new Button[4];

        private readonly Action[] _weaponBtnActions = new Action[4];
        private readonly Action[] _shieldBtnActions = new Action[4];
        private readonly Action[] _engineBtnActions = new Action[4];
        private readonly Action[] _modifierBtnActions = new Action[4];

        private readonly List<ItemVm> _items = new List<ItemVm>(128);
        private readonly List<ItemVm> _pool = new List<ItemVm>(128);

        private ItemVm _selectedItem;
        private SlotPick _selectedSlot;

        private ItemType _slotFilterType = ItemType.None;
        private int _slotFilterIndex = -1;

        private float _nextUiTick;
        private bool _inventorySetupDone;
        private bool _suppressInventorySelectionChanged;

        private int _lastTokens = int.MinValue;
        private int _lastIron = int.MinValue;
        private string _lastShipId;
        private int _lastShipMk = int.MinValue;

        // Важно: теперь _lastInventoryHash считается ПО ПРОФИЛЮ,
        // а сами _items перестраиваются только при изменениях.
        private int _lastInventoryHash;
        private int _lastEquippedHash;

        private ItemType _lastFilterType = (ItemType)(-999);
        private int _lastFilterIndex = int.MinValue;

        private readonly StringBuilder _sb = new StringBuilder(256);

        // NEW: защита от двойного клика "Готово"
        private bool _finishInProgress;

        // NEW: если GironoidApp еще не готов - мягкая отложенная инициализация
        private bool _deferredInitDone;

        private void Awake()
        {
            if (_ui == null) _ui = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (_ui == null) _ui = GetComponent<UIDocument>();
            if (_ui == null) return;

            _root = _ui.rootVisualElement;
            if (_root == null) return;

            BindUi(_root);
            BindSlotButtons(_root);

            CreateTutorialOverlay(_root);
            CreateShopModalOrExternal(_root);

            WireUi();

            SetupInventoryListOnce();
            EnsureInventorySelectionHook();

            // Визуализация корабля (SpriteRenderer-слой)
            _shipVisual = FindObjectOfType<ShipVisualView>();

            _selectedItem = null;
            _selectedSlot = default;
            ClearSlotFilter();

            _nextUiTick = 0f;
            _deferredInitDone = false;
            _externalShopOpen = false;

            InvalidateCaches();

            // Если App уже готов - делаем всё сразу. Если нет - отложим.
            if (GironoidApp.IsReady)
            {
                RunInitialFlow();
            }
            else
            {
                // Подготовим UI без доступа к данным — чтобы экран был живым
                RefreshAll(force: true);
                UpdateTutorialOverlay();
            }
        }

        private void RunInitialFlow()
        {
            // Нормализация (НЕ выдаём стартовый корабль/предметы)
            if (GironoidApp.IsReady && GironoidApp.Hangar != null)
                GironoidApp.Hangar.EnsureStarterKit();

            // Вход в ангар = начинаем онбординг
            SetTutorialStepAtLeast(1);

            RefreshAll(force: true);

            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void OnDisable()
        {
            UnwireUi();

            if (_inventoryList != null)
                _inventoryList.selectionChanged -= OnInventorySelectionChanged;

            UnbindSlotButtons();

            if (_tutorial != null)
            {
                _tutorial.Dispose();
                _tutorial = null;
            }

            if (_shopService != null)
                _shopService.OnProfileChanged -= OnShopProfileChanged;

            if (_shopModal != null)
            {
                _shopModal.Dispose();
                _shopModal = null;
            }

            if (_btnShopExternalBack != null)
                _btnShopExternalBack.clicked -= CloseExternalShop;

            _shopService = null;
            _shopModalRoot = null;
            _btnShopClose = null;

            _shopExternalRoot = null;
            _btnShopExternalBack = null;
            _externalShopOpen = false;
        }

        private void Update()
        {
            // Если приложению нужно время, чтобы поднять сервисы — подождём.
            if (!GironoidApp.IsReady)
                return;

            // Отложенная инициализация (один раз), если при OnEnable App не был ready.
            if (!_deferredInitDone)
            {
                _deferredInitDone = true;
                RunInitialFlow();
            }

            _nextUiTick -= Time.unscaledDeltaTime;
            if (_nextUiTick <= 0f)
            {
                _nextUiTick = UiTickInterval;
                RefreshAll(force: false);
                UpdateTutorialOverlay();
            }
        }

        private void BindUi(VisualElement root)
        {
            _btnBack = root.Q<Button>("btnBack");
            _btnFinish = root.Q<Button>("btnFinish");

            _btnPrevShip = root.Q<Button>("btnPrevShip");
            _btnNextShip = root.Q<Button>("btnNextShip");
            _btnUpgradeShip = root.Q<Button>("btnUpgradeShip");

            _btnShop = root.Q<Button>("btnShop");
            _btnCraft = root.Q<Button>("btnCraft");
            _btnDismantle = root.Q<Button>("btnDismantle");

            _btnEquip = root.Q<Button>("btnEquip"); // optional

            _lblTokens = root.Q<Label>("lblTokens");
            _lblShipName = root.Q<Label>("lblShipName");
            _lblShipMk = root.Q<Label>("lblShipMk");
            _lblShipStats = root.Q<Label>("lblShipStats");
            _lblShipAction = root.Q<Label>("lblShipAction");

            _lblFilter = root.Q<Label>("lblFilter");
            _lblItemDetails = root.Q<Label>("lblItemDetails");

            _inventoryList = root.Q<ListView>("inventoryList");
        }

        private void CreateTutorialOverlay(VisualElement root)
        {
            var layer = root.Q<VisualElement>("tutorialLayer");
            _tutorial = new TutorialOverlay(root, layer);
        }

        private void CreateShopModalOrExternal(VisualElement root)
        {
            // Ищем сервис магазина в сцене (может быть в Services).
            _shopService = FindObjectOfType<ShopServiceBehaviour>(true);

            // 1) Пытаемся привязать МОДАЛЬНЫЙ магазин (ожидается внутри Hangar UXML).
            _shopModal = new ShopModalController();
            _shopModal.Bind(root, _shopService);

            _shopModalRoot = root.Q<VisualElement>("shopModal");
            if (_shopModalRoot != null)
                _btnShopClose = _shopModalRoot.Q<Button>("btnShopClose");

            if (_shopService != null)
                _shopService.OnProfileChanged += OnShopProfileChanged;

            // 2) Если модалки в UXML нет — готовим EXTERNAL UIDocument магазина (UI_shop).
            if ((_shopModal == null || !_shopModal.IsBound) && (_shopUi == null) && _autoFindExternalShopUi)
            {
                _shopUi = TryFindExternalShopDocument();
            }

            if (_shopUi != null)
            {
                // Подготовим ссылки на "назад/закрыть" в экране магазина.
                _shopExternalRoot = _shopUi.rootVisualElement;

                // Пытаемся найти кнопку выхода с наиболее вероятными именами.
                _btnShopExternalBack =
                    _shopExternalRoot?.Q<Button>("btnBack")
                    ?? _shopExternalRoot?.Q<Button>("btnClose")
                    ?? _shopExternalRoot?.Q<Button>("btnShopClose");

                if (_btnShopExternalBack != null)
                    _btnShopExternalBack.clicked += CloseExternalShop;

                // По умолчанию держим внешний магазин выключенным, если он есть в сцене.
                // (Если вам нужно иначе — просто снимите SetActive в инспекторе/коде.)
                if (_shopUi.gameObject.activeSelf)
                    _shopUi.gameObject.SetActive(false);
            }
        }

        private UIDocument TryFindExternalShopDocument()
        {
            try
            {
                // Ищем любой UIDocument в сцене, кроме основного _ui,
                // который похож на магазин (по имени GO или VisualTreeAsset).
                var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>(true);
                for (int i = 0; i < docs.Length; i++)
                {
                    var d = docs[i];
                    if (d == null || d == _ui) continue;

                    var goName = d.gameObject != null ? d.gameObject.name : "";
                    var vtaName = d.visualTreeAsset != null ? d.visualTreeAsset.name : "";

                    if (!string.IsNullOrEmpty(goName) && goName.IndexOf("UI_shop", StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;

                    if (!string.IsNullOrEmpty(vtaName) && vtaName.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;
                }
            }
            catch { }

            return null;
        }

        private void OnShopProfileChanged(PlayerProfile p)
        {
            // Магазин мог изменить жетоны/ресурсы — обновляем UI и туториал.
            InvalidateCaches();
            RefreshAll(force: true);
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void WireUi()
        {
            if (_btnBack != null) _btnBack.clicked += OnBack;
            if (_btnFinish != null) _btnFinish.clicked += OnFinish;

            if (_btnPrevShip != null) _btnPrevShip.clicked += OnPrevShip;
            if (_btnNextShip != null) _btnNextShip.clicked += OnNextShip;
            if (_btnUpgradeShip != null) _btnUpgradeShip.clicked += OnUpgradeShip;

            if (_btnShop != null) _btnShop.clicked += OnShop;
            if (_btnCraft != null) _btnCraft.clicked += OnCraft;
            if (_btnDismantle != null) _btnDismantle.clicked += OnDismantle;

            if (_btnEquip != null) _btnEquip.clicked += OnEquipClicked;
        }

        private void UnwireUi()
        {
            if (_btnBack != null) _btnBack.clicked -= OnBack;
            if (_btnFinish != null) _btnFinish.clicked -= OnFinish;

            if (_btnPrevShip != null) _btnPrevShip.clicked -= OnPrevShip;
            if (_btnNextShip != null) _btnNextShip.clicked -= OnNextShip;
            if (_btnUpgradeShip != null) _btnUpgradeShip.clicked -= OnUpgradeShip;

            if (_btnShop != null) _btnShop.clicked -= OnShop;
            if (_btnCraft != null) _btnCraft.clicked -= OnCraft;
            if (_btnDismantle != null) _btnDismantle.clicked -= OnDismantle;

            if (_btnEquip != null) _btnEquip.clicked -= OnEquipClicked;
        }

        private void BindSlotButtons(VisualElement root)
        {
            UnbindSlotButtons();

            for (int i = 0; i < 4; i++)
            {
                _weaponBtns[i] = root.Q<Button>($"btnW{i}");
                _shieldBtns[i] = root.Q<Button>($"btnS{i}");
                _engineBtns[i] = root.Q<Button>($"btnE{i}");
                _modifierBtns[i] = root.Q<Button>($"btnM{i}");
            }

            for (int i = 0; i < 4; i++)
            {
                var idx = i;

                _weaponBtnActions[i] = () => OnSlotClicked(ItemType.Weapon, idx);
                _shieldBtnActions[i] = () => OnSlotClicked(ItemType.Shield, idx);
                _engineBtnActions[i] = () => OnSlotClicked(ItemType.Engine, idx);
                _modifierBtnActions[i] = () => OnSlotClicked(ItemType.Modifier, idx);

                if (_weaponBtns[i] != null) _weaponBtns[i].clicked += _weaponBtnActions[i];
                if (_shieldBtns[i] != null) _shieldBtns[i].clicked += _shieldBtnActions[i];
                if (_engineBtns[i] != null) _engineBtns[i].clicked += _engineBtnActions[i];
                if (_modifierBtns[i] != null) _modifierBtns[i].clicked += _modifierBtnActions[i];
            }
        }

        private void UnbindSlotButtons()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_weaponBtns[i] != null && _weaponBtnActions[i] != null) _weaponBtns[i].clicked -= _weaponBtnActions[i];
                if (_shieldBtns[i] != null && _shieldBtnActions[i] != null) _shieldBtns[i].clicked -= _shieldBtnActions[i];
                if (_engineBtns[i] != null && _engineBtnActions[i] != null) _engineBtns[i].clicked -= _engineBtnActions[i];
                if (_modifierBtns[i] != null && _modifierBtnActions[i] != null) _modifierBtns[i].clicked -= _modifierBtnActions[i];

                _weaponBtnActions[i] = null;
                _shieldBtnActions[i] = null;
                _engineBtnActions[i] = null;
                _modifierBtnActions[i] = null;
            }
        }

        private void SetupInventoryListOnce()
        {
            if (_inventoryList == null || _inventorySetupDone) return;
            _inventorySetupDone = true;

            _inventoryList.itemsSource = _items;
            _inventoryList.selectionType = SelectionType.Single;
            _inventoryList.fixedItemHeight = 64;

            _inventoryList.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.paddingLeft = 12;
                row.style.paddingRight = 12;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;

                var title = new Label { name = "title" };
                title.style.unityFontStyleAndWeight = FontStyle.Bold;

                var subtitle = new Label { name = "subtitle" };
                subtitle.style.color = new Color(0.78f, 0.86f, 0.95f, 1f);

                row.Add(title);
                row.Add(subtitle);
                return row;
            };

            _inventoryList.bindItem = (ve, i) =>
            {
                if ((uint)i >= (uint)_items.Count) return;

                var vm = _items[i];
                var title = ve.Q<Label>("title");
                var subtitle = ve.Q<Label>("subtitle");

                if (title != null) title.text = vm.Title;
                if (subtitle != null) subtitle.text = vm.Subtitle;
            };
        }

        private void EnsureInventorySelectionHook()
        {
            if (_inventoryList == null) return;

            _inventoryList.selectionChanged -= OnInventorySelectionChanged;
            _inventoryList.selectionChanged += OnInventorySelectionChanged;
        }

        private void OnInventorySelectionChanged(IEnumerable<object> selected)
        {
            if (_suppressInventorySelectionChanged) return;

            _selectedItem = null;
            if (selected != null)
            {
                foreach (var o in selected)
                {
                    _selectedItem = o as ItemVm;
                    break;
                }
            }

            UpdateItemDetails(_selectedItem);

            if (_selectedItem != null)
            {
                if (_btnEquip == null)
                    TryAutoEquipIfSlotPicked();
                else
                    ShowAction("Нажмите «Установить», чтобы экипировать предмет в выбранный слот.");
            }

            UpdateTutorialOverlay();
        }

        private void UpdateItemDetails(ItemVm vm)
        {
            if (_lblItemDetails == null) return;

            if (vm == null)
            {
                _lblItemDetails.text = "Выберите предмет.";
                return;
            }

            _sb.Length = 0;
            _sb.Append(vm.DefinitionId);
            _sb.Append('\n');
            _sb.Append("Тип: ").Append(vm.Type).Append('\n');
            _sb.Append("Уровень: ").Append(vm.Level).Append('\n');
            _sb.Append("Качество: ").Append(vm.Quality).Append('\n');
            if (vm.IsProtected) _sb.Append("Статус: защищён (стартовый)\n");
            if (vm.IsEquipped) _sb.Append("Статус: экипирован\n");

            _lblItemDetails.text = _sb.ToString();
        }

        private void OnSlotClicked(ItemType type, int index)
        {
            if (_selectedSlot.IsValid && _selectedSlot.Type == type && _selectedSlot.Index == index)
            {
                _selectedSlot = default;
                ClearSlotFilter();
                ApplySlotSelectionVisuals();
                ShowAction("Фильтр слота сброшен.");
                RefreshAll(force: false);
                UpdateTutorialOverlay();
                return;
            }

            var prevFilterType = _slotFilterType;

            _selectedSlot = new SlotPick(type, index);
            _slotFilterType = type;
            _slotFilterIndex = index;

            if (prevFilterType != ItemType.None && prevFilterType != _slotFilterType)
                ClearInventorySelection();

            ApplySlotSelectionVisuals();
            UpdateFilterLabel();

            ShowAction($"Выбран слот: {_slotFilterType} #{_slotFilterIndex}");

            if (_btnEquip == null)
                TryAutoEquipIfSlotPicked();
            else
                ShowAction("Выберите предмет в списке и нажмите «Установить».");

            // важно: фильтр поменялся -> форсим перестройку инвентаря
            InvalidateInventoryCache();
            RefreshAll(force: false);

            UpdateTutorialOverlay();
        }

        private void ApplySlotSelectionVisuals()
        {
            const string selectedClass = "slot-btn--selected";

            void Set(Button b, bool on)
            {
                if (b == null) return;
                b.EnableInClassList(selectedClass, on);
            }

            for (int i = 0; i < 4; i++)
            {
                Set(_weaponBtns[i], _selectedSlot.IsValid && _selectedSlot.Type == ItemType.Weapon && _selectedSlot.Index == i);
                Set(_shieldBtns[i], _selectedSlot.IsValid && _selectedSlot.Type == ItemType.Shield && _selectedSlot.Index == i);
                Set(_engineBtns[i], _selectedSlot.IsValid && _selectedSlot.Type == ItemType.Engine && _selectedSlot.Index == i);
                Set(_modifierBtns[i], _selectedSlot.IsValid && _selectedSlot.Type == ItemType.Modifier && _selectedSlot.Index == i);
            }
        }

        private void ClearSlotFilter()
        {
            _slotFilterType = ItemType.None;
            _slotFilterIndex = -1;
            UpdateFilterLabel();
        }

        private void UpdateFilterLabel()
        {
            if (_lblFilter == null) return;

            if (_slotFilterType == ItemType.None)
                _lblFilter.text = "Фильтр: все предметы";
            else
                _lblFilter.text = $"Фильтр: {_slotFilterType} слот #{_slotFilterIndex}";
        }

        private void TryAutoEquipIfSlotPicked()
        {
            if (_selectedItem == null || !_selectedSlot.IsValid)
                return;

            if (_selectedItem.Type != _selectedSlot.Type)
            {
                ShowAction("Тип предмета не подходит к слоту.");
                return;
            }

            EquipSelected();
        }

        private void OnEquipClicked()
        {
            if (_selectedItem == null || !_selectedSlot.IsValid)
            {
                ShowAction("Выберите слот и предмет.");
                return;
            }

            if (_selectedItem.Type != _selectedSlot.Type)
            {
                ShowAction("Тип предмета не подходит к слоту.");
                return;
            }

            EquipSelected();
        }

        private void RefreshAll(bool force)
        {
            var profile = GironoidApp.Profile;
            var cfg = GironoidApp.Config;

            if (profile == null || cfg == null)
                return;

            if (_lblTokens != null)
            {
                if (profile.Tokens != _lastTokens || profile.Iron != _lastIron || force)
                {
                    _lastTokens = profile.Tokens;
                    _lastIron = profile.Iron;
                    _lblTokens.text = $"Жетоны: {profile.Tokens} • Железо: {profile.Iron}";
                }
            }

            var shipId = ResolveSelectedShipId(profile, cfg);
            if (!string.Equals(shipId, _lastShipId, StringComparison.Ordinal) || force)
            {
                _lastShipId = shipId;
                UpdateShipNameLabel(cfg, shipId);
                InvalidateInventoryAndEquippedCaches();
            }

            var mk = ResolveShipMk(profile, shipId);
            if (mk != _lastShipMk || force)
            {
                _lastShipMk = mk;
                if (_lblShipMk != null) _lblShipMk.text = string.IsNullOrEmpty(shipId) ? "Ранг: —" : $"Ранг: Mk{mk}";
            }

            if (_lblShipStats != null)
                _lblShipStats.text = BuildShipStatsLine(cfg, shipId, mk);

            var eqHash = ComputeEquippedHash(profile, shipId, mk);
            if (eqHash != _lastEquippedHash || force)
            {
                _lastEquippedHash = eqHash;
                UpdateSlotButtons(profile, cfg, shipId, mk);
                RefreshShipVisual(profile, cfg, shipId, mk);

                // экипировка влияет на бейджи ✅ в списке
                InvalidateInventoryCache();
            }

            if (_slotFilterType != _lastFilterType || _slotFilterIndex != _lastFilterIndex)
            {
                _lastFilterType = _slotFilterType;
                _lastFilterIndex = _slotFilterIndex;
                InvalidateInventoryCache();
            }

            // ✅ Главное исправление:
            // считаем хэш по ПРОФИЛЮ, а перестройку _items делаем только при изменениях.
            var invHash = ComputeInventoryHash(profile);
            if (invHash != _lastInventoryHash || force)
            {
                _lastInventoryHash = invHash;
                BuildInventoryItems(profile);
                RebuildInventoryPreservingSelection();
            }
        }

        private int ComputeInventoryHash(PlayerProfile profile)
        {
            int hash = 17;
            if (profile == null || profile.Inventory == null) return hash;

            // учитываем фильтр (чтобы смена фильтра меняла hash)
            hash = hash * 31 + (int)_slotFilterType;
            hash = hash * 31 + _slotFilterIndex;

            for (int i = 0; i < profile.Inventory.Count; i++)
            {
                var it = profile.Inventory[i];
                if (it == null) continue;

                if (_slotFilterType != ItemType.None && it.Type != _slotFilterType)
                    continue;

                hash = hash * 31 + (it.InstanceId != null ? it.InstanceId.GetHashCode() : 0);
                hash = hash * 31 + (it.DefinitionId != null ? it.DefinitionId.GetHashCode() : 0);
                hash = hash * 31 + (int)it.Type;
                hash = hash * 31 + it.Level;
                hash = hash * 31 + (int)it.Quality;
                hash = hash * 31 + (it.IsProtected ? 1 : 0);

                // бейдж "экипирован"
                bool eq = profile.IsItemEquippedAnywhere(it.InstanceId);
                hash = hash * 31 + (eq ? 1 : 0);
            }

            return hash;
        }

        private void BuildInventoryItems(PlayerProfile profile)
        {
            // Сбрасываем старые VM в пул
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null) _pool.Add(_items[i]);

            _items.Clear();

            if (profile == null || profile.Inventory == null)
                return;

            for (int i = 0; i < profile.Inventory.Count; i++)
            {
                var it = profile.Inventory[i];
                if (it == null) continue;

                if (_slotFilterType != ItemType.None && it.Type != _slotFilterType)
                    continue;

                var vm = TakeVm();
                vm.InstanceId = it.InstanceId;
                vm.DefinitionId = it.DefinitionId;
                vm.Type = it.Type;
                vm.Level = it.Level;
                vm.Quality = it.Quality;
                vm.IsProtected = it.IsProtected;
                vm.IsEquipped = profile.IsItemEquippedAnywhere(it.InstanceId);

                vm.Title = $"[{it.Type}] {it.DefinitionId}";
                vm.Subtitle = $"Lv {it.Level} • {it.Quality}"
                              + (it.IsProtected ? " • 🔒" : "")
                              + (vm.IsEquipped ? " • ✅" : "");

                _items.Add(vm);
            }
        }

        private void RefreshShipVisual(PlayerProfile profile, GameConfig cfg, string shipId, int mk)
        {
            if (_shipVisual == null) return;
            _shipVisual.Apply(profile, cfg, shipId, mk);
        }

        private void UpdateShipNameLabel(GameConfig cfg, string shipId)
        {
            if (_lblShipName == null) return;

            if (string.IsNullOrEmpty(shipId))
            {
                _lblShipName.text = "Корабль: — (купите в магазине)";
                return;
            }

            if (cfg != null && cfg.ShipCatalog != null && cfg.ShipCatalog.TryGet(shipId, out var def))
            {
                var name = string.IsNullOrWhiteSpace(def.NameRu) ? shipId : def.NameRu;
                _lblShipName.text = $"Корабль: {name}";
            }
            else
            {
                _lblShipName.text = $"Корабль: {shipId}";
            }
        }

        private void RebuildInventoryPreservingSelection()
        {
            if (_inventoryList == null) return;

            var keepInstanceId = _selectedItem != null ? _selectedItem.InstanceId : null;

            _suppressInventorySelectionChanged = true;

            try
            {
                try { _inventoryList.ClearSelection(); } catch { }
                try { _inventoryList.SetSelectionWithoutNotify(new int[0]); } catch { }
            }
            catch { }

            try
            {
                _inventoryList.Rebuild();
            }
            catch
            {
                try { _inventoryList.RefreshItems(); } catch { }
            }

            _suppressInventorySelectionChanged = false;

            if (!string.IsNullOrEmpty(keepInstanceId))
            {
                var idx = FindItemIndexByInstanceId(keepInstanceId);
                if (idx >= 0)
                {
                    _suppressInventorySelectionChanged = true;
                    try { _inventoryList.SetSelection(idx); } catch { }
                    _suppressInventorySelectionChanged = false;

                    _selectedItem = _items[idx];
                    UpdateItemDetails(_selectedItem);
                    return;
                }
            }

            ClearInventorySelection();
        }

        private void ClearInventorySelection()
        {
            _selectedItem = null;
            UpdateItemDetails(null);

            if (_inventoryList == null) return;

            _suppressInventorySelectionChanged = true;
            try
            {
                _inventoryList.ClearSelection();
                _inventoryList.SetSelectionWithoutNotify(new int[0]);
            }
            catch
            {
                try { _inventoryList.ClearSelection(); } catch { }
            }
            finally
            {
                _suppressInventorySelectionChanged = false;
            }
        }

        private int FindItemIndexByInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it != null && it.InstanceId == instanceId)
                    return i;
            }
            return -1;
        }

        private ItemVm TakeVm()
        {
            var n = _pool.Count;
            if (n > 0)
            {
                var vm = _pool[n - 1];
                _pool.RemoveAt(n - 1);
                return vm;
            }
            return new ItemVm();
        }

        private void UpdateSlotButtons(PlayerProfile profile, GameConfig cfg, string shipId, int mk)
        {
            if (string.IsNullOrEmpty(shipId))
            {
                HideAllSlots(_weaponBtns);
                HideAllSlots(_shieldBtns);
                HideAllSlots(_engineBtns);
                HideAllSlots(_modifierBtns);
                ApplySlotSelectionVisuals();
                return;
            }

            int wSlots = 1, sSlots = 1, eSlots = 1, mSlots = 1;

            if (cfg != null && cfg.ShipCatalog != null && cfg.ShipCatalog.TryGetTier(shipId, mk, out var tier))
            {
                wSlots = tier.Slots.WeaponSlots;
                sSlots = tier.Slots.ShieldSlots;
                eSlots = tier.Slots.EngineSlots;
                mSlots = tier.Slots.ModSlots;
            }

            UpdateSlotGroup(_weaponBtns, profile, shipId, ItemType.Weapon, wSlots, "W");
            UpdateSlotGroup(_shieldBtns, profile, shipId, ItemType.Shield, sSlots, "S");
            UpdateSlotGroup(_engineBtns, profile, shipId, ItemType.Engine, eSlots, "E");
            UpdateSlotGroup(_modifierBtns, profile, shipId, ItemType.Modifier, mSlots, "M");

            ApplySlotSelectionVisuals();
        }

        private static void HideAllSlots(Button[] buttons)
        {
            if (buttons == null) return;
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;
                b.SetEnabled(false);
                b.style.display = DisplayStyle.None;
                b.text = "-";
            }
        }

        private void UpdateSlotGroup(Button[] buttons, PlayerProfile profile, string shipId, ItemType type, int slotCount, string prefix)
        {
            slotCount = Mathf.Clamp(slotCount, 0, 4);

            var ship = profile.GetShip(shipId);

            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                if (b == null) continue;

                var enabled = i < slotCount;
                b.SetEnabled(enabled);
                b.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;

                if (!enabled)
                    continue;

                string equippedInstanceId = null;
                if (ship != null)
                    ship.TryGet(type, i, out equippedInstanceId);

                var shortName = "";
                if (!string.IsNullOrEmpty(equippedInstanceId) && profile.TryGetItem(equippedInstanceId, out var item) && item != null)
                    shortName = item.DefinitionId;

                b.text = string.IsNullOrEmpty(shortName)
                    ? $"{prefix}{i + 1}"
                    : $"{prefix}{i + 1}: {shortName}";
            }
        }

        private void EquipSelected()
        {
            if (!GironoidApp.IsReady || GironoidApp.Hangar == null)
                return;

            var profile = GironoidApp.Profile;
            if (profile == null) return;

            var shipId = ResolveSelectedShipId(profile, GironoidApp.Config);
            if (string.IsNullOrEmpty(shipId))
            {
                ShowAction("Сначала купите корабль в магазине.");
                return;
            }

            if (!_selectedSlot.IsValid || _selectedItem == null)
                return;

            if (_selectedItem.Type != _selectedSlot.Type)
            {
                ShowAction("Тип предмета не подходит к слоту.");
                return;
            }

            var flush = ShouldFlushToServer();

            var res = GironoidApp.Hangar.Equip(
                shipId,
                _selectedSlot.Index,
                _selectedItem.Type,
                _selectedItem.InstanceId,
                flushToServer: flush
            );

            if (res.Ok)
            {
                ShowAction("Экипировано.");
                AdvanceTutorialByEquip(_selectedItem.Type);
            }
            else
            {
                ShowAction(string.IsNullOrEmpty(res.Error) ? "Ошибка экипировки." : res.Error);
            }

            InvalidateInventoryAndEquippedCaches();
            RefreshAll(force: true);

            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void AdvanceTutorialByEquip(ItemType type)
        {
            if (type == ItemType.Engine) SetTutorialStepAtLeast(6);
            if (type == ItemType.Weapon) SetTutorialStepAtLeast(7);
        }

        private void OnDismantle()
        {
            if (!GironoidApp.IsReady || GironoidApp.Hangar == null)
                return;

            if (_selectedItem == null)
            {
                ShowAction("Выберите предмет для разборки.");
                return;
            }

            if (_selectedItem.IsProtected)
            {
                ShowAction("Стартовый предмет защищён и не может быть разобран.");
                return;
            }

            var flush = ShouldFlushToServer();
            HangarLogic.DismantleGain gain;

            var res = GironoidApp.Hangar.Dismantle(_selectedItem.InstanceId, out gain, flushToServer: flush);
            if (res.Ok)
                ShowAction($"Разобрано. Получено: {FormatGain(gain)}");
            else
                ShowAction(string.IsNullOrEmpty(res.Error) ? "Ошибка разборки." : res.Error);

            _selectedItem = null;
            UpdateItemDetails(null);

            InvalidateInventoryAndEquippedCaches();
            RefreshAll(force: true);

            UpdateTutorialOverlay();
        }

        private string FormatGain(HangarLogic.DismantleGain gain)
        {
            _sb.Length = 0;

            void Add(string name, int v)
            {
                if (v <= 0) return;
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(name).Append(": ").Append(v);
            }

            Add("Iron", gain.Iron);
            Add("Copper", gain.Copper);
            Add("Silver", gain.Silver);
            Add("Tokens", gain.Tokens);

            return _sb.Length == 0 ? "—" : _sb.ToString();
        }

        private void OnPrevShip() => CycleShip(-1);
        private void OnNextShip() => CycleShip(+1);

        private void CycleShip(int dir)
        {
            var profile = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (profile == null || cfg == null) return;

            var owned = profile.OwnedShips;
            if (owned == null || owned.Count == 0)
            {
                ShowAction("Нет купленных кораблей.");
                return;
            }

            var current = ResolveSelectedShipId(profile, cfg);
            var idx = 0;

            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    if (owned[i] != null && owned[i].ShipId == current) { idx = i; break; }
                }
            }

            idx += dir;
            if (idx < 0) idx = owned.Count - 1;
            if (idx >= owned.Count) idx = 0;

            var next = owned[idx] != null ? owned[idx].ShipId : null;
            if (string.IsNullOrEmpty(next))
                return;

            var ps = GironoidApp.ProfileService;
            if (ps != null)
            {
                ps.Apply(p =>
                {
                    p.SelectedShipId = next;
                }, flushToServer: ShouldFlushToServer());
            }

            ShowAction($"Выбран корабль: {next}");
            InvalidateCaches();
            RefreshAll(force: true);

            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void OnUpgradeShip()
        {
            ShowAction("Апгрейд ранга будет добавлен на следующем этапе.");
        }

        private void OnBack()
        {
            if (_finishInProgress) return;
            SceneManager.LoadScene(ResolveSceneName("Menu", "10_Menu"));
        }

        private void OnFinish()
        {
            if (!GironoidApp.IsReady) return;
            if (_finishInProgress) return;

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p == null || cfg == null) return;

            var shipId = ResolveSelectedShipId(p, cfg);
            if (string.IsNullOrEmpty(shipId))
            {
                ShowAction("Нельзя продолжить без корабля. Сначала купите корабль в магазине.");
                SetTutorialStepAtLeast(1);
                UpdateTutorialOverlay();
                return;
            }

            if (!HasRequiredModules(p, shipId, out var err))
            {
                ShowAction(err);
                UpdateTutorialOverlay();
                return;
            }

            SetTutorialStepAtLeast(8);
            StartCoroutine(FinishFlowToStarMap());
        }

        private IEnumerator FinishFlowToStarMap()
        {
            _finishInProgress = true;

            if (_btnFinish != null) _btnFinish.SetEnabled(false);
            if (_btnBack != null) _btnBack.SetEnabled(false);

            ShowAction("Сохранение…");

            var ps = GironoidApp.ProfileService;
            var y = GironoidApp.Yandex;

            if (ps != null)
                ps.Save(flushToServer: false);

            bool cloudOk = false;

            if (y != null && y.CanUseCloud)
            {
                var profile = GironoidApp.Profile;
                if (profile != null)
                {
                    var json = ProfileJson.ToJson(profile);
                    var updated = profile.UpdatedUtcMs;

                    yield return y.SaveProfileToCloudBlocking(json, updated, r => cloudOk = r, timeoutSeconds: 8f);
                }
            }
            else
            {
                cloudOk = true;
            }

            ShowAction(cloudOk ? "Переход на звёздную карту…" : "Сохранение не подтверждено, переход…");

            SceneManager.LoadScene(ResolveSceneName("StarMap", "20_StarMap"));
        }

        private static bool HasRequiredModules(PlayerProfile p, string shipId, out string error)
        {
            error = "";

            var ship = p.GetShip(shipId);
            if (ship == null)
            {
                error = "Корабль не найден в профиле.";
                return false;
            }

            bool hasWeapon = false;
            bool hasEngine = false;

            if (ship.Equipped != null)
            {
                for (int i = 0; i < ship.Equipped.Count; i++)
                {
                    var e = ship.Equipped[i];
                    if (string.IsNullOrEmpty(e.InstanceId)) continue;

                    if (e.Type == ItemType.Weapon) hasWeapon = true;
                    if (e.Type == ItemType.Engine) hasEngine = true;
                }
            }

            if (!hasEngine)
            {
                error = "Нельзя продолжить: установите хотя бы один двигатель.";
                return false;
            }

            if (!hasWeapon)
            {
                error = "Нельзя продолжить: установите хотя бы одно оружие.";
                return false;
            }

            return true;
        }

        private void OnShop()
        {
            if (!GironoidApp.IsReady)
                return;

            SetTutorialStepAtLeast(1);

            // 1) Если модальный магазин корректно привязан — используем его.
            if (_shopModal != null && _shopModal.IsBound)
            {
                _shopModal.Open();

                var p = GironoidApp.Profile;
                bool hasShip = p != null && p.OwnedShips != null && p.OwnedShips.Count > 0;

                // Во время обучения (когда нет корабля) — не даём закрыть модалку.
                SetShopCloseAllowed(hasShip);

                UpdateTutorialOverlay();
                return;
            }

            // 2) Иначе — пробуем внешний UIDocument магазина (UI_shop).
            if (_shopUi != null)
            {
                OpenExternalShop();
                return;
            }

            // 3) Сообщение с точной причиной.
            if (_shopService == null)
                ShowAction("Магазин не найден: отсутствует ShopServiceBehaviour в сцене (объект Services).");
            else
                ShowAction("Магазин не найден: в UXML ангара нет VisualElement name=\"shopModal\" (ожидается модалка).");

            UpdateTutorialOverlay();
        }

        private void OpenExternalShop()
        {
            _externalShopOpen = true;

            // Скрываем ангарный UI, чтобы не было кликов “сквозь” магазин.
            if (_root != null)
                _root.style.display = DisplayStyle.None;

            // Отключаем туториал-оверлей (он в дереве ангара).
            _tutorial?.Hide();

            _shopUi.gameObject.SetActive(true);

            ShowAction("Открыт магазин (отдельный экран).");
        }

        private void CloseExternalShop()
        {
            _externalShopOpen = false;

            if (_shopUi != null)
                _shopUi.gameObject.SetActive(false);

            if (_root != null)
                _root.style.display = DisplayStyle.Flex;

            RefreshAll(force: true);
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void SetShopCloseAllowed(bool allowed)
        {
            if (_btnShopClose != null)
                _btnShopClose.SetEnabled(allowed);
        }

        private bool IsShopOpen()
        {
            // модальный магазин
            if (_shopModal != null && _shopModal.IsOpen)
                return true;

            // внешний магазин
            return _externalShopOpen;
        }

        private bool TryGetShopPrimaryActionTarget(out VisualElement target)
        {
            target = null;

            if (_shopModalRoot == null)
                return false;

            // 1) Пытаемся найти первую кнопку покупки в списке
            var list = _shopModalRoot.Q<VisualElement>("shopList");
            if (list != null)
            {
                var btn = list.Q<Button>();
                if (btn != null)
                {
                    target = btn;
                    return true;
                }

                for (int i = 0; i < list.childCount; i++)
                {
                    var row = list[i];
                    if (row == null) continue;
                    var b = row.Q<Button>();
                    if (b != null)
                    {
                        target = b;
                        return true;
                    }
                }
            }

            // 2) Если нет списка/кнопок — подсветим rewarded (если есть)
            var watch = _shopModalRoot.Q<Button>("btnWatchAdTokens");
            if (watch != null)
            {
                target = watch;
                return true;
            }

            // 3) Фоллбек — весь модал
            target = _shopModalRoot;
            return true;
        }

        private void OnCraft()
        {
            if (!GironoidApp.IsReady || GironoidApp.Hangar == null) return;

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p == null || cfg == null) return;

            if (cfg.ItemCatalog == null)
            {
                ShowAction("ItemCatalog не задан — крафт недоступен.");
                return;
            }

            bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;
            if (!hasShip)
            {
                ShowAction("Сначала купите корабль в магазине.");
                SetTutorialStepAtLeast(1);
                UpdateTutorialOverlay();
                return;
            }

            if (!HasAnyItemOfType(p, ItemType.Weapon))
            {
                var defId = cfg.ItemCatalog.StarterWeaponDefinitionId;
                var res = GironoidApp.Hangar.CraftItem(defId, ironCost: 25, tokenCost: 0, out var crafted, flushToServer: ShouldFlushToServer());
                if (res.Ok)
                {
                    ShowAction($"Оружие создано: {crafted.DefinitionId} • Lv{crafted.Level} • {crafted.Quality}");
                    SetTutorialStepAtLeast(4);
                    InvalidateInventoryAndEquippedCaches();
                    RefreshAll(force: true);
                }
                else
                {
                    ShowAction(string.IsNullOrEmpty(res.Error) ? "Ошибка крафта оружия." : res.Error);
                }

                ShowOnboardingHint();
                UpdateTutorialOverlay();
                return;
            }

            if (!HasAnyItemOfType(p, ItemType.Engine))
            {
                var defId = cfg.ItemCatalog.StarterEngineDefinitionId;
                var res = GironoidApp.Hangar.CraftItem(defId, ironCost: 25, tokenCost: 0, out var crafted, flushToServer: ShouldFlushToServer());
                if (res.Ok)
                {
                    ShowAction($"Двигатель создан: {crafted.DefinitionId} • Lv{crafted.Level} • {crafted.Quality}");
                    SetTutorialStepAtLeast(5);
                    InvalidateInventoryAndEquippedCaches();
                    RefreshAll(force: true);
                }
                else
                {
                    ShowAction(string.IsNullOrEmpty(res.Error) ? "Ошибка крафта двигателя." : res.Error);
                }

                ShowOnboardingHint();
                UpdateTutorialOverlay();
                return;
            }

            ShowAction("Крафт: базовые предметы уже созданы.");
            UpdateTutorialOverlay();
        }

        private void ShowAction(string msg)
        {
            msg ??= "";

            if (_lblShipAction != null)
                _lblShipAction.text = msg;
            else
                Debug.Log($"[HangarScreen] {msg}");
        }

        private void ShowOnboardingHint()
        {
            var p = GironoidApp.Profile;
            if (p == null) return;

            int step = p.TutorialStep;

            if (step < 3)
            {
                ShowAction("Шаг 1: Откройте «Магазин» и купите первый корабль.");
                return;
            }

            if (!HasAnyItemOfType(p, ItemType.Weapon))
            {
                ShowAction("Шаг 2: Нажмите «Крафт», чтобы создать оружие.");
                return;
            }

            if (!HasAnyItemOfType(p, ItemType.Engine))
            {
                ShowAction("Шаг 3: Нажмите «Крафт», чтобы создать двигатель.");
                return;
            }

            var cfg = GironoidApp.Config;
            var shipId = ResolveSelectedShipId(p, cfg);

            if (!HasAnyEquipped(p, shipId, ItemType.Engine))
            {
                ShowAction("Шаг 4: Установите двигатель (E1 → выбрать двигатель в списке).");
                return;
            }

            if (!HasAnyEquipped(p, shipId, ItemType.Weapon))
            {
                ShowAction("Шаг 5: Установите оружие (W1 → выбрать оружие в списке).");
                return;
            }

            ShowAction("Шаг 6: Нажмите «Готово» для перехода на звёздную карту.");
        }

        private void UpdateTutorialOverlay()
        {
            if (_tutorial == null || !GironoidApp.IsReady)
                return;

            // Если открыт внешний магазин — оверлей ангара неактуален.
            if (_externalShopOpen)
            {
                _tutorial.Hide();
                return;
            }

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p == null || cfg == null)
                return;

            bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;

            if (!hasShip)
            {
                if (IsShopOpen())
                {
                    SetShopCloseAllowed(false);

                    if (TryGetShopPrimaryActionTarget(out var target) && target != null)
                    {
                        _tutorial.Show(target, "Сделайте первую покупку в магазине. Во время обучения доступно только подсвеченное действие.");
                        return;
                    }
                }

                SetShopCloseAllowed(true);

                if (_btnShop != null)
                {
                    _tutorial.Show(_btnShop, "Нажмите «Магазин». Во время обучения можно нажимать только подсвеченную кнопку.");
                    return;
                }
            }

            SetShopCloseAllowed(true);

            if (!HasAnyItemOfType(p, ItemType.Weapon))
            {
                if (_btnCraft != null)
                    _tutorial.Show(_btnCraft, "Нажмите «Крафт», чтобы создать оружие.");
                return;
            }

            if (!HasAnyItemOfType(p, ItemType.Engine))
            {
                if (_btnCraft != null)
                    _tutorial.Show(_btnCraft, "Нажмите «Крафт», чтобы создать двигатель.");
                return;
            }

            var shipId = ResolveSelectedShipId(p, cfg);
            if (string.IsNullOrEmpty(shipId))
            {
                if (_btnShop != null)
                    _tutorial.Show(_btnShop, "Купите корабль в магазине.");
                return;
            }

            if (!HasAnyEquipped(p, shipId, ItemType.Engine))
            {
                if (!_selectedSlot.IsValid || _selectedSlot.Type != ItemType.Engine)
                {
                    if (_engineBtns[0] != null)
                        _tutorial.Show(_engineBtns[0], "Нажмите на слот двигателя E1.");
                    return;
                }

                if (_inventoryList != null)
                    _tutorial.Show(_inventoryList, "Выберите двигатель в списке справа.");
                return;
            }

            if (!HasAnyEquipped(p, shipId, ItemType.Weapon))
            {
                if (!_selectedSlot.IsValid || _selectedSlot.Type != ItemType.Weapon)
                {
                    if (_weaponBtns[0] != null)
                        _tutorial.Show(_weaponBtns[0], "Нажмите на слот оружия W1.");
                    return;
                }

                if (_inventoryList != null)
                    _tutorial.Show(_inventoryList, "Выберите оружие в списке справа.");
                return;
            }

            if (_btnFinish != null)
                _tutorial.Show(_btnFinish, "Нажмите «Готово» для перехода на звёздную карту.");

            SetTutorialStepAtLeast(7);
        }

        private void SetTutorialStepAtLeast(int step)
        {
            var ps = GironoidApp.ProfileService;
            if (ps == null) return;

            ps.Apply(p =>
            {
                if (p.TutorialStep < step)
                    p.TutorialStep = step;
            }, flushToServer: ShouldFlushToServer());
        }

        private void InvalidateCaches()
        {
            _lastTokens = int.MinValue;
            _lastIron = int.MinValue;
            _lastShipId = null;
            _lastShipMk = int.MinValue;
            _lastInventoryHash = 0;
            _lastEquippedHash = 0;
            _lastFilterType = (ItemType)(-999);
            _lastFilterIndex = int.MinValue;
        }

        private void InvalidateInventoryAndEquippedCaches()
        {
            _lastInventoryHash = 0;
            _lastEquippedHash = 0;
        }

        private void InvalidateInventoryCache()
        {
            _lastInventoryHash = 0;
        }

        private static string ResolveSelectedShipId(PlayerProfile profile, GameConfig cfg)
        {
            if (profile == null) return null;

            if (!string.IsNullOrEmpty(profile.SelectedShipId))
                return profile.SelectedShipId;

            if (profile.OwnedShips != null)
            {
                for (int i = 0; i < profile.OwnedShips.Count; i++)
                {
                    var s = profile.OwnedShips[i];
                    if (s != null && !string.IsNullOrEmpty(s.ShipId))
                        return s.ShipId;
                }
            }

            return null;
        }

        private static int ResolveShipMk(PlayerProfile profile, string shipId)
        {
            if (profile == null || string.IsNullOrEmpty(shipId)) return 1;
            var s = profile.GetShip(shipId);
            return s != null ? Mathf.Clamp(s.Rank, 1, 4) : 1;
        }

        private static int ComputeEquippedHash(PlayerProfile profile, string shipId, int mk)
        {
            int hash = 17;
            if (profile == null || string.IsNullOrEmpty(shipId)) return hash;

            var ship = profile.GetShip(shipId);
            if (ship == null || ship.Equipped == null) return hash;

            for (int i = 0; i < ship.Equipped.Count; i++)
            {
                var e = ship.Equipped[i];
                hash = hash * 31 + (int)e.Type;
                hash = hash * 31 + e.SlotIndex;
                hash = hash * 31 + (e.InstanceId != null ? e.InstanceId.GetHashCode() : 0);
            }

            return hash;
        }

        private string BuildShipStatsLine(GameConfig cfg, string shipId, int mk)
        {
            if (cfg == null || cfg.ShipCatalog == null || string.IsNullOrEmpty(shipId))
                return "Статы: —";

            if (!cfg.ShipCatalog.TryGetTier(shipId, mk, out var tier))
                return "Статы: —";

            _sb.Length = 0;
            _sb.Append("Статы: ");
            _sb.Append("HP ").Append(tier.Stats.Hull);
            _sb.Append(" • ");
            _sb.Append("Speed ").Append(tier.Stats.Speed.ToString("0.0"));
            _sb.Append(" • ");
            _sb.Append("Accel ").Append(tier.Stats.Acceleration.ToString("0.0"));
            _sb.Append(" • ");
            _sb.Append("Atk ").Append(tier.Stats.Attack.ToString("0.00"));
            _sb.Append(" • ");
            _sb.Append("Def ").Append(tier.Stats.Defense.ToString("0.00"));
            return _sb.ToString();
        }

        private static bool ShouldFlushToServer()
        {
            var y = GironoidApp.Yandex;
            return y != null && y.CanUseCloud;
        }

        private static string ResolveSceneName(string constName, string fallback)
        {
            try
            {
                var t = typeof(GironoidScenes);
                var f = t.GetField(constName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = (string)f.GetValue(null);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return fallback;
        }

        private static bool HasAnyItemOfType(PlayerProfile p, ItemType type)
        {
            if (p == null || p.Inventory == null) return false;
            for (int i = 0; i < p.Inventory.Count; i++)
            {
                var it = p.Inventory[i];
                if (it != null && it.Type == type)
                    return true;
            }
            return false;
        }

        private static bool HasAnyEquipped(PlayerProfile p, string shipId, ItemType type)
        {
            if (p == null || string.IsNullOrEmpty(shipId)) return false;

            var ship = p.GetShip(shipId);
            if (ship == null || ship.Equipped == null) return false;

            for (int i = 0; i < ship.Equipped.Count; i++)
            {
                var e = ship.Equipped[i];
                if (e.Type == type && !string.IsNullOrEmpty(e.InstanceId))
                    return true;
            }

            return false;
        }

        [Serializable]
        private sealed class ItemVm
        {
            public string InstanceId;
            public string DefinitionId;
            public ItemType Type;
            public int Level;
            public ItemQuality Quality;
            public bool IsProtected;
            public bool IsEquipped;

            public string Title;
            public string Subtitle;
        }

        private readonly struct SlotPick
        {
            public readonly ItemType Type;
            public readonly int Index;

            public bool IsValid => Type != ItemType.None && Index >= 0;

            public SlotPick(ItemType type, int index)
            {
                Type = type;
                Index = index;
            }
        }
    }
}
