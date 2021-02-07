using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Tubumu.Meeting.Client.WPF
{
    public static class MediasoupClient
    {
        private const string MediasoupClientWrapperDllName = @"C:\Developer\OpenSource\Meeting\libmediasoupclient\out\Release\MediasoupClientWrapper.dll";
        //private const string MediasoupClientWrapperDllName = "runtimes/win/native/MediasoupClientWrapper.dll";

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Initialize(string webrtcDebug);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Cleanup();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Version([MarshalAs(UnmanagedType.LPStr)] StringBuilder version);

        public static class Device
        {
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool Load(string routerRtpCapabilities);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool IsLoaded();

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool GetRtpCapabilities([MarshalAs(UnmanagedType.LPStr)] StringBuilder deviceRtpCapabilities);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool GetSctpCapabilities([MarshalAs(UnmanagedType.LPStr)] StringBuilder deviceSctpCapabilities);
            
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool CreateSendTransport(string args);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool CreateRecvTransport(string args);
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
            public static extern bool Produce(string mediaKind, bool useSimulcast/* for video*/);
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
            public static extern bool Consume(string args, IntPtr handle);
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
    public delegate void OnSendTransportProduce(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnProducerTransportClose(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnConsumerTransportClose(IntPtr value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Callbacks
    {
        public OnTransportConnect OnTransportConnect;
        public OnTransportConnectionStateChange OnTransportConnectionStateChange;
        public OnSendTransportProduce OnSendTransportProduce;
        public OnProducerTransportClose OnProducerTransportClose;
        public OnConsumerTransportClose OnConsumerTransportClose;
    };
}
