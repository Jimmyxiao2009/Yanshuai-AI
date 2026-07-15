using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

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

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                ApplyTheme();

                AppSettings.LoadTranslations();
                await DataManager.LoadAsync();
                DialoguePoolManager.LoadAll();

                // 异步加载嵌入模型（不阻塞启动）
                var embedder = new OnEmbedder();
                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Low, async () =>
                    {
                        try
                        {
                            var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                            var modelFile = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                                new Uri("ms-appx:///Assets/bge.embmodel"));
                            if (modelFile != null)
                            {
                                // 仅在加载成功时发布，否则下游会得到全零向量（CosineSim 恒为 0），
                                // RAG 静默退化为关键词检索，甚至把零向量持久化到磁盘。
                                if (embedder.LoadModel(modelFile.Path))
                                {
                                    AppState.Embedder = embedder;
                                    System.Diagnostics.Debug.WriteLine("[App] Embedding model loaded");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("[App] Embedder model load failed");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[App] Embedder init failed: {ex.Message}");
                        }
                    });

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

                    if (AppSettings.OobeCompleted)
                    {
                        if (!rootFrame.Navigate(typeof(ShellPage), e.Arguments))
                            throw new Exception("Failed to create ShellPage");
                    }
                    else
                    {
                        if (!rootFrame.Navigate(typeof(OobeWelcomePage), e.Arguments))
                            throw new Exception("Failed to create initial page");
                    }
                }

                // 统一后退导航（Mobile 硬件键 + Desktop 标题栏后退 + 平板模式）
                var navMgr = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
                navMgr.BackRequested += (s, args) =>
                {
                    var shellFrame = ShellPage.Current?.ContentFrame;
                    if (shellFrame != null && shellFrame.CanGoBack)
                    {
                        args.Handled = true;
                        shellFrame.GoBack();
                    }
                    else
                    {
                        Frame frame = Window.Current.Content as Frame;
                        if (frame != null && frame.CanGoBack)
                        {
                            args.Handled = true;
                            frame.GoBack();
                        }
                    }
                };

                rootFrame.Navigated += (s, args) => UpdateBackButtonVisibility(rootFrame);
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

        private void UpdateBackButtonVisibility(Frame rootFrame)
        {
            var navMgr = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
            var shellFrame = ShellPage.Current?.ContentFrame;
            bool canGoBack = rootFrame.CanGoBack || (shellFrame != null && shellFrame.CanGoBack);
            navMgr.AppViewBackButtonVisibility = canGoBack
                ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;

            if (shellFrame != null)
            {
                shellFrame.Navigated -= ShellFrame_Navigated;
                shellFrame.Navigated += ShellFrame_Navigated;
            }
        }

        private void ShellFrame_Navigated(object sender, NavigationEventArgs e)
        {
            var navMgr = Windows.UI.Core.SystemNavigationManager.GetForCurrentView();
            var shellFrame = sender as Frame;
            Frame rootFrame = Window.Current.Content as Frame;
            bool canGoBack = (rootFrame != null && rootFrame.CanGoBack) || (shellFrame != null && shellFrame.CanGoBack);
            navMgr.AppViewBackButtonVisibility = canGoBack
                ? Windows.UI.Core.AppViewBackButtonVisibility.Visible
                : Windows.UI.Core.AppViewBackButtonVisibility.Collapsed;
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
            try
            {
                // 关键持久化要先做：UWP 挂起仅有 ~5 秒，下面的网络摘要（最长 60s/次）
                // 极易超时被系统终止；先存盘保证用户最后一轮对话不会丢失。
                await DataManager.SaveAsync();

                // 挂起前机会性触发记忆提取（如有活跃对话）。这些工作平时已在每次发送后
                // 运行，这里只是补一刀；用硬超时（2s）兜底，绝不阻塞挂起完成。
                var activeConv = AppState.ActiveConversation;
                if (activeConv != null && activeConv.MemoryEnabled && activeConv.ExchangesSinceLastSummary > 0 && activeConv.Messages.Count >= 3)
                {
                    activeConv.ExchangesSinceLastSummary = 0;
                    Func<System.Threading.Tasks.Task> memoryWork = async () =>
                    {
                        await MemoryPipeline.SummarizeAndStoreAsync(activeConv);
                        if (!string.IsNullOrEmpty(activeConv.CharacterCardId))
                        {
                            var pool = DialoguePoolManager.GetPool(activeConv.CharacterCardId, activeConv.UserProfileId);
                            if (pool != null && pool.Settings.AutoSummarizeConversations)
                                await MemoryPipeline.ExtractDeepMemoryAsync(activeConv, pool);
                        }
                    };
                    await System.Threading.Tasks.Task.WhenAny(memoryWork(), System.Threading.Tasks.Task.Delay(2000));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] OnSuspending failed: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        public static void ApplyTheme()
        {
            var name = AppSettings.ThemeName;
            string source;
            if (name == "Celadon")
                source = "ms-appx:///Themes/Celadon.xaml";
            else if (name == "MidnightVermilion")
                source = "ms-appx:///Themes/MidnightVermilion.xaml";
            else if (name == "ModernSlate")
                source = "ms-appx:///Themes/ModernSlate.xaml";
            else if (name == "InkScroll")
                source = "ms-appx:///Themes/InkScroll.xaml";
            else
                source = "ms-appx:///Themes/Mindscape.xaml";   // 默认：心象（全新设计语言）

            var mds = Application.Current.Resources.MergedDictionaries;
            ClearFontResourceOverrides();
            if (mds.Count > 0)
                mds.RemoveAt(0);
            var rd = new ResourceDictionary { Source = new Uri(source) };
            mds.Insert(0, rd);

            // 自定义主题：覆盖所有颜色资源
            if (name == "Custom")
                ApplyCustomThemeColors();

            // 应用用户选择的字体（覆盖主题默认字体）
            try { ApplyFontOverrides(); } catch { }

            // 应用自定义主色（覆盖主题默认 YanshuaiAccentBrush）
            try
            {
                var hex = AppSettings.CustomAccentHex;
                if (!string.IsNullOrEmpty(hex))
                {
                    Color color;
                    if (TryParseHex(hex, out color))
                    {
                        Application.Current.Resources["YanshuaiAccentBrush"] =
                            new SolidColorBrush(color);
                    }
                }
            }
            catch { }
        }

        static void ApplyCustomThemeColors()
        {
            var res = Application.Current.Resources;
            SetBrush(res, "YanshuaiPageBrush",       AppSettings.CustomTheme_PageBg);
            SetBrush(res, "YanshuaiAccentBrush",     AppSettings.CustomTheme_Accent);
            SetBrush(res, "YanshuaiTextBrush",       AppSettings.CustomTheme_Text);
            SetBrush(res, "YanshuaiSurfaceBrush",    AppSettings.CustomTheme_Surface);
            SetBrush(res, "YanshuaiBorderBrush",     AppSettings.CustomTheme_Border);
            SetBrush(res, "YanshuaiUserBubbleBrush", AppSettings.CustomTheme_UserBubble);
            SetBrush(res, "YanshuaiAiBubbleBrush",   AppSettings.CustomTheme_AiBubble);
            SetBrush(res, "YanshuaiMutedTextBrush",  AppSettings.CustomTheme_MutedText);
            SetBrush(res, "YanshuaiChromeBrush",     AppSettings.CustomTheme_ChromeBg);
            SetBrush(res, "YanshuaiSubtlePanelBrush",AppSettings.CustomTheme_SubtlPanel);
            SetBrush(res, "YanshuaiPanelBrush",      AppSettings.CustomTheme_Panel);
            SetBrush(res, "YanshuaiPaneHeaderBrush", AppSettings.CustomTheme_PaneHeader);
        }

        static void SetBrush(ResourceDictionary res, string key, string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            Color color;
            if (TryParseHex(hex, out color))
                res[key] = new SolidColorBrush(color);
        }

        static void ClearFontResourceOverrides()
        {
            var res = Application.Current.Resources;
            RemoveResource(res, "YanshuaiTitleFont");
            RemoveResource(res, "YanshuaiBodyFont");
            RemoveResource(res, "YanshuaiEnglishFont");
            RemoveResource(res, "YanshuaiEnglishItalicFont");
        }

        static void RemoveResource(ResourceDictionary res, string key)
        {
            if (res != null && res.ContainsKey(key))
                res.Remove(key);
        }

        static void ApplyFontOverrides()
        {
            var cn = AppSettings.ChineseFontFamily;
            var en = AppSettings.EnglishFontFamily;

            // 中文字体 → YanshuaiTitleFont / YanshuaiBodyFont
            string cnTitlePath = null, cnBodyPath = null, cnFamily = null;
            if (cn != "ThemeDefault")
                GetChineseFont(cn, out cnTitlePath, out cnBodyPath, out cnFamily);
            if (cnTitlePath != null)
            {
                SetResource("YanshuaiTitleFont", new FontFamily(cnTitlePath));
                SetResource("YanshuaiBodyFont", new FontFamily(cnBodyPath));
            }

            // 英文字体 → YanshuaiEnglishFont / YanshuaiEnglishItalicFont
            string enRegPath = null, enItPath = null, enFamily = null;
            if (en != "ThemeDefault")
                GetEnglishFont(en, out enRegPath, out enItPath, out enFamily);
            if (enRegPath != null)
            {
                SetResource("YanshuaiEnglishFont", new FontFamily(enRegPath));
                SetResource("YanshuaiEnglishItalicFont", new FontFamily(enItPath));
            }
        }

        public static void ApplyTypography(DependencyObject root)
        {
            if (root == null) return;
            var body = GetResourceFont("YanshuaiBodyFont");
            var title = GetResourceFont("YanshuaiTitleFont");
            if (body == null) return;
            ApplyTypographyRecursive(root, body, title);
        }

        static void ApplyTypographyRecursive(DependencyObject current, FontFamily body, FontFamily title)
        {
            var textBlock = current as TextBlock;
            if (textBlock != null && CanReplaceFont(textBlock.FontFamily))
                textBlock.FontFamily = textBlock.FontSize >= 24 && title != null ? title : body;

            var textBox = current as TextBox;
            if (textBox != null && CanReplaceFont(textBox.FontFamily))
                textBox.FontFamily = body;

            var passwordBox = current as PasswordBox;
            if (passwordBox != null && CanReplaceFont(passwordBox.FontFamily))
                passwordBox.FontFamily = body;

            var comboBox = current as ComboBox;
            if (comboBox != null && CanReplaceFont(comboBox.FontFamily))
                comboBox.FontFamily = body;

            var button = current as Button;
            if (button != null && CanReplaceFont(button.FontFamily))
                button.FontFamily = body;

            var richTextBlock = current as RichTextBlock;
            if (richTextBlock != null && CanReplaceFont(richTextBlock.FontFamily))
                richTextBlock.FontFamily = body;

            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
                ApplyTypographyRecursive(VisualTreeHelper.GetChild(current, i), body, title);
        }

        static bool CanReplaceFont(FontFamily font)
        {
            var source = font?.Source ?? "";
            return source.IndexOf("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase) < 0 &&
                   source.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) < 0 &&
                   source.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) < 0;
        }

        static FontFamily GetResourceFont(string key)
        {
            var res = Application.Current.Resources;
            if (res.ContainsKey(key))
                return res[key] as FontFamily;
            foreach (var md in res.MergedDictionaries)
            {
                if (md.ContainsKey(key))
                    return md[key] as FontFamily;
            }
            return null;
        }

        static void SetResource(string key, object value)
        {
            var res = Application.Current.Resources;
            res[key] = value;
            foreach (var md in res.MergedDictionaries)
            {
                if (md.ContainsKey(key))
                    md[key] = value;
            }
        }

        static void GetChineseFont(string key, out string titlePath, out string bodyPath, out string family)
        {
            titlePath = null; bodyPath = null; family = null;
            if (key == "LXGW")
            {
                titlePath = "ms-appx:///Assets/Fonts/LXGWWenKai-Bold.ttf#LXGW WenKai";
                bodyPath  = "ms-appx:///Assets/Fonts/LXGWWenKai-Regular.ttf#LXGW WenKai";
            }
            else if (key == "SourceHanSerif")
            {
                titlePath = "ms-appx:///Assets/Fonts/SourceHanSerif-Bold.ttf#Source Han Serif SC";
                bodyPath  = "ms-appx:///Assets/Fonts/SourceHanSerif-Regular.ttf#Source Han Serif SC";
            }
            else if (key == "ZCOOL")
            {
                titlePath = "ms-appx:///Assets/Fonts/ZCOOLXiaoWei-Regular.ttf#ZCOOL XiaoWei";
                bodyPath  = "ms-appx:///Assets/Fonts/ZCOOLXiaoWei-Regular.ttf#ZCOOL XiaoWei";
            }
            else if (key == "NotoSansSC")
            {
                titlePath = "ms-appx:///Assets/Fonts/NotoSansSC-Regular.ttf#Noto Sans SC Thin";
                bodyPath  = "ms-appx:///Assets/Fonts/NotoSansSC-Regular.ttf#Noto Sans SC Thin";
            }
            else if (key == "MaShanZheng")
            {
                titlePath = "ms-appx:///Assets/Fonts/MaShanZheng-Regular.ttf#Ma Shan Zheng";
                bodyPath  = "ms-appx:///Assets/Fonts/MaShanZheng-Regular.ttf#Ma Shan Zheng";
            }
            else if (key == "System")
            {
                titlePath = "Segoe UI";
                bodyPath  = "Segoe UI";
            }
        }

        static void GetEnglishFont(string key, out string regPath, out string itPath, out string family)
        {
            regPath = null; itPath = null; family = null;
            if (key == "EBGaramond")
            {
                regPath = "ms-appx:///Assets/Fonts/EBGaramond-Regular.ttf#EB Garamond";
                itPath  = "ms-appx:///Assets/Fonts/EBGaramond-Italic.ttf#EB Garamond";
            }
            else if (key == "Cormorant")
            {
                regPath = "ms-appx:///Assets/Fonts/Cormorant-Regular.ttf#Cormorant Light";
                itPath  = "ms-appx:///Assets/Fonts/Cormorant-Italic.ttf#Cormorant Light";
            }
            else if (key == "Lora")
            {
                regPath = "ms-appx:///Assets/Fonts/Lora-Regular.ttf#Lora";
                itPath  = "ms-appx:///Assets/Fonts/Lora-Italic.ttf#Lora";
            }
            else if (key == "PlayfairDisplay")
            {
                regPath = "ms-appx:///Assets/Fonts/PlayfairDisplay-Regular.ttf#Playfair Display";
                itPath  = "ms-appx:///Assets/Fonts/PlayfairDisplay-Italic.ttf#Playfair Display";
            }
            else if (key == "System")
            {
                regPath = "Segoe UI";
                itPath  = "Segoe UI";
            }
        }

        static bool TryParseHex(string hex, out Color color)
        {
            color = default(Color);
            hex = (hex ?? "").TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                color = Color.FromArgb(255, r, g, b);
                return true;
            }
            catch { return false; }
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
    }
}
