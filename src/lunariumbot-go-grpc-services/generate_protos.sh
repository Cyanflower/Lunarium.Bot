#!/bin/bash

# set -e: 如果任何命令失败，脚本将立即退出。
# set -o pipefail: 如果管道中的任何命令失败，整个管道都算失败。
set -e
set -o pipefail

# --- 配置 ---
# .proto 文件所在的源目录
PROTO_DIR=./protos
# 生成的 Go 代码要输出到的根目录
GO_OUT_DIR=./internal/pb

# --- 脚本开始 ---
echo "🚀 Starting Protobuf code generation..."
echo "Source directory: $PROTO_DIR"
echo "Output directory: $GO_OUT_DIR"

# 检查源目录是否存在
if [ ! -d "$PROTO_DIR" ]; then
    echo "❌ Source directory '$PROTO_DIR' not found. Nothing to do."
    exit 0
fi

# 使用 `find` 找到所有的 .proto 文件，并对每一个文件进行处理
# -print0 和 `while read -d ''` 是处理包含特殊字符文件名的最安全方式
find "$PROTO_DIR" -name "*.proto" -print0 | while IFS= read -r -d '' proto_file; do
    
    # 1. 从完整路径中提取文件名（不含扩展名）
    # 例如：从 "./protos/monitoring.proto" 得到 "monitoring"
    base_name=$(basename "$proto_file" .proto)
    
    # 2. 根据文件名，在输出目录内创建对应的子文件夹
    # 例如：创建 "./internal/pb/monitoring"
    output_subdir="$GO_OUT_DIR/$base_name"
    mkdir -p "$output_subdir"
    
    echo "---------------------------------"
    echo "Processing: $proto_file"
    echo " -> Outputting to: $output_subdir"
    
    # 3. 执行 protoc 命令，为当前这一个文件生成代码
    protoc \
        --proto_path="$PROTO_DIR" \
        --go_out="$output_subdir" \
        --go-grpc_out="$output_subdir" \
        --go_opt=paths=source_relative \
        --go-grpc_opt=paths=source_relative \
        "$proto_file"

done

echo "---------------------------------"
echo "✅ All Protobuf files generated successfully!"