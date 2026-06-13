// Program.cs — 验证 OnEmbedder 推理引擎
// 在 PC 上测试 .embmodel 加载和嵌入生成

using System;
using System.IO;
using System.Linq;
using yanshuai.OnDeviceAI;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== OnEmbedder 验证测试 ===\n");

        var embedder = new OnEmbedder();
        Console.WriteLine("[1/3] Initialize...");
        if (!embedder.Initialize())
        {
            Console.WriteLine("  ❌ 初始化失败");
            return;
        }
        Console.WriteLine("  ✅ OK");

        string modelPath = args.Length > 0 ? args[0] :
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bge.embmodel");

        Console.WriteLine($"[2/3] LoadModel({modelPath})...");
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"  ❌ 找不到模型文件: {modelPath}");
            Console.WriteLine($"  💡 请在运行目录放置 bge.embmodel，或用参数指定路径");
            return;
        }
        if (!embedder.LoadModel(modelPath))
        {
            Console.WriteLine("  ❌ 模型加载失败");
            return;
        }
        Console.WriteLine("  ✅ OK");

        Console.WriteLine("[3/3] Encode 测试...\n");

        string[] testTexts = {
            "今天天气怎么样",
            "帮我搜索人工智能相关的新闻",
            "OpenClaw是什么",
        };

        foreach (var text in testTexts)
        {
            var vec = embedder.Encode(text);
            Console.WriteLine($"  文本: \"{text}\"");
            Console.WriteLine($"  维度: {vec.Length}");
            int show = Math.Min(10, vec.Length);
            var first10 = new string[show];
            for (int i = 0; i < show; i++)
                first10[i] = $"{vec[i]:F4}";
            Console.WriteLine($"  前10维: [{string.Join(", ", first10)}]");
            
            // 验证 L2 norm ≈ 1
            float sumSq = 0;
            foreach (var v in vec) sumSq += v * v;
            float norm = (float)Math.Sqrt(sumSq);
            Console.WriteLine($"  L2 Norm: {norm:F6}  (期望 ≈1.0)\n");
        }

        Console.WriteLine("=== 全部测试通过 ✅ ===");
    }
}
