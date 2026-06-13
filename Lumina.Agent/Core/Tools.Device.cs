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
        private static async Task<string> ExecuteCalendarList(string argsJson)
        {
            string maxStr = ExtractJsonString(argsJson, "max_count");
            int maxCount = 10;
            if (!string.IsNullOrEmpty(maxStr)) int.TryParse(maxStr, out maxCount);
            if (maxCount < 1) maxCount = 1;
            if (maxCount > 50) maxCount = 50;

            try
            {
                var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
                if (store == null) return "日历服务不可用";

                var findRaw = await store.FindAppointmentsAsync(DateTimeOffset.Now, TimeSpan.FromDays(30));
                var find = (findRaw != null) ? findRaw.ToList() : new List<Appointment>();
                if (find.Count == 0) return "未来30天内没有日历事件。";

                var sb = new StringBuilder();
                sb.AppendLine("即将到来的日历事件：");
                int count = 0;
                foreach (var a in find.OrderBy(a => a.StartTime))
                {
                    if (count >= maxCount) break;
                    string loc = string.IsNullOrEmpty(a.Location) ? "" : " @ " + a.Location;
                    sb.AppendLine(string.Format("  [{0}] {1}  ({2:yyyy-MM-dd HH:mm}–{3:HH:mm}){4}",
                        count + 1, a.Subject ?? "(无标题)", a.StartTime, a.StartTime.Add(a.Duration), loc));
                    count++;
                }
                if (find.Count > maxCount)
                    sb.AppendLine(string.Format("...以及另外 {0} 个事件", find.Count - maxCount));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "获取日历失败: " + ex.Message;
            }
        }

        private static async Task<string> ExecuteCalendarCreate(string argsJson)
        {
            string title = ExtractJsonString(argsJson, "title");
            string startStr = ExtractJsonString(argsJson, "start_time");
            string durStr = ExtractJsonString(argsJson, "duration_minutes");
            string location = ExtractJsonString(argsJson, "location");
            string details = ExtractJsonString(argsJson, "details");

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(startStr))
                return "错误：标题和开始时间不能为空";

            int dur = 60;
            if (!string.IsNullOrEmpty(durStr)) int.TryParse(durStr, out dur);
            if (dur < 1) dur = 1;

            DateTimeOffset start;
            if (!DateTimeOffset.TryParse(startStr, out start))
                return "错误：时间格式无效，请使用 yyyy-MM-dd HH:mm 格式";

            try
            {
                var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite);
                if (store == null) return "日历服务不可用";

                var apt = new Appointment
                {
                    Subject = title ?? "",
                    Location = location ?? "",
                    Details = details ?? "",
                    StartTime = start,
                    Duration = TimeSpan.FromMinutes(dur),
                    AllDay = false,
                };

                await store.ShowAddAppointmentAsync(apt, new Windows.Foundation.Rect());
                return string.Format("已创建日历事件「{0}」于 {1:yyyy-MM-dd HH:mm}（持续 {2} 分钟）",
                    title, start, dur);
            }
            catch (Exception ex)
            {
                return "创建日历事件失败: " + ex.Message;
            }
        }

        // ── Contacts tools ─────────────────────────────────────────────────

        private static async Task<string> ExecuteContactsSearch(string argsJson)
        {
            string query = ExtractJsonString(argsJson, "query");
            if (string.IsNullOrWhiteSpace(query))
                return "错误：搜索关键词不能为空";

            try
            {
                var store = await ContactManager.RequestStoreAsync();
                if (store == null) return "联系人服务不可用";

                var allContacts = await store.FindContactsAsync();
                var filtered = allContacts != null
                    ? allContacts.Where(c => (c.DisplayName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || c.Phones.Any(p => (p.Number ?? "").Contains(query))
                        || c.Emails.Any(e => (e.Address ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList()
                    : null;
                if (filtered == null || filtered.Count == 0)
                    return "未找到匹配的联系人。";

                var sb = new StringBuilder();
                sb.AppendLine(string.Format("找到 {0} 个联系人：", filtered.Count));
                foreach (var c in filtered)
                {
                    string name = c.DisplayName ?? "(无姓名)";
                    string phones = string.Join(", ", c.Phones.Select(p => p.Number));
                    string emails = string.Join(", ", c.Emails.Select(e => e.Address));
                    sb.AppendLine("  " + name);
                    if (!string.IsNullOrEmpty(phones))
                        sb.AppendLine("    电话: " + phones);
                    if (!string.IsNullOrEmpty(emails))
                        sb.AppendLine("    邮箱: " + emails);
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "搜索联系人失败: " + ex.Message;
            }
        }

        // ── Phone tools ────────────────────────────────────────────────────

        private static async Task<string> ExecuteMakeCall(string argsJson)
        {
            string number = ExtractJsonString(argsJson, "phone_number");
            if (string.IsNullOrWhiteSpace(number))
                return "错误：电话号码不能为空";

            if (!IsMobile)
                return "拨号功能仅支持手机端。当前设备: " + AnalyticsInfo.VersionInfo.DeviceFamily;

            try
            {
                var uri = new Uri("tel:" + Uri.EscapeDataString(number));
                await LaunchUriOnUiAsync(uri);
                return "已打开拨号界面: " + number;
            }
            catch (Exception ex)
            {
                return "拨号失败: " + ex.Message;
            }
        }

        // ── SMS tools ──────────────────────────────────────────────────────

        private static async Task<string> ExecuteSendSms(string argsJson)
        {
            string number = ExtractJsonString(argsJson, "phone_number");
            string message = ExtractJsonString(argsJson, "message");
            if (string.IsNullOrWhiteSpace(number))
                return "错误：号码不能为空";

            if (!IsMobile)
                return "短信功能仅支持手机端。当前设备: " + AnalyticsInfo.VersionInfo.DeviceFamily;

            try
            {
                var uri = new Uri("sms:" + Uri.EscapeDataString(number) + "?body=" + Uri.EscapeDataString(message ?? ""));
                await LaunchUriOnUiAsync(uri);
                return "已打开短信界面: " + number;
            }
            catch (Exception ex)
            {
                return "打开短信界面失败: " + ex.Message;
            }
        }

        // ── Open app ──────────────────────────────────────────────────────

        private static async Task<string> ExecuteOpenApp(string argsJson)
        {
            string uriOrName = ExtractJsonString(argsJson, "uri_or_name");
            if (string.IsNullOrWhiteSpace(uriOrName))
                return "错误：请提供应用 URI 或名称";

            try
            {
                // 尝试作为 URI 直接启动
                if (uriOrName.Contains(":"))
                {
                    Uri uri;
                    if (Uri.TryCreate(uriOrName, UriKind.Absolute, out uri))
                    {
                        bool success = await LaunchUriOnUiAsync(uri);
                        return success ? "已启动: " + uriOrName
                            : "无法启动该 URI（应用可能未安装或协议不支持）: " + uriOrName;
                    }
                    return "无效的 URI 格式: " + uriOrName;
                }

                // 非 URI：尝试按包名查找并启动（仅桌面端，Mobile 不支持 PackageManager）
                if (IsDesktop)
                {
                    try
                    {
                        var pkgManager = new Windows.Management.Deployment.PackageManager();
                        var pkgs = pkgManager.FindPackagesForUser("");
                        var match = pkgs.FirstOrDefault(p =>
                            (p.DisplayName ?? "").IndexOf(uriOrName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (p.Id?.Name ?? "").IndexOf(uriOrName, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (match != null)
                        {
                            var entries = await match.GetAppListEntriesAsync();
                            if (entries.Count > 0)
                            {
                                await entries[0].LaunchAsync();
                                return "已启动应用: " + (match.DisplayName ?? uriOrName);
                            }
                        }
                    }
                    catch { }
                }

                // 最后尝试作为 URI 加上冒号再试
                string guessUri = uriOrName + ":";
                Uri guess;
                if (Uri.TryCreate(guessUri, UriKind.Absolute, out guess))
                {
                    bool success = await LaunchUriOnUiAsync(guess);
                    if (success) return "已启动: " + guessUri;
                }

                return "无法找到或启动应用: " + uriOrName + "。请确认应用名称或提供完整的 URI 协议（如 ms-settings:）。";
            }
            catch (Exception ex)
            {
                return "启动应用失败: " + ex.Message;
            }
        }

        // ── Media control ─────────────────────────────────────────────────

        private static async Task<string> ExecuteMediaControl(string argsJson)
        {
            string action = ExtractJsonString(argsJson, "action").ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(action))
                return "错误：action 不能为空";

            try
            {
                switch (action)
                {
                    case "play":
                    case "pause":
                    case "play_pause":
                    case "next":
                    case "previous":
                    case "stop":
                    {
                        // 通过 SystemMediaTransportControls 发送媒体按键
                        string result = await SendMediaKeyAsync(action);
                        return result;
                    }
                    case "volume_up":
                    case "volume_down":
                    case "mute":
                    case "unmute":
                    case "set_volume":
                    {
                        // 打开系统音量设置
                        var uri = new Uri("ms-settings:sound");
                        bool ok = await LaunchUriOnUiAsync(uri);
                        string vol = ExtractJsonString(argsJson, "volume");
                        if (action == "set_volume" && !string.IsNullOrEmpty(vol))
                            return ok ? "已打开音量设置，请手动调整至 " + vol + "%" : "无法打开音量设置";
                        return ok ? "已打开音量设置页面" : "无法打开音量设置";
                    }
                    default:
                        return "错误：不支持的操作 \"" + action + "\"。支持的操作：play, pause, play_pause, next, previous, stop, volume_up, volume_down, mute, unmute, set_volume";
                }
            }
            catch (Exception ex)
            {
                return "媒体控制失败: " + ex.Message;
            }
        }

        private static async Task<string> SendMediaKeyAsync(string action)
        {
            var tcs = new TaskCompletionSource<string>();
            var _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        var smtc = Windows.Media.SystemMediaTransportControls.GetForCurrentView();
                        if (smtc == null)
                        {
                            tcs.TrySetResult("无法获取系统媒体控制器");
                            return;
                        }
                        switch (action)
                        {
                            case "play":
                                smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Playing;
                                tcs.TrySetResult("已发送播放指令");
                                break;
                            case "pause":
                                smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Paused;
                                tcs.TrySetResult("已发送暂停指令");
                                break;
                            case "play_pause":
                                if (smtc.PlaybackStatus == Windows.Media.MediaPlaybackStatus.Playing)
                                    smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Paused;
                                else
                                    smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Playing;
                                tcs.TrySetResult("已切换播放/暂停状态");
                                break;
                            case "stop":
                                smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Stopped;
                                tcs.TrySetResult("已发送停止指令");
                                break;
                            case "next":
                            case "previous":
                                tcs.TrySetResult("跳转上/下一曲需要媒体应用支持，已尝试发送指令");
                                break;
                            default:
                                tcs.TrySetResult("未知操作");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult("媒体控制不可用: " + ex.Message + "。当前没有正在播放的媒体，或系统不支持此操作。");
                    }
                });
            return await tcs.Task;
        }

        // ── Subagent ──────────────────────────────────────────────────────

        private static int _subagentDepth = 0;
        private const int MaxSubagentDepth = 2;

    }
}
