// NuGet 包依赖
using JiebaNet.Segmenter;


namespace LunariumBot.Net.SearchBot.Utils;

// 定义分词器接口
// 未来如果更换分词器，只需要实现这个接口，不改动业务代码
public interface ITokenizerService
{
    /// <summary>
    /// 对文本进行精确分词
    /// </summary>
    /// <param name="text">要分词的文本</param>
    /// <returns>分词后的词语集合</returns>
    string Cut(string text);

    /// <summary>
    /// 对文本进行搜索引擎模式分词，非常适合用于索引和搜索查询
    /// </summary>
    /// <param name="text">要分词的文本</param>
    /// <returns>分词后的词语集合</returns>
    string CutForSearch(string text);
}

// 这是接口的具体实现类
public class JiebaTokenizerService : ITokenizerService
{
    // 将 segmenter 定义为私有只读字段，在构造函数中初始化一次
    // 在整个服务生命周期内，Jieba的词典只加载一次
    private readonly JiebaSegmenter _segmenter;

    private static readonly HashSet<string> _stopWords = new HashSet<string>
    {
        // 常见中文停用词
        "的", "了", "在", "是", "我", "有", "和", "就", "不", "人", "都", "一", "一个",
        "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有", "看", "好",
        "自己", "这", "那", "里", "就是", "还", "把", "来", "时候", "过", "出", "小",
        "么", "起", "你们", "开始", "下", "可以", "什么", "现在", "知道", "只是",
        // 常见英文停用词
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "is", "are", "was", "were", "be", "been", "have",
        "has", "had", "do", "does", "did", "will", "would", "could", "should",
        "may", "might", "must", "can", "this", "that", "these", "those",
        // 标点符号和特殊字符
        "，", "。", "！", "？", "；", "：", """, """, """, """, "（", "）", "【", "】",
        "《", "》", "、", "·", "…", "—", "－", "+", "=", "*", "/", "\\", "|",
        ",", ".", "!", "?", ";", ":", """, """, "(", ")", "[", "]", "{", "}",
        "<", ">", "&", "%", "$", "#", "@", "^", "~", "`"
    };

    public JiebaTokenizerService()
    {
        _segmenter = new JiebaSegmenter();
    }

    /// <summary>
    /// 对文本进行精确分词
    /// </summary>
    /// <param name="text">要分词的文本</param>
    /// <returns>分词后的词语集合 或 空字符串</returns>
    public string Cut(string text)
    {
        // 返回一个空的集合，而不是null
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        // 使用精确模式分词
        var tokenizer = _segmenter.Cut(text);
        // 清洗和拼接分词结果
        var cleanedText = CleanAndJoinTokens(tokenizer);
        return cleanedText;
    }

    /// <summary>
    /// 对文本进行搜索引擎模式分词，用于索引和搜索查询
    /// </summary>
    /// <param name="text">要分词的文本</param>
    /// <returns>分词后的词语集合 或 空字符串</returns>
    public string CutForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        // 使用搜索引擎模式分词
        var tokenizer = _segmenter.CutForSearch(text);
        // 清洗和拼接分词结果
        var cleanedText = CleanAndJoinTokens(tokenizer);
        return cleanedText;
    }

    /// <summary>
    /// 清洗和拼接分词结果。
    /// 1. 过滤掉停用词（包括标点符号）。
    /// 2. 过滤掉长度小于等于1的词语。
    /// 3. 用单个空格拼接剩余的词语，为FTS做准备。
    /// </summary>
    /// <param name="tokens">Jieba分词后产生的词语序列</param>
    /// <returns>一个干净的、用空格连接的字符串</returns>
    private string CleanAndJoinTokens(IEnumerable<string> tokens)
    {
        // 过滤掉停用词、空字符串、单个字符
        var cleanedTokens = tokens.Where(token => 
            !string.IsNullOrWhiteSpace(token) &&
            token.Length > 1 &&
            !_stopWords.Contains(token)
        );

        // 用空格拼接剩余的词语
        return string.Join(" ", cleanedTokens);
    }
}