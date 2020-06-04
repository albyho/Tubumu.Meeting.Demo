using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class AudioLevelObserver : RtpObserver
    {
        // Logger
        private readonly ILogger<AudioLevelObserver> _logger;

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits volumes - (volumes: AudioLevelObserverVolume[])</para>
        /// <para>@emits silence</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits pause</para>
        /// <para>@emits resume</para>
        /// <para>@emits addproducer - (producer: Producer)</para>
        /// <para>@emits removeproducer - (producer: Producer)</para>
        /// <para>@emits volumes - (volumes: AudioLevelObserverVolume[])</para>
        /// <para>@emits silence</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="rtpObserverInternalData"></param>
        /// <param name="channel"></param>
        /// <param name="payloadChannel"></param>
        /// <param name="appData"></param>
        /// <param name="getProducerById"></param>
        public AudioLevelObserver(ILoggerFactory loggerFactory,
                    RtpObserverInternalData rtpObserverInternalData,
                    Channel channel,
                    PayloadChannel payloadChannel,
                    Dictionary<string, object>? appData,
                    Func<string, Producer> getProducerById) : base(loggerFactory, rtpObserverInternalData, channel, payloadChannel, appData, getProducerById)
        {
            _logger = loggerFactory.CreateLogger<AudioLevelObserver>();

            HandleWorkerNotifications();
        }

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != Internal.RtpObserverId) return;
            switch (@event)
            {
                case "volumes":
                    {
                        var notification = JsonConvert.DeserializeObject<AudioLevelObserverVolumeNotificationData[]>(data);
                        var volumes = notification.Select(m => new AudioLevelObserverVolume
                        {
                            Producer = GetProducerById(m.ProducerId),
                            Volume = m.Volume,
                        }).ToArray();

                        if (volumes.Length > 0)
                        {
                            Emit("volumes", volumes);

                            // Emit observer event.
                            Observer.Emit("volumes", volumes);
                        }

                        break;
                    }
                case "silence":
                    {
                        Emit("silence");

                        // Emit observer event.
                        Observer.Emit("silence");

                        break;
                    }
                default:
                    {
                        _logger.LogError($"OnChannelMessage() | ignoring unknown event{@event}");
                        break;
                    }
            }
        }
    }
}
