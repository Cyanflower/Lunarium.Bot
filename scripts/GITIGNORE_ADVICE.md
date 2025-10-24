# LunariumBot.Net .gitignore 配置建议

## 基础 .NET 忽略规则
bin/
obj/
*.user
*.suo
.vs/

## 发布目录
dist/
publish/

## 脚本相关（重要！）
# 
# 策略说明：
# - start.sh: 每个项目目录下的 start.sh 应该纳入版本控制，但保持模板状态（Token 为注释）
# - deploy.sh 和 build.sh: 可以纳入版本控制
# 
# 但是，如果开发者意外修改了本地的 start.sh 并填入了 Token，
# 我们需要一个机制来防止意外提交。

# 方案一：完全不追踪 start.sh（推荐用于多人协作）
# 优点：最安全，绝对不会提交 Token
# 缺点：首次克隆需要运行 generate_scripts.py
# **/start.sh

# 方案二：追踪模板，但在 pre-commit hook 中检查
# 优点：新克隆的项目可以直接使用
# 缺点：需要配置 git hook
# 不添加 .gitignore 规则，但需要配置 hook（见下文）

## 临时文件
*.tmp
*.log
*.bak

## 环境配置（如果使用 .env 文件）
.env
.env.local
.env.production

## IDE 配置
.idea/
.vscode/
*.swp
*.swo

## macOS
.DS_Store

## Windows
Thumbs.db
desktop.ini

# ============================================================
# Git Hook 配置（推荐）
# ============================================================

# 如果选择追踪 start.sh，强烈建议配置 pre-commit hook
# 创建 .git/hooks/pre-commit 文件，内容如下：

"""
#!/bin/bash

# 检查所有待提交的 start.sh 文件
# 确保 Token 行仍然是注释状态

echo "正在检查 start.sh 文件..."

# 查找所有待提交的 start.sh
files=$(git diff --cached --name-only | grep 'start.sh$')

if [ -z "$files" ]; then
    exit 0
fi

# 检查每个文件
for file in $files; do
    # 检查是否有未注释的 export XXX_BOT_TOKEN 行
    if grep -P '^export \w+_BOT_TOKEN=' "$file" > /dev/null; then
        echo "❌ 错误: $file 包含未注释的 Token！"
        echo ""
        echo "请确保 Token 行保持注释状态："
        echo "  # export ARCHIVE_BOT_TOKEN=\"TOKEN_AT_HERE\""
        echo ""
        echo "如果这是有意为之（不推荐），请使用："
        echo "  git commit --no-verify"
        exit 1
    fi
done

echo "✓ start.sh 文件检查通过"
exit 0
"""

# 安装 hook 的命令：
# chmod +x .git/hooks/pre-commit

# ============================================================
# 推荐的完整工作流
# ============================================================

# 1. 首次设置项目
"""
git clone <repository>
cd LunariumBot.Net
python3 generate_scripts.py  # 生成所有脚本
"""

# 2. 开发时使用临时环境变量
"""
export ARCHIVE_BOT_TOKEN="开发Token"
dotnet run --project src/LunariumBot.Net.Archive
"""

# 3. 部署时在服务器上修改
"""
# 在服务器上编辑
nano /home/lunarium/LunariumBot.Net/LunariumBot.Net.Archive/start.sh
# 修改后仅存在于服务器，不会同步回本地仓库
"""

# 4. 更新脚本模板
"""
# 修改 generate_scripts.py 中的模板
python3 generate_scripts.py  # 重新生成
git add generate_scripts.py
git commit -m "更新脚本模板"
# 注意：这不会影响服务器上已经定制的 start.sh
"""
