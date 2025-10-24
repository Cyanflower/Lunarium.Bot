#!/bin/bash

# ============================================================================
# Git Hook 一键安装脚本
# ============================================================================
# 
# 用途: 自动安装 pre-commit hook 以保护 Token 安全
# 使用: ./install_git_hook.sh
# ============================================================================

echo "Git Hook 安装程序"
echo "==============================================="
echo ""

# 检查是否在 Git 仓库中
if [ ! -d "../.git" ]; then
    echo "❌ 错误: 当前目录不是 Git 仓库根目录"
    echo "请在项目根目录运行此脚本"
    exit 1
fi

# 检查 pre-commit.sample 文件是否存在
if [ ! -f "pre-commit.sample" ]; then
    echo "❌ 错误: 找不到 pre-commit.sample 文件"
    echo "请确保 pre-commit.sample 文件与此脚本在同一目录"
    exit 1
fi

# 目标路径
HOOK_PATH="../.git/hooks/pre-commit"

# 检查是否已存在 hook
if [ -f "$HOOK_PATH" ]; then
    echo "⚠️  警告: pre-commit hook 已存在"
    echo ""
    read -p "是否要覆盖现有的 hook？(y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "已取消安装"
        exit 0
    fi
    echo ""
fi

# 复制并设置权限
echo "正在安装 pre-commit hook..."
cp pre-commit.sample "$HOOK_PATH"
chmod +x "$HOOK_PATH"

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Git hook 安装成功！"
    echo ""
    echo "现在当你提交包含未注释 Token 的 start.sh 时，"
    echo "Git 会自动阻止提交并提示你修正。"
    echo ""
    echo "如需临时跳过检查（不推荐），使用："
    echo "  git commit --no-verify"
else
    echo ""
    echo "❌ 安装失败"
    exit 1
fi
