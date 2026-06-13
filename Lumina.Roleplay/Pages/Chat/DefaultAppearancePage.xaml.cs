using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    /// <summary>
    /// Global default appearance — stored in AppSettings (LocalSettings) with "default_" prefix.
    /// New conversations are initialized from these values in DataManager.GetOrCreateDefaultConversation.
    /// </summary>
    public sealed partial class DefaultAppearancePage : Page
    {
        private bool _loading = true;

        public DefaultAppearancePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            LoadCurrentValues();
        }

        // ── AppSettings accessors (default_ prefix) ───────────────────────────

        private static string DefaultBgType
        {
            get => AppSettings.GetString("default_BgType", "none");
            set => AppSettings.SetString("default_BgType", value);
        }
        private static string DefaultBgValue
        {
            get => AppSettings.GetString("default_BgValue", "");
            set => AppSettings.SetString("default_BgValue", value);
        }
        private static string DefaultUserBubble
        {
            get => AppSettings.GetString("default_UserBubble", "");
            set => AppSettings.SetString("default_UserBubble", value);
        }
        private static string DefaultAiBubble
        {
            get => AppSettings.GetString("default_AiBubble", "");
            set => AppSettings.SetString("default_AiBubble", value);
        }
        private static string DefaultQuoteColor
        {
            get => AppSettings.GetString("default_QuoteColor", "");
            set => AppSettings.SetString("default_QuoteColor", value);
        }
        private static string DefaultBracketColor
        {
            get => AppSettings.GetString("default_BracketColor", "");
            set => AppSettings.SetString("default_BracketColor", value);
        }

        // ── Public helper to build a ConvAppearance from defaults ─────────────

        public static ConvAppearance BuildDefaultAppearance()
        {
            return new ConvAppearance
            {
                BackgroundType  = DefaultBgType,
                BackgroundValue = DefaultBgValue,
                UserBubbleColor = DefaultUserBubble,
                AiBubbleColor   = DefaultAiBubble,
                QuoteColor      = DefaultQuoteColor,
                BracketColor    = DefaultBracketColor,
            };
        }

        // ── Load ──────────────────────────────────────────────────────────────

        private void LoadCurrentValues()
        {
            _loading = true;

            switch (DefaultBgType)
            {
                case "solid": BgTypePicker.SelectedIndex = 1; break;
                default:      BgTypePicker.SelectedIndex = 0; break;
            }
            UpdateBgPanel(DefaultBgType);

            BgHexBox.Text = DefaultBgType == "solid" ? DefaultBgValue : "";
            RefreshPreview(BgColorPreview, BgColorHexLabel, DefaultBgType == "solid" ? DefaultBgValue : "", "");

            UserHexBox.Text = DefaultUserBubble;
            RefreshPreview(UserBubblePreview, UserBubbleHexLabel, DefaultUserBubble, "（使用强调色）");

            AiHexBox.Text = DefaultAiBubble;
            RefreshPreview(AiBubblePreview, AiBubbleHexLabel, DefaultAiBubble, "（使用强调色暗色）");

            QuoteHexBox.Text = DefaultQuoteColor;
            RefreshPreview(QuoteColorPreview, QuoteColorHexLabel, DefaultQuoteColor, "（默认橘黄）");

            BracketHexBox.Text = DefaultBracketColor;
            RefreshPreview(BracketColorPreview, BracketColorHexLabel, DefaultBracketColor, "（默认灰色）");

            _loading = false;
        }

        // ── Background type ───────────────────────────────────────────────────

        private void BgTypePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(BgTypePicker.SelectedItem is ComboBoxItem item)) return;
            var tag = item.Tag as string ?? "none";
            UpdateBgPanel(tag);
            if (_loading) return;
            DefaultBgType = tag;
            if (tag == "none") DefaultBgValue = "";
        }

        private void UpdateBgPanel(string type)
        {
            if (SolidPanel != null) SolidPanel.Visibility = type == "solid" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BgHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = BgHexBox.Text.Trim();
            if (ConvAppearancePage.TryParseHex(hex, out _))
            {
                BgHexBox.BorderBrush = null;
                var normalized = hex.StartsWith("#") ? hex : "#" + hex;
                DefaultBgType = "solid"; DefaultBgValue = normalized;
                if (BgTypePicker.SelectedIndex != 1) { _loading = true; BgTypePicker.SelectedIndex = 1; _loading = false; }
                RefreshPreview(BgColorPreview, BgColorHexLabel, normalized, "");
            }
            else
            {
                BgHexBox.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void BgPreset_Click(object sender, string hex)
        {
            _loading = true; BgHexBox.Text = hex; _loading = false;
            DefaultBgType = "solid"; DefaultBgValue = hex;
            if (BgTypePicker.SelectedIndex != 1) BgTypePicker.SelectedIndex = 1;
            RefreshPreview(BgColorPreview, BgColorHexLabel, hex, "");
        }

        // ── User bubble ───────────────────────────────────────────────────────

        private void UserHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = UserHexBox.Text.Trim();
            if (ConvAppearancePage.TryParseHex(hex, out _))
            {
                UserHexBox.BorderBrush = null;
                DefaultUserBubble = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreview(UserBubblePreview, UserBubbleHexLabel, DefaultUserBubble, "（使用强调色）");
            }
            else
            {
                UserHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void UserPreset_Click(object sender, string hex)
        {
            _loading = true; UserHexBox.Text = hex; _loading = false;
            DefaultUserBubble = hex;
            RefreshPreview(UserBubblePreview, UserBubbleHexLabel, hex, "（使用强调色）");
        }

        private void ClearUserBubble_Click(object sender, RoutedEventArgs e)
        {
            DefaultUserBubble = "";
            _loading = true; UserHexBox.Text = ""; _loading = false;
            RefreshPreview(UserBubblePreview, UserBubbleHexLabel, "", "（使用强调色）");
        }

        // ── AI bubble ─────────────────────────────────────────────────────────

        private void AiHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = AiHexBox.Text.Trim();
            if (ConvAppearancePage.TryParseHex(hex, out _))
            {
                AiHexBox.BorderBrush = null;
                DefaultAiBubble = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreview(AiBubblePreview, AiBubbleHexLabel, DefaultAiBubble, "（使用强调色暗色）");
            }
            else
            {
                AiHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void AiPreset_Click(object sender, string hex)
        {
            _loading = true; AiHexBox.Text = hex; _loading = false;
            DefaultAiBubble = hex;
            RefreshPreview(AiBubblePreview, AiBubbleHexLabel, hex, "（使用强调色暗色）");
        }

        private void ClearAiBubble_Click(object sender, RoutedEventArgs e)
        {
            DefaultAiBubble = "";
            _loading = true; AiHexBox.Text = ""; _loading = false;
            RefreshPreview(AiBubblePreview, AiBubbleHexLabel, "", "（使用强调色暗色）");
        }

        // ── Quote color ───────────────────────────────────────────────────────

        private void QuoteHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = QuoteHexBox.Text.Trim();
            if (ConvAppearancePage.TryParseHex(hex, out _))
            {
                QuoteHexBox.BorderBrush = null;
                DefaultQuoteColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreview(QuoteColorPreview, QuoteColorHexLabel, DefaultQuoteColor, "（默认橘黄）");
            }
            else
            {
                QuoteHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void QuotePreset_Click(object sender, string hex)
        {
            _loading = true; QuoteHexBox.Text = hex; _loading = false;
            DefaultQuoteColor = hex;
            RefreshPreview(QuoteColorPreview, QuoteColorHexLabel, hex, "（默认橘黄）");
        }

        private void ClearQuoteColor_Click(object sender, RoutedEventArgs e)
        {
            DefaultQuoteColor = "";
            _loading = true; QuoteHexBox.Text = ""; _loading = false;
            RefreshPreview(QuoteColorPreview, QuoteColorHexLabel, "", "（默认橘黄）");
        }

        // ── Bracket color ─────────────────────────────────────────────────────

        private void BracketHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = BracketHexBox.Text.Trim();
            if (ConvAppearancePage.TryParseHex(hex, out _))
            {
                BracketHexBox.BorderBrush = null;
                DefaultBracketColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshPreview(BracketColorPreview, BracketColorHexLabel, DefaultBracketColor, "（默认灰色）");
            }
            else
            {
                BracketHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void BracketPreset_Click(object sender, string hex)
        {
            _loading = true; BracketHexBox.Text = hex; _loading = false;
            DefaultBracketColor = hex;
            RefreshPreview(BracketColorPreview, BracketColorHexLabel, hex, "（默认灰色）");
        }

        private void ClearBracketColor_Click(object sender, RoutedEventArgs e)
        {
            DefaultBracketColor = "";
            _loading = true; BracketHexBox.Text = ""; _loading = false;
            RefreshPreview(BracketColorPreview, BracketColorHexLabel, "", "（默认灰色）");
        }

        // ── Reset / Back ──────────────────────────────────────────────────────

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            DefaultBgType = "none"; DefaultBgValue = "";
            DefaultUserBubble = ""; DefaultAiBubble = "";
            DefaultQuoteColor = ""; DefaultBracketColor = "";
            LoadCurrentValues();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        { if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(SettingsPage)); }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void RefreshPreview(Border preview, TextBlock label, string hex, string emptyText)
        {
            if (preview == null) return;
            if (!string.IsNullOrEmpty(hex) && ConvAppearancePage.TryParseHex(hex, out Color c))
            {
                preview.Background = new SolidColorBrush(c);
                if (label != null)
                {
                    label.Text = hex.StartsWith("#") ? hex.ToUpper() : "#" + hex.ToUpper();
                    double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    label.Foreground = new SolidColorBrush(lum > 128 ? Colors.Black : Colors.White);
                }
            }
            else
            {
                preview.Background = null;
                if (label != null)
                {
                    label.Text = emptyText;
                    label.Foreground = new SolidColorBrush(Colors.Gray);
                }
            }
        }
    }
}
