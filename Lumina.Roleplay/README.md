# Lumina Roleplay

言枢AI 的 UWP Roleplay 组件，基于 WinRT / Visual Studio 2015 构建，运行于 Windows 10 Mobile。

专注于角色扮演对话体验——创建角色卡、设定世界观、与 AI 角色沉浸式互动。

## 功能

- **角色卡系统** — 创建、编辑、导入/导出角色设定
- **角色创建向导** — 分步骤引导创建完整角色
- **多 UI 主题** — Celadon（青瓷）、InkScroll（墨卷）、MidnightVermilion（夜朱）
- **RAG 记忆检索** — 本地嵌入模型离线语义记忆
- **世界书系统** — 设定世界观、自定义知识库
- **多 API 配置** — 支持多种 LLM 后端切换
- **分支对话** — 对话回溯与分支管理
- **OOBE 引导** — 首次使用配置向导

## 项目结构

```
Lumina.Roleplay/
├── AI/                  # AI 核心
│   ├── CardCompleter.cs    角色卡自动补全
│   ├── CharaSource.cs      角色数据源
│   ├── CharaWizardData.cs  角色创建向导数据
│   ├── DialoguePool.cs     对话池管理
│   ├── OnEmbedder.cs       本地嵌入引擎
│   ├── RagMemory.cs        RAG 记忆存储
│   └── ApiLogger.cs        API 调用日志
├── Core/                # 核心逻辑
│   ├── AppSettings.cs      应用设置
│   ├── AppState.cs         全局状态
│   ├── DataManager.cs      数据持久化
│   ├── BuiltinCards.cs     内置角色卡
│   ├── MarkdownBlock.cs    Markdown 渲染
│   ├── Models.cs           数据模型
│   └── Translations.cs     多语言翻译
├── Controls/            # 自定义控件
│   └── ColorPresetPanel    颜色预设面板
├── Common/              # 通用组件
├── Pages/               # UI 页面
│   ├── Characters/         角色管理
│   ├── Chat/               对话界面
│   ├── Data/               数据管理
│   ├── Oobe/               首次引导
│   └── Settings/           设置页面
├── Shell/               # 导航框架
├── Themes/              # 主题资源
├── Services/            # 后台服务
└── Scripts/             # 工具脚本
```

## 构建

| 环境 | 要求 |
|------|------|
| IDE | Visual Studio 2015 |
| SDK | Windows 10 SDK 10.0.10240.0 |
| 目标 | Windows 10 Mobile (ARM) |
| 部署 | Lumia 950 / ARM 模拟器 |

```bash
msbuild yanshuai.sln /p:Configuration=Release /p:Platform=ARM
```

## 依赖

- [OnDeviceAI](https://github.com/Jimmyxiao2009/OnDeviceAI) — 本地嵌入模型推理引擎
- Newtonsoft.Json
- Microsoft.Toolkit.Uwp

## License

MIT
