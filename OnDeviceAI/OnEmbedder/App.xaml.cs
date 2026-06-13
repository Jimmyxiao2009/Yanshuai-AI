using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Windows.Storage;
// OnEmbedder 类在 yanshuai 命名空间下，自动可用

namespace yanshuai
{
    public sealed partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.UnhandledException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Unhandled: " + e.Exception);
                e.Handled = false;
            };
        }

        // 全局嵌入引擎实例
        internal static OnEmbedder Embedder { get; private set; }

        private async System.Threading.Tasks.Task InitOnDeviceAI()
        {
            Embedder = new OnEmbedder();
            if (!Embedder.Initialize())
            {
                System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 初始化失败");
                return;
            }

            try
            {
                var file = await Package.Current.InstalledLocation.GetFileAsync(@"Assets\bge.embmodel");
                if (Embedder.LoadModel(file.Path))
                    System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 就绪 ✅");
                else
                    System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 模型加载失败");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnDeviceAI] 加载异常: {ex.Message}");
            }
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            await DataManager.LoadAsync();
            await InitOnDeviceAI();

            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame { CacheSize = 1 };
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += RootFrame_FirstNavigated;
                // 未完成OOBE则进引导流程
                var startPage = AppSettings.OobeCompleted ? typeof(MainPage) : typeof(OobePage1);
                if (!rootFrame.Navigate(startPage, e.Arguments))
                    throw new Exception("Failed to create initial page");
            }

            HardwareButtons.BackPressed += (s, args) =>
            {
                Frame frame = Window.Current.Content as Frame;
                if (frame != null && frame.CanGoBack)
                {
                    args.Handled = true;
                    frame.GoBack();
                }
            };

            Window.Current.Activate();
        }

        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            rootFrame.ContentTransitions = new TransitionCollection
            {
                new NavigationThemeTransition
                {
                    DefaultNavigationTransitionInfo = new SlideNavigationTransitionInfo()
                }
            };
            rootFrame.Navigated -= RootFrame_FirstNavigated;
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await DataManager.SaveAsync();
            deferral.Complete();
        }
    }
}
