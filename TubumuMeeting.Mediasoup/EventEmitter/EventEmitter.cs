using System;
using System.Collections.Generic;
using System.Threading;

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

        private readonly Dictionary<string, List<Action<object?>>> _events;

        /// <summary>
        /// The EventEmitter object to subscribe to events with
        /// </summary>
        public EventEmitter()
        {
            _events = new Dictionary<string, List<Action<object?>>>();
        }

        /// <summary>
        /// Whenever eventName is emitted, the methods attached to this event will be called
        /// </summary>
        /// <param name="eventName">Event name to subscribe to</param>
        /// <param name="method">Method to add to the event</param>
        public void On(string eventName, Action<object?> method)
        {
            if (_events.TryGetValue(eventName, out List<Action<object?>> subscribedMethods))
            {
                subscribedMethods.Add(method);
            }
            else
            {
                _events.Add(eventName, new List<Action<object?>> { method });
            }
        }

        /// <summary>
        /// Emits the event and associated data
        /// </summary>
        /// <param name="eventName">Event name to be emitted</param>
        /// <param name="data">Data to call the attached methods with</param>
        public void Emit(string eventName, object? data = null)
        {
            if (!_events.TryGetValue(eventName, out List<Action<object?>> subscribedMethods))
            {
                //throw new DoesNotExistException(string.Format("Event [{0}] does not exist in the emitter. Consider calling EventEmitter.On", eventName));
            }
            else
            {
                foreach (var f in subscribedMethods)
                {
                    f(data);
                }
            }
        }

        /// <summary>
        /// Removes [method] from the event
        /// </summary>
        /// <param name="eventName">Event name to remove function from</param>
        /// <param name="method">Method to remove from eventName</param>
        public void RemoveListener(string eventName, Action<object?> method)
        {
            if (!_events.TryGetValue(eventName, out List<Action<object?>> subscribedMethods))
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
        }

        /// <summary>
        /// Removes all methods from the event [eventName]
        /// </summary>
        /// <param name="eventName">Event name to remove methods from</param>
        public void RemoveAllListeners(string eventName)
        {
            if (!_events.TryGetValue(eventName, out List<Action<object?>> subscribedMethods))
            {
                throw new DoesNotExistException(string.Format("Event [{0}] does not exist to have methods removed.", eventName));
            }
            else
            {
                subscribedMethods.RemoveAll(x => x != null);
            }
        }

        /// <summary>
        /// Emits the event and runs all associated methods asynchronously
        /// </summary>
        /// <param name="eventName">The event name to call methods for</param>
        /// <param name="data">The data to call all the methods with</param>
        public void EmitAsync(string eventName, object? data = null)
        {
            if (!_events.TryGetValue(eventName, out List<Action<object?>> subscribedMethods))
            {
                throw new DoesNotExistException(string.Format("Event [{0}] does not exist in the emitter. Consider calling EventEmitter.On", eventName));
            }
            else
            {
                foreach (var f in subscribedMethods)
                {
                    ThreadPool.QueueUserWorkItem(m => f(m), data);
                }
            }
        }
    }
}
