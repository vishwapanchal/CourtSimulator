using CourtSim.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Base class for every modal screen. <see cref="UIManager"/> owns the screen stack and calls
    /// <see cref="Build"/> once and <see cref="SetVisible"/> on every transition.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIScreen : MonoBehaviour
    {
        private CanvasGroupFader _fader;

        public abstract ScreenId Id { get; }

        public bool IsVisible => _fader != null && _fader.TargetVisible;

        protected UIManager Manager { get; private set; }

        protected UIFactory Factory { get; private set; }

        protected UITheme Theme => Factory.Theme;

        /// <summary>The control that receives keyboard/gamepad focus when the screen opens.</summary>
        protected virtual Selectable DefaultSelection => null;

        internal void Build(UIManager manager, UIFactory factory)
        {
            Manager = manager;
            Factory = factory;

            var root = (RectTransform)transform;
            UIFactory.Stretch(root);
            _fader = gameObject.AddComponent<CanvasGroupFader>();
            _fader.Duration = factory.Theme.FadeDuration;

            BuildContent(root);
            _fader.SetVisible(false, true);
        }

        internal void SetVisible(bool visible)
        {
            if (visible)
            {
                OnShown();
            }

            _fader.SetVisible(visible);

            if (visible)
            {
                Selectable selection = DefaultSelection;
                if (selection != null && EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(selection.gameObject);
                }
            }
        }

        /// <summary>Re-renders dynamic content while the screen is visible, e.g. when advice arrives.</summary>
        internal void Refresh()
        {
            if (IsVisible)
            {
                OnShown();
            }
        }

        /// <summary>Creates the screen's hierarchy under <paramref name="root"/> (stretched to the canvas).</summary>
        protected abstract void BuildContent(RectTransform root);

        /// <summary>Called just before the screen becomes visible; refresh dynamic content here.</summary>
        protected virtual void OnShown()
        {
        }
    }
}
