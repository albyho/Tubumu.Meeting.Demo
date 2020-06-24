using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    [StructLayout(LayoutKind.Sequential)]
    struct uv_handle_t
    {
        // public
        public IntPtr data;
        // read only
        public IntPtr loop;
        public HandleType type;
        // private
        public IntPtr close_cb;
        // TODO: implement nginx queue
    }
}
