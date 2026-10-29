# CODE_STYLE.md — 代码规范与经验

> 只收录**已在本仓库验证过**的规则与踩坑结论。每条尽量写清「规则 / 原因 / 反例」。
> 架构总览见 `README.md`，日志细节见 `LUNARIUM_LOGGING.md`，部署见 `DEPLOY.md`。

## 0. 上游基线

本仓的 C# 风格**默认沿用 [dotnet/runtime 的 C# Coding Style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)**，下面各节只记录本仓特有规则与差异。

明确采用的条款：

- Allman 花括号（左括号另起一行）；4 空格缩进、不用 Tab
- 私有/内部字段 `_camelCase`，静态字段 `s_`；`static readonly` 而不是 `readonly static`
- 显式写可见性且放最前；`using` 放在 namespace 外并排序（`System.*` 在最前）
- 避免连续空行与多余空白；字段声明放在类型顶部
- `nameof(...)` 优先于字符串字面量
- 内部/私有类型默认 `static` / `sealed`（除非确实需要继承）
- PascalCase 命名常量与方法（含本地函数）

本仓**有意偏离**的条款：

| 上游条款 | 本仓做法 | 原因 |
| --- | --- | --- |
| 16. 非 ASCII 用 `\uXXXX` 转义 | 直接写中文 | 面向 Discord 的文案本身就是中文，转义后不可读 |
| 10. `var` 仅当右侧显式写出类型 | 允许 `var` 接方法返回值（如 `var threads = await ...`） | 与现有代码保持一致，减少噪音 |
| （上游未涉及） | §1.1 禁止 `.ConfigureAwait(false)` | 见下文 |

尚未落实的：上游靠 `.editorconfig` + `dotnet-format` 固化风格，本仓暂无 `.editorconfig`，样式靠人工与评审。

## 1. 异步

### 1.1 不要写 `.ConfigureAwait(false)`

- **规则**：`src/` 下所有代码一律不加 `.ConfigureAwait(false)`。全量清理见提交 `f45420a`（10 个文件 / 75 处）。
- **原因**：这些进程都跑在 Generic Host 下，没有 `SynchronizationContext`，`ConfigureAwait(false)` 没有收益，只增加噪音和行宽；现代 .NET 应用侧写法默认省略。
- **反例**：`await FooAsync().ConfigureAwait(false);`
- **例外**：`Discord.Net/` 是 vendored 第三方源码，不改；`Reference/` 是历史存档，不改。
- **自查**：`grep -rn 'ConfigureAwait(false)' src/` 应为空。

## 2. 日志（`Lunarium.Logging`）

### 2.1 异常必须是第一个参数

- **规则**：`_logger.Error(ex, "…", args)`。
- **原因**：`Lunarium.Logging.ILogger` **没有** `Error(string, Exception)` 重载。写成 `_logger.Error("xxx 失败", ex)` 会静默绑定到 `Error(string, params object?[])`，异常被当成参数值塞进消息，**堆栈丢失**。
- **自查**：`grep -rn 'Error("[^"]*", *ex' src/`

### 2.2 审计日志用根 logger

- **规则**：需要进 `Audit` / `CommandAuth` 之类按前缀过滤的 sink 时，用根 logger 直接打，不要用 `ForContext(...)` 派生的 logger。
- **原因**：`ForContext` 前缀会层层叠加，过滤用的是 `StartsWith`，带了前缀就永远匹配不上。

### 2.3 不要只写 `_logger.Error(ex)`

- **规则**：异常日志至少带一个定位键（`GuildId` / `ThreadId` / `ChannelId` / `SessionId`）。
- **原因**：单参重载没有任何上下文，事后无法判断是哪个服务器、哪一轮。

## 3. 配置

### 3.1 写回只走 `ConfigLoader<T>.TryUpdate`

- **原因**：绕过校验与「临时文件 + 原子 Move」的写盘路径；`.Value` 上的属性只读语义。

### 3.2 `appsettings.json` 不入库

- **规则**：只提交 `appsettings.template.json` / `appsettings.health.template.json`；`appsettings.health.json` 可入库。

### 3.3 字段改名 = 破坏性变更

- **规则**：改配置模型字段名时，必须同步修改服务器上的 `appsettings.json`，并在提交信息里写明。
- **原因**：`JsonConfigOptions.Read` 使用 `UnmappedMemberHandling.Disallow`，未知字段会让**启动校验直接失败**（不是静默忽略）。例如 `NotifierChannelId` → `StaffChannelId`（`8a31222`）。

### 3.4 配置模型实现 `IValidatableObject`

- **原因**：`ValidationResult` 的 message 会原样回显给 Discord 用户，校验失败必须能看懂。

## 4. Discord 交互

### 4.1 `[Group]` 模块里的组件/模态必须 `ignoreGroupNames: true`

- **原因**：Discord.Net 默认把组名拼进命令路径，而回调查找只用原始 customId，漏写就是 `UnknownCommand`。

### 4.2 鉴权只走 `RuleEngine` + `BaseRuleSet`

- **规则**：不要新写 `AuthUtils`；不要在 handler 里手搓 `if (user.Roles...)`。
- **规则顺序有意义**：`MaintainerBypass` 命中会**短路**后续规则。所以「范围类」规则（`RequireInChannel`、`RequireInGuild`）必须排在 Bypass **之前**，否则部署者能绕过范围限制。

### 4.3 组件/模态回调要二次鉴权

- **原因**：会话默认 1 小时且每次访问刷新。打开面板时校验过的权限，不等于点「应用」时的权限。落盘/改状态前再跑一次 `RuleEngine`。

### 4.4 回复可见性是有意选择，不要"顺手"改

- **示例**：`/维护 编辑配置` 的面板**故意不用 ephemeral**——留在管理频道里，便于监督管理组的操作、避免暗改。改成 ephemeral 前先确认这是产品意图。

## 5. Discord REST

### 5.1 所有 REST 调用走 `RateLimitedExecutor` + `DiscordRetryPolicy.HttpRetryPolicyConfig`

- **原因**：统一并发/限速；重试策略对 404 / 403 / 400 **不重试**，对 429 / 5xx / 网络异常指数退避（2/4/8 秒）。
- **反例**：直接 `_client.GetChannelAsync(...)` / `thread.ModifyAsync(...)` 绕过限流（置顶监控早期版本踩过）。

### 5.2 不做删除类操作

- **规则**：Archive 全项目零 `DeleteAsync` / `BulkDelete` / `Ban` / `Kick` / 权限覆盖写入。
- **归档不是删除**：`ModifyAsync(p => p.Archived = true)`，帖子仍可访问、可搜索、可恢复。

### 5.3 `Flags` 是整字段覆盖

- **规则**：`PATCH /channels/{id}` 的 `flags` 必须读改写：`current | ChannelFlags.Pinned` / `current & ~ChannelFlags.Pinned`。
- **注意**：基准值来自缓存/一次性 GET，可能落后；线程上目前只有 `Pinned` 一位在用，风险可控但不要当成原子操作。

## 6. 消息渲染

### 6.1 用现成的预制件，不要手搓

- **规则**：`MessagePrefab` / `ModalWindowPrefab`；功能模块自己的卡片集中在 `*Component` 静态工厂（如 `NotifierComponent`）。不要新写 `EmbedBuilder` 拼装。

### 6.2 Component V2 的 message flag 由 Discord.Net 自动补

- 发送时顶层出现非 `ActionRow` 组件会自动加 `MessageFlags.ComponentsV2`；
- **编辑**时不会补，但服务端保留原 flag，所以改卡片只需传 `components`。

## 7. 持久化

### 7.1 用 `JsonDocumentStore<T>` + 源生成 `JsonSerializerContext`

- **原因**：原子写盘 + 防抖；AOT/裁剪下必须走 `JsonTypeInfo` 重载。

### 7.2 STJ 不支持非字符串字典键

- **规则**：`Dictionary<ulong, …>` / `record struct` 当 key 都不能直接落盘，用 `List<Entry>` 表达（`PinnedMonitoringDocument` 就是这么做的）。

### 7.3 枚举落盘用 `JsonStringEnumConverter<T>`

- **注意**：改枚举成员名 = 改数据格式。无历史数据可直接改，有数据必须写迁移或让 `allowCorruptRebuild` 兜底。

## 8. 领域建模

### 8.1 ID 用强类型 `readonly record struct`

- **示例**：`GuildKey` / `PinnedChannel` / `PinnedThread`。编译器帮你挡住 guild id / thread id 串用。

### 8.2 周期性对账优于事件状态机

- **经验**：置顶监控从「`ThreadUpdated` 事件 + 超时线程 + 投递队列」改成「每轮扫描 diff」，代码少了三分之一，状态从 7 态降到 4 态，且没有跨线程共享。
- **判据**：如果业务本身是定时任务、对实时性没有硬要求，先考虑「快照对账」而不是事件订阅。

### 8.3 会改外部状态的操作必须幂等 + 有上限重试

- **模板**：抢占状态 → 执行 → 失败回滚并把重试时间顺延 → 超过上限收尾并留日志。

## 9. 构建与提交

### 9.1 按项目构建

- **规则**：`dotnet build src/<Project>/<Project>.csproj`。
- **原因**：解决方案里可能包含迁移中的项目，全量构建失败属预期；判断回归以相关项目为准。

### 9.2 提交只带相关文件

- **规则**：不要 `git add .` / `git add -u` 一把梭——本仓库经常有长期未提交的并行改动。
- **教训**：`git add -u src/` 会把别人未提交的 `using` 清理一起卷进提交，事后要 `reset --soft` 拆开。

### 9.3 提交信息

- **格式**：Conventional Commits + 中文正文（`feat(archive): …` / `fix(…): …` / `refactor(…): …` / `style(…): …`）。
- **正文**：写清「改了什么 / 为什么 / 影响面」，破坏性变更（配置字段、持久化格式）必须显式标出。

## 10. 已踩过的 BCL / C# 坑

| 现象 | 结论 |
| --- | --- |
| `bool.TryParse` 返回 false | 只认 `True`/`False`（大小写不敏感、允许首尾空白）；`1` / `yes` / `on` 全部失败 |
| `As<Version>()` / `As<Uri>()` 编译不过 | `IParsable<T>` 覆盖面很窄，`Version`、`Uri` 都不实现 |
| `CS0118: 'X' is a namespace but is used like a type` | 命名空间与类型同名，且当前代码在该命名空间之下 |
| STJ + Native AOT 报 "Reflection-based serialization has been disabled" | 必须用源生成上下文 + `JsonTypeInfo` 重载 |
| `Environment.SetEnvironmentVariable(..., User)` 在 Linux 上无生效 | 非 Process 目标是静默 no-op，不抛异常 |
| `HttpListener.Stop()` 后客户端收到 200 + 空 body | 优雅关闭要先取消、等在飞请求，最后才 `Stop()` |
