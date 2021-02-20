using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.AspNetCore.SignalR.Client;
using Tubumu.Mediasoup;
using Tubumu.Core.Extensions.Object;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.Threading;
using System.Threading;
using Tubumu.Core.Extensions;

namespace Tubumu.Meeting.Client.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HubConnection connection;
        private Callbacks callbacks;
        //private JoinableTaskContext jtc = new JoinableTaskContext(null, new SynchronizationContext());
        private JoinableTaskFactory jtf = new JoinableTaskFactory(new JoinableTaskContext(null, new SynchronizationContext()));

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Run();
        }

        public async Task Run()
        {
            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiOSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.3Hnnkoxe52L7joy99dXkcIjHtz9FUitf4BGYCYjyKdE";

            callbacks = new Callbacks
            {
                OnTransportConnect = OnTransportConnectHandle,
                OnTransportConnectionStateChange = OnTransportConnectionStateChangeHandle,
                OnSendTransportProduce = OnSendTransportProduceHandle,
                OnProducerTransportClose = OnProducerTransportCloseHandle,
                OnConsumerTransportClose = OnConsumerTransportCloseHandle,
                OnLogging = OnLoggingHandle,
            };
            //MediasoupClient.Initialize("warn", ref callbacks);
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(callbacks)); // TODO: Marshal.FreeHGlobal(ptr);
            Marshal.StructureToPtr(callbacks, ptr, true);
            MediasoupClient.Initialize($"https://192.168.1.8:5001/hubs/meetingHub?access_token={accessToken}", "debug", "warn", ptr);

            var versionPtr = MediasoupClient.Version();
            var version = Marshal.PtrToStringAnsi(versionPtr);
            //Marshal.FreeHGlobal(versionPtr);
            Debug.WriteLine($"MediasoupClient version: {version}");

            return;
            connection = new HubConnectionBuilder()
                .AddJsonProtocol(options =>
                {
                    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumMemberConverter());
                })
                .WithUrl($"https://192.168.1.8:5001/hubs/meetingHub?access_token={accessToken}", options =>
                {
                    var handler = new HttpClientHandler
                    {
                        ClientCertificateOptions = ClientCertificateOption.Manual,
                        ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
                    };
                    options.HttpMessageHandlerFactory = _ => handler;
                    options.WebSocketConfiguration = sockets =>
                    {
                        sockets.RemoteCertificateValidationCallback = (sender, certificate, chain, policyErrors) => true;
                    };
                })
                .Build();

            connection.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await connection.StartAsync();
            };

            connection.On<object>("Notify", message =>
            {
                ProcessNotification(message);
            });

            await connection.StartAsync();

            // GetRouterRtpCapabilities
            var getRouterRtpCapabilitiesResult = await connection.InvokeAsync<MeetingMessage<RtpCapabilities>>("GetRouterRtpCapabilities");
            if (getRouterRtpCapabilitiesResult.Code != 200)
            {
                throw new Exception();
            }
            var routerRtpCapabilities = getRouterRtpCapabilitiesResult.Data.ToJson();

            // Device: Load
            var isSuccessed = MediasoupClient.Device.Load(routerRtpCapabilities);
            if (!isSuccessed)
            {
                throw new Exception();
            }

            // Device: Check is loaded.
            var isLoaded = MediasoupClient.Device.IsLoaded();
            Debug.WriteLine($"IsLoaded: {isLoaded}", "Device");
            if (!isLoaded)
            {
                throw new Exception();
            }

            // Device: GetRtpCapabilities
            var deviceRtpCapabilitiesPtr = MediasoupClient.Device.GetRtpCapabilities();
            var deviceRtpCapabilities = Marshal.PtrToStringAnsi(deviceRtpCapabilitiesPtr);
            //Marshal.FreeHGlobal(deviceRtpCapabilitiesPtr);
            Debug.WriteLine($"deviceRtpCapabilities: {deviceRtpCapabilities}");

            // Device: GetSctpCapabilities
            var deviceSctpCapabilitiesPtr = MediasoupClient.Device.GetSctpCapabilities();
            var deviceSctpCapabilities = Marshal.PtrToStringAnsi(deviceSctpCapabilitiesPtr);
           // Marshal.FreeHGlobal(deviceSctpCapabilitiesPtr);
            Debug.WriteLine($"deviceRtpCapabilities: {deviceSctpCapabilities}");

            // Join
            var joinResult = await connection.InvokeAsync<MeetingMessage>("Join", new JoinRequest
            {
                RtpCapabilities = ObjectExtensions.FromJson<RtpCapabilities>(deviceRtpCapabilities.ToString()),
                SctpCapabilities = null, // DataChannel
                DisplayName = null,
                Sources = new[] { "mic", "cam" },
                AppData = new Dictionary<string, object>(),
            });
            if (joinResult.Code != 200)
            {
                throw new Exception();
            }

            // JoinRoom
            var joinRoomResult = await connection.InvokeAsync<MeetingMessage>("JoinRoom", new JoinRoomRequest
            {
                RoomId = "0",
            });
            if (joinRoomResult.Code != 200)
            {
                throw new Exception();
            }

            // CreateWebRtcTransport: Producing
            var createProducingWebRtcTransportResult = await connection.InvokeAsync<MeetingMessage<CreateWebRtcTransportResult>>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
            {
                ForceTcp = false,
                Consuming = true,
                Producing = false,
            });
            Debug.WriteLine($"CreateWebRtcTransportResult: {createProducingWebRtcTransportResult.Data.ToJson()}", "SignalR");

            // CreateWebRtcTransport: Consuming
            var createConsumingWebRtcTransportResult = await connection.InvokeAsync<MeetingMessage<CreateWebRtcTransportResult>>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
            {
                ForceTcp = false,
                Consuming = false,
                Producing = true,
            });
            Debug.WriteLine($"CreateWebRtcTransportResult: {createConsumingWebRtcTransportResult.Data.ToJson()}", "SignalR");

            isSuccessed = MediasoupClient.Device.CreateSendTransport(createProducingWebRtcTransportResult.Data.ToJson());
            Debug.WriteLine($"CreateSendTransport: {isSuccessed}", "Device");
            if (!isSuccessed)
            {
                throw new Exception();
            }

            isSuccessed = MediasoupClient.SendTansport.Produce("video", false, new Dictionary<string, object> {
                { "source", "cam" }
            }.ToJson());
            Debug.WriteLine($"Produce: {isSuccessed}", "Device");
            if (!isSuccessed)
            {
                throw new Exception();
            }
        }

        private void Cleanup()
        {
            MediasoupClient.Cleanup();
        }

        private void ProcessNotification(object message)
        {
            Debug.WriteLine(message.ToString(), "Notification");
        }

        #region Callbacks

        public void OnTransportConnectHandle(IntPtr value)
        {
            var json = Marshal.PtrToStringAnsi(value);
            Debug.WriteLine(json, "Callback: OnTransportConnectHandle");

            var connectWebRtcTransportRequest = ObjectExtensions.FromJson<ConnectWebRtcTransportRequest>(json);
            //Marshal.FreeHGlobal(value);

            connection.InvokeAsync<MeetingMessage>("ConnectWebRtcTransport", connectWebRtcTransportRequest).NoWarning();
            //var t = await connection.InvokeAsync<MeetingMessage>("ConnectWebRtcTransport", connectWebRtcTransportRequest);
            return;

            var result = jtf.Run(async delegate
            {
                return await connection.InvokeAsync<MeetingMessage<RtpCapabilities>>("GetRouterRtpCapabilities");
            });

            //var produceRespose = connection.InvokeAsync<MeetingMessage<ProduceRespose>>("Produce", new ProduceRequest()).ConfigureAwait(false).GetAwaiter().GetResult();

            var task = connection.InvokeAsync<MeetingMessage>("ConnectWebRtcTransport", connectWebRtcTransportRequest);
            task.ContinueWith(val =>
              {
                  val.Exception.Handle(ex =>
                  {
                      Debug.WriteLine(ex.Message, "TaskException");
                      return true;
                  });
              }, TaskContinuationOptions.OnlyOnFaulted);
            task.ContinueWith(val =>
            {
                var r = val.Result;
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public void OnTransportConnectionStateChangeHandle(IntPtr value)
        {
            //var json = Marshal.PtrToStringAnsi(value);
            //Debug.WriteLine(json, "OnTransportConnectionStateChangeHandle");

            //Marshal.FreeHGlobal(value);
        }

        public IntPtr OnSendTransportProduceHandle(IntPtr value)
        {
            //return Marshal.StringToHGlobalAnsi("123"); 
            var json = Marshal.PtrToStringAnsi(value);
            Debug.WriteLine(json, "Callback: OnSendTransportProduceHandle");

            var produceRequest = ObjectExtensions.FromJson<ProduceRequest>(json);
            //Marshal.FreeHGlobal(value);

            var produceRespose = connection.InvokeAsync<MeetingMessage<ProduceRespose>>("Produce", produceRequest).ConfigureAwait(false).GetAwaiter().GetResult();
            Debug.WriteLine(produceRespose.ToJson(), "Callback: OnSendTransportProduceHandle");

            IntPtr ptr = Marshal.StringToHGlobalAnsi(produceRespose.Data.Id);
            //IntPtr ptr = Marshal.StringToHGlobalAnsi("123");
            return ptr;
        }

        public void OnProducerTransportCloseHandle(IntPtr value)
        {
            var json = Marshal.PtrToStringAnsi(value);
            Debug.WriteLine(json, "Callback: OnProducerTransportCloseHandle");

            //Marshal.FreeHGlobal(value);
        }

        public void OnConsumerTransportCloseHandle(IntPtr value)
        {
            var json = Marshal.PtrToStringAnsi(value);
            Debug.WriteLine(json, "Callback: OnConsumerTransportCloseHandle");

            //Marshal.FreeHGlobal(value);
        }

        public void OnLoggingHandle(IntPtr value)
        {
            var log = Marshal.PtrToStringAnsi(value);
            Debug.WriteLine(log);
        }

        #endregion
    }
}
