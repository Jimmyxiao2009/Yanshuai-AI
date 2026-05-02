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
    // ── SillyTavern V2 character card JSON types ──────────────────────────────

    [DataContract] internal class StCharaV2
    {
        [DataMember(Name = "spec")]         public string Spec        { get; set; } = "chara_card_v2";
        [DataMember(Name = "spec_version")] public string SpecVersion { get; set; } = "2.0";
        [DataMember(Name = "data")]         public StCharaData Data   { get; set; }
    }

    [DataContract] internal class StCharaData
    {
        [DataMember(Name = "name")]                      public string Name        { get; set; } = "";
        [DataMember(Name = "description")]               public string Description { get; set; } = "";
        [DataMember(Name = "personality")]               public string Personality { get; set; } = "";
        [DataMember(Name = "scenario")]                  public string Scenario    { get; set; } = "";
        [DataMember(Name = "first_mes")]                 public string FirstMes    { get; set; } = "";
        [DataMember(Name = "mes_example")]               public string MesExample  { get; set; } = "";
        [DataMember(Name = "creator_notes")]             public string CreatorNotes { get; set; } = "";
        [DataMember(Name = "system_prompt")]             public string SystemPrompt { get; set; } = "";
        [DataMember(Name = "post_history_instructions")] public string PostHistory  { get; set; } = "";
        [DataMember(Name = "tags")]                      public List<string> Tags   { get; set; } = new List<string>();
        [DataMember(Name = "creator")]                   public string Creator      { get; set; } = "";
        [DataMember(Name = "character_version")]         public string Version      { get; set; } = "";
    }

    // ── SillyTavern world book JSON types ─────────────────────────────────────

    [DataContract] internal class StWorldBook
    {
        [DataMember(Name = "name")]    public string Name    { get; set; } = "世界书";
        [DataMember(Name = "entries")] public Dictionary<string, StWorldEntry> Entries { get; set; }
            = new Dictionary<string, StWorldEntry>();
    }

    [DataContract] internal class StWorldEntry
    {
        [DataMember(Name = "uid")]      public int    Uid      { get; set; }
        [DataMember(Name = "key")]      public List<string> Key { get; set; } = new List<string>();
        [DataMember(Name = "comment")]  public string Comment  { get; set; } = "";
        [DataMember(Name = "content")]  public string Content  { get; set; } = "";
        [DataMember(Name = "constant")] public bool   Constant { get; set; }
        [DataMember(Name = "selective")]public bool   Selective{ get; set; }
        [DataMember(Name = "order")]    public int    Order    { get; set; } = 100;
        [DataMember(Name = "disable")]  public bool   Disable  { get; set; }
    }

    // ── ST chat JSONL types ───────────────────────────────────────────────────

    [DataContract] internal class StChatMeta
    {
        [DataMember(Name = "user_name")]      public string UserName      { get; set; } = "用户";
        [DataMember(Name = "character_name")] public string CharacterName { get; set; } = "AI";
        [DataMember(Name = "create_date")]    public string CreateDate    { get; set; } = "";
        [DataMember(Name = "chat_metadata")]  public object ChatMetadata  { get; set; } = new object();
    }

    [DataContract] internal class StChatMessage
    {
        [DataMember(Name = "name")]      public string Name     { get; set; }
        [DataMember(Name = "is_user")]   public bool   IsUser   { get; set; }
        [DataMember(Name = "send_date")] public string SendDate { get; set; } = "";
        [DataMember(Name = "mes")]       public string Mes      { get; set; } = "";
        [DataMember(Name = "swipes")]    public List<string> Swipes  { get; set; }
        [DataMember(Name = "swipe_id")]  public int   SwipeId  { get; set; }

        public string ResolvedContent
        {
            get
            {
                if (Swipes != null && Swipes.Count > 0)
                {
                    int idx = SwipeId >= 0 && SwipeId < Swipes.Count ? SwipeId : 0;
                    var sw = Swipes[idx];
                    if (!string.IsNullOrEmpty(sw)) return sw;
                }
                return Mes ?? "";
            }
        }
    }

    // ── Yanshu API profile export types ──────────────────────────────────────

    [DataContract] internal class YanshuApiExport
    {
        [DataMember(Name = "format")]   public string Format   { get; set; } = "yanshu_api_profiles";
        [DataMember(Name = "version")]  public string Version  { get; set; } = "1.0";
        [DataMember(Name = "profiles")] public List<YanshuApiEntry> Profiles { get; set; }
            = new List<YanshuApiEntry>();
    }

    [DataContract] internal class YanshuApiEntry
    {
        [DataMember(Name = "name")]    public string Name   { get; set; }
        [DataMember(Name = "url")]     public string Url    { get; set; }
        [DataMember(Name = "api_key")] public string ApiKey { get; set; }
        [DataMember(Name = "model")]   public string Model  { get; set; }
    }

    // ── Search settings export ────────────────────────────────────────────────

    [DataContract] internal class SearchSettingsExport
    {
        [DataMember(Name = "provider")]     public int    Provider    { get; set; }
        [DataMember(Name = "api_key")]      public string ApiKey      { get; set; } = "";
        [DataMember(Name = "base_url")]     public string BaseUrl     { get; set; } = "";
        [DataMember(Name = "result_depth")] public int    ResultDepth { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Page
    // ═════════════════════════════════════════════════════════════════════════

    public sealed partial class ImportExportPage : Page
    {
        public ImportExportPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
        }

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

        // ── Character card export ─────────────────────────────────────────────

        private async void ExportChara_Click(object sender, RoutedEventArgs e)
        {
            var cards = DataManager.Data.CharacterCards;
            if (cards == null || cards.Count == 0) { CharaStatus.Text = "⚠ 没有角色卡"; return; }

            var folder = await PickFolder();
            if (folder == null) return;

            int ok = 0;
            foreach (var card in cards)
            {
                try
                {
                    string json = BuildStCharaJson(card);
                    string safe = MakeSafeFilename(card.Name);

                    if (card.HasAvatar)
                    {
                        // 嵌入 PNG tEXt chunk 导出（ST 标准格式）
                        byte[] pngBytes = Convert.FromBase64String(card.AvatarBase64);
                        byte[] output   = InjectPngTextChunk(pngBytes, "chara",
                                              Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
                        var file = await folder.CreateFileAsync(safe + ".png", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteBytesAsync(file, output);
                    }
                    else
                    {
                        var file = await folder.CreateFileAsync(safe + ".json", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    }
                    ok++;
                }
                catch { }
            }
            CharaStatus.Text = $"✓ 已导出 {ok}/{cards.Count} 张角色卡";
        }

        /// <summary>构建符合 SillyTavern V2 spec 的角色卡 JSON 字符串（手动拼接保证字段顺序和格式）。</summary>
        internal static string BuildStCharaJson(CharacterCard card)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"spec\":\"chara_card_v2\",");
            sb.Append("\"spec_version\":\"2.0\",");
            sb.Append("\"data\":{");
            sb.Append($"\"name\":\"{EscapeJson(card.Name ?? "")}\",");
            sb.Append($"\"description\":\"{EscapeJson(card.Description ?? "")}\",");
            sb.Append($"\"personality\":\"{EscapeJson(card.Personality ?? "")}\",");
            sb.Append($"\"scenario\":\"{EscapeJson(card.Scenario ?? "")}\",");
            sb.Append($"\"first_mes\":\"{EscapeJson(card.FirstMessage ?? "")}\",");
            sb.Append($"\"mes_example\":\"{EscapeJson(card.MesExample ?? "")}\",");
            sb.Append($"\"creator_notes\":\"{EscapeJson(card.CreatorNotes ?? "")}\",");
            sb.Append($"\"system_prompt\":\"{EscapeJson(card.SystemPrompt ?? "")}\",");
            sb.Append($"\"post_history_instructions\":\"{EscapeJson(card.PostHistoryInstructions ?? "")}\",");
            // tags: split comma-separated string back to array
            var tagList = (card.Tags ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            sb.Append("\"tags\":[" + string.Join(",", tagList.Select(t => "\"" + EscapeJson(t) + "\"")) + "],");
            sb.Append($"\"creator\":\"{EscapeJson(card.Creator ?? "")}\",");
            sb.Append($"\"character_version\":\"{EscapeJson(card.CharacterVersion ?? "")}\",");
            sb.Append("\"extensions\":{}");
            sb.Append("}");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 在 PNG 的 IEND chunk 之前插入一个 tEXt chunk（keyword\0text）。
        /// 用于嵌入 SillyTavern 角色卡数据（keyword = "chara"）。
        /// </summary>
        internal static byte[] InjectPngTextChunk(byte[] png, string keyword, string text)
        {
            // tEXt chunk data = keyword bytes + 0x00 + text bytes (Latin-1)
            byte[] kwBytes   = Encoding.ASCII.GetBytes(keyword);
            byte[] textBytes = Encoding.GetEncoding(1252).GetBytes(text);
            byte[] data      = new byte[kwBytes.Length + 1 + textBytes.Length];
            kwBytes.CopyTo(data, 0);
            data[kwBytes.Length] = 0;
            textBytes.CopyTo(data, kwBytes.Length + 1);

            // chunk = 4-byte length + "tEXt" + data + 4-byte CRC
            byte[] typeBytes = Encoding.ASCII.GetBytes("tEXt");
            uint crc = PngCrc32(typeBytes, data);
            int chunkLen = 12 + data.Length; // 4+4+data+4
            byte[] chunk = new byte[chunkLen];
            int p = 0;
            // length (big-endian)
            chunk[p++] = (byte)(data.Length >> 24);
            chunk[p++] = (byte)(data.Length >> 16);
            chunk[p++] = (byte)(data.Length >> 8);
            chunk[p++] = (byte)(data.Length);
            // type
            typeBytes.CopyTo(chunk, p); p += 4;
            // data
            data.CopyTo(chunk, p); p += data.Length;
            // CRC
            chunk[p++] = (byte)(crc >> 24);
            chunk[p++] = (byte)(crc >> 16);
            chunk[p++] = (byte)(crc >> 8);
            chunk[p]   = (byte)(crc);

            // Find IEND offset in original PNG
            int iend = png.Length - 12; // IEND is always the last 12 bytes
            // Build output: everything before IEND + tEXt chunk + IEND
            byte[] output = new byte[png.Length + chunkLen];
            Array.Copy(png, 0,    output, 0,              iend);
            Array.Copy(chunk, 0,  output, iend,           chunkLen);
            Array.Copy(png, iend, output, iend + chunkLen, png.Length - iend);
            return output;
        }

        private static uint PngCrc32(byte[] type, byte[] data)
        {
            // Standard CRC-32 used by PNG
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte b in type) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (byte b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        // ── Character card import ─────────────────────────────────────────────

        private async void ImportChara_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".json", ".png" });
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    CharacterCard card = null;
                    if (file.FileType.ToLower() == ".png")
                    {
                        var bytes = (await FileIO.ReadBufferAsync(file)).ToArray();
                        card = await ParseCharaPng(bytes);
                    }
                    else
                    {
                        string json = await FileIO.ReadTextAsync(file);
                        card = ParseCharaJson(json);
                    }
                    if (card != null) { DataManager.Data.CharacterCards.Add(card); ok++; }
                }
                catch { }
            }
            await DataManager.SaveAsync();
            CharaStatus.Text = $"✓ 导入了 {ok}/{files.Count} 张角色卡";
        }

        // ── PNG角色卡解析（读tEXt chunk里的chara字段）────────────────────────
        internal static async Task<CharacterCard> ParseCharaPng(byte[] pngBytes)
        {
            // PNG tEXt chunk格式: 4字节长度 + 4字节"tEXt" + keyword text + 4字节CRC
            // 找所有tEXt chunk，找keyword=="chara"的
            int pos = 8; // 跳过PNG signature
            while (pos + 12 <= pngBytes.Length)
            {
                int len = (pngBytes[pos] << 24) | (pngBytes[pos+1] << 16) | (pngBytes[pos+2] << 8) | pngBytes[pos+3];
                string type = System.Text.Encoding.ASCII.GetString(pngBytes, pos + 4, 4);
                if (type == "tEXt" && len > 0 && pos + 8 + len <= pngBytes.Length)
                {
                    // keyword是null结尾的ASCII字符串
                    int kEnd = pos + 8;
                    while (kEnd < pos + 8 + len && pngBytes[kEnd] != 0) kEnd++;
                    string keyword = System.Text.Encoding.ASCII.GetString(pngBytes, pos + 8, kEnd - (pos + 8));
                    if (keyword == "chara" && kEnd + 1 < pos + 8 + len)
                    {
                        string b64 = System.Text.Encoding.GetEncoding(1252).GetString(pngBytes, kEnd + 1, pos + 8 + len - kEnd - 1);
                        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64.Trim()));
                        var card = ParseCharaJson(json);
                        if (card != null)
                        {
                            // 把PNG本身存为头像
                            card.AvatarBase64   = Convert.ToBase64String(pngBytes);
                            card.AvatarMimeType  = "image/png";
                            // 也存为立绘
                            card.IllustrationBase64   = card.AvatarBase64;
                            card.IllustrationMimeType = "image/png";
                        }
                        return card;
                    }
                }
                if (type == "IEND") break;
                pos += 12 + len; // 4len + 4type + len + 4crc
            }
            return null;
        }

        internal static CharacterCard ParseCharaJson(string json)
        {
            // Try V2 first
            try
            {
                var v2 = FromJson<StCharaV2>(json);
                if (v2?.Spec == "chara_card_v2" && v2.Data != null)
                {
                    return new CharacterCard
                    {
                        Name                    = v2.Data.Name        ?? "",
                        Description             = v2.Data.Description ?? "",
                        Personality             = v2.Data.Personality ?? "",
                        Scenario                = v2.Data.Scenario    ?? "",
                        FirstMessage            = v2.Data.FirstMes    ?? "",
                        SystemPrompt            = v2.Data.SystemPrompt ?? "",
                        PostHistoryInstructions = v2.Data.PostHistory  ?? "",
                        MesExample              = v2.Data.MesExample   ?? "",
                        CreatorNotes            = v2.Data.CreatorNotes ?? "",
                        Tags                    = v2.Data.Tags != null ? string.Join(", ", v2.Data.Tags) : "",
                        Creator                 = v2.Data.Creator  ?? "",
                        CharacterVersion        = v2.Data.Version   ?? "",
                    };
                }
            }
            catch { }

            // Try V1 / flat JSON
            try
            {
                var v1 = FromJson<StCharaData>(json);
                if (!string.IsNullOrEmpty(v1?.Name))
                {
                    return new CharacterCard
                    {
                        Name                    = v1.Name         ?? "",
                        Description             = v1.Description  ?? "",
                        Personality             = v1.Personality  ?? "",
                        Scenario                = v1.Scenario     ?? "",
                        FirstMessage            = v1.FirstMes     ?? "",
                        SystemPrompt            = v1.SystemPrompt ?? "",
                        PostHistoryInstructions = v1.PostHistory  ?? "",
                        MesExample              = v1.MesExample   ?? "",
                        CreatorNotes            = v1.CreatorNotes ?? "",
                        Tags                    = v1.Tags != null ? string.Join(", ", v1.Tags) : "",
                        Creator                 = v1.Creator ?? "",
                        CharacterVersion        = v1.Version  ?? "",
                    };
                }
            }
            catch { }
            return null;
        }

        internal static StCharaV2 CardToSt(CharacterCard card) => new StCharaV2
        {
            Data = new StCharaData
            {
                Name        = card.Name        ?? "",
                Description = card.Description ?? "",
                Personality = card.Personality ?? "",
                Scenario    = card.Scenario    ?? "",
                FirstMes    = card.FirstMessage ?? "",
            }
        };

        // ── World book export ─────────────────────────────────────────────────

        private async void ExportWorld_Click(object sender, RoutedEventArgs e)
        {
            var entries = DataManager.Data.WorldBookEntries;
            if (entries == null || entries.Count == 0) { WorldStatus.Text = "⚠ 没有世界书条目"; return; }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "yanshu_worldbook"
            };
            picker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                var wb = new StWorldBook { Name = "言枢世界书" };
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var ste = new StWorldEntry
                    {
                        Uid      = i,
                        Comment  = entry.Name    ?? "",
                        Content  = entry.Content ?? "",
                        Constant = entry.AlwaysActive,
                        Key      = (entry.Keywords ?? "")
                                    .Split(',')
                                    .Select(k => k.Trim())
                                    .Where(k => k.Length > 0)
                                    .ToList(),
                        Order    = 100,
                    };
                    wb.Entries[i.ToString()] = ste;
                }
                await FileIO.WriteTextAsync(file, ToJson(wb), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                WorldStatus.Text = $"✓ 已导出 {entries.Count} 条世界书条目";
            }
            catch (Exception ex) { WorldStatus.Text = "✗ 导出失败：" + ex.Message; }
        }

        // ── World book import ─────────────────────────────────────────────────

        private async void ImportWorld_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".json" });
            if (files == null || files.Count == 0) return;

            int total = 0;
            foreach (var file in files)
            {
                try
                {
                    string json = await FileIO.ReadTextAsync(file);
                    var wb = FromJson<StWorldBook>(json);
                    if (wb?.Entries == null) continue;
                    foreach (var kv in wb.Entries)
                    {
                        var ste = kv.Value;
                        if (ste == null) continue;
                        DataManager.Data.WorldBookEntries.Add(new WorldBookEntry
                        {
                            Name         = ste.Comment ?? "",
                            Content      = ste.Content ?? "",
                            AlwaysActive = ste.Constant,
                            Keywords     = string.Join(", ", ste.Key ?? new List<string>()),
                        });
                        total++;
                    }
                }
                catch { }
            }
            await DataManager.SaveAsync();
            WorldStatus.Text = $"✓ 导入了 {total} 条世界书条目";
        }

        // ── API export ────────────────────────────────────────────────────────

        private async void ExportApi_Click(object sender, RoutedEventArgs e)
        {
            var profiles = DataManager.Data.ApiProfiles;
            if (profiles == null || profiles.Count == 0) { ApiStatus.Text = "⚠ 没有 API 配置"; return; }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "yanshu_api_profiles"
            };
            picker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                var export = new YanshuApiExport();
                foreach (var p in profiles)
                    export.Profiles.Add(new YanshuApiEntry
                    {
                        Name = p.Name, Url = p.Url, ApiKey = p.ApiKey, Model = p.Model
                    });
                await FileIO.WriteTextAsync(file, ToJson(export), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                ApiStatus.Text = $"✓ 已导出 {profiles.Count} 个 API 配置";
            }
            catch (Exception ex) { ApiStatus.Text = "✗ 导出失败：" + ex.Message; }
        }

        // ── API import ────────────────────────────────────────────────────────

        private async void ImportApi_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".json" });
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    string json = await FileIO.ReadTextAsync(file);
                    var export = FromJson<YanshuApiExport>(json);
                    if (export?.Format != "yanshu_api_profiles") continue;
                    foreach (var entry in export.Profiles ?? new List<YanshuApiEntry>())
                    {
                        DataManager.Data.ApiProfiles.Add(new ApiProfile
                        {
                            Name = entry.Name ?? "导入的配置",
                            Url  = entry.Url  ?? "",
                            ApiKey = entry.ApiKey ?? "",
                            Model  = entry.Model  ?? "",
                        });
                        ok++;
                    }
                }
                catch { }
            }
            await DataManager.SaveAsync();
            ApiStatus.Text = $"✓ 导入了 {ok} 个 API 配置";
        }

        // ── File picker helpers ───────────────────────────────────────────────

        internal static async Task<IReadOnlyList<StorageFile>> PickFiles(string[] extensions)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
            return await picker.PickMultipleFilesAsync();
        }

        internal static async Task<StorageFolder> PickFolder()
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            return await picker.PickSingleFolderAsync();
        }

        // ── JSON helpers ──────────────────────────────────────────────────────

        internal static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            var r = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if      (c == '\\') r.Append("\\\\");
                else if (c == '"')  r.Append("\\\"");
                else if (c == '\n') r.Append("\\n");
                else if (c == '\r') r.Append("\\r");
                else if (c == '\t') r.Append("\\t");
                else                r.Append(c);
            }
            return r.ToString();
        }

        internal static string ToJson<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                ser.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        internal static T FromJson<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                return (T)ser.ReadObject(ms);
            }
        }

        internal static string MakeSafeFilename(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "未命名";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "_");
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }

        // ── ZIP全局备份 ───────────────────────────────────────────────────────

        internal static async Task<bool> ExportAllAsZip(StorageFile zipFile)
        {
            try
            {
                var data = DataManager.Data;
                // 序列化各部分
                string convsJson   = ToJson(data.Conversations   ?? new List<Conversation>());
                string charasJson  = ToJson(data.CharacterCards  ?? new List<CharacterCard>());
                string worldJson   = ToJson(data.WorldBookEntries ?? new List<WorldBookEntry>());
                string apiJson     = ToJson(data.ApiProfiles     ?? new List<ApiProfile>());
                string profileJson = ToJson(data.UserProfile     ?? new UserProfile());
                // 搜索配置
                var searchSettings = new SearchSettingsExport
                {
                    Provider      = AppSettings.SearchProvider,
                    ApiKey        = AppSettings.SearchApiKey,
                    BaseUrl       = AppSettings.SearchBaseUrl,
                    ResultDepth   = AppSettings.SearchResultDepth,
                };
                string searchJson  = ToJson(searchSettings);

                using (var stream = await zipFile.OpenStreamForWriteAsync())
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    void AddEntry(string name, string json)
                    {
                        var entry = archive.CreateEntry(name);
                        using (var w = new StreamWriter(entry.Open(), Encoding.UTF8))
                            w.Write(json);
                    }
                    AddEntry("conversations.json",   convsJson);
                    AddEntry("characters.json",      charasJson);
                    AddEntry("worldbook.json",       worldJson);
                    AddEntry("api_profiles.json",    apiJson);
                    AddEntry("user_profile.json",    profileJson);
                    AddEntry("search_settings.json", searchJson);
                }
                return true;
            }
            catch { return false; }
        }

        internal static async Task<bool> ImportFromZip(StorageFile zipFile)
        {
            try
            {
                using (var stream = await zipFile.OpenStreamForReadAsync())
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string Read(string name)
                    {
                        var entry = archive.GetEntry(name);
                        if (entry == null) return null;
                        using (var r = new StreamReader(entry.Open(), Encoding.UTF8))
                            return r.ReadToEnd();
                    }

                    var convsJson   = Read("conversations.json");
                    var charasJson  = Read("characters.json");
                    var worldJson   = Read("worldbook.json");
                    var apiJson     = Read("api_profiles.json");
                    var profileJson = Read("user_profile.json");
                    var searchJson  = Read("search_settings.json");

                    if (convsJson  != null) DataManager.Data.Conversations    = FromJson<List<Conversation>>(convsJson);
                    if (charasJson != null) DataManager.Data.CharacterCards   = FromJson<List<CharacterCard>>(charasJson);
                    if (worldJson  != null) DataManager.Data.WorldBookEntries = FromJson<List<WorldBookEntry>>(worldJson);
                    if (apiJson    != null) DataManager.Data.ApiProfiles      = FromJson<List<ApiProfile>>(apiJson);
                    if (profileJson!= null) DataManager.Data.UserProfile      = FromJson<UserProfile>(profileJson);
                    if (searchJson != null)
                    {
                        var ss = FromJson<SearchSettingsExport>(searchJson);
                        if (ss != null)
                        {
                            AppSettings.SearchProvider   = ss.Provider;
                            AppSettings.SearchApiKey     = ss.ApiKey    ?? "";
                            AppSettings.SearchBaseUrl    = ss.BaseUrl   ?? "";
                            AppSettings.SearchResultDepth = ss.ResultDepth;
                        }
                    }
                }
                await DataManager.SaveAsync();
                return true;
            }
            catch { return false; }
        }

        private async void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.SuggestedFileName = $"yanshuai_backup_{DateTime.Now:yyyyMMdd_HHmm}";
            picker.FileTypeChoices.Add("ZIP备份", new List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            bool ok = await ExportAllAsZip(file);
        }

        private async void ImportZip_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportFromZip(files[0]);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }
    }
}
