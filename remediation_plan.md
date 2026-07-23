# 言枢 AI (Lumina-Yanshuai) UWP 客户端综合整改与优化方案 (Remediation Plan)

## 1. 执行摘要 (Executive Summary)

### 1.1 项目背景与现状概述
言枢 AI (Lumina-Yanshuai) 是一款专为 Windows 平台打造的、基于通用 Windows 平台 (UWP) 的本地原生 AI 智能体与沉浸式角色扮演客户端。该项目由两个核心应用组成：`Lumina.Agent`（偏向效率与多智能体协同）和 `Lumina.Roleplay`（原名“言枢”，偏向沉浸式角色扮演与卡片管理）。与采用 Electron 或 Web-Server 架构的竞品不同，言枢 AI 致力于在低算力、低内存的遗留桌面设备和 Windows 10 Mobile ARM 架构手机上，提供纯本地的文本生成（如 Qwen3.5-0.8B 本地模型）、SIMD 向量检索（BGE-Micro）和 HLSL GPU 着色器加速能力。

### 1.2 2026-07-15 修复成果核验
基于对最新分支代码的审查，验证了上一轮（2026-07-15）所做修复的有效性：
- **数据损坏防护**：解决了 ZIP 备份恢复时的整表直接覆盖、半覆盖一致性破坏等问题，重构为了临时变量反序列化的事务型加载并引入了二次确认 `MessageDialog`。
- **消息与分支处理**：修复了分支快照丢失图片和 Token 计数的缺陷，在删除消息与流式生成中增加了 `_isSending` 状态锁防并发，以及对角色卡级联删除孤儿会话的处理。
- **全局异常拦截**：两应用均引入了全局 `UnhandledException` 处理逻辑，将崩溃堆栈输出至本地 `uwp_crash.log`，并在 `OnLaunched` 崩溃时提供用户友好的错误 UI 页面，消除了白屏隐患。

### 1.3 Windows 10 Mobile (10.0.15254) 移动平台适配验证
- **Lumina.Agent**：展现了高度优化的内存分配与存储策略，采用 `ImageStore` 机制将附件写入磁盘、采用 `DecodePixelWidth` 限制解码内存，且列表容器均有高度约束，UI 虚拟化完全生效，在 1GB/2GB 移动端表现稳定。
- **Lumina.Roleplay**：存在严重的崩溃与性能瓶颈。主要体现在：未对头像和立绘配置 `DecodePixelWidth` 导致数十张高清 PNG 瞬间爆内存（Permanent Bitmap RAM 达 200MB+）；将大体积 Base64 图片数据内嵌在主 AppData JSON 数据库中，引发高频序列化卡死与 Out Of Memory (OOM) 闪退；以及在会话设置页面的 RAG 记忆列表中，由于外层嵌套了 `ScrollViewer` 导致 UI 虚拟化彻底失效。

---

## 2. 完整 Bug 状态矩阵 (Complete Bug Status Matrix)

本矩阵汇总了 2026-07-12 审查报告中列出的全部 71 个 Bug，涵盖 P0-P3 四个等级。对于所有 **OUTSTANDING** (未修复) 和 **PARTIALLY_FIXED** (部分修复) 的缺陷，给出了详细的代码级整改实施策略。

### 2.1 P0 级缺陷 —— 数据丢失 / 崩溃 (16 项)

| Bug ID |  severity | 组件/模块 | 缺陷描述 | 状态 | 代码引用位置 | 验证结果 / 整改与实施策略 (Code Remediation Details) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **P0-1** | P0 | Roleplay | ZIP 备份漏掉 `DialoguePools` 和 `UserProfiles` | **FIXED** | `ImportExportPage.Zip.cs` (Line 35-36, 62-63, 100-101, 118-119) | **已验证**。导出与导入代码已显式处理 `DialoguePools` 与 `UserProfiles` 的 JSON 转换与 DataManager 同步。 |
| **P0-2** | P0 | Agent | ZIP 备份只覆盖约 1/3 数据 | **FIXED** | `ImportExportPage.xaml.cs` (Line 450-456, 571-577) | **已验证**。补齐了 `Projects`、`GlobalMemories`、评估结果/实验、MCP 服务和 Skills 的导出导入。 |
| **P0-3** | P0 | 两应用 | ZIP 恢复无二次确认，直接覆盖整表 | **FIXED** | `ImportExportPage.Zip.cs` (Line 75-82)<br>`ImportExportPage.xaml.cs` (Line 508-515) | **已验证**。均加入了 `MessageDialog` 阻塞弹窗，提供“确定”与“取消”二次确认，非确定则直接 `return`。 |
| **P0-4** | P0 | 两应用 | 损坏备份导入产生“半覆盖”不一致 | **FIXED** | `ImportExportPage.Zip.cs` (Line 104-139)<br>`ImportExportPage.xaml.cs` (Line 541-595) | **已验证**。重构为“事务性反序列化”，先将所有文件读入局部临时变量，无异常后才统一对 `DataManager.Data` 赋值。 |
| **P0-5** | P0 | Agent | 分支快照丢失图片/附件/token 字段 | **FIXED** | `Models.cs` (Line 171-200)<br>`MainPage.Bubbles.cs` (Line 201, 431) | **已验证**。`ConversationMessage.Clone()` 中深度复制了 Base64 列表、附件名、Token 数等，分支切换时克隆生效。 |
| **P0-6** | P0 | Agent | 重新生成物理删除原回复，未创建分支 | **OUTSTANDING** | `MainPage.Bubbles.cs` (Line 368) | **整改策略**：重构重新生成（Regenerate）逻辑，将要被覆盖的回复及后续链条保存为新分支：<br>`var msgToRegen = _conv.Messages[idx];`<br>`var bp = _conv.BranchPoints.FirstOrDefault(b => b.AnchorIndex == idx - 1);`<br>`if (bp == null) { bp = new BranchPoint { AnchorIndex = idx - 1, Branches = new List<List<ConversationMessage>>() }; _conv.BranchPoints.Add(bp); }`<br>`bp.Branches.Add(_conv.Messages.Skip(idx).Select(m => m.Clone()).ToList());`<br>`_conv.Messages.RemoveRange(idx, _conv.Messages.Count - idx);` |
| **P0-7** | P0 | Agent | `GetAllImagesAsync` 未包 try/catch 导致发送图片死锁 | **FIXED** | `MainPage.Send.cs` (Line 109-153)<br>`MainPage.Composer.cs` (Line 312-452) | **已验证**。发送循环包裹在 `try-catch` 中，发生 IO 异常时强制重置 `_isSending = false`，恢复发送按钮状态。 |
| **P0-8** | P0 | 两应用 | 删除消息后 `BranchPoints[*].AnchorIndex` 未维护导致越界 | **PARTIALLY_FIXED** | `MainPage.Bubbles.cs` (Line 85, 436-440) in Roleplay | **整改策略**：Roleplay 已加入 `PruneBranchPointsFrom`，但 Agent 侧 `MainPage.Bubbles.cs` (Line 132-157) 的删除事件完全缺失该逻辑。需在 Agent 侧实现相同的剪枝方法并在删除消息时调用。 |
| **P0-9** | P0 | Roleplay | 编辑用户消息重复插入角色开场白 | **FIXED** | `MainPage.SystemPrompt.cs` (Line 255) | **已验证**。`MaybeShowFirstMessage` 中增加了 `_conv.Messages.Count > 0` 守卫，会话非空时不再追加开场白。 |
| **P0-10** | P0 | 两应用 | 流式生成中可通过右键或气泡操作删除历史消息 | **FIXED** | `MainPage.Bubbles.cs` (Line 75 in Roleplay, Line 134 in Agent) | **已验证**。删除消息与分支切换按钮的 Click 事件中均加入了 `if (_isSending) return;` 守卫拦截。 |
| **P0-11** | P0 | Roleplay | 删除角色卡产生永久孤儿会话与残留 | **FIXED** | `CharacterCardsPage.xaml.cs` (Line 352-368) | **已验证**。删卡时级联删除了 `DialoguePoolManager` 内缓存以及 `DataManager.Data.Conversations` 中关联的所有对话。 |
| **P0-12** | P0 | Roleplay | 删除“正在聊天”的活跃角色无保护导致空指针 | **FIXED** | `CharacterCardsPage.xaml.cs` (Line 324-337) | **已验证**。加入防护：当删除卡片的 ID 等于 `SelectedCharacterCardId` 时，弹出 ContentDialog 阻断并返回。 |
| **P0-13** | P0 | Roleplay | PNG 导入把整张原图存两份进主 JSON 且无大小上限 | **PARTIALLY_FIXED** | `ImportExportPage.Characters.cs` (Line 206-210) | **整改策略**：目前虽已将 `IllustrationBase64` 赋空防止了双份存储，但对导入的 PNG 大小依然没有任何上限拦截。需在读取文件时限制最大大小（例如 5MB），超出则抛出异常警告。 |
| **P0-14** | P0 | Roleplay | 角色向导/头像选择超大图直接整读内存转 Base64 崩溃 | **OUTSTANDING** | `CharaWizardPage1.xaml.cs` (Line 49)<br>`UserProfilePage.xaml.cs` (Line 78)<br>`OobeUserPage.xaml.cs` (Line 60) | **整改策略**：重构大图加载。使用 `Windows.Graphics.Imaging.BitmapDecoder` 检查图片分辨率，超过 1024px 则使用 `BitmapTransform` 进行内存降采样压制后再转 Base64 存入，防止 UWP 大对象堆爆内存。 |
| **P0-15** | P0 | 两应用 | `MemoryStore.LoadAsync` 全工程无人调用，重启丢失记忆 | **FIXED** | `DataManager.cs` (Line 49 in Agent, Line 56 in Roleplay) | **已验证**。已在两应用 DataManager 初始化时显式调用了 `await MemoryStore.LoadAsync();` 加载本地向量库。 |
| **P0-16** | P0 | Agent | 保存时后台序列化与 UI 线程并发修改集合冲突 | **OUTSTANDING** | `DataManager.cs` (Line 107-116) | **整改策略**：防止 `Collection was modified` 崩溃。在 UI 线程执行 `SaveAsync` 时，使用 `DataManager.Data.Clone()` 对要持久化的实体数据进行一次深拷贝，再将克隆体传给后台线程序列化落盘；或在对数据集合操作时增加全局重入读写锁 `ReaderWriterLockSlim`。 |

---

### 2.2 P1 级缺陷 —— 功能失效 (28 项)

| Bug ID | severity | 组件/模块 | 缺陷描述 | 状态 | 代码引用位置 | 整改与实施策略 (Code Remediation Details) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **P1-1** | P1 | 两应用 | Claude 原生流式返回内容完全无法解析 | **FIXED** | `ChatJson.cs` (Line 156-176)<br>`MainPage.Response.cs` (Line 122) | **已验证**。通过 `ExtractClaudeText` 专门提取 Claude API 返回的 `text` 或 `completion` 字段并刷新气泡。 |
| **P1-2** | P1 | Core | RAG 嵌入对非 OpenAI 供应商全部失效 | **OUTSTANDING** | `RagMemory.cs` (Line 300-305) | **整改策略**：移除根据 `profile.Model` 强行替换为 `text-embedding-ada-002` 的硬编码。读取用户在界面配置的 `EmbeddingModel` 变量并动态拼装 Payload，支持自定义第三方嵌入模型端点。 |
| **P1-3** | P1 | Core | RAG 请求使用匿名类型在 Release (.NET Native) 抛反射异常 | **OUTSTANDING** | `RagMemory.cs` (Line 310, 409) | **整改策略**：彻底消除 `new { model = m, input = text }` 等匿名类型的序列化。新建具名实体类并加上 `[DataContract]` 和 `[DataMember]` 标记，确保其在 UWP 开启 .NET Native 编译优化后不被反射裁剪。 |
| **P1-4** | P1 | Agent | DeepSeek-R1 类模型多轮工具调用丢失 `ReasoningContent` 报错 | **OUTSTANDING** | `Tools.cs` (Line 78-87, 770-802) | **整改策略**：在 `ApiMessageWithTools` 实体类中补全 `reasoning_content` 属性，并在 `SerializeMessage` 构建下轮请求 Payload 时，将当前助手的推理过程及工具调用顺序序列化为标准 OpenAI 格式。 |
| **P1-5** | P1 | Agent | Claude tool_use `arguments` 原样字符串拼接导致格式损坏 | **OUTSTANDING** | `Tools.cs` (Line 1223) | **整改策略**：修改原有的 raw string 插值拼接方式。将 `arguments` 字符串先使用 `JsonObject.Parse` 解析，若因截断发生异常则在 `catch` 块中补齐闭合括号，确保最终工具参数符合 JSON 语法再输出。 |
| **P1-6** | P1 | Agent | `read_image` 执行后在并行 Tool 回复中间插入 user 消息报 400 | **OUTSTANDING** | `Tools.cs` (Line 1126-1135) | **整改策略**：调整消息追加顺序。暂存工具执行产生的用户提示消息，待当前轮次中所有的并行 Tool 响应消息（Role="tool"）序列化完毕并添加后，再追加 `user` 角色消息，遵循 OpenAI 规范。 |
| **P1-7** | P1 | Roleplay | OOBE “测试连接”对 Claude/Anthropic 恒失败 | **OUTSTANDING** | `OobeApiMainPage.xaml.cs` (Line 248-251) | **整改策略**：根据当前选择的 API 供应商动态调整 HTTP Request Headers。如果是 Claude，移除 `Bearer` 前缀改用 `x-api-key` 头，并追加 `anthropic-version: 2023-06-01`，修改请求体为 Claude 格式。 |
| **P1-8** | P1 | 两应用 | SSE 半行数据或多 `data:` 在同一缓冲区时被丢弃 | **OUTSTANDING** | `MainPage.Response.cs` (Line 91-97)<br>`PlaaApiClient.cs` (Line 108-115) | **整改策略**：用状态机流式读取器替代 `ReadLineAsync`。设置一个字符串累加器 `_streamBuffer`，每次读取数据块后追加，按 `\n` 切分行，只有遇到完整行才处理；若 `data:` 分段跨越了 TCP 数据包，由累加器拼接，防止截断。 |
| **P1-9** | P1 | Roleplay | RAG 记忆 `[事实]` 区块吞掉 `[好感度]` 等字段 | **FIXED** | `RagMemory.cs` (Line 316, 285) | **已验证**。补全了 Favorability 的区块头提取，并加入了 `[0, 100]` 的钳制限制，防数值溢出。 |
| **P1-10** | P1 | Roleplay | 记忆池满后新记忆无法存入，旧记忆无清理 | **FIXED** | `DialoguePool.cs` (Line 405-413) | **已验证**。引入了时间衰减加权过滤，将 24 小时内的记忆给予 `+0.5` 评分加成，7 天内给予 `+0.25` 评分加成，降序排列仅保留前 `MaxMemories` 个，解决常驻满池问题。 |
| **P1-11** | P1 | Core | 更换嵌入模型（如 384 维换 1024 维）维度不匹配静默 0 召回 | **OUTSTANDING** | `RagMemory.cs` (Line 125) | **整改策略**：在计算 Cosine 相似度前做维度比对，若数据库内的 Embedding 维度与当前查询维度不同，抛出清晰的系统提示弹窗，提醒用户并提供“重新生成全部记忆向量库”的功能，拒绝静默过滤。 |
| **P1-12** | P1 | Agent | 自动提取的系统记忆与 RAG 本地向量库隔离互不感知 | **OUTSTANDING** | `MemoryManager.cs` vs `MemoryStore.cs` | **整改策略**：整合记忆子系统。在 `MemoryManager` 提取出新的全局事实记忆后，不仅更新 `DataManager.Data.GlobalMemories`，同时在后台调用 `MemoryStore.AddAsync()` 计算该事实的向量并存入向量数据库，使两者保持实时同步。 |
| **P1-13** | P1 | Roleplay | 记忆总结窗口“最近 N 条”计算包含提示/系统消息导致偏位 | **OUTSTANDING** | `RagMemory.cs` (Line 55-60) | **整改策略**：修正切片范围。在截取最近对话时，先过滤 `conv.Messages.Where(m => m.Role == "user" || m.Role == "assistant")`，在此过滤后的纯对话消息集合上按总结间隔定位 Slice。 |
| **P1-14** | P1 | Agent | 自动记忆按消息数取模触发，因删除重生成导致重复总结或漏总结 | **OUTSTANDING** | `MainPage.Send.cs` (Line 321) | **整改策略**：在 `Conversation` 中增加持久化属性 `LastSummarizedMessageId`。在每次消息生成结束后，计算自上次总结以来的新增用户/助手消息数，达到阈值（如 5 条）才触发，总结后更新 ID，防止重复。 |
| **P1-15** | P1 | Roleplay | JPEG 格式头像被无条件以 PNG 头处理导出损坏 | **OUTSTANDING** | `ImportExportPage.Characters.cs` (Line 43-44) | **整改策略**：根据 `AvatarMimeType` 判断。若是 JPEG 格式，需先将其用 UWP 的 WIC 组件转码为 PNG 二进制数组，然后再应用 `InjectPngTextChunk`；或者针对 JPEG 格式采用另外的元数据写入机制（如 Exif 元数据插值）。 |
| **P1-16** | P1 | Roleplay | 世界书导入丢弃 `disable` 和 `order` 且未按优先级排序生效 | **PARTIALLY_FIXED** | `ImportExportPage.WorldBook.cs`<br>`MainPage.SystemPrompt.cs` (Line 141-154) | **整改策略**：虽然导入处已补齐了字段赋值，但 SystemPrompt 构建时依旧没有根据 `Order` 对关键词条目做升序排列，也未检查 `!Disable`。需在 SystemPrompt 的 linq 查询中增加筛选与排序逻辑。 |
| **P1-17** | P1 | Roleplay | SillyTavern V2/V3 内嵌世界书与备用开场白导入静默丢弃 | **OUTSTANDING** | `ImportExportPage.Characters.cs` (Line 221-273) | **整改策略**：扩展 `ParseCharaJson` 逻辑。增加对 `character_book`（内嵌世界书数据）和 `alternate_greetings`（备用开场白数组）的反序列化，并将解析到的世界书条目和备用开场白级联写入本数据库。 |
| **P1-18** | P1 | Roleplay | 世界书关键词只认英文逗号分隔且子串匹配极易误触失效 | **OUTSTANDING** | `MainPage.SystemPrompt.cs` (Line 246-251) | **整改策略**：重构匹配逻辑。使用 `char[] separators = { ',', '，', ';', '；' }` 拆分关键词列表。同时，改用单词边界正则匹配或精准子串匹配，防止如“ta”（她）误触包含“status”等无关英文单词。 |
| **P1-19** | P1 | Roleplay | PNG 角色卡元数据解析不支持现代 zTXt 压缩与 ccv3 格式 | **OUTSTANDING** | `ImportExportPage.Characters.cs` (Line 183-219) | **整改策略**：引入 zlib 解压支持（利用 `System.IO.Compression.DeflateStream` 去掉前两个字节的 zlib 头部）。在读取 PNG Chunk 时，当遇到 "zTXt" 类型时，读取关键字后用 DeflateStream 解压其数据内容进行解析。 |
| **P1-20** | P1 | Roleplay | 畸形 PNG 读取导致 length 符号溢出越界或无限死循环卡死 UI | **OUTSTANDING** | `ImportExportPage.Characters.cs` (Line 190, 216) | **整改策略**：在解析 PNG 字节流的 `while (pos < fileBytes.Length)` 循环中，加入数据完整性校验：`if (pos + 4 > fileBytes.Length) break;`。读取 `chunkLen` 后，若 `chunkLen < 0` 或 `pos + chunkLen + 12 > fileBytes.Length`，立即 break 并抛出格式损坏异常。 |
| **P1-21** | P1 | Agent | 打开项目不加载知识库列表导致功能不连贯 | **FIXED** | `ProjectSettingsPage.xaml.cs` (Line 31) | **已验证**。在项目页初始化与项目切换时已补全了 `RefreshKbList()` 逻辑，列表可正常展现。 |
| **P1-22** | P1 | Roleplay | OOBE 输入的用户名和人设在当次聊天不生效（未刷新全局实例） | **FIXED** | `OobeUserPage.xaml.cs` (Line 77-84) | **已验证**。补充了对 `DataManager.Data.UserProfiles` 列表的新建/同步，并正确更新了 `ActiveUserProfileId`。 |
| **P1-23** | P1 | Roleplay | OOBE 阶段“前进→返回→前进”会导致在配置表中重复建 API | **OUTSTANDING** | `OobeApiMainPage.xaml.cs` (Line 175-188) | **整改策略**：在“下一步”点击事件中增加幂等性检查。首先检查 `DataManager.Data.ApiProfiles` 中是否已有指定名称或端点的配置，若有则直接更新对应字段，不无条件调用 `.Add()`。 |
| **P1-24** | P1 | Agent | 多智能体/多任务并行时，停止按钮只取消最后一个任务（全局 CTS 被覆写） | **OUTSTANDING** | `MainPage.xaml.cs` (Line 47) | **整改策略**：将单一的 `CancellationTokenSource _streamCts` 字段重构为一个并发字典 `ConcurrentDictionary<string, CancellationTokenSource> _activeTasks`，键为 `ConversationId`，点击停止时按会话 ID 检索并取消。 |
| **P1-25** | P1 | Roleplay | 点击“继续”时没有向网络请求传递 cancellation token，无法终止 | **OUTSTANDING** | `MainPage.Bubbles.cs` (Line 225) | **整改策略**：修改“继续（Continue）”操作的 HTTP 客户端请求调用，将当前会话的 `_streamCts.Token` 传入 `SendAsync` 或 `GetStreamAsync` 方法中，使继续生成也能即时响应用户取消。 |
| **P1-26** | P1 | Agent | 手动触发上下文压缩时设置了 `_isSending=true` 但未修改发送按钮状态 | **OUTSTANDING** | `MainPage.Tokens.cs` (Line 127) | **整改策略**：在执行压缩期间，除了设置 `_isSending = true;` 外，同步执行 `SubmitButton.IsEnabled = false;` 和切换 SubmitIcon 状态，压缩完毕后再予以恢复，优化用户反馈。 |
| **P1-27** | P1 | Roleplay | 发送框回车或点击发送缺失 `_conv == null` 校验造成空指针闪退 | **OUTSTANDING** | `MainPage.Composer.cs` (Line 236) | **整改策略**：在发送消息逻辑的最开始，加入边界保护：`if (_conv == null) { ShowSystemAlert("请先选择或创建一个对话角色。"); return; }`。 |
| **P1-28** | P1 | Agent | `/v1/models` 解析使用裸 `"id":` 字符串匹配导致 permission 字段污染下拉框 | **OUTSTANDING** | `ApiModelFetcher.cs` (Line 147-150) | **整改策略**：重构 API 解析逻辑。不再使用暴力 `IndexOf` 字符串扫描。应使用 `DataContractJsonSerializer` 匹配包含 `id` 的模型实体类列表，仅读取根节点 models 下的 `id` 属性。 |

---

### 2.3 P2 级缺陷 —— 体验缺陷与健壮性 (22 项)

| Bug ID | severity | 组件/模块 | 缺陷描述 | 状态 | 代码引用位置 | 整改与实施策略 (Code Remediation Details) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **P2-1** | P2 | 两应用 | 出现未捕获异常时闪退或白屏，对用户不友好 | **FIXED** | `App.xaml.cs` (Line 21-30 in both apps)<br>UI error pages (Roleplay Line 125-149, Agent Line 109-133) | **已验证**。成功拦截了 XAML `UnhandledException`，并在致命异常时导航至友好的崩溃报告 UI，同时输出本地堆栈日志。 |
| **P2-2** | P2 | 两应用 | 消息气泡辅助操作（如分支切换失败）只在 Debug 输出，对用户静默 | **OUTSTANDING** | 两应用的 `MainPage.Bubbles.cs` 辅助方法 | **整改策略**：移除单纯的 `Debug.WriteLine(ex.Message)`，在 catch 块中增加轻量级的应用内通知提示（如使用 `InAppNotification` 控件或简单的 `SystemBubble` 气泡提示操作失败）。 |
| **P2-3** | P2 | Roleplay | 角色卡批量导入全部失败时没有任何界面弹窗提示 | **OUTSTANDING** | `CharacterCardsPage.xaml.cs` (Line 425) | **整改策略**：在该方法末尾，增加 `else` 分支判断。如果成功导入的卡片数量 `ok == 0`，使用 `ContentDialog` 显式弹出提示框“未检测到有效的 Tavern 格式角色卡，导入失败”，引导用户重试。 |
| **P2-4** | P2 | Roleplay | 世界书导入 0 条记录仍旧显示成功打勾且技术说明未本地化 | **OUTSTANDING** | `ImportExportPage.WorldBook.cs` (Line 100) | **整改策略**：若导入总数 `total == 0`，应展示警告符号而非勾选图标，提示文案“未在文件中找到有效的世界书条目”。将 "Total: {0}" 等英文字符移入资源文件本地化。 |
| **P2-5** | P2 | Agent | 权限文件与 API 请求日志直接覆盖写，异常断电会导致数据损坏截断 | **OUTSTANDING** | `Tools.Permissions.cs` (Line 79-81)<br>`ApiLogger.cs` (Line 213-214) | **整改策略**：改用“写临时文件+重命名”原子写入策略。先将数据写入 `Log.tmp`，完成写入后调用 `StorageFile.ReplaceWithKnownFileAsync` 或 `MoveAsync` 覆盖原日志，保护原始数据。 |
| **P2-6** | P2 | 两应用 | 在流式首个 Token 返回前点击停止，会残留空 AI 消息气泡 | **FIXED** | `MainPage.Send.cs` (Line 150-165 in Roleplay, Line 272-287 in Agent) | **已验证**。若 `IsCancellationRequested` 触发且文本内容与推理内容皆为空，则从 UI 和内存列表同步移除该临时气泡。 |
| **P2-7** | P2 | Agent | 后台生成完毕刷新 Token 时若用户已切换会话，UI 会出现“串味”刷新 | **FIXED** | `MainPage.Send.cs` (Line 360-373) | **已验证**。增加了 `if (conv.Id == _conv?.Id)` 守卫，只有在当前激活会话与回调完成会话一致时才刷新 UI。 |
| **P2-8** | P2 | Agent | 删除/重生成/切换分支后，Token 状态显示区未触发同步刷新 | **FIXED** | `MainPage.Bubbles.cs` (DeleteMsg_Click, SwitchBranch) | **已验证**。在相关数据变更的末尾均补全了 `UpdateTokenDisplay()` 调用。 |
| **P2-9** | P2 | Roleplay | 好感度趋势向上 (up) 与稳定 (stable) 都返回实心桃心图标导致混淆 | **OUTSTANDING** | `MainPage.Suggest.cs` (Line 54) | **整改策略**：明确图标映射逻辑：<br>`string icon = trend == "up" ? "▲" : trend == "down" ? "▼" : "─";` 或者使用更加直观的 Unicode 符号表示感情波动的升降平稳。 |
| **P2-10** | P2 | 两应用 | 继续（Continue）状态下的停止图标与主发送不同且推荐回复无防连点 | **OUTSTANDING** | `MainPage.Bubbles.cs` (Line 183) | **整改策略**：统一将 stop 按钮的 Glyph 字符常量收拢为 `\uE71A`。在推荐回复点击后，立刻置 `IsEnabled = false` 进行去抖，等待生成完毕再解锁。 |
| **P2-11** | P2 | Core | EstimateTokens 中文估算低估，未考虑 CJK 扩展区与中文全角标点 | **OUTSTANDING** | `ContextCompressor.cs` (Line 43-47) | **整改策略**：重构 `IsChinese` 方法，除了 `[0x4E00, 0x9FFF]` 外，增加 CJK 扩展 A/B 区 `[0x3400, 0x4DBF]`、CJK 符号标点 `[0x3000, 0x303F]` 以及全角字符 `[0xFF00, 0xFFEF]` 的判断，将标点也记为 1 个 Token。 |
| **P2-12** | P2 | Roleplay | OOBE 引导页面进度条标为“X/8”但实际上只有 6 步，引发混淆 | **OUTSTANDING** | 各 Oobe 页面后台文件 | **整改策略**：核对实际 OOBE 路径（实际 6 步到 DonePage），修改每个 OobePage 中的 `Text="X/8"` 静态文本为 `Text="X/6"`。 |
| **P2-13** | P2 | Agent | OOBE 最后的 API 设置页面全静态中文硬编码，无法实现多语言 | **OUTSTANDING** | `OobePage2.xaml` | **整改策略**：将 `OobePage2.xaml` 中所有的 `Text` 和 `Header` 静态属性替换为 `x:Uid` 资源占位符，并在多语言资源文件 `Resources.resw` 中定义对应的翻译项。 |
| **P2-14** | P2 | 两应用 | 导出数据时 UWP `FileSavePicker` 未设置 CommitButton 默认文本 | **OUTSTANDING** | 导出相关类 | **整改策略**：在弹出 `FileSavePicker` 前，加上 `picker.CommitButtonText = "确认导出";` 字段，优化平台按钮本地化体验。 |
| **P2-15** | P2 | Roleplay | 导入会话关联资料时隐式更新了全局活跃用户，用户无感知 | **OUTSTANDING** | 导入会话逻辑 | **整改策略**：在切换活跃用户前，增加一个 Toast 气泡或 `ContentDialog` 提示：“已自动切换至该对话关联的用户人设 profile”。 |
| **P2-16** | P2 | Roleplay | `UserProfilePage.xaml.cs` (单数形式) 作为死代码遗留在项目中 | **OUTSTANDING** | `UserProfilePage.xaml.cs` | **整改策略**：在项目构建配置中，彻底安全地移除并物理删除 `UserProfilePage.xaml` 和 `UserProfilePage.xaml.cs` 文件，保留复数形式的 `UserProfilesPage`。 |
| **P2-17** | P2 | Core | 异步 RAG 检索和网络任务没有传递 `CancellationToken` 导致挂起 | **OUTSTANDING** | `RagMemory.cs` (Line 34, 92, 221) | **整改策略**：将当前会话对应的取消 Token（`_streamCts.Token`）从 UI 传递给底层 `RagMemory` API，并在 `HttpClient.SendAsync` 及重排计算的循环中调用 `ThrowIfCancellationRequested`。 |
| **P2-18** | P2 | Roleplay | `SealPoolEmbeddings` 在每次保存时同步更新嵌入向量造成卡顿 | **OUTSTANDING** | `DialoguePool.cs` (SealPoolEmbeddings) | **整改策略**：将 `SealPoolEmbeddings` 内部计算向量逻辑包装在 `Task.Run` 中异步执行，避免在大批量保存时阻塞 UI 线程造成长达数秒的视觉冻结。 |
| **P2-19** | P2 | Core | `CosineSim` 计算未加维度校验，在向量检索时面临数组越界崩溃 | **OUTSTANDING** | `OnEmbedder.cs` (Line 151-170) | **整改策略**：在 `CosineSim` 算子入口处加入防御拦截：`if (a.Length != b.Length) throw new ArgumentException("向量维度不匹配，相似度计算已中止。");` |
| **P2-20** | P2 | Agent | 子代理的深度深度限制是全局静态参数，无法针对单个任务设定 | **OUTSTANDING** | `AgentCore.cs` | **整改策略**：将 `MaxDepth` 从全局静态配置改写为 `AgentProfile` 或 `TaskConfiguration` 的实例属性，允许在发起不同任务时动态调整深度。 |
| **P2-21** | P2 | Core | 许多代码文件的中文注释编码为 GBK，在 Git 提交或多平台下乱码 | **OUTSTANDING** | 全工程源码 | **整改策略**：使用批量转换脚本将全工程下所有的 `.cs` 和 `.xaml` 文件统一另存为标准 **UTF-8 with BOM** 编码，消除混用。 |
| **P2-22** | P2 | Core | 在线 API 重排失败后没有优雅降级到本地 BM25/Cosine 混合检索 | **OUTSTANDING** | `DialoguePool.cs` (RerankFlow) | **整改策略**：在 API Reranker 出现网络抖动或鉴权失败时，捕获异常并在 `catch` 块中自动启用本地混合重排计算（Cosine + BM25），保障核心检索可用。 |

---

### 2.4 P3 级缺陷 —— 功能缺口 (5 项)

| Bug ID | severity | 组件/模块 | 功能缺口描述 | 状态 | 代码引用位置 | 整改与实施策略 (Code Remediation Details) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **P3-1** | P3 | Agent | 聊天记录中没有持久化工具调用的中间轨迹字段 | **OUTSTANDING** | `Models.cs` (ConversationMessage) | **整改策略**：在 `ConversationMessage` 数据模型中增加 `[DataMember(Name = "tool_calls")]` 属性列表，使助手消息输出的工具调用指令和轨迹得以持久化保存至本地 JSON。 |
| **P3-2** | P3 | Agent | 大模型输出的思考（Reasoning）内容在工具调用轮次中丢失 | **OUTSTANDING** | 消息处理流程 | **整改策略**：在会话逻辑中，如果检测到工具调用过程中伴随有 `ReasoningContent`，必须将该推理内容一并归档，在调用工具的中间消息中加以保留并展示在时间轴上。 |
| **P3-3** | P3 | Agent | `ask_user` 面板使用全局单字段 TaskCompletionSource 容易并发挂死 | **OUTSTANDING** | `MainPage.Subagents.cs` (`_askTcs`) | **整改策略**：将 `_askTcs` 重构为基于 Task 标识的 `ConcurrentDictionary<string, TaskCompletionSource<string>> _askTcsMap`。每一个并发的智能体提问分配唯一 GUID，按 ID 回应，防并发覆盖。 |
| **P3-4** | P3 | Agent | 跨智能体执行链路的深度计数丢失，存在死循环调用风险 | **OUTSTANDING** | 智能体调度核心 | **整改策略**：在子智能体执行上下文 `AgentContext` 中增加 `CurrentDepth` 计数器，并向下级联传递。每进行一层代理转发则加 1，达到阈值立即阻断。 |
| **P3-5** | P3 | Agent | 缺失工作流编排（Workflow / Loop）引擎支持 | **OUTSTANDING** | 智能体模块 | **整改策略**：设计轻量级的状态机或基于 JSON 定义的任务流程执行器（WorkflowExecutor），按步骤依次调度子代理或工具，支持条件分支与循环检测。 |

---

## 3. Windows 10 Mobile SDK (10.0.15254) 兼容性策略

为了让言枢 AI 在 Windows 10 Mobile Build 15254 (1709 / Fall Creators Update) 物理设备上流畅运行且不发生 OOM（进程最大可用物理内存通常在 185MB - 390MB 之间），必须对工程做如下架构层级与控件层级的调整：

### 3.1 SDK 目标版本与 Extension SDK 对齐调整
- **问题分析**：`Lumina.Roleplay` 在 `yanshuai.csproj` 中将 `TargetPlatformVersion` 设置为了 `10.0.19041.0`（Windows 10 Version 2004）。由于 19041 的 SDK 包含了许多 Windows 10 Mobile 不支持的 API（移动端在 15254 版本后已停止更新），调用这些 API 在运行时会抛出 `TypeLoadException`。
- **方案实施**：
  1. 修改 `yanshuai.csproj`，将 `TargetPlatformVersion` 调整为 `10.0.15254.0` (或 `10.0.16299.0`，这是 Mobile 实际能支持的最大 SDK 子集编译对齐版本)。
  2. 将引用的 Mobile Extension SDK 从 `WindowsMobile, Version=10.0.19041.0` 降级为 `WindowsMobile, Version=10.0.15254.0`。
  3. 对所有平台差异化 API 使用 `ApiInformation.IsApiContractPresent` 或 `ApiInformation.IsMethodPresent` 进行运行时动态分支隔离。

### 3.2 移动 StatusBar 的动态反射调用
- **设计实践**：为了在非 Mobile 设备（桌面、Xbox）和 Mobile 设备上运行同一套代码且避免 .NET Native 反射剥离引发的崩溃，在 `MainPage.Theme.cs` 中，采用反射动态实例化 StatusBar：
```csharp
var sbType = Type.GetType("Windows.UI.ViewManagement.StatusBar, Windows, ContentType=WindowsRuntime");
if (sbType != null)
{
    var getForCurrentView = sbType.GetRuntimeMethod("GetForCurrentView", new Type[0]);
    dynamic sb = getForCurrentView.Invoke(null, null);
    if (sb != null)
    {
        // 动态调用 Mobile 独有 API
        await sb.HideAsync();
    }
}
```
该写法有效规避了在编译期静态引用 `Windows.UI.ViewManagement.StatusBar` 所导致的原生二进制文件在 PC 平台加载失败的难题。

### 3.3 响应式视觉状态与触控优化
- **双栏响应式设计**：在 `ShellPage.xaml` 中，利用 `VisualStateManager` 监视窗口大小。Windows 10 Mobile 物理分辨率虽高，但其 epx（有效像素）宽度一般只有 360 epx - 480 epx。
- **状态切换**：
  - `MinWindowWidth="0"`（窄窗口状态）：`SplitView.DisplayMode` 设置为 `Overlay`。侧边菜单默认隐藏，用户必须点击汉堡按钮才以浮层显示，聊天界面占满全屏。
  - `MinWindowWidth="900"`（宽窗口状态）：`SplitView.DisplayMode` 设置为 `Inline`。PC 上左侧菜单常驻。
- **触控优化**：对于窄窗口模式，所有底部工具栏和气泡快捷操作的图标按钮（如 Branch 切换、编辑、删除）的最小触控面积设置为 `38x38` 像素以上，防止误触。

### 3.4 Input Pane 软键盘的动态遮挡适配
- **策略实现**：在 Mobile 平台中，软件键盘（SIP）弹出时默认会遮挡底部文本框。在页面加载时注册事件：
```csharp
InputPane.GetForCurrentView().Showing += InputPane_Showing;
InputPane.GetForCurrentView().Hiding += InputPane_Hiding;

private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
{
    args.EnsuredFocusedElementInView = true;
    // 抬高根容器底部 Margin，高度与键盘遮挡区一致
    RootGrid.Margin = new Thickness(0, 0, 0, args.OccludedRect.Height);
}

private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
{
    RootGrid.Margin = new Thickness(0);
}
```
由此可确保聊天发送框在键盘弹起时随动上移，聊天历史气泡也能自动调整滚动视图高度。

### 3.5 系统返回键机制接管
- **机制实施**：Windows 10 Mobile 设备拥有实体或虚拟底部导航返回键。在 `App.xaml.cs` 中实现集中接管：
```csharp
SystemNavigationManager.GetForCurrentView().BackRequested += App_BackRequested;

private void App_BackRequested(object sender, BackRequestedEventArgs e)
{
    var rootFrame = Window.Current.Content as Frame;
    if (rootFrame == null) return;
    
    // 如果 ShellPage 内部子页面有返回历史，先返回子页面
    if (ShellPage.CurrentFrame != null && ShellPage.CurrentFrame.CanGoBack)
    {
        ShellPage.CurrentFrame.GoBack();
        e.Handled = true;
    }
    else if (rootFrame.CanGoBack)
    {
        rootFrame.GoBack();
        e.Handled = true;
    }
}
```

### 3.6 彻底修复 ListView 虚拟化失效 (MemoryList 改造)
- **失效根因**：在 `Lumina.Roleplay` 的 `ConvSettingsPage.xaml` 中，为了能让整页上下滚动，开发者将 `MemoryList`（`ListView` 控件）包裹在了一个纵向 `ScrollViewer` 容器里。这导致 `ListView` 在被父容器测量时获得了“无限高”的可用空间，导致其**关闭了 UI 虚拟化逻辑**。这会一次性将向量库里的上千个事实项目全部实例化，直接挤爆 RAM 导致 OOM。
- **整改方案**：
  1. 剥离 `ScrollViewer` 对 `MemoryList` 的包裹。
  2. 使用 Grid 布局将页面分为两行：第一行放固定设置项，第二行直接放 `MemoryList`，并将第二行的高度设为 `*`（受限高度）。
  3. `MemoryList` 使用其自身的内置滚动条滚动。这可确保其内部的 `ItemsStackPanel` 能够正常回收并重用不可见的 List Item 容器，内存占用瞬间下降 95%。

### 3.7 图像加载的内存压缩策略 (DecodePixelWidth)
- **优化技术**：默认情况下，UWP 将图片加载入内存时，是按图片文件的原始分辨率（例如 1024x1024 或更高）去解码的。一个 2MB 的 PNG 图像解码后在显存/内存中会展开成 `1024 * 1024 * 4` 字节（约 4MB）的原始 RGBA 位图数据。
- **优化实现**：在 `Lumina.Roleplay` 中引入 `DecodePixelWidth` 控制。当在 `CharacterCardView` 或头像列表中需要渲染大图时，显式配置 `BitmapImage`：
```csharp
using (var stream = await cardFile.OpenAsync(FileAccessMode.Read))
{
    var bitmapImage = new BitmapImage();
    bitmapImage.DecodePixelType = DecodePixelType.Logical;
    bitmapImage.DecodePixelWidth = 104; // 针对卡片头像列表，仅解出 104px 宽度的像素
    await bitmapImage.SetSourceAsync(stream);
    AvatarBrush.ImageSource = bitmapImage;
}
```
此举可将每一张头像的物理内存开销从 4MB 级压缩到 100KB - 200KB 级，彻底消除由于卡片列表滚动引发的 OOM。

### 3.8 数据库去 Base64 胖化，重构为本地磁盘存储
- **架构重构**：`Lumina.Roleplay` 现有的设计将角色的头像（`AvatarBase64`）和插图（`IllustrationBase64`）全部转化为 Base64 编码保存在唯一的 `yanshuaiAppData.json` 主 JSON 数据库中。这会导致整个数据库体积暴增至 30MB-50MB，读写时 CPU 会产生数十秒的卡顿。
- **重构方案**：
  1. 引入并封装 `ImageStore` 管理器（参考 `Lumina.Agent` 架构）。
  2. 在导入 PNG 卡片时，将图片二进制流写入本地区域磁盘文件（如 `LocalFolder/images/{cardId}.png`）。
  3. 在 JSON 数据结构中，仅保存文件的相对路径或 ID（例如 `ImageRef = "{cardId}.png"`）。
  4. 启动时执行一次 `MigrateImagesToStoreAsync` 数据迁移：扫描现有 JSON 数据库，将其中的 Base64 字段剥离，转存为本地文件，并重写 JSON 文件，彻底实现主 JSON 的“瘦身”。

### 3.9 嵌入模型 (87.6MB) 加载流式优化
- **性能优化**：`Lumina.Core` 当前在加载向量嵌入模型 `bge.embmodel` 时，调用 `File.ReadAllBytes` 一次性将 87.6MB 的数据读入一个庞大的连续字节数组中，再加上 float 转换，峰值内存瞬间飙升至 175MB 以上，极易在 Mobile 端遭遇虚拟内存碎片分配失败引发的 OOM。
- **流式重构**：重构 `OnEmbedder.LoadModel` 逻辑。放弃一次性读取，改用 `FileStream` 结合 `BinaryReader`，以流式逐字节、逐 Float 解析模型中的权重张量直接填入最终的 float 数组中。这可让峰值堆分配降低 80MB+。

---

## 4. 竞品对比综合分析 (Competitor Comparison Synthesis)

本节系统阐述了言枢 AI (Lumina-Yanshuai) 与主流 AI 客户端 **SillyTavern**、**Cherry Studio** 和 **Chatbox** 在核心架构和功能实现上的对比定位：

### 4.1 技术栈与运行效率对比
- **言枢 AI**：基于 **UWP C# 7.3 与 .NET Native** 原生编译。在 release 模式下，直接编译为目标架构（x86/ARM）机器码，没有中间 JIT 或虚拟机解释损耗。支持在 CPU 上通过 C# SIMD `Vector<float>` 加速向量计算，并支持编写 **HLSL Direct3D 11 Compute Shader**，使得在 Lumia 950 或 Adreno 330 移动端 GPU 上也能运行离线嵌入和模型推理。
- **SillyTavern**：基于 **Node.js 后端与 Web 浏览器前端**。属于 client-server 模式，执行效率完全依赖于浏览器的 V8 JS 引擎，无法进行底层硬件加速（如 SIMD 汇编、D3D 通信）。
- **Cherry Studio**：基于 **Electron (Chromium + Node.js)**。内存开销大，启动即占用 400MB-800MB 内存，对老旧设备及移动端极不友好。虽然支持 transformers.js 和 WebGPU，但高负载的 GPU 调用常伴随着极高的发热量。
- **Chatbox**：采用 **Tauri (Rust 后端) / Flutter** 架构。虽然比 Electron 轻量，但在低配置的 Windows 机器上渲染开销依然高于原生的 UWP XAML 界面，且无法实现精细的系统级交互（如软键盘输入遮挡适配和系统物理返回键深度融合）。

### 4.2 离线化与本地推理能力
- **言枢 AI**：具备**强大的离线闭环能力**。在没有网络连接的情况下，客户端不仅能够使用 C# 自建的 SIMD 嵌入网络处理 RAG，甚至内嵌了纯 C# 编写的自回归 Qwen3.5-0.8B 自主推理引擎（集成 18 层 DeltaNet 线性注意力和 6 层 GQA 全注意力机制，支持 Int8 量化），可以直接在本机实现单机离线对话。
- **SillyTavern / Cherry Studio / Chatbox**：本身**不具备本地 LLM 推理引擎**。它们充当 API 路由中介，必须依赖用户在局域网内开启 Ollama、LM Studio，或者连通云端 OpenAI/Claude 端点才能工作。

### 4.3 角色扮演（Roleplay）机制对比
- **角色卡与多平台解析**：
  - **SillyTavern** 是行业规范制定者，支持完整 Tavern 卡元数据规范、动态表情差分、背景动态绑定等。
  - **言枢 AI** 实现了对 Tavern PNG 元数据的全面解析，同时自研了 `CharaSource.cs` 系统，对 JanitorAI、XingyeAI、Huayu 等多个主流角色共享平台提供了专门的字段清洗与导入适配。
  - **Cherry Studio 与 Chatbox** 定位办公助手，不支持 PNG 角色卡的解析与渲染，仅有简单的“系统提示词”和静态“Assistant 预设”。
- **世界书 (Worldbooks)**：
  - **SillyTavern** 提供了最先进的世界书检索，支持正则表达式、深度优先级合并和词数阈值剪枝。
  - **言枢 AI** 对 SillyTavern 的 JSON 世界书格式提供了原生双向导入/导出兼容（`StWorldBook` 互转），使得社区世界书可以直接导入，并拥有自身的 Order 排序机制。
  - **Cherry Studio 与 Chatbox** 完全缺失世界书功能。

### 4.4 记忆剪枝与向量 RAG 系统
- **检索架构**：
  - **言枢 AI** 实现了极其独特的 **2阶段混合检索**：第1阶段使用 Cosine 相似度粗筛向量数据，第2阶段应用如下公式进行本地融合排序：
    $$\text{Score} = 0.5 \times \text{CosineSim} + 0.3 \times \text{BM25} + 0.2 \times \text{Importance}$$
    其中融合了词频 Overlap 和摘要提取的“记忆重要性”指标。
  - **SillyTavern** 的向量 RAG 必须依赖用户在外部启动 ChromaDB 等数据库服务。
  - **Cherry Studio** 采用本地 sqlite-vector 检索 PDF 文件。
  - **Chatbox** 仅支持滑窗裁剪，无深层 RAG 逻辑。
- **记忆淘汰与衰减**：
  - **言枢 AI** 的 `DialoguePool` 支持新近度衰减与重要性保留逻辑（24h/7d 权重加成），能够保障近期发生的事件不被 RAG 淘汰，同时淘汰低信息密度旧记忆。
  - **其他竞品** 仅做无脑 Token 截断，无法感知记忆的主动重要性。

---

## 5. 优先级整改路线图 (Prioritized Remediation Roadmap)

本路线图将整改任务划分为四个阶段，优先解决严重阻碍系统部署和引发崩溃的致命问题，随后逐步完善系统功能与高级表现。

```
+-----------------------------------------------------------------------+
|  【第一阶段】致命崩溃修复与 Mobile 内存控制 (P0 & Mobile 适配)        |
|  - 修复 P0-6 重新生成未建分支、P0-8 Agent 侧删除消息分支越界          |
|  - 改造 ConvSettingsPage，剥离 ScrollViewer 以恢复 ListView 虚拟化   |
|  - 引入 DecodePixelWidth 限制头像/立绘加载内存，实现大图降采样        |
|  - 迁移主 AppData JSON 数据库中的 Base64 图像数据至磁盘 ImageStore   |
|  - 引入并发锁防护 P0-16 的保存冲突崩溃                               |
+------------------------------------+----------------------------------+
                                     |
                                     v
+-----------------------------------------------------------------------+
|  【第二阶段】核心模型对接与流式功能补全 (P1 级功能修复)              |
|  - 移除 RagMemory.cs 嵌入模型硬编码，支持非 OpenAI 嵌入端点           |
|  - 消除匿名序列化，为 Release (.NET Native) 编写强类型数据契约        |
|  - 修复 DeepSeek-R1 工具轮次中的 ReasoningContent 丢失问题           |
|  - 重构 SSE 解析器，支持流式跨包拼接累加                             |
|  - 补全世界书 entry.Disable 和 entry.Order 的匹配与升序排序机制      |
+------------------------------------+----------------------------------+
                                     |
                                     v
+-----------------------------------------------------------------------+
|  【第三阶段】用户体验微调与界面本地化规范 (P2 级体验修缮)            |
|  - 修复 OOBE 阶段步骤显示 X/8 -> X/6 对齐                            |
|  - 本地化 OobePage2.xaml 中的所有中文静态文本为 Resources 绑定        |
|  - 修正 Suggest 中好感度趋势 up/stable 桃心图标重复的显示混乱        |
|  - 补全 CosineSim 相似度计算中的维度越界防御校验                      |
|  - 批量重编码工程文件注释为 UTF-8 with BOM                             |
+------------------------------------+----------------------------------+
                                     |
                                     v
+-----------------------------------------------------------------------+
|  【第四阶段】智能体深度追踪与多任务流编排 (P3 级缺口填补)            |
|  - 拓展 ConversationMessage 模型，持久化 ToolCalls 中间执行轨迹      |
|  - 将 ask_user 全局单字段 tcs 重构为 Map 字典，支持并发智能体询问     |
|  - 在 AgentContext 中嵌入深度计数器，限制子智能体递归循环深度限制    |
|  - 实现 Workflow 状态机编排引擎，支持复杂的条件任务调度              |
+-----------------------------------------------------------------------+
```

### 5.1 第一阶段实施细则（致命崩溃与 Mobile 降耗）
- **交付目标**：在 Windows 10 Mobile 物理设备上运行本客户端不发生因图像大图、Base64 或列表滚动导致的 OOM。
- **验证手段**：
  - 启动应用加载含有 50+ 角色卡的卡片库，监控物理内存变化低于 100MB。
  - 打开 RAG 记忆设置页面，滚动含有 1000+ 记忆条目的列表，UI 维持 60 FPS 且无卡顿。

### 5.2 第二阶段实施细则（核心模型与通信修复）
- **交付目标**：本地混合重排、非 OpenAI 嵌入端点及 DeepSeek-R1 API 对接成功率达 100%。UWP Release 包运行良好。
- **验证手段**：
  - 切换本地编译的嵌入维度为 384 并运行检索，提示窗口不抛越界且能按需重置。
  - 进行多轮带工具的 DeepSeek 提问，观察请求 JSON 正确封装了前序的 `reasoning_content`。

### 5.3 第三与第四阶段实施细则（体验与高级功能）
- **交付目标**：UI 本地化规范完全契合“心象 (Mindscape)”设计，多代理并发与工作流机制无阻塞运行。
- **验证手段**：
  - 在语言设为英文的 Windows 11 设备上，OOBE 页面完全呈现对应英文词条。
  - 并发发起 3 个子代理的提问任务，主页面能够并行弹窗并正确收集各自的回执。
