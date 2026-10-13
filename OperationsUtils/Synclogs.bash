#!/bin/bash

# ============================================
# 日志文件同步脚本
# 从远程主机同步 .log 和 .jsonl 文件
# ============================================

# 配置区域 - 请根据实际情况修改
REMOTE_HOST="BotCenterVPS"
REMOTE_PATH="~/services/cyanflower/Lunarium.Bot/"  # 修改为实际的远程路径
LOCAL_FOLDER="synced_logs"  # 本地保存文件夹名称

# ============================================
# 脚本开始
# ============================================

# 获取脚本所在目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOCAL_PATH="${SCRIPT_DIR}/${LOCAL_FOLDER}"

# 颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 打印带颜色的消息
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# 检查rsync是否安装
if ! command -v rsync &> /dev/null; then
    print_error "rsync 未安装，请先安装 rsync"
    exit 1
fi

# 创建本地目标目录
if [ ! -d "$LOCAL_PATH" ]; then
    print_info "创建本地目录: $LOCAL_PATH"
    mkdir -p "$LOCAL_PATH"
fi

# 检查SSH连接
print_info "测试SSH连接到 ${REMOTE_HOST}..."
if ! ssh -o ConnectTimeout=5 -o BatchMode=yes "$REMOTE_HOST" exit 2>/dev/null; then
    print_error "无法连接到 ${REMOTE_HOST}，请检查SSH配置"
    exit 1
fi

print_info "SSH连接成功"
echo ""
print_info "===== 开始同步 ====="
print_info "远程主机: ${REMOTE_HOST}"
print_info "远程路径: ${REMOTE_PATH}"
print_info "本地路径: ${LOCAL_PATH}"
echo ""

# 执行rsync同步
# -a: 归档模式，保持文件属性和目录结构
# -v: 显示详细信息
# -z: 压缩传输
# -u: 只更新较新的文件（跳过本地较新的文件）
# --progress: 显示进度
# --include: 包含规则
# --exclude: 排除规则
# --stats: 显示统计信息

rsync -avzu --progress \
    --include='*/' \
    --include='*.log' \
    --include='*.jsonl' \
    --exclude='*' \
    --stats \
    "${REMOTE_HOST}:${REMOTE_PATH}" \
    "${LOCAL_PATH}/"

# 检查rsync执行结果
if [ $? -eq 0 ]; then
    echo ""
    print_info "===== 同步完成 ====="
    print_info "文件保存在: ${LOCAL_PATH}"
else
    echo ""
    print_error "===== 同步失败 ====="
    exit 1
fi