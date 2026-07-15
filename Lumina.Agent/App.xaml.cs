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
                CrashLog("XAML-UNHANDLED", e.Exception);
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                CrashLog("TASK", e.Exception);
                e.SetObserved();
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

                rootFrame.Navigated += (s, args) =>
                {
                    var navMgr = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
                    navMgr.AppViewBackButtonVisibility = rootFrame.CanGoBack
                        ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                        : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
                };

                Windows.UI.Core.SystemNavigationManager.GetForCurrentView().BackRequested += (s, args) =>
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
                CrashLog("LAUNCH-CRASH", ex);
                
                // 显示友好错误屏幕代替白屏 (P2-1)
                var panel = new StackPanel 
                { 
                    VerticalAlignment = VerticalAlignment.Center, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(20)
                };
                panel.Children.Add(new TextBlock 
                { 
                    Text = "应用启动失败 / Startup Failed", 
                    FontSize = 24, 
                    FontWeight = Windows.UI.Text.FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 10),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                panel.Children.Add(new TextBlock 
                { 
                    Text = $"抱歉，应用在启动时遇到了严重错误。日志已记录到本地存储的 uwp_crash.log 中。\n\n详细信息:\n{ex.Message}\n{ex.StackTrace}", 
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxHeight = 400
                });
                Window.Current.Content = panel;
                Window.Current.Activate();
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

        private static void CrashLog(string tag, Exception ex)
        {
            string detail;
            try
            {
                var sb = new System.Text.StringBuilder();
                var e = ex;
                int depth = 0;
                while (e != null && depth++ < 6)
                {
                    sb.AppendLine($"  [{e.GetType().FullName}] HResult=0x{e.HResult:X8}: {e.Message}");
                    if (!string.IsNullOrEmpty(e.StackTrace)) sb.AppendLine(e.StackTrace);
                    e = e.InnerException;
                }
                detail = sb.ToString();
            }
            catch { detail = ex?.ToString() ?? "(null exception)"; }

            try { System.Diagnostics.Debug.WriteLine(tag + ": " + detail); } catch { }
            try
            {
                var path = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "uwp_crash.log");
                System.IO.File.AppendAllText(path, DateTime.Now.ToString("u") + " [" + tag + "]\n" + detail + "\n\n");
            }
            catch { }
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await DataManager.SaveAsync();
            deferral.Complete();
        }

        private void OnNavigationFailed(object sender, Windows.UI.Xaml.Navigation.NavigationFailedEventArgs e)
        {
            CrashLog("NAV-FAILED", e.Exception);
        }
    }
}
