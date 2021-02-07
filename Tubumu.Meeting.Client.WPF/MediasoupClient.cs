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
        public static extern void Initialize();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Cleanup();

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Version([MarshalAs(UnmanagedType.LPStr)] StringBuilder version);

        [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool Load(string routerRtpCapabilities);

        public static class Device
        {
            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool IsLoaded();

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool GetRtpCapabilities([MarshalAs(UnmanagedType.LPStr)] StringBuilder deviceRtpCapabilities);

            [DllImport(MediasoupClientWrapperDllName, CallingConvention = CallingConvention.Cdecl)]
            public static extern bool GetSctpCapabilities([MarshalAs(UnmanagedType.LPStr)] StringBuilder deviceSctpCapabilities);
        }
    }
}
