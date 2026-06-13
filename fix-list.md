# 修复清单 — 基于 AI 建议 & 言枢 2.0 规划

> 2026-05-05
> 筛选原则：与 2.0 架构方向一致，或属于独立低风险修复。与 2.0 拆分为两个 App 冲突的变更暂缓。

---

## P0 — 核心体验缺陷（立即修复）

### P0-1 发送按钮颜色硬编码 → 跟随主题
**来源**：建议 #11  
**文件**：`Pages/Chat/MainPage.xaml`  
**改动**：`Background="#FF2979FF"` → `Background="{ThemeResource YanshuaiAccentBrush}"`  
**依据**：独立 UI 修复，不涉及架构变动  

### P0-2 删除对话没有二次确认
**来源**：建议 #16  
**文件**：`Pages/Chat/ConvSettingsPage.xaml.cs`  
**改动**：`DeleteConvBtn_Click` 中添加 `ContentDialog` 确认弹窗  
**依据**：数据安全，低风险  

### P0-3 ToggleSwitch 负边距 Hack → Grid 布局
**来源**：建议 #14  
**文件**：`Pages/Chat/ConvSettingsPage.xaml`  
**改动**：`Margin="0,0,-90,0"` 改为 `Grid` + `ColumnDefinitions` 正确定位  
**依据**：布局稳定性  

### P0-4 API 错误缺少重试按钮
**来源**：建议 #23  
**文件**：`Pages/Chat/MainPage.xaml.cs`  
**改动**：`AddSystemBubble` 中添加"重试"按钮或操作入口  
**依据**：基本容错，与 2.0 API 层独立  

---

## P1 — 视觉一致性 & 可用性

### P1-1 思考动效中文化
**来源**：建议 #27  
**文件**：`Pages/Chat/MainPage.xaml.cs`  
**改动**：`_thinkingVerbs` 数组英文 → 中文（"思考中…""分析中…""推理中…"）  
**依据**：应用 UI 已是中文，语言一致  

### P1-2 低透明度文字调高
**来源**：建议 #25  
**文件**：多个 XAML  
**改动**：`Opacity="0.18"` → `0.45`，`Opacity="0.35"` → `0.55`，`Opacity="0.55"` → `0.7`  
**依据**：无障碍可读性，需逐个文件审查 Oobe/Setting 页面  

### P1-3 当前使用的 API/模型信息可查
**来源**：建议 #24  
**文件**：`Pages/Chat/MainPage.xaml` / `.xaml.cs`  
**改动**：在聊天气泡列表顶部或底部添加当前 API Profile 名称和模型的轻量显示  
**依据**：用户需要知道当前在用什么模型，2.0 多 API Profile 场景下更重要  

### P1-4 系统消息独立样式（区别于 AI 气泡）
**来源**：建议 #22  
**文件**：`Pages/Chat/MainPage.xaml.cs`（`AddSystemBubble`）  
**改动**：系统消息用居中灰色小字样式，不设角色标识，不触发操作按钮  
**依据**：与 2.0 RAG 记忆系统联动——记忆摘要/系统通知不应混入对话流  

---

## P2 — 体验优化（与 2.0 不冲突）

### P2-1 消息气泡添加时间戳
**来源**：建议 #6  
**文件**：`Pages/Chat/MainPage.xaml` + `ChatBubble` 控件  
**改动**：每条消息右下或 hover 时显示 `Timestamp`（简短格式 "14:30"）  
**依据**：`ConversationMessage` 已有 `Timestamp` 字段只存不用  

### P2-2 "回到底部"悬浮按钮
**来源**：建议 #7  
**文件**：`Pages/Chat/MainPage.xaml` / `.xaml.cs`  
**改动**：`ScrollViewer.ViewChanged` 检测偏离底部时显示浮动按钮  
**依据**：长对话浏览刚需，与 2.0 消息渲染独立  

### P2-3 图标按钮补充 Tooltip
**来源**：建议 #26  
**文件**：`Pages/Chat/MainPage.xaml`（操作栏 FontIcon 按钮）  
**改动**：给分支导航 `<` `>` 等缺失标签的图标加 `ToolTipService.ToolTip`  
**依据**：新用户引导，修复成本极低  

### P2-4 全屏输入改为独立页面
**来源**：建议 #13  
**文件**：新建 `Pages/Chat/FullInputPage.xaml` + `.xaml.cs`  
**改动**：`ContentDialog` → `Frame.Navigate` 到独立编辑页面  
**依据**：ContentDialog 在手机上确实空间有限  

### P2-5 角色卡容器 StackPanel → ListView/GridView
**来源**：建议 #18  
**文件**：`Pages/Characters/CharacterCardsPage.xaml` / `.xaml.cs`  
**改动**：`StackPanel` + code-behind 添加控件 → `GridView` + `DataTemplate`  
**依据**：虚拟化性能 + 原生多选支持（已有 `_selectMode` 多选逻辑，迁移后更干净）  
**注意**：2.0 Roleplay 后续可能重新设计角色卡，此处做增量重构  

---

## P3 — 可延后或不纳入

以下建议**暂不纳入**本清单：

| 建议 | 原因 |
|------|------|
| #1 对话历史列表 | 2.0 拆分为两个 App，对话管理方式不同，应等拆分时重新设计 |
| #2 SplitView Inline 模式 | 与 2.0 Shell 层重构绑定 |
| #3 导航栏 active 高亮 | 2.0 拆分后导航结构会变 |
| #4 操作按钮折叠为"…" | 好的设计但不 urgent；操作栏按钮在手机上确实小，但无崩溃风险 |
| #5 角色头像/名称 | 需要在 `ConversationMessage` 中关联角色头像资源，改动较大 |
| #8 骨架屏 | UX 改善但不是 bug |
| #9 图片缩略图比例 | 图片消息非核心功能 |
| #10 工具栏可折叠 | 体验提升但非必需 |
| #12 Token 计数器 | 与 2.0 上下文管理独立，但可后续添加 |
| #15 保存反馈 Toast | 设置页改进，low impact |
| #17 设置页拆分 Tab | ConvSettingsPage 内容确实多，但目前可用 |
| #19 角色卡搜索/筛选 | 角色卡数量不多时不紧迫 |
| #20 OOBE 精简 | 当前已是 8 步带进度指示，首次体验尚可 |
| #21 备份恢复入口 | 当前用户量级下不是关键路径 |

---

## 执行顺序

1. **P0-1** 发送按钮颜色 → 5 分钟（单行 XAML）
2. **P0-4** 重试按钮 → 15 分钟（`AddSystemBubble` 加按钮）
3. **P0-2** 删除确认 → 10 分钟（加 ContentDialog）
4. **P0-3** 负边距修复 → 15 分钟
5. **P1-1** 思考动效中文化 → 5 分钟（数组替换）
6. **P1-2** 透明度调整 → 20 分钟（全局搜索 Opacity 值）
7. **P1-4** 系统消息独立样式 → 20 分钟
8. **P1-3** API/模型信息显示 → 20 分钟
9. **P2** 各项 → 按需求逐个推进
