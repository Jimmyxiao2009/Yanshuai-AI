using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;

namespace yanshuai
{
    /// <summary>
    /// 图片外置存储：把消息附带的图片以独立文件保存在 LocalFolder/images/，
    /// 仅在消息里保留文件引用 id。这样图片 base64 不再随 AppData 长期常驻内存
    /// （低内存设备卡死/OOM 的诱因之一），需要时（显示缩略图 / 发送给模型）才按需读取。
    /// 磁盘上以原始字节存储（比 base64 文本省约 1/3 空间）。
    /// </summary>
    public static class ImageStore
    {
        private const string FolderName = "images";
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private static StorageFolder _folder;

        private static async Task<StorageFolder> GetFolderAsync()
        {
            if (_folder != null) return _folder;
            await _gate.WaitAsync();
            try
            {
                if (_folder == null)
                    _folder = await ApplicationData.Current.LocalFolder
                        .CreateFolderAsync(FolderName, CreationCollisionOption.OpenIfExists);
            }
            finally { _gate.Release(); }
            return _folder;
        }

        /// <summary>保存 base64 图片，返回引用 id（文件名）。失败返回 null。</summary>
        public static async Task<string> SaveBase64Async(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var folder = await GetFolderAsync();
                string id = Guid.NewGuid().ToString("N");
                var file = await folder.CreateFileAsync(id, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, bytes);
                return id;
            }
            catch { return null; }
        }

        /// <summary>按 id 读取图片为 base64。失败（文件不存在等）返回 null。</summary>
        public static async Task<string> LoadBase64Async(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var folder = await GetFolderAsync();
                var file = await folder.GetFileAsync(id);
                var buffer = await FileIO.ReadBufferAsync(file);
                return Convert.ToBase64String(buffer.ToArray());
            }
            catch { return null; }
        }

        /// <summary>删除一个图片文件（尽力而为，不抛异常）。</summary>
        public static async Task DeleteAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                var folder = await GetFolderAsync();
                var file = await folder.GetFileAsync(id);
                await file.DeleteAsync();
            }
            catch { }
        }
    }
}
