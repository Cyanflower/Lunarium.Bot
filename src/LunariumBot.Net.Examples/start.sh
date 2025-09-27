#!/bin/bash

# ==============================================================================
# LunariumBot.Net.Examples 启动与守护脚本
# 功能:
# 1. 设置所需的环境变量
# 2. 循环启动应用程序
# 3. 监控程序退出码，如果非0 (异常退出)，则自动重启
# 4. 限制最大重启次数，防止无限循环
# ==============================================================================

# --- 请在这里修改你的配置 ---

# 最大重启次数 (0代表不重启，5代表最多重启5次)
MAX_RESTARTS=5

# 你的应用程序发布后的DLL文件路径
# 请确保你已经使用 dotnet publish 命令发布了你的项目
# 例如: dotnet publish -c Release -o ./publish
APP_DLL_PATH="./LunariumBot.Net.Examples"

# --- 设置环境变量 ---

# BOT TOKEN
export EXAMPLES_BOT_TOKEN="TOKEN_AT_HERE"

# 日志级别 (可选, 可选值: Debug, Info, Warning, Error, Critical)
# 保持默认值，或者根据需要修改
export FILE_LOG_LEVEL="Info"
export CONSOLE_LOG_LEVEL="Info"

# HTTP 代理 (如果不需要，请将此行注释掉或留空)
# 格式: http://[user:password@]host:port
# export http_proxy="http://127.0.0.1:7890"

# --- 脚本主逻辑 ---

# 初始化重启计数器
restart_count=0

# 主循环，只要重启次数小于最大限制就一直执行
while [ $restart_count -lt $MAX_RESTARTS ]
do
    echo "================================================="
    echo "🚀 准备启动程序... 这是第 $((restart_count + 1)) 次尝试。"
    echo "================================================="

    # 启动 .NET 应用程序
    "$APP_DLL_PATH"
    
    # 获取刚刚退出程序的退出码
    # 在Linux/macOS中, $? 会保存上一个命令的退出码
    # 0 代表正常退出, 非0 代表异常退出
    exit_code=$?

    # 判断退出码
    if [ $exit_code -eq 0 ]; then
        # 如果退出码是 0，说明是正常退出（比如 Ctrl+C）
        echo "✅ 程序正常退出 (退出码: 0)，脚本将停止。"
        break # 跳出循环，结束脚本
    else
        # 如果退出码非 0，说明程序崩溃了
        echo "😥 程序异常退出 (退出码: $exit_code)。"
        
        # 增加重启计数
        restart_count=$((restart_count + 1))
        
        # 检查是否已达到最大重启次数
        if [ $restart_count -ge $MAX_RESTARTS ]; then
            echo "💥 已达到最大重启次数 ($MAX_RESTARTS 次)，程序未能成功启动。脚本终止。"
            break # 达到最大次数，跳出循环
        else
            remaining_attempts=$((MAX_RESTARTS - restart_count))
            echo "⏳ 5秒后将尝试重启... 剩余尝试次数: $remaining_attempts"
            sleep 5 # 等待5秒再重启，避免因秒崩导致CPU占用过高
        fi
    fi
done

echo "👋 脚本执行完毕。"