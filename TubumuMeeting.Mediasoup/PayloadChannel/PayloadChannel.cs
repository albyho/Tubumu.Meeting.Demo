using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;
using TubumuMeeting.Libuv;
using TubumuMeeting.Netstrings;

namespace TubumuMeeting.Mediasoup
{
    public class PayloadChannel
    {
        #region Constants

        private const int NsMessageMaxLen = 4194313;

        private const int NsPayloadMaxLen = 4194304;

        #endregion

        #region Private Fields

        // Logger
        private readonly ILogger<PayloadChannel> _logger;

        // Unix Socket instance for sending messages to the worker process.
        private readonly UVStream _producerSocket;

        // Unix Socket instance for receiving messages to the worker process.
        private readonly UVStream _consumerSocket;

        // Worker process PID.
        private readonly int _processId;

        // Closed flag.
        private bool _closed = false;

        // Buffer for reading messages from the worker.
        private StringBuilder? _recvBuffer;

        #endregion

        #region Events

        public event Action<string>? RunningEvent;

        public event Action<string, string, string>? MessageEvent;

        #endregion

        public PayloadChannel(ILogger<PayloadChannel> logger, UVStream producerSocket, UVStream consumerSocket, int processId)
        {
            _logger = logger;

            _logger.LogDebug("PayloadChannel() | constructor");

            _producerSocket = producerSocket;
            _consumerSocket = consumerSocket;
            _processId = processId;

            _consumerSocket.Data += ConsumerSocketOnData;
            _consumerSocket.Closed += ConsumerSocketOnClosed;
            _consumerSocket.Error += ConsumerSocketOnError;
            _producerSocket.Closed += ProducerSocketOnClosed;
            _producerSocket.Error += ProducerSocketOnError;
        }

        public void Close()
        {
            if (_closed)
                return;

            _logger.LogDebug("Close()");

            _closed = true;

            // Remove event listeners but leave a fake 'error' hander to avoid
            // propagation.
            _consumerSocket.Closed -= ConsumerSocketOnClosed;
            _consumerSocket.Error -= ConsumerSocketOnError;

            _producerSocket.Closed -= ProducerSocketOnClosed;
            _producerSocket.Error -= ProducerSocketOnError;

            // Destroy the socket after a while to allow pending incoming messages.
            // 在 Node.js 实现中，延迟了 200 ms。
            try
            {
                _producerSocket.Close();
            }
            catch (Exception)
            {

            }

            try
            {
                _consumerSocket.Close();
            }
            catch (Exception)
            {

            }
        }

        #region Event handles

        private void ConsumerSocketOnData(ArraySegment<byte> data)
        {
            var buffer = Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);

            if (_recvBuffer == null)
            {
                _recvBuffer = new StringBuilder(buffer);
            }
            else
            {
                _recvBuffer.Append(buffer);
            }

            var message = _recvBuffer.ToString();
            if (message.Length > NsPayloadMaxLen)
            {
                _logger.LogError("ConsumerSocketOnData() | receiving buffer is full, discarding all data into it");
                // Reset the buffer and exit.
                _recvBuffer = null;
                return;
            }

            //_logger.LogError($"ConsumerSocketOnData: {buffer}");
            using var nsReader = new NetstringReader(message);
            try
            {
                var nsPayloadLength = 0;
                foreach (var nsPayload in nsReader)
                {
                    nsPayloadLength += nsPayload.Length.ToString().Length + 1 + nsPayload.Length + 1;
                    ProcessMessage(nsPayload);
                }

                if (nsPayloadLength > 0)
                {
                    if (nsPayloadLength == message.Length)
                    {
                        // Reset the buffer.
                        _recvBuffer = null;
                    }
                    else
                    {
                        _recvBuffer = new StringBuilder(message.Substring(nsPayloadLength, message.Length - nsPayloadLength));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"ConsumerSocketOnData() | invalid netstring data received from the worker process:{ex}");
                // Reset the buffer and exit.
                _recvBuffer = null;
                return;
            }
        }

        private void ConsumerSocketOnClosed()
        {
            _logger.LogDebug("ConsumerSocketOnClosed() | Consumer Channel ended by the worker process");
        }

        private void ConsumerSocketOnError(Exception exception)
        {
            _logger.LogDebug("ConsumerSocketOnError() | Consumer Channel error", exception);
        }

        private void ProducerSocketOnClosed()
        {
            _logger.LogDebug("ProducerSocketOnClosed() | Producer Channel ended by the worker process");
        }

        private void ProducerSocketOnError(Exception exception)
        {
            _logger.LogDebug("ProducerSocketOnError() | Producer Channel error", exception);
        }

        #endregion

        #region Private Methods

        private void ProcessMessage(string nsPayload)
        {
            var msg = JObject.Parse(nsPayload);
            var targetId = msg["targetId"].Value(String.Empty);
            var @event = msg["event"].Value(string.Empty);
            var data = msg["data"].Value(string.Empty);

            if (!targetId.IsNullOrWhiteSpace() && !@event.IsNullOrWhiteSpace())
            {
                _logger.LogError("received message is not a notification");
                return;
            }
        }

        #endregion
    }
}
