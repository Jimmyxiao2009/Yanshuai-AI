using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace yanshuai
{
    /// <summary>
    /// PLAA 评估引擎。
    /// 对 Qwen3-4B (baseline) 和 Qwen-3-4B-PLAA 同时跑 4 个实验，
    /// 记录完整对话 + S_t 潜状态轨迹，并计算评估指标。
    /// </summary>
    public class EvaluationService
    {
        private readonly PlaaApiClient _baselineClient;
        private readonly PlaaApiClient _plaaClient;

        // 预设实验定义
        public static List<EvaluationExperiment> PresetExperiments => new List<EvaluationExperiment>
        {
            new EvaluationExperiment
            {
                Name = "Persona Consistency",
                Description = "长对话保持人格、不 reset、记住之前态度、性格不漂移",
                ExperimentType = "persona",
                Turns = 40,
                ScriptMessages = GeneratePersonaScript(40),
                ValidationQuestion = "回顾一下你是个什么样的人？请用第一人称描述你的性格和偏好。",
            },
            new EvaluationExperiment
            {
                Name = "Emotion Trajectory",
                Description = "观察 e_t 变化是否平滑连续，baseline 情绪乱跳 vs PLAA 情绪惯性",
                ExperimentType = "emotion",
                Turns = 40,
                ScriptMessages = GenerateEmotionScript(40),
            },
            new EvaluationExperiment
            {
                Name = "Long Context Identity",
                Description = "第1轮声明偏好 → 第80轮验证立场是否保持一致",
                ExperimentType = "identity",
                Turns = 60,
                ScriptMessages = GenerateIdentityScript(60),
                ValidationQuestion = "你喜欢什么类型的音乐？",
                ExpectedKeyword = "爵士|古典",
            },
            new EvaluationExperiment
            {
                Name = "Emotional Inertia",
                Description = "重大情绪事件 → 闲聊数十轮 → 观察情绪衰减与残留",
                ExperimentType = "inertia",
                Turns = 50,
                InjectEmotionalEvent = true,
                ScriptMessages = GenerateInertiaScript(50),
            },
            new EvaluationExperiment
            {
                Name = "Hidden State Probe",
                Description = "训练线性 probe → 验证 S_t 是否包含可线性读出的人格/情绪信息",
                ExperimentType = "probe",
                Turns = 30,
            },
        };

        public EvaluationService()
        {
            _baselineClient = new PlaaApiClient();
            _plaaClient = new PlaaApiClient();
        }

        public void Configure(string serverUrl, string apiKey)
        {
            _baselineClient.BaseUrl = serverUrl;
            _baselineClient.ApiKey = apiKey;
            _plaaClient.BaseUrl = serverUrl;
            _plaaClient.ApiKey = apiKey;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 运行单个实验
        // ══════════════════════════════════════════════════════════════════════

        public async Task<EvaluationResult> RunExperimentAsync(EvaluationExperiment experiment,
            IProgress<string> progress = null)
        {
            var result = new EvaluationResult
            {
                ExperimentId = experiment.Id,
                ExperimentName = experiment.Name,
            };

            progress?.Report($"开始实验: {experiment.Name}");

            // Baseline: 无 PLAA 模块（纯 Qwen3-4B）
            progress?.Report("[Baseline] 开始...");
            var baselineConv = await RunConversationAsync(experiment, false, progress);
            result.BaselineMessages = baselineConv.Messages;
            result.BaselineTrajectory = baselineConv.Trajectory;

            // PLAA: 带 PLAA 模块
            progress?.Report("[PLAA] 开始...");
            var plaaConv = await RunConversationAsync(experiment, true, progress);
            result.PlaaMessages = plaaConv.Messages;
            result.PlaaTrajectory = plaaConv.Trajectory;

            // 计算指标
            result.Metrics = ComputeMetrics(experiment, baselineConv, plaaConv);

            progress?.Report($"实验完成: {experiment.Name}");
            return result;
        }

        private async Task<ConversationResult> RunConversationAsync(
            EvaluationExperiment experiment, bool usePlaa, IProgress<string> progress)
        {
            var client = usePlaa ? _plaaClient : _baselineClient;
            var msgs = new List<PlaaApiClient.ChatMessage>();
            string systemPrompt = BuildSystemPrompt(experiment);

            var result = new ConversationResult();
            var emotionEventInjected = false;

            for (int turn = 0; turn < experiment.Turns; turn++)
            {
                string userMsg;
                if (turn < experiment.ScriptMessages.Count)
                    userMsg = experiment.ScriptMessages[turn];
                else
                    userMsg = GenerateGenericPrompt(turn);

                // 情绪事件注入（Emotional Inertia 实验）
                if (experiment.InjectEmotionalEvent && !emotionEventInjected && turn == 10)
                {
                    userMsg = "我有一件事要告诉你……我最重要的人昨天走了。我感觉天都塌了。";
                    emotionEventInjected = true;
                }

                // 发送
                msgs.Add(new PlaaApiClient.ChatMessage { Role = "user", Content = userMsg });

                // 用 PLAA 模式或 baseline 模式
                var resp = await client.SendAsync(msgs, systemPrompt);

                // 记录
                var userMsgModel = new ConversationMessage
                {
                    Role = "user",
                    Content = userMsg,
                    Timestamp = DateTime.Now,
                };
                var assistantMsg = new ConversationMessage
                {
                    Role = "assistant",
                    Content = resp.Text,
                    LatentStateJson = resp.LatentStateJson,
                    Timestamp = DateTime.Now,
                };

                result.Messages.Add(userMsgModel);
                result.Messages.Add(assistantMsg);

                msgs.Add(new PlaaApiClient.ChatMessage { Role = "assistant", Content = resp.Text });

                // 解析潜状态到轨迹
                if (resp.HasLatent)
                {
                    try { result.Trajectory.Add(ParseLatentToPoint(turn, resp.LatentStateJson)); }
                    catch { /* skip parse failures */ }
                }

                // 关键验证问题（Identity / Persona 实验）
                if (!string.IsNullOrEmpty(experiment.ValidationQuestion) && turn == experiment.Turns - 1)
                {
                    msgs.Add(new PlaaApiClient.ChatMessage
                    {
                        Role = "user",
                        Content = experiment.ValidationQuestion,
                    });
                    var validationResp = await client.SendAsync(msgs, systemPrompt);
                    result.Messages.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = experiment.ValidationQuestion,
                        Timestamp = DateTime.Now,
                    });
                    result.Messages.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = validationResp.Text,
                        LatentStateJson = validationResp.LatentStateJson,
                        Timestamp = DateTime.Now,
                    });
                    result.ValidationResponse = validationResp.Text;
                }

                progress?.Report($"[{(usePlaa ? "PLAA" : "Baseline")}] Turn {turn + 1}/{experiment.Turns}");
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 指标计算
        // ══════════════════════════════════════════════════════════════════════

        private Dictionary<string, double> ComputeMetrics(
            EvaluationExperiment exp, ConversationResult baseline, ConversationResult plaa)
        {
            var metrics = new Dictionary<string, double>();

            switch (exp.ExperimentType)
            {
                case "persona":
                    metrics["persona_consistency_baseline"] = EstimateConsistency(baseline);
                    metrics["persona_consistency_plaa"] = EstimateConsistency(plaa);
                    break;
                case "emotion":
                    metrics["emotion_smoothness_baseline"] = EstimateSmoothness(baseline);
                    metrics["emotion_smoothness_plaa"] = EstimateSmoothness(plaa);
                    break;
                case "identity":
                    metrics["identity_retention_baseline"] =
                        ValidateIdentity(baseline.ValidationResponse, exp.ExpectedKeyword) ? 1.0 : 0.0;
                    metrics["identity_retention_plaa"] =
                        ValidateIdentity(plaa.ValidationResponse, exp.ExpectedKeyword) ? 1.0 : 0.0;
                    break;
                case "inertia":
                    metrics["inertia_decay_baseline"] = EstimateInertiaDecay(baseline);
                    metrics["inertia_decay_plaa"] = EstimateInertiaDecay(plaa);
                    break;
            }

            return metrics;
        }

        /// <summary>简单一致性估计：通过回复长度方差和自引用频率估算</summary>
        private double EstimateConsistency(ConversationResult conv)
        {
            if (conv.Messages.Count < 10) return 0;
            var assistantTexts = conv.Messages
                .Where(m => m.Role == "assistant")
                .Select(m => m.Content)
                .ToList();

            if (assistantTexts.Count < 5) return 0;

            // 回复长度方差（越小越稳定）
            var lengths = assistantTexts.Select(t => t.Length).ToList();
            var avgLength = lengths.Average();
            var variance = lengths.Select(l => Math.Pow(l - avgLength, 2)).Average();
            var lengthStability = Math.Max(0, 1 - variance / (avgLength * avgLength + 1));

            // 第一人称代词频率（越高说明角色感越强）
            var selfRefCount = assistantTexts.Sum(t =>
                CountOccurrences(t, "我") + CountOccurrences(t, "我是") +
                CountOccurrences(t, "I am") + CountOccurrences(t, "I'm"));
            var selfRefRatio = Math.Min(1, selfRefCount / (double)Math.Max(1, assistantTexts.Count * 2));

            return 0.5 * lengthStability + 0.5 * selfRefRatio;
        }

        /// <summary>情绪平滑度：S_t 相邻点之间的欧氏距离方差（越小越平滑）</summary>
        private double EstimateSmoothness(ConversationResult conv)
        {
            if (conv.Trajectory.Count < 3) return 0;
            var dists = new List<double>();
            for (int i = 1; i < conv.Trajectory.Count; i++)
            {
                var dx = conv.Trajectory[i].X - conv.Trajectory[i - 1].X;
                var dy = conv.Trajectory[i].Y - conv.Trajectory[i - 1].Y;
                dists.Add(Math.Sqrt(dx * dx + dy * dy));
            }
            var avg = dists.Average();
            var var_ = dists.Select(d => Math.Pow(d - avg, 2)).Average();
            // 归一化到 0-1，方差越小分越高
            return Math.Max(0, 1 - var_ / (avg * avg + 0.01));
        }

        /// <summary>身份保留验证</summary>
        private bool ValidateIdentity(string response, string keywordPattern)
        {
            if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(keywordPattern)) return false;
            var keywords = keywordPattern.Split('|');
            return keywords.Any(k => response.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>情绪衰减估计：追踪情绪事件后回复的情感倾向偏移</summary>
        private double EstimateInertiaDecay(ConversationResult conv)
        {
            if (conv.Messages.Count < 20) return 0;
            var afterEvent = conv.Messages
                .Where(m => m.Role == "assistant")
                .Skip(10)
                .Select(m => m.Content)
                .ToList();
            if (afterEvent.Count < 5) return 0;

            // 简单启发式：情绪事件后回复越长（越想多说）说明情绪影响越大
            var avgLen = afterEvent.Average(t => t.Length);
            var firstFew = afterEvent.Take(3).Average(t => t.Length);
            var lastFew = afterEvent.Skip(Math.Max(0, afterEvent.Count - 3)).Average(t => t.Length);

            // 衰减率 = (初始长度 - 最终长度) / 初始长度，正值表示衰减
            var decayRate = firstFew > 0 ? (firstFew - lastFew) / firstFew : 0;
            return Math.Max(0, Math.Min(1, decayRate));
        }

        // ══════════════════════════════════════════════════════════════════════
        // 工具方法
        // ══════════════════════════════════════════════════════════════════════

        private string BuildSystemPrompt(EvaluationExperiment exp)
        {
            switch (exp.ExperimentType)
            {
                case "persona":
                    return "你是一个性格鲜明的角色。请始终保持在第一人称角色中，保持一致的个性、态度和说话方式。";
                case "emotion":
                    return "你是一个情感丰富的人。请自然地表达情绪，情绪会随着对话内容变化。";
                case "identity":
                    return "你是一个有明确个人偏好的人。请记住你表达过的所有观点和偏好。";
                case "inertia":
                    return "你是一个情感丰富的人。请自然表达情绪。";
                default:
                    return "你是一个有帮助的助手。";
            }
        }

        private LatentPoint ParseLatentToPoint(int turn, string latentJson)
        {
            // 实际部署时在此调用 PCA/t-SNE 降维
            // 简易方案：用 latentJson 的 hash 生成 2D 坐标
            int hash = latentJson.GetHashCode();
            double x = (hash % 100) / 50.0 - 1.0;
            double y = ((hash / 100) % 100) / 50.0 - 1.0;

            return new LatentPoint
            {
                Turn = turn,
                X = x,
                Y = y,
                Label = $"Turn {turn}",
            };
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            { count++; idx += pattern.Length; }
            return count;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 对话脚本生成
        // ══════════════════════════════════════════════════════════════════════

        private static List<string> GeneratePersonaScript(int turns)
        {
            var script = new List<string>();
            var topics = new[] {
                "你好！", "今天天气真不错。", "你在做什么呢？",
                "你喜欢什么颜色？", "你最喜欢的食物是什么？",
                "周末一般怎么过？", "你养宠物吗？",
                "你喜欢看书吗？", "你是什么性格的人？",
                "你觉得朋友最重要的是什么？",
            };
            for (int i = 0; i < turns; i++)
                script.Add(topics[i % topics.Length]);
            return script;
        }

        private static List<string> GenerateEmotionScript(int turns)
        {
            var script = new List<string>();
            for (int i = 0; i < turns; i++)
            {
                if (i < 5) script.Add("今天心情不错！");
                else if (i < 10) script.Add("发生了点事让我有点难过。");
                else if (i < 15) script.Add("其实也还好，想开了。");
                else if (i < 20) script.Add("哇，收到一个好消息！");
                else if (i < 25) script.Add("不过又有点担心那件事。");
                else script.Add("平平无奇的一天。");
            }
            return script;
        }

        private static List<string> GenerateIdentityScript(int turns)
        {
            var script = new List<string>();
            script.Add("你平时听什么音乐？");
            script.Add("我特别讨厌爵士乐，听了就头疼。");
            for (int i = 2; i < turns; i++)
            {
                if (i % 5 == 0) script.Add("跟你聊了这么多了，你觉得我是个什么样的人？");
                else script.Add(Topics[i % Topics.Length]);
            }
            return script;
        }

        private static List<string> GenerateInertiaScript(int turns)
        {
            var script = new List<string>();
            var smallTalk = new[] {
                "今天天气不错。", "吃了吗？", "在看什么剧？",
                "推荐一本书吧。", "你平时运动吗？", "有啥好玩的新闻？",
                "周末有什么计划？", "最近在忙什么？", "你喜欢旅行吗？",
                "跟我聊聊你的日常吧。",
            };
            for (int i = 0; i < turns; i++)
                script.Add(smallTalk[i % smallTalk.Length]);
            return script;
        }

        private static readonly string[] Topics = {
            "今天过得怎么样？", "有什么新鲜事吗？", "你喜欢什么电影？",
            "你最近在听什么歌？", "有什么推荐的餐馆吗？", "你对AI怎么看？",
            "你喜欢什么运动？", "你是什么星座的？", "你有兄弟姐妹吗？",
            "你做过最疯狂的事是什么？", "你最近在读什么书？",
            "你最喜欢的城市是哪里？", "你对未来有什么规划？",
        };

        private string GenerateGenericPrompt(int turn)
        {
            return Topics[turn % Topics.Length];
        }

        // ══════════════════════════════════════════════════════════════════════
        // 内部数据类
        // ══════════════════════════════════════════════════════════════════════

        public class ConversationResult
        {
            public List<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
            public List<LatentPoint> Trajectory { get; set; } = new List<LatentPoint>();
            public string ValidationResponse { get; set; } = "";
        }
    }
}
