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
        private const int TutorialStepBuyFirstShip = 1;
        private const int TutorialStepShipPurchased = 2;
        private const int TutorialStepFirstShipConfirmed = 3;
        private const int TutorialStepStarterEngineCrafted = 4;
        private const int TutorialStepStarterWeaponCrafted = 5;
        private const int TutorialStepStarterEngineEquipped = 6;
        private const int TutorialStepStarterWeaponEquipped = 7;
        private const int TutorialStepHangarCompleted = 8;

        private const int StarterCraftIronCost = 25;
        private const int DefaultCraftIronCost = 35;

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
        private bool _shopWasOpenLastTick;

        // Popup после первой покупки корабля: "Вот ваш первый корабль".
        private VisualElement _firstShipPopupLayer;
        private VisualElement _firstShipPopupCard;
        private Label _firstShipPopupTitle;
        private Label _firstShipPopupText;
        private Button _firstShipPopupOk;

        // Popup крафта в ангаре (первичный onboarding-флоу).
        private VisualElement _craftModalLayer;
        private VisualElement _craftModalCard;
        private Label _craftModalTitle;
        private Label _craftModalText;
        private Label _craftModalBlueprintName;
        private Label _craftModalChance;
        private Label _craftModalStats;
        private ListView _craftBlueprintsList;
        private Button _craftTabWeapons;
        private Button _craftTabShields;
        private Button _craftTabEngines;
        private Button _craftTabModifiers;
        private Button _craftModalAction;
        private Button _craftModalClose;
        private bool _craftModalBusy;
        private bool _craftListSetupDone;
        private bool _suppressCraftSelectionChanged;
        private ItemType _craftSelectedType = ItemType.Engine;
        private readonly List<CraftBlueprintVm> _craftBlueprints = new List<CraftBlueprintVm>(32);
        private CraftBlueprintVm _craftSelectedBlueprint;

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
            BuildFirstShipPopup(_root);
            BuildCraftModal(_root);
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
            _shopWasOpenLastTick = false;

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

            var p = GironoidApp.Profile;
            if (p != null)
            {
                bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;

                if (hasShip)
                {
                    if (p.TutorialStep < TutorialStepShipPurchased)
                        SetTutorialStepAtLeast(TutorialStepShipPurchased);
                }
                else
                {
                    if (p.TutorialStep != TutorialStepBuyFirstShip)
                        SetTutorialStepExact(TutorialStepBuyFirstShip);

                    HideFirstShipPopup();
                    HideCraftModal();
                }
            }

            RefreshAll(force: true);

            bool hasAnyShip = p != null && p.OwnedShips != null && p.OwnedShips.Count > 0;
            if (hasAnyShip && p != null && p.TutorialStep == TutorialStepShipPurchased && !IsShopOpen())
                ShowFirstShipPopup();

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
            if (_btnShopClose != null)
                _btnShopClose.clicked -= OnShopCloseClickedFallback;

            _shopService = null;
            _shopModalRoot = null;
            _btnShopClose = null;

            _shopExternalRoot = null;
            _btnShopExternalBack = null;
            _externalShopOpen = false;

            if (_firstShipPopupOk != null)
                _firstShipPopupOk.clicked -= OnFirstShipPopupOk;

            _firstShipPopupLayer = null;
            _firstShipPopupCard = null;
            _firstShipPopupTitle = null;
            _firstShipPopupText = null;
            _firstShipPopupOk = null;

            if (_craftModalAction != null)
                _craftModalAction.clicked -= OnCraftModalAction;
            if (_craftModalClose != null)
                _craftModalClose.clicked -= OnCraftModalClose;
            if (_craftTabWeapons != null)
                _craftTabWeapons.clicked -= OnCraftTabWeaponsClicked;
            if (_craftTabShields != null)
                _craftTabShields.clicked -= OnCraftTabShieldsClicked;
            if (_craftTabEngines != null)
                _craftTabEngines.clicked -= OnCraftTabEnginesClicked;
            if (_craftTabModifiers != null)
                _craftTabModifiers.clicked -= OnCraftTabModifiersClicked;
            if (_craftBlueprintsList != null)
                _craftBlueprintsList.selectionChanged -= OnCraftBlueprintSelectionChanged;

            _craftModalLayer = null;
            _craftModalCard = null;
            _craftModalTitle = null;
            _craftModalText = null;
            _craftModalBlueprintName = null;
            _craftModalChance = null;
            _craftModalStats = null;
            _craftBlueprintsList = null;
            _craftTabWeapons = null;
            _craftTabShields = null;
            _craftTabEngines = null;
            _craftTabModifiers = null;
            _craftModalAction = null;
            _craftModalClose = null;
            _craftModalBusy = false;
            _craftListSetupDone = false;
            _suppressCraftSelectionChanged = false;
            _craftBlueprints.Clear();
            _craftSelectedBlueprint = null;
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
                HandleEarlyOnboardingTransitions();
                UpdateTutorialOverlay();
            }
        }

        private void HandleEarlyOnboardingTransitions()
        {
            if (!GironoidApp.IsReady)
                return;

            var p = GironoidApp.Profile;
            if (p == null)
                return;

            bool shopOpen = IsShopOpen();
            bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;

            if (!hasShip)
            {
                if (p.TutorialStep != TutorialStepBuyFirstShip)
                    SetTutorialStepExact(TutorialStepBuyFirstShip);

                HideFirstShipPopup();
                HideCraftModal();
                _shopWasOpenLastTick = shopOpen;
                return;
            }

            if (p.TutorialStep < TutorialStepShipPurchased)
                SetTutorialStepAtLeast(TutorialStepShipPurchased);

            if (_shopWasOpenLastTick && !shopOpen && p.TutorialStep == TutorialStepShipPurchased)
                ShowFirstShipPopup();

            if (HasStarterEngineCrafted(p) && p.TutorialStep < TutorialStepStarterEngineCrafted)
                SetTutorialStepAtLeast(TutorialStepStarterEngineCrafted);

            if (HasStarterWeaponCrafted(p) && p.TutorialStep < TutorialStepStarterWeaponCrafted)
                SetTutorialStepAtLeast(TutorialStepStarterWeaponCrafted);

            var shipId = ResolveSelectedShipId(p, GironoidApp.Config);
            if (!string.IsNullOrEmpty(shipId))
            {
                if (HasAnyEquipped(p, shipId, ItemType.Engine) && p.TutorialStep < TutorialStepStarterEngineEquipped)
                    SetTutorialStepAtLeast(TutorialStepStarterEngineEquipped);

                if (HasAnyEquipped(p, shipId, ItemType.Weapon) && p.TutorialStep < TutorialStepStarterWeaponEquipped)
                    SetTutorialStepAtLeast(TutorialStepStarterWeaponEquipped);
            }

            _shopWasOpenLastTick = shopOpen;
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

            if (_lblShipAction != null)
            {
                _lblShipAction.style.fontSize = 30;
                _lblShipAction.style.unityFontStyleAndWeight = FontStyle.Bold;
                _lblShipAction.style.color = new Color(0.93f, 0.98f, 1f, 1f);
                _lblShipAction.style.unityTextOutlineWidth = 2;
                _lblShipAction.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.82f);
                _lblShipAction.style.whiteSpace = WhiteSpace.Normal;
            }
        }

        private void CreateTutorialOverlay(VisualElement root)
        {
            var layer = root.Q<VisualElement>("tutorialLayer");
            _tutorial = new TutorialOverlay(root, layer);
        }

        private void BuildFirstShipPopup(VisualElement root)
        {
            if (root == null)
                return;

            _firstShipPopupLayer = root.Q<VisualElement>("firstShipPopupLayer");
            if (_firstShipPopupLayer == null)
            {
                _firstShipPopupLayer = new VisualElement { name = "firstShipPopupLayer" };
                _firstShipPopupLayer.style.position = Position.Absolute;
                _firstShipPopupLayer.style.left = 0;
                _firstShipPopupLayer.style.top = 0;
                _firstShipPopupLayer.style.right = 0;
                _firstShipPopupLayer.style.bottom = 0;
                _firstShipPopupLayer.style.justifyContent = Justify.Center;
                _firstShipPopupLayer.style.alignItems = Align.Center;
                _firstShipPopupLayer.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
                _firstShipPopupLayer.pickingMode = PickingMode.Position;
                _firstShipPopupLayer.style.display = DisplayStyle.None;

                _firstShipPopupCard = new VisualElement { name = "firstShipPopupCard" };
                _firstShipPopupCard.style.width = 520;
                _firstShipPopupCard.style.maxWidth = new Length(90, LengthUnit.Percent);
                _firstShipPopupCard.style.paddingLeft = 18;
                _firstShipPopupCard.style.paddingRight = 18;
                _firstShipPopupCard.style.paddingTop = 16;
                _firstShipPopupCard.style.paddingBottom = 16;
                _firstShipPopupCard.style.backgroundColor = new Color(0.06f, 0.10f, 0.18f, 0.96f);
                _firstShipPopupCard.style.borderTopLeftRadius = 12;
                _firstShipPopupCard.style.borderTopRightRadius = 12;
                _firstShipPopupCard.style.borderBottomLeftRadius = 12;
                _firstShipPopupCard.style.borderBottomRightRadius = 12;
                _firstShipPopupCard.style.borderTopWidth = 1;
                _firstShipPopupCard.style.borderRightWidth = 1;
                _firstShipPopupCard.style.borderBottomWidth = 1;
                _firstShipPopupCard.style.borderLeftWidth = 1;

                var borderColor = new Color(0.45f, 0.75f, 1f, 0.65f);
                _firstShipPopupCard.style.borderTopColor = borderColor;
                _firstShipPopupCard.style.borderRightColor = borderColor;
                _firstShipPopupCard.style.borderBottomColor = borderColor;
                _firstShipPopupCard.style.borderLeftColor = borderColor;

                _firstShipPopupTitle = new Label { name = "firstShipPopupTitle", text = "Вот ваш первый корабль" };
                _firstShipPopupTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                _firstShipPopupTitle.style.fontSize = 28;
                _firstShipPopupTitle.style.marginBottom = 10;
                _firstShipPopupTitle.style.unityTextAlign = TextAnchor.MiddleCenter;

                _firstShipPopupText = new Label
                {
                    name = "firstShipPopupText",
                    text = "Отлично. Теперь нужно подготовить корабль к вылету."
                };
                _firstShipPopupText.style.whiteSpace = WhiteSpace.Normal;
                _firstShipPopupText.style.unityTextAlign = TextAnchor.MiddleCenter;
                _firstShipPopupText.style.marginBottom = 14;

                _firstShipPopupOk = new Button { name = "btnFirstShipPopupOk", text = "Понятно" };
                _firstShipPopupOk.style.height = 56;
                _firstShipPopupOk.style.alignSelf = Align.Center;
                _firstShipPopupOk.style.minWidth = 220;
                _firstShipPopupOk.clicked += OnFirstShipPopupOk;

                _firstShipPopupCard.Add(_firstShipPopupTitle);
                _firstShipPopupCard.Add(_firstShipPopupText);
                _firstShipPopupCard.Add(_firstShipPopupOk);
                _firstShipPopupLayer.Add(_firstShipPopupCard);
                root.Add(_firstShipPopupLayer);
            }
            else
            {
                _firstShipPopupCard = _firstShipPopupLayer.Q<VisualElement>("firstShipPopupCard");
                _firstShipPopupTitle = _firstShipPopupLayer.Q<Label>("firstShipPopupTitle");
                _firstShipPopupText = _firstShipPopupLayer.Q<Label>("firstShipPopupText");
                _firstShipPopupOk = _firstShipPopupLayer.Q<Button>("btnFirstShipPopupOk");

                if (_firstShipPopupOk != null)
                {
                    _firstShipPopupOk.clicked -= OnFirstShipPopupOk;
                    _firstShipPopupOk.clicked += OnFirstShipPopupOk;
                }
            }

            HideFirstShipPopup();
        }

        private bool IsFirstShipPopupVisible()
        {
            return _firstShipPopupLayer != null &&
                   _firstShipPopupLayer.style.display == DisplayStyle.Flex;
        }

        private void ShowFirstShipPopup()
        {
            if (_firstShipPopupLayer == null)
                return;

            _firstShipPopupLayer.style.display = DisplayStyle.Flex;
            _tutorial?.Hide();
            ShowAction("Вот ваш первый корабль. Нажмите «Понятно».");
        }

        private void HideFirstShipPopup()
        {
            if (_firstShipPopupLayer != null)
                _firstShipPopupLayer.style.display = DisplayStyle.None;
        }

        private void OnFirstShipPopupOk()
        {
            HideFirstShipPopup();
            SetTutorialStepAtLeast(TutorialStepFirstShipConfirmed);
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void BuildCraftModal(VisualElement root)
        {
            if (root == null)
                return;

            _craftModalLayer = root.Q<VisualElement>("craftModalLayer");
            if (_craftModalLayer == null)
            {
                _craftModalLayer = new VisualElement { name = "craftModalLayer" };
                _craftModalLayer.style.position = Position.Absolute;
                _craftModalLayer.style.left = 0;
                _craftModalLayer.style.top = 0;
                _craftModalLayer.style.right = 0;
                _craftModalLayer.style.bottom = 0;
                _craftModalLayer.style.justifyContent = Justify.Center;
                _craftModalLayer.style.alignItems = Align.Center;
                _craftModalLayer.style.backgroundColor = new Color(0f, 0f, 0f, 0.80f);
                _craftModalLayer.pickingMode = PickingMode.Position;
                _craftModalLayer.style.display = DisplayStyle.None;

                _craftModalCard = new VisualElement { name = "craftModalCard" };
                _craftModalCard.style.width = 1080;
                _craftModalCard.style.maxWidth = new Length(96, LengthUnit.Percent);
                _craftModalCard.style.maxHeight = new Length(92, LengthUnit.Percent);
                _craftModalCard.style.paddingLeft = 18;
                _craftModalCard.style.paddingRight = 18;
                _craftModalCard.style.paddingTop = 16;
                _craftModalCard.style.paddingBottom = 16;
                _craftModalCard.style.backgroundColor = new Color(0.06f, 0.10f, 0.18f, 0.96f);
                _craftModalCard.style.borderTopLeftRadius = 12;
                _craftModalCard.style.borderTopRightRadius = 12;
                _craftModalCard.style.borderBottomLeftRadius = 12;
                _craftModalCard.style.borderBottomRightRadius = 12;
                _craftModalCard.style.borderTopWidth = 1;
                _craftModalCard.style.borderRightWidth = 1;
                _craftModalCard.style.borderBottomWidth = 1;
                _craftModalCard.style.borderLeftWidth = 1;

                var borderColor = new Color(0.45f, 0.75f, 1f, 0.65f);
                _craftModalCard.style.borderTopColor = borderColor;
                _craftModalCard.style.borderRightColor = borderColor;
                _craftModalCard.style.borderBottomColor = borderColor;
                _craftModalCard.style.borderLeftColor = borderColor;
                _craftModalCard.style.flexDirection = FlexDirection.Column;

                _craftModalTitle = new Label { name = "craftModalTitle", text = "Крафт" };
                _craftModalTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                _craftModalTitle.style.fontSize = 28;
                _craftModalTitle.style.marginBottom = 8;
                _craftModalTitle.style.unityTextAlign = TextAnchor.MiddleCenter;

                _craftModalText = new Label
                {
                    name = "craftModalText",
                    text = "Выберите вкладку и чертёж, затем нажмите «Крафт»."
                };
                _craftModalText.style.whiteSpace = WhiteSpace.Normal;
                _craftModalText.style.unityTextAlign = TextAnchor.MiddleCenter;
                _craftModalText.style.marginBottom = 12;

                var tabsRow = new VisualElement { name = "craftTabsRow" };
                tabsRow.style.flexDirection = FlexDirection.Row;
                tabsRow.style.justifyContent = Justify.Center;
                tabsRow.style.alignItems = Align.Center;
                tabsRow.style.marginBottom = 10;

                _craftTabWeapons = new Button { name = "btnCraftTabWeapons", text = "Орудия" };
                _craftTabShields = new Button { name = "btnCraftTabShields", text = "Щиты" };
                _craftTabEngines = new Button { name = "btnCraftTabEngines", text = "Двигатели" };
                _craftTabModifiers = new Button { name = "btnCraftTabModifiers", text = "Модификаторы" };

                ConfigureCraftTabButton(_craftTabWeapons);
                ConfigureCraftTabButton(_craftTabShields);
                ConfigureCraftTabButton(_craftTabEngines);
                ConfigureCraftTabButton(_craftTabModifiers);

                tabsRow.Add(_craftTabWeapons);
                tabsRow.Add(_craftTabShields);
                tabsRow.Add(_craftTabEngines);
                tabsRow.Add(_craftTabModifiers);

                var bodyRow = new VisualElement { name = "craftBodyRow" };
                bodyRow.style.flexDirection = FlexDirection.Row;
                bodyRow.style.flexGrow = 1;
                bodyRow.style.minHeight = 420;

                var leftCol = new VisualElement { name = "craftBlueprintsCol" };
                leftCol.style.width = 360;
                leftCol.style.maxWidth = new Length(42, LengthUnit.Percent);
                leftCol.style.marginRight = 12;
                leftCol.style.paddingLeft = 10;
                leftCol.style.paddingRight = 10;
                leftCol.style.paddingTop = 10;
                leftCol.style.paddingBottom = 10;
                leftCol.style.backgroundColor = new Color(0.09f, 0.13f, 0.22f, 0.65f);
                leftCol.style.borderTopLeftRadius = 10;
                leftCol.style.borderTopRightRadius = 10;
                leftCol.style.borderBottomLeftRadius = 10;
                leftCol.style.borderBottomRightRadius = 10;

                var blueprintsTitle = new Label { name = "lblCraftBlueprintsTitle", text = "Чертежи" };
                blueprintsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                blueprintsTitle.style.marginBottom = 6;

                _craftBlueprintsList = new ListView { name = "craftBlueprintsList" };
                _craftBlueprintsList.style.flexGrow = 1;
                _craftBlueprintsList.style.minHeight = 320;

                leftCol.Add(blueprintsTitle);
                leftCol.Add(_craftBlueprintsList);

                var rightCol = new VisualElement { name = "craftDetailsCol" };
                rightCol.style.flexGrow = 1;
                rightCol.style.paddingLeft = 10;
                rightCol.style.paddingRight = 10;
                rightCol.style.paddingTop = 10;
                rightCol.style.paddingBottom = 10;
                rightCol.style.backgroundColor = new Color(0.09f, 0.13f, 0.22f, 0.65f);
                rightCol.style.borderTopLeftRadius = 10;
                rightCol.style.borderTopRightRadius = 10;
                rightCol.style.borderBottomLeftRadius = 10;
                rightCol.style.borderBottomRightRadius = 10;

                _craftModalBlueprintName = new Label { name = "craftModalBlueprintName", text = "Чертёж: —" };
                _craftModalBlueprintName.style.unityFontStyleAndWeight = FontStyle.Bold;
                _craftModalBlueprintName.style.fontSize = 23;
                _craftModalBlueprintName.style.marginBottom = 8;

                _craftModalChance = new Label { name = "craftModalChance", text = "Шанс: —" };
                _craftModalChance.style.whiteSpace = WhiteSpace.Normal;
                _craftModalChance.style.marginBottom = 8;

                _craftModalStats = new Label
                {
                    name = "craftModalStats",
                    text = "При крафте предмет получает случайные уровень, качество и характеристики."
                };
                _craftModalStats.style.whiteSpace = WhiteSpace.Normal;
                _craftModalStats.style.flexGrow = 1;

                rightCol.Add(_craftModalBlueprintName);
                rightCol.Add(_craftModalChance);
                rightCol.Add(_craftModalStats);

                bodyRow.Add(leftCol);
                bodyRow.Add(rightCol);

                var actionsRow = new VisualElement { name = "craftModalActionsRow" };
                actionsRow.style.flexDirection = FlexDirection.Row;
                actionsRow.style.justifyContent = Justify.Center;
                actionsRow.style.alignItems = Align.Center;
                actionsRow.style.marginTop = 12;

                _craftModalAction = new Button { name = "btnCraftModalAction", text = "Крафт" };
                _craftModalAction.style.height = 56;
                _craftModalAction.style.minWidth = 250;
                _craftModalAction.style.marginRight = 6;

                _craftModalClose = new Button { name = "btnCraftModalClose", text = "Закрыть" };
                _craftModalClose.style.height = 56;
                _craftModalClose.style.minWidth = 170;
                _craftModalClose.style.marginLeft = 6;

                actionsRow.Add(_craftModalAction);
                actionsRow.Add(_craftModalClose);

                _craftModalCard.Add(_craftModalTitle);
                _craftModalCard.Add(_craftModalText);
                _craftModalCard.Add(tabsRow);
                _craftModalCard.Add(bodyRow);
                _craftModalCard.Add(actionsRow);
                _craftModalLayer.Add(_craftModalCard);
                root.Add(_craftModalLayer);
            }
            else
            {
                _craftModalCard = _craftModalLayer.Q<VisualElement>("craftModalCard");
                _craftModalTitle = _craftModalLayer.Q<Label>("craftModalTitle");
                _craftModalText = _craftModalLayer.Q<Label>("craftModalText");
                _craftModalBlueprintName = _craftModalLayer.Q<Label>("craftModalBlueprintName");
                _craftModalChance = _craftModalLayer.Q<Label>("craftModalChance");
                _craftModalStats = _craftModalLayer.Q<Label>("craftModalStats");
                _craftBlueprintsList = _craftModalLayer.Q<ListView>("craftBlueprintsList");
                _craftTabWeapons = _craftModalLayer.Q<Button>("btnCraftTabWeapons");
                _craftTabShields = _craftModalLayer.Q<Button>("btnCraftTabShields");
                _craftTabEngines = _craftModalLayer.Q<Button>("btnCraftTabEngines");
                _craftTabModifiers = _craftModalLayer.Q<Button>("btnCraftTabModifiers");
                _craftModalAction = _craftModalLayer.Q<Button>("btnCraftModalAction");
                _craftModalClose = _craftModalLayer.Q<Button>("btnCraftModalClose");
            }

            if (_craftModalAction != null)
            {
                _craftModalAction.clicked -= OnCraftModalAction;
                _craftModalAction.clicked += OnCraftModalAction;
            }

            if (_craftModalClose != null)
            {
                _craftModalClose.clicked -= OnCraftModalClose;
                _craftModalClose.clicked += OnCraftModalClose;
            }

            if (_craftTabWeapons != null)
            {
                _craftTabWeapons.clicked -= OnCraftTabWeaponsClicked;
                _craftTabWeapons.clicked += OnCraftTabWeaponsClicked;
            }

            if (_craftTabShields != null)
            {
                _craftTabShields.clicked -= OnCraftTabShieldsClicked;
                _craftTabShields.clicked += OnCraftTabShieldsClicked;
            }

            if (_craftTabEngines != null)
            {
                _craftTabEngines.clicked -= OnCraftTabEnginesClicked;
                _craftTabEngines.clicked += OnCraftTabEnginesClicked;
            }

            if (_craftTabModifiers != null)
            {
                _craftTabModifiers.clicked -= OnCraftTabModifiersClicked;
                _craftTabModifiers.clicked += OnCraftTabModifiersClicked;
            }

            SetupCraftBlueprintListOnce();
            HideCraftModal();
        }

        private static void ConfigureCraftTabButton(Button btn)
        {
            if (btn == null)
                return;

            btn.style.height = 56;
            btn.style.minWidth = 220;
            btn.style.marginLeft = 4;
            btn.style.marginRight = 4;
            btn.style.marginBottom = 2;
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.whiteSpace = WhiteSpace.Normal;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private void SetupCraftBlueprintListOnce()
        {
            if (_craftBlueprintsList == null || _craftListSetupDone)
                return;

            _craftListSetupDone = true;
            _craftBlueprintsList.itemsSource = _craftBlueprints;
            _craftBlueprintsList.selectionType = SelectionType.Single;
            _craftBlueprintsList.fixedItemHeight = 68;

            _craftBlueprintsList.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Column;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.paddingTop = 8;
                row.style.paddingBottom = 8;

                var title = new Label { name = "title" };
                title.style.unityFontStyleAndWeight = FontStyle.Bold;

                var subtitle = new Label { name = "subtitle" };
                subtitle.style.opacity = 0.9f;

                row.Add(title);
                row.Add(subtitle);
                return row;
            };

            _craftBlueprintsList.bindItem = (ve, index) =>
            {
                if ((uint)index >= (uint)_craftBlueprints.Count)
                    return;

                var vm = _craftBlueprints[index];
                var title = ve.Q<Label>("title");
                var subtitle = ve.Q<Label>("subtitle");

                if (title != null) title.text = vm.Title;
                if (subtitle != null) subtitle.text = vm.Subtitle;
            };

            _craftBlueprintsList.selectionChanged -= OnCraftBlueprintSelectionChanged;
            _craftBlueprintsList.selectionChanged += OnCraftBlueprintSelectionChanged;
        }

        private void RebuildCraftBlueprints(bool keepCurrentSelection)
        {
            string keepId = keepCurrentSelection && _craftSelectedBlueprint != null
                ? _craftSelectedBlueprint.DefinitionId
                : null;

            _craftBlueprints.Clear();

            var cfg = GironoidApp.Config;
            var cat = cfg != null ? cfg.ItemCatalog : null;
            if (cat != null && cat.Items != null)
            {
                for (int i = 0; i < cat.Items.Length; i++)
                {
                    var def = cat.Items[i];
                    if (string.IsNullOrEmpty(def.Id))
                        continue;

                    if (def.Type != _craftSelectedType)
                        continue;

                    int ironCost = ResolveCraftIronCost(def.Id, def.Type);
                    float avgChance = HangarLogic.GetCraftAverageSuccessChance();
                    string displayName = string.IsNullOrWhiteSpace(def.NameRu) ? def.Id : def.NameRu;

                    _craftBlueprints.Add(new CraftBlueprintVm
                    {
                        DefinitionId = def.Id,
                        Type = def.Type,
                        NameRu = displayName,
                        IronCost = ironCost,
                        TokenCost = 0,
                        AverageChance = avgChance,
                        Title = displayName,
                        Subtitle = $"Железо: {ironCost} • Ср. шанс: {Mathf.RoundToInt(avgChance * 100f)}%"
                    });
                }
            }

            _craftBlueprints.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal));

            if (_craftBlueprintsList != null)
            {
                try { _craftBlueprintsList.Rebuild(); }
                catch { try { _craftBlueprintsList.RefreshItems(); } catch { } }
            }

            CraftBlueprintVm selected = null;
            if (!string.IsNullOrEmpty(keepId))
            {
                for (int i = 0; i < _craftBlueprints.Count; i++)
                {
                    if (_craftBlueprints[i].DefinitionId == keepId)
                    {
                        selected = _craftBlueprints[i];
                        break;
                    }
                }
            }

            if (selected == null && _craftBlueprints.Count > 0)
                selected = _craftBlueprints[0];

            _craftSelectedBlueprint = selected;

            if (_craftBlueprintsList != null)
            {
                _suppressCraftSelectionChanged = true;
                try
                {
                    if (selected == null)
                    {
                        _craftBlueprintsList.ClearSelection();
                        _craftBlueprintsList.SetSelectionWithoutNotify(new int[0]);
                    }
                    else
                    {
                        int idx = FindCraftBlueprintIndexByDefinitionId(selected.DefinitionId);
                        if (idx >= 0)
                            _craftBlueprintsList.SetSelectionWithoutNotify(new[] { idx });
                        else
                            _craftBlueprintsList.ClearSelection();
                    }
                }
                catch { }
                finally
                {
                    _suppressCraftSelectionChanged = false;
                }
            }
        }

        private int FindCraftBlueprintIndexByDefinitionId(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
                return -1;

            for (int i = 0; i < _craftBlueprints.Count; i++)
            {
                if (_craftBlueprints[i].DefinitionId == definitionId)
                    return i;
            }
            return -1;
        }

        private void OnCraftBlueprintSelectionChanged(IEnumerable<object> selected)
        {
            if (_suppressCraftSelectionChanged)
                return;

            CraftBlueprintVm vm = null;
            if (selected != null)
            {
                foreach (var o in selected)
                {
                    vm = o as CraftBlueprintVm;
                    if (vm != null)
                        break;
                }
            }

            _craftSelectedBlueprint = vm;
            RefreshCraftModalState();
            UpdateTutorialOverlay();
        }

        private void OnCraftTabWeaponsClicked()
        {
            if (_craftModalBusy)
                return;

            _craftSelectedType = ItemType.Weapon;
            RebuildCraftBlueprints(keepCurrentSelection: false);
            RefreshCraftModalState();
            UpdateTutorialOverlay();
        }

        private void OnCraftTabShieldsClicked()
        {
            if (_craftModalBusy)
                return;

            _craftSelectedType = ItemType.Shield;
            RebuildCraftBlueprints(keepCurrentSelection: false);
            RefreshCraftModalState();
            UpdateTutorialOverlay();
        }

        private void OnCraftTabEnginesClicked()
        {
            if (_craftModalBusy)
                return;

            _craftSelectedType = ItemType.Engine;
            RebuildCraftBlueprints(keepCurrentSelection: false);
            RefreshCraftModalState();
            UpdateTutorialOverlay();
        }

        private void OnCraftTabModifiersClicked()
        {
            if (_craftModalBusy)
                return;

            _craftSelectedType = ItemType.Modifier;
            RebuildCraftBlueprints(keepCurrentSelection: false);
            RefreshCraftModalState();
            UpdateTutorialOverlay();
        }

        private bool IsCraftModalVisible()
        {
            return _craftModalLayer != null &&
                   _craftModalLayer.style.display == DisplayStyle.Flex;
        }

        private void OpenCraftModal()
        {
            if (_craftModalLayer == null)
                return;

            _craftModalBusy = false;
            _craftSelectedType = ItemType.Engine;
            RebuildCraftBlueprints(keepCurrentSelection: false);
            RefreshCraftModalState();
            _craftModalLayer.style.display = DisplayStyle.Flex;
            _tutorial?.Hide();
        }

        private void HideCraftModal()
        {
            _craftModalBusy = false;
            if (_craftModalLayer != null)
                _craftModalLayer.style.display = DisplayStyle.None;
        }

        private void RefreshCraftModalState()
        {
            if (_craftModalTitle != null)
                _craftModalTitle.text = "Крафт";

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            bool canCraft = p != null && cfg != null && cfg.ItemCatalog != null && GironoidApp.Hangar != null;

            string requiredDefId = GetRequiredTutorialCraftDefinitionId(p, cfg);
            ItemType requiredType = GetRequiredCraftTypeByDefinition(requiredDefId, cfg);
            bool tutorialCraftActive = !string.IsNullOrEmpty(requiredDefId);

            if (tutorialCraftActive && requiredType != ItemType.None)
            {
                if (_craftTabWeapons != null) _craftTabWeapons.SetEnabled(requiredType == ItemType.Weapon && !_craftModalBusy);
                if (_craftTabShields != null) _craftTabShields.SetEnabled(false);
                if (_craftTabEngines != null) _craftTabEngines.SetEnabled(requiredType == ItemType.Engine && !_craftModalBusy);
                if (_craftTabModifiers != null) _craftTabModifiers.SetEnabled(false);
            }
            else
            {
                if (_craftTabWeapons != null) _craftTabWeapons.SetEnabled(!_craftModalBusy);
                if (_craftTabShields != null) _craftTabShields.SetEnabled(!_craftModalBusy);
                if (_craftTabEngines != null) _craftTabEngines.SetEnabled(!_craftModalBusy);
                if (_craftTabModifiers != null) _craftTabModifiers.SetEnabled(!_craftModalBusy);
            }

            RefreshCraftTabSelectionVisuals();

            bool hasSelected = _craftSelectedBlueprint != null;
            bool selectedIsRequired = hasSelected && (!tutorialCraftActive || _craftSelectedBlueprint.DefinitionId == requiredDefId);
            bool hasResources = canCraft && hasSelected && HasCraftResources(p, _craftSelectedBlueprint);

            if (_craftModalText != null)
            {
                if (!canCraft)
                {
                    _craftModalText.text = "Крафт временно недоступен.";
                }
                else if (tutorialCraftActive)
                {
                    string requiredName = GetCraftDefinitionDisplayName(cfg, requiredDefId);
                    if (_craftSelectedType != requiredType)
                    {
                        _craftModalText.text = $"Шаг обучения: откройте вкладку «{ItemTypeToRu(requiredType)}».";
                    }
                    else if (!selectedIsRequired)
                    {
                        _craftModalText.text = $"Шаг обучения: выберите чертёж «{requiredName}».";
                    }
                    else
                    {
                        _craftModalText.text =
                            "Нажмите «Крафт». В обычном режиме шанс зависит от качества предмета: выше качество, ниже шанс.";
                    }
                }
                else
                {
                    _craftModalText.text = "Выберите чертёж и нажмите «Крафт».";
                }
            }

            if (_craftModalBlueprintName != null)
            {
                _craftModalBlueprintName.text = hasSelected
                    ? $"Чертёж: {_craftSelectedBlueprint.NameRu}"
                    : "Чертёж: —";
            }

            if (_craftModalChance != null)
            {
                if (!hasSelected)
                    _craftModalChance.text = "Шанс создания: —";
                else if (tutorialCraftActive && selectedIsRequired)
                    _craftModalChance.text = "Шанс создания: 100% (обучение).";
                else
                    _craftModalChance.text =
                        $"Шанс: Common 92%, Uncommon 78%, Rare 56%, Epic 34%, Legendary 18% (средний ~{Mathf.RoundToInt(_craftSelectedBlueprint.AverageChance * 100f)}%).";
            }

            if (_craftModalStats != null)
                _craftModalStats.text = BuildCraftStatsText(cfg, _craftSelectedBlueprint);

            if (_craftModalAction != null)
            {
                _craftModalAction.text = tutorialCraftActive ? "Крафт (обучение)" : "Крафт";
                bool actionable = !_craftModalBusy && canCraft && hasSelected && selectedIsRequired && hasResources;
                _craftModalAction.SetEnabled(actionable);
            }

            if (_craftModalClose != null)
                _craftModalClose.SetEnabled(!_craftModalBusy && !tutorialCraftActive);
        }

        private void OnCraftModalAction()
        {
            if (_craftModalBusy)
                return;

            _craftModalBusy = true;
            RefreshCraftModalState();

            try
            {
                if (TryCraftSelectedBlueprint(out var msg))
                    ShowAction(msg);
                else
                    ShowAction(string.IsNullOrEmpty(msg) ? "Ошибка крафта." : msg);
            }
            finally
            {
                _craftModalBusy = false;
                RefreshCraftModalState();
                InvalidateInventoryAndEquippedCaches();
                RefreshAll(force: true);
                ShowOnboardingHint();
                UpdateTutorialOverlay();
            }
        }

        private void OnCraftModalClose()
        {
            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p != null && cfg != null)
            {
                if (!string.IsNullOrEmpty(GetRequiredTutorialCraftDefinitionId(p, cfg)))
                {
                    ShowAction("Сначала создайте базовый двигатель и базовое орудие.");
                    RefreshCraftModalState();
                    UpdateTutorialOverlay();
                    return;
                }
            }

            HideCraftModal();
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private bool TryCraftSelectedBlueprint(out string message)
        {
            message = "";

            if (!GironoidApp.IsReady || GironoidApp.Hangar == null)
            {
                message = "Крафт недоступен.";
                return false;
            }

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p == null || cfg == null || cfg.ItemCatalog == null)
            {
                message = "ItemCatalog не задан — крафт недоступен.";
                return false;
            }

            if (_craftSelectedBlueprint == null)
            {
                message = "Сначала выберите чертёж.";
                return false;
            }

            var defId = _craftSelectedBlueprint.DefinitionId;
            string requiredDefId = GetRequiredTutorialCraftDefinitionId(p, cfg);
            bool tutorialCraftActive = !string.IsNullOrEmpty(requiredDefId);

            if (tutorialCraftActive && !string.Equals(defId, requiredDefId, StringComparison.Ordinal))
            {
                message = $"По обучению сейчас нужно создать: {GetCraftDefinitionDisplayName(cfg, requiredDefId)}.";
                return false;
            }

            int ironCost = ResolveCraftIronCost(defId, _craftSelectedBlueprint.Type);
            int tokenCost = 0;
            if (p.Iron < ironCost)
            {
                message = "Недостаточно железа для крафта.";
                return false;
            }

            HangarLogic.Result res;
            ItemInstance crafted;
            if (tutorialCraftActive)
            {
                res = GironoidApp.Hangar.CraftItemWithChanceOverride(
                    defId,
                    ironCost,
                    tokenCost,
                    successChanceOverride: 1f,
                    out crafted,
                    flushToServer: ShouldFlushToServer());
            }
            else
            {
                res = GironoidApp.Hangar.CraftItem(
                    defId,
                    ironCost,
                    tokenCost,
                    out crafted,
                    flushToServer: ShouldFlushToServer());
            }

            if (!res.Ok)
            {
                message = string.IsNullOrEmpty(res.Error) ? "Ошибка крафта." : res.Error;
                return false;
            }

            if (crafted == null)
            {
                message = "Крафт не завершён.";
                return false;
            }

            if (string.Equals(crafted.DefinitionId, cfg.ItemCatalog.StarterEngineDefinitionId, StringComparison.Ordinal))
                SetTutorialStepAtLeast(TutorialStepStarterEngineCrafted);

            if (string.Equals(crafted.DefinitionId, cfg.ItemCatalog.StarterWeaponDefinitionId, StringComparison.Ordinal))
                SetTutorialStepAtLeast(TutorialStepStarterWeaponCrafted);

            RebuildCraftBlueprints(keepCurrentSelection: true);
            RefreshCraftModalState();

            message = $"Создано: {crafted.DefinitionId} • Lv{crafted.Level} • {crafted.Quality}";
            return true;
        }

        private void RefreshCraftTabSelectionVisuals()
        {
            SetCraftTabSelected(_craftTabWeapons, _craftSelectedType == ItemType.Weapon);
            SetCraftTabSelected(_craftTabShields, _craftSelectedType == ItemType.Shield);
            SetCraftTabSelected(_craftTabEngines, _craftSelectedType == ItemType.Engine);
            SetCraftTabSelected(_craftTabModifiers, _craftSelectedType == ItemType.Modifier);
        }

        private static void SetCraftTabSelected(Button btn, bool selected)
        {
            if (btn == null)
                return;

            btn.EnableInClassList("btn--selected", selected);
        }

        private static bool HasCraftResources(PlayerProfile p, CraftBlueprintVm vm)
        {
            if (p == null || vm == null)
                return false;

            return p.Iron >= vm.IronCost && p.Tokens >= vm.TokenCost;
        }

        private static string BuildCraftStatsText(GameConfig cfg, CraftBlueprintVm vm)
        {
            if (cfg == null || cfg.ItemCatalog == null || vm == null)
                return "Выберите чертёж. На выходе предмет получает случайные уровень, качество и характеристики.";

            if (!cfg.ItemCatalog.TryGet(vm.DefinitionId, out var def))
                return "Описание чертежа недоступно.";

            var sb = new StringBuilder(320);
            sb.Append("Тип: ").Append(ItemTypeToRu(def.Type)).Append('\n');
            sb.Append("Стоимость: ").Append(vm.IronCost).Append(" железа").Append('\n');
            sb.Append("Результат: случайные уровень, качество и статы.").Append('\n');

            if (def.Type == ItemType.Engine)
            {
                sb.Append("Скорость +").Append(def.MoveSpeedBonus.ToString("0.##")).Append('\n');
                sb.Append("Ускорение +").Append(def.AccelerationBonus.ToString("0.##"));
            }
            else if (def.Type == ItemType.Weapon)
            {
                sb.Append("Урон ").Append(def.Damage.ToString("0.##")).Append('\n');
                sb.Append("Скорострельность ").Append(def.FireRate.ToString("0.###"));
            }
            else if (def.Type == ItemType.Shield)
            {
                sb.Append("Щит +").Append(def.ShieldMaxBonus.ToString("0.##")).Append('\n');
                sb.Append("Реген +").Append(def.ShieldRegenBonus.ToString("0.##"));
            }
            else if (def.Type == ItemType.Modifier)
            {
                sb.Append("Множитель урона x").Append(def.DamageMultiplier.ToString("0.##")).Append('\n');
                sb.Append("Множитель скорострельности x").Append(def.FireRateMultiplier.ToString("0.##"));
            }

            return sb.ToString();
        }

        private int ResolveCraftIronCost(string definitionId, ItemType type)
        {
            var cfg = GironoidApp.Config;
            var cat = cfg != null ? cfg.ItemCatalog : null;
            if (cat != null)
            {
                if (!string.IsNullOrEmpty(cat.StarterEngineDefinitionId) &&
                    string.Equals(definitionId, cat.StarterEngineDefinitionId, StringComparison.Ordinal))
                    return StarterCraftIronCost;

                if (!string.IsNullOrEmpty(cat.StarterWeaponDefinitionId) &&
                    string.Equals(definitionId, cat.StarterWeaponDefinitionId, StringComparison.Ordinal))
                    return StarterCraftIronCost;
            }

            switch (type)
            {
                case ItemType.Shield: return DefaultCraftIronCost + 5;
                case ItemType.Modifier: return DefaultCraftIronCost + 10;
                case ItemType.Weapon:
                case ItemType.Engine:
                default:
                    return DefaultCraftIronCost;
            }
        }

        private static string GetCraftDefinitionDisplayName(GameConfig cfg, string definitionId)
        {
            if (cfg != null && cfg.ItemCatalog != null && !string.IsNullOrEmpty(definitionId))
            {
                if (cfg.ItemCatalog.TryGet(definitionId, out var def) && !string.IsNullOrWhiteSpace(def.NameRu))
                    return def.NameRu;
            }

            return string.IsNullOrEmpty(definitionId) ? "—" : definitionId;
        }

        private static ItemType GetRequiredCraftTypeByDefinition(string definitionId, GameConfig cfg)
        {
            if (cfg == null || cfg.ItemCatalog == null || string.IsNullOrEmpty(definitionId))
                return ItemType.None;

            if (cfg.ItemCatalog.TryGet(definitionId, out var def))
                return def.Type;

            return ItemType.None;
        }

        private static string ItemTypeToRu(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return "Орудия";
                case ItemType.Shield: return "Щиты";
                case ItemType.Engine: return "Двигатели";
                case ItemType.Modifier: return "Модификаторы";
                default: return "Предметы";
            }
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
            {
                _btnShopClose = _shopModalRoot.Q<Button>("btnShopClose");
                if (_btnShopClose != null)
                {
                    _btnShopClose.clicked -= OnShopCloseClickedFallback;
                    _btnShopClose.clicked += OnShopCloseClickedFallback;
                }
            }

            if (_shopService != null)
                _shopService.OnProfileChanged += OnShopProfileChanged;

            // 2) Если модалки в UXML нет — готовим EXTERNAL UIDocument магазина (UI_shop).
            if ((_shopModal == null || !_shopModal.IsBound) && (_shopUi == null) && _autoFindExternalShopUi)
            {
                _shopUi = TryFindExternalShopDocument();
            }

            if (_shopUi != null)
            {
                EnsureExternalShopBackBinding();

                // По умолчанию держим внешний магазин выключенным, если он есть в сцене.
                // (Если вам нужно иначе — просто снимите SetActive в инспекторе/коде.)
                if (_shopUi.gameObject.activeSelf)
                    _shopUi.gameObject.SetActive(false);
            }
        }

        private void EnsureExternalShopBackBinding()
        {
            if (_shopUi == null)
                return;

            var root = _shopUi.rootVisualElement;
            if (root == null)
                return;

            _shopExternalRoot = root;

            var back =
                root.Q<Button>("btnBack")
                ?? root.Q<Button>("btnClose")
                ?? root.Q<Button>("btnShopClose");

            if (back == null)
                return;

            if (!ReferenceEquals(_btnShopExternalBack, back) && _btnShopExternalBack != null)
                _btnShopExternalBack.clicked -= CloseExternalShop;

            _btnShopExternalBack = back;
            _btnShopExternalBack.clicked -= CloseExternalShop;
            _btnShopExternalBack.clicked += CloseExternalShop;
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
            if (p != null)
            {
                bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;
                if (hasShip)
                {
                    if (p.TutorialStep < TutorialStepShipPurchased)
                        SetTutorialStepAtLeast(TutorialStepShipPurchased);

                    if (IsShopOpen())
                        SetShopCloseAllowed(true);
                }
            }

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

            var requiredType = GetRequiredTutorialEquipType(profile, shipId);
            if (requiredType != ItemType.None && _selectedItem.Type != requiredType)
            {
                ShowAction(requiredType == ItemType.Engine
                    ? "Сначала установите двигатель."
                    : "Сначала установите оружие.");
                UpdateTutorialOverlay();
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
            if (type == ItemType.Engine)
                SetTutorialStepAtLeast(TutorialStepStarterEngineEquipped);

            if (type == ItemType.Weapon)
            {
                var p = GironoidApp.Profile;
                var shipId = ResolveSelectedShipId(p, GironoidApp.Config);
                if (HasAnyEquipped(p, shipId, ItemType.Engine))
                    SetTutorialStepAtLeast(TutorialStepStarterWeaponEquipped);
            }
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
                SetTutorialStepExact(TutorialStepBuyFirstShip);
                UpdateTutorialOverlay();
                return;
            }

            if (!HasRequiredModules(p, shipId, out var err))
            {
                ShowAction(err);
                UpdateTutorialOverlay();
                return;
            }

            SetTutorialStepAtLeast(TutorialStepHangarCompleted);
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

            SetTutorialStepAtLeast(TutorialStepBuyFirstShip);

            // 1) Если модальный магазин корректно привязан — используем его.
            if (_shopModal != null && _shopModal.IsBound)
            {
                _shopModal.Open();
                _shopWasOpenLastTick = true;

                // Кнопка возврата из магазина должна всегда оставаться рабочей.
                SetShopCloseAllowed(true);

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
            _shopWasOpenLastTick = true;

            // Скрываем ангарный UI, чтобы не было кликов “сквозь” магазин.
            if (_root != null)
                _root.style.display = DisplayStyle.None;

            // Отключаем туториал-оверлей (он в дереве ангара).
            _tutorial?.Hide();

            _shopUi.gameObject.SetActive(true);
            EnsureExternalShopBackBinding();
            if (_root != null)
                _root.schedule.Execute(EnsureExternalShopBackBinding).ExecuteLater(0);

            ShowAction("Открыт магазин (отдельный экран).");
        }

        private void OnShopCloseClickedFallback()
        {
            // Защита: если встроенная привязка ShopModalController по какой-то причине потерялась,
            // закрываем модалку отсюда.
            if (_shopModal != null && _shopModal.IsBound && _shopModal.IsOpen)
                _shopModal.Close();

            var p = GironoidApp.Profile;
            bool hasShip = p != null && p.OwnedShips != null && p.OwnedShips.Count > 0;
            if (hasShip && p != null && p.TutorialStep == TutorialStepShipPurchased && !IsFirstShipPopupVisible())
                ShowFirstShipPopup();

            RefreshAll(force: true);
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void CloseExternalShop()
        {
            _externalShopOpen = false;
            _shopWasOpenLastTick = false;

            if (_shopUi != null)
                _shopUi.gameObject.SetActive(false);

            if (_root != null)
                _root.style.display = DisplayStyle.Flex;

            if (GironoidApp.IsReady && GironoidApp.Profile != null &&
                GironoidApp.Profile.TutorialStep == TutorialStepShipPurchased &&
                GironoidApp.Profile.OwnedShips != null &&
                GironoidApp.Profile.OwnedShips.Count > 0)
            {
                ShowFirstShipPopup();
            }

            RefreshAll(force: true);
            ShowOnboardingHint();
            UpdateTutorialOverlay();
        }

        private void SetShopCloseAllowed(bool allowed)
        {
            if (_btnShopClose != null)
                _btnShopClose.SetEnabled(allowed);

            if (_btnShopExternalBack != null)
                _btnShopExternalBack.SetEnabled(allowed);
        }

        private bool IsShopOpen()
        {
            // модальный магазин
            if (_shopModal != null && _shopModal.IsOpen)
                return true;

            // внешний магазин
            return _externalShopOpen;
        }

        private bool TryGetShopCloseTarget(out VisualElement target)
        {
            target = null;

            if (_externalShopOpen)
            {
                if (_btnShopExternalBack != null)
                {
                    target = _btnShopExternalBack;
                    return true;
                }

                return false;
            }

            if (_btnShopClose != null)
            {
                target = _btnShopClose;
                return true;
            }

            if (_shopModalRoot != null)
            {
                var fallback = _shopModalRoot.Q<Button>("btnBack");
                if (fallback != null)
                {
                    target = fallback;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetShopPrimaryActionTarget(out VisualElement target)
        {
            target = null;

            if (_shopModalRoot == null)
                return false;

            // 1) Advanced-shop: кнопка "Купить" справа.
            var buy = _shopModalRoot.Q<Button>("btnShopBuy");
            if (buy != null)
            {
                target = buy;
                return true;
            }

            // 2) Пытаемся найти первую кнопку покупки в legacy-списке
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

            // 3) Если нет кнопки покупки — подсветим список кораблей
            var shipsList = _shopModalRoot.Q<ListView>("shopShipsList");
            if (shipsList != null)
            {
                target = shipsList;
                return true;
            }

            // 4) Если нет списка/кнопок — подсветим rewarded (если есть)
            var watch = _shopModalRoot.Q<Button>("btnWatchAdTokens");
            if (watch != null)
            {
                target = watch;
                return true;
            }

            var watchModern = _shopModalRoot.Q<Button>("btnWatchAd");
            if (watchModern != null)
            {
                target = watchModern;
                return true;
            }

            // 5) Фоллбек — весь модал
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
                SetTutorialStepExact(TutorialStepBuyFirstShip);
                UpdateTutorialOverlay();
                return;
            }

            if (IsFirstShipPopupVisible())
            {
                ShowAction("Сначала нажмите «Понятно» в подсказке о первом корабле.");
                UpdateTutorialOverlay();
                return;
            }

            if (p.TutorialStep < TutorialStepFirstShipConfirmed)
            {
                ShowAction("Сначала завершите этап покупки первого корабля.");
                UpdateTutorialOverlay();
                return;
            }

            OpenCraftModal();
            ShowOnboardingHint();
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

            var cfg = GironoidApp.Config;
            bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;

            if (!hasShip)
            {
                ShowAction("Шаг 1: Откройте «Магазин» и купите первый корабль.");
                return;
            }

            if (p.TutorialStep <= TutorialStepShipPurchased && IsShopOpen())
            {
                ShowAction("Корабль куплен. Закройте магазин, чтобы продолжить обучение.");
                return;
            }

            if (IsFirstShipPopupVisible())
            {
                ShowAction("Нажмите «Понятно», чтобы перейти к настройке корабля.");
                return;
            }

            if (p.TutorialStep <= TutorialStepShipPurchased)
            {
                ShowAction("Завершите шаг с первым кораблём: откройте магазин, купите корабль и закройте окно магазина.");
                return;
            }

            if (IsCraftModalVisible())
            {
                string requiredDefId = GetRequiredTutorialCraftDefinitionId(p, cfg);
                if (!string.IsNullOrEmpty(requiredDefId))
                {
                    var requiredType = GetRequiredCraftTypeByDefinition(requiredDefId, cfg);
                    string requiredName = GetCraftDefinitionDisplayName(cfg, requiredDefId);

                    if (_craftSelectedType != requiredType)
                    {
                        ShowAction($"Крафт: выберите вкладку «{ItemTypeToRu(requiredType)}».");
                        return;
                    }

                    if (_craftSelectedBlueprint == null || _craftSelectedBlueprint.DefinitionId != requiredDefId)
                    {
                        ShowAction($"Крафт: выберите чертёж «{requiredName}».");
                        return;
                    }

                    ShowAction($"Крафт: нажмите «Крафт» для «{requiredName}». В обучении шанс 100%.");
                    return;
                }

                ShowAction("Закройте окно крафта и перейдите к установке модулей.");
                return;
            }

            string requiredCraftDef = GetRequiredTutorialCraftDefinitionId(p, cfg);
            if (!string.IsNullOrEmpty(requiredCraftDef))
            {
                string requiredName = GetCraftDefinitionDisplayName(cfg, requiredCraftDef);
                ShowAction($"Шаг 3: Нажмите «Крафт» и создайте «{requiredName}».");
                return;
            }

            var shipId = ResolveSelectedShipId(p, cfg);

            if (!HasAnyEquipped(p, shipId, ItemType.Engine))
            {
                ShowAction("Шаг 4: Установите двигатель (E1 → выбрать двигатель в списке → Установить).");
                return;
            }

            if (!HasAnyEquipped(p, shipId, ItemType.Weapon))
            {
                ShowAction("Шаг 5: Установите оружие (W1 → выбрать орудие в списке → Установить).");
                return;
            }

            ShowAction("Шаг 6: Нажмите «Готово» для перехода на звёздную карту.");
        }

        private void UpdateTutorialOverlay()
        {
            if (_tutorial == null || !GironoidApp.IsReady)
                return;

            var p = GironoidApp.Profile;
            var cfg = GironoidApp.Config;
            if (p == null || cfg == null)
                return;

            // Для внешнего магазина root ангара скрыт, поэтому оверлей неактуален.
            if (_externalShopOpen)
            {
                _tutorial.Hide();
                return;
            }

            if (IsFirstShipPopupVisible())
            {
                if (_firstShipPopupOk != null)
                {
                    _tutorial.Show(_firstShipPopupOk, "Нажмите «Понятно», чтобы перейти к крафту.");
                    return;
                }
                _tutorial.Hide();
                return;
            }

            bool hasShip = p.OwnedShips != null && p.OwnedShips.Count > 0;
            bool shopOpen = IsShopOpen();

            if (!hasShip)
            {
                if (shopOpen)
                {
                    SetShopCloseAllowed(true);
                    if (TryGetShopPrimaryActionTarget(out var buyTarget) && buyTarget != null)
                    {
                        _tutorial.Show(
                            buyTarget,
                            "Купите первый корабль. При необходимости можно вернуться в ангар.",
                            padding: 12f,
                            tooltipOffset: 12f,
                            blockInput: false);
                        return;
                    }
                }
                else
                {
                    SetShopCloseAllowed(true);
                    if (_btnShop != null)
                    {
                        _tutorial.Show(_btnShop, "Нажмите «Магазин». Во время обучения можно нажимать только подсвеченную кнопку.");
                        return;
                    }
                }

                _tutorial.Hide();
                return;
            }

            if (shopOpen)
            {
                SetShopCloseAllowed(true);

                if (p.TutorialStep <= TutorialStepShipPurchased &&
                    TryGetShopCloseTarget(out var closeTarget) &&
                    closeTarget != null)
                {
                    _tutorial.Show(
                        closeTarget,
                        "Корабль куплен. Закройте магазин, чтобы продолжить обучение.",
                        padding: 12f,
                        tooltipOffset: 12f,
                        blockInput: false);
                    return;
                }

                _tutorial.Hide();
                return;
            }

            SetShopCloseAllowed(true);

            if (p.TutorialStep <= TutorialStepShipPurchased)
            {
                _tutorial.Hide();
                return;
            }

            if (IsCraftModalVisible())
            {
                string requiredDefId = GetRequiredTutorialCraftDefinitionId(p, cfg);
                if (!string.IsNullOrEmpty(requiredDefId))
                {
                    ItemType requiredType = GetRequiredCraftTypeByDefinition(requiredDefId, cfg);
                    string requiredName = GetCraftDefinitionDisplayName(cfg, requiredDefId);

                    if (_craftSelectedType != requiredType)
                    {
                        var tabTarget = GetCraftTabButton(requiredType);
                        if (tabTarget != null)
                        {
                            _tutorial.Show(tabTarget, $"Откройте вкладку «{ItemTypeToRu(requiredType)}».");
                            return;
                        }
                    }

                    if (_craftSelectedBlueprint == null || _craftSelectedBlueprint.DefinitionId != requiredDefId)
                    {
                        if (_craftBlueprintsList != null)
                        {
                            _tutorial.Show(_craftBlueprintsList, $"Выберите чертёж «{requiredName}».");
                            return;
                        }
                    }

                    if (_craftModalAction != null)
                    {
                        _tutorial.Show(_craftModalAction, "Нажмите «Крафт». Во время обучения шанс успеха 100%.");
                        return;
                    }
                }

                if (_craftModalClose != null)
                {
                    _tutorial.Show(_craftModalClose, "Закройте окно крафта, чтобы вернуться в ангар.");
                    return;
                }

                _tutorial.Hide();
                return;
            }

            string requiredCraftDef = GetRequiredTutorialCraftDefinitionId(p, cfg);
            if (!string.IsNullOrEmpty(requiredCraftDef))
            {
                if (_btnCraft != null)
                    _tutorial.Show(_btnCraft, $"Нажмите «Крафт», чтобы создать «{GetCraftDefinitionDisplayName(cfg, requiredCraftDef)}».");
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
                {
                    if (_selectedItem == null || _selectedItem.Type != ItemType.Engine)
                        _tutorial.Show(_inventoryList, "Выберите двигатель в списке справа.");
                    else if (_btnEquip != null)
                        _tutorial.Show(_btnEquip, "Нажмите «Установить», чтобы поставить двигатель.");
                    else
                        _tutorial.Show(_inventoryList, "Подтвердите установку двигателя.");
                }
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
                {
                    if (_selectedItem == null || _selectedItem.Type != ItemType.Weapon)
                        _tutorial.Show(_inventoryList, "Выберите оружие в списке справа.");
                    else if (_btnEquip != null)
                        _tutorial.Show(_btnEquip, "Нажмите «Установить», чтобы поставить оружие.");
                    else
                        _tutorial.Show(_inventoryList, "Подтвердите установку оружия.");
                }
                return;
            }

            if (_btnFinish != null)
                _tutorial.Show(_btnFinish, "Нажмите «Готово» для перехода на звёздную карту.");

            SetTutorialStepAtLeast(TutorialStepStarterWeaponEquipped);
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

        private void SetTutorialStepExact(int step)
        {
            var ps = GironoidApp.ProfileService;
            if (ps == null || ps.Profile == null)
                return;

            if (ps.Profile.TutorialStep == step)
                return;

            ps.Apply(p => p.TutorialStep = step, flushToServer: ShouldFlushToServer());
        }

        private string GetRequiredTutorialCraftDefinitionId(PlayerProfile p, GameConfig cfg)
        {
            if (p == null || cfg == null || cfg.ItemCatalog == null)
                return null;

            var cat = cfg.ItemCatalog;

            if (!HasStarterEngineCrafted(p))
                return cat.StarterEngineDefinitionId;

            if (!HasStarterWeaponCrafted(p))
                return cat.StarterWeaponDefinitionId;

            return null;
        }

        private bool HasStarterEngineCrafted(PlayerProfile p)
        {
            var cfg = GironoidApp.Config;
            if (cfg == null || cfg.ItemCatalog == null)
                return HasAnyItemOfType(p, ItemType.Engine);

            return HasDefinitionInInventory(p, cfg.ItemCatalog.StarterEngineDefinitionId);
        }

        private bool HasStarterWeaponCrafted(PlayerProfile p)
        {
            var cfg = GironoidApp.Config;
            if (cfg == null || cfg.ItemCatalog == null)
                return HasAnyItemOfType(p, ItemType.Weapon);

            return HasDefinitionInInventory(p, cfg.ItemCatalog.StarterWeaponDefinitionId);
        }

        private static bool HasDefinitionInInventory(PlayerProfile p, string definitionId)
        {
            if (p == null || p.Inventory == null || string.IsNullOrEmpty(definitionId))
                return false;

            for (int i = 0; i < p.Inventory.Count; i++)
            {
                var it = p.Inventory[i];
                if (it != null && string.Equals(it.DefinitionId, definitionId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static ItemType GetRequiredTutorialEquipType(PlayerProfile p, string shipId)
        {
            if (p == null || string.IsNullOrEmpty(shipId))
                return ItemType.None;

            if (!HasAnyEquipped(p, shipId, ItemType.Engine))
                return ItemType.Engine;

            if (!HasAnyEquipped(p, shipId, ItemType.Weapon))
                return ItemType.Weapon;

            return ItemType.None;
        }

        private Button GetCraftTabButton(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return _craftTabWeapons;
                case ItemType.Shield: return _craftTabShields;
                case ItemType.Engine: return _craftTabEngines;
                case ItemType.Modifier: return _craftTabModifiers;
                default: return null;
            }
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
        private sealed class CraftBlueprintVm
        {
            public string DefinitionId;
            public string NameRu;
            public ItemType Type;
            public int IronCost;
            public int TokenCost;
            public float AverageChance;
            public string Title;
            public string Subtitle;
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
