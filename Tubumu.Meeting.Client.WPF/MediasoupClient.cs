using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Tubumu.Meeting.Client.WPF
{
    public enum ConnectionState
    {
        Connecting,
        Connected,
        Disconnecting,
        Disconnected
    };

    public enum ErrorCode
    {
        Success = 0,
        Uninitialized = -1000,
        Unconnected = -1001,
        DataError = -1002
    };

    public static class MediasoupClient
    {
#if DEBUG
        private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\Tubumu.Meeting.Group\Tubumu.Meeting.Demo\x64\Debug\MediasoupClientWrapper.dll";
#else
        //private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\Tubumu.Meeting.Group\Tubumu.Meeting.Demo\x64\Release\MediasoupClientWrapper.dll";
        private const string MediasoupClientWrapperDllName = "runtimes/win/native/MediasoupClientWrapper.dll";
#endif

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        //public static extern void Initialize([MarshalAs(UnmanagedType.LPStr)] string serverUrl, [MarshalAs(UnmanagedType.LPStr)] string mediasoupClientLogLevel, [MarshalAs(UnmanagedType.LPStr)] string rtcLogLevel, ref Callbacks callbacks);
        public static extern ErrorCode Initialize([MarshalAs(UnmanagedType.LPStr)] string mediasoupClientLogLevel,
            [MarshalAs(UnmanagedType.LPStr)] string rtcLogLevel,
            [MarshalAs(UnmanagedType.LPStr)] string signalRLogLevel,
            IntPtr callbacks);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode Cleanup();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Version();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode StartPreviewLocalVideo(IntPtr handle);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode StopPreviewLocalVideo();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode Connect([MarshalAs(UnmanagedType.LPStr)] string serverUrl, [MarshalAs(UnmanagedType.LPStr)] string joinArguments);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode Disconnect();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode Join([MarshalAs(UnmanagedType.LPStr)] string args);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode JoinRoom([MarshalAs(UnmanagedType.LPStr)] string roomId);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode LeaveRoom();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ErrorCode Pull([MarshalAs(UnmanagedType.LPStr)] string args);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogging(IntPtr log);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnNotification(IntPtr type, IntPtr content);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnConnectionStateChanged(ConnectionState from, ConnectionState to);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr OnNewVideoTrack(IntPtr args);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Callbacks
    {
        public OnLogging OnLogging;

        public OnNotification OnNotification;

        public OnConnectionStateChanged OnConnectionStateChanged;

        public OnNewVideoTrack OnNewVideoTrack;
    };
}
