using System;
using System.Collections.Generic;

namespace CourtSim.Core.UI
{
    /// <summary>
    /// Ordered stack of open modal screens. Only the top screen is visible and interactive.
    /// </summary>
    /// <remarks>
    /// A screen appears in the stack at most once: pushing a screen that is already open
    /// moves it to the top. <see cref="TopChanged"/> fires exactly once per operation that
    /// changes the top screen, never for operations that leave it unchanged.
    /// </remarks>
    public sealed class ScreenStack
    {
        private readonly List<ScreenId> _items = new List<ScreenId>();

        /// <summary>Raised with (previous top, new top) after the top screen changes.</summary>
        public event Action<ScreenId, ScreenId> TopChanged;

        /// <summary>The visible screen, or <see cref="ScreenId.None"/> when the stack is empty.</summary>
        public ScreenId Top => _items.Count == 0 ? ScreenId.None : _items[_items.Count - 1];

        public int Count => _items.Count;

        public bool IsEmpty => _items.Count == 0;

        /// <summary>Screens from bottom to top.</summary>
        public IReadOnlyList<ScreenId> Items => _items;

        public bool Contains(ScreenId id)
        {
            return _items.Contains(id);
        }

        /// <summary>Opens <paramref name="id"/> on top, moving it up if it is already open.</summary>
        public void Push(ScreenId id)
        {
            ThrowIfNone(id);
            ScreenId previous = Top;
            if (previous == id)
            {
                return;
            }

            _items.Remove(id);
            _items.Add(id);
            RaiseIfChanged(previous);
        }

        /// <summary>Closes the top screen. Returns false when the stack was already empty.</summary>
        public bool Pop()
        {
            if (_items.Count == 0)
            {
                return false;
            }

            ScreenId previous = Top;
            _items.RemoveAt(_items.Count - 1);
            RaiseIfChanged(previous);
            return true;
        }

        /// <summary>Closes <paramref name="id"/> wherever it is in the stack. Returns false if it was not open.</summary>
        public bool Remove(ScreenId id)
        {
            ScreenId previous = Top;
            if (!_items.Remove(id))
            {
                return false;
            }

            RaiseIfChanged(previous);
            return true;
        }

        /// <summary>Swaps the top screen for <paramref name="id"/> (or pushes it onto an empty stack).</summary>
        public void ReplaceTop(ScreenId id)
        {
            ThrowIfNone(id);
            ScreenId previous = Top;
            if (_items.Count > 0)
            {
                _items.RemoveAt(_items.Count - 1);
            }

            _items.Remove(id);
            _items.Add(id);
            RaiseIfChanged(previous);
        }

        /// <summary>Closes every screen and then opens <paramref name="id"/> as the only screen.</summary>
        public void Reset(ScreenId id)
        {
            ThrowIfNone(id);
            ScreenId previous = Top;
            _items.Clear();
            _items.Add(id);
            RaiseIfChanged(previous);
        }

        /// <summary>Closes every screen.</summary>
        public void Clear()
        {
            ScreenId previous = Top;
            _items.Clear();
            RaiseIfChanged(previous);
        }

        private static void ThrowIfNone(ScreenId id)
        {
            if (id == ScreenId.None)
            {
                throw new ArgumentException("ScreenId.None cannot be pushed; use Pop or Clear instead.", nameof(id));
            }
        }

        private void RaiseIfChanged(ScreenId previous)
        {
            ScreenId current = Top;
            if (current != previous)
            {
                TopChanged?.Invoke(previous, current);
            }
        }
    }
}
