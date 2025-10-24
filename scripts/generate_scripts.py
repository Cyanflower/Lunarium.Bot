#!/usr/bin/env python3
"""
LunariumBot.Net 项目脚本生成器
自动为每个子项目生成启动、部署和编译脚本
"""

import os
import sys
from pathlib import Path
from typing import List


# ==============================================================================
# 配置区域
# ==============================================================================

# 需要排除的目录（类库项目不需要生成脚本）
EXCLUDED_DIRS = {
    'Discord.Net',
    'LunariumBot.Net.Extensions',
    'LunariumBot.Net.Logger',
}

# 服务器配置
REMOTE_HOST = "LunariumBot.Net"
REMOTE_BASE_PATH = "/home/lunarium/LunariumBot.Net"


# ==============================================================================
# 模板定义
# ==============================================================================

def get_start_script_template(project_name: str, token_var_name: str) -> str:
    """生成启动脚本模板"""
    return f'''#!/bin/bash

# ==============================================================================
# {project_name} 启动与守护脚本
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
APP_DLL_PATH="./{project_name}"

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
# >     export {token_var_name}="你的开发用Bot-Token"
# >     ```
# >
# > 2.  **使用 `dotnet run` 启动项目**:
# >     直接从项目源代码启动，便于调试和快速迭代。
# >     ```bash
# >     # 在解决方案根目录运行
# >     dotnet run --project src/{project_name}
# >     # 在项目根目录运行
# >     dotnet run
# >     ```
# >
# > ---
# > 只有当你准备**正式部署**到服务器时，才应修改**服务器**上 `start.sh` 脚本中的 Token。

# Bot Token (部署时取消注释并填写)
# export {token_var_name}="TOKEN_AT_HERE"

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
'''


def get_deploy_script_template(project_name: str, remote_host: str, remote_path: str) -> str:
    """生成部署脚本模板"""
    return f'''#!/bin/bash

SOURCE_DIR="./dist/{project_name}/"
REMOTE_DEST_URL="{remote_host}:{remote_path}"
REMOTE_DEST_PATH="{remote_path}"

echo "正在连接远程服务器并确保目标目录存在..."
ssh {remote_host} "mkdir -p $REMOTE_DEST_PATH"

# ==========================================================
#  分步部署策略
# ==========================================================

# --- 步骤一：同步核心应用文件 ---
# 更新所有必要的应用文件，但排除启动脚本以保护现有配置
echo "[1/2] 正在同步核心应用文件..."
rsync -avz \\
  --include='{project_name}' \\
  --include='appsettings.template.jsonc' \\
  --exclude='start.sh' \\
  --exclude='*' \\
  "$SOURCE_DIR" "$REMOTE_DEST_URL"

# 检查同步结果
if [ $? -ne 0 ]; then
  echo "错误：核心文件同步失败，请检查错误信息"
  exit 1
fi

# --- 步骤二：处理启动脚本 ---
# 使用 --ignore-existing 选项，仅在远程不存在时上传 start.sh
# 这样可以避免覆盖服务器上已经定制的启动脚本
echo "[2/2] 正在检查并上传启动脚本（仅在不存在时）..."
rsync -avz --ignore-existing \\
  "${{SOURCE_DIR}}start.sh" \\
  "$REMOTE_DEST_URL"
  
# ==========================================================
#  部署完成
# ==========================================================

# 检查最终结果
if [ $? -eq 0 ]; then
  echo "✓ 部署完成，服务器配置已妥善保护"
else
  echo "错误：启动脚本上传失败，请检查错误信息"
  exit 1
fi
'''


def get_build_script_template(project_name: str) -> str:
    """生成编译脚本模板"""
    return f'''#!/bin/bash

# ==============================================================================
# {project_name} 编译脚本
# ==============================================================================

echo "正在编译 {project_name}..."

dotnet publish -c Release

if [ $? -eq 0 ]; then
  echo "✓ 编译完成"
else
  echo "✗ 编译失败"
  exit 1
fi
'''


# ==============================================================================
# 核心逻辑
# ==============================================================================

def extract_token_var_name(project_name: str) -> str:
    """
    从项目名提取 Token 环境变量名
    例如: LunariumBot.Net.Archive -> ARCHIVE_BOT_TOKEN
    """
    # 移除前缀 "LunariumBot.Net."
    suffix = project_name.replace("LunariumBot.Net.", "")
    # 转换为大写并添加后缀
    return f"{suffix.upper()}_BOT_TOKEN"


def is_valid_project(dir_path: Path) -> bool:
    """
    判断是否是有效的项目目录
    需要满足：
    1. 是目录
    2. 以 LunariumBot.Net. 开头
    3. 不在排除列表中
    4. 包含 .csproj 文件
    """
    if not dir_path.is_dir():
        return False
    
    dir_name = dir_path.name
    
    if not dir_name.startswith("LunariumBot.Net."):
        return False
    
    if dir_name in EXCLUDED_DIRS:
        return False
    
    # 检查是否包含 .csproj 文件
    csproj_files = list(dir_path.glob("*.csproj"))
    if not csproj_files:
        return False
    
    return True


def generate_scripts_for_project(project_dir: Path) -> None:
    """为指定项目生成所有脚本"""
    project_name = project_dir.name
    token_var_name = extract_token_var_name(project_name)
    remote_path = f"{REMOTE_BASE_PATH}/{project_name}"
    
    print(f"\n正在为项目生成脚本: {project_name}")
    print(f"  Token 变量名: {token_var_name}")
    print(f"  远程路径: {remote_path}")
    
    # 生成启动脚本
    start_script = get_start_script_template(project_name, token_var_name)
    start_script_path = project_dir / "start.sh"
    start_script_path.write_text(start_script, encoding='utf-8')
    os.chmod(start_script_path, 0o755)  # 添加执行权限
    print(f"  ✓ 生成: start.sh")
    
    # 生成部署脚本
    deploy_script = get_deploy_script_template(project_name, REMOTE_HOST, remote_path)
    deploy_script_path = project_dir / "deploy.sh"
    deploy_script_path.write_text(deploy_script, encoding='utf-8')
    os.chmod(deploy_script_path, 0o755)
    print(f"  ✓ 生成: deploy.sh")
    
    # 生成编译脚本
    build_script = get_build_script_template(project_name)
    build_script_path = project_dir / "build.sh"
    build_script_path.write_text(build_script, encoding='utf-8')
    os.chmod(build_script_path, 0o755)
    print(f"  ✓ 生成: build.sh")


def main():
    """主函数"""
    # 获取脚本所在目录的父目录（假设脚本放在项目根目录）
    script_dir = Path(__file__).parent.parent
    src_dir = script_dir / "src"
    
    if not src_dir.exists():
        print(f"错误：找不到 src 目录: {src_dir}")
        print("请确保此脚本位于项目根目录")
        sys.exit(1)
    
    print("=" * 60)
    print("LunariumBot.Net 项目脚本生成器")
    print("=" * 60)
    
    # 查找所有有效的项目目录
    projects: List[Path] = []
    for item in src_dir.iterdir():
        if is_valid_project(item):
            projects.append(item)
    
    if not projects:
        print("未找到任何有效的项目目录")
        sys.exit(1)
    
    print(f"\n找到 {len(projects)} 个项目:")
    for project in projects:
        print(f"  - {project.name}")
    
    # 为每个项目生成脚本
    for project in projects:
        generate_scripts_for_project(project)
    
    print("\n" + "=" * 60)
    print("✓ 所有脚本生成完成！")
    print("=" * 60)
    print("\n提示:")
    print("  1. 生成的脚本已自动添加执行权限")
    print("  2. 如需修改配置，请编辑本脚本顶部的配置区域")


if __name__ == "__main__":
    main()
