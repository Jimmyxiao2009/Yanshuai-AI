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

        // 统一到 Lumina.Core/AI/ChatJson（原版漏控制字符(<0x20)转义；含快速路径）
        internal static string EscapeJson(string s) => ChatJson.EscapeJson(s);

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

    }
}
