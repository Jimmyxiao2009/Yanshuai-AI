# Markdown渲染重做 + 流式输出优化 + API供应商多模型

## 1. Markdown 渲染重做

### 问题
- **表格不对齐**: 当前用 `│` 字符分隔单元格，依赖等宽字体才能对齐，混合中英文时错位
- **链接点不了**: `[text](url)` 渲染为蓝色下划线文字但没有 Click 事件
- **代码块粗糙**: 无背景区域感，无复制按钮，语法高亮太简单
- **公式不显示**: `$...$` 只显示为 `Σ(...)` 文本，无真实数学渲染

### 修复方案 (`Pages/Chat/MarkdownBlock.cs` 完全重写)

**表格**: 使用 `Grid` + `InlineUIContainer` 实现真正的对齐表格
- 先解析所有行确定列数和列宽
- 用 `Border` + `TextBlock` 的 Grid 布局代替文本拼接
- 表头行加粗 + 浅色背景，分隔线用 Border

**链接**: 添加 `Hyperlink` 元素替代 `Underline`
- UWP RichTextBlock 支持 `Hyperlink` inline，自带 Click 事件
- Click 后用 `Launcher.LaunchUriAsync` 打开浏览器
- 保留文本颜色样式

**代码块**: 用 `InlineUIContainer` + `Border` + `RichTextBlock` 嵌套
- 代码块外加圆角深色 Border 背景
- 语言标签 + 复制按钮在右上角
- 关键字高亮：增加常见语言的关键字列表（C#/Python/JS）
- 字符串用绿色，数字用蓝色

**公式**: 改进数学渲染
- 将 LaTeX 公式转换为 Unicode 数学符号的简易映射
- `\frac{a}{b}` → `a/b`, `\sqrt{x}` → `√x`, `\sum` → `∑` 等
- 保留 Courier New 紫色样式，但解析基本 LaTeX 命令

### 其他改进
- Bold/Italic 内嵌内容支持递归 inline 解析（当前只有 plain Run）
- 嵌套列表更好的缩进
- 代码块内不做 inline 解析（当前已正确）

## 2. 流式输出性能优化

### 问题
`bubble.Content += ct2` 导致:
1. 每个 token 都 O(n) 字符串拷贝 → 总体 O(n²)
2. 每个 token 都触发 PropertyChanged → UI 重新布局整个 TextBlock
3. W10M 上 TextBlock 对长文本布局很慢

### 修复方案

**A. 使用 StringBuilder 批量更新**
- 在 ChatBubble 内部用 `StringBuilder _streamBuffer` 累积
- 每 N 个 token（或每 50ms）才刷新一次 `Content` 属性
- 减少 PropertyChanged 触发次数从每 token 一次变为每批一次

**B. 流式阶段使用 TextBlock.Text 直接绑定**
- 当前已经是 plain TextBlock（IsStreaming=true 时），这是对的
- 关键优化：避免每次 set Content 都触发完整重新测量
- 改为追加模式：使用 `Run` 的 `Text` 追加而非替换整个 TextBlock.Text

**C. 节流 ScrollToBottom**
- 当前每 8 个 token 滚动一次，改为每 200ms 最多一次
- 避免 `ChangeView` 频繁触发布局

### 实现细节 (`Pages/Chat/MainPage.xaml.cs` + `ChatBubble`)
- `ChatBubble` 新增 `AppendStreamToken(string token)` 方法
- 内部 StringBuilder 累积，50ms 定时器触发 `Content = _sb.ToString()`
- `HandleStreamingResponse` 中改用 `AppendStreamToken` 代替 `+=`
- `ScrollToBottom` 加节流（200ms 内只执行一次）

## 3. API 供应商多模型功能

### 概念
一个供应商（如 OpenAI、DeepSeek）提供多个模型。用户配置一次 URL + Key，可选择不同模型。

### 数据模型 (`Models/Models.cs`)
- `ApiProfile` 新增:
  - `List<string> AvailableModels` — 该供应商可用的模型列表（缓存）
  - `DateTime ModelsLastFetched` — 上次获取时间
  - `bool SupportsVisionAuto` — 自动检测视觉支持

### 模型列表获取 (`Core/ApiModelFetcher.cs` 新建)
- `FetchModelsAsync(ApiProfile)`: GET `/v1/models` 端点
- 解析 `data[].id` 字段
- Claude: GET 不同端点或硬编码已知模型
- 缓存 24 小时

### 模态自动检测
- 已知模型名映射表（如包含 "vision"/"4o"/"gemini" → 支持图片）
- 或从 `/v1/models` 返回的 capabilities 字段读取

### UI (`Pages/Settings/ApiProfilesPage.xaml/.cs`)
- 模型输入框旁新增"刷新"按钮 → 获取模型列表
- 获取后显示下拉选择器（ComboBox）替代手动输入
- 模型列表项显示名称 + 上下文长度
- "测试连接"按钮：发送简单请求验证

---

## 文件变更

| 操作 | 文件 | 变更 |
|------|------|------|
| **重写** | `Pages/Chat/MarkdownBlock.cs` | 表格Grid、链接Hyperlink、代码块Border、公式Unicode |
| **修改** | `Pages/Chat/MainPage.xaml.cs` | 流式性能优化（批量更新+节流） |
| **修改** | `Pages/Chat/MainPage.xaml` | 流式TextBlock改为用Run追加 |
| **新建** | `Core/ApiModelFetcher.cs` | 获取供应商模型列表 |
| **修改** | `Models/Models.cs` | ApiProfile 新增 AvailableModels 等字段 |
| **修改** | `Pages/Settings/ApiProfilesPage.xaml/.cs` | 模型下拉、刷新、测试连接 |
| **修改** | `Lumina.Agent.csproj` | 新文件引用 |

## 实现顺序

1. 流式输出优化（ChatBubble + MainPage.cs）— 影响面小，立竿见影
2. MarkdownBlock.cs 重写 — 最大改动
3. ApiModelFetcher + 数据模型 + ApiProfilesPage 升级
