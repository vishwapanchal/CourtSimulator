using System.Collections;
using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Core.Events;
using CourtSim.Core.Sessions;
using CourtSim.Core.UI;
using CourtSim.Runtime.Audio;
using CourtSim.Runtime.Characters;
using CourtSim.Runtime.Common;
using CourtSim.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CourtSim.Runtime.App
{
    /// <summary>
    /// Plays the short hearing before each ruling: the bench clerk calls the matter, then the Public Prosecutor and
    /// the defence advocate stand and argue their submissions aloud, with subtitles, while the courtroom turns to
    /// watch. Only then does the decision dialog (with the AI assistant's advice) open.
    /// </summary>
    /// <remarks>Each decision point is heard once per session; pressing E at the bench again opens the dialog at once.</remarks>
    [DisallowMultipleComponent]
    public sealed class HearingDirector : MonoBehaviour
    {
        private readonly HashSet<string> _heard = new HashSet<string>();
        private UIManager _ui;
        private CaseSessionController _sessions;
        private CourtroomCast _cast;
        private AssistantVoice _voice;
        private GameObject _subtitleBar;
        private Text _speaker;
        private Text _line;
        private Coroutine _hearing;
        private Camera _seatCamera;
        private Camera _playerCamera;
        private Quaternion _seatTarget;

        /// <summary>True while a hearing is being played.</summary>
        public bool IsPlaying => _hearing != null;

        /// <summary>True while a speaker's voice is still being synthesised (the demo recorder pauses then).</summary>
        public bool IsPreparingSpeech => _voice != null && _voice.IsPreparing;

        public void Initialize(UIManager ui, CaseSessionController sessions, CourtroomCast cast)
        {
            _ui = ui;
            _sessions = sessions;
            _cast = cast;
            if (_ui != null)
            {
                _ui.BeforeDecisionPoint = TryStartHearing;
            }
        }

        private void Awake()
        {
            _voice = gameObject.AddComponent<AssistantVoice>();
        }

        private void OnDestroy()
        {
            if (_ui != null && _ui.BeforeDecisionPoint == (System.Func<bool>)TryStartHearing)
            {
                _ui.BeforeDecisionPoint = null;
            }
        }

        /// <summary>Called by the UI when the judge asks for the next ruling. Returns true if a hearing starts first.</summary>
        private bool TryStartHearing()
        {
            CaseSession session = _sessions != null ? _sessions.Session : null;
            if (_hearing != null || _cast == null || session == null || session.IsComplete)
            {
                return _hearing != null;
            }

            string key = session.SessionId + "/" + session.CurrentIndex;
            if (_heard.Contains(key))
            {
                return false;
            }

            _heard.Add(key);
            _hearing = StartCoroutine(PlayHearing(session, session.CurrentPoint));
            return true;
        }

        private IEnumerator PlayHearing(CaseSession session, CaseDecisionPoint point)
        {
            EnsureSubtitleBar();
            GameServices.Events.Publish(new GameplayInputBlockChanged(true)); // the judge listens; no walking off mid-hearing
            UseSeatCamera(true);

            bool evidence = point.ExhibitId != null;
            if (evidence)
            {
                _cast.ShowWitness(true);
            }

            yield return Speak(_cast.Reader, "Bench Clerk", $"{session.Case.Title}. {point.Definition.Title}. The Public Prosecutor, Your Honour.", false, 1, false);
            yield return Speak(_cast.Prosecutor, _cast.Prosecutor.DisplayName, point.ProsecutionSubmission, false, 0, true);
            yield return Speak(_cast.Defence, _cast.Defence.DisplayName, point.DefenceSubmission, true, 0, true);

            foreach (CharacterAnimator character in _cast.AllCharacters)
            {
                character.ClearLookAt();
            }

            if (evidence)
            {
                _cast.ShowWitness(false);
            }

            _subtitleBar.SetActive(false);
            UseSeatCamera(false);
            GameServices.Events.Publish(new GameplayInputBlockChanged(false));
            _hearing = null;
            _ui.Open(ScreenId.DecisionPoint);
        }

        /// <summary>One speaker: stand (advocates), speak with subtitles while everyone looks, then sit again.</summary>
        private IEnumerator Speak(CharacterAnimator who, string name, string text, bool female, int rate, bool stands)
        {
            if (who == null || string.IsNullOrWhiteSpace(text))
            {
                yield break;
            }

            if (stands)
            {
                who.SetStanding(true);
                yield return new WaitForSeconds(0.6f);
            }

            foreach (CharacterAnimator character in _cast.AllCharacters)
            {
                if (character != who)
                {
                    character.LookAt(who.HeadPosition);
                }
            }

            AimSeatCamera(who.HeadPosition);
            _speaker.text = name.ToUpperInvariant();
            _line.text = text;
            _subtitleBar.SetActive(true);
            _voice.Speak(text, female, rate);
            who.SetSpeaking(true);

            // Wait for the voice; without speech (e.g. not on Windows) give readers time to read the subtitle.
            float readingTime = Mathf.Clamp(text.Length / 15f, 2.5f, 14f);
            float started = Time.time;
            while (_voice.IsSpeaking || (!_voice.Enabled && Time.time - started < readingTime) || Time.time - started < 1f)
            {
                yield return null;
            }

            who.SetSpeaking(false);
            yield return new WaitForSeconds(0.4f);
            if (stands)
            {
                who.SetStanding(false);
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>
        /// During a hearing the view cuts to the judge's seat on the dais, looking out over the courtroom as a real
        /// judge would, and turns towards each speaker. The player's own camera returns afterwards.
        /// </summary>
        private void UseSeatCamera(bool on)
        {
            if (on)
            {
                if (_seatCamera == null)
                {
                    var go = new GameObject("JudgeSeatCamera");
                    go.transform.SetParent(transform, false);
                    go.transform.position = new Vector3(0f, 2.75f, 8.0f); // the judge's eyes on the raised dais, above the desk
                    _seatCamera = go.AddComponent<Camera>();
                    _seatCamera.fieldOfView = 62f;
                    _seatCamera.nearClipPlane = 0.05f;
                }

                _seatTarget = Quaternion.Euler(18f, 180f, 0f);
                _seatCamera.transform.rotation = _seatTarget;
                _playerCamera = Camera.main;
                if (_playerCamera != null)
                {
                    _playerCamera.enabled = false;
                }

                _seatCamera.enabled = true;
                return;
            }

            if (_seatCamera != null)
            {
                _seatCamera.enabled = false;
            }

            if (_playerCamera != null)
            {
                _playerCamera.enabled = true;
            }
        }

        private void AimSeatCamera(Vector3 point)
        {
            if (_seatCamera != null)
            {
                Vector3 direction = point - _seatCamera.transform.position;
                _seatTarget = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(4f, 0f, 0f);
            }
        }

        private void LateUpdate()
        {
            // Ease the seat camera towards the current speaker.
            if (_seatCamera != null && _seatCamera.enabled)
            {
                _seatCamera.transform.rotation = Quaternion.Slerp(_seatCamera.transform.rotation, _seatTarget, 1f - Mathf.Exp(-3f * Time.deltaTime));
            }
        }

        private void EnsureSubtitleBar()
        {
            if (_subtitleBar != null)
            {
                return;
            }

            var canvasObject = new GameObject("HearingSubtitles", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _subtitleBar = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _subtitleBar.transform.SetParent(canvasObject.transform, false);
            var bar = (RectTransform)_subtitleBar.transform;
            bar.anchorMin = new Vector2(0.15f, 0f);
            bar.anchorMax = new Vector2(0.85f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, 160f); // above the demo caption strip
            _subtitleBar.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.16f, 0.9f);
            var layout = _subtitleBar.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 14, 16);
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _subtitleBar.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _speaker = CreateText(bar, "Speaker", 20, new Color(0.83f, 0.68f, 0.31f), FontStyle.Bold);
            _line = CreateText(bar, "Line", 28, new Color(0.96f, 0.93f, 0.86f), FontStyle.Normal);
            _subtitleBar.SetActive(false);
        }

        private static Text CreateText(Transform parent, string name, int size, Color color, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = BuiltinAssets.DefaultFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
