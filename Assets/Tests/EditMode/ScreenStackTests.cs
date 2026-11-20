using System;
using System.Collections.Generic;
using CourtSim.Core.UI;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class ScreenStackTests
    {
        private ScreenStack _stack;
        private List<(ScreenId Previous, ScreenId Current)> _changes;

        [SetUp]
        public void SetUp()
        {
            _stack = new ScreenStack();
            _changes = new List<(ScreenId, ScreenId)>();
            _stack.TopChanged += (previous, current) => _changes.Add((previous, current));
        }

        [Test]
        public void NewStack_IsEmpty()
        {
            Assert.That(_stack.IsEmpty, Is.True);
            Assert.That(_stack.Top, Is.EqualTo(ScreenId.None));
            Assert.That(_stack.Count, Is.EqualTo(0));
        }

        [Test]
        public void Push_MakesScreenTop_AndRaisesOnce()
        {
            _stack.Push(ScreenId.Pause);

            Assert.That(_stack.Top, Is.EqualTo(ScreenId.Pause));
            Assert.That(_changes, Is.EqualTo(new[] { (ScreenId.None, ScreenId.Pause) }));
        }

        [Test]
        public void Push_SameTopTwice_IsNoOp()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Pause);

            Assert.That(_stack.Count, Is.EqualTo(1));
            Assert.That(_changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Push_ExistingScreen_MovesItToTopWithoutDuplicating()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);
            _stack.Push(ScreenId.Pause);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.Controls, ScreenId.Pause }));
        }

        [Test]
        public void Push_None_Throws()
        {
            Assert.Throws<ArgumentException>(() => _stack.Push(ScreenId.None));
            Assert.Throws<ArgumentException>(() => _stack.ReplaceTop(ScreenId.None));
            Assert.Throws<ArgumentException>(() => _stack.Reset(ScreenId.None));
        }

        [Test]
        public void Pop_RevealsPreviousScreen()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);

            Assert.That(_stack.Pop(), Is.True);

            Assert.That(_stack.Top, Is.EqualTo(ScreenId.Pause));
            Assert.That(_changes[_changes.Count - 1], Is.EqualTo((ScreenId.Controls, ScreenId.Pause)));
        }

        [Test]
        public void Pop_OnEmpty_ReturnsFalse_AndDoesNotRaise()
        {
            Assert.That(_stack.Pop(), Is.False);
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void Remove_NonTop_DoesNotRaise()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);
            _changes.Clear();

            Assert.That(_stack.Remove(ScreenId.Pause), Is.True);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.Controls }));
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void Remove_Top_Raises()
        {
            _stack.Push(ScreenId.Pause);

            Assert.That(_stack.Remove(ScreenId.Pause), Is.True);
            Assert.That(_changes[_changes.Count - 1], Is.EqualTo((ScreenId.Pause, ScreenId.None)));
        }

        [Test]
        public void Remove_Missing_ReturnsFalse()
        {
            Assert.That(_stack.Remove(ScreenId.Evidence), Is.False);
        }

        [Test]
        public void ReplaceTop_SwapsTopScreen_WithSingleEvent()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);
            _changes.Clear();

            _stack.ReplaceTop(ScreenId.Evidence);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.Pause, ScreenId.Evidence }));
            Assert.That(_changes, Is.EqualTo(new[] { (ScreenId.Controls, ScreenId.Evidence) }));
        }

        [Test]
        public void ReplaceTop_OnEmpty_Pushes()
        {
            _stack.ReplaceTop(ScreenId.MainMenu);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.MainMenu }));
        }

        [Test]
        public void ReplaceTop_WithScreenLowerInStack_DoesNotDuplicate()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);

            _stack.ReplaceTop(ScreenId.Pause);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.Pause }));
        }

        [Test]
        public void Reset_LeavesOnlyGivenScreen()
        {
            _stack.Push(ScreenId.Pause);
            _stack.Push(ScreenId.Controls);

            _stack.Reset(ScreenId.MainMenu);

            Assert.That(_stack.Items, Is.EqualTo(new[] { ScreenId.MainMenu }));
            Assert.That(_stack.Contains(ScreenId.Pause), Is.False);
        }

        [Test]
        public void Clear_EmptiesStack_AndRaisesOnlyWhenSomethingWasOpen()
        {
            _stack.Clear();
            Assert.That(_changes, Is.Empty);

            _stack.Push(ScreenId.Pause);
            _stack.Clear();

            Assert.That(_stack.IsEmpty, Is.True);
            Assert.That(_changes[_changes.Count - 1], Is.EqualTo((ScreenId.Pause, ScreenId.None)));
        }
    }
}
