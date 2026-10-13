using System.Text;
using System.Collections.Concurrent;

using Lunarium.Bot.Logger;

using Lunarium.Bot.SearchBot.Models;

namespace Lunarium.Bot.SearchBot.Utils;

public static class KeywordParser
{
    private static readonly HashSet<char> delimiters = new HashSet<char>
    {
        ' ', '，', '。', '、', '；', '：', '；', '「', '」', '『', '』',
        '（', '）', '(', ')', '[', ']', '{', '}', '【', '】', '\\', '/',
        '&', '|', '!', 
    };
    static KeywordParser() { }
    public static List<string> ParseQueryString(string keywords)
    {
        try
        {
            var normalized = NormalizedSplit(keywords);
            var tokenized = normalized.Select(s => JiebaTokenizerService.CutForSearch(s, true)).ToList();
            return tokenized;
        }
        catch
        {
            throw;
        }
    }

    private static List<string> NormalizedSplit(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (delimiters.Contains(c))
            {
                sb.Append(','); // 替换为目标分隔符
            }
            else
            {
                sb.Append(c); // 保留原字符
            }
        }
        return sb.ToString()
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }
}
