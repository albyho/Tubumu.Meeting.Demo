using System;

namespace TubumuMeeting.Mediasoup
{
    public interface IEventEmitter
    {
        /// <summary>
        /// Whenever eventName is emitted, the methods attached to this event will be called
        /// </summary>
        /// <param name="eventName">Event name to subscribe to</param>
        /// <param name="method">Method to add to the event</param>
        void On(string eventName, Action<object?> method);

        /// <summary>
        /// Emits the event and associated data
        /// </summary>
        /// <param name="eventName">Event name to be emitted</param>
        /// <param name="data">Data to call the attached methods with</param>
        void Emit(string eventName, object? data = null);

        /// <summary>
        /// Removes [method] from the event
        /// </summary>
        /// <param name="eventName">Event name to remove function from</param>
        /// <param name="method">Method to remove from eventName</param>
        void RemoveListener(string eventName, Action<object?> method);

        /// <summary>
        /// Removes all methods from the event [eventName]
        /// </summary>
        /// <param name="eventName">Event name to remove methods from</param>
        void RemoveAllListeners(string eventName);

        /// <summary>
        /// Emits the event and runs all associated methods asynchronously
        /// </summary>
        /// <param name="eventName">The event name to call methods for</param>
        /// <param name="data">The data to call all the methods with</param>
        void EmitAsync(string eventName, object? data = null);
    }
}
