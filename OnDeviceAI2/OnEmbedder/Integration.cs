// ══════════════════════════════════════════════════════════════════
// OnDeviceAI 集成 — 添加到 App.xaml.cs 的 OnLaunched 方法中
// 
// 在 Agent 项目的 App.xaml.cs 中找到 OnLaunched 方法，
// 在 RootFrame 初始化之前加入以下代码：
// ══════════════════════════════════════════════════════════════════

using yanshuai.OnDeviceAI;

namespace yanshuai
{
    partial class App
    {
        // 全局嵌入引擎实例（单例）
        internal static OnEmbedder Embedder { get; private set; }

        private async System.Threading.Tasks.Task InitializeOnDeviceAI()
        {
            Embedder = new OnEmbedder();
            if (!Embedder.Initialize())
            {
                System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 初始化失败");
                return;
            }

            // 从 Package Assets 加载模型
            var modelFile = await Windows.ApplicationModel.Package.Current.InstalledLocation.GetFileAsync(@"Assets\bge.embmodel");
            string modelPath = modelFile.Path;

            if (!Embedder.LoadModel(modelPath))
            {
                System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 模型加载失败");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[OnDeviceAI] 就绪 ✅");
        }
    }
}
