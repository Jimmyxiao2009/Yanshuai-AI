using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        private static string ExtractGeminiText(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int candIdx = json.IndexOf("\"candidates\"");
            if (candIdx < 0) return null;
            int partsIdx = json.IndexOf("\"parts\"", candIdx);
            if (partsIdx < 0) return null;
            int textIdx = json.IndexOf("\"text\":", partsIdx);
            if (textIdx < 0) return null;
            return ExtractJsonString(json, textIdx + 7);
        }

        // 按偏移量从 JSON 字符串中读取引号包裹的值
        private static string ExtractJsonString(string json, int start)
        {
            while (start < json.Length && json[start] != '"') start++;
            if (start >= json.Length) return "";
            start++;
            var sb = new StringBuilder();
            while (start < json.Length)
            {
                char c = json[start++];
                if (c == '"') break;
                if (c == '\\' && start < json.Length)
                {
                    char esc = json[start++];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default:  sb.Append(esc);  break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // EscapeJson / BuildRequestJson 已下沉到 Lumina.Core/AI/ChatJson.cs（两项目共用）
        private static string EscapeJson(string s) => ChatJson.EscapeJson(s);

        private static string BuildRequestJson(string model, List<ApiRequestMessage> messages, bool stream, bool supportsVision = false, bool isClaudeProvider = false)
            => ChatJson.BuildRequestJson(model, messages, stream, supportsVision, isClaudeProvider);

    }
}
