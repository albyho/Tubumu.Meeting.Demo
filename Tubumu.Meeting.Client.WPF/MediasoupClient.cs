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

        public static class SignalR
        {
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Start([MarshalAs(UnmanagedType.LPStr)] string serverUrl);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void Stop();
        }

        public static class Device
        {
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool Load([MarshalAs(UnmanagedType.LPStr)] string routerRtpCapabilities);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool IsLoaded();

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr GetRtpCapabilities();

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr GetSctpCapabilities();
            
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool CreateSendTransport([MarshalAs(UnmanagedType.LPStr)] string args);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool CreateRecvTransport([MarshalAs(UnmanagedType.LPStr)] string args);
        }

        public static class SendTansport
        {
            /// <summary>
            /// Produce 
            /// </summary>
            /// <param name="mediaKind">video or audio</param>
            /// <param name="useSimulcast"></param>
            /// <returns></returns>
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool Produce([MarshalAs(UnmanagedType.LPStr)] string mediaKind, bool useSimulcast/* for video*/, [MarshalAs(UnmanagedType.LPStr)] string appData);
        }

        public static class RecvTansport
        {
            /// <summary>
            /// Consume
            /// </summary>
            /// <param name="args"></param>
            /// <param name="handle"></param>
            /// <returns></returns>
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool Consume([MarshalAs(UnmanagedType.LPStr)] string args, IntPtr handle);
        }
    }

    /// <summary>
    /// SignalR:ConnectWebRtcTransport
    /// </summary>
    /// <param name="value"></param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnTransportConnect(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnTransportConnectionStateChange(IntPtr value);

    /// <summary>
    /// SignalR:Produce
    /// </summary>
    /// <param name="value"></param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr OnSendTransportProduce(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnProducerTransportClose(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnConsumerTransportClose(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogging(IntPtr value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Callbacks
    {
        public OnTransportConnect OnTransportConnect;

        public OnTransportConnectionStateChange OnTransportConnectionStateChange;

        public OnSendTransportProduce OnSendTransportProduce;

        public OnProducerTransportClose OnProducerTransportClose;

        public OnConsumerTransportClose OnConsumerTransportClose;

        public OnLogging OnLogging;
    };
}
