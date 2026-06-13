using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ConvAppearancePage : Page
    {
        private Conversation _conv;
        private bool _loading = true;

        public ConvAppearancePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            _conv = AppState.ActiveConversation;
            if (_conv == null) { Frame.GoBack(); return; }
            if (_conv.Appearance == null) _conv.Appearance = new ConvAppearance();
            LoadCurrentValues();
        }

        private void LoadCurrentValues()
        {
            _loading = true;
            var a = _conv.Appearance;

            switch (a.BackgroundType)
            {
                case "solid": BgTypePicker.SelectedIndex = 1; break;
                case "image": BgTypePicker.SelectedIndex = 2; break;
                default:      BgTypePicker.SelectedIndex = 0; break;
            }
            UpdateBgPanels(a.BackgroundType);

            BgHexBox.Text = a.BackgroundType == "solid" ? a.BackgroundValue : "";
            RefreshBgPreview(a.BackgroundType == "solid" ? a.BackgroundValue : "");

            if (a.BackgroundType == "image" && !string.IsNullOrEmpty(a.BackgroundValue))
            { ImageStatusText.Text = "已设置图片背景"; ShowImagePreviewFromBase64(a.BackgroundValue); }
            DimSlider.Value = a.DimOpacity;
            DimLabel.Text = $"暗度：{a.DimOpacity}%";

            UserHexBox.Text = a.UserBubbleColor ?? "";
            RefreshBubblePreview(UserBubblePreview, UserBubbleHexLabel, a.UserBubbleColor, "（使用强调色）");

            AiHexBox.Text = a.AiBubbleColor ?? "";
            RefreshBubblePreview(AiBubblePreview, AiBubbleHexLabel, a.AiBubbleColor, "（使用强调色暗色）");

            QuoteHexBox.Text = a.QuoteColor ?? "";
            RefreshBubblePreview(QuoteColorPreview, QuoteColorHexLabel, a.QuoteColor, "（默认橘黄）");

            BracketHexBox.Text = a.BracketColor ?? "";
            RefreshBubblePreview(BracketColorPreview, BracketColorHexLabel, a.BracketColor, "（默认灰色）");

            _loading = false;
        }

        // ── Background type ───────────────────────────────────────────────────

        private void BgTypePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(BgTypePicker.SelectedItem is ComboBoxItem item)) return;
            var tag = item.Tag as string ?? "none";
            UpdateBgPanels(tag);
            if (_loading) return;
            _conv.Appearance.BackgroundType = tag;
            if (tag == "none") _conv.Appearance.BackgroundValue = "";
            _ = DataManager.SaveAsync();
        }

        private void UpdateBgPanels(string type)
        {
            if (SolidPanel != null) SolidPanel.Visibility = type == "solid" ? Visibility.Visible : Visibility.Collapsed;
            if (ImagePanel != null) ImagePanel.Visibility = type == "image" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BgHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = BgHexBox.Text.Trim();
            if (TryParseHex(hex, out _))
            {
                BgHexBox.BorderBrush = null;
                RefreshBgPreview(hex);
                _conv.Appearance.BackgroundType = "solid";
                _conv.Appearance.BackgroundValue = hex.StartsWith("#") ? hex : "#" + hex;
                if (BgTypePicker.SelectedIndex != 1) { _loading = true; BgTypePicker.SelectedIndex = 1; _loading = false; }
                _ = DataManager.SaveAsync();
            }
            else
            {
                BgHexBox.BorderBrush = new SolidColorBrush(Colors.Red);
            }
        }

        private void RefreshBgPreview(string hex)
        {
            if (BgColorPreview == null) return;
            if (TryParseHex(hex, out Color c))
            {
                BgColorPreview.Background = new SolidColorBrush(c);
                if (BgColorHexLabel != null)
                {
                    BgColorHexLabel.Text = hex.StartsWith("#") ? hex.ToUpper() : "#" + hex.ToUpper();
                    double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    BgColorHexLabel.Foreground = new SolidColorBrush(lum > 128 ? Colors.Black : Colors.White);
                }
            }
            else
            {
                BgColorPreview.Background = null;
                if (BgColorHexLabel != null) BgColorHexLabel.Text = "";
            }
        }

        private void BgPreset_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string hex)) return;
            _loading = true; BgHexBox.Text = hex; _loading = false;
            RefreshBgPreview(hex);
            _conv.Appearance.BackgroundType = "solid"; _conv.Appearance.BackgroundValue = hex;
            if (BgTypePicker.SelectedIndex != 1) BgTypePicker.SelectedIndex = 1;
            _ = DataManager.SaveAsync();
        }

        // ── Image picker ──────────────────────────────────────────────────────

        private async void PickImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg"); picker.FileTypeFilter.Add(".png");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            try
            {
                var buffer = await FileIO.ReadBufferAsync(file);
                string b64 = Convert.ToBase64String(buffer.ToArray());
                _conv.Appearance.BackgroundType = "image"; _conv.Appearance.BackgroundValue = b64;
                await DataManager.SaveAsync();
                ImageStatusText.Text = "已选择：" + file.Name;
                ShowImagePreviewFromBase64(b64);
            }
            catch (Exception ex) { ImageStatusText.Text = "读取失败：" + ex.Message; }
        }

        private async void ShowImagePreviewFromBase64(string b64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(b64);
                var bmp = new BitmapImage();
                using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0); await bmp.SetSourceAsync(ms);
                }
                ImagePreview.Source = bmp;
                ImagePreviewBorder.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // ── User bubble ───────────────────────────────────────────────────────

        private void UserHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = UserHexBox.Text.Trim();
            if (TryParseHex(hex, out _))
            {
                UserHexBox.BorderBrush = null;
                _conv.Appearance.UserBubbleColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshBubblePreview(UserBubblePreview, UserBubbleHexLabel, _conv.Appearance.UserBubbleColor, "（使用强调色）");
                _ = DataManager.SaveAsync();
            }
            else
            {
                UserHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void UserPreset_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string hex)) return;
            _loading = true; UserHexBox.Text = hex; _loading = false;
            _conv.Appearance.UserBubbleColor = hex;
            RefreshBubblePreview(UserBubblePreview, UserBubbleHexLabel, hex, "（使用强调色）");
            _ = DataManager.SaveAsync();
        }

        private void ClearUserBubble_Click(object sender, RoutedEventArgs e)
        {
            _conv.Appearance.UserBubbleColor = "";
            _loading = true; UserHexBox.Text = ""; _loading = false;
            RefreshBubblePreview(UserBubblePreview, UserBubbleHexLabel, "", "（使用强调色）");
            _ = DataManager.SaveAsync();
        }

        // ── AI bubble ─────────────────────────────────────────────────────────

        private void AiHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = AiHexBox.Text.Trim();
            if (TryParseHex(hex, out _))
            {
                AiHexBox.BorderBrush = null;
                _conv.Appearance.AiBubbleColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshBubblePreview(AiBubblePreview, AiBubbleHexLabel, _conv.Appearance.AiBubbleColor, "（使用强调色暗色）");
                _ = DataManager.SaveAsync();
            }
            else
            {
                AiHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void AiPreset_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string hex)) return;
            _loading = true; AiHexBox.Text = hex; _loading = false;
            _conv.Appearance.AiBubbleColor = hex;
            RefreshBubblePreview(AiBubblePreview, AiBubbleHexLabel, hex, "（使用强调色暗色）");
            _ = DataManager.SaveAsync();
        }

        private void ClearAiBubble_Click(object sender, RoutedEventArgs e)
        {
            _conv.Appearance.AiBubbleColor = "";
            _loading = true; AiHexBox.Text = ""; _loading = false;
            RefreshBubblePreview(AiBubblePreview, AiBubbleHexLabel, "", "（使用强调色暗色）");
            _ = DataManager.SaveAsync();
        }

        // ── Quote color ───────────────────────────────────────────────────────

        private void QuoteHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = QuoteHexBox.Text.Trim();
            if (TryParseHex(hex, out _))
            {
                QuoteHexBox.BorderBrush = null;
                _conv.Appearance.QuoteColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshBubblePreview(QuoteColorPreview, QuoteColorHexLabel, _conv.Appearance.QuoteColor, "（默认橘黄）");
                _ = DataManager.SaveAsync();
            }
            else
            {
                QuoteHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void QuotePreset_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string hex)) return;
            _loading = true; QuoteHexBox.Text = hex; _loading = false;
            _conv.Appearance.QuoteColor = hex;
            RefreshBubblePreview(QuoteColorPreview, QuoteColorHexLabel, hex, "（默认橘黄）");
            _ = DataManager.SaveAsync();
        }

        private void ClearQuoteColor_Click(object sender, RoutedEventArgs e)
        {
            _conv.Appearance.QuoteColor = "";
            _loading = true; QuoteHexBox.Text = ""; _loading = false;
            RefreshBubblePreview(QuoteColorPreview, QuoteColorHexLabel, "", "（默认橘黄）");
            _ = DataManager.SaveAsync();
        }

        // ── Bracket color ─────────────────────────────────────────────────────

        private void BracketHexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            var hex = BracketHexBox.Text.Trim();
            if (TryParseHex(hex, out _))
            {
                BracketHexBox.BorderBrush = null;
                _conv.Appearance.BracketColor = hex.StartsWith("#") ? hex : "#" + hex;
                RefreshBubblePreview(BracketColorPreview, BracketColorHexLabel, _conv.Appearance.BracketColor, "（默认灰色）");
                _ = DataManager.SaveAsync();
            }
            else
            {
                BracketHexBox.BorderBrush = string.IsNullOrEmpty(hex) ? null : new SolidColorBrush(Colors.Red);
            }
        }

        private void BracketPreset_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is string hex)) return;
            _loading = true; BracketHexBox.Text = hex; _loading = false;
            _conv.Appearance.BracketColor = hex;
            RefreshBubblePreview(BracketColorPreview, BracketColorHexLabel, hex, "（默认灰色）");
            _ = DataManager.SaveAsync();
        }

        private void ClearBracketColor_Click(object sender, RoutedEventArgs e)
        {
            _conv.Appearance.BracketColor = "";
            _loading = true; BracketHexBox.Text = ""; _loading = false;
            RefreshBubblePreview(BracketColorPreview, BracketColorHexLabel, "", "（默认灰色）");
            _ = DataManager.SaveAsync();
        }

        // ── Dim slider ────────────────────────────────────────────────────────

        private void DimSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_loading || _conv?.Appearance == null) return;
            int val = (int)DimSlider.Value;
            _conv.Appearance.DimOpacity = val;
            DimLabel.Text = $"暗度：{val}%";
            _ = DataManager.SaveAsync();
        }

        // ── Reset / Back ──────────────────────────────────────────────────────

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        { _conv.Appearance = new ConvAppearance(); _ = DataManager.SaveAsync(); LoadCurrentValues(); }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        { if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(MainPage)); }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void RefreshBubblePreview(Border preview, TextBlock label, string hex, string emptyText)
        {
            if (preview == null) return;
            if (!string.IsNullOrEmpty(hex) && TryParseHex(hex, out Color c))
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

        public static bool TryParseHex(string hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrEmpty(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                color = Color.FromArgb(255, r, g, b); return true;
            }
            catch { return false; }
        }
    }
}
