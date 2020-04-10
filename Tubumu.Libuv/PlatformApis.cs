using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tubumu.Libuv
{
    public static class PlatformApis
    {
        public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool IsDarwin { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long VolatileRead(ref long value)
        {
            if (IntPtr.Size == 8)
            {
                return Volatile.Read(ref value);
            }
            else
            {
                // Avoid torn long reads on 32-bit
                return Interlocked.Read(ref value);
            }
        }
    }
}
