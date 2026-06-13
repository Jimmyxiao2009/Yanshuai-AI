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
        // ── Character card export ─────────────────────────────────────────────

        private async void ExportChara_Click(object sender, RoutedEventArgs e)
        {
            var cards = DataManager.Data.CharacterCards;
            if (cards == null || cards.Count == 0) { CharaStatus.Text = "⚠ 没有角色卡"; return; }

            var folder = await PickFolder();
            if (folder == null) return;

            int ok = 0;
            foreach (var card in cards)
            {
                try
                {
                    string json = BuildStCharaJson(card);
                    string safe = MakeSafeFilename(card.Name);

                    if (card.HasAvatar)
                    {
                        // 嵌入 PNG tEXt chunk 导出（ST 标准格式）
                        byte[] pngBytes = Convert.FromBase64String(card.AvatarBase64);
                        byte[] output   = InjectPngTextChunk(pngBytes, "chara",
                                              Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
                        var file = await folder.CreateFileAsync(safe + ".png", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteBytesAsync(file, output);
                    }
                    else
                    {
                        var file = await folder.CreateFileAsync(safe + ".json", CreationCollisionOption.GenerateUniqueName);
                        await FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    }
                    ok++;
                }
                catch { }
            }
            CharaStatus.Text = $"✓ 已导出 {ok}/{cards.Count} 张角色卡";
        }

        /// <summary>构建符合 SillyTavern V2 spec 的角色卡 JSON 字符串（手动拼接保证字段顺序和格式）。</summary>
        internal static string BuildStCharaJson(CharacterCard card)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"spec\":\"chara_card_v2\",");
            sb.Append("\"spec_version\":\"2.0\",");
            sb.Append("\"data\":{");
            sb.Append($"\"name\":\"{EscapeJson(card.Name ?? "")}\",");
            sb.Append($"\"description\":\"{EscapeJson(card.Description ?? "")}\",");
            sb.Append($"\"personality\":\"{EscapeJson(card.Personality ?? "")}\",");
            sb.Append($"\"scenario\":\"{EscapeJson(card.Scenario ?? "")}\",");
            sb.Append($"\"first_mes\":\"{EscapeJson(card.FirstMessage ?? "")}\",");
            sb.Append($"\"mes_example\":\"{EscapeJson(card.MesExample ?? "")}\",");
            sb.Append($"\"creator_notes\":\"{EscapeJson(card.CreatorNotes ?? "")}\",");
            sb.Append($"\"system_prompt\":\"{EscapeJson(card.SystemPrompt ?? "")}\",");
            sb.Append($"\"post_history_instructions\":\"{EscapeJson(card.PostHistoryInstructions ?? "")}\",");
            // tags: split comma-separated string back to array
            var tagList = (card.Tags ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            sb.Append("\"tags\":[" + string.Join(",", tagList.Select(t => "\"" + EscapeJson(t) + "\"")) + "],");
            sb.Append($"\"creator\":\"{EscapeJson(card.Creator ?? "")}\",");
            sb.Append($"\"character_version\":\"{EscapeJson(card.CharacterVersion ?? "")}\",");
            sb.Append("\"extensions\":{}");
            sb.Append("}");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 在 PNG 的 IEND chunk 之前插入一个 tEXt chunk（keyword\0text）。
        /// 用于嵌入 SillyTavern 角色卡数据（keyword = "chara"）。
        /// </summary>
        internal static byte[] InjectPngTextChunk(byte[] png, string keyword, string text)
        {
            // tEXt chunk data = keyword bytes + 0x00 + text bytes (Latin-1)
            byte[] kwBytes   = Encoding.ASCII.GetBytes(keyword);
            byte[] textBytes = Encoding.GetEncoding(1252).GetBytes(text);
            byte[] data      = new byte[kwBytes.Length + 1 + textBytes.Length];
            kwBytes.CopyTo(data, 0);
            data[kwBytes.Length] = 0;
            textBytes.CopyTo(data, kwBytes.Length + 1);

            // chunk = 4-byte length + "tEXt" + data + 4-byte CRC
            byte[] typeBytes = Encoding.ASCII.GetBytes("tEXt");
            uint crc = PngCrc32(typeBytes, data);
            int chunkLen = 12 + data.Length; // 4+4+data+4
            byte[] chunk = new byte[chunkLen];
            int p = 0;
            // length (big-endian)
            chunk[p++] = (byte)(data.Length >> 24);
            chunk[p++] = (byte)(data.Length >> 16);
            chunk[p++] = (byte)(data.Length >> 8);
            chunk[p++] = (byte)(data.Length);
            // type
            typeBytes.CopyTo(chunk, p); p += 4;
            // data
            data.CopyTo(chunk, p); p += data.Length;
            // CRC
            chunk[p++] = (byte)(crc >> 24);
            chunk[p++] = (byte)(crc >> 16);
            chunk[p++] = (byte)(crc >> 8);
            chunk[p]   = (byte)(crc);

            // Find IEND offset in original PNG
            int iend = png.Length - 12; // IEND is always the last 12 bytes
            // Build output: everything before IEND + tEXt chunk + IEND
            byte[] output = new byte[png.Length + chunkLen];
            Array.Copy(png, 0,    output, 0,              iend);
            Array.Copy(chunk, 0,  output, iend,           chunkLen);
            Array.Copy(png, iend, output, iend + chunkLen, png.Length - iend);
            return output;
        }

        private static uint PngCrc32(byte[] type, byte[] data)
        {
            // Standard CRC-32 used by PNG
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            uint crc = 0xFFFFFFFF;
            foreach (byte b in type) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (byte b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        // ── Character card import ─────────────────────────────────────────────

        private async void ImportChara_Click(object sender, RoutedEventArgs e)
        {
            var files = await PickFiles(new[] { ".json", ".png" });
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                try
                {
                    CharacterCard card = null;
                    if (file.FileType.ToLower() == ".png")
                    {
                        var bytes = (await FileIO.ReadBufferAsync(file)).ToArray();
                        card = ParseCharaPng(bytes);
                    }
                    else
                    {
                        string json = await FileIO.ReadTextAsync(file);
                        card = ParseCharaJson(json);
                    }
                    if (card != null) { DataManager.Data.CharacterCards.Add(card); ok++; }
                }
                catch { }
            }
            await DataManager.SaveAsync();
            CharaStatus.Text = $"✓ 导入了 {ok}/{files.Count} 张角色卡";
        }

        // ── PNG角色卡解析（读tEXt chunk里的chara字段）────────────────────────
        internal static CharacterCard ParseCharaPng(byte[] pngBytes)
        {
            // PNG tEXt chunk格式: 4字节长度 + 4字节"tEXt" + keyword text + 4字节CRC
            // 找所有tEXt chunk，找keyword=="chara"的
            int pos = 8; // 跳过PNG signature
            while (pos + 12 <= pngBytes.Length)
            {
                int len = (pngBytes[pos] << 24) | (pngBytes[pos+1] << 16) | (pngBytes[pos+2] << 8) | pngBytes[pos+3];
                string type = System.Text.Encoding.ASCII.GetString(pngBytes, pos + 4, 4);
                if (type == "tEXt" && len > 0 && pos + 8 + len <= pngBytes.Length)
                {
                    // keyword是null结尾的ASCII字符串
                    int kEnd = pos + 8;
                    while (kEnd < pos + 8 + len && pngBytes[kEnd] != 0) kEnd++;
                    string keyword = System.Text.Encoding.ASCII.GetString(pngBytes, pos + 8, kEnd - (pos + 8));
                    if (keyword == "chara" && kEnd + 1 < pos + 8 + len)
                    {
                        string b64 = System.Text.Encoding.GetEncoding(1252).GetString(pngBytes, kEnd + 1, pos + 8 + len - kEnd - 1);
                        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64.Trim()));
                        var card = ParseCharaJson(json);
                        if (card != null)
                        {
                            // 把PNG本身存为头像
                            card.AvatarBase64   = Convert.ToBase64String(pngBytes);
                            card.AvatarMimeType  = "image/png";
                            // 也存为立绘
                            card.IllustrationBase64   = card.AvatarBase64;
                            card.IllustrationMimeType = "image/png";
                        }
                        return card;
                    }
                }
                if (type == "IEND") break;
                pos += 12 + len; // 4len + 4type + len + 4crc
            }
            return null;
        }

        internal static CharacterCard ParseCharaJson(string json)
        {
            // Try V2 first
            try
            {
                var v2 = FromJson<StCharaV2>(json);
                if (v2?.Spec == "chara_card_v2" && v2.Data != null)
                {
                    return new CharacterCard
                    {
                        Name                    = v2.Data.Name        ?? "",
                        Description             = v2.Data.Description ?? "",
                        Personality             = v2.Data.Personality ?? "",
                        Scenario                = v2.Data.Scenario    ?? "",
                        FirstMessage            = v2.Data.FirstMes    ?? "",
                        SystemPrompt            = v2.Data.SystemPrompt ?? "",
                        PostHistoryInstructions = v2.Data.PostHistory  ?? "",
                        MesExample              = v2.Data.MesExample   ?? "",
                        CreatorNotes            = v2.Data.CreatorNotes ?? "",
                        Tags                    = v2.Data.Tags != null ? string.Join(", ", v2.Data.Tags) : "",
                        Creator                 = v2.Data.Creator  ?? "",
                        CharacterVersion        = v2.Data.Version   ?? "",
                    };
                }
            }
            catch { }

            // Try V1 / flat JSON
            try
            {
                var v1 = FromJson<StCharaData>(json);
                if (!string.IsNullOrEmpty(v1?.Name))
                {
                    return new CharacterCard
                    {
                        Name                    = v1.Name         ?? "",
                        Description             = v1.Description  ?? "",
                        Personality             = v1.Personality  ?? "",
                        Scenario                = v1.Scenario     ?? "",
                        FirstMessage            = v1.FirstMes     ?? "",
                        SystemPrompt            = v1.SystemPrompt ?? "",
                        PostHistoryInstructions = v1.PostHistory  ?? "",
                        MesExample              = v1.MesExample   ?? "",
                        CreatorNotes            = v1.CreatorNotes ?? "",
                        Tags                    = v1.Tags != null ? string.Join(", ", v1.Tags) : "",
                        Creator                 = v1.Creator ?? "",
                        CharacterVersion        = v1.Version  ?? "",
                    };
                }
            }
            catch { }
            return null;
        }

        internal static StCharaV2 CardToSt(CharacterCard card) => new StCharaV2
        {
            Data = new StCharaData
            {
                Name        = card.Name        ?? "",
                Description = card.Description ?? "",
                Personality = card.Personality ?? "",
                Scenario    = card.Scenario    ?? "",
                FirstMes    = card.FirstMessage ?? "",
            }
        };

    }
}
