using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.ApplicationModel.Appointments;
using Windows.ApplicationModel.Contacts;
using Windows.System;
using Windows.System.Profile;

namespace yanshuai
{
    public static partial class FunctionCallEngine
    {
        public static async Task<List<string>> GetGrantedFolderPathsAsync()
        {
            var result = new List<string>();
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    // metadata 里存的是路径（我们写入时存了 folder.Path 作为 metadata）
                    if (!string.IsNullOrEmpty(entry.Metadata))
                    {
                        result.Add(entry.Metadata);
                        continue;
                    }
                    // 兜底：实际获取文件夹读路径
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    result.Add(folder.Path);
                }
                catch { }
            }
            return result;
        }

        // 持久化已授权的敏感操作（key = 工具名:路径/标题）
        private static HashSet<string> _grantedPermissions = null;
        private static bool _permsLoaded = false;

        private static async Task LoadPermissionsAsync()
        {
            if (_permsLoaded) return;
            _permsLoaded = true;
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "yanshuaiPerms.json", CreationCollisionOption.OpenIfExists);
                string json = await FileIO.ReadTextAsync(file);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    // 简单格式：每行一个 key
                    var keys = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    _grantedPermissions = new HashSet<string>(
                        keys.Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);
                    return;
                }
            }
            catch { }
            _grantedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static async Task SavePermissionsAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "yanshuaiPerms.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, string.Join("\n", _grantedPermissions));
            }
            catch { }
        }

        private static async Task<bool> CheckPermissionAsync(string permKey)
        {
            await LoadPermissionsAsync();
            return _grantedPermissions.Contains(permKey);
        }

        private static async Task GrantPermissionAsync(string permKey)
        {
            await LoadPermissionsAsync();
            _grantedPermissions.Add(permKey);
            await SavePermissionsAsync();
        }

        public static void ResetGrantedPermissions() => _grantedPermissions?.Clear();

        private static bool IsSensitiveTool(string name)
            => name == "write_file" || name == "calendar_create"
            || name == "make_call" || name == "send_sms"
            || name == "read_image";   // 可读任意已授权路径并 base64 外传 → 需用户授权

        private static string BuildPermKey(string name, string argsJson)
        {
            if (name == "write_file")
                return "write_file:" + ExtractJsonString(argsJson, "name");
            if (name == "calendar_create")
                return "calendar_create:" + ExtractJsonString(argsJson, "title");
            if (name == "make_call")
                return "make_call:" + ExtractJsonString(argsJson, "phone_number");
            if (name == "send_sms")
                return "send_sms:" + ExtractJsonString(argsJson, "phone_number");
            if (name == "read_image")
                return "read_image:" + ExtractJsonString(argsJson, "path");
            return name;
        }

        private static string BuildPermDescription(string name, string argsJson)
        {
            if (name == "write_file")
            {
                string fn = SafeFileName(ExtractJsonString(argsJson, "name"), ExtractJsonString(argsJson, "ext"));
                string content = ExtractJsonString(argsJson, "content");
                int preview = Math.Min(content?.Length ?? 0, 80);
                string snippet = preview > 0 ? content.Substring(0, preview) + (content.Length > 80 ? "…" : "") : "";
                return $"写入文件：{fn}（工作目录）\n内容预览：{snippet}";
            }
            if (name == "calendar_create")
            {
                string title = ExtractJsonString(argsJson, "title");
                string start = ExtractJsonString(argsJson, "start_time");
                string dur   = ExtractJsonString(argsJson, "duration_minutes");
                return $"创建日历事件：{title}\n时间：{start}，时长 {dur} 分钟";
            }
            if (name == "make_call")
            {
                string number = ExtractJsonString(argsJson, "phone_number");
                return $"拨打电话：{number}";
            }
            if (name == "send_sms")
            {
                string number = ExtractJsonString(argsJson, "phone_number");
                string message = ExtractJsonString(argsJson, "message");
                int preview = Math.Min(message?.Length ?? 0, 60);
                string snippet = preview > 0 ? message.Substring(0, preview) + (message.Length > 60 ? "…" : "") : "";
                return $"发送短信至：{number}\n内容：{snippet}";
            }
            if (name == "read_image")
            {
                string path = ExtractJsonString(argsJson, "path");
                return $"读取图片文件并发送给视觉模型：{path}";
            }
            return name;
        }

    }
}
