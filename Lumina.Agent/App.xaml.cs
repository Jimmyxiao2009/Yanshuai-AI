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

        internal static OnEmbedder Embedder { get; private set; }

        /// <summary>延迟初始化嵌入引擎（后台，不阻塞启动）</summary>
        private async void InitOnDeviceAI()
        {
            await System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    Embedder = new OnEmbedder();
                    if (!Embedder.Initialize())
                    {
                        System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 初始化失败");
                        return;
                    }
                    var file = await Package.Current.InstalledLocation.GetFileAsync(@"Assets\bge.embmodel");
                    if (Embedder.LoadModel(file.Path))
                        System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 就绪 OK");
                    else
                        System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 模型加载失败");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 异常: " + ex.Message);
                }
            });
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                await DataManager.LoadAsync();
                InitOnDeviceAI(); // fire-and-forget，不阻塞 UI

                Frame rootFrame = Window.Current.Content as Frame;
                if (rootFrame == null)
                {
                    rootFrame = new Frame { CacheSize = 1 };
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    Window.Current.Content = rootFrame;
                }

                if (rootFrame.Content == null)
                {
                    rootFrame.ContentTransitions = null;
                    rootFrame.Navigated += RootFrame_FirstNavigated;
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnLaunched crash: " + ex);
            }
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

        private void OnNavigationFailed(object sender, Windows.UI.Xaml.Navigation.NavigationFailedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("导航失败: " + e.SourcePageType + " | " + e.Exception?.Message);
            if (e.Exception != null)
                throw new Exception("导航到 " + e.SourcePageType.Name + " 失败: " + e.Exception.Message);
        }
    }
}
