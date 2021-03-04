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
using Tubumu.Mediasoup;
using Tubumu.Core.Extensions.Object;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Tubumu.Core.Extensions;
using System.Windows.Interop;

namespace Tubumu.Meeting.Client.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Callbacks callbacks;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

        }

        public void Initialize()
        {
            if (callbacks.OnLogging == null)
            {
                callbacks = new Callbacks
                {
                    OnLogging = OnLoggingHandle,
                    OnNotification = OnNotificationHandle,
                    OnConnectionStateChanged = OnConnectionStateChangedHandle,
                    OnNewVideoTrack = OnNewVideoTrackHandle,
                };
            }
            //MediasoupClient.Initialize("warn", ref callbacks);
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(callbacks)); // TODO: Marshal.FreeHGlobal(ptr);
            Marshal.StructureToPtr(callbacks, ptr, true);
            var result = MediasoupClient.Initialize("debug", "warn", "all", ptr);

            var versionPtr = MediasoupClient.Version();
            var version = Marshal.PtrToStringAnsi(versionPtr);
            //Marshal.FreeHGlobal(versionPtr);
            Debug.WriteLine($"MediasoupClient version: {version}");
        }

        private void Cleanup()
        {
            MediasoupClient.Cleanup();
        }

        private void StartPreviewLocalVideo()
        {
            MediasoupClient.StartPreviewLocalVideo(localVideoPanel.Handle);
        }

        private void StopPreviewLocalVideo()
        {
            MediasoupClient.StopPreviewLocalVideo();

        }

        private void Connect(string serverUrl)
        {
            var joinRequest = new JoinRequest
            {
                Sources = new[] { "audio:mic", "video:cam" },
                DisplayName = null,
                AppData = null,
            };
            var result = MediasoupClient.Connect(serverUrl, joinRequest.ToJson());
        }

        private void Disconnect()
        {
            MediasoupClient.Disconnect();
        }

        private void JoinRoom(string roomId)
        {
            MediasoupClient.Join(roomId);
        }

        private void LeaveRoom()
        {
            MediasoupClient.LeaveRoom();
        }

        private void Pull(PullRequest pullRequest)
        {
            // Example:
            /*
            pullRequest = new PullRequest
            {
                ProducerPeerId = 0,
                Sources = new[] { "audio:mic", "video:cam" },
            };
            */
            MediasoupClient.Pull(pullRequest.ToJson());
        }

        #region Callbacks

        public void OnLoggingHandle(IntPtr log)
        {
            var logString = Marshal.PtrToStringUTF8(log);
            Debug.WriteLine(logString);
        }

        public void OnNotificationHandle(IntPtr type, IntPtr content)
        {
            var typeString = Marshal.PtrToStringUTF8(type);
            var contentString = Marshal.PtrToStringUTF8(content);
            Debug.WriteLine($"OnNotificationHandle: {typeString}|{contentString}");
        }

        public void OnConnectionStateChangedHandle(ConnectionState from, ConnectionState to)
        {
            Debug.WriteLine($"OnConnectionStateChangedHandle: {from} -> {to}");
        }

        public IntPtr OnNewVideoTrackHandle(IntPtr args)
        {
            return IntPtr.Zero;
        }

        #endregion

        #region Event handlers

        private void InitializeButton_Click(object sender, RoutedEventArgs e)
        {
            if (InitializeButton.Content.ToString() == "Initialize")
            {
                Initialize();
                InitializeButton.Content = "Cleanup";
            }
            else
            {
                Cleanup();
                InitializeButton.Content = "Initialize";
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {

            if (ConnectButton.Content.ToString() == "Connect")
            {
                var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiOSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.3Hnnkoxe52L7joy99dXkcIjHtz9FUitf4BGYCYjyKdE";
                var serverUrl = $"{ServerUrlTextBox.Text}?access_token={accessToken}";
                Connect(serverUrl);
                ConnectButton.Content = "Disconnect";
            }
            else
            {
                Disconnect();
                ConnectButton.Content = "Connect";
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewButton.Content.ToString() == "Start Preview")
            {
                StartPreviewLocalVideo();
                PreviewButton.Content = "Stop Preview";
            }
            else
            {
                StopPreviewLocalVideo();
                PreviewButton.Content = "Start Preview";
            }
        }

        #endregion
    }
}
