using System;
using System.Collections.Generic;
using CourtSim.Core.Advising;
using CourtSim.Core.Decisions;
using CourtSim.Core.Events;
using CourtSim.Core.Interaction;
using CourtSim.Core.Logging;
using CourtSim.Core.Sarvam;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using CourtSim.Runtime.App;
using CourtSim.Runtime.Audio;
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
        [Tooltip("Play the opening titles over the main menu at launch (never in batch mode; see IntroSequence).")]
        [SerializeField] private bool _playIntro = true;

        private readonly ScreenStack _stack = new ScreenStack();
        private readonly Dictionary<ScreenId, UIScreen> _screens = new Dictionary<ScreenId, UIScreen>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private IPlayerInputSource _input;
        private DecisionSequence _decisions;
        private CaseSession _session;
        private UISpriteCache _sprites;
        private UIFactory _factory;
        private HudView _hud;
        private AssistantVoice _voice;
        private Advice _spokenAdvice;
        private bool _submitting;
        private bool _ownsTheme;
        private bool _built;
        private bool _gameplayBlocked;
        private IntroSequence _intro;

        public UITheme Theme => _theme;

        /// <summary>True while the opening titles cover the main menu (they are skipped with any key or click).</summary>
        public bool IsIntroPlaying => _intro != null && _intro.IsPlaying;

        public ScreenId CurrentScreen => _stack.Top;

        public bool IsGameplayBlocked => !_stack.IsEmpty;

        public bool IsAssistantVisible => _hud != null && _hud.IsAssistantVisible;

        /// <summary>The assistant's speaking voice; it reads each piece of advice aloud once.</summary>
        public AssistantVoice Voice => _voice;

        /// <summary>True while the assistant's speech is still being generated.</summary>
        public bool IsAssistantPreparing => _voice != null && _voice.IsPreparing;

        /// <summary>True while the assistant is audibly speaking or a visible assistant message is still typing.</summary>
        public bool IsAssistantTalking
        {
            get
            {
                if (_voice != null && _voice.IsPlaying)
                {
                    return true;
                }

                if (_hud != null && _hud.IsAssistantVisible && _hud.IsAssistantTyping)
                {
                    return true;
                }

                // A hidden card pauses its typing, so only a visible one counts.
                return _screens.TryGetValue(ScreenId.DecisionPoint, out UIScreen screen)
                    && screen.IsVisible
                    && screen is DecisionPointScreen decision
                    && decision.Assistant.IsTyping;
            }
        }

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
                if (_session != null)
                {
                    return _session.Decisions;
                }

                if (_decisions == null)
                {
                    _decisions = DecisionSequence.CreateStandardPlaceholder();
                }

                return _decisions;
            }
        }

        /// <summary>The case session being played, or null when the decision flow runs on placeholder content.</summary>
        public CaseSession Session => _session;

        /// <summary>Raised when the trainee asks for a new session; <see cref="App.CaseSessionController"/> starts one.</summary>
        public event Action RestartRequested;

        /// <summary>Presents <paramref name="session"/>: decisions, advice, exhibits and the debrief come from it.</summary>
        public void AttachSession(CaseSession session)
        {
            if (_session != null)
            {
                _session.AdviceChanged -= OnSessionAdviceChanged;
            }

            if (_session != session)
            {
                // A new session (e.g. a restart): the old advice must not keep talking.
                StopSpeaking();
            }

            _session = session;
            if (_session != null)
            {
                _session.AdviceChanged += OnSessionAdviceChanged;
            }

            RefreshDecisionViews();
            RefreshOpenScreen();
        }

        /// <summary>Records the trainee's Accept/Challenge reaction to the pending advice.</summary>
        public void SetAdviceStance(AdviceStance stance)
        {
            if (_session == null || _session.CurrentAdvice == null)
            {
                return;
            }

            _session.SetStance(stance);
            ShowToast(stance == AdviceStance.Accepted ? "Advice accepted. Now make your ruling." : "Advice challenged. Now make your ruling.");
            RefreshOpenScreen();
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
            SpeakNewAdvice();
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

            // Once the ruling is made the advice for it is moot, so the voice stops mid-sentence. The next
            // advice waits until the dialog has closed and the trainee actually sees it.
            StopSpeaking();
            _submitting = true;
            try
            {
                DecisionRecord record = _session != null ? _session.Submit(optionId).Record : Decisions.Record(optionId);
                GameServices.Events.Publish(new DecisionSubmitted(record, option.Label));
                RefreshDecisionViews();
                ShowToast($"Ruling recorded: {option.Label}");
            }
            finally
            {
                _submitting = false;
            }

            _stack.Remove(ScreenId.DecisionPoint);
            return true;
        }

        public void RestartSession()
        {
            StopSpeaking();
            if (RestartRequested != null)
            {
                RestartRequested.Invoke();
            }
            else
            {
                Decisions.Reset();
                RefreshDecisionViews();
            }

            ShowToast(_session != null ? $"New session: {_session.Case.Title}" : "New session started");
        }

        /// <summary>The cases the player can choose from, set by the session controller.</summary>
        public IReadOnlyList<CourtSim.Core.Cases.CaseFile> CaseCatalog { get; private set; } = Array.Empty<CourtSim.Core.Cases.CaseFile>();

        /// <summary>Raised when the player picks a case on the case-selection screen (argument: case id).</summary>
        public event Action<string> CaseChosen;

        public void SetCaseCatalog(IReadOnlyList<CourtSim.Core.Cases.CaseFile> cases)
        {
            CaseCatalog = cases ?? Array.Empty<CourtSim.Core.Cases.CaseFile>();
        }

        /// <summary>Starts the chosen case and walks the player into the courtroom.</summary>
        public void ChooseCase(string caseId)
        {
            CaseChosen?.Invoke(caseId);
            CloseAll();
        }

        /// <summary>
        /// Called when the judge asks for the next ruling at the bench. If it returns true, something else (the hearing)
        /// takes over and opens the decision dialog later.
        /// </summary>
        public Func<bool> BeforeDecisionPoint { get; set; }

        /// <summary>Raised by the main menu's "Watch guided demo" button; the bootstrap starts the demo.</summary>
        public event Action GuidedDemoRequested;

        public void RequestGuidedDemo()
        {
            GuidedDemoRequested?.Invoke();
        }

        public void QuitApplication()
        {
            StopSpeaking();
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

            _voice = GetComponent<AssistantVoice>();
            if (_voice == null)
            {
                _voice = gameObject.AddComponent<AssistantVoice>();
            }

            // Typing waits for the voice, so the advice is typed while it is being read out.
            TypewriterText.HoldWhile = () => _voice != null && _voice.IsPreparing;
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

                // The titles only cover the menu, which is already open underneath, so the screen stack (and
                // everything that checks it, tests included) is the same with or without them.
                if (_playIntro && IntroSequence.ShouldPlay())
                {
                    _intro = IntroSequence.Play(_factory, transform, OnIntroFinished);
                }
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
            if (_session != null)
            {
                _session.AdviceChanged -= OnSessionAdviceChanged;
            }

            // The drone lives on a separate, long-lived object; without the menus it has no reason to keep playing.
            SoundEffects.StopDrone(0.3f);
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
            _hud.SetSession(_session);

            Canvas screenCanvas = _factory.CreateCanvas("ScreenCanvas", transform, ScreenSortingOrder, true);
            AddScreen<MainMenuScreen>(screenCanvas.transform);
            AddScreen<PauseScreen>(screenCanvas.transform);
            AddScreen<ControlsScreen>(screenCanvas.transform);
            AddScreen<DecisionPointScreen>(screenCanvas.transform);
            AddScreen<EvidenceScreen>(screenCanvas.transform);
            AddScreen<CaseSelectScreen>(screenCanvas.transform);
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

            // Anything that leaves the main menu (a tool, a test, the demo) must not stay hidden under the titles.
            // Done before the drone below, because skipping the titles resets the drone to its menu volume.
            if (current != ScreenId.MainMenu && IsIntroPlaying)
            {
                _intro.Skip();
            }

            bool onMenu = _stack.Contains(ScreenId.MainMenu);
            _hud.SetVisible(!onMenu);

            // The ambient drone belongs to the menus (main menu and the screens opened from it) and fades out
            // as the player walks into the courtroom.
            if (onMenu)
            {
                SoundEffects.StartDrone(IsIntroPlaying ? SoundEffects.IntroDroneVolume : SoundEffects.MenuDroneVolume);
            }
            else
            {
                SoundEffects.StopDrone();
            }

            ApplyGameplayBlock(IsGameplayBlocked);
            GameServices.Events.Publish(new ScreenChanged(previous, current));
            SpeakNewAdvice();
        }

        /// <summary>Shows the main menu and plays the opening titles over it (used by the demo recording).</summary>
        public void PlayIntro()
        {
            EnsureBuilt();
            ShowMainMenu();
            if (IsIntroPlaying)
            {
                _intro.Skip();
            }

            _intro = IntroSequence.Play(_factory, transform, OnIntroFinished);
            SoundEffects.StartDrone(SoundEffects.IntroDroneVolume);
        }

        /// <summary>
        /// The titles are fading away: show the main menu again, which replays its entrance animation and gives the
        /// keyboard focus back to its first button (the intro had cleared it so Enter could not press a hidden button).
        /// </summary>
        private void OnIntroFinished()
        {
            if (_stack.Top == ScreenId.MainMenu && _screens.TryGetValue(ScreenId.MainMenu, out UIScreen menu))
            {
                menu.SetVisible(true);
            }
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
                _hud.SetSession(_session);
            }

            SpeakNewAdvice();
        }

        /// <summary>
        /// Reads the current advice aloud the first time it is both ready and on screen (in the decision
        /// dialog or the HUD assistant panel). Remembering which advice was spoken means a screen refresh,
        /// or the same advice showing in both places, never repeats it.
        /// </summary>
        private void SpeakNewAdvice()
        {
            if (_voice == null || _submitting || _session == null || _session.IsComplete || _session.CurrentAdviceState != AdviceState.Ready)
            {
                return;
            }

            Advice advice = _session.CurrentAdvice;
            bool onScreen = _stack.Top == ScreenId.DecisionPoint || (_stack.IsEmpty && IsAssistantVisible);
            if (advice == null || advice == _spokenAdvice || !onScreen)
            {
                return;
            }

            _spokenAdvice = advice;
            // The assistant always speaks in the same voice ("roopa"); the panel text stays English.
            _voice.Speak(AdviceText.Spoken(_session), SarvamVoiceRole.Assistant, _session.Case.Id);
        }

        private void StopSpeaking()
        {
            if (_voice != null)
            {
                _voice.Stop();
            }
        }

        private void RefreshOpenScreen()
        {
            if (_screens.TryGetValue(_stack.Top, out UIScreen screen))
            {
                screen.Refresh();
            }
        }

        private void OnSessionAdviceChanged(int index)
        {
            if (_session == null || index != _session.CurrentIndex)
            {
                return;
            }

            if (_hud != null)
            {
                _hud.SetSession(_session);
            }

            RefreshOpenScreen();
            SpeakNewAdvice();
        }

        private void OnInteractionRequested(InteractionRequested evt)
        {
            switch (evt.Action)
            {
                case InteractionAction.OpenDecisionPoint:
                    // A hearing (advocates arguing) may play first; it opens the dialog itself when it ends.
                    if (BeforeDecisionPoint == null || !BeforeDecisionPoint())
                    {
                        Open(ScreenId.DecisionPoint);
                    }

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
