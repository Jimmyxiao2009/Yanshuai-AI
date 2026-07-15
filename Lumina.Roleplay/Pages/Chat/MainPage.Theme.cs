using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        private void PlaySound()
        {
            try { if (AppSettings.ReplySoundEnabled && ReplySound != null) { ReplySound.Source = new Uri("ms-appx:///Assets/replySound.mp3"); ReplySound.AutoPlay = true; ReplySound.Play(); } }
            catch { }
        }

        // ── Colour helpers ────────────────────────────────────────────────────

        // 已下沉到 Lumina.Core/Common/UiHelpers.cs（两项目共用，缓存也随之移走）
        private static Color GetAccentColor() => UiHelpers.GetAccentColor();

        // Call once when loading a new conversation to pre-build all bubble brushes.
        private void RebuildBrushCache()
        {
            var app = _conv?.Appearance;
            var defaultUserBg = DefaultUserBubbleColor();
            var defaultAiBg = DefaultAiBubbleColor();

            // User background
            if (app != null && !string.IsNullOrEmpty(app.UserBubbleColor)
                && ConvAppearancePage.TryParseHex(app.UserBubbleColor, out Color uc))
                _cachedUserBg = new SolidColorBrush(uc);
            else
                _cachedUserBg = new SolidColorBrush(defaultUserBg);

            // User foreground (auto contrast)
            {
                var c = (app != null && !string.IsNullOrEmpty(app.UserBubbleColor)
                         && ConvAppearancePage.TryParseHex(app.UserBubbleColor, out Color cc)) ? cc : defaultUserBg;
                _cachedUserFg = new SolidColorBrush(
                    c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186 ? Colors.Black : Colors.White);
            }

            // AI background
            if (app != null && !string.IsNullOrEmpty(app.AiBubbleColor)
                && ConvAppearancePage.TryParseHex(app.AiBubbleColor, out Color aiBc))
                _cachedAiBg = new SolidColorBrush(aiBc);
            else
                _cachedAiBg = new SolidColorBrush(defaultAiBg);

            // AI foreground (auto contrast based on AI bubble background)
            {
                var c = (app != null && !string.IsNullOrEmpty(app.AiBubbleColor)
                         && ConvAppearancePage.TryParseHex(app.AiBubbleColor, out Color cc)) ? cc
                         : defaultAiBg;
                _cachedAiFg = new SolidColorBrush(
                    c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 186 ? Colors.Black : Colors.White);
            }
        }

        private static Color DefaultUserBubbleColor()
            => AppSettings.IsDark ? Color.FromArgb(255, 199, 130, 90) : Color.FromArgb(255, 184, 110, 71);

        private static Color DefaultAiBubbleColor()
            => AppSettings.IsDark ? Color.FromArgb(255, 63, 50, 44) : Color.FromArgb(255, 255, 248, 240);

        private static Color DefaultChatBackgroundColor()
            => AppSettings.IsDark ? Color.FromArgb(255, 38, 32, 29) : Color.FromArgb(255, 250, 239, 226);

        private SolidColorBrush UserBubbleBg() => _cachedUserBg  ?? (_cachedUserBg  = new SolidColorBrush(DefaultUserBubbleColor()));
        private SolidColorBrush UserBubbleFg() => _cachedUserFg  ?? (_cachedUserFg  = _whiteBrush);
        private SolidColorBrush AiBubbleBg()   => _cachedAiBg   ?? (_cachedAiBg    = new SolidColorBrush(DefaultAiBubbleColor()));
        private SolidColorBrush AiBubbleFg()   => _cachedAiFg   ?? (_cachedAiFg    = new SolidColorBrush(AppSettings.IsDark
            ? Color.FromArgb(255, 244, 231, 220)
            : Color.FromArgb(255, 62, 45, 37)));

        private void SetSendButtonColor()
            => SubmitBtnBorder.Background = _cachedUserBg ?? new SolidColorBrush(GetAccentColor());

        private void ApplyTheme() => AppSettings.ApplyTheme(RootGrid, this);

        /// <summary>Apply per-conversation background and refresh all bubble colors.</summary>
        private async void ApplyConvAppearance()
        {
            if (_conv == null) return;
            var app = _conv.Appearance ?? new ConvAppearance();

            // 心象微染：按当前角色给聊天屏着色
            ApplyMindscapeTint();

            // 如果开启了角色卡立绘背景，且对话背景未手动设置，自动用角色卡立绘
            if (AppSettings.UseCharaIllustrationAsBg && app.BackgroundType == "none")
            {
                var chara = DataManager.GetCharacterForConversation(_conv);
                if (chara != null && chara.HasIllustration)
                {
                    app = new ConvAppearance { BackgroundType = "image", BackgroundValue = chara.IllustrationBase64 };
                }
            }

            // Background
            switch (app.BackgroundType)
            {
                case "solid":
                    ChatBgImage.Visibility  = Visibility.Collapsed;
                    if (ConvAppearancePage.TryParseHex(app.BackgroundValue, out Color bgc))
                    {
                        ChatBgSolid.Background = new SolidColorBrush(bgc);
                        ChatBgSolid.Visibility = Visibility.Visible;
                    }
                    else ChatBgSolid.Visibility = Visibility.Collapsed;
                    break;

                case "image":
                    ChatBgSolid.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(app.BackgroundValue))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(app.BackgroundValue);
                            var bmp = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                            using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                            {
                                await ms.WriteAsync(bytes.AsBuffer());
                                ms.Seek(0);
                                await bmp.SetSourceAsync(ms);
                            }
                            ChatBgImage.Source     = bmp;
                            ChatBgImage.Visibility = Visibility.Visible;
                            // 暗度遮罩：DimOpacity 0=透明 100=全黑
                            byte alpha = (byte)(app.DimOpacity * 255 / 100);
                            ChatBgDimOverlay.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                            ChatBgDimOverlay.Visibility = alpha > 0 ? Visibility.Visible : Visibility.Collapsed;
                        }
                        catch { ChatBgImage.Visibility = Visibility.Collapsed; ChatBgDimOverlay.Visibility = Visibility.Collapsed; }
                    }
                    else { ChatBgImage.Visibility = Visibility.Collapsed; ChatBgDimOverlay.Visibility = Visibility.Collapsed; }
                    break;

                default:
                    // 心象：默认不铺不透明底色，让 MindscapeWash 透出中性页面底色形成整屏微染
                    ChatBgSolid.Visibility = Visibility.Collapsed;
                    ChatBgImage.Visibility = Visibility.Collapsed;
                    ChatBgDimOverlay.Visibility = Visibility.Collapsed;
                    break;
            }

            // Refresh bubble colors on existing bubbles
            foreach (var b in _bubbles)
            {
                bool isUser = b.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
                if (isUser)
                {
                    b.BackgroundColor = UserBubbleBg();
                    b.ForegroundColor = UserBubbleFg();
                }
                else
                {
                    b.BackgroundColor = AiBubbleBg();
                    b.ForegroundColor = AiBubbleFg();
                }
            }
        }

        private async void SetStatusBarColor()
        {
            try
            {
                if (!Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
                    return;
                // 通过 WinRT 互操作访问 StatusBar，避免编译时依赖 WindowsMobile SDK
                var sbType = Type.GetType("Windows.UI.ViewManagement.StatusBar, Windows, ContentType=WindowsRuntime");
                if (sbType == null) return;
                var getForCurrentView = sbType.GetRuntimeMethod("GetForCurrentView", new Type[0]);
                dynamic sb = getForCurrentView.Invoke(null, null);
                sb.BackgroundColor = AppSettings.IsDark
                    ? Color.FromArgb(255, 31, 25, 23)
                    : Color.FromArgb(255, 46, 37, 33);
                sb.BackgroundOpacity = 1;
                sb.ForegroundColor = Colors.White;
                await sb.ShowAsync();
            }
            catch { }
        }
    }
}
