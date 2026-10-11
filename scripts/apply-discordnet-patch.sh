#!/usr/bin/env bash
# 把外层仓库维护的 Discord.Net 本地补丁同步进 vendored 源码目录。
#
# 约定：
#   - Discord.Net/          保持"纯净"：HEAD detached @ 上游 tag，不含任何本地提交
#   - DiscordNet.Local/     本补丁的唯一权威副本（随外层仓库提交/备份）
#
# 补丁内容（Directory.Build.props）：
#   - InternalsVisibleTo(LunariumBot.Net.Extensions)：让扩展能访问库的 internal 成员
#   - SignAssembly=false：免去 InternalsVisibleTo 必须附带公钥的麻烦
#
# 用法：
#   scripts/apply-discordnet-patch.sh          # 同步（幂等）
#   scripts/apply-discordnet-patch.sh --check  # 只校验是否一致（不一致则退出码 1）
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src="$root/DiscordNet.Local/Directory.Build.props"
dst="$root/Discord.Net/Directory.Build.props"

if [ ! -d "$root/Discord.Net" ]; then
    echo "❌ 未找到 $root/Discord.Net"
    echo "   首次设置请先克隆上游（走代理）并切到外层记录的提交："
    echo "     SHA=\$(git ls-files -s Discord.Net | awk '{print \$2}')"
    echo "     git -c http.proxy=http://127.0.0.1:7890 clone https://github.com/discord-net/Discord.Net.git Discord.Net"
    echo "     git -C Discord.Net checkout \"\$SHA\""
    echo "   然后重新运行本脚本。"
    exit 1
fi

if [ ! -f "$src" ]; then
    echo "❌ 缺少权威副本 $src"
    exit 1
fi

if cmp -s "$src" "$dst"; then
    echo "✔ 补丁已是最新，无需处理"
    exit 0
fi

if [ "${1:-}" = "--check" ]; then
    echo "✖ 补丁不一致：$dst 与权威副本不同"
    echo "  运行 scripts/apply-discordnet-patch.sh 同步"
    exit 1
fi

cp "$src" "$dst"
echo "✔ 已同步补丁 → $dst"
