using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TubumuMeeting.Mediasoup
{
    public class AudioLevelObserver : RtpObserver
    {
        // Logger
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<AudioLevelObserver> _logger;

        /// <summary>
        /// @emits volumes - (volumes: AudioLevelObserverVolume[])
        /// @emits silence
        /// Observer:
        /// @emits close
        /// @emits pause
        /// @emits resume
        /// @emits addproducer - (producer: Producer)
        /// @emits removeproducer - (producer: Producer)
        /// @emits volumes - (volumes: AudioLevelObserverVolume[])
        /// @emits silence
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="routerId"></param>
        /// <param name="rtpObserverId"></param>
        /// <param name="channel"></param>
        /// <param name="appData"></param>
        /// <param name="getProducerById"></param>
        public AudioLevelObserver(ILoggerFactory loggerFactory,
                    string routerId,
                    string rtpObserverId,
                    Channel channel,
                    object? appData,
                    Func<string, Producer> getProducerById) : base(loggerFactory, routerId, rtpObserverId, channel, appData, getProducerById)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<AudioLevelObserver>();

            HandleWorkerNotifications();
        }

        private void HandleWorkerNotifications()
        {
            Channel.MessageEvent += OnChannelMessage;
        }

        private void OnChannelMessage(string targetId, string @event, string data)
        {
            if (targetId != RtpObserverId) return;
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
                        _logger.LogError($"ignoring unknown event{@event}");
                        break;
                    }
            }
        }
    }
}
