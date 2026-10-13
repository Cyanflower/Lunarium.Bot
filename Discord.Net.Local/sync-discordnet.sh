#!/usr/bin/env bash
#
# 同步 vendored Discord.Net：
#   1. 按 discordnet.version 里的 tag 克隆 / 切换到官方仓库（无任何本地改动）
#   2. 把本目录的 Directory.Build.props 注入到检出根目录
#      （该 props 注入 InternalsVisibleTo -> Lunarium.Bot.Extensions 并关闭强签名）
#
# 用法:
#   ./sync-discordnet.sh              # 使用 discordnet.version
#   ./sync-discordnet.sh 3.21.0       # 临时指定版本（不会写回 version 文件）
#
# 升级版本的正确做法：改 discordnet.version，再跑一次本脚本。
#
set -euo pipefail

REPO_URL="https://github.com/discord-net/Discord.Net.git"
PROPS_NAME="Directory.Build.props"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_REPO="$(cd "$HERE/.." && pwd)/Discord.Net"
PROPS_SRC="$HERE/$PROPS_NAME"
VERSION_FILE="$HERE/discordnet.version"

version="${1:-$(tr -d '[:space:]' < "$VERSION_FILE")}"
if [ -z "$version" ]; then
    echo "错误: 未指定版本号，且 $VERSION_FILE 为空" >&2
    exit 1
fi

# --- 1. 克隆 / 更新到指定 tag ------------------------------------------------
if [ -d "$TARGET_REPO/.git" ]; then
    echo "==> 更新已有检出: $TARGET_REPO"
    git -C "$TARGET_REPO" fetch --tags --prune origin
    git -C "$TARGET_REPO" checkout --force "$version"
else
    echo "==> 克隆 $REPO_URL @ $version"
    git clone --branch "$version" "$REPO_URL" "$TARGET_REPO"
fi

# --- 2. 让嵌套仓库忽略注入的 props -------------------------------------------
# .git/info/exclude 不受版本控制，克隆后必然是空的；不补这一行，
# 嵌套仓库会一直把 Directory.Build.props 显示为未跟踪文件。
exclude_file="$TARGET_REPO/.git/info/exclude"
grep -qxF "$PROPS_NAME" "$exclude_file" 2>/dev/null \
    || echo "$PROPS_NAME" >> "$exclude_file"

# --- 3. 注入 props -----------------------------------------------------------
cp "$PROPS_SRC" "$TARGET_REPO/$PROPS_NAME"

# --- 4. 自检 -----------------------------------------------------------------
actual="$(git -C "$TARGET_REPO" describe --tags --exact-match 2>/dev/null \
    || git -C "$TARGET_REPO" rev-parse --short HEAD)"
if [ "$actual" != "$version" ]; then
    echo "错误: 版本不符，期望 $version，实际 $actual" >&2
    exit 1
fi

dirty="$(git -C "$TARGET_REPO" status --porcelain)"
if [ -n "$dirty" ]; then
    echo "错误: 嵌套仓库不干净（不应出现本地改动）:" >&2
    echo "$dirty" >&2
    exit 1
fi

echo "完成: Discord.Net @ $version，props 已注入，嵌套仓库干净。"
