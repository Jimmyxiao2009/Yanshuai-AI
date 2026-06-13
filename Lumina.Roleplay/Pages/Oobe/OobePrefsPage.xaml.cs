using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobePrefsPage : Page
    {
        public OobePrefsPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            // Startup behavior: 0=resume, 1=new
            int startup = AppSettings.StartupBehavior;
            StartupResume.IsChecked = startup == 0;
            StartupNew.IsChecked    = startup == 1;

            // Enter behavior: 0=send, 1=newline
            EnterSend.IsChecked    = AppSettings.EnterBehavior == 0;
            EnterNewline.IsChecked = AppSettings.EnterBehavior == 1;

            CharaBgToggle.IsOn = AppSettings.UseCharaIllustrationAsBg;
            SoundToggle.IsOn   = AppSettings.ReplySoundEnabled;

            SourceChub.IsOn   = AppSettings.SourceEnabled_chub;
            SourceHuayu.IsOn  = AppSettings.SourceEnabled_huayu;
            SourceXingye.IsOn = AppSettings.SourceEnabled_xingye;
            SourceQuack.IsOn  = AppSettings.SourceEnabled_quack;
            SourceDzmm.IsOn   = AppSettings.SourceEnabled_dzmm;

            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text    = AppSettings.S("偏好设置", "Preferences");
            PageStepHint.Text = AppSettings.S("步骤 6 / 8  ·  可跳过", "Step 6 / 8  ·  Optional");
            StartupLabel.Text = AppSettings.S("启动页", "Startup Page");
            StartupResume.Content = AppSettings.S("继续上次对话", "Resume last conversation");
            StartupNew.Content    = AppSettings.S("新建对话", "New conversation");
            EnterLabel.Text  = AppSettings.S("回车键行为", "Enter Key Behavior");
            EnterSend.Content    = AppSettings.S("回车发送", "Enter to send");
            EnterNewline.Content = AppSettings.S("回车换行，Ctrl+回车发送", "Enter = newline, Ctrl+Enter = send");
            CharaBgLabel.Text = AppSettings.S("角色立绘作为对话背景", "Character illustration as chat background");
            SoundLabel.Text   = AppSettings.S("回复音效", "Reply sound");
            SourceLabel.Text  = AppSettings.S("角色广场平台", "Character Plaza Sources");
            SkipBtn.Label   = AppSettings.S("跳过", "Skip");
            NextBtn.Label   = AppSettings.S("下一步", "Next");
            StepLabel.Text    = AppSettings.S("6/8", "6/8");
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            // Startup behavior
            if (StartupResume.IsChecked == true) AppSettings.StartupBehavior = 0;
            else if (StartupNew.IsChecked == true) AppSettings.StartupBehavior = 1;

            // Enter behavior
            AppSettings.EnterBehavior = EnterSend.IsChecked == true ? 0 : 1;

            AppSettings.UseCharaIllustrationAsBg = CharaBgToggle.IsOn;
            AppSettings.ReplySoundEnabled = SoundToggle.IsOn;

            AppSettings.SourceEnabled_chub   = SourceChub.IsOn;
            AppSettings.SourceEnabled_huayu  = SourceHuayu.IsOn;
            AppSettings.SourceEnabled_xingye = SourceXingye.IsOn;
            AppSettings.SourceEnabled_quack  = SourceQuack.IsOn;
            AppSettings.SourceEnabled_dzmm   = SourceDzmm.IsOn;

            Frame.Navigate(typeof(OobeDonePage));
        }

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeDonePage));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
