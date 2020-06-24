using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    [StructLayout(LayoutKind.Sequential)]
    struct uv_work_t
    {
        public IntPtr loop;
        public IntPtr work_cb;
        public IntPtr work_after_cb;
    }
}
