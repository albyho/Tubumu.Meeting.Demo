using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tubumu.Core.Extensions;
using TubumuMeeting.Libuv;

namespace TubumuMeeting.Mediasoup
{
    /// <summary>
    /// A worker represents a mediasoup C++ subprocess that runs in a single CPU core and handles Router instances.
    /// </summary>
    public class Worker : EventEmitter
    {
        #region Constants

        private const int StdioCount = 5;

        #endregion

        #region Private Fields

        // Logger factory for create Channel logger.
        private readonly ILoggerFactory _loggerFactory;

        // Logger
        private readonly ILogger<Worker> _logger;

        // mediasoup-worker child process.
        private TubumuMeeting.Libuv.Process? _child;

        // Worker process PID.
        public int ProcessId { get; private set; }

        // Is spawn done?
        private bool _spawnDone;

        private readonly Channel _channel;

        private readonly UVStream[] _pipes;

        // Routers set.
        private readonly List<Router> _routers = new List<Router>();

        #endregion

        #region Public Properties

        // Closed flag.
        public bool Closed { get; private set; }

        // Custom app data.
        public Dictionary<string, object>? AppData { get; }

        #endregion

        public EventEmitter Observer { get; } = new EventEmitter();

        /// <summary>
        /// <para>Events:</para>
        /// <para>@emits died - (error: Error)</para>
        /// <para>@emits @success</para>
        /// <para>@emits @failure - (error: Error)</para>
        /// <para>Observer events:</para>
        /// <para>@emits close</para>
        /// <para>@emits newrouter - (router: Router)</para>
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <param name="mediasoupOptions"></param>
        public Worker(ILoggerFactory loggerFactory, MediasoupOptions mediasoupOptions)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Worker>();

            var workerSettings = mediasoupOptions.MediasoupSettings.WorkerSettings;

            AppData = workerSettings.AppData;

            var env = new[] { $"MEDIASOUP_VERSION={mediasoupOptions.MediasoupStartupSettings.MediasoupVersion}" };

            var args = new List<string>
            {
                mediasoupOptions.MediasoupStartupSettings.WorkerPath
            };
            if (workerSettings.LogLevel.HasValue)
            {
                args.Add($"--logLevel={workerSettings.LogLevel.GetEnumStringValue()}");
            }
            if (!workerSettings.LogTags.IsNullOrEmpty())
            {
                workerSettings.LogTags.ForEach(m => args.Add($"--logTag={m.GetEnumStringValue()}"));
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

            _logger.LogDebug($"Worker() | spawning worker process: {args.ToArray().Join(" ")}");

            _pipes = new Pipe[StdioCount];
            // 备注：忽略标准输入
            for (var i = 1; i < StdioCount; i++)
            {
                _pipes[i] = new Pipe() { Writeable = true, Readable = true };
            }

            try
            {
                // 备注：和 Node.js 不同，_child 没有 error 事件。不过，Process.Spawn 可抛出异常。
                _child = Process.Spawn(new ProcessOptions()
                {
                    File = mediasoupOptions.MediasoupStartupSettings.WorkerPath,
                    Arguments = args.ToArray(),
                    Environment = env,
                    Detached = false,
                    Streams = _pipes,
                }, OnExit);

                ProcessId = _child.Id;
            }
            catch (Exception ex)
            {
                _child = null;
                Close();

                if (!_spawnDone)
                {
                    _spawnDone = true;
                    _logger.LogError($"Worker() | worker process failed [pid:{ProcessId}]: {ex.Message}");
                    Emit("@failure", ex);
                }
                else
                {
                    // 执行到这里的可能性？
                    _logger.LogError($"Worker() | worker process error [pid:{ProcessId}]: {ex.Message}");
                    Emit("died", ex);
                }
            }

            _channel = new Channel(_loggerFactory.CreateLogger<Channel>(), _pipes[3], _pipes[4], ProcessId);
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
            Observer.Emit("close");
        }

        #region Request

        /// <summary>
        /// Dump Worker.
        /// </summary>
        public Task<string?> DumpAsync()
        {
            _logger.LogDebug("DumpAsync()");
            return _channel.RequestAsync(MethodId.WORKER_DUMP);
        }

        /// <summary>
        /// Get mediasoup-worker process resource usage.
        /// </summary>
        public Task<string?> GetResourceUsageAsync()
        {
            _logger.LogDebug("GetResourceUsageAsync()");
            return _channel.RequestAsync(MethodId.WORKER_GET_RESOURCE_USAGE);
        }

        /// <summary>
        /// Updates the worker settings in runtime. Just a subset of the worker settings can be updated.
        /// </summary>
        public Task<string?> UpdateSettingsAsync(WorkerUpdateableSettings workerUpdateableSettings)
        {
            _logger.LogDebug("UpdateSettingsAsync()");
            var reqData = new
            {
                LogLevel = workerUpdateableSettings.LogLevel.HasValue ? workerUpdateableSettings.LogLevel.GetEnumStringValue() : null,
                LogTags = workerUpdateableSettings.LogTags != null ? workerUpdateableSettings.LogTags.Select(m => m.GetEnumStringValue()) : null,
            };
            return _channel.RequestAsync(MethodId.WORKER_UPDATE_SETTINGS, null, reqData);
        }

        /// <summary>
        /// Create a Router.
        /// </summary>
        public async Task<Router> CreateRouter(RouterOptions routerOptions)
        {
            _logger.LogDebug("CreateRouter()");

            // This may throw.
            var rtpCapabilities = ORTC.GenerateRouterRtpCapabilities(routerOptions.MediaCodecs);

            var @internal = new { RouterId = Guid.NewGuid().ToString() };

            await _channel.RequestAsync(MethodId.WORKER_CREATE_ROUTER, @internal);

            var router = new Router(_loggerFactory, @internal.RouterId, rtpCapabilities, _channel, AppData);

            _routers.Add(router);

            router.On("@close", _ => _routers.Remove(router));

            // Emit observer event.
            Observer.Emit("newrouter", router);

            return router;
        }

        #endregion

        #region Event handles

        private void OnChannelRunning(string processId)
        {
            _channel.RunningEvent -= OnChannelRunning;
            if (!_spawnDone)
            {
                _spawnDone = true;
                _logger.LogDebug($"OnChannelRunning() | worker process running [pid:{processId}]");
                Emit("@success");
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
                    _logger.LogError($"OnExit() | worker process failed due to wrong settings [pid:{ProcessId}]");
                    Emit("@failure", new Exception("wrong settings"));
                }
                else
                {
                    _logger.LogError($"OnExit() | worker process failed unexpectedly [pid:{ProcessId}, code:{process.ExitCode}, signal:{process.TermSignal}]");
                    Emit("@failure", new Exception($"[pid:{ProcessId}, code:{ process.ExitCode}, signal:{ process.TermSignal}]"));

                }
            }
            else
            {
                _logger.LogError($"OnExit() | [pid:{ProcessId}] worker process died unexpectedly [pid:{ProcessId}, code:{process.ExitCode}, signal:{process.TermSignal}]");
                Emit("died", new Exception($"[pid:{ProcessId}, code:{ process.ExitCode}, signal:{ process.TermSignal}]"));
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
