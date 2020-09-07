using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TubumuMeeting.Mediasoup
{
    public class EventEmitter : IEventEmitter
    {
        /*
        {
            "subscribe_event",
            [
                HandleSubscribe<List<object>>,
                DoDbWork<List<object>>,
                SendInfo<List<object>>
            ],
             "listen_event",
            [
                HandleListen<List<object>>
            ]
        }
        */

        private readonly Dictionary<string, List<Func<object?, Task>>> _events;
        private readonly ReaderWriterLockSlim _rwl;

        /// <summary>
        /// The EventEmitter object to subscribe to events with
        /// </summary>
        public EventEmitter()
        {
            _events = new Dictionary<string, List<Func<object?, Task>>>();
            _rwl = new ReaderWriterLockSlim();
        }

        /// <summary>
        /// Whenever eventName is emitted, the methods attached to this event will be called
        /// </summary>
        /// <param name="eventName">Event name to subscribe to</param>
        /// <param name="method">Method to add to the event</param>
        public void On(string eventName, Func<object?, Task> method)
        {
            _rwl.EnterWriteLock();
            if (_events.TryGetValue(eventName, out List<Func<object?, Task>> subscribedMethods))
            {
                subscribedMethods.Add(method);
            }
            else
            {
                _events.Add(eventName, new List<Func<object?, Task>> { method });
            }
            _rwl.ExitWriteLock();
        }

        /// <summary>
        /// Emits the event and runs all associated methods asynchronously
        /// </summary>
        /// <param name="eventName">The event name to call methods for</param>
        /// <param name="data">The data to call all the methods with</param>
        public void Emit(string eventName, object? data = null)
        {
            _rwl.EnterReadLock();
            if (!_events.TryGetValue(eventName, out List<Func<object?, Task>> subscribedMethods))
            {
                //throw new DoesNotExistException(string.Format("Event [{0}] does not exist in the emitter. Consider calling EventEmitter.On", eventName));
            }
            else
            {
                foreach (var f in subscribedMethods)
                {
                    // For Testing
                    //f(data).ConfigureAwait(false).GetAwaiter().GetResult();
                    f(data).ContinueWith(val =>
                    {
                        val.Exception.Handle(ex =>
                        {
                            Debug.WriteLine("Emit fail:{0}", ex);
                            return true;
                        });
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            _rwl.ExitReadLock();
        }

        /// <summary>
        /// Removes [method] from the event
        /// </summary>
        /// <param name="eventName">Event name to remove function from</param>
        /// <param name="method">Method to remove from eventName</param>
        public void RemoveListener(string eventName, Func<object?, Task> method)
        {
            _rwl.EnterWriteLock();
            if (!_events.TryGetValue(eventName, out List<Func<object?, Task>> subscribedMethods))
            {
                throw new DoesNotExistException(string.Format("Event [{0}] does not exist to have listeners removed.", eventName));
            }
            else
            {
                var _event = subscribedMethods.Exists(e => e == method);
                if (_event == false)
                {
                    throw new DoesNotExistException(string.Format("Func [{0}] does not exist to be removed.", method.Method));
                }
                else
                {
                    subscribedMethods.Remove(method);
                }
            }
            _rwl.ExitWriteLock();
        }

        /// <summary>
        /// Removes all methods from the event [eventName]
        /// </summary>
        /// <param name="eventName">Event name to remove methods from</param>
        public void RemoveAllListeners(string eventName)
        {
            _rwl.EnterWriteLock();
            if (!_events.TryGetValue(eventName, out List<Func<object?, Task>> subscribedMethods))
            {
                throw new DoesNotExistException(string.Format("Event [{0}] does not exist to have methods removed.", eventName));
            }
            else
            {
                subscribedMethods.RemoveAll(m => true);
            }
            _rwl.ExitWriteLock();
        }
    }
}
