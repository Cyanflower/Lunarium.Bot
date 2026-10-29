# CLAUDE.md — Agent 速查

> 架构、项目状态、新 Bot 脚手架一律以 [`README.md`](./README.md) 为准。
> 本文件只记录 agent 最容易踩的坑，不重复架构说明。

## 硬约束

- **新代码 / 重构照 `src/Lunarium.Bot.Ticket`**；共享设施只加到 `Lunarium.Bot.Framework` / `Lunarium.Bot.AI`。
- **不要引用已删除的工程**：`Lunarium.Bot.Extensions`、`Lunarium.Bot.Logger`、`Lunarium.Bot.Scheduler`（2026-10-29 删除，统一用 NuGet `Lunarium.Logging` / `Lunarium.Scheduler`）；`SearchBot` 仍引用后两者，**当前无法编译**。
- **不要引用已移出仓库的项目**：`ReleaseBot`、`VoteBot`、`LunariumProxy`、`Examples`（2026-10-29 移出，前三个见提交 `97d4c89`，Examples 见 `3ea8a1d`）；也不要参考 `SearchBot` 的架构。
- **配置文件**：`appsettings.json` 是本地运行配置，**不入库**；只提交 `appsettings.template.json`。`appsettings.health.json` 可入库。
- **配置写回**只能走 `ConfigLoader<T>.TryUpdate`；不要直接改 `.Value` 上的属性。
- **鉴权**用 `RuleEngine` + `BaseRuleSet`；不要新写 `AuthUtils`。
- **消息**用 `MessagePrefab` / `ModalWindowPrefab`；不要新写 `EmbedBuilder` 拼装。
- 改到 `Archive` / `DataCenter` 时先确认其迁移状态（README §1.3），不要假设它们已符合标准。

## 常用命令

```bash
# 构建（Ticket 是当前唯一完整走通标准的参考实现）
dotnet build src/Lunarium.Bot.Ticket/Lunarium.Bot.Ticket.csproj

# 构建共享库
dotnet build src/Lunarium.Bot.Framework/Lunarium.Bot.Framework.csproj

# 测试：tests/ 已清空，暂无测试项目

# 本地运行（CWD 必须是项目目录，配置按 CWD → 程序目录查找）
cd src/Lunarium.Bot.Ticket
TICKET_BOT_TOKEN=... dotnet run
```

- 全解决方案构建可能因迁移中的项目失败（当前是 Archive），属预期；判断改动是否引入回归时以相关项目为准。
- vendored Discord.Net 缺失时先跑 `Discord.Net.Local/sync-discordnet.sh`。

## 改完自查

1. 是否只引用了 `Framework` / NuGet `Common` / `Logging`，没有碰遗留副本？
2. 配置模型是否实现了 `IValidatableObject`，错误文案是否可回显？
3. 新增交互是否走了 `RuleEngine`，失败是否 `ephemeral` 回复？
4. 日志上下文是否带 `RootContext` 前缀，审计事件是否进了 `Audit` sink？
5. `Program.cs` 是否仍然只有四段职责？

## 参考资料

- [`README.md`](./README.md)：架构标准、项目状态、脚手架清单
- [`LUNARIUM_LOGGING.md`](./LUNARIUM_LOGGING.md)：日志上下文与过滤前缀的坑
- [`DEPLOY.md`](./DEPLOY.md)：远端部署手册（**被要求"部署到服务器"时先读这份**，含 bash 与 CLI 两条等价路径、判定标准、禁令与回滚）
