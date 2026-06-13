让我深入分析一下核心 UI 文件，再给出具体的优化建议。
通过仔细阅读了核心页面代码（MainPage、ShellPage、导航栏、设置页、角色卡页、对话设置页、OOBE 向导等），以下是我对这个应用在**界面和易用性**方面的详细分析和优化建议：

---

## 一、导航与信息架构

### 1. 缺少对话历史列表入口
导航栏中只有"当前对话"这一个对话相关入口，用户**无法在应用内浏览、搜索和切换历史对话**。AGENTS.md 中提到了 `ConversationsListPage`，但实际导航面板中并没有对应的按钮。这意味着：
- 用户无法方便地找到以前的对话
- 切换对话只能依赖启动时的"最近对话"行为
- **建议**：在导航栏中添加"对话历史"入口，并支持搜索、置顶、分组和滑动删除

### 2. SplitView 永远是 Overlay 模式
```xml
DisplayMode="Overlay" IsPaneOpen="False"
```
Overlay 模式意味着侧栏打开时会**遮挡整个内容区域**。在桌面端（x86/x64），这浪费了屏幕空间。建议：
- 桌面端使用 `CompactInline` 或 `Inline` 模式，侧栏常驻
- 仅在移动端使用 `Overlay`

### 3. 导航栏缺乏视觉层级
所有导航项（当前对话、角色、角色广场、API 连接）使用完全相同样式的按钮，没有 active 状态高亮。用户无法感知当前处于哪个功能区域。

---

## 二、聊天主界面（MainPage）

### 4. 操作按钮始终可见，信息密度过高
每条消息下方都有一排操作按钮（复制、编辑、删除、重新生成、继续、分支导航），最多可达 6 个：
```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="{Binding Align}" Margin="14,2,14,4">
    <!-- Copy, Edit, Delete, Regenerate, Continue, Branch Nav -->
</StackPanel>
```
问题：
- 按钮尺寸极小（30×28、26×28），在手机上**极难点击**
- 所有按钮始终可见，视觉噪音严重
- **建议**：默认只显示一个 `…` 更多按钮，长按或点击后展开操作菜单（Flyout）

### 5. 消息缺少角色头像和名称标识
AI 气泡没有显示角色名称或头像，用户气泡也没有头像。在多角色或长时间对话中，**很难区分谁在说话**。建议：
- 在 AI 气泡左上角添加角色头像和名称
- 用户气泡右上角添加用户头像

### 6. 消息缺少时间戳
`ConversationMessage` 模型中有 `Timestamp` 字段，但聊天界面完全没有展示。长时间对话中用户无法判断消息的时间顺序。
- **建议**：在消息之间显示时间分隔线（如"今天 14:30"、"昨天"），或 hover/长按时显示确切时间

### 7. 没有"回到底部"悬浮按钮
当用户上滑浏览历史消息后，没有快速回到最新消息的入口。目前只能手动滚动。
- **建议**：检测滚动位置，当不在底部时显示一个浮动的"↓"按钮

### 8. 加载遮罩体验粗糙
```xml
<Border x:Name="LoadingOverlay" Background="{ThemeResource YanshuaiPageBrush}" Visibility="Collapsed">
    <ProgressBar ... Width="200" Height="4"/>
    <TextBlock Text="加载中…"/>
</Border>
```
整个聊天区域被不透明的背景完全覆盖。对于有 30+ 条消息的对话，用户会看到一个空白页面加上一个细小的进度条。
- **建议**：使用**骨架屏**（Skeleton Screen）或保持之前的内容可见，顶部叠加一个半透明进度条

### 9. 图片缩略图尺寸固定 120×120
```xml
<Image Source="{Binding ImageSource}" Width="120" Height="120" Stretch="UniformToFill"/>
```
不区分横图/竖图，统一裁剪为正方形。竖拍照片会被严重裁剪。
- **建议**：按原始比例显示，限制最大宽度而非固定尺寸

---

## 三、输入区域

### 10. 工具栏常驻，浪费空间
```xml
<Border x:Name="ComposerToolbarBorder" ...>
    <Grid Height="36">
        <!-- 全屏输入、插入括号、插入引号 -->
    </Grid>
</Border>
```
括号/引号插入按钮始终占据一行高度（36px），但大多数用户并不频繁使用这些功能。
- **建议**：将工具栏设为可收起，或长按输入框弹出；只保留全屏输入和附件按钮

### 11. 发送按钮颜色硬编码
```xml
<Border ... Background="#FF2979FF" CornerRadius="12">
```
发送按钮固定为蓝色 `#FF2979FF`，不跟随主题变化。在非蓝色主题下会非常突兀。
- **建议**：使用主题强调色资源 `YanshuaiAccentBrush`

### 12. 缺少字数/Token 计数器
用户无法得知当前输入的大致 token 数量，容易超出上下文窗口限制。
- **建议**：在输入框右下角或工具栏中显示当前消息的估算字符数

### 13. 全屏输入名不副实
```csharp
var dialog = new ContentDialog { Title = "全屏输入", ... };
await dialog.ShowAsync();
```
实际上是一个 `ContentDialog` 弹窗，并非真正的全屏编辑体验。在手机上 ContentDialog 的空间非常有限。
- **建议**：导航到一个专用的全屏编辑页面

---

## 四、设置与配置页面

### 14. ConvSettingsPage 存在 Hacky 布局修复
```xml
<ToggleSwitch x:Name="MemEnabledToggle" ... Margin="0,0,-90,0"/>
<ToggleSwitch x:Name="RagDebugToggle" ... Margin="0,0,-90,0"/>
```
用负边距 `Margin="0,0,-90,0"` 来修正 ToggleSwitch 位置，这是不稳定的布局方案，在不同设备分辨率上容易出问题。
- **建议**：使用 Grid 的 Column 定义来合理分配空间，而不是负边距 hack

### 15. 设置页缺少保存反馈
ToggleSwitch、ComboBox 等修改后没有任何保存成功的视觉反馈。用户不确定设置是否已生效。
- **建议**：添加一个短暂的 Toast 通知或控件微动画确认保存

### 16. "删除对话"没有二次确认
ConvSettingsPage 底部 CommandBar 的"删除对话"按钮直接执行删除：
```xml
<AppBarButton x:Name="DeleteConvBtn" Icon="Delete" Label="删除对话" Click="DeleteConvBtn_Click"/>
```
没有确认对话框，容易误触导致不可逆的数据丢失。

### 17. 对话设置页面过长
ConvSettingsPage 包含了 API 配置、重命名、上下文窗口、长记忆开关/参数/列表等大量内容，全部在一个 ScrollViewer 中纵向堆叠。
- **建议**：拆分为多个 Tab 或子页面（基础设置、记忆设置、高级设置）

---

## 五、角色卡页面

### 18. 使用代码容器而非原生数据绑定
```xml
<StackPanel x:Name="CardContainer" Margin="14,8,14,30"/>
```
角色卡列表使用 `StackPanel` 作为容器，在 code-behind 中手动添加/移除控件。这比使用 `ListView` + `DataTemplate` 的方案性能更差，且失去了：
- 原生虚拟化（长列表卡顿）
- 内置的多选支持
- 内置的拖拽排序
- **建议**：迁移到 `ListView` 或 `GridView`，配合 `DataTemplate` 使用

### 19. 缺少搜索和筛选功能
角色卡页面没有搜索框和分类筛选器。随着角色卡数量增加，查找特定角色会越来越困难。

---

## 六、首次使用引导（OOBE）

### 20. 8 步向导过长
从 `OobeWelcomePage` → `OobeAppearancePage` → `OobeApiMainPage` → `OobeApiSubPage` → `OobeCharaPage` → `OobeUserPage` → `OobePrefsPage` → `OobeDonePage`，共 8 步。
- **建议**：减少到 3-4 步核心设置（API 配置 + 角色选择），其余推迟到使用中引导
- 添加步骤进度指示器（Step 1/4 的形式）

### 21. "从备份恢复"入口太隐蔽
```xml
<CommandBar.SecondaryCommands>
    <AppBarButton Label="从备份恢复"/>
</CommandBar.SecondaryCommands>
```
放在 CommandBar 的 SecondaryCommands 中，用户几乎不可能发现。对于有数据的老用户，这个入口非常重要。

---

## 七、系统反馈与错误处理

### 22. 系统通知消息伪装成 AI 气泡
记忆总结、深层记忆更新等系统消息被添加为 AI 角色的气泡：
```csharp
_bubbles.Add(new ChatBubble
{
    Role = "assistant",
    Content = "已生成 3 条短时记忆...",
    BackgroundColor = new SolidColorBrush(Color.FromArgb(60, 80, 160, 80)),
    ...
});
```
用户可能会误以为是 AI 的回复，而且这些消息会被保存到对话历史中，干扰后续的上下文窗口。
- **建议**：使用独立的系统消息气泡样式（居中、灰色小字、无操作按钮）

### 23. API 错误缺少重试机制
当 API 调用失败时，错误信息通过 `AddSystemBubble` 展示，但没有"重试"按钮。用户需要手动重新发送消息。

### 24. 当前使用的 API/模型信息不明确
聊天界面没有任何地方显示当前对话使用的是哪个 API Profile 和模型。用户切换配置后可能产生困惑。

---

## 八、辅助功能与无障碍

### 25. 大量元素透明度过低
```xml
Opacity="0.18"  <!-- 欢迎图标 -->
Opacity="0.35"  <!-- 欢迎副标题 -->
Opacity="0.55"  <!-- 设置项描述 -->
Opacity="0.6"   <!-- 工具按钮 -->
```
多个关键文字的透明度在 0.35-0.6 之间，在浅色主题或室外使用时**几乎不可读**。

### 26. 图标按钮缺乏文本标签
操作栏的按钮全部使用 `FontIcon`，没有辅助文本。新用户需要逐个试探才能理解功能。
- **建议**：至少在 Tooltip 中提供描述（部分已有，但如分支导航 `<` `>` 缺失）

### 27. 思考过程动效语言不一致
```csharp
private static readonly string[] _thinkingVerbs =
{
    "Thinking", "Pondering", "Reflecting", "Reasoning", ...
};
```
应用界面是中文，但思考过程的动态文字是英文（"Thinking…", "Pondering…"）。
- **建议**：与界面语言保持一致，使用"思考中…""分析中…""推理中…"等中文表达

---

## 优先级排序建议

| 优先级 | 改进项 | 影响 |
|--------|--------|------|
| **P0** | 添加对话历史列表入口 | 核心功能缺失 |
| **P0** | 操作按钮改为长按/更多菜单 | 手机端可用性 |
| **P0** | API 错误重试机制 | 基本容错 |
| **P1** | 消息角色头像+名称+时间戳 | 对话可读性 |
| **P1** | "回到底部"悬浮按钮 | 长对话浏览 |
| **P1** | 删除操作二次确认 | 数据安全 |
| **P1** | 发送按钮颜色跟随主题 | 视觉一致性 |
| **P2** | 工具栏可折叠 | 空间利用 |
| **P2** | 角色卡页迁移到 ListView | 性能与可维护性 |
| **P2** | 系统消息独立样式 | 信息清晰度 |
| **P3** | OOBE 步骤精简 | 首次体验 |
| **P3** | 思考动效中文化 | 语言一致性 |
| **P3** | 桌面端 SplitView Inline 模式 | 桌面体验 |