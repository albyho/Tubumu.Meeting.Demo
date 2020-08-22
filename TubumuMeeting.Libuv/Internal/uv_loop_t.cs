using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct uv_loop_t
    {
        public IntPtr data;
        public uint active_handles;
    }
}
