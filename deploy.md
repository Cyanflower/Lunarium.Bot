# deploy.md — 远端部署手册（供 AI 直接执行）

> **读者**：被要求「把 X 部署到服务器」的 AI agent，或照着做的人。
> **一句话**：每一步都有「通过标准」，达不到就停下报告，**不要顺手修别的**。

---

## 0. 30 秒速览

| 项目 | 产物 / ExecStart 名 | 远端目录 | systemd 单元 |
|---|---|---|---|
| Ticket | `Lunarium.Bot.Ticket` | `/root/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Ticket` | `lunariumbot-ticket` |
| Archive | `Lunarium.Bot.Archive` | `…/Lunarium.Bot.Archive` | `lunariumbot-archive` |
| DataCenter | `Lunarium.Bot.DataCenter` | `…/Lunarium.Bot.DataCenter` | `lunariumbot-datacenter` |

SSH 别名 `BotCenterVPS`（`User=root`，已配置密钥，`BatchMode` 可用）。

**三条执行路径，任选，允许混用：**

- **A. CLI**：`python3 build-deploy-tools/deploy.py deploy Ticket`
- **B. 纯 bash**：本文每步都给了等价命令
- **C. 混合（推荐）**：探查 / 备份 / 排障用 bash，构建 + 推送 + 重启用 CLI

CLI 与 bash 做的事完全等价，**不要两条路同时跑**（会重复构建、重复 restart）。

---

## 1. 先核对事实，不要凭记忆

任何操作前先跑这一段（纯只读）：

```bash
ssh BotCenterVPS '
  echo "--- 单元 ---"
  for u in lunariumbot-ticket lunariumbot-archive lunariumbot-datacenter; do
    printf "%-28s %-8s %s\n" "$u" "$(systemctl is-active $u)" \
      "$(systemctl show $u -p ExecStart --value | sed "s/.*path=//;s/ ;.*//")"
  done
  echo "--- 部署目录 ---"; ls -1 /root/services/cyanflower/Lunarium.Bot/
  echo "--- 磁盘 ---"; df -h /root | tail -1
  echo "--- 运行时 ---"; command -v dotnet || echo "dotnet: 不存在"
'
```

必须确认的两件事：

1. **目标单元的 `ExecStart` 指向的路径，恰好等于「远端目录 + 产物名」**。不一致就停下——说明单元和历史发布不同步，见 §7。
2. **`dotnet` 在服务器上不存在**。所以发布物必须是**自包含单文件**；出现 `.dll` 说明发布形态错了，不要上传。

---

## 2. 环境事实（这些值不要重新推导）

| 事实 | 值 |
|---|---|
| 部署根 | `/root/services/cyanflower/Lunarium.Bot` |
| 部署目录结构 | `<根>/<产物名>`，目录名与产物名相同 |
| 服务器运行时 | **没有 .NET**，只能跑自包含单文件 |
| 单文件解压目录 | 单元里设了 `DOTNET_BUNDLE_EXTRACT_BASE_DIR=<部署目录>/.bundle` |
| Token 位置 | `/etc/lunariumbot/<slug>.env`（`600 root:root`，slug = ticket / archive / datacenter） |
| 单元加固 | `ProtectSystem=full`、`ProtectHome=read-only`、`ReadWritePaths=<部署目录>` |
| 停机预算 | 单元 `TimeoutStopSec=150`，程序 `ShutdownTimeout=120` |
| 重启策略 | `Restart=always` + `RestartSec=10` + `StartLimitIntervalSec=300`/`Burst=10` |

**部署目录同时也是运行时数据目录**，里面混着这些**不属于发布产物**的东西：

```
appsettings.json / appsettings.jsonc      ← 服务器真实配置（绝不覆盖）
Logs/  storage/  data/  CacheStore/  sqllog/  Resources/  dist/   ← 运行时数据
.bundle/                                   ← 单文件解压缓存
```

---

## 3. 硬性禁令

| 禁止 | 后果 |
|---|---|
| `rsync --delete` | 删除 `appsettings.json`、`Logs/`、`storage/`、`.bundle/` —— **生产数据丢失** |
| `rsync --inplace` | 覆写正在运行的可执行文件 → `ETXTBSY: Text file busy` |
| 远端路径写 `~` | `~` 靠远端 shell 展开，与单元里的绝对路径不一致 → **推了新文件但跑的还是旧的** |
| 上传 `appsettings.json` / `appsettings.health.json` | 覆盖服务器真实配置，bot 可能直接起不来 |
| 把 token 写进 `.service` 文件 | 单元权限 `644`，token 泄漏 |
| `systemctl stop` 后不收尾 | 服务保持下线 |
| 在服务器上 `dotnet build` | 没有 dotnet |
| 未经确认改 `/etc/systemd/system/lunariumbot-*.service` | 可能把 `After=` 等业务依赖改掉 |

---

## 4. 标准流程

```mermaid
flowchart LR
    A[预检] --> B[备份现行二进制]
    B --> C[dotnet publish]
    C --> D[剔除本地配置]
    D --> E[rsync]
    E --> F[sha256 比对]
    F --> G[restart]
    G --> H[健康判定]
    H -->|失败| I[journalctl + 回滚]
```

### Step 1 构建

```bash
STAGING=build-deploy-tools/.staging/Ticket
rm -rf "$STAGING"
dotnet publish src/Lunarium.Bot.Ticket/Lunarium.Bot.Ticket.csproj \
  -c Release -o "$STAGING" --nologo
```

CLI：`python3 build-deploy-tools/deploy.py build Ticket`

**通过标准**：`rm` 后的目录里正好 3 个文件——1 个可执行 + `appsettings.template.json` + `appsettings.health.template.json`。出现 `.dll` / `.pdb` 即为发布形态错误，停下。

### Step 2 剔除本地配置

```bash
rm -f "$STAGING/appsettings.json" "$STAGING/appsettings.health.json"
```

CLI 自动完成（`local_artifact.exclude`）。

### Step 3 备份现行产物（回滚点）

```bash
REMOTE=/root/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Ticket
TS=$(date +%Y%m%d_%H%M%S)
ssh BotCenterVPS "cp -p $REMOTE/Lunarium.Bot.Ticket $REMOTE/Lunarium.Bot.Ticket.bak_$TS"
```

复制正在运行的二进制是安全的（运行中的进程持有旧 inode）。**保留最近 1–2 份即可**，别无限堆积。

### Step 4 推送

```bash
ssh BotCenterVPS "mkdir -p $REMOTE"
rsync -avz --partial --info=progress2 --chmod=F755 \
      "$STAGING/" "BotCenterVPS:$REMOTE/"
```

CLI：`python3 build-deploy-tools/deploy.py push Ticket`（构建 + 推送 + 校验，不重启）

**通过标准**：rsync 退出码 0，且 `Transfer starting` 之后有实际传输字节（产物没变时可以只有几十字节的 json）。

### Step 5 校验

```bash
sha256sum "$STAGING/Lunarium.Bot.Ticket"
ssh BotCenterVPS "sha256sum $REMOTE/Lunarium.Bot.Ticket"
```

**通过标准**：两行哈希逐字相同。不同就重新推送，不要继续。

### Step 6 重启

```bash
ssh BotCenterVPS 'systemctl restart lunariumbot-ticket'
```

CLI：`python3 build-deploy-tools/deploy.py restart Ticket`（含健康判定）

注意：单元 `TimeoutStopSec=150`，优雅退出最长要等 2.5 分钟。**命令超时要给到 ≥180s**，否则会误判失败。

### Step 7 健康判定（三步，缺一不可）

```bash
sleep 3
ssh BotCenterVPS 'systemctl is-active lunariumbot-ticket'
ssh BotCenterVPS 'systemctl show lunariumbot-ticket -p NRestarts -p MainPID'
ssh BotCenterVPS 'journalctl -u lunariumbot-ticket -n 40 --no-pager -o short-iso'
ssh BotCenterVPS 'journalctl -u lunariumbot-ticket --since "2 minutes ago" -p err --no-pager'
```

**通过标准（全部满足）**：

1. `is-active` → `active`
2. `MainPID` 是一个新的非 0 值，且 `NRestarts` 没有持续增长（`Restart=always` 下崩溃循环里也会瞬间 `active`）
3. 最近日志里出现 bot 自己的就绪标志（Ticket/Archive 是 `🎉 Bot 已就绪`，DataCenter 是 `Listener` 正常输出）
4. `-p err` 自重启以来无输出

任一条不满足：**先回滚，再分析**（§6）。

---

## 5. 必须先问人的情况

- 要删除远端任何文件或目录
- 要修改 `/etc/systemd/system/lunariumbot-*.service` 或 `/etc/lunariumbot/*.env`
- 服务将失去响应超过 2 分钟
- 目标项目不在 §0 的表里（例如 ReleaseBot —— 它是停用状态，不是部署目标）
- 判不清某一步的结果时

---

## 6. 回滚

```bash
ssh BotCenterVPS '
  D=/root/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Ticket
  BAK=$(ls -t $D/Lunarium.Bot.Ticket.bak_* | head -1)
  cp -p "$BAK" $D/Lunarium.Bot.Ticket
  systemctl restart lunariumbot-ticket
  sleep 3; systemctl is-active lunariumbot-ticket
'
```

回滚后仍需执行 §4 Step 7 的完整健康判定。

回滚**不会**恢复 `appsettings*`（本流程从不覆盖它），也不会恢复运行时数据。

---

## 7. 故障处置

| 症状 | 先查 | 处置 |
|---|---|---|
| `systemctl start` 报 `status=159` / `Failed at step EXEC` | `ls -l <部署目录>/.bundle` | 单文件解压目录不可写。确认单元里有 `DOTNET_BUNDLE_EXTRACT_BASE_DIR` 且指向 `ReadWritePaths` 内的路径 |
| `status=203/EXEC` | `systemctl show -p ExecStart` | `ExecStart` 路径不存在 → 产品名与单元不一致，改单元或改名（须先问人） |
| `is-active` 抖动、`NRestarts` 增长 | `journalctl -u X -n 80` | 崩溃循环。先回滚，再带着日志分析；不要靠 `restart` 反复试 |
| 上传成功但行为像旧版 | `sha256sum` 两侧比对 | 大概率推到了错目录（`~` 或被 shell 展开的路径） |
| rsync 报 `Text file busy` | 命令里是否有 `--inplace` | 去掉 `--inplace` |
| rsync 后远端配置丢了 | 命令里是否有 `--delete` | 从 `_unit-backup-*` 或人工备份恢复配置 |
| 发布物含 `.dll` | `Bot.props` 是否被 Import | 缺 `build/Bot.props` 会发布成框架依赖，服务器无 .NET 起不来 |

---

## 8. 交付报告格式

部署结束后按这个结构汇报，别写成叙事：

```
项目: Ticket
提交: <git rev-parse --short HEAD>（工作区是否干净: 是/否）
产物: Lunarium.Bot.Ticket  85.0 MiB  sha256=ab12…（本地=远端 ✓）
推送: rsync rc=0，传输 <n> 字节
重启: systemctl restart 耗时 <n>s，MainPID <old>→<new>，NRestarts=<n>
健康: active / 就绪日志已出现 / 自重启以来 0 条 error
回滚点: <部署目录>/Lunarium.Bot.Ticket.bak_<ts>
残留风险: <无 / 具体说明>
```

---

## 9. 非部署目标与已知遗留

| 项 | 状态 |
|---|---|
| `Lunarium.Bot.ReleaseBot` 目录（6.3 GB） | 源码已移出仓库，单元 `disabled`。**不是部署目标**；清理需先问人 |
| `lunariumbot-releasebot.service` | 停用。`ExecStart` 仍指向旧名 `LunariumBot.Net.ReleaseBot` |
| `Lunarium.Bot.ReleaseBotTest` 目录 | 历史测试目录，无单元 |
| 单元备份 | `/root/services/cyanflower/Lunarium.Bot/_unit-backup-<ts>/` |
| `SearchBot` | 仓库内仍无法编译（引用已删除的 Logger/Scheduler），**不可部署** |

---

## 10. 执行完自查

1. 是否只动了目标项目、目标目录？
2. `appsettings*`、`Logs/`、`storage/`、`.bundle/` 是否原样存在（mtime 不变）？
3. 单元文件是否未被修改（除显式获准）？
4. 回滚点是否创建、路径是否写进报告？
5. 三个服务的最终状态是否都还是 `active`（含**未部署**的那些）？
