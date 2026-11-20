using System;
using System.Collections.Generic;
using CourtSim.Core.Decisions;
using CourtSim.Core.Events;
using CourtSim.Core.Interaction;
using CourtSim.Core.UI;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Common;
using CourtSim.Runtime.Player;
using CourtSim.Runtime.UI.Screens;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The one owner of user interface state: the modal screen stack, the HUD, cursor locking and the
    /// gameplay-input block. Nothing else opens screens or touches the cursor.
    /// </summary>
    /// <remarks>
    /// <para>The hierarchy is generated at runtime from <see cref="UITheme"/>, so the UI carries no prefab or
    /// texture assets and a theme change restyles everything at once.</para>
    /// <para>Rules: any open screen blocks gameplay input and frees the cursor; Esc opens the pause menu or
    /// goes back one screen; the main menu can only be left via its own buttons.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class UIManager : MonoBehaviour
    {
        private const int HudSortingOrder = 0;
        private const int ScreenSortingOrder = 10;

        [Tooltip("Optional. Leave empty to use the default theme.")]
        [SerializeField] private UITheme _theme;
        [SerializeField] private bool _showMainMenuOnStart = true;

        private readonly ScreenStack _stack = new ScreenStack();
        private readonly Dictionary<ScreenId, UIScreen> _screens = new Dictionary<ScreenId, UIScreen>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private IPlayerInputSource _input;
        private DecisionSequence _decisions;
        private UISpriteCache _sprites;
        private UIFactory _factory;
        private HudView _hud;
        private bool _ownsTheme;
        private bool _built;
        private bool _gameplayBlocked;

        public UITheme Theme => _theme;

        public ScreenId CurrentScreen => _stack.Top;

        public bool IsGameplayBlocked => !_stack.IsEmpty;

        public bool IsAssistantVisible => _hud != null && _hud.IsAssistantVisible;

        public IPlayerInputSource InputSource
        {
            get
            {
                if (_input == null)
                {
                    _input = new LegacyPlayerInputSource();
                }

                return _input;
            }
        }

        public DecisionSequence Decisions
        {
            get
            {
                if (_decisions == null)
                {
                    _decisions = DecisionSequence.CreateStandardPlaceholder();
                }

                return _decisions;
            }
        }

        /// <summary>Creates a new UI root in the active scene.</summary>
        public static UIManager Create(Transform parent = null)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(parent, false);
            return go.AddComponent<UIManager>();
        }

        /// <summary>
        /// Supplies shared dependencies. Call before the first frame: a later call switches input handling, but
        /// screens that are already built keep the key labels of the previous source.
        /// </summary>
        public void Initialize(IPlayerInputSource input, DecisionSequence decisions = null)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            if (decisions != null)
            {
                _decisions = decisions;
            }
        }

        public void Open(ScreenId id)
        {
            EnsureBuilt();
            _stack.Push(id);
        }

        /// <summary>Closes the top screen, except the main menu, which is left only via its own buttons.</summary>
        public void Back()
        {
            if (_stack.Top != ScreenId.MainMenu)
            {
                _stack.Pop();
            }
        }

        public void CloseAll()
        {
            _stack.Clear();
        }

        public void ShowMainMenu()
        {
            EnsureBuilt();
            _stack.Reset(ScreenId.MainMenu);
        }

        public void ShowToast(string message)
        {
            EnsureBuilt();
            _hud.ShowToast(message);
        }

        public void SetAssistantVisible(bool visible)
        {
            EnsureBuilt();
            _hud.SetAssistantVisible(visible);
        }

        public void ToggleAssistant()
        {
            SetAssistantVisible(!IsAssistantVisible);
        }

        /// <summary>Records a ruling for the pending decision point, announces it and closes the dialog.</summary>
        /// <returns>False if there is no pending decision or <paramref name="optionId"/> is not one of its options.</returns>
        public bool SubmitDecision(string optionId)
        {
            DecisionPointDefinition current = Decisions.Current;
            DecisionOption option = current?.FindOption(optionId);
            if (option == null)
            {
                Debug.LogWarning($"Ignoring ruling '{optionId}': it is not an option for the pending decision.");
                return false;
            }

            DecisionRecord record = Decisions.Record(optionId);
            GameServices.Events.Publish(new DecisionSubmitted(record, option.Label));
            RefreshDecisionViews();
            ShowToast($"Ruling recorded: {option.Label}");
            _stack.Remove(ScreenId.DecisionPoint);
            return true;
        }

        public void RestartSession()
        {
            Decisions.Reset();
            RefreshDecisionViews();
            ShowToast("New session started");
        }

        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Awake()
        {
            if (_theme == null)
            {
                _theme = UITheme.CreateDefault();
                _ownsTheme = true;
            }

            _stack.TopChanged += OnTopChanged;
        }

        private void OnEnable()
        {
            _subscriptions.Add(GameServices.Events.Subscribe<InteractionRequested>(OnInteractionRequested));
            _subscriptions.Add(GameServices.Events.Subscribe<InteractionFocusChanged>(OnInteractionFocusChanged));
        }

        private void OnDisable()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        private void Start()
        {
            EnsureBuilt();
            if (_showMainMenuOnStart)
            {
                ShowMainMenu();
            }
            else
            {
                ApplyGameplayBlock(false, true);
            }
        }

        private void Update()
        {
            if (!_built)
            {
                return;
            }

            IPlayerInputSource input = InputSource;
            if (input.BackPressed)
            {
                if (_stack.IsEmpty)
                {
                    Open(ScreenId.Pause);
                }
                else
                {
                    Back();
                }
            }
            else if (input.ToggleAssistantPressed && !IsGameplayBlocked)
            {
                ToggleAssistant();
            }
        }

        private void OnDestroy()
        {
            _stack.TopChanged -= OnTopChanged;
            _sprites?.Dispose();
            _sprites = null;
            if (_ownsTheme)
            {
                UnityObjectUtil.SafeDestroy(_theme);
            }

            if (_gameplayBlocked || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            EnsureEventSystem(transform);

            _sprites = new UISpriteCache();
            _factory = new UIFactory(_theme, _sprites);

            Canvas hudCanvas = _factory.CreateCanvas("HudCanvas", transform, HudSortingOrder, false);
            RectTransform hudRoot = _factory.CreateRect("Hud", hudCanvas.transform);
            _hud = hudRoot.gameObject.AddComponent<HudView>();
            _hud.Build(_factory, InputSource);
            _hud.SetDecisionProgress(Decisions);

            Canvas screenCanvas = _factory.CreateCanvas("ScreenCanvas", transform, ScreenSortingOrder, true);
            AddScreen<MainMenuScreen>(screenCanvas.transform);
            AddScreen<PauseScreen>(screenCanvas.transform);
            AddScreen<ControlsScreen>(screenCanvas.transform);
            AddScreen<DecisionPointScreen>(screenCanvas.transform);
            AddScreen<EvidenceScreen>(screenCanvas.transform);
        }

        private void AddScreen<T>(Transform parent) where T : UIScreen
        {
            RectTransform rect = _factory.CreateRect(typeof(T).Name, parent);
            T screen = rect.gameObject.AddComponent<T>();
            screen.Build(this, _factory);
            _screens.Add(screen.Id, screen);
        }

        private void OnTopChanged(ScreenId previous, ScreenId current)
        {
            if (_screens.TryGetValue(previous, out UIScreen previousScreen))
            {
                previousScreen.SetVisible(false);
            }

            if (_screens.TryGetValue(current, out UIScreen currentScreen))
            {
                // Draw above any screen that is still fading out.
                currentScreen.transform.SetAsLastSibling();
                currentScreen.SetVisible(true);
            }

            _hud.SetVisible(!_stack.Contains(ScreenId.MainMenu));
            ApplyGameplayBlock(IsGameplayBlocked);
            GameServices.Events.Publish(new ScreenChanged(previous, current));
        }

        private void ApplyGameplayBlock(bool blocked, bool force = false)
        {
            if (!force && blocked == _gameplayBlocked)
            {
                return;
            }

            _gameplayBlocked = blocked;
            Cursor.lockState = blocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = blocked;
            if (!blocked && EventSystem.current != null)
            {
                // Stops Space/Enter from activating a button the player can no longer see.
                EventSystem.current.SetSelectedGameObject(null);
            }

            GameServices.Events.Publish(new GameplayInputBlockChanged(blocked));
        }

        private void RefreshDecisionViews()
        {
            if (_hud != null)
            {
                _hud.SetDecisionProgress(Decisions);
            }
        }

        private void OnInteractionRequested(InteractionRequested evt)
        {
            switch (evt.Action)
            {
                case InteractionAction.OpenDecisionPoint:
                    Open(ScreenId.DecisionPoint);
                    break;
                case InteractionAction.OpenEvidence:
                    Open(ScreenId.Evidence);
                    break;
                case InteractionAction.ShowAssistant:
                    SetAssistantVisible(true);
                    break;
            }
        }

        private void OnInteractionFocusChanged(InteractionFocusChanged evt)
        {
            if (_hud != null)
            {
                _hud.SetPrompt(evt.HasFocus ? evt.Prompt : null);
            }
        }

        /// <summary>
        /// Creates an EventSystem if the scene has none, choosing the Input System UI module when that
        /// package is installed and the legacy StandaloneInputModule otherwise.
        /// </summary>
        /// <remarks>Approach adapted from LLMUnity's EventSystemAutoSetup (Apache-2.0); see NOTICES.md.</remarks>
        private static void EnsureEventSystem(Transform parent)
        {
            if (UnityObjectUtil.FindFirst<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.transform.SetParent(parent, false);

            Type inputSystemModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null)
            {
                go.AddComponent(inputSystemModule);
            }
            else
            {
                go.AddComponent<StandaloneInputModule>();
            }
        }
    }
}
