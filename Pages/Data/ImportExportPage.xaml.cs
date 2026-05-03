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

        internal static async Task<bool> ImportConvFromText(string text, string displayName)
        {
            // ── 1. 解析对话 ──────────────────────────────────────────────────
            string detectedUserName = "";
            var firstLine = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault() ?? "";
            try
            {
                var meta = FromJson<StChatMeta>(firstLine);
                detectedUserName = meta?.UserName ?? "";
            }
            catch { }

            var conv = ParseConvJsonl(text, displayName);
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

            // ── 2. 弹窗：选 User Profile ────────────────────────────────────
            var profiles = DataManager.Data.UserProfiles;

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

            if (!string.IsNullOrEmpty(detectedUserName))
            {
                var hint = new TextBlock
                {
                    FontSize = 13, Opacity = 0.6, TextWrapping = TextWrapping.Wrap,
                    Text = $"文件中检测到用户「{detectedUserName}」",
                    Margin = new Windows.UI.Xaml.Thickness(0, 0, 0, 10),
                };
                panel.Children.Add(hint);
            }

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
            int profIdx = profileCombo.SelectedIndex - 1;

            if (profIdx >= 0 && profIdx < profiles.Count)
            {
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
                string convsJson   = ToJson(data.Conversations   ?? new List<Conversation>());
                string apiJson     = ToJson(data.ApiProfiles     ?? new List<ApiProfile>());
                string profileJson = ToJson(data.UserProfile     ?? new UserProfile());

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
                    var apiJson     = Read("api_profiles.json");
                    var profileJson = Read("user_profile.json");
                    var searchJson  = Read("search_settings.json");

                    if (convsJson  != null) DataManager.Data.Conversations    = FromJson<List<Conversation>>(convsJson);
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
