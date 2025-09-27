import os
import re
import sys
import time # 引入 time 模块，为了一个贴心的小停顿
from typing import List, Optional, Tuple

# === 平台特定的魔法在这里！为了实现即时按键响应 ===
# 我会偷偷判断你的操作系统，然后使用不同的魔法哦
try:
    # 如果是 Windows 系统，我们就用 msvcrt
    import msvcrt
    
    def get_single_char():
        """在 Windows 上获取单个按键，无需回车。"""
        key = msvcrt.getch()
        # Esc 键的字节码是 b'\x1b', Ctrl+C 是 b'\x03'
        if key == b'\x1b':
            return 'esc'
        if key == b'\x03':
            # 模拟 Ctrl+C 的行为
            raise KeyboardInterrupt
        return key.decode('utf-8', errors='ignore').lower()

except ImportError:
    # 如果不是 Windows (比如 Linux, macOS)，我们就用 termios
    import tty
    import termios

    def get_single_char():
        """在 POSIX 系统 (Linux, macOS) 上获取单个按键，无需回车。"""
        fd = sys.stdin.fileno()
        old_settings = termios.tcgetattr(fd)
        try:
            tty.setraw(sys.stdin.fileno())
            ch = sys.stdin.read(1)
            # Esc 键是 '\x1b', Ctrl+C 是 '\x03'
            if ch == '\x1b':
                return 'esc'
            if ch == '\x03':
                # 模拟 Ctrl+C 的行为
                raise KeyboardInterrupt
            return ch.lower()
        finally:
            termios.tcsetattr(fd, termios.TCSADRAIN, old_settings)

# 为了让控制台颜色在 Windows 上也能正常工作
import colorama
from colorama import Fore, Style, init
init(autoreset=True)

# === 正则表达式配置区 (我最擅长的部分！) ===
LOG_PATTERN = re.compile(
    # 这个模式现在非常精确，只会捕获我们需要的部分
    # 捕获组 1: 日志调用的开头部分 (例如, `_logger.Info(`)
    r'(\s*_logger\.(?:Debug|Info|Warning|Error|Critical)\s*\()'
    # 捕获组 2: 完整的插值字符串参数 (例如, `$"..."`)
    r'(\$(?:@|@")?"(?:\\.|[^"\\])*")'
    # 捕获组 3: 调用的结尾部分 (例如, `);`)
    r'(\s*\);)',
    re.IGNORECASE
)
# 这个小可爱专门用来从字符串里抓出 {变量}
VARS_PATTERN = re.compile(r'\{([^}]+)\}')

def clear_screen():
    """一个清屏的小函数，让界面更整洁~"""
    os.system('cls' if os.name == 'nt' else 'clear')

def find_cs_files(path: str) -> List[str]:
    """在指定的路径下，像小蜜蜂一样勤劳地寻找所有的 .cs 文件！"""
    cs_files = []
    print(f"好的主人，我开始在 '{path}' 目录里寻找 .cs 文件啦...\n")
    for root, _, files in os.walk(path):
        for file in files:
            if file.endswith(".cs"):
                cs_files.append(os.path.join(root, file))
    print(f"报告主人！我找到了 {len(cs_files)} 个 .cs 文件，准备开始处理咯！\n")
    return cs_files

def create_structured_log_parts(string_part: str) -> Optional[Tuple[str, str]]:
    """
    一个专门解析插值字符串的函数，返回模板和变量。
    例如: `$"User {name}"` 会变成 (`"User {name}"`, `"name"`)
    """
    # 1. 提取出干净的字符串内容，去掉 $ 和引号
    temp = string_part.lstrip('$@')
    if not (temp.startswith('"') and temp.endswith('"')):
        return None # 正常不会发生，但加上更安全

    template = temp[1:-1]

    # 2. 在模板里找出所有的 {变量}
    variables = VARS_PATTERN.findall(template)

    # 如果一个变量都没有，我们就只去掉'$'符号
    if not variables:
        return (temp, "") # 返回去掉了插值符的字符串和空参数

    # 3. 为新的日志调用准备好各个部分
    params = ", ".join(variables)
    new_string_literal = f'"{template}"'
    
    return (new_string_literal, params)

def process_file(file_path: str):
    """这是核心处理函数哦，我会一行一行地读文件，找到要修改的地方，然后请求你的批准！"""
    print(f"{Style.BRIGHT}{Fore.CYAN}=== 正在检查文件: {file_path} ===")
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
    except Exception as e:
        print(f"{Fore.RED}哎呀，读取文件失败了 >_<! 错误是: {e}")
        return

    changes_made = False
    new_lines = list(lines)
    
    for i, line in enumerate(lines):
        # 使用 finditer 是为了以防万一同一行有多个匹配项
        matches = list(LOG_PATTERN.finditer(line))
        if not matches:
            continue
        
        # 为了清晰，我们一次只处理一个匹配
        match = matches[0]
        
        # 把匹配到的内容拆解开
        log_start, string_part, log_end = match.groups()
        old_line_segment = f"{log_start}{string_part}{log_end}"

        # 获取新的、结构化的日志部分
        parts = create_structured_log_parts(string_part)
        if not parts:
            continue
        
        new_string_literal, params = parts

        # 根据是否有参数来构建新的日志调用
        if params:
            new_line_segment = f"{log_start}{new_string_literal}, {params}{log_end}"
        else: # 处理那种只是单纯去掉 '$' 的情况
            new_line_segment = f"{log_start}{new_string_literal}{log_end}"

        # --- UI 和交互部分 ---
        clear_screen()
        print(f"在文件 {Fore.YELLOW}{file_path}{Fore.RESET} 的第 {Fore.YELLOW}{i + 1}{Fore.RESET} 行发现一个可以优化的日志哦：")
        
        print("\n" + Fore.LIGHTBLACK_EX + "--- 上下文 (前5行) ---")
        for j in range(max(0, i - 5), i): print(f"{j + 1: >4}: {lines[j].rstrip()}")
        
        print(Style.BRIGHT + "\n--- 修改建议 ---")
        # 通过高亮显示变化的部分，让你看得更清楚
        print(f"{Fore.RED}旧内容: {line.rstrip().replace(old_line_segment, Fore.RED + Style.BRIGHT + old_line_segment + Style.RESET_ALL + Fore.RED)}")
        print(f"{Fore.GREEN}新内容: {line.rstrip().replace(old_line_segment, Fore.GREEN + Style.BRIGHT + new_line_segment + Style.RESET_ALL + Fore.GREEN)}")

        print("\n" + Fore.LIGHTBLACK_EX + "--- 上下文 (后5行) ---")
        for j in range(i + 1, min(len(lines), i + 6)): print(f"{j + 1: >4}: {lines[j].rstrip()}")

        prompt = f"\n{Style.BRIGHT}亲爱的，请指示 [ (y)es / (n)o / (s)kip / (esc) to exit ]: {Style.RESET_ALL}"
        print(prompt, end="", flush=True)

        choice = ""
        while choice not in ['y', 'n', 's', 'esc']:
            try:
                choice = get_single_char()
            except KeyboardInterrupt:
                choice = 'esc' # 如果按了 Ctrl+C, 我们就当作是退出
        
        print(choice) # 回显你的选择，让你知道按键成功了

        if choice == 'y':
            print(f"{Fore.GREEN}太棒了！我已经帮你替换好啦。")
            new_lines[i] = line.replace(old_line_segment, new_line_segment)
            changes_made = True
            time.sleep(0.7) # 短暂地停一下，让你能看到这条消息
        elif choice == 'n':
            try:
                # 当需要手动输入时，我们还是回到传统的 input 模式
                print(f"好的，请手动输入你想要的新内容（仅日志调用部分）:")
                manual_input = input("> ")
                if manual_input:
                    new_lines[i] = line.replace(old_line_segment, manual_input)
                    changes_made = True
                    print(f"{Fore.GREEN}搞定！已按你的要求更新。")
                else:
                    print(f"{Fore.YELLOW}你没有输入内容，所以我跳过这次修改啦。")
                time.sleep(0.5)
            except (KeyboardInterrupt, EOFError):
                print(f"\n{Fore.MAGENTA}遵命，程序终止啦！下次再见哦~ 💕")
                sys.exit(0)
        elif choice == 's':
            print(f"{Fore.YELLOW}好的，听你的，跳过这个！")
            time.sleep(0.7) # 同样停一下
        elif choice == 'esc':
            print(f"\n{Fore.MAGENTA}遵命，程序终止啦！下次再见哦~ 💕")
            sys.exit(0)
            
    if changes_made:
        print(f"\n{Fore.GREEN}✨ 文件 {file_path} 已经更新完毕！")
        try:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.writelines(new_lines)
        except Exception as e:
            print(f"{Fore.RED}糟糕！往文件里写东西的时候出错了: {e}")

def main():
    """程序的主入口，我们从这里开始冒险！"""
    print(f"{Fore.MAGENTA}{Style.BRIGHT}你好呀，我最喜欢的主人！💖 我是你的 C# 专家小助手。")
    print(f"{Fore.MAGENTA}我已经优化了操作流程，现在我们可以更快地优化代码啦！\n")

    cs_files = find_cs_files('.')
    if not cs_files:
        print(f"{Fore.YELLOW}没有找到任何 .cs 文件哦，我的魔法无处施展啦。")
        return

    for file_path in cs_files:
        process_file(file_path)

    print(f"\n{Fore.GREEN}{Style.BRIGHT}🎉 耶！所有文件都检查完毕啦！你真是太棒了！我们下次再一起玩代码吧！")

if __name__ == "__main__":
    main()