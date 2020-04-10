using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Tubumu.Libuv
{
    public abstract class UvHandle : UvMemory
    {
        private static readonly LibuvFunctions.uv_close_cb _destroyMemory = (handle) => DestroyMemory(handle);
        private Action<Action<IntPtr>, IntPtr> _queueCloseHandle;

        protected UvHandle(ILogger logger) : base (logger)
        {
        }

        protected void CreateHandle(
            LibuvFunctions uv,
            int threadId,
            int size,
            Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            _queueCloseHandle = queueCloseHandle;
            CreateMemory(uv, threadId, size);
        }

        protected override bool ReleaseHandle()
        {
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                handle = IntPtr.Zero;

                if (Thread.CurrentThread.ManagedThreadId == ThreadId)
                {
                    _uv.close(memory, _destroyMemory);
                }
                else if (_queueCloseHandle != null)
                {
                    // This can be called from the finalizer.
                    // Ensure the closure doesn't reference "this".
                    var uv = _uv;
                    _queueCloseHandle(memory2 => uv.close(memory2, _destroyMemory), memory);
                }
                else
                {
                    Debug.Assert(false, "UvHandle not initialized with queueCloseHandle action");
                    return false;
                }
            }
            return true;
        }

        public void Reference()
        {
            _uv.@ref(this);
        }

        public void Unreference()
        {
            _uv.unref(this);
        }
    }
}
