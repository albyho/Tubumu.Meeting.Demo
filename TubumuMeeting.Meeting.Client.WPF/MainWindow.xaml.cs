using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Client.WPF
{

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CALLBACKCONNECTSERVER2(IntPtr param1, IntPtr param2);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CALLBACKCONNECTSERVER4(IntPtr param1, IntPtr param2, IntPtr param3, IntPtr param4);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CALLBACKJOINROOM(IntPtr param1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CALLBACKNEWCONSUMERREADY(IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CallBackFunStruct
    {
        public CALLBACKCONNECTSERVER2 FunCallBackConnectServer2;
        public CALLBACKCONNECTSERVER4 FunCallBackConnectServer4;
        public CALLBACKJOINROOM FunCallBackJoinRoom;
        public CALLBACKNEWCONSUMERREADY FunCallBackNewConsumerReady;
    };

    internal static class RtclientLib
    {
        private const string LibRTClientDllName = "runtimes/win/native/librtclient.dll";

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitializeRTC(IntPtr ptrCallBackFunc);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnInitRTC();

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void LoadRtpCapabilities(IntPtr loaddata);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CreateSendTransport(IntPtr param);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CreateRecvTransport(IntPtr param);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void JoinRoom(IntPtr wnd);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ConsumeStream(IntPtr param, IntPtr wnd);

        [DllImport(LibRTClientDllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Destroy();

    }

    public class User32API
    {
        private static Hashtable processWnd = null;

        public delegate bool WNDENUMPROC(IntPtr hwnd, uint lParam);

        static User32API()
        {
            if (processWnd == null)
            {
                processWnd = new Hashtable();
            }
        }

        [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
        public static extern bool EnumWindows(WNDENUMPROC lpEnumFunc, uint lParam);

        [DllImport("user32.dll", EntryPoint = "GetParent", SetLastError = true)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, ref uint lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        public static extern void SetLastError(uint dwErrCode);

        public static IntPtr GetCurrentWindowHandle()
        {
            IntPtr ptrWnd = IntPtr.Zero;
            uint uiPid = (uint)Process.GetCurrentProcess().Id;  // 当前进程 ID
            object objWnd = processWnd[uiPid];

            if (objWnd != null)
            {
                ptrWnd = (IntPtr)objWnd;
                if (ptrWnd != IntPtr.Zero && IsWindow(ptrWnd))  // 从缓存中获取句柄
                {
                    return ptrWnd;
                }
                else
                {
                    ptrWnd = IntPtr.Zero;
                }
            }

            bool bResult = EnumWindows(new WNDENUMPROC(EnumWindowsProc), uiPid);
            // 枚举窗口返回 false 并且没有错误号时表明获取成功
            if (!bResult && Marshal.GetLastWin32Error() == 0)
            {
                objWnd = processWnd[uiPid];
                if (objWnd != null)
                {
                    ptrWnd = (IntPtr)objWnd;
                }
            }

            return ptrWnd;
        }

        private static bool EnumWindowsProc(IntPtr hwnd, uint lParam)
        {
            uint uiPid = 0;

            if (GetParent(hwnd) == IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, ref uiPid);
                if (uiPid == lParam)    // 找到进程对应的主窗口句柄
                {
                    processWnd[uiPid] = hwnd;   // 把句柄缓存起来
                    SetLastError(0);    // 设置无错误
                    return false;   // 返回 false 以终止枚举窗口
                }
            }

            return true;
        }
    }

    public partial class MainWindow : Window
    {
        private readonly HubConnection connection;
        private readonly string transportId;
        private static CallBackFunStruct stCallBackFunStruct;

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            RtclientLib.Destroy();
            base.OnClosed(e);
        }

        private async void CallBackConnectServer2(IntPtr param1, IntPtr param2)
        {
            try
            {
                string dtls = Marshal.PtrToStringAnsi(param2);
                DtlsParameters dtlsParameters = JsonConvert.DeserializeObject<DtlsParameters>(dtls);

                ConnectWebRtcTransportRequest request = new ConnectWebRtcTransportRequest();
                request.TransportId = Marshal.PtrToStringAnsi(param1);
                request.DtlsParameters = dtlsParameters;
                var result = await connection.InvokeAsync<dynamic>("ConnectWebRtcTransport", request);
                int i = 0;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }

        }

        private async void CallBackConnectServer4(IntPtr param1, IntPtr param2, IntPtr param3, IntPtr param4)
        {
            try
            {
                string rtps = Marshal.PtrToStringAnsi(param3);
                RtpParameters rtpParameters = JsonConvert.DeserializeObject<RtpParameters>(rtps);

                string strappdata = Marshal.PtrToStringAnsi(param4);
                Dictionary<string, object> appdata = JsonConvert.DeserializeObject<Dictionary<string, object>>(strappdata);

                ProduceRequest request = new ProduceRequest();
                //request.TransportId = Marshal.PtrToStringAnsi(param1);
                request.Kind = (Marshal.PtrToStringAnsi(param2) == "audio") ? MediaKind.Audio : MediaKind.Video;
                request.RtpParameters = rtpParameters;
                request.AppData = appdata;

                var result = await connection.InvokeAsync<dynamic>("Produce", request);
                int i = 0;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void CallBackJoinRoom(IntPtr param1)
        {
            try
            {
                string rtpcaps = Marshal.PtrToStringAnsi(param1);
                RtpCapabilities rtpCapabilities = JsonConvert.DeserializeObject<RtpCapabilities>(rtpcaps);
                JoinRequest request = new JoinRequest();
                request.RtpCapabilities = rtpCapabilities;
                request.SctpCapabilities = null;
                // rtpCapabilities 参数：Device.Load 后，取其 RtpCapabilities 属性。
                var result = await connection.InvokeAsync<dynamic>("Join", request);
                int i = 0;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void CallBackNewConsumerReady(IntPtr param1, IntPtr param2)
        {
            try
            {
                string id = Marshal.PtrToStringAnsi(param1);
                string peerid = Marshal.PtrToStringAnsi(param2);
                var request = new NewConsumerReturnRequest();
                request.ConsumerId = id;
                request.PeerId = peerid;
                var result = await connection.InvokeAsync<dynamic>("NewConsumerReturn", request);
                int i = 0;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMjkiLCJnIjoi5Yy76ZmiIiwibmJmIjoxNTg0MzQ5MDQ2LCJleHAiOjE1ODY5NDEwNDYsImlzcyI6Imlzc3VlciIsImF1ZCI6ImF1ZGllbmNlIn0._bGG1SOF9WqY8TIErRkxsh9_l_mFB_5JcGrKO1GyQ0E";
            connection = new HubConnectionBuilder()
                .WithUrl($"https://192.168.0.124:5001/hubs/meetingHub?access_token={accessToken}")
                .Build();

            connection.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await connection.StartAsync();
            };

            //
            stCallBackFunStruct = new CallBackFunStruct
            {
                FunCallBackConnectServer2 = CallBackConnectServer2,
                FunCallBackConnectServer4 = CallBackConnectServer4,
                FunCallBackJoinRoom = CallBackJoinRoom,
                FunCallBackNewConsumerReady = CallBackNewConsumerReady
            };
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(stCallBackFunStruct));
            Marshal.StructureToPtr(stCallBackFunStruct, ptr, true);
            RtclientLib.InitializeRTC(ptr);
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            connection.On<object>("ReceiveMessage", message =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{message}";
                    messagesList.Items.Add(newMessage);
                });
                ProcessRecvMessage(message);
            });

            connection.On<object>("PeerHandled", message =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{message}";
                    messagesList.Items.Add(newMessage);
                });
                ProcessPeerHandled(message);
            });

            connection.On<object>("NewConsumer", message =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{message}";
                    messagesList.Items.Add(newMessage);
                });
                ProcessNewConsumer(message);
            });

            System.Net.ServicePointManager.ServerCertificateValidationCallback = Callback;

            try
            {
                await connection.StartAsync();
                messagesList.Items.Add("Connection started");
                ConnectButton.IsEnabled = false;
                //JoinButton.IsEnabled = true;
                //EnterRoomButton.IsEnabled = true;
                //GetRouterRtpCapabilitiesButton.IsEnabled = true;
                //CreateWebRtcTransportButton.IsEnabled = true;
                //ConnectWebRtcTransportButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void ProcessRecvMessage(object message)
        {
            int i = 0;
            //switch (interval)
            //{

            //}
        }

        private async void ProcessNewConsumer(object message)
        {
            var param = Marshal.StringToHGlobalAnsi(message.ToString());
            RtclientLib.ConsumeStream(param, new WindowInteropHelper(this).Handle);
        }

        private async void ProcessPeerHandled(object message)
        {
            try
            {
                var result = await connection.InvokeAsync<dynamic>("EnterRoom", Guid.Empty);
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{result}";
                    messagesList.Items.Add(newMessage);
                });
                result = await connection.InvokeAsync<dynamic>("GetRouterRtpCapabilities");
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{result}";
                    messagesList.Items.Add(newMessage);
                });
                //load routertp
                IntPtr param = Marshal.StringToHGlobalUni(result.ToString());
                RtclientLib.LoadRtpCapabilities(param);

                //
                result = await connection.InvokeAsync<dynamic>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = false,
                    Producing = true,
                });
                param = Marshal.StringToHGlobalAnsi(result.ToString());
                RtclientLib.CreateSendTransport(param);

                //connectwebrtctransport


                //Marshal.FreeHGlobal(param);
                result = await connection.InvokeAsync<dynamic>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = true,
                    Producing = false,
                });
                param = Marshal.StringToHGlobalAnsi(result.ToString());
                RtclientLib.CreateRecvTransport(param);
                RtclientLib.JoinRoom(new WindowInteropHelper(this).Handle);


            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private static bool Callback(object sender, X509Certificate certificate, X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private async void JoinButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // rtpCapabilities 参数：Device.Load 后，取其 RtpCapabilities 属性。
                await connection.InvokeAsync("Join", new RtpCapabilities());
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void EnterRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await connection.InvokeAsync<dynamic>("EnterRoom", Guid.Empty);
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{result}";
                    messagesList.Items.Add(newMessage);
                });
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void GetRouterRtpCapabilitiesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await connection.InvokeAsync<dynamic>("GetRouterRtpCapabilities");
                IntPtr param = Marshal.StringToHGlobalAnsi(result.ToString());
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{result}";
                    messagesList.Items.Add(newMessage);
                });
                RtclientLib.LoadRtpCapabilities(param);
                //Marshal.FreeHGlobal(param);
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void CreateWebRtcTransportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await connection.InvokeAsync<dynamic>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = true,
                    Producing = false,
                });
                IntPtr param = Marshal.StringToHGlobalAnsi(result.ToString());

                RtclientLib.CreateSendTransport(param);
                //Marshal.FreeHGlobal(param);
                result = await connection.InvokeAsync<dynamic>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = false,
                    Producing = true,
                });
                RtclientLib.CreateRecvTransport(param);
                //RtclientLib.JoinRoom();
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }

        private async void ConnectWebRtcTransportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConnectWebRtcTransportRequest request = new ConnectWebRtcTransportRequest();
                //request.TransportId = ;
                //request.DtlsParameters = ;

                await connection.InvokeAsync("ConnectWebRtcTransport", request);
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.ToString());
            }
        }
    }
}
