using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace CourtSim.Core.Events
{
    /// <summary>
    /// Synchronous publish/subscribe bus keyed by event type.
    /// </summary>
    /// <remarks>
    /// <para>Events are value types so publishing never boxes. Handlers may subscribe or
    /// unsubscribe while an event is being dispatched: dispatch iterates over a pooled
    /// snapshot, so changes take effect from the next publish.</para>
    /// <para>A throwing handler never prevents the remaining handlers from running. Errors
    /// are routed to <see cref="HandlerException"/> when it is set; otherwise they are
    /// rethrown once dispatch has finished.</para>
    /// </remarks>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        /// <summary>Receives exceptions thrown by handlers. When null, exceptions are rethrown after dispatch.</summary>
        public Action<Exception> HandlerException { get; set; }

        /// <summary>Registers <paramref name="handler"/> for events of type <typeparamref name="T"/>.</summary>
        /// <returns>A token that unsubscribes the handler when disposed.</returns>
        public IDisposable Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_handlers.TryGetValue(typeof(T), out List<Delegate> list))
            {
                list = new List<Delegate>();
                _handlers.Add(typeof(T), list);
            }

            list.Add(handler);
            return new Subscription<T>(this, handler);
        }

        /// <summary>Removes one registration of <paramref name="handler"/>. Returns false if it was not registered.</summary>
        public bool Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null || !_handlers.TryGetValue(typeof(T), out List<Delegate> list))
            {
                return false;
            }

            return list.Remove(handler);
        }

        /// <summary>Delivers <paramref name="evt"/> to every handler registered for <typeparamref name="T"/>.</summary>
        public void Publish<T>(T evt) where T : struct
        {
            if (!_handlers.TryGetValue(typeof(T), out List<Delegate> list) || list.Count == 0)
            {
                return;
            }

            int count = list.Count;
            Delegate[] snapshot = ArrayPool<Delegate>.Shared.Rent(count);
            List<Exception> errors = null;
            try
            {
                list.CopyTo(snapshot);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        ((Action<T>)snapshot[i]).Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        if (errors == null)
                        {
                            errors = new List<Exception>(1);
                        }

                        errors.Add(ex);
                    }
                }
            }
            finally
            {
                Array.Clear(snapshot, 0, count);
                ArrayPool<Delegate>.Shared.Return(snapshot);
            }

            if (errors != null)
            {
                ReportErrors(errors);
            }
        }

        /// <summary>Number of handlers currently registered for <typeparamref name="T"/>.</summary>
        public int HandlerCount<T>() where T : struct
        {
            return _handlers.TryGetValue(typeof(T), out List<Delegate> list) ? list.Count : 0;
        }

        /// <summary>Removes every handler for every event type.</summary>
        public void Clear()
        {
            _handlers.Clear();
        }

        private void ReportErrors(List<Exception> errors)
        {
            if (HandlerException != null)
            {
                foreach (Exception error in errors)
                {
                    HandlerException(error);
                }

                return;
            }

            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }

            throw new AggregateException("Multiple event handlers threw.", errors);
        }

        private sealed class Subscription<T> : IDisposable where T : struct
        {
            private EventBus _bus;
            private Action<T> _handler;

            public Subscription(EventBus bus, Action<T> handler)
            {
                _bus = bus;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_bus == null)
                {
                    return;
                }

                _bus.Unsubscribe(_handler);
                _bus = null;
                _handler = null;
            }
        }
    }
}
