using System.Runtime.InteropServices;

namespace TubumuMeeting.Libuv
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct uv_dirent_t
    {
        public sbyte* name;
        public UVDirectoryEntityType type;
    }
}
