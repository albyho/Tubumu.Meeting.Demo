using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    public class LoopBackend
    {
        IntPtr nativeHandle;

        internal LoopBackend(Loop loop)
        {
            nativeHandle = loop.NativeHandle;
        }

        [DllImport("libuv", CallingConvention = CallingConvention.Cdecl)]
        static extern int uv_backend_fd(IntPtr loop);

        public int FileDescriptor
        {
            get
            {
                return uv_backend_fd(nativeHandle);
            }
        }

        [DllImport("libuv", CallingConvention = CallingConvention.Cdecl)]
        static extern int uv_backend_timeout(IntPtr loop);

        public int Timeout
        {
            get
            {
                return uv_backend_timeout(nativeHandle);
            }
        }
    }
}

