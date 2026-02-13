using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gironoid._Project.Code.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayHudView : MonoBehaviour
    {
        [SerializeField] private UIDocument _ui;

        private VisualElement _root;

        private VisualElement _topBar;
        private Label _lblTimer;
        private Label _lblLives;
        private Label _lblKills;

        private VisualElement _introPanel;
        private Label _lblIntroTitle;
        private Label _lblIntroObjective;
        private Button _btnIntroStart;

        private VisualElement _resultPanel;
        private Label _lblResultTitle;
        private Label _lblResultDetails;
        private Button _btnResultRetry;
        private Button _btnResultExit;

        public event Action StartClicked;
        public event Action RetryClicked;
        public event Action ExitClicked;

        private void Reset()
        {
            _ui = GetComponent<UIDocument>();
        }

        private void Awake()
        {
            EnsureUi();
            BuildUiIfMissing();
            HideIntro();
            HideResult();
        }

        private void EnsureUi()
        {
            if (_ui == null)
                _ui = GetComponent<UIDocument>();

            if (_ui == null)
            {
                Debug.LogError("GameplayHudView: UIDocument is missing.", this);
                return;
            }

            _root = _ui.rootVisualElement;
        }

        private void BuildUiIfMissing()
        {
            if (_root == null)
                return;

            // TOP BAR
            _topBar = _root.Q<VisualElement>("topBar");
            if (_topBar == null)
            {
                _topBar = new VisualElement { name = "topBar" };
                _topBar.style.position = Position.Absolute;
                _topBar.style.left = 12;
                _topBar.style.top = 12;
                _topBar.style.right = 12;
                _topBar.style.height = 44;
                _topBar.style.flexDirection = FlexDirection.Row;
                _topBar.style.justifyContent = Justify.SpaceBetween;
                _topBar.style.alignItems = Align.Center;
                _topBar.style.paddingLeft = 12;
                _topBar.style.paddingRight = 12;

                _root.Add(_topBar);
            }

            _lblTimer = _root.Q<Label>("lblTimer");
            if (_lblTimer == null)
            {
                _lblTimer = new Label { name = "lblTimer" };
                _lblTimer.style.unityFontStyleAndWeight = FontStyle.Bold;
                _topBar.Add(_lblTimer);
            }

            _lblLives = _root.Q<Label>("lblLives");
            if (_lblLives == null)
            {
                _lblLives = new Label { name = "lblLives" };
                _topBar.Add(_lblLives);
            }

            _lblKills = _root.Q<Label>("lblKills");
            if (_lblKills == null)
            {
                _lblKills = new Label { name = "lblKills" };
                _topBar.Add(_lblKills);
            }

            // INTRO PANEL
            _introPanel = _root.Q<VisualElement>("introPanel");
            if (_introPanel == null)
            {
                _introPanel = new VisualElement { name = "introPanel" };
                _introPanel.style.position = Position.Absolute;
                _introPanel.style.left = 0;
                _introPanel.style.top = 0;
                _introPanel.style.right = 0;
                _introPanel.style.bottom = 0;
                _introPanel.style.justifyContent = Justify.Center;
                _introPanel.style.alignItems = Align.Center;

                var box = new VisualElement();
                box.style.width = 520;
                box.style.maxWidth = new Length(92, LengthUnit.Percent);
                box.style.paddingLeft = 16;
                box.style.paddingRight = 16;
                box.style.paddingTop = 14;
                box.style.paddingBottom = 14;
                box.style.flexDirection = FlexDirection.Column;

                _lblIntroTitle = new Label { name = "lblIntroTitle" };
                _lblIntroTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                _lblIntroTitle.style.fontSize = 18;
                _lblIntroTitle.style.marginBottom = 10;

                _lblIntroObjective = new Label { name = "lblIntroObjective" };
                _lblIntroObjective.style.whiteSpace = WhiteSpace.Normal;
                _lblIntroObjective.style.marginBottom = 10;

                _btnIntroStart = new Button { name = "btnIntroStart", text = "Вылет" };
                _btnIntroStart.clicked += () => StartClicked?.Invoke();

                box.Add(_lblIntroTitle);
                box.Add(_lblIntroObjective);
                box.Add(_btnIntroStart);

                _introPanel.Add(box);
                _root.Add(_introPanel);
            }
            else
            {
                _lblIntroTitle = _root.Q<Label>("lblIntroTitle");
                _lblIntroObjective = _root.Q<Label>("lblIntroObjective");
                _btnIntroStart = _root.Q<Button>("btnIntroStart");

                if (_btnIntroStart != null)
                {
                    _btnIntroStart.clicked -= OnIntroStartClickedProxy;
                    _btnIntroStart.clicked += OnIntroStartClickedProxy;
                }
            }

            // RESULT PANEL
            _resultPanel = _root.Q<VisualElement>("resultPanel");
            if (_resultPanel == null)
            {
                _resultPanel = new VisualElement { name = "resultPanel" };
                _resultPanel.style.position = Position.Absolute;
                _resultPanel.style.left = 0;
                _resultPanel.style.top = 0;
                _resultPanel.style.right = 0;
                _resultPanel.style.bottom = 0;
                _resultPanel.style.justifyContent = Justify.Center;
                _resultPanel.style.alignItems = Align.Center;

                var box = new VisualElement();
                box.style.width = 520;
                box.style.maxWidth = new Length(92, LengthUnit.Percent);
                box.style.paddingLeft = 16;
                box.style.paddingRight = 16;
                box.style.paddingTop = 14;
                box.style.paddingBottom = 14;
                box.style.flexDirection = FlexDirection.Column;

                _lblResultTitle = new Label { name = "lblResultTitle" };
                _lblResultTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                _lblResultTitle.style.fontSize = 18;
                _lblResultTitle.style.marginBottom = 10;

                _lblResultDetails = new Label { name = "lblResultDetails" };
                _lblResultDetails.style.whiteSpace = WhiteSpace.Normal;
                _lblResultDetails.style.marginBottom = 10;

                var buttons = new VisualElement();
                buttons.style.flexDirection = FlexDirection.Row;

                _btnResultRetry = new Button { name = "btnResultRetry", text = "Повторить" };
                _btnResultExit = new Button { name = "btnResultExit", text = "Выйти" };

                // заменяем columnGap на marginRight
                _btnResultRetry.style.marginRight = 10;

                _btnResultRetry.clicked += () => RetryClicked?.Invoke();
                _btnResultExit.clicked += () => ExitClicked?.Invoke();

                buttons.Add(_btnResultRetry);
                buttons.Add(_btnResultExit);

                box.Add(_lblResultTitle);
                box.Add(_lblResultDetails);
                box.Add(buttons);

                _resultPanel.Add(box);
                _root.Add(_resultPanel);
            }
            else
            {
                _lblResultTitle = _root.Q<Label>("lblResultTitle");
                _lblResultDetails = _root.Q<Label>("lblResultDetails");
                _btnResultRetry = _root.Q<Button>("btnResultRetry");
                _btnResultExit = _root.Q<Button>("btnResultExit");

                if (_btnResultRetry != null)
                {
                    _btnResultRetry.style.marginRight = 10;
                    _btnResultRetry.clicked -= OnResultRetryClickedProxy;
                    _btnResultRetry.clicked += OnResultRetryClickedProxy;
                }

                if (_btnResultExit != null)
                {
                    _btnResultExit.clicked -= OnResultExitClickedProxy;
                    _btnResultExit.clicked += OnResultExitClickedProxy;
                }
            }

            // defaults
            SetTopBar(timerSeconds: 0, lives: 3, kills: 0, killTarget: 0);
        }

        private void OnIntroStartClickedProxy() => StartClicked?.Invoke();
        private void OnResultRetryClickedProxy() => RetryClicked?.Invoke();
        private void OnResultExitClickedProxy() => ExitClicked?.Invoke();

        public void SetTopBar(int timerSeconds, int lives, int kills, int killTarget)
        {
            if (_lblTimer != null)
                _lblTimer.text = $"Время: {FormatSeconds(timerSeconds)}";

            if (_lblLives != null)
                _lblLives.text = $"Жизни: {lives}";

            if (_lblKills != null)
            {
                if (killTarget > 0)
                    _lblKills.text = $"Метеориты: {kills}/{killTarget}";
                else
                    _lblKills.text = $"Метеориты: {kills}";
            }
        }

        public void ShowIntro(string title, string objectiveText, string startButtonText)
        {
            if (_lblIntroTitle != null) _lblIntroTitle.text = title ?? "";
            if (_lblIntroObjective != null) _lblIntroObjective.text = objectiveText ?? "";
            if (_btnIntroStart != null) _btnIntroStart.text = string.IsNullOrWhiteSpace(startButtonText) ? "Вылет" : startButtonText;

            if (_introPanel != null)
                _introPanel.style.display = DisplayStyle.Flex;
        }

        public void HideIntro()
        {
            if (_introPanel != null)
                _introPanel.style.display = DisplayStyle.None;
        }

        public void ShowResult(string title, string details)
        {
            if (_lblResultTitle != null) _lblResultTitle.text = title ?? "";
            if (_lblResultDetails != null) _lblResultDetails.text = details ?? "";

            if (_resultPanel != null)
                _resultPanel.style.display = DisplayStyle.Flex;
        }

        public void HideResult()
        {
            if (_resultPanel != null)
                _resultPanel.style.display = DisplayStyle.None;
        }

        private static string FormatSeconds(int seconds)
        {
            if (seconds < 0) seconds = 0;
            var m = seconds / 60;
            var s = seconds % 60;
            return $"{m:00}:{s:00}";
        }
    }
}
