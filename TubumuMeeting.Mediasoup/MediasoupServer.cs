using System;
using System.Collections.Generic;
using Tubumu.Core.Extensions.Object;

namespace TubumuMeeting.Mediasoup
{
    public class MediasoupServer
    {
        private int _nextMediasoupWorkerIndex = 0;

        private readonly object _locker = new object();

        private readonly List<Worker> _workers = new List<Worker>();

        /// <summary>
        /// Get a cloned copy of the mediasoup supported RTP capabilities.
        /// </summary>
        /// <returns></returns>
        public static RtpCapabilities GetSupportedRtpCapabilities()
        {
            return RtpCapabilities.SupportedRtpCapabilities.DeepClone<RtpCapabilities>();
        }

        /// <summary>
        /// Get next mediasoup Worker.
        /// </summary>
        /// <returns></returns>
        public Worker GetWorker()
        {
            lock (_locker)
            {
                if (_nextMediasoupWorkerIndex > _workers.Count - 1)
                {
                    throw new Exception("none worker");
                }
                var worker = _workers[_nextMediasoupWorkerIndex];
                if (++_nextMediasoupWorkerIndex == _workers.Count)
                    _nextMediasoupWorkerIndex = 0;

                return worker;
            }
        }

        /// <summary>
        /// Add worker.
        /// </summary>
        /// <param name="worker"></param>
        public void AddWorker(Worker worker)
        {
            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }

            lock (_locker)
            {
                _workers.Add(worker);
            }
        }
    }
}
