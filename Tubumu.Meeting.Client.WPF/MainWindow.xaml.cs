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
            Initialize();
            Start();
        }

        public void Initialize()
        {
            if (callbacks.OnLogging == null)
            {
                callbacks = new Callbacks
                {
                    OnLogging = OnLoggingHandle,
                    OnMessage = OnMessageHandle,
                    OnNotification = OnNotificationHandle,
                };
            }
            //MediasoupClient.Initialize("warn", ref callbacks);
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(callbacks)); // TODO: Marshal.FreeHGlobal(ptr);
            Marshal.StructureToPtr(callbacks, ptr, true);
            MediasoupClient.Initialize("debug", "warn", "all", ptr, new WindowInteropHelper(this).Handle);

            var versionPtr = MediasoupClient.Version();
            var version = Marshal.PtrToStringAnsi(versionPtr);
            //Marshal.FreeHGlobal(versionPtr);
            Debug.WriteLine($"MediasoupClient version: {version}");
        }

        private void Cleanup()
        {
            MediasoupClient.Cleanup();
        }

        private void Start()
        {
            var accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiOSIsIm5iZiI6MTU4NDM0OTA0NiwiZXhwIjoxNTg2OTQxMDQ2LCJpc3MiOiJpc3N1ZXIiLCJhdWQiOiJhdWRpZW5jZSJ9.3Hnnkoxe52L7joy99dXkcIjHtz9FUitf4BGYCYjyKdE";
            MediasoupClient.Start($"http://192.168.1.8:5000/hubs/meetingHub?access_token={accessToken}");
        }

        private void Stop()
        {
            MediasoupClient.Stop();
        }

        #region Callbacks

        public void OnLoggingHandle(IntPtr value)
        {
            var log = Marshal.PtrToStringUTF8(value);
            Debug.WriteLine(log);
        }

        public void OnMessageHandle(IntPtr value)
        {
            var message = Marshal.PtrToStringUTF8(value);
            Debug.WriteLine(message);
        }

        public void OnNotificationHandle(IntPtr type, IntPtr value)
        {
            var typeString = Marshal.PtrToStringUTF8(type);
            var valueString = Marshal.PtrToStringUTF8(value);
            Debug.WriteLine($"Notification: {typeString}|{valueString}");
        }

        #endregion
    }
}
