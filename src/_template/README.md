# Bot 骨架模板

新建 bot 时从这里复制，**不要**直接复制某个真实 bot（会带进业务代码和遗留配置）。

`Lunarium.Bot.Template` 是一个能 `dotnet build` / `dotnet publish` 通过的最小骨架，
每次改完构建配置请回来跑一次，确保它没烂掉：

```bash
dotnet build src/_template/Lunarium.Bot.Template.csproj
```

> 为什么放在 `src/` 而不是 `build/templates/`：中央包管理（`src/Directory.Packages.props`）
> 只作用在 `src/` 下。放到 `build/` 里 `PackageReference` 就没有版本号可解析（NU1604），
> 模板也就无法被构建验证。前缀 `_` 表示它不是真实项目，不会进 `Lunarium.Bot.slnx`。
>
> 目录深度与真实 bot 一致（`src/<Project>/`），所以复制出去后**相对路径无需任何修改**。

## 新建一个 bot

以新建 `Lunarium.Bot.Foo` 为例：

```bash
cd /path/to/LunariumBot.Net

cp -r src/_template src/Lunarium.Bot.Foo
cd src/Lunarium.Bot.Foo

mv Lunarium.Bot.Template.csproj Lunarium.Bot.Foo.csproj
mv TemplateBot.cs FooBot.cs

# 替换命名空间与类名（macOS 用 sed -i ''）
grep -rl 'Lunarium.Bot.Template\|TemplateBot\|TEMPLATE_BOT_TOKEN' . \
  | xargs sed -i \
      -e 's/Lunarium\.Bot\.Template/Lunarium.Bot.Foo/g' \
      -e 's/TemplateBotService/FooBotService/g' \
      -e 's/TemplateConfig/FooConfig/g' \
      -e 's/TEMPLATE_BOT_TOKEN/FOO_BOT_TOKEN/g'

dotnet build .        # 先确认能编译
rm README.md          # 这份说明不要带进新项目
```

再把项目加进 `Lunarium.Bot.slnx` 的 `/src/` 分组。

## 三个「本地全绿、上线才炸」的坑

| 坑 | 后果 | 模板里对应位置 |
|---|---|---|
| 漏掉 `<Import Project="../../build/Bot.props" />` | 发布出**框架依赖**产物，服务器没装 dotnet，进程直接起不来 | csproj 第一行 |
| 漏掉 `Content` 的 `ExcludeFromSingleFile` | 配置模板被嵌进 89 MB 的单文件二进制，远端改 `appsettings.json` 无效 | csproj 的 `Content` 组 |
| Debug 下也开 `RuntimeIdentifier` | 本地调试变成跨架构，IDE 断点/热重载异常 | `build/Bot.props` 已按 `Configuration` 分档 |

## 必须自己补的部分

模板**只**包含每个 bot 都必须一致的东西。下面这些没有模板，照 `Lunarium.Bot.Ticket` / `Lum` 现写：

- 业务命令模块（`[SlashCommand]` / `[ComponentInteraction]`）——注意 `[Group]` 里的组件交互必须写 `ignoreGroupNames: true`
- 交互会话（`SessionManager` + `IInteractionSession`）
- 数据持久化（写盘一律「临时文件 + `Move`」原子替换）
- 部署脚本 / systemd unit（见下）

## 部署侧约定

- 环境变量文件：`/etc/lunariumbot/<slug>.env`（`600 root:root`），只放 token，**不要写进 unit**
- unit 名：`lunariumbot-<slug>.service`；`ExecStart` 指向发布产物 `<部署目录>/<AssemblyName>`
- 单文件发布必须设 `DOTNET_BUNDLE_EXTRACT_BASE_DIR=<部署目录>/.bundle`（`ProtectHome=read-only` 下必需）
- `TimeoutStopSec` 必须大于程序 `ShutdownTimeout`（模板里是 120s，unit 用 150s）
