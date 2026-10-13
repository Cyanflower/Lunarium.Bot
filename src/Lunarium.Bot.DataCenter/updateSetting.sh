#!/bin/bash

# ==========================================================
#  配置区域
# ==========================================================
SOURCE_DIR="./" 
REMOTE_HOST="BotCenterVPS"
REMOTE_BASE_PATH="~/services/cyanflower/Lunarium.Bot/Lunarium.Bot.DataCenter"
TARGET_FILE="appsettings.jsonc"

LOCAL_FILE_PATH="${SOURCE_DIR}${TARGET_FILE}"
REMOTE_DEST_URL="${REMOTE_HOST}:${REMOTE_BASE_PATH}"

# ==========================================================
#  前置检查
# ==========================================================

# 检查本地文件是否存在
if [ ! -f "$LOCAL_FILE_PATH" ]; then
  echo "❌ 错误：本地未找到配置文件：$LOCAL_FILE_PATH"
  echo "   当前工作目录是: $(pwd)"
  echo "   请确认你在项目根目录下执行脚本，且 appsettings.jsonc 存在于此。"
  exit 1
fi

echo "⚠️  警告：你即将执行配置覆盖操作！"
echo "   源文件: $(pwd)/$TARGET_FILE"
echo "   目标地: $REMOTE_DEST_URL/$TARGET_FILE"
echo ""
read -p "❓ 确定要继续吗？(y/n): " confirm
if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
  echo "操作已取消。"
  exit 0
fi

# ==========================================================
#  执行更新
# ==========================================================

# 远程备份
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
echo "[1/2] 正在备份远程服务器上的旧配置..."
ssh "$REMOTE_HOST" "if [ -f ${REMOTE_BASE_PATH}/${TARGET_FILE} ]; then cp ${REMOTE_BASE_PATH}/${TARGET_FILE} ${REMOTE_BASE_PATH}/${TARGET_FILE}.bak_${TIMESTAMP}; echo '   ✓ 备份完成: ${TARGET_FILE}.bak_${TIMESTAMP}'; else echo '   ! 远程文件不存在，跳过备份'; fi"

if [ $? -ne 0 ]; then
  echo "❌ 错误：无法连接服务器或备份失败，操作终止。"
  exit 1
fi

# 上传新配置
echo "[2/2] 正在上传并覆盖新配置..."
rsync -avz \
  "$LOCAL_FILE_PATH" \
  "$REMOTE_DEST_URL/"

# ==========================================================
#  结束处理
# ==========================================================

if [ $? -eq 0 ]; then
  echo ""
  echo "✓ 配置更新成功！"
else
  echo "❌ 错误：文件上传失败。"
  exit 1
fi