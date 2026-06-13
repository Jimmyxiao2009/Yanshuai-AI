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

    }
}
