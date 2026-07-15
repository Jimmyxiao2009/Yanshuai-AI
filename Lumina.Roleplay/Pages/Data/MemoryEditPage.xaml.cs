// MemoryEditPage.xaml.cs — 记忆编辑页面
// 分两标签：用户画像、RAG 查询

using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MemoryEditPage : Page
    {
        private DialoguePool _pool;
        private CharacterCard _card;

        public MemoryEditPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);

            _pool = e.Parameter as DialoguePool;
            if (_pool == null)
            {
                Frame.GoBack();
                return;
            }

            _card = DataManager.Data.CharacterCards.Find(c => c.Id == _pool.CharacterCardId);
            SectionPivot.SelectionChanged += OnPivotChanged;
            RenderProfileTab();
        }

        private void OnPivotChanged(object sender, SelectionChangedEventArgs e)
        {
            AddBtn.Visibility = Visibility.Collapsed;
            if (SectionPivot.SelectedItem == MemoriesPivot)
            {
                RenderMemoriesTab();
            }
            else
            {
                RenderProfileTab();
            }
        }

        private void ClearContent()
        {
            ContentArea.Content = null;
        }

        private void RenderProfileTab()
        {
            AddBtn.Visibility = Visibility.Collapsed;
            var stack = new StackPanel();

            AddSectionTitle(stack, AppSettings.S("人设用户画像", "Persona Profile"));
            AddHintText(stack, AppSettings.S("这里显示当前对话池绑定的人设。一个角色 + 一个用户人设对应一个独立对话池。", "This shows the persona bound to the current dialogue pool."));
            var profiles = DataManager.Data.UserProfiles ?? new List<UserProfile>();
            if (profiles.Count == 0 && DataManager.Data.UserProfile != null)
                profiles.Add(DataManager.Data.UserProfile);
            var userPicker = new ComboBox
            {
                ItemsSource = profiles,
                DisplayMemberPath = "Name",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 10)
            };
            userPicker.SelectedItem = profiles.FirstOrDefault(p => p.Id == _pool.UserProfileId)
                ?? DataManager.GetActiveUserProfile();
            userPicker.SelectionChanged += (s, e) =>
            {
                var selected = userPicker.SelectedItem as UserProfile;
                if (selected == null || _card == null) return;
                _pool = DialoguePoolManager.GetOrCreatePool(_card, selected.Id);
                if (string.IsNullOrWhiteSpace(_pool.Profile.UserPortrait))
                    _pool.Profile.UserPortrait = selected.Description ?? "";
                _ = DataManager.SaveAsync();
                RenderProfileTab();
            };
            stack.Children.Add(userPicker);

            var bound = profiles.FirstOrDefault(p => p.Id == _pool.UserProfileId)
                ?? DataManager.GetActiveUserProfile();
            AddReadonlyField(stack, AppSettings.S("当前池绑定人设", "Pool Persona"),
                string.IsNullOrEmpty(bound?.Name) ? AppSettings.S("未命名人设", "Unnamed Persona") : bound.Name);

            if (_card != null)
            {
                AddReadonlyField(stack, AppSettings.S("当前角色", "Current Character"), _card.Name);
            }

            AddSectionTitle(stack, AppSettings.S("人设描述", "Persona Description"));
            if (string.IsNullOrWhiteSpace(bound?.Description))
                AddEmptyHint(stack, AppSettings.S("暂无人设描述", "No persona description"));
            else
                AddReadonlyCard(stack, bound.Description.Trim());

            AddAgentEditor(stack);
            ContentArea.Content = stack;
        }

        private void RenderMemoriesTab()
        {
            AddBtn.Visibility = Visibility.Collapsed;

            var stack = new StackPanel();
            AddSectionTitle(stack, AppSettings.S("RAG 记忆检索", "RAG Memory Search"));
            AddRagScopeCard(stack);
            AddHintText(stack, AppSettings.S("输入关键词只会搜索当前对话池的池级 RAG 记忆。", "Search only pool-level RAG memories in the current dialogue pool."));

            var searchBox = new TextBox
            {
                PlaceholderText = AppSettings.S("搜索记忆...", "Search memories..."),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 6, 10, 6)
            };

            var resultPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            searchBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter)
                {
                    ev.Handled = true;
                    var query = searchBox.Text.Trim();
                    if (string.IsNullOrEmpty(query)) return;

                    RenderRagResults(resultPanel, query);
                }
            };

            stack.Children.Add(searchBox);
            stack.Children.Add(resultPanel);
            AddPoolSettings(stack);
            ContentArea.Content = stack;
        }

        private async void AddMemory()
        {
            var editBox = new TextBox
            {
                AcceptsReturn = true,
                Height = 100,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                PlaceholderText = AppSettings.S("输入记忆内容...", "Enter memory content...")
            };

            var importanceSlider = new Slider
            {
                Minimum = 0.1,
                Maximum = 2.0,
                Value = 1.0,
                StepFrequency = 0.1,
                Header = AppSettings.S("重要性", "Importance"),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var panel = new StackPanel();
            panel.Children.Add(editBox);
            panel.Children.Add(importanceSlider);

            var dlg = new ContentDialog
            {
                Title = AppSettings.S("添加池级 RAG 记忆", "Add Pool RAG Memory"),
                Content = panel,
                PrimaryButtonText = AppSettings.S("添加", "Add"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };

            if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                var text = editBox.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    _pool.AddSharedMemory(text, "", (float)importanceSlider.Value);
                    await DataManager.SaveAsync();
                    RenderMemoriesTab();
                }
            }
        }

        private void AddAgentEditor(Panel parent)
        {
            var profile = _pool?.Profile;
            if (profile == null)
            {
                AddEmptyHint(parent, AppSettings.S("当前对话池没有深层记忆容器。", "No deep memory container for this pool."));
                return;
            }

            AddSectionTitle(parent, AppSettings.S("Agent.md 深层记忆", "Agent.md Deep Memory"), 20);
            AddDeepMemoryScopeCard(parent);
            AddHintText(parent, AppSettings.S("这里编辑稳定长期记忆。它会作为 Agent.md 式上下文注入系统提示词；池级 RAG 会另行按关键词检索。", "Edit stable long-term memory here. It is injected like Agent.md context; pool RAG is retrieved separately by keyword."));

            if (profile.CoreTraits == null) profile.CoreTraits = new List<string>();
            if (profile.ExperienceItems == null) profile.ExperienceItems = new List<string>();
            if (profile.KnownFacts == null) profile.KnownFacts = new List<string>();

            AddSectionTitle(parent, AppSettings.S("总体认知", "Overall Understanding"));
            var portraitBox = new TextBox
            {
                Text = profile.UserPortrait ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                PlaceholderText = AppSettings.S("例如：他知道我是……", "What this character knows about me..."),
                Margin = new Thickness(0, 2, 0, 10)
            };
            portraitBox.LostFocus += async (s, e) =>
            {
                profile.UserPortrait = portraitBox.Text.Trim();
                await DataManager.SaveAsync();
            };
            parent.Children.Add(portraitBox);

            AddAgentListSection(parent, AppSettings.S("对我的印象", "Impressions"),
                profile.CoreTraits, AppSettings.S("暂无印象", "No impressions"),
                AppSettings.S("＋ 添加印象", "+ Add Impression"));
            AddAgentListSection(parent, AppSettings.S("关键互动经历", "Key Interactions"),
                profile.ExperienceItems, AppSettings.S("暂无互动经历", "No interactions"),
                AppSettings.S("＋ 添加互动经历", "+ Add Interaction"));
            AddAgentListSection(parent, AppSettings.S("已确认事实", "Known Facts"),
                profile.KnownFacts, AppSettings.S("暂无关于我的事实", "No known facts about me"),
                AppSettings.S("＋ 添加关于我的事实", "+ Add Fact About Me"));
        }

        private void AddAgentListSection(Panel parent, string title, List<string> list, string emptyText, string addText)
        {
            AddSectionTitle(parent, title);
            if (list == null)
            {
                AddEmptyHint(parent, emptyText);
                return;
            }

            if (list.Count == 0)
            {
                AddEmptyHint(parent, emptyText);
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    var item = AddEditableItem(parent, list[i], () => EditMemoryItem(list, idx));
                    AddDeleteHandler(item, async () =>
                    {
                        list.RemoveAt(idx);
                        await DataManager.SaveAsync();
                        RenderProfileTab();
                    });
                }
            }

            AddAddButton(parent, addText, () =>
            {
                list.Add("");
                RenderProfileTab();
            });
        }

        private void AddPoolSettings(Panel parent)
        {
            AddSectionTitle(parent, AppSettings.S("池设置", "Pool Settings"), 20);

            var settings = _pool?.Settings;
            if (settings == null) return;

            AddSettingsToggle(parent, "自动沉淀池级 RAG", "Auto Store Pool RAG",
                settings.EnableSharedMemory, v => settings.EnableSharedMemory = v);
            AddSettingsToggle(parent, "启用 RAG", "Enable RAG",
                settings.EnableRAG, v => settings.EnableRAG = v);
            AddSettingsToggle(parent, "自动摘要", "Auto Summarize",
                settings.AutoSummarizeConversations, v => settings.AutoSummarizeConversations = v);
        }

        private void AddRagScopeCard(Panel parent)
        {
            AddScopeCard(parent, AppSettings.S("当前检索范围", "Current Search Scope"));
        }

        private void AddDeepMemoryScopeCard(Panel parent)
        {
            AddScopeCard(parent, AppSettings.S("当前 Agent.md 范围", "Current Agent.md Scope"));
        }

        private void AddScopeCard(Panel parent, string title)
        {
            var memories = _pool?.SharedMemories ?? new List<PoolMemoryItem>();
            var persona = GetPoolUserProfile();
            string cardName = string.IsNullOrWhiteSpace(_card?.Name)
                ? AppSettings.S("未绑定角色", "No character")
                : _card.Name;
            string personaName = string.IsNullOrWhiteSpace(persona?.Name)
                ? AppSettings.S("未命名人设", "Unnamed persona")
                : persona.Name;

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 2, 0, 10),
                Background = new SolidColorBrush(AppSettings.IsDark
                    ? Color.FromArgb(24, 255, 255, 255)
                    : Color.FromArgb(14, 0, 0, 0)),
                BorderBrush = (SolidColorBrush)Application.Current.Resources["YanshuaiBorderBrush"],
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Opacity = 0.65
            });
            stack.Children.Add(new TextBlock
            {
                Text = AppSettings.S(
                    $"角色：{cardName} / 人设：{personaName} / 池级 RAG：{memories.Count} 条",
                    $"Character: {cardName} / Persona: {personaName} / Pool RAG: {memories.Count}"),
                FontSize = 12,
                Opacity = 0.45,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            border.Child = stack;
            parent.Children.Add(border);
        }

        private static bool IsDeepMemoryEmpty(CharacterProfile profile)
        {
            return string.IsNullOrWhiteSpace(profile.UserPortrait) &&
                (profile.CoreTraits == null || !profile.CoreTraits.Any(x => !string.IsNullOrWhiteSpace(x))) &&
                (profile.ExperienceItems == null || !profile.ExperienceItems.Any(x => !string.IsNullOrWhiteSpace(x))) &&
                (profile.KnownFacts == null || !profile.KnownFacts.Any(x => !string.IsNullOrWhiteSpace(x)));
        }

        private void AddDeepPreviewBlock(Panel parent, string title, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            AddSectionTitle(parent, title, 14);
            AddReadonlyCard(parent, text.Trim());
        }

        private void AddDeepPreviewList(Panel parent, string title, List<string> items)
        {
            if (items == null || !items.Any(x => !string.IsNullOrWhiteSpace(x))) return;
            AddSectionTitle(parent, title, 14);
            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x)))
                AddReadonlyCard(parent, "- " + item.Trim());
        }

        private void AddReadonlyCard(Panel parent, string text)
        {
            // 心象：记忆内容卡片用 CardBorderStyle，正文用 VoiceTextStyle（文楷）
            parent.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardBorderStyle"],
                Margin = new Thickness(0, 2, 0, 8),
                Child = new TextBlock
                {
                    Style = (Style)Application.Current.Resources["VoiceTextStyle"],
                    Text = text,
                    TextWrapping = TextWrapping.Wrap
                }
            });
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

        private void RenderRagResults(Panel resultPanel, string query)
        {
            resultPanel.Children.Clear();
            var results = new List<string>();
            int memoryCount = _pool?.SharedMemories?.Count ?? 0;

            if (_pool != null)
                results.AddRange(_pool.SearchMemories(query, _pool.Settings?.RAGTopK ?? 5));

            results = results
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .Take(10)
                .ToList();

            if (results.Count == 0)
            {
                string emptyText = memoryCount == 0
                    ? AppSettings.S("当前对话池还没有池级 RAG 记忆。请先手动总结对话，或开启自动摘要沉淀。", "The current dialogue pool has no pool-level RAG memories yet.")
                    : AppSettings.S($"当前池有 {memoryCount} 条 RAG 记忆，但没有命中「{query}」。", $"The current pool has {memoryCount} RAG memories, but none matched '{query}'.");
                resultPanel.Children.Add(new TextBlock
                {
                    Text = emptyText,
                    FontSize = 12,
                    Opacity = 0.45,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            resultPanel.Children.Add(new TextBlock
            {
                Text = AppSettings.S($"找到 {results.Count} 条相关记忆", $"Found {results.Count} relevant memories"),
                FontSize = 12,
                Opacity = 0.45,
                Margin = new Thickness(0, 4, 0, 8)
            });

            foreach (var r in results)
            {
                // 心象：RAG 命中记忆卡片用 CardBorderStyle，正文用 VoiceTextStyle（文楷）
                resultPanel.Children.Add(new Border
                {
                    Style = (Style)Application.Current.Resources["CardBorderStyle"],
                    Margin = new Thickness(0, 2, 0, 8),
                    Child = new TextBlock
                    {
                        Style = (Style)Application.Current.Resources["VoiceTextStyle"],
                        Text = r,
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }
        }

        // ══ UI 辅助方法 ═══════════════════════════════════════════════

        private void AddSectionTitle(Panel parent, string text, double topMargin = 16)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Opacity = 0.6,
                CharacterSpacing = 40,
                Margin = new Thickness(0, topMargin, 0, 6)
            });
        }

        private void AddHintText(Panel parent, string text)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Opacity = 0.35,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private async void EditPoolMemoryItem(PoolMemoryItem mem)
        {
            var editBox = new TextBox
            {
                Text = mem.Summary,
                AcceptsReturn = true,
                Height = 100,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var dlg = new ContentDialog
            {
                Title = AppSettings.S("编辑记忆", "Edit Memory"),
                Content = editBox,
                PrimaryButtonText = AppSettings.S("保存", "Save"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };
            if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                mem.Summary = editBox.Text.Trim();
                await DataManager.SaveAsync();
                RenderMemoriesTab();
            }
        }

        private void AddEmptyHint(Panel parent, string text)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                Opacity = 0.3,
                Margin = new Thickness(4, 6, 0, 6)
            });
        }

        private void AddReadonlyField(Panel parent, string label, string value)
        {
            parent.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 13,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private Border AddEditableItem(Panel parent, string text, Action editAction)
        {
            // 心象：可编辑记忆条目用 CardBorderStyle，正文用 VoiceTextStyle（文楷）
            var item = new Border
            {
                Style = (Style)Application.Current.Resources["CardBorderStyle"],
                Margin = new Thickness(0, 2, 0, 8),
                Tag = editAction
            };
            item.Tapped += (s, ev) => editAction();

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            g.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["VoiceTextStyle"],
                Text = string.IsNullOrEmpty(text) ? AppSettings.S("(空)", "(empty)") : text,
                TextWrapping = TextWrapping.Wrap,
                Opacity = string.IsNullOrEmpty(text) ? 0.3 : 0.8,
                TextTrimming = Windows.UI.Xaml.TextTrimming.CharacterEllipsis,
                MaxLines = 3
            });

            g.Children.Add(new FontIcon
            {
                Glyph = "\uE70F",
                FontSize = 13,
                Opacity = 0.3,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            Grid.SetColumn((FrameworkElement)g.Children[1], 1);

            item.Child = g;
            parent.Children.Add(item);
            return item;
        }

        private void AddDeleteHandler(Border item, Action deleteAction)
        {
            var flyout = new MenuFlyout();
            var del = new MenuFlyoutItem
            {
                Text = AppSettings.S("删除", "Delete"),
                Icon = new FontIcon { Glyph = "\uE74D", FontSize = 14 }
            };
            del.Click += (s, ev) => deleteAction();
            flyout.Items.Add(del);
            FlyoutBase.SetAttachedFlyout(item, flyout);
            item.RightTapped += (s, ev) =>
            {
                ev.Handled = true;
                FlyoutBase.ShowAttachedFlyout(item);
            };
        }

        private void AddAddButton(Panel parent, string text, Action action)
        {
            var btn = new Button
            {
                Content = text,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                Foreground = new SolidColorBrush(Color.FromArgb(150, 200, 200, 200)),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            btn.Click += (s, ev) => action();
            parent.Children.Add(btn);
        }

        private void AddSettingsToggle(Panel parent, string cnLabel, string enLabel,
            bool initialValue, Action<bool> setter)
        {
            var toggle = new ToggleSwitch
            {
                Header = AppSettings.S(cnLabel, enLabel),
                IsOn = initialValue,
                Margin = new Thickness(0, 4, 0, 0)
            };
            toggle.Toggled += (s, ev) => { setter(toggle.IsOn); _ = DataManager.SaveAsync(); };
            parent.Children.Add(toggle);
        }

        private async void EditMemoryItem(List<string> list, int index)
        {
            var editBox = new TextBox
            {
                Text = index < list.Count ? list[index] : "",
                AcceptsReturn = true,
                Height = 80,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var dlg = new ContentDialog
            {
                Title = AppSettings.S("编辑", "Edit"),
                Content = editBox,
                PrimaryButtonText = AppSettings.S("保存", "Save"),
                SecondaryButtonText = AppSettings.S("取消", "Cancel"),
                RequestedTheme = AppSettings.IsDark ? ElementTheme.Dark : ElementTheme.Light
            };

            if (await dlg.ShowAsync().AsTask() == ContentDialogResult.Primary)
            {
                if (index < list.Count)
                {
                    list[index] = editBox.Text.Trim();
                }
                await DataManager.SaveAsync();
                RenderProfileTab();
            }
        }

        // ══ 事件 ═══════════════════════════════════════════════════════

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            await DataManager.SaveAsync();
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SectionPivot.SelectedItem == MemoriesPivot)
                AddMemory();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
