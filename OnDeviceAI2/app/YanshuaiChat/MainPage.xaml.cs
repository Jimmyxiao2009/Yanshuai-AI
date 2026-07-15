using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Yanshuai.Qwen;

namespace YanshuaiChat
{
    /// <summary>
    /// 言枢对话：完全离线的端侧 Qwen3.5-0.8B(int8) 聊天。
    /// 模型文件经设备门户推送到 LocalState\qwen3_5_0_8b-int8.llmmodel。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const string ModelFileName = "qwen3_5_0_8b-int8.llmmodel";
        const string SystemPrompt = "你是言枢，一个运行在这台手机上的离线智能助手。回答保持简短。";
        const int MaxNewTokens = 200;
        const int ImStartId = 248045;
        const int ImEndId = 248046;

        QwenModel _model;
        QwenRunner _runner;
        BpeTokenizer _tokenizer;
        BpeDecoder _decoder;
        bool _busy;
        readonly List<KeyValuePair<string, string>> _history = new List<KeyValuePair<string, string>>();

        public MainPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
        }

        async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            await TryLoadModelAsync();
        }

        /// <summary>模型文件在 LocalState 里就加载；不在就提示用"导入模型"。</summary>
        async Task TryLoadModelAsync()
        {
            try
            {
                ulong limit = Windows.System.MemoryManager.AppMemoryUsageLimit;
                string path = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, ModelFileName);
                if (!System.IO.File.Exists(path))
                {
                    Status($"内存上限 {limit / 1024 / 1024} MB。\n未找到模型文件，请点下方【导入模型】，" +
                           $"从手机本地存储（存储卡/Downloads 等）选择 {ModelFileName}。");
                    return;
                }

                long size = new System.IO.FileInfo(path).Length;
                Status($"正在加载模型（{size / 1024 / 1024} MB，请稍候，约需 1-2 分钟）…");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                await Task.Run(() =>
                {
                    _model = QwenModel.Load(path);
                    _runner = new QwenRunner(_model);
                    _tokenizer = new BpeTokenizer(_model.Vocab, _model.Merges, _model.EosId);
                    _decoder = new BpeDecoder(_model.Vocab);
                });
                Status($"模型就绪（加载 {sw.Elapsed.TotalSeconds:0.0}s，" +
                       $"当前占用 {Windows.System.MemoryManager.AppMemoryUsage / 1024 / 1024} MB）。开始对话吧。");
                InputBox.IsEnabled = true;
                SendButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                App.LogCrash("LoadModel", ex);
                Status("模型加载失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 设备门户上传大文件到 LocalState 经常失败/超时，改由应用自己拉取：
        /// 用 FileOpenPicker 从手机本地任意可访问位置（存储卡、Downloads…）选取
        /// .llmmodel 文件，流式拷贝进 LocalState，边拷贝边报进度。
        /// </summary>
        async void OnImportModelClick(object sender, RoutedEventArgs e)
        {
            if (_copying) return;
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Downloads,
                };
                picker.FileTypeFilter.Add(".llmmodel");
                picker.FileTypeFilter.Add("*");

                StorageFile source = await picker.PickSingleFileAsync();
                if (source == null) return;

                if (_runner != null)
                {
                    Status("已有模型在运行中；导入完成后请手动重启应用以加载新模型。");
                }

                _copying = true;
                ImportButton.IsEnabled = false;
                InputBox.IsEnabled = false;
                SendButton.IsEnabled = false;

                await CopyModelIntoLocalStateAsync(source);

                if (_runner == null)
                {
                    await TryLoadModelAsync();
                }
                else
                {
                    Status("导入完成。请重启应用以加载新模型。");
                }
            }
            catch (Exception ex)
            {
                App.LogCrash("ImportModel", ex);
                Status("导入失败：" + ex.Message);
            }
            finally
            {
                _copying = false;
                ImportButton.IsEnabled = true;
            }
        }

        bool _copying;

        async Task CopyModelIntoLocalStateAsync(StorageFile source)
        {
            var basicProps = await source.GetBasicPropertiesAsync();
            ulong total = Math.Max(1, basicProps.Size);

            StorageFolder folder = ApplicationData.Current.LocalFolder;
            // 先拷到临时文件，成功后再原子改名，避免中途失败留下半截模型文件
            StorageFile dest = await folder.CreateFileAsync(ModelFileName + ".importing", CreationCollisionOption.ReplaceExisting);

            using (Stream input = await source.OpenStreamForReadAsync())
            using (Stream output = await dest.OpenStreamForWriteAsync())
            {
                byte[] buffer = new byte[4 * 1024 * 1024];
                long copied = 0;
                int read;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read);
                    copied += read;
                    if (sw.ElapsedMilliseconds > 400)
                    {
                        sw.Restart();
                        int pct = (int)(copied * 100 / (long)total);
                        Status($"正在导入模型… {pct}%（{copied / 1024 / 1024} / {total / 1024 / 1024} MB）");
                    }
                }
                await output.FlushAsync();
            }

            await dest.RenameAsync(ModelFileName, NameCollisionOption.ReplaceExisting);
            Status("导入完成，正在加载模型…");
        }

        void Status(string text)
        {
            StatusText.Text = text;
        }

        void OnInputKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && SendButton.IsEnabled && !_busy)
            {
                OnSendClick(null, null);
            }
        }

        void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _history.Clear();
            ChatPanel.Children.Clear();
            ChatPanel.Children.Add(StatusText);
            Status("会话已清空。");
        }

        async void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (_busy || _runner == null) return;
            string question = (InputBox.Text ?? "").Trim();
            if (question.Length == 0) return;

            _busy = true;
            InputBox.Text = "";
            InputBox.IsEnabled = false;
            SendButton.IsEnabled = false;

            AddBubble(question, true);
            TextBlock answer = AddBubble("…", false);

            try
            {
                // 组 chat 模板（非思考模式：注入空 think 块）
                var sb = new System.Text.StringBuilder();
                sb.Append("<|im_start|>system\n").Append(SystemPrompt).Append("<|im_end|>\n");
                foreach (var turn in _history)
                {
                    sb.Append("<|im_start|>user\n").Append(turn.Key).Append("<|im_end|>\n");
                    sb.Append("<|im_start|>assistant\n").Append(turn.Value).Append("<|im_end|>\n");
                }
                sb.Append("<|im_start|>user\n").Append(question).Append("<|im_end|>\n");
                sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");

                List<int> promptIds = _tokenizer.Encode(sb.ToString());
                var dispatcher = Dispatcher;
                var pieces = new List<int>();
                var swGen = System.Diagnostics.Stopwatch.StartNew();

                string finalText = await Task.Run(() =>
                {
                    _runner.Reset();
                    float[] logits = _runner.Forward(promptIds.ToArray());
                    int next = ArgMaxPenalized(logits, pieces);
                    for (int step = 0; step < MaxNewTokens; step++)
                    {
                        if (next == _model.EosId || next == _model.EosId2 || next == ImEndId || next == ImStartId)
                            break;
                        pieces.Add(next);
                        // 流式上屏
                        string sofar = _decoder.Decode(pieces);
                        var _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            answer.Text = sofar;
                            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true);
                        });
                        logits = _runner.Forward(new[] { next });
                        next = ArgMaxPenalized(logits, pieces);
                    }
                    return _decoder.Decode(pieces);
                });

                double tps = pieces.Count / Math.Max(0.001, swGen.Elapsed.TotalSeconds);
                answer.Text = finalText.Trim().Length > 0 ? finalText.Trim() : "（无输出）";
                _history.Add(new KeyValuePair<string, string>(question, answer.Text));
                Status($"{pieces.Count} tokens · {tps:0.00} tok/s · 占用 {Windows.System.MemoryManager.AppMemoryUsage / 1024 / 1024} MB");
            }
            catch (Exception ex)
            {
                App.LogCrash("Generate", ex);
                answer.Text = "生成失败：" + ex.Message;
            }
            finally
            {
                _busy = false;
                InputBox.IsEnabled = true;
                SendButton.IsEnabled = true;
            }
        }

        /// <summary>贪心 + 轻度重复惩罚（近 64 token 的 logit ÷1.15，缓解小模型复读）。</summary>
        int ArgMaxPenalized(float[] logits, List<int> recent)
        {
            int from = Math.Max(0, recent.Count - 64);
            for (int i = from; i < recent.Count; i++)
            {
                int t = recent[i];
                logits[t] = logits[t] > 0 ? logits[t] / 1.15f : logits[t] * 1.15f;
            }
            return QwenRunner.ArgMax(logits);
        }

        TextBlock AddBubble(string text, bool user)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = (SolidColorBrush)Application.Current.Resources[user ? "AccentBrush" : "TextBrush"],
            };
            ChatPanel.Children.Add(tb);
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true);
            return tb;
        }
    }
}
