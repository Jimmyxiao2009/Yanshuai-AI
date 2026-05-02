using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeBasicPage : Page
    {
        private bool _loading = true;

        public OobeBasicPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;
            AppSettings.ApplyTheme(RootGrid, this);
            DarkModeToggle.IsOn = AppSettings.IsDark;
            LangToggle.IsOn     = AppSettings.IsEnglish;
            ApplyLanguage();
            _loading = false;
        }

        private void ApplyLanguage()
        {
            PageTitle.Text      = AppSettings.S("基础设置", "Basic Settings");
            PageSubtitle.Text   = AppSettings.S("你可以随时在设置页面更改这些选项。", "You can change these later in Settings.");
            DarkModeLabel.Text  = AppSettings.S("深色模式", "Dark Mode");
            DarkModeDesc.Text   = AppSettings.S("切换深色/浅色界面", "Switch between dark and light theme");
            LangLabel.Text      = AppSettings.S("语言 / Language", "Language");
            LangDesc.Text       = AppSettings.S("切换中文/英文界面", "Switch between Chinese and English");
            NextBtn.Label       = AppSettings.S("下一步", "Next");
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsDark = DarkModeToggle.IsOn;
            AppSettings.ApplyTheme(RootGrid, this);
        }

        private void LangToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppSettings.IsEnglish = LangToggle.IsOn;
            ApplyLanguage();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobePage2));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(OobePage1));
        }
    }
}
