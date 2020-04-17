using System;

namespace TubumuMeeting.Libuv
{
    public interface IFileDescriptor
    {
        void Open(IntPtr socket);
        IntPtr FileDescriptor { get; }
    }
}

