using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    public class Check : StartableCallbackHandle
    {
        [DllImport("libuv", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uv_check_init(IntPtr loop, IntPtr idle);

        [DllImport("libuv", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uv_check_start(IntPtr check, uv_handle_cb callback);

        [DllImport("libuv", CallingConvention = CallingConvention.Cdecl)]
        private static extern int uv_check_stop(IntPtr check);

        public Check()
            : this(Loop.Constructor)
        {
        }

        public Check(Loop loop)
            : base(loop, HandleType.UV_IDLE, uv_check_init)
        {
        }

        public override void Start()
        {
            Invoke(uv_check_start);
        }

        public override void Stop()
        {
            Invoke(uv_check_stop);
        }
    }
}
