using CourtSim.Core.Presentation;
using CourtSim.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// Gives a button its subtle sounds: a faint tick when the pointer moves onto it and a soft click when it is
    /// pressed. <see cref="UIFactory.CreateButton"/> adds it to every button, so screens never have to.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonSounds : MonoBehaviour, IPointerEnterHandler
    {
        private const float HoverVolume = 0.45f;
        private const float ClickVolume = 0.6f;

        private Selectable _selectable;

        /// <summary>The press sound; UIFactory adds it as the button's first click listener.</summary>
        public static void PlayClick()
        {
            SoundEffects.Play(SoundCue.UiClick, ClickVolume);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // A disabled button stays silent, which quietly tells the user it can't be pressed.
            if (_selectable != null && _selectable.IsInteractable())
            {
                SoundEffects.Play(SoundCue.UiHover, HoverVolume);
            }
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }
    }
}
