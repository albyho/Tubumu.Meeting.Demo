using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Tubumu.Meeting.Client.WPF
{
    public static class MediasoupClient
    {
#if DEBUG
        private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\Tubumu.Meeting.Group\Tubumu.Meeting.Demo\x64\Debug\MediasoupClientWrapper.dll";
        //private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\Tubumu.Meeting.Group\Tubumu.Meeting.Demo\x64\Release\MediasoupClientWrapper.dll";
#else
        //private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\Tubumu.Meeting.Group\Tubumu.Meeting.Demo\x64\Release\MediasoupClientWrapper.dll";
        private const string MediasoupClientWrapperDllName = "runtimes/win/native/MediasoupClientWrapper.dll";
#endif

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        //public static extern void Initialize([MarshalAs(UnmanagedType.LPStr)] string serverUrl, [MarshalAs(UnmanagedType.LPStr)] string mediasoupClientLogLevel, [MarshalAs(UnmanagedType.LPStr)] string rtcLogLevel, ref Callbacks callbacks);
        public static extern void Initialize([MarshalAs(UnmanagedType.LPStr)] string mediasoupClientLogLevel, 
            [MarshalAs(UnmanagedType.LPStr)] string rtcLogLevel,
            [MarshalAs(UnmanagedType.LPStr)] string signalRLogLevel,
            IntPtr callbacks);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Cleanup();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Version();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Start([MarshalAs(UnmanagedType.LPStr)] string serverUrl);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Stop();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Join([MarshalAs(UnmanagedType.LPStr)] string args);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JoinRoom([MarshalAs(UnmanagedType.LPStr)] string roomId);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LeaveRoom();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Pull([MarshalAs(UnmanagedType.LPStr)] string args);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogging(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnMessage(IntPtr value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Callbacks
    {
        public OnLogging OnLogging;

        public OnLogging OnMessage;
    };
}
