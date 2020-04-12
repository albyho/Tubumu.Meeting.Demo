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
        /// Observer instance.
        /// </summary>
        public AudioLevelObserverObserver Observer { get; } = new AudioLevelObserverObserver();

        #region Events

        public event Action<AudioLevelObserverVolume[]>? VolumesEvent;

        public event Action? SilenceEvent;

        #endregion

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
                            VolumesEvent?.Invoke(volumes);

                            // Emit observer event.
                            Observer.EmitVolumes(volumes);
                        }

                        break;
                    }
                case "silence":
                    {
                        SilenceEvent?.Invoke();

                        // Emit observer event.
                        Observer.EmitSilence();

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
