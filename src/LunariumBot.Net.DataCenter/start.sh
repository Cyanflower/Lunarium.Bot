#!/bin/bash

# ==============================================================================
# LunariumBot.Net.DataCenter 启动与守护脚本
# 功能:
# 1. 设置所需的环境变量
# 2. 循环启动应用程序
# 3. 监控程序退出码，如果非0 (异常退出)，则自动重启
# 4. 限制最大重启次数，防止无限循环
# ==============================================================================

# --- 配置区域 ---

# 最大重启次数 (0代表不重启，5代表最多重启5次)
MAX_RESTARTS=5

# 应用程序可执行文件路径
APP_DLL_PATH="./LunariumBot.Net.DataCenter"

# --- 环境变量设置 ---

# > **⚠️ 重要安全提示：请严格区分开发与部署环境！**
# >
# > `start.sh` 脚本是为了在**生产服务器上部署和运行**而设计的。请**绝对不要**在日常开发中将真实 Bot Token 写入此文件。
# > 这样做极易导致 Token 意外提交到 Git 等版本控制系统，从而造成永久性泄露。
# >
# > #### **正确的开发工作流**
# >
# > 1.  **使用会话级环境变量设置 Token**:
# >     在你的开发终端中，运行以下命令来设置一个**临时的**环境变量。它只在当前终端会话中有效，关闭终端后即失效，非常安全。
# >     ```bash
# >     export DATACENTER_BOT_TOKEN="TOKEN_AT_HERE"
# >     ```
# >
# > 2.  **使用 `dotnet run` 启动项目**:
# >     直接从项目源代码启动，便于调试和快速迭代。
# >     ```bash
# >     # 在解决方案根目录运行
# >     dotnet run --project src/LunariumBot.Net.DataCenter
# >     # 在项目根目录运行
# >     dotnet run
# >     ```
# >
# > ---
# > 只有当你准备**正式部署**到服务器时，才应修改**服务器**上 `start.sh` 脚本中的 Token。

# Bot Token (部署时取消注释并填写)
# export DATACENTER_BOT_TOKEN="TOKEN_AT_HERE"

# 日志级别 (可选值: Debug, Info, Warning, Error, Critical)
export FILE_LOG_LEVEL="Info"
export CONSOLE_LOG_LEVEL="Info"

# HTTP 代理 (如不需要请注释掉或留空)
# 格式: http://host:port
# export http_proxy="http://127.0.0.1:7890"

# --- 脚本主逻辑 ---

# 初始化重启计数器
restart_count=0

# 主循环
while [ $restart_count -lt $MAX_RESTARTS ]
do
    echo "================================================="
    echo "正在启动程序... 这是第 $((restart_count + 1)) 次尝试"
    echo "================================================="

    # 启动 .NET 应用程序
    "$APP_DLL_PATH"
    
    # 获取退出码
    exit_code=$?

    # 判断退出码
    if [ $exit_code -eq 0 ]; then
        echo "✓ 程序正常退出 (退出码: 0)，脚本将停止"
        break
    else
        echo "✗ 程序异常退出 (退出码: $exit_code)"
        
        # 增加重启计数
        restart_count=$((restart_count + 1))
        
        # 检查是否已达到最大重启次数
        if [ $restart_count -ge $MAX_RESTARTS ]; then
            echo "✗ 已达到最大重启次数 ($MAX_RESTARTS 次)，脚本终止"
            break
        else
            remaining_attempts=$((MAX_RESTARTS - restart_count))
            echo "等待 5 秒后重启... 剩余尝试次数: $remaining_attempts"
            sleep 5
        fi
    fi
done

echo "脚本执行完毕"
