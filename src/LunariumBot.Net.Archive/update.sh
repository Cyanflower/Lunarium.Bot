#!/bin/bash

SOURCE_DIR="./dist/LunariumBot.Net.Archive/"
REMOTE_DEST_URL="LunariumBot.Net:/home/lunarium/LunariumBot.Net/LunariumBot.Net.Archive"
REMOTE_DEST_PATH="/home/lunarium/LunariumBot.Net/LunariumBot.Net.Archive"

echo "🚀 准备连接，并确保远程目录存在..."
ssh LunariumBot.Net "mkdir -p $REMOTE_DEST_PATH"

# ==========================================================
#  ✨✨✨ 优雅的两步舞，从这里开始 ✨✨✨
# ==========================================================

# --- 第一步：同步核心应用文件 ---
# 我们更新所有必要的文件，但是用 --exclude 把 start.sh 排除在外
echo "🚀 (步骤1/2) 正在更新核心应用文件..."
rsync -avz \
  --include='LunariumBot.Net.Archive' \
  --include='appsettings.template.jsonc' \
  --exclude='start.sh' \
  --exclude='*' \
  "$SOURCE_DIR" "$REMOTE_DEST_URL"

# 检查第一步是否成功
if [ $? -ne 0 ]; then
  echo "💥 核心文件同步失败！请检查上面的错误信息。"
  exit 1
fi

# --- 第二步：处理 start.sh 启动脚本 ---
# 我们只为 start.sh 单独运行一次 rsync，并附上 --ignore-existing 选项
# 这能确保只有当服务器上不存在 start.sh 时，我们才会上传它
echo "🚀 (步骤2/2) 正在检查并上传启动脚本 (如果需要)..."
rsync -avz --ignore-existing \
  "${SOURCE_DIR}start.sh" \
  "$REMOTE_DEST_URL"
  
# ==========================================================
#  ✨✨✨ 两步舞结束 ✨✨✨
# ==========================================================


# 检查第二步是否成功 (rsync 在跳过文件时也会返回成功码0)
if [ $? -eq 0 ]; then
  echo "✅ 哇！部署流程完美结束！你的服务器配置得到了妥善的保护！"
else
  echo "💥 噢，上传过程中好像出了点问题，请检查上面的错误信息。"
  exit 1
fi