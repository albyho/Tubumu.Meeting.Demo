using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TubumuMeeting.Libuv;
using TubumuMeeting.Libuv.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Tubumu.Core.Extensions;

namespace TubumuMeeting.Mediasoup
{
    public interface IWorker : IDisposable
    {
        bool Closed { get; }

        // Custom app data.
        object? AppData { get; }

        void Close();
    }

    public class Worker : IWorker
    {
        #region Constants

        private const int StdioCount = 5;

        #endregion

        #region Private Fields

        // Logger factory for create Channel logger.
        private ILoggerFactory _loggerFactory;

        // Logger
        private ILogger<Worker> _logger;

        // mediasoup-worker child process.
        private TubumuMeeting.Libuv.Process? _child;

        // Is spawn done?
        private bool _spawnDone;

        private Channel _channel;

        private readonly IPCPipe[] _pipes;

        #endregion

        #region Public Properties

        // Closed flag.
        public bool Closed { get; private set; }

        // Custom app data.
        public object? AppData { get; }

        public WorkerObserver Observer { get; } = new WorkerObserver();

        #endregion

        #region Events

        public event Action<Exception>? DiedEvent;

        public event Action<Exception>? FailureEvent;

        public event Action? SuccessEvent;

        #endregion

        public Worker(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Worker>();

            var workerSettings = mediasoupOptions.MediasoupSettings.WorkerSettings;

            AppData = workerSettings.AppData;

            var env = new[] { $"MEDIASOUP_VERSION={mediasoupOptions.MediasoupVersion}" };

            var args = new List<String>();
            args.Add(mediasoupOptions.WorkerPath);
            if (workerSettings.LogLevel.HasValue)
            {
                args.Add($"--logLevel={Enum.GetName(typeof(WorkerLogLevel), workerSettings.LogLevel).ToLowerInvariant()}");
            }
            if (!workerSettings.LogTags.IsNullOrEmpty())
            {
                workerSettings.LogTags.ForEach(m => args.Add($"--logTag={m}"));
            }
            if (workerSettings.RtcMinPort.HasValue)
            {
                args.Add($"--rtcMinPort={workerSettings.RtcMinPort}");
            }
            if (workerSettings.RtcMaxPort.HasValue)
            {
                args.Add($"--rtcMaxPort={workerSettings.RtcMaxPort}");
            }
            if (!workerSettings.DtlsCertificateFile.IsNullOrWhiteSpace())
            {
                args.Add($"--dtlsCertificateFile={workerSettings.DtlsCertificateFile}");
            }
            if (!workerSettings.DtlsPrivateKeyFile.IsNullOrWhiteSpace())
            {
                args.Add($"--dtlsPrivateKeyFile={workerSettings.DtlsPrivateKeyFile}");
            }

            _logger.LogDebug($"spawning worker process: {args.ToArray().Join(" ")}");

            _pipes = new IPCPipe[StdioCount];
            // 备注：忽略标准输入
            for (var i = 1; i < StdioCount; i++)
            {
                _pipes[i] = new IPCPipe() { Writeable = true, Readable = true };
            }

            try
            {
                // 备注：和 Node.js 不同，_child 没有 error 事件。不过，Process.Spawn 可抛出异常。
                _child = Process.Spawn(new ProcessOptions()
                {
                    File = mediasoupOptions.WorkerPath,
                    Arguments = args.ToArray(),
                    Environment = env,
                    Detached = false,
                    Streams = _pipes,
                }, OnExit);
            }
            catch (Exception ex)
            {
                _child = null;
                Close();

                if (!_spawnDone)
                {
                    _spawnDone = true;
                    _logger.LogError($"worker process failed: {ex.Message}");
                    FailureEvent?.Invoke(ex);
                }
                else
                {
                    // 执行到这里的可能性？
                    _logger.LogError($"worker process error: {ex.Message}");
                    DiedEvent?.Invoke(ex);
                }
            }

            _channel = new Channel(_loggerFactory.CreateLogger<Channel>(), _pipes[3], _pipes[4]);
            _channel.RunningEvent += OnChannelRunning;

            _pipes.ForEach(m => m?.Resume());
        }

        public void Close()
        {
            if (Closed)
                return;
            Closed = true;

            // Kill the worker process.
            if (_child != null)
            {
                // Remove event listeners but leave a fake 'error' hander to avoid
                // propagation.
                _child.Kill(15/*SIGTERM*/);
                _child = null;
            }

            // Close the Channel instance.
            _channel?.Close();

            // Emit observer event.
            Observer.EmitClose();
        }

        #region Request

        /// <summary>
        /// Dump Worker.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return _channel.RequestAsync(MethodId.WORKER_DUMP.GetEnumStringValue());
        }

        /// <summary>
        /// Get mediasoup-worker process resource usage.
        /// </summary>
        public Task<string?> GetResourceUsageAsync()
        {
            _logger.LogDebug("GetResourceUsageAsync()");
            return _channel.RequestAsync(MethodId.WORKER_GET_RESOURCE_USAGE.GetEnumStringValue());
        }

        /// <summary>
        /// Update settings.
        /// </summary>
        public Task<string?> UpdateSettingsAsync(WorkerLogLevel logLevel, IEnumerable<string> logTags)
        {
            _logger.LogDebug("UpdateSettingsAsync()");
            var reqData = new { logLevel = logLevel.GetEnumStringValue(), logTags };
            return _channel.RequestAsync(MethodId.WORKER_UPDATE_SETTINGS.GetEnumStringValue(), null, reqData);
        }

        /*
        /// <summary>
        /// Create a Router.
        /// </summary>
        public Router CreateRouter(IEnumerable<RtpCodecCapability> mediaCodecs, object? appData = null)
        {
            _logger.LogDebug("CreateRouter()");

            if (appData && typeof appData !== 'object')
			throw new TypeError('if given, appData must be an object');

        // This may throw.
        const rtpCapabilities = ortc.generateRouterRtpCapabilities(mediaCodecs);

        const internal = { routerId: uuidv4() };

        await this._channel.request('worker.createRouter', internal);

		const data = { rtpCapabilities };
        const router = new Router(
			{

                internal,
				data,
				channel : this._channel,
                appData

            });

        this._routers.add(router);

        router.on('@close', () => this._routers.delete(router));

		// Emit observer event.
		this._observer.safeEmit('newrouter', router);

		return router;
        }
        */

        #endregion

        #region Event handles

        private void OnChannelRunning(string processId)
        {
            _channel.RunningEvent -= OnChannelRunning;
            if (!_spawnDone)
            {
                _spawnDone = true;
                _logger.LogDebug($"worker process running [pid:{processId}]");
                SuccessEvent?.Invoke();
                //Loop.Default.QueueUserWorkItem(async () =>
                //{
                //    //var ttt = await UpdateSettingsAsync(WorkerLogLevel.Debug, new[] { "info" });
                //    var ttt = await GetResourceUsageAsync();
                //    ;
                //});
                //var ttt = UpdateSettingsAsync(WorkerLogLevel.Debug, new[] { "info" }).GetAwaiter().GetResult();
                UpdateSettingsAsync(WorkerLogLevel.Debug, new[] { "info" });
                ;
            }
        }

        private void OnExit(Process process)
        {
            _child = null;
            Close();

            if (!_spawnDone)
            {
                _spawnDone = true;

                if (process.ExitCode == 42)
                {
                    _logger.LogError("worker process failed due to wrong settings");
                    FailureEvent?.Invoke(new Exception("wrong settings"));
                }
                else
                {
                    _logger.LogError($"worker process failed unexpectedly [code:{process.ExitCode}, signal:{process.TermSignal}]");
                    FailureEvent?.Invoke(new Exception($"[code:{ process.ExitCode}, signal:{ process.TermSignal}]"));
                }
            }
            else
            {
                _logger.LogError($"worker process died unexpectedly [code:{process.ExitCode}, signal:{process.TermSignal}]");
            }
        }

        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)。
                    _child?.Dispose();
                    _pipes.ForEach(m => m?.Dispose());
                }

                // TODO: 释放未托管的资源(未托管的对象)并在以下内容中替代终结器。
                // TODO: 将大型字段设置为 null。

                disposedValue = true;
            }
        }

        // TODO: 仅当以上 Dispose(bool disposing) 拥有用于释放未托管资源的代码时才替代终结器。
        // ~Worker()
        // {
        //   // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
        //   Dispose(false);
        // }

        // 添加此代码以正确实现可处置模式。
        public void Dispose()
        {
            // 请勿更改此代码。将清理代码放入以上 Dispose(bool disposing) 中。
            Dispose(true);
            // TODO: 如果在以上内容中替代了终结器，则取消注释以下行。
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
