# #!/bin/bash

# # =================配置区域=================
# TIMEZONE="Asia/Shanghai"
# PROXY_ADDR="http://127.0.0.1:7897"
# # =========================================

# # --- 1. 智能代理设置 ---
# PROXY_SET_BY_SCRIPT=false

# # 检查当前 Shell 会话是否已经有代理设置
# if [ -n "$http_proxy" ]; then
#     echo "🌐 检测到已有代理环境变量 ($http_proxy)，脚本将直接使用该设置。"
# else
#     echo "🔌 当前未检测到代理，脚本正在启用临时代理..."
#     export http_proxy="$PROXY_ADDR"
#     export https_proxy="$PROXY_ADDR"
#     # 标记是由脚本设置的代理
#     PROXY_SET_BY_SCRIPT=true
# fi

# # 定义清理函数：脚本退出（无论成功还是中断）时执行
# cleanup() {
#     if [ "$PROXY_SET_BY_SCRIPT" = true ]; then
#         unset http_proxy
#         unset https_proxy
#         echo "🛡️ 脚本退出，已自动关闭临时代理。"
#     fi
# }

# # 使用 trap 捕获 EXIT 信号（包括正常结束、出错退出、Ctrl+C中断）
# trap cleanup EXIT


# # --- 2. 智能 Git 提交 ---

# # 使用 git status --porcelain 检查工作区是否有变动（包括修改、新增、删除）
# if [ -n "$(git status --porcelain)" ]; then
#     echo "📝 检测到文件变动，开始执行提交流程..."
    
#     # 获取当前时间
#     current_time=$(TZ="$TIMEZONE" date -Iseconds)

#     # 构建提交信息
#     if [ -n "$1" ]; then
#         commit_message="${current_time} >> $1"
#     else
#         commit_message="$current_time"
#     fi

#     echo "➕ 正在执行 git add ..."
#     git add .

#     echo "💾 正在提交，提交信息: $commit_message"
#     git commit -m "$commit_message"
# else
#     echo "☕ 工作区干净（无未提交的变动），跳过 add 和 commit 步骤。"
# fi


# # --- 3. 推送 ---
# echo "🚀 正在推送到远程仓库..."
# git push

# echo "✅ 所有操作完成！"