using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharaWizardPage1 : Page
    {
        public CharaWizardPage1() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();

            // 恢复已填内容（返回时）
            NameBox.Text = CharaWizardData.Name;
            if (!string.IsNullOrEmpty(CharaWizardData.AvatarBase64))
                LoadImageToControl(CharaWizardData.AvatarBase64, AvatarImage, AvatarPlaceholder);
            if (!string.IsNullOrEmpty(CharaWizardData.IllustBase64))
                LoadImageToControl(CharaWizardData.IllustBase64, IllustImage, IllustPlaceholder);
        }

        private void LoadImageToControl(string b64, Image img, FrameworkElement placeholder)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = AppSettings.LoadBitmapSync(bytes);
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private async void PickAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var compressed = await CompressImageFileAsync(file, 1024);
            if (compressed == null) return;
            CharaWizardData.AvatarBase64   = compressed.Item1;
            CharaWizardData.AvatarMimeType = compressed.Item2;
            LoadImageToControl(CharaWizardData.AvatarBase64, AvatarImage, AvatarPlaceholder);
        }

        private void ClearAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.AvatarBase64 = null; CharaWizardData.AvatarMimeType = null;
            AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed;
            AvatarPlaceholder.Visibility = Visibility.Visible;
        }

        private async void PickIllustBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg"); picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png"); picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var compressed = await CompressImageFileAsync(file, 1280);
            if (compressed == null) return;
            CharaWizardData.IllustBase64   = compressed.Item1;
            CharaWizardData.IllustMimeType = compressed.Item2;
            LoadImageToControl(CharaWizardData.IllustBase64, IllustImage, IllustPlaceholder);
        }

        private void ClearIllustBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.IllustBase64 = null; CharaWizardData.IllustMimeType = null;
            IllustImage.Source = null; IllustImage.Visibility = Visibility.Collapsed;
            IllustPlaceholder.Visibility = Visibility.Visible;
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.Name = NameBox.Text.Trim();
            Frame.Navigate(typeof(CharaWizardPage2));
        }

        private void ApplyLanguage()
        {
            PageTitle.Text = AppSettings.S("新建角色卡", "New Character Card");
            StepText.Text = AppSettings.S("第 1 步 / 共 4 步  ·  头像与名称", "Step 1/4 · Avatar & Name");
            AvatarLabel.Text = AppSettings.S("头像", "Avatar");
            PickAvatarBtn.Content = AppSettings.S("选择头像图片", "Choose Avatar Image");
            ClearAvatarBtn.Content = AppSettings.S("清除", "Clear");
            NameLabel.Text = AppSettings.S("角色名称", "Character Name");
            NameBox.PlaceholderText = AppSettings.S("输入角色名称…", "Enter character name…");
            IllustLabel.Text = AppSettings.S("立绘图片（可选）", "Character Art (optional)");
            IllustHint.Text = AppSettings.S("用于对话背景，建议竖版图片", "Used as chat background, portrait recommended");
            PickIllustBtn.Content = AppSettings.S("选择立绘", "Choose Art");
            ClearIllustBtn.Content = AppSettings.S("清除", "Clear");
            NextBtn.Label = AppSettings.S("下一步", "Next");
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        /// <summary>
        /// 限制最大边并转 JPEG，避免超大头像/立绘整图 Base64 打爆内存。
        /// </summary>
        private static async Task<Tuple<string, string>> CompressImageFileAsync(StorageFile file, uint maxDim)
        {
            try
            {
                using (var stream = await file.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    uint w = decoder.OrientedPixelWidth, h = decoder.OrientedPixelHeight;
                    uint newW = w, newH = h;
                    if (w > maxDim || h > maxDim)
                    {
                        if (w > h) { newW = maxDim; newH = (uint)(h * (double)maxDim / w); }
                        else { newH = maxDim; newW = (uint)(w * (double)maxDim / h); }
                    }

                    uint scaleW = newW, scaleH = newH;
                    if (decoder.OrientedPixelWidth != decoder.PixelWidth &&
                        decoder.OrientedPixelWidth == decoder.PixelHeight)
                    {
                        scaleW = newH;
                        scaleH = newW;
                    }

                    var transform = new BitmapTransform { ScaledWidth = scaleW, ScaledHeight = scaleH };
                    var pixels = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, transform,
                        ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

                    using (var ms = new InMemoryRandomAccessStream())
                    {
                        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);
                        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied,
                            newW, newH, 96, 96, pixels.DetachPixelData());
                        await encoder.FlushAsync();
                        ms.Seek(0);
                        var bytes = new byte[ms.Size];
                        await ms.ReadAsync(bytes.AsBuffer(), (uint)ms.Size, InputStreamOptions.None);
                        return Tuple.Create(Convert.ToBase64String(bytes), "image/jpeg");
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
