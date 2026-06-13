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

                    if (convsJson  != null) DataManager.Data.Conversations    = FromJson<List<Conversation>>(convsJson);
                    if (charasJson != null) DataManager.Data.CharacterCards   = FromJson<List<CharacterCard>>(charasJson);
                    if (worldJson  != null) DataManager.Data.WorldBookEntries = FromJson<List<WorldBookEntry>>(worldJson);
                    if (apiJson    != null) DataManager.Data.ApiProfiles      = FromJson<List<ApiProfile>>(apiJson);
                    if (profileJson!= null) DataManager.Data.UserProfile      = FromJson<UserProfile>(profileJson);
                }
                await DataManager.SaveAsync();
                return true;
            }
            catch { return false; }
        }

        private async void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.SuggestedFileName = $"lumina_backup_{DateTime.Now:yyyyMMdd_HHmm}";
            picker.FileTypeChoices.Add("ZIP备份", new List<string> { ".zip" });
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            bool ok = await ExportAllAsZip(file);
            ZipStatus.Text = ok ? $"✓ 备份已导出：{file.Name}" : "✗ 导出失败";
        }

        private async void ImportZip_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".zip" });
            if (files == null || files.Count == 0) return;
            bool ok = await ImportFromZip(files[0]);
            ZipStatus.Text = ok ? "✓ 已从备份恢复，请重启应用以刷新数据" : "✗ 恢复失败，文件可能损坏";
        }
    }
}
