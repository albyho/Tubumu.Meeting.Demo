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

namespace Tubumu.Meeting.Client.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HubConnection connection;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Test();
        }

        public async Task Test()
        {
            MediasoupClient.Initialize();

            var version = new StringBuilder();
            MediasoupClient.Version(version);
            Debug.WriteLine($"MediasoupClient version: {version}");

            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiOSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.3Hnnkoxe52L7joy99dXkcIjHtz9FUitf4BGYCYjyKdE";
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
            var isSuccessed = MediasoupClient.Load(routerRtpCapabilities);
            if (!isSuccessed)
            {
                throw new Exception();
            }

            // Device: Check is loaded.
            var isLoaded = MediasoupClient.Device.IsLoaded();
            Debug.WriteLine($"Device IsLoaded: {isLoaded}");
            if (!isLoaded)
            {
                throw new Exception();
            }

            // Device: GetRtpCapabilities
            var deviceRtpCapabilities = new StringBuilder(1024 * 10);
            isSuccessed = MediasoupClient.Device.GetRtpCapabilities(deviceRtpCapabilities);
            Debug.WriteLine($"Device RtpCapabilities: {deviceRtpCapabilities}");
            if (!isSuccessed)
            {
                throw new Exception();
            }
            
            // Device: GetSctpCapabilities
            var deviceSctpCapabilities = new StringBuilder(1024 * 10);
            isSuccessed = MediasoupClient.Device.GetSctpCapabilities(deviceSctpCapabilities);
            Debug.WriteLine($"Device SctpCapabilities: {deviceSctpCapabilities}");
            if (!isSuccessed)
            {
                throw new Exception();
            }

            // Join
            var joinResult = await connection.InvokeAsync<MeetingMessage>("Join", new JoinRequest
            {
                RtpCapabilities = ObjectExtensions.FromJson<RtpCapabilities>(deviceRtpCapabilities.ToString()),
                SctpCapabilities = null,
                DisplayName = null,
                Sources = new[] { "mic", "cam" },
                AppData = new Dictionary<string, object>(),
            });
            if(joinResult.Code != 200)
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

            // CreateWebRtcTransport: Consuming
            var createConsumingWebRtcTransportResult = await connection.InvokeAsync<MeetingMessage<CreateWebRtcTransportResult>>("CreateWebRtcTransport", new CreateWebRtcTransportRequest
            {
                ForceTcp = false,
                Consuming = false,
                Producing = true,
            });


        }

        private void Cleanup()
        {
            MediasoupClient.Cleanup();
        }

        private void ProcessNotification(object message)
        {
            Debug.WriteLine(message.ToString());
        }
    }
}
