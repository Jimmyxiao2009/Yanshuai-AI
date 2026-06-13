using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace yanshuai
{
    public sealed partial class EvaluationPage : Page
    {
        private EvaluationService _service;
        private ObservableCollection<EvaluationResult> _results;

        public EvaluationPage()
        {
            this.InitializeComponent();
            _service = new EvaluationService();
            _results = new ObservableCollection<EvaluationResult>();
            ResultsList.ItemsSource = _results;
            ServerUrlBox.Text = AppSettings.PlaaServerUrl ?? "";
            LoadExperiments();
        }

        // ── 加载实验 ──

        private void LoadExperiments()
        {
            ExperimentsPanel.Children.Clear();

            // 标题
            ExperimentsPanel.Children.Add(new TextBlock
            {
                Text = "可用实验",
                FontSize = 16,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            });

            // 预置实验
            foreach (var exp in EvaluationService.PresetExperiments)
            {
                var card = CreateExperimentCard(exp);
                ExperimentsPanel.Children.Add(card);
            }

            // 已保存的实验
            if (DataManager.Data.EvaluationExperiments?.Count > 0)
            {
                var sep = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Windows.UI.Colors.Gray),
                    Margin = new Thickness(0, 8, 0, 8),
                    Opacity = 0.3,
                };
                ExperimentsPanel.Children.Add(sep);
                ExperimentsPanel.Children.Add(new TextBlock
                {
                    Text = "已保存",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                    Margin = new Thickness(0, 0, 0, 4),
                });

                foreach (var exp in DataManager.Data.EvaluationExperiments)
                {
                    var card = CreateExperimentCard(exp);
                    ExperimentsPanel.Children.Add(card);
                }
            }
        }

        private Border CreateExperimentCard(EvaluationExperiment exp)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 12, 12, 12),
                Margin = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 100, 100, 255)),
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = exp.Name,
                FontSize = 15,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
            });
            stack.Children.Add(new TextBlock
            {
                Text = exp.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4),
            });

            var infoRow = new StackPanel { Orientation = Orientation.Horizontal };
            infoRow.Children.Add(new TextBlock
            {
                Text = $"{exp.Turns} 轮",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
            });
            infoRow.Children.Add(new TextBlock
            {
                Text = exp.ExperimentType,
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 100, 180, 255)),
            });
            stack.Children.Add(infoRow);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0),
            };

            var runBtn = new Button
            {
                Content = "运行实验",
                FontSize = 12,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 50, 150, 50)),
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                Tag = exp,
            };
            runBtn.Click += RunExperiment_Click;
            btnPanel.Children.Add(runBtn);

            // 查看历史结果
            var existing = _results.FirstOrDefault(r => r.ExperimentId == exp.Id);
            if (existing != null)
            {
                var viewBtn = new Button
                {
                    Content = "查看结果",
                    FontSize = 12,
                    Tag = existing,
                };
                viewBtn.Click += (s, e) => ShowResult(existing);
                btnPanel.Children.Add(viewBtn);
            }

            stack.Children.Add(btnPanel);
            card.Child = stack;
            return card;
        }

        // ── 实验运行 ──

        private async void RunExperiment_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var experiment = btn?.Tag as EvaluationExperiment;
            if (experiment == null) return;

            // 配置
            var url = ServerUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StatusText.Text = "请先输入 PLAA 服务器地址";
                return;
            }
            _service.Configure(url, AppSettings.PlaaApiKey);

            // 准备 UI
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 0;
            ProgressText.Text = $"运行: {experiment.Name}...";
            btn.IsEnabled = false;

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    ProgressText.Text = msg;
                    // 简单估算进度
                    if (msg.Contains("Turn"))
                    {
                        var parts = msg.Split('/');
                        if (parts.Length >= 2 && int.TryParse(parts[0].Trim().Split(' ').Last(), out var turn)
                            && int.TryParse(parts[1].Trim().Split(' ')[0], out var total))
                        {
                            ProgressBar.Value = (double)turn / total * 50; // 50% per model
                            if (msg.Contains("PLAA")) ProgressBar.Value += 50;
                        }
                    }
                });

                var result = await Task.Run(() => _service.RunExperimentAsync(experiment, progress));

                // 保存
                DataManager.Data.EvaluationResults.Add(result);
                _results.Add(result);
                await DataManager.SaveAsync();

                StatusText.Text = $"{experiment.Name} 完成 ✅";
                ShowResult(result);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"错误: {ex.Message}";
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                btn.IsEnabled = true;
                LoadExperiments(); // 刷新卡片
            }
        }

        // ── 结果展示 ──

        private void ShowResult(EvaluationResult result)
        {
            // 切换到右侧显示详细结果
            ResultsPanel.Children.Clear();
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = $"{result.ExperimentName} — 结果详情",
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            });
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = $"时间: {result.Timestamp:yyyy-MM-dd HH:mm:ss}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
            });

            // 指标表格
            if (result.Metrics.Count > 0)
            {
                var metricsBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 12, 12, 12),
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(15, 255, 255, 255)),
                };
                var metricsStack = new StackPanel();
                metricsStack.Children.Add(new TextBlock
                {
                    Text = "指标",
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                });
                foreach (var kv in result.Metrics)
                {
                    metricsStack.Children.Add(new TextBlock
                    {
                        Text = $"  {kv.Key}: {kv.Value:F4}",
                        FontSize = 12,
                        Margin = new Thickness(0, 2, 0, 2),
                    });
                }
                metricsBorder.Child = metricsStack;
                ResultsPanel.Children.Add(metricsBorder);
            }

            // 对话记录摘要
            var convBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.Gray),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 12, 12, 12),
                Margin = new Thickness(0, 4, 0, 4),
            };
            var convStack = new StackPanel();
            convStack.Children.Add(new TextBlock
            {
                Text = "对话记录",
                FontSize = 14,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
            });

            var baselineCount = result.BaselineMessages.Count;
            var plaaCount = result.PlaaMessages.Count;
            convStack.Children.Add(new TextBlock
            {
                Text = $"Baseline: {baselineCount} 条消息 | PLAA: {plaaCount} 条消息",
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
            });

            // 查看详情按钮
            var detailBtn = new Button
            {
                Content = "查看详细对话",
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                Tag = result,
            };
            detailBtn.Click += (s, e) => ShowConversationDetail(result);
            convStack.Children.Add(detailBtn);

            convBorder.Child = convStack;
            ResultsPanel.Children.Add(convBorder);

            // 可视化按钮
            var chartBtn = new Button
            {
                Content = "查看 S_t 轨迹图",
                FontSize = 13,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 80, 80, 180)),
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                Margin = new Thickness(0, 8, 0, 8),
                Tag = result,
            };
            chartBtn.Click += (s, e) => ShowTrajectoryChart(result);
            ResultsPanel.Children.Add(chartBtn);
        }

        private void ShowConversationDetail(EvaluationResult result)
        {
            // 打开对话回放
            var detailDialog = new ContentDialog
            {
                Title = $"{result.ExperimentName} — 对话详情",
                CloseButtonText = "关闭",
                MaxWidth = 800,
            };

            var scroll = new ScrollViewer { MaxHeight = 500 };
            var stack = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };

            stack.Children.Add(new TextBlock
            {
                Text = "=== Baseline ===",
                FontSize = 14,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 200, 100, 100)),
            });
            foreach (var msg in result.BaselineMessages.Take(20))
            {
                var label = msg.Role == "user" ? "🧑" : "🤖";
                var text = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
                stack.Children.Add(new TextBlock
                {
                    Text = $"{label} {msg.Role}: {text}",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = "\n=== PLAA ===",
                FontSize = 14,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 100, 200, 100)),
            });
            foreach (var msg in result.PlaaMessages.Take(20))
            {
                var label = msg.Role == "user" ? "🧑" : "🤖";
                var text = msg.Content.Length > 100 ? msg.Content.Substring(0, 100) + "..." : msg.Content;
                var hasLatent = !string.IsNullOrEmpty(msg.LatentStateJson) ? " [S_t]" : "";
                stack.Children.Add(new TextBlock
                {
                    Text = $"{label} {msg.Role}: {text}{hasLatent}",
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
            }

            scroll.Content = stack;
            detailDialog.Content = scroll;
            _ = detailDialog.ShowAsync();
        }

        private void ShowTrajectoryChart(EvaluationResult result)
        {
            // 在 ChartContainer Canvas 中绘制轨迹
            ChartContainer.Visibility = Visibility.Visible;
            var drawer = new LatentChartDrawer();
            TrajectoryCanvas.Width = TrajectoryCanvas.ActualWidth > 10
                ? TrajectoryCanvas.ActualWidth : 640;
            TrajectoryCanvas.Height = 360;
            drawer.Draw(TrajectoryCanvas, result);
        }

        // ── 事件处理 ──

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.PlaaServerUrl = ServerUrlBox.Text.Trim();
            StatusText.Text = "配置已保存";
        }

        private void LoadExperiments_Click(object sender, RoutedEventArgs e)
        {
            LoadExperiments();
            StatusText.Text = "已刷新";
        }
    }
}
