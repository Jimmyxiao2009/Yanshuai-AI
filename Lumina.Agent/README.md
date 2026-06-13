# Lumina Agent

言枢AI 的 UWP Agent 组件，基于 WinRT / Visual Studio 2015 构建，运行于 Windows 10 Mobile。

提供 AI 对话代理能力，包含多模型 API 接入、RAG 记忆检索、角色卡与世界观系统。

## 功能

- **多 LLM 支持** — DeepSeek、LLaMA3、自定义 API 接入
- **RAG 记忆检索** — 本地嵌入模型离线语义搜索
- **角色卡 & 世界观** — 分支对话、人物设定、世界书
- **会话管理** — 历史导入导出、分支回溯
- **Windows 10 Mobile 优化** — 触控交互、低内存适配、Metro 风格 UI

## 项目结构

```
Lumina.Agent/
├── AI/                # AI 核心
│   ├── OnEmbedder.cs    本地嵌入引擎
│   └── RagMemory.cs     RAG 记忆存储与检索
├── Core/              # 核心逻辑
│   ├── AppSettings.cs   应用设置管理
│   ├── AppState.cs      全局状态
│   ├── DataManager.cs   数据持久化
│   ├── ApiLogger.cs     API 调用日志
│   └── Tools.cs         工具函数
├── Common/            # 通用组件
│   ├── NavigationHelper.cs
│   ├── ObservableDictionary.cs
│   ├── RelayCommand.cs
│   └── SuspensionManager.cs
├── Models/            # 数据模型
│   ├── ChatMessage.cs   消息模型
│   ├── ChatSession.cs   会话模型
│   └── SessionManager.cs
├── Pages/             # UI 页面
│   ├── Chat/            对话页面
│   ├── Data/            数据管理
│   ├── Oobe/            首次引导
│   └── Settings/        设置页面
└── Assets/            # 资源文件
```

## 构建

| 环境 | 要求 |
|------|------|
| IDE | Visual Studio 2015 |
| SDK | Windows 10 SDK 10.0.10240.0 |
| 目标 | Windows 10 Mobile (ARM) |
| 部署 | Lumia 950 / ARM 模拟器 |

```bash
# 构建命令
msbuild Lumina.Agent.sln /p:Configuration=Release /p:Platform=ARM
```

## 依赖

- [OnDeviceAI](https://github.com/Jimmyxiao2009/OnDeviceAI) — 本地嵌入模型推理引擎
- Newtonsoft.Json
- Microsoft.Toolkit.Uwp

## License

MIT
