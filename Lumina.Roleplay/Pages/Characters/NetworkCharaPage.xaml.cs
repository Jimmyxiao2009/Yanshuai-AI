// NetworkCharaPage.xaml.cs — 角色广场（信息流模式）
// 大图卡片信息流 + 顶栏搜索 + URL导入/平台设置 Flyout

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class NetworkCharaPage : Page
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private int _chubPage = 1;
        private int _huayuPage = 1;
        private bool _isLoading = false;
        private bool _isSearchMode = false;
        private bool _toggleInitialized = false;
        private int _feedCardCount = 0;

        public NetworkCharaPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            ToggleChub.IsOn = AppSettings.SourceEnabled_chub;
            ToggleHuayu.IsOn = AppSettings.SourceEnabled_huayu;
            ToggleXingye.IsOn = AppSettings.SourceEnabled_xingye;
            ToggleQuack.IsOn = AppSettings.SourceEnabled_quack;
            ToggleDzmm.IsOn = AppSettings.SourceEnabled_dzmm;
            _toggleInitialized = true;

            await LoadFeedAsync(true);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }


        // ══ 平台开关 ═════════════════════════════════════════════════════════

        private void SourceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_toggleInitialized) return;
            if (!(sender is ToggleSwitch toggle) || !(toggle.Tag is string tag)) return;
            switch (tag)
            {
                case "chub":   AppSettings.SourceEnabled_chub = toggle.IsOn; break;
                case "huayu":  AppSettings.SourceEnabled_huayu = toggle.IsOn; break;
                case "xingye": AppSettings.SourceEnabled_xingye = toggle.IsOn; break;
                case "quack":  AppSettings.SourceEnabled_quack = toggle.IsOn; break;
                case "dzmm":   AppSettings.SourceEnabled_dzmm = toggle.IsOn; break;
            }
            if (!_isSearchMode)
                _ = LoadFeedAsync(true);
        }

        // ══ URL 链接导入 ═══════════════════════════════════════════════════

        private async void UrlDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlInput.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            UrlDownloadBtn.IsEnabled = false;
            UrlDownloadBtn.Content = AppSettings.S("下载中...", "Downloading...");
            UrlResultPanel.Children.Clear();

            try
            {
                var card = await SourceAutoDetect.DownloadAsync(url, _http);
                if (card == null)
                {
                    UrlResultPanel.Children.Add(new TextBlock
                    {
                        Text = AppSettings.S("下载失败，请检查 URL 是否正确", "Download failed. Check the URL."),
                        FontSize = 13, Opacity = 0.5
                    });
                    return;
                }

                UrlFlyout.Hide();
                Frame.Navigate(typeof(NetworkCharaPreviewPage), card);
            }
            catch (Exception ex)
            {
                UrlResultPanel.Children.Add(new TextBlock
                {
                    Text = AppSettings.S("错误：" + ex.Message, "Error: " + ex.Message),
                    FontSize = 13, Opacity = 0.5
                });
            }
            finally
            {
                UrlDownloadBtn.IsEnabled = true;
                UrlDownloadBtn.Content = AppSettings.S("下载并预览", "Download & Preview");
            }
        }

        // ══ 搜索 ═════════════════════════════════════════════════════════════

        private void SearchInput_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = DoFeedSearchAsync();
            }
        }

        private async void SearchBtn_Click(object sender, RoutedEventArgs e)
        {
            await DoFeedSearchAsync();
        }

        private async Task DoFeedSearchAsync()
        {
            string query = SearchInput.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                _isSearchMode = false;
                await LoadFeedAsync(true);
                return;
            }

            _isSearchMode = true;
            _isLoading = true;

            FeedLoadingRing.IsActive = true;
            FeedLoadingRing.Visibility = Visibility.Visible;
            LoadMoreBtn.Visibility = Visibility.Collapsed;
            FeedCol0.Children.Clear();
            FeedCol1.Children.Clear();
            _feedCardCount = 0;
            FeedStatusText.Visibility = Visibility.Collapsed;

            try
            {
                var allResults = new List<CharaSearchResult>();
                var tasks = new List<Task<List<CharaSearchResult>>>();

                if (AppSettings.SourceEnabled_chub)
                    tasks.Add(SourceCharacterHub.SearchAsync(query, _http));
                if (AppSettings.SourceEnabled_huayu)
                    tasks.Add(SourceHuayu.SearchAsync(query, _http));

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    foreach (var t in tasks)
                    {
                        if (t.Result != null)
                            allResults.AddRange(t.Result);
                    }
                }

                if (allResults.Count == 0)
                {
                    FeedStatusText.Text = AppSettings.S("未找到结果", "No results found.");
                    FeedStatusText.Visibility = Visibility.Visible;
                }
                else
                {
                    FeedStatusText.Text = AppSettings.S(
                        "找到 " + allResults.Count + " 个结果",
                        "Found " + allResults.Count + " results");
                    FeedStatusText.Visibility = Visibility.Visible;

                    foreach (var r in allResults)
                        AddFeedCard(r);
                }
            }
            catch (Exception ex)
            {
                FeedStatusText.Text = AppSettings.S(
                    "搜索失败：" + ex.Message, "Search failed: " + ex.Message);
                FeedStatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                FeedLoadingRing.IsActive = false;
                FeedLoadingRing.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }
        }

        // ══ 下载 + 预览 ═════════════════════════════════════════════════════

        private static async Task<CharacterCard> DownloadFromSource(CharaSearchResult item, HttpClient http)
        {
            if (item == null) return null;

            CharacterCard card;
            switch (item.Source)
            {
                case "huayu":
                    card = await SourceHuayu.DownloadAsync(item.Id, http);
                    break;
                case "xingye":
                    card = await SourceXingyeAI.DownloadAsync(item.Id, http);
                    break;
                case "quack":
                    card = await SourceQuack.DownloadAsync(item.Id, http);
                    break;
                case "dzmm":
                    card = await SourceDZMM.DownloadAsync(item.Id, http);
                    break;
                default:
                    card = await SourceCharacterHub.DownloadAsync(item.Id, http);
                    break;
            }

            if (card != null && !card.HasAvatar && !string.IsNullOrEmpty(item.AvatarUrl))
            {
                var img = await SourceUtil.DownloadImageAsBase64Async(http, item.AvatarUrl);
                if (img != null) { card.AvatarBase64 = img.Base64; card.AvatarMimeType = img.MimeType; }
            }

            return card;
        }

        private async Task<bool> ShowCardPreviewDialog(CharacterCard card)
        {
            if (card == null) return false;

            var panel = new StackPanel();

            if (card.HasAvatar)
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(card.AvatarBase64);
                    var bmp = new BitmapImage();
                    using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        await ms.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes));
                        ms.Seek(0);
                        bmp.SetSource(ms);
                    }
                    panel.Children.Add(new Border
                    {
                        Width = 96, Height = 96, CornerRadius = new CornerRadius(48),
                        Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill },
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }
                catch { }
            }

            panel.Children.Add(new TextBlock
            {
                Text = card.Name,
                FontSize = 18, FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            TextBlock firstMsgLabel = null;
            TextBlock firstMsgText = null;
            if (!string.IsNullOrWhiteSpace(card.FirstMessage))
            {
                firstMsgLabel = new TextBlock
                {
                    Text = AppSettings.S("开场白：", "First message:"),
                    FontSize = 11, Opacity = 0.4,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                panel.Children.Add(firstMsgLabel);
                firstMsgText = new TextBlock
                {
                    Text = card.FirstMessage.Length > 120
                        ? card.FirstMessage.Substring(0, 120) + "..."
                        : card.FirstMessage,
                    FontSize = 12, Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap, MaxLines = 3,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                panel.Children.Add(firstMsgText);
            }

            TextBlock tagsText = null;
            if (!string.IsNullOrWhiteSpace(card.Tags))
            {
                tagsText = new TextBlock
                {
                    Text = card.Tags,
                    FontSize = 11, Opacity = 0.4,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                panel.Children.Add(tagsText);
            }

            TextBlock descText = null;
            var descParts = new[] { card.Description, card.Personality, card.Scenario }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            string desc = string.Join("\n", descParts);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                descText = new TextBlock
                {
                    Text = desc.Length > 200 ? desc.Substring(0, 200) + "..." : desc,
                    FontSize = 12, Opacity = 0.5,
                    TextWrapping = TextWrapping.Wrap, MaxLines = 4,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                panel.Children.Add(descText);
            }

            panel.Children.Add(new TextBlock
            {
                Text = AppSettings.S("分配到分组", "Assign to group"),
                FontSize = 13, Opacity = 0.6,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var groupCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var existingGroups = DataManager.Data.CharacterCards
                .Select(c => c.GroupName)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct().OrderBy(g => g).ToList();

            groupCombo.Items.Add(AppSettings.S("（未分组）", "(No group)"));
            foreach (var g in existingGroups)
                groupCombo.Items.Add(g);
            groupCombo.SelectedIndex = 0;

            if (!string.IsNullOrEmpty(card.GroupName))
            {
                int idx = existingGroups.IndexOf(card.GroupName);
                if (idx >= 0) groupCombo.SelectedIndex = idx + 1;
            }
            panel.Children.Add(groupCombo);

            var aiBtn = new Button
            {
                Content = AppSettings.S("AI 补全人设", "AI Complete"),
                Background = new SolidColorBrush(Color.FromArgb(200, 100, 150, 255)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(aiBtn);

            aiBtn.Click += async (s, e) =>
            {
                aiBtn.IsEnabled = false;
                aiBtn.Content = AppSettings.S("AI 补全中...", "AI completing...");

                card = await CardCompleter.CompleteCardAsync(card);

                if (!string.IsNullOrWhiteSpace(card.FirstMessage))
                {
                    if (firstMsgLabel == null)
                    {
                        int idx = panel.Children.IndexOf(aiBtn);
                        firstMsgLabel = new TextBlock
                        {
                            Text = AppSettings.S("开场白：", "First message:"),
                            FontSize = 11, Opacity = 0.4,
                            Margin = new Thickness(0, 6, 0, 2)
                        };
                        panel.Children.Insert(idx, firstMsgLabel);
                        firstMsgText = new TextBlock
                        {
                            FontSize = 12, Opacity = 0.6,
                            TextWrapping = TextWrapping.Wrap, MaxLines = 3
                        };
                        panel.Children.Insert(panel.Children.IndexOf(aiBtn), firstMsgText);
                    }
                    if (firstMsgText != null)
                        firstMsgText.Text = card.FirstMessage.Length > 120
                            ? card.FirstMessage.Substring(0, 120) + "..." : card.FirstMessage;
                }

                var newDescParts = new[] { card.Description, card.Personality, card.Scenario }
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                string newDesc = string.Join("\n", newDescParts);
                if (!string.IsNullOrWhiteSpace(newDesc))
                {
                    if (descText == null)
                    {
                        descText = new TextBlock
                        {
                            FontSize = 12, Opacity = 0.5,
                            TextWrapping = TextWrapping.Wrap, MaxLines = 4
                        };
                        int insertIdx = panel.Children.IndexOf(aiBtn);
                        if (tagsText != null) insertIdx = panel.Children.IndexOf(tagsText) + 1;
                        panel.Children.Insert(insertIdx, descText);
                    }
                    descText.Text = newDesc.Length > 200 ? newDesc.Substring(0, 200) + "..." : newDesc;
                }

                aiBtn.Content = AppSettings.S("AI 补全完成", "AI completed");
                aiBtn.IsEnabled = true;
            };

            var dlg = new ContentDialog
            {
                Title = AppSettings.S("导入角色卡", "Import Character Card"),
                Content = panel,
                PrimaryButtonText = AppSettings.S("导入", "Import"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light,
                MinWidth = 320
            };

            if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                int selIdx = groupCombo.SelectedIndex;
                if (selIdx > 0 && selIdx <= existingGroups.Count)
                    card.GroupName = existingGroups[selIdx - 1];
                else
                    card.GroupName = "";

                DataManager.Data.CharacterCards.Add(card);
                await DataManager.SaveAsync();
                ShowToast(AppSettings.S("✓ 已导入 " + card.Name, "✓ Imported " + card.Name));
                return true;
            }

            return false;
        }

        private async void ShowToast(string message)
        {
            var dlg = new ContentDialog
            {
                Title = message,
                PrimaryButtonText = "OK",
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            await dlg.ShowAsync().AsTask();
        }

        private void AddFeedCard(CharaSearchResult item)
        {
            var card = BuildFeedCard(item);
            if (_feedCardCount % 2 == 0)
                FeedCol0.Children.Add(card);
            else
                FeedCol1.Children.Add(card);
            _feedCardCount++;
        }

        // ══ 信息流加载 ═════════════════════════════════════════════════════

        private async Task LoadFeedAsync(bool clearExisting)
        {
            if (_isLoading) return;
            _isLoading = true;

            FeedLoadingRing.IsActive = true;
            FeedLoadingRing.Visibility = Visibility.Visible;
            LoadMoreBtn.Visibility = Visibility.Collapsed;

            if (clearExisting)
            {
                FeedCol0.Children.Clear();
                FeedCol1.Children.Clear();
                _feedCardCount = 0;
                _chubPage = 1;
                _huayuPage = 1;
                FeedStatusText.Visibility = Visibility.Collapsed;
            }

            try
            {
                var newResults = new List<CharaSearchResult>();
                var tasks = new List<Task<List<CharaSearchResult>>>();

                if (AppSettings.SourceEnabled_chub)
                    tasks.Add(SourceCharacterHub.BrowseAsync(_http, _chubPage));
                if (AppSettings.SourceEnabled_huayu)
                    tasks.Add(SourceHuayu.BrowseAsync(_http, _huayuPage));
                if (AppSettings.SourceEnabled_xingye)
                    tasks.Add(SourceXingyeAI.BrowseAsync(_http));
                if (AppSettings.SourceEnabled_dzmm)
                    tasks.Add(SourceDZMM.BrowseAsync(_http));

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    foreach (var t in tasks)
                    {
                        if (t.Result != null)
                            newResults.AddRange(t.Result);
                    }
                }

                if (AppSettings.SourceEnabled_chub) _chubPage++;
                if (AppSettings.SourceEnabled_huayu) _huayuPage++;

                var rng = new Random();
                for (int i = newResults.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var tmp = newResults[i];
                    newResults[i] = newResults[j];
                    newResults[j] = tmp;
                }

                if (newResults.Count == 0 && clearExisting)
                {
                    FeedStatusText.Text = AppSettings.S(
                        "暂无推荐内容，请检查网络或开启更多平台",
                        "No content. Check network or enable more platforms.");
                    FeedStatusText.Visibility = Visibility.Visible;
                }

                foreach (var r in newResults)
                    AddFeedCard(r);

                if (newResults.Count > 0)
                    LoadMoreBtn.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                FeedStatusText.Text = AppSettings.S(
                    "加载失败：" + ex.Message, "Load failed: " + ex.Message);
                FeedStatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                FeedLoadingRing.IsActive = false;
                FeedLoadingRing.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }
        }

        private async void LoadMoreBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearchMode) return;
            await LoadFeedAsync(false);
        }

        private async void FeedScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_isLoading || _isSearchMode) return;
            var sv = sender as ScrollViewer;
            if (sv == null) return;

            double distanceToBottom = sv.ExtentHeight - sv.VerticalOffset - sv.ViewportHeight;
            if (distanceToBottom < 200)
                await LoadFeedAsync(false);
        }

        // ══ 大图卡片构建 ═════════════════════════════════════════════════

        private Border BuildFeedCard(CharaSearchResult item)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 6, 0, 6),
                Background = (SolidColorBrush)Application.Current.Resources["YanshuaiPanelBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
                BorderThickness = new Thickness(1),
                Tag = item
            };

            card.Tapped += async (s, ev) =>
            {
                if (_isLoading) return;
                _isLoading = true;
                var result = s is Border b ? b.Tag as CharaSearchResult : null;
                if (result == null) { _isLoading = false; return; }
                card.Opacity = 0.5;
                try
                {
                    var chara = await DownloadFromSource(result, _http);
                    card.Opacity = 1.0;
                    _isLoading = false;
                    if (chara == null)
                    {
                        ShowToast(AppSettings.S("下载失败", "Download failed"));
                        return;
                    }
                    Frame.Navigate(typeof(NetworkCharaPreviewPage), chara);
                }
                catch (Exception ex)
                {
                    card.Opacity = 1.0;
                    _isLoading = false;
                    ShowToast(AppSettings.S("下载失败：" + ex.Message, "Download failed: " + ex.Message));
                }
            };

            var outerStack = new StackPanel();

            // ── 封面图区域 ──
            var imageGrid = new Grid { Height = 220 };

            var imageBorder = new Border
            {
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128))
            };
            if (!string.IsNullOrEmpty(item.AvatarUrl))
            {
                try
                {
                    var bmp = new BitmapImage(new Uri(item.AvatarUrl))
                    {
                        DecodePixelHeight = 720
                    };
                    imageBorder.Background = new ImageBrush
                    {
                        ImageSource = bmp,
                        Stretch = Stretch.UniformToFill
                    };
                }
                catch { }
            }
            imageGrid.Children.Add(imageBorder);

            // ── 底部渐变遮罩 + 角色名 + 来源徽章 ──
            var overlay = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 64,
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Padding = new Thickness(12, 0, 12, 10)
            };

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            nameStack.Children.Add(new TextBlock
            {
                Text = item.Name ?? "",
                FontSize = 17,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });

            var badgePanel = new StackPanel { Orientation = Orientation.Horizontal };
            badgePanel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = GetSourceBadgeColor(item.Source),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock
                {
                    Text = GetSourceDisplayName(item.Source),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            });
            nameStack.Children.Add(badgePanel);

            overlay.Child = nameStack;
            imageGrid.Children.Add(overlay);
            outerStack.Children.Add(imageGrid);

            // ── 描述文字区域 ──
            if (!string.IsNullOrEmpty(item.Description))
            {
                var textPanel = new StackPanel { Padding = new Thickness(12, 8, 12, 12) };
                textPanel.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    FontSize = 13,
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 3,
                    LineHeight = 18
                });
                outerStack.Children.Add(textPanel);
            }

            card.Child = outerStack;
            return card;
        }

        // ══ 辅助方法 ═════════════════════════════════════════════════════════

        private static SolidColorBrush GetSourceBadgeColor(string source)
        {
            switch (source)
            {
                case "chub":   return new SolidColorBrush(Color.FromArgb(200, 80, 140, 220));
                case "huayu":  return new SolidColorBrush(Color.FromArgb(200, 220, 120, 160));
                case "xingye": return new SolidColorBrush(Color.FromArgb(200, 100, 180, 120));
                case "dzmm":   return new SolidColorBrush(Color.FromArgb(200, 180, 100, 200));
                case "quack":  return new SolidColorBrush(Color.FromArgb(200, 200, 160, 80));
                default:       return new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));
            }
        }

        private static string GetSourceDisplayName(string source)
        {
            switch (source)
            {
                case "chub":   return "Chub";
                case "huayu":  return "花屿";
                case "xingye": return "星野";
                case "dzmm":   return "电子魅魔";
                case "quack":  return "云酒馆";
                default:       return source ?? "";
            }
        }
    }
}
