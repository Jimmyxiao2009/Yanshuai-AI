# Lumina.Core —— 言枢 AI 共享底层

`Lumina.Core` 是一个 **共享项目（Shared Project, `.shproj`）**，把同一份源码在编译期分别编译进
`Lumina.Agent` 与 `Lumina.Roleplay` 两个 UWP app。它没有独立的二进制产物 —— 这正是
"两个 app 共享同一份底层" 的实现方式。

## 为什么用共享项目而不是类库 DLL

- **Windows 10 Mobile / .NET Native**：源码级共享避免了额外程序集的运行时/版本耦合。
- **可按 app 分支**：两个 app 的内核曾各自演进，少量分歧用 `#if` 条件编译解决，
  而二进制类库无法对不同调用方编译出不同代码。

## 条件编译约定

每个 app 在自己的 `.csproj` 末尾定义了编译标识：

| App            | DefineConstants |
|----------------|-----------------|
| Lumina.Agent   | `AGENT`         |
| Lumina.Roleplay| `ROLEPLAY`      |

在共享源码里用 `#if ROLEPLAY` / `#if AGENT` 包裹只属于某一方的代码。例如
`AI/OnEmbedder.cs` 中的 `GetEmbedProfile()` 依赖 Roleplay 专属的设置项，故以 `#if ROLEPLAY` 包裹。

## 当前已纳入共享底层的内容

| 文件 | 说明 | 取自 |
|------|------|------|
| `Common/NavigationHelper.cs`     | 页面导航助手           | 两端字节相同 |
| `Common/ObservableDictionary.cs` | 可观察字典             | 两端字节相同 |
| `Common/SuspensionManager.cs`    | 挂起/恢复管理          | 两端字节相同 |
| `Common/RelayCommand.cs`         | 命令（含泛型 `RelayCommand<T>`） | Roleplay 超集 |
| `Core/AppState.cs`               | 全局内存态             | 超集（含 `Embedder`） |
| `Core/ApiLogger.cs`              | API 调用日志（含工具调用日志） | Agent 高级版（超集） |
| `AI/OnEmbedder.cs`               | **SIMD 加速**的本地嵌入推理引擎 | Roleplay SIMD 版（Agent 同获提速） |

## 暂未共享（领域耦合，按 app 保留）

`RagMemory`、`DataManager`、`AppSettings`、`Models` 等与各 app 的领域模型
（角色卡 / 世界书 vs. 项目 / 子代理 / MCP）强耦合，且检索/持久化逻辑各自调优。
统一它们涉及**行为一致性的产品决策**，留待后续按需推进 —— 届时可把纯数据类型
（如 `MemoryItem`、`SearchResult`）下沉到本共享项目，把领域逻辑留在各 app。

## 如何新增共享文件

1. 把源码放到 `Lumina.Core/<子目录>/`。
2. 在 `Lumina.Core.projitems` 的 `<ItemGroup>` 里加一行
   `<Compile Include="$(MSBuildThisFileDirectory)<子目录>\<文件>.cs" />`。
3. 从两个 app 的 `.csproj` 移除原来的 `<Compile Include="...">` 并删除各自的副本。
4. 分歧处用 `#if AGENT` / `#if ROLEPLAY`。
5. 两个 app 都重新编译验证。
