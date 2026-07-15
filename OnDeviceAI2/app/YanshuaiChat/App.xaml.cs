using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace YanshuaiChat
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
                LogCrash("Unhandled [" + e.Message + "]", e.Exception);
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
                (s, e) => { LogCrash("UnobservedTask", e.Exception); e.SetObserved(); };
            Suspending += OnSuspending;
        }

        public static void LogCrash(string tag, Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash.log");
                System.IO.File.AppendAllText(path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + tag + ": " +
                    (ex != null ? ex.ToString() : "(null)") + "\r\n\r\n");
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            if (!(Window.Current.Content is Frame rootFrame))
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += (s, a) =>
                    LogCrash("NavigationFailed to Page " + a.SourcePageType.FullName, a.Exception);
                Window.Current.Content = rootFrame;
            }
            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
