using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class OobeCharaPage : Page
    {
        public OobeCharaPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            ApplyLanguage();
            UpdateImportedSummary();
        }

        private void ApplyLanguage()
        {
            PageTitle.Text    = AppSettings.S("角色卡", "Character Card");
            PageStepHint.Text = AppSettings.S("步骤 4 / 8  ·  可跳过", "Step 4 / 8  ·  Optional");
            DescText.Text = AppSettings.S("添加一张角色卡开始角色扮演对话，也可以跳过直接使用普通对话模式。",
                "Add a character card for roleplay, or skip to use normal chat mode.");
            NewCardText.Text  = AppSettings.S("新建角色卡", "New Character Card");
            NewCardDesc.Text  = AppSettings.S("使用向导逐步填写", "Use the wizard to create one");
            ImportCardText.Text = AppSettings.S("导入角色卡", "Import Character Card");
            ImportCardDesc.Text = AppSettings.S("支持 .json 和 .png 格式", "Supports .json and .png files");
            NetCardText.Text  = AppSettings.S("从角色广场导入", "Browse Character Plaza");
            NetCardDesc.Text  = AppSettings.S("浏览和下载在线角色卡", "Browse and download cards online");
            SkipBtn.Label   = AppSettings.S("跳过", "Skip");
            StepLabel.Text    = AppSettings.S("4/8", "4/8");
        }

        private void UpdateImportedSummary()
        {
            int count = DataManager.Data.CharacterCards.Count;
            if (count > 0)
                ImportedSummary.Text = AppSettings.S($"已导入 {count} 张角色卡", $"{count} card(s) imported");
            else
                ImportedSummary.Text = "";
        }

        private void NewCardBtn_Click(object sender, RoutedEventArgs e)
        {
            CharaWizardData.Reset();
            CharaWizardData.ReturnTarget = "OobeCharaPage";
            Frame.Navigate(typeof(CharaWizardPage1));
        }

        private async void ImportCardBtn_Click(object sender, RoutedEventArgs e)
        {
            var files = await ImportExportPage.PickFiles(new[] { ".json", ".png" });
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    CharacterCard card = null;
                    if (file.FileType.ToLower() == ".png")
                    {
                        var bytes = (await Windows.Storage.FileIO.ReadBufferAsync(file)).ToArray();
                        card = ImportExportPage.ParseCharaPng(bytes);
                    }
                    else
                    {
                        string json = await Windows.Storage.FileIO.ReadTextAsync(file);
                        card = ImportExportPage.ParseCharaJson(json);
                    }
                    if (card != null) { DataManager.Data.CharacterCards.Add(card); ok++; }
                }
                catch { }
            }
            if (ok > 0)
            {
                await DataManager.SaveAsync();
                UpdateImportedSummary();
            }
        }

        private void NetCardBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(NetworkCharaPage));

        private void SkipBtn_Click(object sender, RoutedEventArgs e)
            => Frame.Navigate(typeof(OobeUserPage));

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
