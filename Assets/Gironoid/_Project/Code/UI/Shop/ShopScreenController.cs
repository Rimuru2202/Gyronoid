// Assets/Gironoid/_Project/Code/UI/Shop/ShopScreenController.cs
using UnityEngine;
using UnityEngine.UIElements;
using Gironoid._Project.Code.Core.Services;
using Gironoid._Project.Code.UI.Shared;

namespace Gironoid._Project.Code.UI.Shop
{
    /// <summary>
    /// Экран магазина как отдельный контроллер (UIDocument).
    /// Если вы делаете магазин модалкой внутри ангара — этот класс можно не использовать.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument _ui;
        [SerializeField] private ShopServiceBehaviour _shopService;

        private VisualElement _root;
        private ShopModalController _modal;

        private void Awake()
        {
            if (_ui == null)
                _ui = GetComponent<UIDocument>();

            if (_shopService == null)
                _shopService = FindObjectOfType<ShopServiceBehaviour>(true);

            _modal = new ShopModalController();
        }

        private void OnEnable()
        {
            if (_ui == null || _ui.rootVisualElement == null) return;

            _root = _ui.rootVisualElement;

            _modal.Bind(_root, _shopService);
            _modal.Open();
        }

        private void OnDisable()
        {
            _modal?.Dispose();
        }
    }
}