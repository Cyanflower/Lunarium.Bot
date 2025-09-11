#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import re
from pathlib import Path

def is_ignored_directory(dir_name):
    """检查是否为需要忽略的目录"""
    ignored_dirs = {'bin', 'obj'}
    return dir_name.lower() in ignored_dirs

def analyze_cs_file(file_path):
    """分析单个.cs文件的行数统计"""
    total_lines = 0
    code_lines = 0
    comment_lines = 0
    
    try:
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as file:
            in_block_comment = False
            
            for line in file:
                total_lines += 1
                line = line.strip()
                
                # 空行跳过
                if not line:
                    continue
                
                # 处理块注释
                if in_block_comment:
                    comment_lines += 1
                    if '*/' in line:
                        in_block_comment = False
                    continue
                
                # 检查块注释开始
                if '/*' in line:
                    comment_lines += 1
                    in_block_comment = True
                    if '*/' in line:
                        in_block_comment = False
                    continue
                
                # 检查单行注释
                if line.startswith('//'):
                    comment_lines += 1
                    continue
                
                # 检查行内注释（代码+注释的行算作代码行）
                if '//' in line:
                    code_lines += 1
                    continue
                
                # 其他情况算作代码行
                code_lines += 1
                
    except Exception as e:
        print(f"读取文件 {file_path} 时出错: {e}")
        return 0, 0, 0
    
    return total_lines, code_lines, comment_lines

def find_cs_files(root_dir):
    """递归查找所有.cs文件，忽略bin和obj目录"""
    cs_files = []
    
    for root, dirs, files in os.walk(root_dir):
        # 移除需要忽略的目录
        dirs[:] = [d for d in dirs if not is_ignored_directory(d)]
        
        # 查找.cs文件
        for file in files:
            if file.lower().endswith('.cs'):
                cs_files.append(os.path.join(root, file))
    
    return cs_files

def main():
    """主函数"""
    print("C# 代码文件分析工具")
    print("=" * 50)
    
    # 获取当前目录
    current_dir = os.getcwd()
    print(f"分析目录: {current_dir}")
    print()
    
    # 查找所有.cs文件
    cs_files = find_cs_files(current_dir)
    
    if not cs_files:
        print("未找到任何.cs文件")
        return
    
    print(f"找到 {len(cs_files)} 个.cs文件")
    print()
    
    # 统计变量
    total_files = len(cs_files)
    grand_total_lines = 0
    grand_code_lines = 0
    grand_comment_lines = 0
    
    # 分析每个文件
    print("文件分析结果:")
    print("-" * 80)
    print(f"{'文件路径':<50} {'总行数':<8} {'代码行数':<8} {'注释行数':<8}")
    print("-" * 80)
    
    for file_path in sorted(cs_files):
        total, code, comment = analyze_cs_file(file_path)
        
        # 计算相对路径用于显示
        rel_path = os.path.relpath(file_path, current_dir)
        
        print(f"{rel_path:<50} {total:<8} {code:<8} {comment:<8}")
        
        # 累加统计
        grand_total_lines += total
        grand_code_lines += code
        grand_comment_lines += comment
    
    # 打印汇总信息
    print("-" * 80)
    print(f"{'汇总统计':<50} {grand_total_lines:<8} {grand_code_lines:<8} {grand_comment_lines:<8}")
    print("=" * 80)
    
    print("\n详细统计:")
    print(f"分析的文件数量: {total_files}")
    print(f"总行数: {grand_total_lines}")
    print(f"代码行数: {grand_code_lines}")
    print(f"注释行数: {grand_comment_lines}")
    print(f"空行数: {grand_total_lines - grand_code_lines - grand_comment_lines}")
    
    if grand_total_lines > 0:
        code_ratio = (grand_code_lines / grand_total_lines) * 100
        comment_ratio = (grand_comment_lines / grand_total_lines) * 100
        print(f"代码行占比: {code_ratio:.1f}%")
        print(f"注释行占比: {comment_ratio:.1f}%")

if __name__ == "__main__":
    main()