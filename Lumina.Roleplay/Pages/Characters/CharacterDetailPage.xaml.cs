// CharacterDetailPage.xaml.cs — 角色详情二级页面
// 角色头像+名称+简介 + 对话池列表 + 点击表头进记忆编辑

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class CharacterDetailPage : Page
    {
        private CharacterCard _card;
        private DialoguePool _pool;

        public CharacterDetailPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            // 从导航参数获取角色 ID
            var cardId = e.Parameter as string;
            if (string.IsNullOrEmpty(cardId))
            {
                // fallback: 回到角色页
                Frame.GoBack();
                return;
            }

            _card = DataManager.Data.CharacterCards.Find(c => c.Id == cardId);
            if (_card == null)
            {
                Frame.GoBack();
                return;
            }

            _pool = DialoguePoolManager.GetOrCreatePool(_card);
            LoadCharacterInfo();
            RefreshConvList();
        }

        private void LoadCharacterInfo()
        {
            // 头像
            if (_card.HasAvatar)
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(_card.AvatarBase64);
                    var bmp = new BitmapImage();
                    using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        ms.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes)).GetResults();
                        ms.Seek(0);
                        bmp.SetSource(ms);
                    }
                    AvatarBorder.Background = new ImageBrush
                    {
                        ImageSource = bmp,
                        Stretch = Stretch.UniformToFill
                    };
                    AvatarPlaceholder.Text = "";
                }
                catch { }
            }

            if (_card.HasIllustration)
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(_card.IllustrationBase64);
                    var bmp = new BitmapImage();
                    using (var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                    {
                        ms.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes)).GetResults();
                        ms.Seek(0);
                        bmp.SetSource(ms);
                    }
                    IllustrationImage.Source = bmp;
                    IllustrationFrame.Visibility = Visibility.Visible;
                }
                catch { }
            }

            // 名称
            CharaNameText.Text = _card.Name;

            // 分组
            CharaGroupText.Text = string.IsNullOrEmpty(_card.GroupName)
                ? "" : _card.GroupName;
            SelectionStatusText.Text = DataManager.Data.SelectedCharacterCardId == _card.Id
                ? AppSettings.S("当前选中角色", "Current selected character")
                : AppSettings.S("未设为当前角色", "Not the current character");

            // 简介
            var desc = _card.Description;
            if (!string.IsNullOrEmpty(_card.Personality))
            {
                if (!string.IsNullOrEmpty(desc)) desc += "\n";
                desc += _card.Personality;
            }
            CharaDescText.Text = string.IsNullOrEmpty(desc)
                ? AppSettings.S("暂无简介", "No description")
                : desc;

            // 池标题
            if (_pool != null)
            {
                int count = _pool.CachedConversations?.Count ?? 0;
                var up = GetPoolUserProfile();
                string persona = string.IsNullOrEmpty(up?.Name) ? "" : $" / {up.Name}";
                PoolTitleText.Text = AppSettings.S($"对话池 ({count}){persona}", $"Dialogue Pool ({count}){persona}");
            }

            // R18G 标签
            R18GBadge.Visibility = _card.IsR18G ? Visibility.Visible : Visibility.Collapsed;
        }

        private UserProfile GetPoolUserProfile()
        {
            if (_pool == null) return DataManager.GetActiveUserProfile();
            var profiles = DataManager.Data?.UserProfiles;
            if (profiles != null)
            {
                var matched = profiles.FirstOrDefault(p => p.Id == _pool.UserProfileId);
                if (matched != null) return matched;
            }
            if (DataManager.Data?.UserProfile?.Id == _pool.UserProfileId)
                return DataManager.Data.UserProfile;
            return DataManager.GetActiveUserProfile();
        }

        private void RefreshConvList()
        {
            ConvContainer.Children.Clear();

            if (_pool == null)
            {
                ConvContainer.Children.Add(new TextBlock
                {
                    Text = AppSettings.S("暂无对话池", "No dialogue pool"),
                    FontSize = 13, Opacity = 0.3,
                    Margin = new Thickness(4, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            var convs = _pool.CachedConversations
                .OrderByDescending(c => c.UpdatedAt)
                .ToList();

            // 新建对话按钮 — 始终显示
            var newConvBtn = new Button
            {
                Content = AppSettings.S("＋ 新建对话", "+ New Conversation"),
                Background = new SolidColorBrush(Color.FromArgb(200, 184, 110, 71)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 8, 14, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 8)
            };
            newConvBtn.Click += (s, e) => StartNewConversation();
            ConvContainer.Children.Add(newConvBtn);

            if (convs.Count == 0)
            {
                ConvContainer.Children.Add(new TextBlock
                {
                    Text = AppSettings.S("还没有对话记录", "No conversations yet"),
                    FontSize = 13, Opacity = 0.3,
                    Margin = new Thickness(4, 16, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var conv in convs)
            {
                var convItem = BuildConvItem(conv);
                ConvContainer.Children.Add(convItem);
            }
        }

        private Border BuildConvItem(Conversation conv)
        {
            var item = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 2),
                Background = new SolidColorBrush(AppSettings.IsDark
                    ? Color.FromArgb(20, 255, 255, 255)
                    : Color.FromArgb(12, 0, 0, 0)),
                Tag = conv
            };

            // 右键菜单：删除对话
            var flyout = new MenuFlyout();
            var deleteItem = new MenuFlyoutItem
            {
                Text = AppSettings.S("删除对话", "Delete Conversation"),
                Icon = new FontIcon { Glyph = "\uE74D", FontSize = 14 }
            };
            deleteItem.Click += async (s, e) =>
            {
                var dlg = new ContentDialog
                {
                    Title = AppSettings.S("删除对话", "Delete Conversation"),
                    Content = AppSettings.S($"确定删除「{conv.Title}」？", $"Delete '{conv.Title}'?"),
                    PrimaryButtonText = AppSettings.S("删除", "Delete"),
                    SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                    RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
                };
                if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
                {
                    DialoguePoolManager.RemoveConversationEverywhere(conv);
                    DataManager.Data.Conversations.Remove(conv);
                    await DataManager.SaveAsync();
                    RefreshConvList();
                }
            };
            flyout.Items.Add(deleteItem);
            FlyoutBase.SetAttachedFlyout(item, flyout);
            item.RightTapped += (s_rt, ev_rt) =>
            {
                ev_rt.Handled = true;
                FlyoutBase.ShowAttachedFlyout(item);
            };

            // 点击进入对话
            item.Tapped += (s, e) =>
            {
                AppState.ActiveConversation = conv;
                Frame.Navigate(typeof(MainPage));
            };

            // 布局
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var innerStack = new StackPanel();
            innerStack.Children.Add(new TextBlock
            {
                Text = conv.Title,
                FontSize = 15,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                TextTrimming = Windows.UI.Xaml.TextTrimming.CharacterEllipsis
            });

            var preview = conv.LastMessagePreview;
            if (!string.IsNullOrEmpty(preview))
            {
                innerStack.Children.Add(new TextBlock
                {
                    Text = preview,
                    FontSize = 12,
                    Opacity = 0.45,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextTrimming = Windows.UI.Xaml.TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                });
            }

            Grid.SetColumn(innerStack, 0);
            grid.Children.Add(innerStack);

            var timeLabel = new TextBlock
            {
                Text = conv.UpdatedAtDisplay,
                FontSize = 11,
                Opacity = 0.35,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(timeLabel, 1);
            grid.Children.Add(timeLabel);

            item.Child = grid;
            return item;
        }

        private void StartNewConversation()
        {
            if (_card == null) return;
            DataManager.Data.SelectedCharacterCardId = _card.Id;
            var conv = DataManager.CreateNewConversation();
            conv.MemoryEnabled = true;
            AppState.ActiveConversation = conv;

            if (_pool != null && !_pool.ConversationIds.Contains(conv.Id))
                _pool.AddConversation(conv);

            Frame.Navigate(typeof(MainPage));
        }

        private void MemoryHeader_Click(object sender, RoutedEventArgs e)
        {
            if (_pool == null) return;
            Frame.Navigate(typeof(MemoryEditPage), _pool);
        }

        private async void R18GToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            _card.IsR18G = !_card.IsR18G;
            R18GBadge.Visibility = _card.IsR18G ? Visibility.Visible : Visibility.Collapsed;
            await DataManager.SaveAsync();
        }

        private void EditCharaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            CharaWizardData.Reset();
            CharaWizardData.LoadFromCard(_card);
            CharaWizardData.ReturnTarget = "CharacterCardsPage";
            Frame.Navigate(typeof(CharaWizardPage1));
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            try
            {
                string json = ImportExportPage.BuildStCharaJson(_card);
                string safe = ImportExportPage.MakeSafeFilename(_card.Name);
                if (_card.HasAvatar)
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = safe
                    };
                    picker.FileTypeChoices.Add("PNG 角色卡", new List<string> { ".png" });
                    var file = await picker.PickSaveFileAsync();
                    if (file == null) return;
                    byte[] pngBytes = Convert.FromBase64String(_card.AvatarBase64);
                    byte[] output = ImportExportPage.InjectPngTextChunk(pngBytes, "chara",
                                        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
                    await FileIO.WriteBytesAsync(file, output);
                }
                else
                {
                    var picker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = safe
                    };
                    picker.FileTypeChoices.Add("JSON 角色卡", new List<string> { ".json" });
                    var file = await picker.PickSaveFileAsync();
                    if (file == null) return;
                    await FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                }
            }
            catch { }
        }

        private void NewConvAppBarBtn_Click(object sender, RoutedEventArgs e)
        {
            StartNewConversation();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(CharacterCardsPage));
        }
    }
}
