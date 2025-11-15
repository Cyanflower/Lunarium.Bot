#!/bin/bash

# 全局变量：设置时区(可以根据需要修改)
TIMEZONE="Asia/Shanghai"

# 获取当前 ISO8601 格式的时间
current_time=$(TZ="$TIMEZONE" date -Iseconds)

# 构建提交信息
if [ -n "$1" ]; then
    # 如果有参数，添加 >> 和参数内容
    commit_message="${current_time} >> $1"
else
    # 如果没有参数，只使用时间
    commit_message="$current_time"
fi

# 执行 git 命令
echo "正在执行 git add ..."
git add .

echo "正在提交，提交信息: $commit_message"
git commit -m "$commit_message"

echo "正在推送到远程仓库..."
git push

echo "完成！"