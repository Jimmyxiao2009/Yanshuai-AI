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
                        Order    = entry.Order,
                        Disable  = entry.Disable,
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
                            Order        = ste.Order,
                            Disable      = ste.Disable,
                        });
                        total++;
                    }
                }
                catch { }
            }
            await DataManager.SaveAsync();
            WorldStatus.Text = $"✓ 导入了 {total} 条世界书条目";
        }

    }
}
