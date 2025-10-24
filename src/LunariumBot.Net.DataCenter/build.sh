#!/bin/bash

# ==============================================================================
# LunariumBot.Net.DataCenter 编译脚本
# ==============================================================================

echo "正在编译 LunariumBot.Net.DataCenter..."

dotnet publish -c Release

if [ $? -eq 0 ]; then
  echo "✓ 编译完成"
else
  echo "✗ 编译失败"
  exit 1
fi
