using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Tubumu.Core.Extensions;
using Tubumu.Core.Extensions.Object;
using TubumuMeeting.Core;
using TubumuMeeting.Libuv;

namespace TubumuMeeting.Mediasoup
{
    public class Channel
    {
        #region Constants

        private const int NsMessageMaxLen = 4194313;

        private const int NsPayloadMaxLen = 4194304;

        #endregion

        #region Private Fields

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<Channel> _logger;

        /// <summary>
        /// Unix Socket instance for sending messages to the worker process.
        /// </summary>
        private readonly UVStream _producerSocket;

        /// <summary>
        /// Unix Socket instance for receiving messages to the worker process.
        /// </summary>
        private readonly UVStream _consumerSocket;

        /// <summary>
        /// Worker process PID.
        /// </summary>
        private readonly int _processId;

        /// <summary>
        /// Closed flag.
        /// </summary>
        private bool _closed = false;

        /// <summary>
        /// Next id for messages sent to the worker process.
        /// </summary>
        private int _nextId = 0;

        /// <summary>
        /// Map of pending sent requests.
        /// </summary>
        private readonly Dictionary<int, Sent> _sents = new Dictionary<int, Sent>();

        /// <summary>
        /// Buffer for reading messages from the worker.
        /// </summary>
        private ArraySegment<byte>? _recvBuffer;

        #endregion

        #region Events

        public event Action<string, string, string>? MessageEvent;

        #endregion

        public Channel(ILogger<Channel> logger, UVStream producerSocket, UVStream consumerSocket, int processId)
        {
            _logger = logger;

            _logger.LogDebug("Channel() | constructor");

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
            {
                return;
            }

            _logger.LogDebug("Close()");

            _closed = true;

            // Close every pending sent.
            _sents.Values.ForEach(m => m?.Close?.Invoke());

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

        public Task<string?> RequestAsync(MethodId methodId, object? @internal = null, object? data = null)
        {
            var method = methodId.GetEnumStringValue();
            var id = _nextId < Int32.MaxValue ? ++_nextId : (_nextId = 1); // TODO: (alby)线程同步

            _logger.LogDebug($"RequestAsync() | [method:{method}, id:{id}]");

            if (_closed)
            {
                throw new InvalidStateException("Channel closed");
            }

            var requestMesssge = new RequestMessage
            {
                Id = id,
                Method = method,
                Internal = @internal,
                Data = data,
            };
            var nsBytes = Netstring.Encode(requestMesssge.ToCamelCaseJson());
            if (nsBytes.Length > NsMessageMaxLen)
            {
                throw new Exception("Channel request too big");
            }

            var tcs = new TaskCompletionSource<string?>();

            var sent = new Sent
            {
                RequestMessage = requestMesssge,
                Close = () =>
                {
                    if (!_sents.Remove(id))
                    {
                        return;
                    }
                    tcs.TrySetException(new InvalidStateException("Channel closed"));
                },
                Resolve = data =>
                {
                    if (!_sents.Remove(id))
                    {
                        return;
                    }
                    tcs.TrySetResult(data);
                },
                Reject = e =>
                {
                    if (!_sents.Remove(id))
                    {
                        return;
                    }
                    tcs.TrySetException(e);
                },
            };
            _sents.Add(id, sent);

            tcs.WithTimeout(TimeSpan.FromSeconds(15 + (0.1 * _sents.Count)), () => _sents.Remove(id));

            Loop.Default.Sync(() =>
            {
                try
                {
                    // This may throw if closed or remote side ended.
                    _producerSocket.Write(nsBytes, ex =>
                    {
                        if (ex != null)
                        {
                            _logger.LogError(ex, "_producerSocket.Write() | error");
                            tcs.TrySetException(ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "_producerSocket.Write() | error");
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        #region Event handles

        private void ConsumerSocketOnData(ArraySegment<byte> data)
        {
            if (_recvBuffer == null)
            {
                _recvBuffer = data;
            }
            else
            {
                var newBuffer = new byte[_recvBuffer.Value.Count + data.Count];
                Array.Copy(_recvBuffer.Value.Array, _recvBuffer.Value.Offset, newBuffer, 0, _recvBuffer.Value.Count);
                Array.Copy(data.Array, data.Offset, newBuffer, _recvBuffer.Value.Count, data.Count);
                _recvBuffer = new ArraySegment<byte>(newBuffer);
            }

            if (_recvBuffer.Value.Count > NsPayloadMaxLen)
            {
                _logger.LogError("ConsumerSocketOnData() | receiving buffer is full, discarding all data into it");
                // Reset the buffer and exit.
                _recvBuffer = null;
                return;
            }

            //_logger.LogError($"ConsumerSocketOnData: {buffer}");
            var netstring = new Netstring(_recvBuffer.Value);
            try
            {
                var nsLength = 0;
                foreach (var payload in netstring)
                {
                    nsLength += payload.NetstringLength;
                    var payloadString = Encoding.UTF8.GetString(payload.Data.Array, payload.Data.Offset, payload.Data.Count);
                    try
                    {
                        // We can receive JSON messages (Channel messages) or log strings.
                        switch (payloadString[0])
                        {
                            // 123 = '{' (a Channel JSON messsage).
                            case '{':
                                ProcessMessage(payloadString);
                                break;

                            // 68 = 'D' (a debug log).
                            case 'D':
                                //if (!payloadString.Contains("(trace)"))
                                _logger.LogDebug($"ConsumerSocketOnData() | [pid:{_processId}] { payloadString }");
                                break;

                            // 87 = 'W' (a warn log).
                            case 'W':
                                _logger.LogWarning($"ConsumerSocketOnData() | [pid:{_processId}] { payloadString }");
                                break;

                            // 69 = 'E' (an error log).
                            case 'E':
                                _logger.LogError($"ConsumerSocketOnData() | [pid:{_processId}] { payloadString }");
                                break;

                            // 88 = 'X' (a dump log).
                            case 'X':
                                // eslint-disable-next-line no-console
                                _logger.LogDebug($"ConsumerSocketOnData() | [pid:{_processId}] { payloadString }");
                                break;

                            default:
                                // eslint-disable-next-line no-console
                                _logger.LogWarning($"ConsumerSocketOnData() | worker[pid:{_processId}] unexpected data:{ payloadString }");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ConsumerSocketOnData() | received invalid message from the worker process:{ex}\ndata: {payloadString}");
                        // Reset the buffer and exit.
                        _recvBuffer = null;
                        return;
                    }
                }

                if (nsLength > 0)
                {
                    if (nsLength == _recvBuffer.Value.Count)
                    {
                        // Reset the buffer.
                        _recvBuffer = null;
                    }
                    else
                    {
                        _recvBuffer = new ArraySegment<byte>(_recvBuffer.Value.Array, _recvBuffer.Value.Offset + nsLength, _recvBuffer.Value.Count - nsLength);
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

        private void ProcessMessage(string payload)
        {
            var msg = JObject.Parse(payload);
            var id = msg["id"].Value(0);
            var accepted = msg["accepted"].Value(false);
            var targetId = msg["targetId"].Value(String.Empty);
            var @event = msg["event"].Value(string.Empty);
            var error = msg["error"].Value(string.Empty);
            var reason = msg["reason"].Value(string.Empty);
            var data = msg["data"].Value(string.Empty);
            // If a response retrieve its associated request.
            if (id > 0)
            {
                if (!_sents.TryGetValue(id, out Sent sent))
                {
                    _logger.LogError($"ProcessMessage() | received response does not match any sent request [id:{id}]");

                    return;
                }

                if (accepted)
                {
                    _logger.LogDebug($"ProcessMessage() | request succeed [method:{sent.RequestMessage.Method}, id:{sent.RequestMessage.Id}]");

                    sent.Resolve?.Invoke(data);
                }
                else if (!error.IsNullOrWhiteSpace())
                {
                    // 在 Node.js 实现中，error 的值可能是 "Error" 或 "TypeError"。
                    _logger.LogWarning($"ProcessMessage() | request failed [method:{sent.RequestMessage.Method}, id:{sent.RequestMessage.Id}]: {reason}");

                    sent.Reject?.Invoke(new Exception(reason));
                }
                else
                {
                    _logger.LogError($"ProcessMessage() | received response is not accepted nor rejected [method:{sent.RequestMessage.Method}, id:{sent.RequestMessage.Id}]");
                }
            }
            // If a notification emit it to the corresponding entity.
            else if (!targetId.IsNullOrWhiteSpace() && !@event.IsNullOrWhiteSpace())
            {
                MessageEvent?.Invoke(targetId, @event, data);
            }
            // Otherwise unexpected message.
            else
            {
                _logger.LogError($"ProcessMessage() | received message is not a response nor a notification: {payload}");
            }
        }

        #endregion
    }
}
