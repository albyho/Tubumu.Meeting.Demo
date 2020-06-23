using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Meeting.Client.WPF
{
    public partial class MainWindow : Window
    {
        private readonly HubConnection connection;
        private readonly string transportId;

        public MainWindow()
        {
            InitializeComponent();

            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMjkiLCJnIjoi5Yy76ZmiIiwibmJmIjoxNTg0MzQ5MDQ2LCJleHAiOjE1ODY5NDEwNDYsImlzcyI6Imlzc3VlciIsImF1ZCI6ImF1ZGllbmNlIn0._bGG1SOF9WqY8TIErRkxsh9_l_mFB_5JcGrKO1GyQ0E";
            connection = new HubConnectionBuilder()
                .WithUrl($"http://localhost:5000/hubs/meetingHub?access_token={accessToken}")
                .Build();

            connection.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await connection.StartAsync();
            };
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
            });

            try
            {
                await connection.StartAsync();
                messagesList.Items.Add("Connection started");
                ConnectButton.IsEnabled = false;
                JoinButton.IsEnabled = true;
                EnterRoomButton.IsEnabled = true;
                GetRouterRtpCapabilitiesButton.IsEnabled = true;
                CreateWebRtcTransportButton.IsEnabled = true;
                ConnectWebRtcTransportButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
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
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void EnterRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await connection.InvokeAsync("EnterRoom", Guid.Empty);
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void GetRouterRtpCapabilitiesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await connection.InvokeAsync("GetRouterRtpCapabilities");
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void CreateWebRtcTransportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await connection.InvokeAsync("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = false,
                    Producing = true,
                });
                await connection.InvokeAsync("CreateWebRtcTransport", new CreateWebRtcTransportRequest
                {
                    ForceTcp = false,
                    Consuming = true,
                    Producing = false,
                });
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void ConnectWebRtcTransportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await connection.InvokeAsync("ConnectWebRtcTransport", new ConnectWebRtcTransportRequest());
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }
    }
}
