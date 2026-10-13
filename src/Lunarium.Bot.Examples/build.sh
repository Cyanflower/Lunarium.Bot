#!/bin/bash

# ==============================================================================
# Lunarium.Bot.Examples 编译脚本
# ==============================================================================

echo "正在编译 Lunarium.Bot.Examples..."

dotnet publish -c Release

if [ $? -eq 0 ]; then
  echo "✓ 编译完成"
else
  echo "✗ 编译失败"
  exit 1
fi
