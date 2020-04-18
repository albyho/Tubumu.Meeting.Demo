#region snippet_MainWindowClass
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using TubumuMeeting.Mediasoup;

namespace TubumuMeeting.Demo.Client
{
    public partial class MainWindow : Window
    {
        HubConnection connection;
        public MainWindow()
        {
            InitializeComponent();

            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiMjkiLCJnIjoi5Yy76ZmiIiwibmJmIjoxNTg0MzQ5MDQ2LCJleHAiOjE1ODY5NDEwNDYsImlzcyI6Imlzc3VlciIsImF1ZCI6ImF1ZGllbmNlIn0._bGG1SOF9WqY8TIErRkxsh9_l_mFB_5JcGrKO1GyQ0E";
            connection = new HubConnectionBuilder()
                .WithUrl($"http://localhost:5000/hubs/meetingHub?roomid={Guid.NewGuid()}&access_token={accessToken}")
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
                JoinRoomButton.IsEnabled = true;
                GetRouterRtpCapabilitiesButton.IsEnabled = true;
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
                await connection.InvokeAsync("Join", new RtpCapabilities());
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await connection.InvokeAsync("JoinRoom", Guid.Empty);
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
                await connection.InvokeAsync("CreateWebRtcTransport");
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }
    }
}
#endregion
