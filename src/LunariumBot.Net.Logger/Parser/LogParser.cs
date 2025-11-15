// The message template parsing logic in this file is heavily inspired by Serilog,
// an excellent open-source logging library for .NET.
//
// While this file is a new implementation from scratch, using a state machine
// approach, the concepts (e.g. parser rule, MessageTemplate, semantization Token ...) are derived
// from the original work by the Serilog contributors.
//
// For the original project, see: https://github.com/serilog/serilog
// Serilog is licensed under the Apache License 2.0.

using System.Collections.Concurrent;
using System.Text;

namespace LunariumBot.Net.Logger.Parser;

internal enum ParserState
{
    Start,
    Text,           // 状态1：解析普通文本
    Property, // 状态2：在{}内部
    Complete
}

internal enum PropertyState
{
    PropertyName,
    Alignment,
    Format,
    Complete
}

internal static class LogParser
{
    internal static MessageTemplate EmptyMessageTemplate = new([]);
    private const int CacheMaxCountLimit = 4096;

    private static readonly ConcurrentDictionary<string, MessageTemplate> _templateCache = new();

    public static MessageTemplate ParseMessage(string message)
    {
        try
        {
            if (message is null || message.Length == 0) return EmptyMessageTemplate;

            if (_templateCache.TryGetValue(message, out var cachedResult))
                return cachedResult;
    
            var result = Parser(message);
    
            if (_templateCache.Count == CacheMaxCountLimit) _templateCache.Clear();
            _templateCache[message] = result;
            return result;
        }
        catch (Exception ex)
        {
            InternalLogger.Error(ex, "ParseMessage Exception:");
            return EmptyMessageTemplate;
        }
    }

    private static MessageTemplate Parser(string message)
    {
        if (message is null || message.Length == 0) return EmptyMessageTemplate;

        try
        {
            var tokens = new List<MessageTemplateTokens>();

            // StringBuilder? textBuffer = null;
            var remaining = message.AsSpan();
            // var startPos = 0;
            var nextSpecial = 0;
            var state = ParserState.Start;

            // void FlushTextBuffer()
            // {
            //     if (textBuffer is not null && textBuffer.Length > 0)
            //     {
            //         tokens.Add(new TextToken(textBuffer.ToString()));
            //         textBuffer.Clear();
            //     }
            // }

            while (remaining.Length > 0 && state != ParserState.Complete)
            {
                switch (state)
                {
                    case ParserState.Start:
                        // 快速检查：是纯文本？
                        // 从索引 0 开始, 寻找第一个 { 或 }的位置
                        nextSpecial = remaining.IndexOfAny('{', '}');

                        // 如果没有找到(返回-1), 或第一个 { 或 } 就位于字符串末尾("abcd{" "abcd}" 即无法凑成一对{}完成合法解析)
                        if (nextSpecial == -1 || nextSpecial == remaining.Length - 1)
                        {
                            tokens.Add(new TextToken(remaining.ToString()));
                            state = ParserState.Complete;
                        }
                        else
                        {
                            // 进入解析
                            state = ParserState.Text;
                        }
                        break;

                    case ParserState.Text:
                        // 从切片开头开始, 寻找第一个 { 或 }的位置
                        nextSpecial = remaining.IndexOfAny('{', '}');

                        // 如果没有找到(返回-1), 或第一个{ }就位于字符串末尾("abcd{" "abcd}" 即无法凑成一对{}完成合法解析)
                        if (nextSpecial == -1 || nextSpecial == remaining.Length - 1)
                        {
                            // 从切片开头直到结尾的字符串作为 TextToken
                            tokens.Add(new TextToken(remaining.ToString()));
                            state = ParserState.Complete;
                        }
                        else // nextSpecial != -1 且 nextSpecial 的取值范围为 0 到 message.Length - 2 (!= Length - 1)
                        {
                            // =====================================================================================
                            // 此数组的访问安全由 if 条件的 nextSpecial == message.Length - 1 保证
                            // 该 表达式 保证了在进入 else 分支时 nextSpecial 的位置不会是 message 的最后一位索引
                            // 保证了 +1 是安全访问, 而 +2 +3 ... 都会导致数组越界, 此处的逻辑也仅需 +1, 不应访问 +2 +3 ...
                            //  e.g. [0, 1, 2, 3, 4](Length:5), else 进入条件: nextSpecial < 4 (Length - 1) 即: nextSpecial 可能为 [0, 1, 2, 3]
                            // =====================================================================================
                            if (remaining[nextSpecial + 1] == remaining[nextSpecial]) // { or } 的下一个字符还是自身, 即: {{ 或 }}
                            {
                                // ======== 转义 ========
                                // {} 会被视为日志参数进行解析, 那么如果想要在日志中输出带有 {} 本身的内容, 则需要一个规则进行转移
                                // 即: {{ 或 }} 需要转为 { 或 }
                                // e.g. "abcd {{ xyz }} efg" 输出为 "abcd { xyz } efg"
                                // ======================

                                // ==== 当前的转义方案 ====
                                // 通过这个方案实现完全无 StringBuilder 的解析方案, 转义依靠保留特殊字符本身 + 跳过完成
                                // 把从 startPos 开头到特殊字符以及包括特殊字符自身, 都追加到缓冲区, + 1 来包含当前位置所在的 {
                                tokens.Add(new TextToken(remaining[..(nextSpecial + 1)].ToString()));
                                // 然后通过跳过下一个特殊字符来完成 {{ -> { 或 }} -> } 的转义, 单个的 { 或 } 已经在截取中包含了
                                // ======================

                                // ==== 之前的转义方案 ==== ==========
                                // 把从 startPos 开头到特殊字符之前的所有文本, 都追加到缓冲区 (不包含特殊字符自身)
                                // textBuffer.Append(message.AsSpan(startPos, nextSpecial)); // 使用零基索引作为长度, 这会导致截取的字符串不包含 nextSpecial 当前位置的字符
                                // 把特殊字符({ 或 })本身追加进去, 完成 {{ -> { 或 }} -> } 的转义
                                // textBuffer.Append(message[nextSpecial]); // 将当前位置的字符({或})追加到字符串

                                // ====== 状态更新 =======
                                // 创建 TextToken
                                // FlushTextBuffer();
                                // 更新下一状态起始索引
                                // startPos = nextSpecial + 2; // 跳过当前位置的 { 自身, 跳过第二个 {
                                // ====================== ==========

                                // ====== 状态更新 =======
                                // 更新切片
                                remaining = remaining[(nextSpecial + 2)..]; // 跳过当前位置的 { 自身, 跳过第二个 {
                                // 进入普通文本解析
                                state = ParserState.Text;
                            }
                            else if (remaining[nextSpecial] == '}') // 不是 {{ 或 }}的情况下, 如果是单个 }
                            {
                                // 视为普通文本处理
                                // 不需要转义, 继续延迟创建 sb
                                // e.g. "abcd } efg { a } zzz" 解析 d e 之间的这个不成对的 } 是无意义的

                                // ====== 状态更新 =======
                                // 创建 TextToken, 将包含 } 自己的内容截取到 TextToken
                                tokens.Add(new TextToken(remaining[..(nextSpecial + 1)].ToString()));
                                // 更新下一状态起始索引
                                remaining = remaining[(nextSpecial + 1)..]; // 跳过当前位置的 } 自身
                                // 进入普通文本解析
                                state = ParserState.Text;
                            }
                            else // 是单个 {
                            {
                                // ====== 状态更新 =======
                                // 在 nextSpecial 的值不等于 0 的情况下, 创建 TextToken, 将 startPos 到 { 的内容截取到 TextToken (不包括 { )
                                // 避免一次空 TextToken
                                // "abcd {e}{f} gh" <- 如此种情况, Property 处理 {e} 结束时, 起始点将是 { ({f} gh), 导致立即进入这个else分支并执行一次创建 TextToken
                                // 导致TextToken 的字符串从数组中取值范围为 0 - 0, 故在 nextSpecial = 0 的情况下略过添加 TextToken
                                if (nextSpecial != 0) tokens.Add(new TextToken(remaining[..nextSpecial].ToString()));
                                // 更新下一状态起始索引
                                remaining = remaining[(nextSpecial + 1)..]; // 跳过当前位置的 { 自身
                                // 进入属性解析
                                state = ParserState.Property;
                            }
                        }
                        break;

                    case ParserState.Property:
                        // 从切片开头开始, 寻找第一个 } 的位置
                        nextSpecial = remaining.IndexOf('}');

                        // 如果没有找到(返回-1): "ab { cd"
                        if (nextSpecial == -1)
                        {
                            // 从切片开头直到结尾的字符串作为 TextToken
                            tokens.Add(new TextToken('{' + remaining.ToString()));
                            // 此时剩余字符串不存在 } 会导致任何情况下都无法构成一对闭合的 {}, 直接将剩余的都当做普通文本 (并补回跳过的 { 到普通文本)
                            // 解析完成
                            state = ParserState.Complete;
                            break;
                        }

                        // 处理空属性: "ab {} cd"
                        if (nextSpecial == 0)
                        {
                            // 空属性，非法
                            tokens.Add(new TextToken("{}"));
                            // 创建内容为 {} 的 TextToken, 来将之前跳过的 { 和在下一行跳过的 } 添加进去
                            remaining = remaining[(nextSpecial + 1)..];
                            // 跳过 }
                            state = ParserState.Text;
                            break;
                        }

                        // 截取参数占位符
                        var propertyName = remaining[..nextSpecial];
                        // 验证参数占位符合法性并解析
                        var token = ParserProperty(propertyName);
                        tokens.Add(token);

                        if (nextSpecial == remaining.Length - 1)
                        {
                            state = ParserState.Complete;
                            break;
                        }
                        else
                        {
                            remaining = remaining[(nextSpecial + 1)..]; // 跳过当前位置的 { 自身
                            state = ParserState.Text;
                        }
                        break;
                }
            }

            var mergedToken = new List<MessageTemplateTokens>();
            var mergeBuffer = new StringBuilder();
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case TextToken textToken:
                        mergeBuffer.Append(textToken.Text);
                        break;
                    case PropertyToken propertyToken:
                        if (mergeBuffer.Length > 0)
                        {
                            mergedToken.Add(new TextToken(mergeBuffer.ToString()));
                            mergeBuffer.Clear();
                        }
                        mergedToken.Add(propertyToken);
                        break;
                }
            }
            if (mergeBuffer.Length > 0)
            {
                mergedToken.Add(new TextToken(mergeBuffer.ToString()));
            }

            return new MessageTemplate(mergedToken.ToArray());
        }
        catch (Exception ex)
        {
            InternalLogger.Error(ex, "Parser: Parser Failure");
            throw;
        }
    }

    private static MessageTemplateTokens ParserProperty(ReadOnlySpan<char> chars)
    {
        // ====== 默认值 ======
        var propertyName = chars;
        string? format = null;
        int? alignment = null;
        var destructuring = Destructuring.Default;
        // ====================

        try
        {
            // ======= 状态 =======
            var remainingChar = chars;
            var nextSpecial = 0;
            var nextState = PropertyState.PropertyName;
            // 参数类型
            var propertyType = PropertyType.Named;
            // ====================

            while (nextState != PropertyState.Complete && remainingChar.Length > 0)
            {
                switch (nextState)
                {
                    case PropertyState.PropertyName:
                        nextSpecial = remainingChar.IndexOfAny(':', ',');
                        // ==== 合法性检查 =====
                        if (nextSpecial == remainingChar.Length - 1)
                        {
                            // 如果第一个 `,` 或 `;` 就已经是字符串最后一位
                            // {abcd:}, {abcd,}, 这种形式是非法的
                            propertyName = chars;
                            return new TextToken($"{{{chars}}}");
                        }
                        // ====================

                        // ==== 处理属性名 =====
                        // 截取属性名
                        propertyName = nextSpecial == -1 ? remainingChar : remainingChar[..nextSpecial];

                        // 是否只有一位长度
                        if (propertyName.Length == 1)
                        {
                            // 只有一位的情况下不是 Letter字符 或 数字 则都不合法
                            if (!char.IsLetterOrDigit(propertyName[0])) return new TextToken($"{{{chars}}}");
                            // 合法情况下判断类型
                            else propertyType = char.IsDigit(propertyName[0]) ? PropertyType.Positional : PropertyType.Named;
                            // 如果没有 `:` 和 `,`, 可以直接返回了
                            if (nextSpecial == -1)
                            {
                                return new PropertyToken(
                                    propertyName: propertyName.ToString(),
                                    rawText: new TextToken($"{{{chars}}}"),
                                    propertyType: propertyType,
                                    format: null,
                                    alignment: alignment,
                                    destructuring: destructuring
                                );
                            }
                            else
                            {
                                nextState = remainingChar[nextSpecial] == ',' ? PropertyState.Alignment : PropertyState.Format;
                                // 后面不需要继续解析参数名了
                                break;
                            }
                        }
                        else // 不止一位
                        {
                            // 识别第一位是否是前缀, 并且如果是前缀, 必须不止一位才合法
                            if (propertyName[0] == '@' || propertyName[0] == '$')
                            {
                                destructuring = propertyName[0] == '@' ? Destructuring.Destructure : Destructuring.Stringify;
                                // 第二位是 Letter字符 还是 数字?
                                propertyType = char.IsDigit(propertyName[1]) ? PropertyType.Positional : PropertyType.Named;
                                // 跳过解析标识符
                                propertyName = propertyName[1..];
                            }
                            else
                            {
                                propertyType = char.IsDigit(propertyName[index: 0]) ? PropertyType.Positional : PropertyType.Named;
                            }
                        }

                        // 从第一位或第二位开始
                        var previousCharWasDot = false;
                        for (var i = 0; i < propertyName.Length; i++)
                        {
                            var c = propertyName[i];
                            if (propertyType == PropertyType.Positional)
                            {
                                if (!char.IsDigit(c)) return new TextToken($"{{{chars}}}");
                                continue;
                            }
                            else
                            {
                                // 检查第一个字符
                                if (i == 0)
                                {
                                    // 第一个字符必须是字母或下划线
                                    if (!char.IsLetter(c) && c != '_')
                                    {
                                        return new TextToken($"{{{chars}}}");
                                    }
                                    continue;  // 第一个字符合法，继续检查下一个
                                }
                                // 上一个字符是 `.` 时的特殊检查
                                if (previousCharWasDot)
                                {
                                    // 正处于一个 '.' 字符之后
                                    // 必须是 Letter字符 或下划线
                                    // 非法情况：
                                    // 1. 连续的点 (c == '.')
                                    // 2. 点后面是数字 (char.IsDigit(c))
                                    // 3. 其他非法字符
                                    if (char.IsLetter(c) || c == '_')
                                    {
                                        // 合法，重置状态为“正常”
                                        previousCharWasDot = false;
                                    }
                                    else
                                    {
                                        propertyName = chars;
                                        return new TextToken($"{{{chars}}}");
                                    }
                                }
                                else
                                {
                                    // 通常情况
                                    // 可以是字母/数字/下划线, 或者一个点
                                    if (char.IsLetterOrDigit(c) || c == '_')
                                    {
                                        // 合法, 状态不变, 继续循环
                                    }
                                    else if (c == '.')
                                    {
                                        // 遇到了一个 `.`, 下一个循环将做特殊检查
                                        previousCharWasDot = true;
                                    }
                                    else
                                    {
                                        propertyName = chars;
                                        return new TextToken($"{{{chars}}}");
                                    }
                                    // 在正常状态下遇到了其他非法字符
                                }
                            }

                        }

                        if (previousCharWasDot)
                        {
                            return new TextToken($"{{{chars}}}");
                        }

                        // ====== 状态更新 =======
                        if (nextSpecial == -1)
                        {
                            // 如果没有 `:` 或 `,`
                            nextState = PropertyState.Complete;
                        }
                        else if (remainingChar[nextSpecial] == ':')
                        {
                            // 这里会导致数组越界的前提:
                            // nextSpecial(,或:)在 remainingChar 最后一位
                            // 在此时其为 {abcd:}, {abcd,}, 这种形式在开头的合法性检查中会直接失败
                            remainingChar = remainingChar[(nextSpecial + 1)..];
                            nextState = PropertyState.Format;
                        }
                        else if (remainingChar[nextSpecial] == ',')
                        {
                            // 这里会导致数组越界的前提:
                            // nextSpecial(,或:)在 remainingChar 最后一位
                            // 在此时其为 {abcd:}, {abcd,}, 这种形式在开头的合法性检查中会直接失败
                            remainingChar = remainingChar[(nextSpecial + 1)..];
                            nextState = PropertyState.Alignment;
                        }
                        break;

                    case PropertyState.Alignment:
                        nextSpecial = remainingChar.IndexOf(':');
                        ReadOnlySpan<char> alignmentChars;

                        // ==== 合法性检查 =====
                        if (nextSpecial == remainingChar.Length - 1)
                        {
                            // 如果下一个 `,` 或 `;` 就已经是字符串最后一位
                            // {ab,cd:}, {ab,cd,}, 这种形式是非法的
                            propertyName = chars;
                            return new TextToken($"{{{chars}}}");
                        }

                        // ==== 处理对齐 =====
                        // 截取对齐
                        alignmentChars = nextSpecial == -1 ? remainingChar : remainingChar[..nextSpecial];

                        // ==== 合法性检查 ====
                        // 确保没有前导和尾随空格
                        if (alignmentChars[0] == ' ' || alignmentChars[^1] == ' ')
                        {
                            return new TextToken($"{{{chars}}}");
                        }
                        // 直接尝试解析，失败则返回错误, TryParse 同时完成了内容合法性检查
                        if (!int.TryParse(alignmentChars, out int result))
                        {
                            // 解析失败，非法格式
                            return new TextToken($"{{{chars}}}");
                        }
                        alignment = result;

                        // 如果不存在 `:`
                        if (nextSpecial == -1)
                        {
                            nextState = PropertyState.Complete;
                        }
                        else
                        {
                            // 这里会导致数组越界的前提:
                            // nextSpecial(:)在 remainingChar 最后一位
                            // 在此时其为 {ab,cd:}, {ab,cd,}, 这种形式在上面的if else if中会直接return
                            remainingChar = remainingChar[(nextSpecial + 1)..];
                            nextState = PropertyState.Format;
                        }

                        break;

                    case PropertyState.Format:
                        // formatString 只需要禁止前导和尾随空格, 只做最基础的头尾空格检查
                        // 在合法的情况下然后将其原样记录
                        if (remainingChar[0] != ' ' && remainingChar[^1] != ' ')
                        {
                            format = remainingChar.ToString();
                        }
                        else
                        {
                            return new TextToken($"{{{chars}}}");
                        }
                        // 格式化参数顺序: {参数,对齐:格式化}
                        // {参数:格式化,对齐} 是非法的, 故在进入 Format 状态后
                        // : 后的内容都视为格式化字符串
                        // 所以不会有 PropertyState.Format -> Alignment 的状态切换
                        nextState = PropertyState.Complete;
                        break;
                }
            }
            return new PropertyToken(
                propertyName: propertyName.ToString(),
                rawText: new TextToken($"{{{chars}}}"),
                propertyType: propertyType,
                format: format,
                alignment: alignment,
                destructuring: destructuring
                );
        }
        catch (Exception ex)
        {
            InternalLogger.Error(ex, "ParserProperty: Parser Failure");
            return new TextToken($"{{{chars}}}");
        }
    }
}