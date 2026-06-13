using System.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeDonePage : Page
    {
        public OobeDonePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            BuildSummary();

            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            DoneTitle.Text = AppSettings.S("设置完成", "Setup Complete");
            HintText.Text  = AppSettings.S("随时可以在设置里修改这些配置。",
                "You can change these settings anytime.");
            FinishBtn.Label = AppSettings.S("开始使用", "Get Started");
        }

        private void BuildSummary()
        {
            SummaryPanel.Children.Clear();

            AddSummaryItem(AppSettings.S("✓ 主题", "✓ Theme"),
                AppSettings.S(ThemeDisplayName(AppSettings.ThemeName), AppSettings.ThemeName));

            var profiles = DataManager.Data.ApiProfiles;
            if (profiles.Count > 0)
                AddSummaryItem(AppSettings.S("✓ 主 API", "✓ Main API"),
                    $"{profiles[0].Name}  ({profiles[0].Model})");
            else
                AddSummaryItem(AppSettings.S("✗ 主 API", "✗ Main API"),
                    AppSettings.S("未配置", "Not configured"));

            if (AppSettings.UseSubAgent && !string.IsNullOrEmpty(AppSettings.SubAgentUrl))
                AddSummaryItem(AppSettings.S("✓ 子智能体", "✓ Sub-Agent"),
                    AppSettings.SubAgentModel);
            else
                AddSummaryItem(AppSettings.S("— 子智能体", "— Sub-Agent"),
                    AppSettings.S("复用主 API", "Reuses main API"));

            int cardCount = DataManager.Data.CharacterCards.Count;
            AddSummaryItem(AppSettings.S("✓ 角色卡", "✓ Cards"),
                AppSettings.S($"已导入 {cardCount} 个", $"{cardCount} imported"));

            var up = DataManager.Data.UserProfile;
            if (!string.IsNullOrEmpty(up?.Name))
                AddSummaryItem(AppSettings.S("✓ 用户档案", "✓ Profile"), up.Name);
            else
                AddSummaryItem(AppSettings.S("— 用户档案", "— Profile"),
                    AppSettings.S("未设置", "Not set"));

            string startupText;
            int s = AppSettings.StartupBehavior;
            if (s == 0) startupText = AppSettings.S("继续上次对话", "Resume last");
            else startupText = AppSettings.S("新建对话", "New chat");
            AddSummaryItem(AppSettings.S("✓ 启动页", "✓ Startup"), startupText);
        }

        private void AddSummaryItem(string label, string value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                }
            };
            grid.AddTextBlock(0, label, 15, Windows.UI.Text.FontWeights.SemiBold);
            var valueTb = grid.AddTextBlock(1, value, 14, Windows.UI.Text.FontWeights.Normal);
            valueTb.Opacity = 0.65;
            valueTb.TextAlignment = TextAlignment.Right;

            SummaryPanel.Children.Add(grid);
        }

        private void FinishBtn_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.OobeCompleted = true;
            Frame.Navigate(typeof(ShellPage));
        }

        private static string ThemeDisplayName(string name)
        {
            if (name == "Celadon") return "青瓷雅韵";
            if (name == "MidnightVermilion") return "朱砂深夜";
            return "墨韵书卷";
        }
    }

    // Extension helper for building summary items
    internal static class GridExtensions
    {
        public static TextBlock AddTextBlock(this Grid grid, int column, string text,
            double fontSize, Windows.UI.Text.FontWeight weight)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, column);
            grid.Children.Add(tb);
            return tb;
        }
    }
}
