# LunariumBot.Net 项目结构文档

## 目录

1. [项目概述](#项目概述)
2. [目录结构](#目录结构)
3. [项目架构与演进](#项目架构与演进)
4. [项目详解](#项目详解)
5. [当前开发焦点](#当前开发焦点)

---

## 项目概述

**LunariumBot.Net** 是一个基于 .NET 9.0 的 Discord Bot 解决方案，核心功能包括论坛帖子搜索、服务器数据同步以及活跃帖自动归档等。项目采用模块化设计，包含多个独立项目，并持续迭代演进架构。

### 技术栈
- **.NET 9.0** SDK
- **Discord.Net** (自维护修改版)
- **PostgreSQL** + **SQLite** 数据库
- **Dapper** ORM
- **FluentScheduler** / **Coravel** 定时任务
- **JiebaNet** 中文分词

---

## 目录结构

```bash
> tree -I 'dist|bin|obj|.vs|.vscode|packages|node_modules|Debug|Release|*.suo|*.user|*.cache|TestResults|.git|.idea|Backup*|UpgradeLog*.htm|_UpgradeReport_Files'
.
├── Database/                       # PostgreSQL Docker 配置
│   └── docker-compose.yml
├── Discord.Net/                    # 自维护的 Discord.Net 库源码 (无需修改)
├── my-postgres.conf                # PostgreSQL 配置文件
├── LunariumBot.Net.sln             # .NET 解决方案文件
├── README.md
├── FileOrProjdir-Doc.md            # 本文档
│
├── src/                            # 主要项目目录
│   ├── LunariumBot.Net.Extensions/         # 基础扩展库
│   ├── LunariumBot.Net.Logger/             # 自实现结构化日志库
│   ├── LunariumBot.Net.SearchBot/          # 第一代: 论坛搜索 Bot
│   ├── LunariumBot.NET.DataCenter/         # 第二代: 数据同步中心 (重构中)
│   ├── LunariumBot.Net.Archive/            # 第三代: 活跃帖归档 Bot (已部署)
│   └── LunariumBot.Net.Examples/           # 第三代: 标准项目模板
│
└── tests/                          # 单元测试
    └── LunariumBot.Net.Logger.Tests/
```

### 特殊目录说明

| 目录/文件 | 用途 | 注意事项 |
|-----------|------|----------|
| `bin/`, `obj/`, `dist/` | SDK 自动生成文件 | **应忽略** (已添加 .gitignore) |
| `src/*/Logs/` | 本地运行日志 | 仅开发时产生，可查看调试信息 |
| `Discord.Net/` | 自维护的 Discord.Net 库 | **无需修改**，作为项目引用存在 |
| `Database/` | PostgreSQL Docker 配置 | 包含 `docker-compose.yml` |

---

## 项目架构与演进

### 架构代际关系图

```
基础设施层
├── LunariumBot.Net.Extensions     (通用扩展方法)
└── LunariumBot.Net.Logger         (结构化日志)

           ↓

第一代
└── LunariumBot.Net.SearchBot      (SQLite + 旧架构)
           │
           ↓ 拆分 + 升级数据库
第二代
└── LunariumBot.NET.DataCenter     (PostgreSQL + 重构中)
           │
           ↓ 架构优化 + 最佳实践
第三代
├── LunariumBot.Net.Archive        (已完成部署)
└── LunariumBot.Net.Examples       (新项目标准模板)
```

### 架构演进总结

| 代际 | 特点 | 数据库 | 架构决策 |
|------|------|--------|----------|
| **第一代** | 单体应用，功能完整 | SQLite | gRPC 通信，紧耦合 |
| **第二代** | 数据层分离 | PostgreSQL | **重构中**，移除 gRPC |
| **第三代** | 模块化，依赖注入 | 根据需求选择 | 双层 DI，配置分离，托管服务模式 |

---

## 项目详解

### 1. 基础设施项目

#### **LunariumBot.Net.Extensions**
- **用途**: 提供通用的 C# 扩展方法
- **示例**: 字符串切片、集合操作等
- **依赖关系**: 被所有业务项目引用

#### **LunariumBot.Net.Logger**
- **用途**: 自实现的结构化日志库
- **功能特性**:
  - 多目标输出 (控制台、文件、Discord 频道)
  - ANSI 颜色支持
  - 上下文化日志 (`ForContext`)
  - 与 Microsoft.Extensions.Logging 桥接
- **测试覆盖**: 有完整单元测试 (`tests/LunariumBot.Net.Logger.Tests`)

---

### 2. 第一代项目 (遗留代码)

#### **LunariumBot.Net.SearchBot**
- **核心功能**: 为 Discord 服务器提供论坛帖子搜索功能
- **数据库**: SQLite (`Data/SearchBot.db`)
- **当前状态**:
  - ✅ 核心功能完整且可用
  - ⚠️ **重构进度 0%**，需要升级到 PostgreSQL
  - ⚠️ 架构陈旧 (使用 gRPC，已弃用)

**主要模块**:
```
AppInteractionService/
├── SlashCommands/          # 斜杠命令
├── InteractionUtils/       # 交互工具类
└── InteractionsCallback/   # 按钮/选择器回调

Database/CRUD/              # 数据库操作 (SQLite)
ThreadFetcher/              # 帖子数据抓取
Scheduler/                  # 定时任务 (分析、全量同步)
Utils/                      # 分词、数据分析、日期解析
Resources/                  # JiebaNet 分词资源文件
```

**重构计划**:
- [ ] 迁移数据库到 PostgreSQL
- [ ] 移除 gRPC 相关代码 (`Protos/monitoring.proto`)
- [ ] 采用第三代架构模式 (参考 `Examples` 项目)

---

### 3. 第二代项目 (重构中)

#### **LunariumBot.NET.DataCenter**
- **核心功能**: 监听和扫描 Discord 服务器数据，同步到 PostgreSQL
- **数据库**: PostgreSQL (Npgsql 3.1.6)
- **当前状态**: 正在基于第一代和第三代经验进行重构

**主要模块**:
```
DataFetcher/                # 数据采集
├── Listener.cs             # 实时监听
├── Scanner.cs              # 全量扫描
└── ThreadProcessor.cs      # 帖子处理

DataProcess/                # 数据处理管道
├── ChannelProcessor.cs
├── MessageProcess.cs
└── ThreadProcessor.cs

Database/CRUD/              # PostgreSQL CRUD 操作
DBMigrations/               # 数据库迁移脚本
├── v1.initial_schema.pgsql

Scheduler/Job/              # 定时任务
├── AnalysisJob.cs
└── FullSyncJob.cs
```

**关键依赖** (来自 `.csproj`):
- `Npgsql` 3.1.6 (PostgreSQL 驱动)
- `Dapper` 2.1.66 (轻量级 ORM)
- `Coravel` 6.0.2 (任务调度)
- `Polly` 8.6.2 (重试策略)
- `JiebaNet.Segmenter` 1.0.6 (中文分词)

---

### 4. 第三代项目 (最佳实践)

#### **LunariumBot.Net.Archive** (已完成)
- **核心功能**: 自动管理 Discord 服务器的活跃帖归档
  - 监控活跃帖数量，避免达到 1000 上限
  - 比 Discord 原生功能更频繁的归档策略
- **架构特点**:
  - ✅ 使用 .NET Generic Host
  - ✅ `IHostedService` 托管服务模式
  - ✅ 双层依赖注入 (主机 DI + 私有 DI)
  - ✅ 配置与代码分离 (环境变量 + JSONC)
- **当前状态**: **已正式部署运行**，架构稳定

**关键配置** (来自 `.csproj`):
```xml
<PublishSingleFile>true</PublishSingleFile>     <!-- 单文件发布 -->
<PublishDir>$(SolutionDir)dist\$(MSBuildProjectName)\</PublishDir>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
```

**项目结构**:
```
AppInteractionService/
├── AdminCommands.cs            # 管理员命令
└── PriorityArchiveCommands.cs  # 归档相关命令

Archiver/
├── Archiver.cs                 # 归档核心逻辑
└── PriorityArchiveDataLoader.cs

Model/
├── ConfigModel.cs              # 配置数据模型
└── PriorityArchiveDataModel.cs

start.sh, update.sh             # 部署脚本
```

---

#### **LunariumBot.Net.Examples** (标准模板)
- **用途**: 基于 Archive 项目的架构经验抽离的**通用项目模板**
- **目标**: 作为未来所有新 Bot 项目的起点
- **文档**: 包含详尽的 `README.md` 开发指南

**核心设计理念** (来自 README):
1. **.NET Generic Host**: 统一管理生命周期、配置和 DI
2. **`IHostedService` 模式**: Bot 作为托管服务运行
3. **双层依赖注入**:
   - **主机 DI 容器** (`Program.cs`): 全局服务 (日志、代理)
   - **私有 DI 容器** (`ExamplesBot.cs`): Bot 专属服务 (Discord 客户端、配置)
4. **配置与代码分离**:
   - 敏感数据 (Token) → 环境变量
   - 业务配置 → `appsettings.jsonc`
5. **专用日志库**: 集成 `LunariumBot.Net.Logger`

**项目结构**:
```
Program.cs                      # 应用入口 + 主机 DI
ExamplesBot.cs                  # Bot 核心服务 + 私有 DI
ConfigLoader.cs                 # 配置管理器 (线程安全)

AppInteractionService/          # 命令模块
└── AuthUtils.cs                # 权限验证工具

Model/
├── ConfigModel.cs
└── ProxyConfig.cs

Utils/
└── GeneralEmbedBuilder.cs      # Discord Embed 构建工具

appsettings.template.jsonc      # 配置模板
start.sh                        # 生产部署脚本
```

**快速开始** (来自 README):
```bash
# 1. 配置环境变量 (开发环境)
export EXAMPLES_BOT_TOKEN="你的Bot-Token"

# 2. 运行项目
dotnet run --project src/LunariumBot.Net.Examples

# 3. 发布到生产环境
dotnet publish src/LunariumBot.Net.Examples -c Release -o ./dist/ExamplesBot
cd ./dist/ExamplesBot
./start.sh
```

---

## 当前开发焦点

### 主要任务
**继续开发 LunariumBot.NET.DataCenter**
- 基于 SearchBot 的业务逻辑
- 引入 Archive 的架构模式
- 完成 PostgreSQL 迁移
- 移除 gRPC 依赖

### 重构优先级
1. **高优先级**: DataCenter 项目重构
2. **中优先级**: SearchBot 数据库迁移
3. **低优先级**: 功能增强和性能优化

---

## 附录

### 相关文档
- [Examples 项目开发指南](src/LunariumBot.Net.Examples/README.md)
- [Logger 库使用说明](src/LunariumBot.Net.Examples/LunariumLogLib_README.md)
- [Archive 管理员命令](src/LunariumBot.Net.Archive/AdminCommand.README.md)

### 环境依赖
- .NET 9.0 SDK
- Docker (用于 PostgreSQL)
- Discord Bot Token (通过环境变量设置)

### 特别提示
- ⚠️ **禁止将 Bot Token 硬编码在代码或 `start.sh` 中**
- ⚠️ **开发环境使用会话级环境变量设置 Token**
- ⚠️ **所有临时文件 (`bin/`, `obj/`, `Logs/`) 已添加到 `.gitignore`**
