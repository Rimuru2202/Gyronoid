using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gironoid._Project.Code.UI.Shared
{
    /// <summary>
    /// Overlay для обучения: блокирует клики по всему UI, кроме подсвеченного элемента.
    /// Важно: сам слой overlay НЕ должен перехватывать клики (PickingMode.Ignore),
    /// клики блокируются четырьмя "диммерами" вокруг подсвеченной области.
    /// </summary>
    public sealed class TutorialOverlay : IDisposable
    {
        private readonly VisualElement _root;
        private readonly VisualElement _layer;

        private VisualElement _dimTop;
        private VisualElement _dimLeft;
        private VisualElement _dimRight;
        private VisualElement _dimBottom;

        private VisualElement _highlight;

        private VisualElement _tooltip;
        private VisualElement _tooltipBox;
        private Label _tooltipLabel;

        private VisualElement _target;
        private float _padding;

        private bool _shown;

        public TutorialOverlay(VisualElement root, VisualElement layer)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            if (layer == null)
            {
                layer = new VisualElement { name = "tutorialLayer" };
                _root.Add(layer);
            }

            _layer = layer;

            BuildUi();
            Hide();
        }

        public void Dispose()
        {
            Hide();

            // Ничего агрессивно не удаляем из дерева, чтобы не ломать UXML.
            // Просто отключаем.
        }

        public void Hide()
        {
            _shown = false;
            _target = null;

            if (_layer != null)
                _layer.style.display = DisplayStyle.None;
        }

        public void Show(VisualElement target, string text, float padding = 12f, float tooltipOffset = 12f)
        {
            if (target == null)
            {
                Hide();
                return;
            }

            _target = target;
            _padding = Mathf.Max(0f, padding);
            _shown = true;

            if (_tooltipLabel != null)
                _tooltipLabel.text = text ?? "";

            _layer.style.display = DisplayStyle.Flex;

            // Ждём, когда UI точно разложится, и затем позиционируем.
            _root.schedule.Execute(() =>
            {
                if (_shown)
                    UpdateLayout(tooltipOffset);
            }).ExecuteLater(0);
        }

        private void BuildUi()
        {
            _layer.Clear();

            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.top = 0;
            _layer.style.right = 0;
            _layer.style.bottom = 0;

            // Ключевой момент: сам overlay-слой НЕ должен быть pickable,
            // иначе он съест клики даже в "дырке".
            _layer.pickingMode = PickingMode.Ignore;

            _dimTop = MakeDim("tutorialDimTop");
            _dimLeft = MakeDim("tutorialDimLeft");
            _dimRight = MakeDim("tutorialDimRight");
            _dimBottom = MakeDim("tutorialDimBottom");

            _highlight = new VisualElement { name = "tutorialHighlight" };
            _highlight.style.position = Position.Absolute;
            _highlight.style.backgroundColor = new Color(0, 0, 0, 0);
            _highlight.style.borderTopWidth = 2;
            _highlight.style.borderRightWidth = 2;
            _highlight.style.borderBottomWidth = 2;
            _highlight.style.borderLeftWidth = 2;

            var c = new Color(0.45f, 0.75f, 1f, 1f);
            _highlight.style.borderTopColor = c;
            _highlight.style.borderRightColor = c;
            _highlight.style.borderBottomColor = c;
            _highlight.style.borderLeftColor = c;

            _highlight.style.borderTopLeftRadius = 10;
            _highlight.style.borderTopRightRadius = 10;
            _highlight.style.borderBottomLeftRadius = 10;
            _highlight.style.borderBottomRightRadius = 10;

            // Рамка не должна мешать клику по кнопке.
            _highlight.pickingMode = PickingMode.Ignore;

            _layer.Add(_highlight);

            _tooltip = new VisualElement { name = "tutorialTooltip" };
            _tooltip.style.position = Position.Absolute;
            _tooltip.pickingMode = PickingMode.Ignore; // тултип тоже не должен перехватывать клики

            _tooltipBox = new VisualElement { name = "tutorialTooltipBox" };
            _tooltipBox.style.flexDirection = FlexDirection.Column;
            _tooltipBox.style.paddingLeft = 12;
            _tooltipBox.style.paddingRight = 12;
            _tooltipBox.style.paddingTop = 10;
            _tooltipBox.style.paddingBottom = 10;

            _tooltipBox.style.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 0.95f);
            _tooltipBox.style.borderTopWidth = 1;
            _tooltipBox.style.borderRightWidth = 1;
            _tooltipBox.style.borderBottomWidth = 1;
            _tooltipBox.style.borderLeftWidth = 1;
            _tooltipBox.style.borderTopColor = new Color(0.30f, 0.60f, 1f, 0.60f);
            _tooltipBox.style.borderRightColor = new Color(0.30f, 0.60f, 1f, 0.60f);
            _tooltipBox.style.borderBottomColor = new Color(0.30f, 0.60f, 1f, 0.60f);
            _tooltipBox.style.borderLeftColor = new Color(0.30f, 0.60f, 1f, 0.60f);

            _tooltipBox.style.borderTopLeftRadius = 10;
            _tooltipBox.style.borderTopRightRadius = 10;
            _tooltipBox.style.borderBottomLeftRadius = 10;
            _tooltipBox.style.borderBottomRightRadius = 10;

            _tooltipLabel = new Label { name = "tutorialTooltipLabel" };
            _tooltipLabel.style.whiteSpace = WhiteSpace.Normal;

            _tooltipBox.Add(_tooltipLabel);
            _tooltip.Add(_tooltipBox);

            _layer.Add(_tooltip);
        }

        private VisualElement MakeDim(string name)
        {
            var ve = new VisualElement { name = name };
            ve.style.position = Position.Absolute;
            ve.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);

            // Именно диммеры блокируют клики
            ve.pickingMode = PickingMode.Position;

            ve.RegisterCallback<PointerDownEvent>(e => e.StopImmediatePropagation());
            ve.RegisterCallback<PointerUpEvent>(e => e.StopImmediatePropagation());
            ve.RegisterCallback<PointerMoveEvent>(e => e.StopImmediatePropagation());
            ve.RegisterCallback<WheelEvent>(e => e.StopImmediatePropagation());
            ve.RegisterCallback<ClickEvent>(e => e.StopImmediatePropagation());

            _layer.Add(ve);
            return ve;
        }

        private void UpdateLayout(float tooltipOffset)
        {
            if (_root == null || _target == null) return;

            var rootWb = _root.worldBound;
            var tWb = _target.worldBound;

            float rootW = rootWb.width;
            float rootH = rootWb.height;

            if (rootW <= 1f || rootH <= 1f)
                return;

            // Переводим worldBound цели в локальные координаты root
            float txMin = tWb.xMin - rootWb.xMin;
            float tyMin = tWb.yMin - rootWb.yMin;
            float txMax = tWb.xMax - rootWb.xMin;
            float tyMax = tWb.yMax - rootWb.yMin;

            float hlLeft = Mathf.Clamp(txMin - _padding, 0f, rootW);
            float hlTop = Mathf.Clamp(tyMin - _padding, 0f, rootH);
            float hlRight = Mathf.Clamp(txMax + _padding, 0f, rootW);
            float hlBottom = Mathf.Clamp(tyMax + _padding, 0f, rootH);

            float hlW = Mathf.Max(0f, hlRight - hlLeft);
            float hlH = Mathf.Max(0f, hlBottom - hlTop);

            // Highlight
            _highlight.style.left = hlLeft;
            _highlight.style.top = hlTop;
            _highlight.style.width = hlW;
            _highlight.style.height = hlH;
            _highlight.style.display = DisplayStyle.Flex;

            // 4 диммера вокруг выделения (оставляем "дырку" над кнопкой)
            // TOP
            _dimTop.style.left = 0;
            _dimTop.style.top = 0;
            _dimTop.style.right = 0;
            _dimTop.style.height = hlTop;
            _dimTop.style.display = (hlTop > 0.5f) ? DisplayStyle.Flex : DisplayStyle.None;

            // BOTTOM
            _dimBottom.style.left = 0;
            _dimBottom.style.top = hlTop + hlH;
            _dimBottom.style.right = 0;
            _dimBottom.style.bottom = 0;
            _dimBottom.style.display = (hlTop + hlH < rootH - 0.5f) ? DisplayStyle.Flex : DisplayStyle.None;

            // LEFT
            _dimLeft.style.left = 0;
            _dimLeft.style.top = hlTop;
            _dimLeft.style.width = hlLeft;
            _dimLeft.style.height = hlH;
            _dimLeft.style.display = (hlLeft > 0.5f && hlH > 0.5f) ? DisplayStyle.Flex : DisplayStyle.None;

            // RIGHT
            _dimRight.style.left = hlLeft + hlW;
            _dimRight.style.top = hlTop;
            _dimRight.style.right = 0;
            _dimRight.style.height = hlH;
            _dimRight.style.display = (hlLeft + hlW < rootW - 0.5f && hlH > 0.5f) ? DisplayStyle.Flex : DisplayStyle.None;

            // Tooltip: стараемся ставить слева от цели (как у вас на скрине), иначе справа
            float maxTipW = Mathf.Clamp(360f, 220f, rootW - 24f);
            _tooltipBox.style.maxWidth = maxTipW;

            // Примерная высота, чтобы не улетать за экран (точная может быть 0 до layout, это ок)
            float approxTipH = 80f;

            float tipX = hlLeft - maxTipW - tooltipOffset;
            if (tipX < 12f)
                tipX = hlLeft + hlW + tooltipOffset;

            float tipY = hlTop + hlH - approxTipH;
            tipY = Mathf.Clamp(tipY, 12f, rootH - approxTipH - 12f);

            _tooltip.style.left = tipX;
            _tooltip.style.top = tipY;
            _tooltip.style.display = DisplayStyle.Flex;
        }
    }
}
