# Lunarium.Bot 自动化脚本工具套件

完整的项目管理和部署自动化解决方案。

## 📦 包含的文件

```
.
├── generate_scripts.py         # 脚本生成器（核心工具）
├── pre-commit.sample            # Git hook 示例
├── install_git_hook.sh          # Git hook 安装器
├── README_SCRIPT_GENERATOR.md   # 详细使用文档
└── GITIGNORE_ADVICE.md          # .gitignore 配置建议
```

## 🚀 快速开始

### 步骤 1: 生成项目脚本

将 `generate_scripts.py` 放在项目根目录并运行：

```bash
python3 generate_scripts.py
```

这会为每个子项目生成：
- ✅ `start.sh` - 启动与守护脚本
- ✅ `deploy.sh` - 部署脚本  
- ✅ `build.sh` - 编译脚本

### 步骤 2: 安装 Git Hook（可选但推荐）

保护你的 Token 不被意外提交：

```bash
chmod +x install_git_hook.sh
./install_git_hook.sh
```

### 步骤 3: 使用生成的脚本

```bash
# 编译项目
cd src/Lunarium.Bot.Archive
./build.sh

# 部署到服务器
./deploy.sh

# 在服务器上启动（需要先 SSH 到服务器并配置 Token）
ssh Lunarium.Bot
cd ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive
# 编辑 start.sh，取消注释并填写 Token
./start.sh
```

## 🎯 核心特性

### 1. 智能项目识别

自动识别符合以下条件的项目：
- ✅ 目录名以 `Lunarium.Bot.` 开头
- ✅ 包含 `.csproj` 文件
- ✅ 不在排除列表中（类库项目会被自动排除）

### 2. 自动命名规则

| 项目名 | Token 变量 | 服务器路径 |
|--------|-----------|-----------|
| Lunarium.Bot.Archive | `ARCHIVE_BOT_TOKEN` | `~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive` |
| Lunarium.Bot.DataCenter | `DATACENTER_BOT_TOKEN` | `~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.DataCenter` |

### 3. 安全部署策略

部署脚本采用两步策略：
1. **同步核心文件** - 更新应用程序和配置模板
2. **保护启动脚本** - 使用 `--ignore-existing` 避免覆盖服务器上已配置的 Token

### 4. Token 安全保护

- ✅ 生成的 `start.sh` 默认 Token 为注释状态
- ✅ Git hook 自动检查，防止意外提交真实 Token
- ✅ 详细的安全提示和最佳实践

## 📖 工作流示例

### 开发环境

```bash
# 使用临时环境变量（关闭终端后失效，安全！）
export ARCHIVE_BOT_TOKEN="你的开发Token"

# 直接运行项目
dotnet run --project src/Lunarium.Bot.Archive
```

### 生产部署

```bash
# 1. 本地编译
cd src/Lunarium.Bot.Archive
./build.sh

# 2. 部署到服务器
./deploy.sh

# 3. SSH 到服务器配置 Token
ssh Lunarium.Bot
cd ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive
nano start.sh  # 取消注释并填写真实 Token

# 4. 启动应用
./start.sh
```

## ⚙️ 配置说明

### 修改排除列表

编辑 `generate_scripts.py` 中的 `EXCLUDED_DIRS`：

```python
EXCLUDED_DIRS = {
    'Discord.Net',
    'Lunarium.Bot.Extensions',
    'Lunarium.Bot.Logger',
    # 添加更多要排除的项目
}
```

### 修改服务器配置

编辑 `generate_scripts.py` 中的服务器配置：

```python
REMOTE_HOST = "BotCenterVPS"  # SSH 配置名
REMOTE_BASE_PATH = "~/services/cyanflower/Lunarium.Bot"
```

## 🔒 安全最佳实践

### ✅ 应该做的

1. **开发时使用临时环境变量**
   ```bash
   export ARCHIVE_BOT_TOKEN="开发Token"
   ```

2. **在服务器上配置真实 Token**
   - 服务器上的 `start.sh` 不会被同步回本地
   - 每次部署都会跳过已存在的 `start.sh`

3. **安装 Git hook**
   ```bash
   ./install_git_hook.sh
   ```

### ❌ 不应该做的

1. ❌ 在本地 `start.sh` 中填写真实 Token
2. ❌ 将包含 Token 的 `start.sh` 提交到 Git
3. ❌ 在代码中硬编码 Token

## 📁 生成的脚本说明

### start.sh 特性

- 🔄 自动重启机制（最多 5 次）
- 📝 详细的日志配置
- 🌐 HTTP 代理支持
- ⚠️ 完整的安全提示

### deploy.sh 特性

- 🛡️ 保护服务器配置（不覆盖现有 `start.sh`）
- 📦 增量同步（只传输变化的文件）
- ✅ 完善的错误检查

### build.sh 特性

- 🏗️ 标准的 Release 编译
- ✅ 简单的错误处理

## 🆚 为什么选择 Python？

| 特性 | Python | JavaScript (Node.js) |
|------|--------|---------------------|
| 跨平台 | ✅ | ✅ |
| 预装率 | ✅ 大多数系统 | ❌ 需要安装 Node.js |
| 文件操作 | ✅ 简洁直观 | ⚠️ 相对繁琐 |
| 服务器环境 | ✅ 几乎总是可用 | ⚠️ 需要确认 |
| 学习曲线 | ✅ 平缓 | ⚠️ 需要了解 Node.js |

**结论**: Python 更适合服务器端部署脚本。

## 🐛 故障排除

### 问题：找不到 src 目录

**原因**: 脚本不在项目根目录

**解决方案**: 
```bash
# 确保目录结构如下
Lunarium.Bot/
├── generate_scripts.py  <-- 在这里
├── src/
└── tests/
```

### 问题：某些项目没有生成脚本

**可能原因**:
1. 项目在排除列表中
2. 项目目录不包含 `.csproj` 文件
3. 项目名不符合命名规范

**解决方案**: 检查项目是否满足条件

### 问题：Git hook 没有生效

**解决方案**:
```bash
# 检查 hook 是否安装
ls -la .git/hooks/pre-commit

# 检查是否有执行权限
chmod +x .git/hooks/pre-commit

# 测试 hook
git commit --dry-run
```

### 问题：部署时覆盖了服务器配置

**原因**: 可能直接复制文件而不是使用 `deploy.sh`

**解决方案**: 始终使用生成的 `deploy.sh` 脚本部署

## 📚 延伸阅读

- [详细使用文档](README_SCRIPT_GENERATOR.md)
- [.gitignore 配置建议](GITIGNORE_ADVICE.md)

## 🤝 贡献

如果你想改进这些工具：

1. 修改 `generate_scripts.py` 中的模板函数
2. 重新生成脚本测试
3. 提交你的改进

## 📄 许可

这些工具脚本与 Lunarium.Bot 项目使用相同的许可。

---

**提示**: 第一次使用时，建议先阅读 [README_SCRIPT_GENERATOR.md](README_SCRIPT_GENERATOR.md) 了解详细信息。
