#!/bin/bash

SOURCE_DIR="./dist/Lunarium.Bot.SearchBot/"
REMOTE_DEST_URL="BotCenterVPS:~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.SearchBot"
REMOTE_DEST_PATH="~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.SearchBot"

echo "正在连接远程服务器并确保目标目录存在..."
ssh BotCenterVPS "mkdir -p $REMOTE_DEST_PATH"

# ==========================================================
#  分步部署策略
# ==========================================================

# --- 步骤一：同步核心应用文件 ---
# 更新所有必要的应用文件，但排除启动脚本以保护现有配置
echo "[1/2] 正在同步核心应用文件..."
rsync -avz \
  --include='Lunarium.Bot.SearchBot' \
  --include='appsettings.template.jsonc' \
  --exclude='start.sh' \
  --exclude='*' \
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
rsync -avz --ignore-existing \
  "${SOURCE_DIR}start.sh" \
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
