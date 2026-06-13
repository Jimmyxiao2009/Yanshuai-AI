using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;


namespace yanshuai
{
    public sealed partial class ImportExportPage : Page
    {
        // ── Conversation export ───────────────────────────────────────────────

        private async void ExportCurrentConv_Click(object sender, RoutedEventArgs e)
        {
            var conv = AppState.ActiveConversation;
            if (conv == null) { ConvStatus.Text = "⚠ 没有活动对话"; return; }
            await SaveConvAsJsonl(conv);
        }

        private async void ExportAllConv_Click(object sender, RoutedEventArgs e)
        {
            var convs = DataManager.Data.Conversations;
            if (convs == null || convs.Count == 0) { ConvStatus.Text = "⚠ 没有对话可导出"; return; }

            var folder = await PickFolder();
            if (folder == null) return;

            int ok = 0;
            foreach (var conv in convs)
            {
                try
                {
                    string safe = MakeSafeFilename(conv.Title) + ".jsonl";
                    var file = await folder.CreateFileAsync(safe, CreationCollisionOption.GenerateUniqueName);
                    await FileIO.WriteTextAsync(file, BuildConvJsonl(conv), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    ok++;
                }
                catch { }
            }
            ConvStatus.Text = $"✓ 已导出 {ok}/{convs.Count} 条对话";
        }

        private async Task SaveConvAsJsonl(Conversation conv)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = MakeSafeFilename(conv.Title)
            };
            picker.FileTypeChoices.Add("JSONL 文件", new List<string> { ".jsonl" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                await FileIO.WriteTextAsync(file, BuildConvJsonl(conv), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                ConvStatus.Text = "✓ 导出成功：" + file.Name;
            }
            catch (Exception ex) { ConvStatus.Text = "✗ 导出失败：" + ex.Message; }
        }

        internal static string BuildConvJsonlPublic(Conversation conv) => BuildConvJsonl(conv);

        private static string BuildConvJsonl(Conversation conv)
        {
            var sb = new StringBuilder();
            var charName = "AI";
            var userName = "用户";
            if (!string.IsNullOrEmpty(conv.CharacterCardId))
            {
                var card = DataManager.Data.CharacterCards.Find(c => c.Id == conv.CharacterCardId);
                if (card != null) charName = card.Name;
            }
            var up = DataManager.GetActiveUserProfile();
            if (!string.IsNullOrEmpty(up?.Name)) userName = up.Name;

            // ST metadata line
            sb.Append("{");
            sb.Append($"\"user_name\":\"{EscapeJson(userName)}\",");
            sb.Append($"\"character_name\":\"{EscapeJson(charName)}\",");
            sb.Append($"\"create_date\":\"{conv.CreatedAt:yyyy-MM-dd @HH:mm:ss}\",");
            sb.Append("\"chat_metadata\":{\"tainted\":false,\"timedWorldInfo\":{}}");
            sb.AppendLine("}");

            foreach (var msg in conv.Messages)
            {
                bool isUser = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
                string name = isUser ? userName : charName;
                sb.Append("{");
                sb.Append($"\"name\":\"{EscapeJson(name)}\",");
                sb.Append($"\"is_user\":{(isUser ? "true" : "false")},");
                sb.Append("\"is_system\":false,");
                sb.Append($"\"send_date\":\"{msg.Timestamp:yyyy-MM-dd @HH:mm:ss}\",");
                sb.Append($"\"mes\":\"{EscapeJson(msg.Content)}\",");
                sb.Append("\"extra\":{\"isSmallSys\":false}");
                sb.AppendLine("}");
            }
            return sb.ToString();
        }

        // ── Conversation import ───────────────────────────────────────────────

        private async void ImportConv_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".jsonl" });
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    string text = await FileIO.ReadTextAsync(file);
                    bool imported = await ImportConvFromText(text, file.DisplayName);
                    if (imported) ok++;
                }
                catch { }
            }
            await DataManager.SaveAsync();
            ConvStatus.Text = $"✓ 导入了 {ok}/{files.Count} 条对话";
        }

        /// <summary>
        /// 解析对话文本，弹出角色卡/Profile 关联窗口，确认后写入数据。
        /// 供 ImportExportPage 和 ConversationsListPage 共用。
        /// 返回是否成功导入。
        /// </summary>
        internal static async Task<bool> ImportConvFromText(string text, string displayName)
        {
            // ── 1. 解析对话 ──────────────────────────────────────────────────
            // 先尝试从 metadata 行提取角色名/用户名
            string detectedCharName = "";
            string detectedUserName = "";
            var firstLine = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault() ?? "";
            try
            {
                var meta = FromJson<StChatMeta>(firstLine);
                detectedCharName = meta?.CharacterName ?? "";
                detectedUserName = meta?.UserName      ?? "";
            }
            catch { }

            var conv = ParseConvJsonl(text, displayName);
            // Fallback: internal ConversationMessage-per-line format
            if (conv == null || conv.Messages.Count == 0)
            {
                conv = new Conversation { Title = System.IO.Path.GetFileNameWithoutExtension(displayName) };
                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var msg = FromJson<ConversationMessage>(line.Trim());
                        if (msg?.Role != null) conv.Messages.Add(msg);
                    }
                    catch { }
                }
            }
            if (conv == null || conv.Messages.Count == 0) return false;

            // ── 2. 弹窗：选角色卡和 User Profile ────────────────────────────
            var cards    = DataManager.Data.CharacterCards;
            var profiles = DataManager.Data.UserProfiles;

            // 角色卡 ComboBox
            var charaCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            charaCombo.Items.Add("— 不关联角色卡 —");
            // 优先选名字匹配的
            int charaPreselect = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                charaCombo.Items.Add(cards[i].Name ?? "");
                if (!string.IsNullOrEmpty(detectedCharName) &&
                    string.Equals(cards[i].Name, detectedCharName, StringComparison.OrdinalIgnoreCase))
                    charaPreselect = i + 1;
            }
            charaCombo.SelectedIndex = charaPreselect;

            // User Profile ComboBox
            var profileCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            profileCombo.Items.Add("— 不关联用户资料 —");
            int profilePreselect = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                profileCombo.Items.Add(profiles[i].Name ?? "（无名）");
                if (!string.IsNullOrEmpty(detectedUserName) &&
                    string.Equals(profiles[i].Name, detectedUserName, StringComparison.OrdinalIgnoreCase))
                    profilePreselect = i + 1;
            }
            profileCombo.SelectedIndex = profilePreselect;

            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(detectedCharName) || !string.IsNullOrEmpty(detectedUserName))
            {
                var hint = new TextBlock
                {
                    FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                    Text = $"文件中检测到：角色「{detectedCharName}」/ 用户「{detectedUserName}」",
                    Margin = new Windows.UI.Xaml.Thickness(0, 0, 0, 10),
                };
                panel.Children.Add(hint);
            }

            panel.Children.Add(new TextBlock { Text = "关联角色卡", FontSize = 13 });
            charaCombo.Margin = new Windows.UI.Xaml.Thickness(0, 4, 0, 10);
            panel.Children.Add(charaCombo);
            panel.Children.Add(new TextBlock { Text = "关联用户资料", FontSize = 13 });
            profileCombo.Margin = new Windows.UI.Xaml.Thickness(0, 4, 0, 0);
            panel.Children.Add(profileCombo);

            var dialog = new ContentDialog
            {
                Title               = $"导入对话：{conv.Title}",
                Content             = panel,
                PrimaryButtonText   = "导入",
                SecondaryButtonText = "取消",
                RequestedTheme      = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

            // ── 3. 写入关联 ──────────────────────────────────────────────────
            int charaIdx = charaCombo.SelectedIndex - 1;   // -1 = 不关联
            int profIdx  = profileCombo.SelectedIndex - 1; // -1 = 不关联

            if (charaIdx >= 0 && charaIdx < cards.Count)
                conv.CharacterCardId = cards[charaIdx].Id;

            if (profIdx >= 0 && profIdx < profiles.Count)
            {
                // 将选中的 profile 设为活跃
                DataManager.Data.ActiveUserProfileId = profiles[profIdx].Id;
            }

            conv.InitBranches();
            DataManager.Data.Conversations.Insert(0, conv);
            return true;
        }

        internal static Conversation ParseConvJsonl(string text, string filename)
        {
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;

            // First line is metadata
            string charName = System.IO.Path.GetFileNameWithoutExtension(filename);
            try
            {
                var meta = FromJson<StChatMeta>(lines[0]);
                if (!string.IsNullOrEmpty(meta?.CharacterName)) charName = meta.CharacterName;
            }
            catch { }

            var conv = new Conversation { Title = charName };
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var msg = FromJson<StChatMessage>(lines[i]);
                    if (msg == null) continue;
                    conv.Messages.Add(new ConversationMessage
                    {
                        Role      = msg.IsUser ? "user" : "assistant",
                        Content   = msg.ResolvedContent,
                        Timestamp = DateTime.TryParse(msg.SendDate, out var dt) ? dt : DateTime.Now
                    });
                }
                catch { }
            }
            if (conv.Messages.Count > 0)
                conv.UpdatedAt = conv.Messages[conv.Messages.Count - 1].Timestamp;
            return conv;
        }

    }
}
