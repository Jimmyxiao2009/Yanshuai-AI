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
                string profilesJson = ToJson(data.UserProfiles   ?? new List<UserProfile>());
                string poolsJson   = ToJson(data.DialoguePools   ?? new List<DialoguePool>());

                var config = new RoleplayConfigExport
                {
                    SelectedApiProfileId = data.SelectedApiProfileId,
                    SelectedCharacterCardId = data.SelectedCharacterCardId,
                    LastActiveConversationId = data.LastActiveConversationId,
                    DefaultApiProfileId = data.DefaultApiProfileId,
                    ActiveUserProfileId = data.ActiveUserProfileId
                };
                string configJson = ToJson(config);

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
                    AddEntry("user_profiles.json",   profilesJson);
                    AddEntry("dialogue_pools.json",  poolsJson);
                    AddEntry("config.json",          configJson);
                }
                return true;
            }
            catch { return false; }
        }

        internal static async Task<bool> ImportFromZip(StorageFile zipFile)
        {
            try
            {
                // 二次确认，防止清空数据
                var dialog = new Windows.UI.Popups.MessageDialog("确定要从备份恢复吗？这会清空并覆盖您当前的所有数据，且不可撤销。", "确认恢复");
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("确定") { Id = 0 });
                dialog.Commands.Add(new Windows.UI.Popups.UICommand("取消") { Id = 1 });
                dialog.DefaultCommandIndex = 1;
                dialog.CancelCommandIndex = 1;
                var confirmResult = await dialog.ShowAsync();
                if ((int)confirmResult.Id != 0) return false;

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

                    var convsJson    = Read("conversations.json");
                    var charasJson   = Read("characters.json");
                    var worldJson    = Read("worldbook.json");
                    var apiJson      = Read("api_profiles.json");
                    var profileJson  = Read("user_profile.json");
                    var profilesJson = Read("user_profiles.json");
                    var poolsJson    = Read("dialogue_pools.json");
                    var configJson   = Read("config.json");

                    List<Conversation> tempConvs       = null;
                    List<CharacterCard> tempCharas      = null;
                    List<WorldBookEntry> tempWorldBook  = null;
                    List<ApiProfile> tempApiProfiles    = null;
                    UserProfile tempProfile             = null;
                    List<UserProfile> tempProfiles      = null;
                    List<DialoguePool> tempPools        = null;
                    RoleplayConfigExport tempConfig     = null;

                    if (convsJson != null)    tempConvs    = FromJson<List<Conversation>>(convsJson);
                    if (charasJson != null)   tempCharas   = FromJson<List<CharacterCard>>(charasJson);
                    if (worldJson != null)    tempWorldBook = FromJson<List<WorldBookEntry>>(worldJson);
                    if (apiJson != null)      tempApiProfiles = FromJson<List<ApiProfile>>(apiJson);
                    if (profileJson != null)  tempProfile  = FromJson<UserProfile>(profileJson);
                    if (profilesJson != null) tempProfiles = FromJson<List<UserProfile>>(profilesJson);
                    if (poolsJson != null)    tempPools    = FromJson<List<DialoguePool>>(poolsJson);
                    if (configJson != null)   tempConfig   = FromJson<RoleplayConfigExport>(configJson);

                    // 事务性安全赋值
                    if (tempConvs != null)    DataManager.Data.Conversations    = tempConvs;
                    if (tempCharas != null)   DataManager.Data.CharacterCards   = tempCharas;
                    if (tempWorldBook != null) DataManager.Data.WorldBookEntries = tempWorldBook;
                    if (tempApiProfiles != null) DataManager.Data.ApiProfiles   = tempApiProfiles;
                    if (tempProfile != null)  DataManager.Data.UserProfile      = tempProfile;
                    if (tempProfiles != null) DataManager.Data.UserProfiles     = tempProfiles;
                    if (tempPools != null)    DataManager.Data.DialoguePools    = tempPools;
                    
                    if (tempConfig != null)
                    {
                        DataManager.Data.SelectedApiProfileId = tempConfig.SelectedApiProfileId;
                        DataManager.Data.SelectedCharacterCardId = tempConfig.SelectedCharacterCardId;
                        DataManager.Data.LastActiveConversationId = tempConfig.LastActiveConversationId;
                        DataManager.Data.DefaultApiProfileId = tempConfig.DefaultApiProfileId;
                        DataManager.Data.ActiveUserProfileId = tempConfig.ActiveUserProfileId;
                    }
                }
                await DataManager.SaveAsync();

                // 重新加载池与记忆
                DialoguePoolManager.LoadAll();
                await MemoryStore.LoadAsync();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZIP Restore] Failed: {ex.Message}");
                return false;
            }
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

    [DataContract]
    public class RoleplayConfigExport
    {
        [DataMember] public string SelectedApiProfileId { get; set; }
        [DataMember] public string SelectedCharacterCardId { get; set; }
        [DataMember] public string LastActiveConversationId { get; set; }
        [DataMember] public string DefaultApiProfileId { get; set; }
        [DataMember] public string ActiveUserProfileId { get; set; }
    }
}
