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
        private static async Task<StorageFolder> ResolveFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ApplicationData.Current.LocalFolder;

            if (path.StartsWith("sd:", StringComparison.OrdinalIgnoreCase))
            {
                var sds = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (sds.Count == 0) throw new Exception("未找到 SD 卡");
                string sub = path.Substring(3).Replace('/', '\\').TrimStart('\\');
                return string.IsNullOrEmpty(sub) ? sds[0] : await NavigateSubFolders(sds[0], sub);
            }

            if (path.StartsWith("public:", StringComparison.OrdinalIgnoreCase))
            {
                StorageFolder pub;
                if (IsMobile)
                {
                    try { pub = KnownFolders.DocumentsLibrary; }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                else
                {
                    try { pub = await StorageFolder.GetFolderFromPathAsync(@"C:\Users\Public"); }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                string sub = path.Substring(7).Replace('/', '\\').TrimStart('\\');
                return string.IsNullOrEmpty(sub) ? pub : await NavigateSubFolders(pub, sub);
            }

            // 手机端不接受盘符路径
            if (IsMobile && path.Length >= 2 && path[1] == ':')
                throw new UnauthorizedAccessException(
                    "此设备为手机，不支持盘符路径 " + path + "。请使用 sd:、public: 前缀或通过 set_working_directory 授权。");

            // 先尝试 KnownFolders（不需要 broadFileSystemAccess）
            string normPath = path.Replace('/', '\\');
            StorageFolder known = await TryGetKnownFolder(normPath);
            if (known != null) return known;

            // 从 FutureAccessList 中查找已授权的文件夹（精确匹配或父路径匹配）
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    string folderNorm = folder.Path.Replace('/', '\\').TrimEnd('\\');
                    // 精确匹配：normPath 就是这个授权文件夹本身
                    if (string.Equals(normPath.TrimEnd('\\'), folderNorm, StringComparison.OrdinalIgnoreCase))
                        return folder;
                    // 子路径匹配：normPath 在授权文件夹之下
                    string folderPrefix = folderNorm + "\\";
                    if (normPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(folderPrefix.Length).TrimEnd('\\');
                        return string.IsNullOrEmpty(relative) ? folder : await folder.GetFolderAsync(relative);
                    }
                }
                catch { /* 条目已过期，跳过 */ }
            }

            // 没有匹配 — 提示请求授权
            throw new UnauthorizedAccessException(
                "路径 \"" + path + "\" 不在任何已授权文件夹范围内。请先调用 set_working_directory 让用户授权包含该路径的文件夹。");
        }

        // 尝试通过 KnownFolders 访问系统库（不需要 broadFileSystemAccess）
        private static async Task<StorageFolder> TryGetKnownFolder(string normPath)
        {
            // 枚举所有 KnownFolder 根路径
            var knownRoots = new List<StorageFolder>();
            var factories = new Func<StorageFolder>[]
            {
                () => KnownFolders.PicturesLibrary,
                () => KnownFolders.MusicLibrary,
                () => KnownFolders.VideosLibrary,
                () => KnownFolders.DocumentsLibrary,
                () => KnownFolders.CameraRoll,
                () => KnownFolders.SavedPictures,
            };
            foreach (var f in factories)
            {
                try { knownRoots.Add(f()); } catch { }
            }

            foreach (var kf in knownRoots)
            {
                if (kf == null) continue;
                try
                {
                    string kfNorm = kf.Path.Replace('/', '\\').TrimEnd('\\');
                    // 精确匹配
                    if (string.Equals(normPath.TrimEnd('\\'), kfNorm, StringComparison.OrdinalIgnoreCase))
                        return kf;
                    // 子路径
                    string kfPrefix = kfNorm + "\\";
                    if (normPath.StartsWith(kfPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(kfPrefix.Length).TrimEnd('\\');
                        if (string.IsNullOrEmpty(relative)) return kf;
                        // 逐级获取子文件夹
                        StorageFolder cur = kf;
                        foreach (var seg in relative.Split('\\'))
                        {
                            if (string.IsNullOrEmpty(seg)) continue;
                            cur = await cur.GetFolderAsync(seg);
                        }
                        return cur;
                    }
                }
                catch { /* 当前 KnownFolder 无法访问子路径，跳过 */ }
            }
            return null;
        }

        private static async Task<StorageFolder> NavigateSubFolders(StorageFolder root, string relativePath)
        {
            StorageFolder cur = root;
            foreach (var seg in relativePath.Split('\\'))
            {
                if (string.IsNullOrEmpty(seg)) continue;
                cur = await cur.CreateFolderAsync(seg, CreationCollisionOption.OpenIfExists);
            }
            return cur;
        }

        private static async Task<StorageFile> ResolveFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空");

            if (path.StartsWith("sd:", StringComparison.OrdinalIgnoreCase))
            {
                var sds = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (sds.Count == 0) throw new Exception("未找到 SD 卡");
                string filePath = path.Substring(3).Replace('/', '\\').TrimStart('\\');
                int sep = filePath.LastIndexOf('\\');
                if (sep < 0) return await sds[0].CreateFileAsync(filePath, CreationCollisionOption.OpenIfExists);
                var folder = await NavigateSubFolders(sds[0], filePath.Substring(0, sep));
                return await folder.CreateFileAsync(filePath.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
            }

            if (path.StartsWith("public:", StringComparison.OrdinalIgnoreCase))
            {
                StorageFolder pub;
                if (IsMobile)
                {
                    try { pub = KnownFolders.DocumentsLibrary; }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                else
                {
                    try { pub = await StorageFolder.GetFolderFromPathAsync(@"C:\Users\Public"); }
                    catch { pub = ApplicationData.Current.LocalFolder; }
                }
                string filePath = path.Substring(7).Replace('/', '\\').TrimStart('\\');
                int sep = filePath.LastIndexOf('\\');
                if (sep < 0) return await pub.CreateFileAsync(filePath, CreationCollisionOption.OpenIfExists);
                var folder = await NavigateSubFolders(pub, filePath.Substring(0, sep));
                return await folder.CreateFileAsync(filePath.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
            }

            // 手机端不接受盘符路径
            if (IsMobile && path.Length >= 2 && path[1] == ':')
                throw new UnauthorizedAccessException(
                    "此设备为手机，不支持盘符路径 " + path + "。请使用 sd:、public: 前缀或通过 set_working_directory 授权。");

            // 先尝试 KnownFolders（图片/音乐/视频等系统库，无需 broadFileSystemAccess）
            string normPath = path.Replace('/', '\\');
            StorageFolder knownForFile = await TryGetKnownFolder(System.IO.Path.GetDirectoryName(normPath) ?? normPath);
            if (knownForFile != null)
            {
                string fileName = System.IO.Path.GetFileName(normPath);
                if (!string.IsNullOrEmpty(fileName))
                    return await knownForFile.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
            }

            // 从 FutureAccessList 中查找已授权的父文件夹（用于文件写入/读取）
            var accessList = StorageApplicationPermissions.FutureAccessList;
            for (int i = 0; i < accessList.Entries.Count; i++)
            {
                try
                {
                    var entry = accessList.Entries[i];
                    var folder = await accessList.GetFolderAsync(entry.Token);
                    string folderNorm = folder.Path.Replace('/', '\\').TrimEnd('\\');
                    string folderPrefix = folderNorm + "\\";
                    if (normPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = normPath.Substring(folderPrefix.Length);
                        int sep = relative.LastIndexOf('\\');
                        if (sep < 0)
                            return await folder.CreateFileAsync(relative, CreationCollisionOption.OpenIfExists);
                        var subFolder = await NavigateSubFolders(folder, relative.Substring(0, sep));
                        return await subFolder.CreateFileAsync(relative.Substring(sep + 1), CreationCollisionOption.OpenIfExists);
                    }
                }
                catch { /* 条目已过期或文件夹已删除，跳过 */ }
            }

            // 没有匹配的已授权文件夹 — 直接失败并提示
            throw new UnauthorizedAccessException(
                "路径 \"" + path + "\" 不在任何已授权文件夹范围内。请先调用 set_working_directory 让用户授权包含该路径的文件夹。");
        }

        private static async Task<string> ExecuteListFiles(string argsJson)
        {
            string path = ExtractJsonString(argsJson, "path");
            try
            {
                // 留空 path → 优先列出工作目录（App 接管路径的新模型）
                StorageFolder folder = string.IsNullOrWhiteSpace(path)
                    ? (await GetWorkingFolderAsync() ?? await ResolveFolder(path))
                    : await ResolveFolder(path);
                var items = await folder.GetItemsAsync();
                var sb = new StringBuilder();
                sb.AppendLine("目录: " + folder.Path + "  (" + items.Count + " 项)");
                sb.AppendLine();
                foreach (var item in items.OrderBy(f => f.Name))
                {
                    if (item is StorageFolder d)
                        sb.AppendLine("  [目录] " + d.Name + "/");
                }
                foreach (var item in items.OrderBy(f => f.Name))
                {
                    if (item is StorageFile f)
                    {
                        var props = await f.GetBasicPropertiesAsync();
                        string size = props.Size > 1024 * 1024
                            ? string.Format("{0:F1}MB", props.Size / (1024.0 * 1024.0))
                            : props.Size > 1024
                                ? string.Format("{0:F1}KB", props.Size / 1024.0)
                                : props.Size + "B";
                        sb.AppendLine("  [文件] " + f.Name + "  (" + size + ")");
                    }
                }
                return sb.ToString();
            }
            catch (UnauthorizedAccessException uae)
            {
                return "权限不足，请调用 set_working_directory 请求用户授权该文件夹。\n详情: " + uae.Message;
            }
            catch (ArgumentException ae)
            {
                return "路径无法访问（" + ae.Message + "）。请调用 set_working_directory 让用户通过系统文件夹选择器授权该路径。";
            }
            catch (Exception ex)
            {
                return "目录列出失败: " + ex.Message + "。如果是权限问题，请调用 set_working_directory 请求授权。";
            }
        }

        // ── 工作目录（App 接管路径；AI 只给文件名/后缀/内容）────────────────────

        /// <summary>取得已授权的工作目录文件夹。按 FutureAccessList 中存储的 Metadata 路径匹配
        /// （不依赖 String.GetHashCode 的进程内随机化，跨会话稳定）。未设置则返回 null。</summary>
        private static async Task<StorageFolder> GetWorkingFolderAsync()
        {
            string path = AppSettings.WorkingDirPath;
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var accessList = StorageApplicationPermissions.FutureAccessList;
                foreach (var entry in accessList.Entries)
                {
                    if (string.Equals(entry.Metadata, path, StringComparison.OrdinalIgnoreCase))
                        return await accessList.GetFolderAsync(entry.Token);
                }
            }
            catch { }
            return null;
        }

        /// <summary>取得工作目录；若未设置则通过回调请用户选择一个。</summary>
        private static async Task<StorageFolder> EnsureWorkingFolderAsync(FolderAccessCallback cb)
        {
            var folder = await GetWorkingFolderAsync();
            if (folder != null) return folder;
            if (cb == null) return null;
            string path = await cb("AI 工作目录（用于读写文件）");
            if (string.IsNullOrEmpty(path)) return null;
            AppSettings.WorkingDirPath = path;
            return await GetWorkingFolderAsync();
        }

        /// <summary>把 AI 给的文件名净化为安全的纯文件名（去掉目录/盘符/非法字符），按需补后缀。</summary>
        private static string SafeFileName(string name, string ext)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1); // 只保留最后一段，杜绝路径穿越
            name = name.Trim().TrimStart('.');                // 不允许以点开头（隐藏/相对）
            char[] invalid = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
            foreach (var c in invalid) name = name.Replace(c.ToString(), "");
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (!string.IsNullOrWhiteSpace(ext))
            {
                ext = ext.Trim().TrimStart('.');
                if (ext.Length > 0 && !name.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                    name = name + "." + ext;
            }
            return name;
        }

        private static async Task<string> ExecuteReadFile(string argsJson, FolderAccessCallback cb)
        {
            string fileName = SafeFileName(ExtractJsonString(argsJson, "name"), null);
            if (fileName == null) return "错误：文件名无效";
            var folder = await EnsureWorkingFolderAsync(cb);
            if (folder == null)
                return "尚未设置工作目录或用户未授权。请调用 set_working_directory 让用户选择工作目录后重试。";
            try
            {
                var file = await folder.GetFileAsync(fileName);
                string text = await FileIO.ReadTextAsync(file);
                if (text.Length > 8000)
                    text = text.Substring(0, 8000) + "\n\n[内容过长，已截断]";
                return text;
            }
            catch (System.IO.FileNotFoundException)
            {
                return "工作目录中找不到文件：" + fileName + "。可先用 list_files 查看有哪些文件。";
            }
            catch (Exception ex)
            {
                return "读取失败: " + ex.Message;
            }
        }

        private static async Task<string> ExecuteWriteFile(string argsJson, FolderAccessCallback cb)
        {
            string fileName = SafeFileName(ExtractJsonString(argsJson, "name"), ExtractJsonString(argsJson, "ext"));
            string content  = ExtractJsonString(argsJson, "content");
            if (fileName == null) return "错误：文件名无效";
            var folder = await EnsureWorkingFolderAsync(cb);
            if (folder == null)
                return "尚未设置工作目录或用户未授权。请调用 set_working_directory 让用户选择工作目录后重试。";
            try
            {
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, content ?? "");
                return "已写入 " + fileName + "（" + (content?.Length ?? 0) + " 字符）到工作目录";
            }
            catch (Exception ex)
            {
                return "写入失败: " + ex.Message;
            }
        }

        private static async Task<string> ExecuteSetWorkingDirectory(string argsJson, FolderAccessCallback callback)
        {
            if (callback == null)
                return "此设备不支持选择文件夹";
            try
            {
                // 始终弹出选择器（允许更换目录）
                string path = await callback("AI 工作目录（用于读写文件）");
                if (string.IsNullOrEmpty(path))
                    return "用户取消了工作目录选择。";
                AppSettings.WorkingDirPath = path;
                return "已设置工作目录：" + path + "。read_file / write_file / list_files 都将在此目录内操作。";
            }
            catch (Exception ex)
            {
                return "设置工作目录出错: " + ex.Message;
            }
        }

        // ── Calendar tools ─────────────────────────────────────────────────

    }
}
