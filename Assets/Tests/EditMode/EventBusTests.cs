using System;
using System.Collections.Generic;
using CourtSim.Core.Events;
using NUnit.Framework;

namespace CourtSim.Core.Tests
{
    public sealed class EventBusTests
    {
        private readonly struct Ping
        {
            public Ping(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private readonly struct Other
        {
        }

        [Test]
        public void Publish_DeliversToEverySubscriberInOrder()
        {
            var bus = new EventBus();
            var received = new List<string>();
            bus.Subscribe<Ping>(p => received.Add("a" + p.Value));
            bus.Subscribe<Ping>(p => received.Add("b" + p.Value));

            bus.Publish(new Ping(7));

            Assert.That(received, Is.EqualTo(new[] { "a7", "b7" }));
        }

        [Test]
        public void Publish_OnlyReachesHandlersOfThatType()
        {
            var bus = new EventBus();
            int pings = 0;
            bus.Subscribe<Ping>(_ => pings++);

            bus.Publish(new Other());

            Assert.That(pings, Is.EqualTo(0));
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNothing()
        {
            var bus = new EventBus();

            Assert.DoesNotThrow(() => bus.Publish(new Ping(1)));
        }

        [Test]
        public void DisposingSubscription_StopsDelivery_AndIsIdempotent()
        {
            var bus = new EventBus();
            int count = 0;
            IDisposable token = bus.Subscribe<Ping>(_ => count++);

            token.Dispose();
            token.Dispose();
            bus.Publish(new Ping(1));

            Assert.That(count, Is.EqualTo(0));
            Assert.That(bus.HandlerCount<Ping>(), Is.EqualTo(0));
        }

        [Test]
        public void Unsubscribe_ReturnsFalseForUnknownHandler()
        {
            var bus = new EventBus();
            Action<Ping> handler = _ => { };

            Assert.That(bus.Unsubscribe(handler), Is.False);
            Assert.That(bus.Unsubscribe<Ping>(null), Is.False);
        }

        [Test]
        public void Subscribe_RejectsNullHandler()
        {
            var bus = new EventBus();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<Ping>(null));
        }

        [Test]
        public void UnsubscribingDuringDispatch_TakesEffectOnNextPublish()
        {
            var bus = new EventBus();
            int second = 0;
            IDisposable secondToken = null;
            bus.Subscribe<Ping>(_ => secondToken.Dispose());
            secondToken = bus.Subscribe<Ping>(_ => second++);

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));

            Assert.That(second, Is.EqualTo(1));
        }

        [Test]
        public void SubscribingDuringDispatch_DoesNotReceiveCurrentEvent()
        {
            var bus = new EventBus();
            int late = 0;
            bool added = false;
            bus.Subscribe<Ping>(_ =>
            {
                if (!added)
                {
                    added = true;
                    bus.Subscribe<Ping>(__ => late++);
                }
            });

            bus.Publish(new Ping(1));
            Assert.That(late, Is.EqualTo(0));

            bus.Publish(new Ping(2));
            Assert.That(late, Is.EqualTo(1));
        }

        [Test]
        public void NestedPublishOfSameType_IsDelivered()
        {
            var bus = new EventBus();
            var values = new List<int>();
            bus.Subscribe<Ping>(p =>
            {
                values.Add(p.Value);
                if (p.Value < 3)
                {
                    bus.Publish(new Ping(p.Value + 1));
                }
            });

            bus.Publish(new Ping(1));

            Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ThrowingHandler_DoesNotStopOthers_AndIsRethrownWithoutSink()
        {
            var bus = new EventBus();
            int after = 0;
            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<Ping>(_ => after++);

            var ex = Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(1)));

            Assert.That(ex.Message, Is.EqualTo("boom"));
            Assert.That(after, Is.EqualTo(1));
        }

        [Test]
        public void MultipleThrowingHandlers_AreAggregated()
        {
            var bus = new EventBus();
            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("one"));
            bus.Subscribe<Ping>(_ => throw new ArgumentException("two"));

            var ex = Assert.Throws<AggregateException>(() => bus.Publish(new Ping(1)));

            Assert.That(ex.InnerExceptions.Count, Is.EqualTo(2));
        }

        [Test]
        public void HandlerExceptionSink_ReceivesErrorsInsteadOfThrowing()
        {
            var errors = new List<Exception>();
            var bus = new EventBus { HandlerException = errors.Add };
            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("one"));
            bus.Subscribe<Ping>(_ => throw new InvalidOperationException("two"));

            Assert.DoesNotThrow(() => bus.Publish(new Ping(1)));
            Assert.That(errors.Count, Is.EqualTo(2));
        }

        [Test]
        public void Clear_RemovesAllHandlers()
        {
            var bus = new EventBus();
            bus.Subscribe<Ping>(_ => { });
            bus.Subscribe<Other>(_ => { });

            bus.Clear();

            Assert.That(bus.HandlerCount<Ping>(), Is.EqualTo(0));
            Assert.That(bus.HandlerCount<Other>(), Is.EqualTo(0));
        }
    }
}
