# 脚本生成器使用说明

## 功能概述

`generate_scripts.py` 可以自动为 Lunarium.Bot 项目中的每个子项目生成三个脚本：

1. **start.sh** - 启动与守护脚本
2. **deploy.sh** - 部署脚本
3. **build.sh** - 编译脚本

## 使用方法

### 1. 将脚本放置在项目根目录

```
Lunarium.Bot/
├── generate_scripts.py  <-- 放在这里
├── src/
│   ├── Lunarium.Bot.Archive/
│   ├── Lunarium.Bot.DataCenter/
│   └── ...
└── tests/
```

### 2. 运行脚本

```bash
# 方式一：直接运行
python3 generate_scripts.py

# 方式二：添加执行权限后运行
chmod +x generate_scripts.py
./generate_scripts.py
```

### 3. 查看生成结果

脚本会在每个项目目录下生成三个文件：

```
src/Lunarium.Bot.Archive/
├── start.sh      # 启动脚本
├── deploy.sh     # 部署脚本
├── build.sh      # 编译脚本
└── ...
```

## 配置说明

### 修改排除列表

如果需要排除更多项目（比如类库），编辑脚本顶部的 `EXCLUDED_DIRS`：

```python
EXCLUDED_DIRS = {
    'Discord.Net',
    'Lunarium.Bot.Extensions',
    'Lunarium.Bot.Logger',
    # 添加更多需要排除的项目
}
```

### 修改服务器配置

编辑脚本顶部的服务器配置：

```python
REMOTE_HOST = "BotCenterVPS"  # SSH 配置名称
REMOTE_BASE_PATH = "~/services/cyanflower/Lunarium.Bot"  # 服务器基础路径
```

## 命名规则

脚本会自动识别以下规则：

| 项目名 | Token 环境变量 | 服务器路径 |
|--------|---------------|-----------|
| Lunarium.Bot.Archive | ARCHIVE_BOT_TOKEN | ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive |
| Lunarium.Bot.DataCenter | DATACENTER_BOT_TOKEN | ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.DataCenter |
| Lunarium.Bot.SearchBot | SEARCHBOT_BOT_TOKEN | ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.SearchBot |

## 生成的脚本说明

### start.sh

- 自动重启机制（最多 5 次）
- 环境变量配置
- 包含详细的安全提示

### deploy.sh

- 两步部署策略
- 保护服务器上现有的 `start.sh`（使用 `--ignore-existing`）
- 同步核心应用文件

### build.sh

- 简单的 `dotnet publish -c Release` 命令
- 包含错误检查

## 工作流示例

### 开发阶段

```bash
# 1. 设置临时环境变量
export ARCHIVE_BOT_TOKEN="开发用Token"

# 2. 直接运行项目
dotnet run --project src/Lunarium.Bot.Archive
```

### 部署阶段

```bash
# 1. 生成脚本（首次）
python3 generate_scripts.py

# 2. 编译项目
cd src/Lunarium.Bot.Archive
./build.sh

# 3. 部署到服务器
./deploy.sh

# 4. 在服务器上修改 start.sh 中的 Token
# ssh 到服务器，编辑 ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive/start.sh
# 取消注释并填写真实 Token

# 5. 在服务器上启动
ssh Lunarium.Bot
cd ~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.Archive
./start.sh
```

## 注意事项

1. **Token 安全**：绝对不要在本地项目的 `start.sh` 中填写真实 Token
2. **Git 忽略**：确保 `.gitignore` 包含服务器上修改过的 `start.sh`
3. **执行权限**：生成的脚本会自动添加执行权限（755）
4. **项目识别**：只有包含 `.csproj` 文件的目录才会被识别为项目

## 故障排除

### 问题：找不到 src 目录

**解决方案**：确保脚本位于项目根目录，与 `src` 文件夹同级

### 问题：某些项目没有生成脚本

**解决方案**：
1. 检查项目是否在 `EXCLUDED_DIRS` 中
2. 确认项目目录包含 `.csproj` 文件
3. 确认项目名以 `Lunarium.Bot.` 开头

### 问题：生成的脚本无法执行

**解决方案**：手动添加执行权限

```bash
chmod +x start.sh deploy.sh build.sh
```

## 扩展性

如果需要为脚本添加更多功能，可以修改模板函数：

- `get_start_script_template()` - 修改启动脚本
- `get_deploy_script_template()` - 修改部署脚本
- `get_build_script_template()` - 修改编译脚本

## 为什么选择 Python？

1. ✓ 跨平台兼容性好（Linux/macOS/Windows）
2. ✓ 大多数系统预装，无需额外安装
3. ✓ 文件操作简洁直观
4. ✓ 标准库功能强大
5. ✓ 易于维护和扩展
