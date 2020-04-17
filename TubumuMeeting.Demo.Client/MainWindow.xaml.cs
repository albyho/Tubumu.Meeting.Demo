#region snippet_MainWindowClass
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;

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

            #region snippet_ClosedRestart
            connection.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await connection.StartAsync();
            };
            #endregion
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            #region snippet_ConnectionOn
            connection.On<object>("ReceiveMessage", message =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var newMessage = $"{message}";
                    messagesList.Items.Add(newMessage);
                });
            });
            #endregion

            try
            {
                await connection.StartAsync();
                messagesList.Items.Add("Connection started");
                connectButton.IsEnabled = false;
                sendButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
        }

        private async void sendButton_Click(object sender, RoutedEventArgs e)
        {
            #region snippet_ErrorHandling
            try
            {
                #region snippet_InvokeAsync
                await connection.InvokeAsync("JoinRoom", Guid.Empty);
                #endregion
            }
            catch (Exception ex)
            {
                messagesList.Items.Add(ex.Message);
            }
            #endregion
        }
    }
}
#endregion
