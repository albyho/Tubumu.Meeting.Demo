using System;
using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
	[StructLayout(LayoutKind.Sequential)]
	struct uv_process_t
	{
		public IntPtr exit_cb;
		public int pid;
	}
}

