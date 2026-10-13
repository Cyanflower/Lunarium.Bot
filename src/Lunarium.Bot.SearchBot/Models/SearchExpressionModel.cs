using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Lunarium.Bot.Extensions;

namespace Lunarium.Bot.SearchBot.Models;

/// <summary>
/// 定义搜索模式
/// </summary>
public enum SearchMode
{
    /// <summary>
    /// 精确搜索：所有关键词都必须匹配 (AND 逻辑)。
    /// </summary>
    Exact,

    /// <summary>
    /// 模糊搜索：任意一个关键词匹配即可 (OR 逻辑)。
    /// </summary>
    Fuzzy
}

/// <summary>
/// 定义帖子的排序方式
/// </summary>
public enum PostSortOption
{
    /// <summary>
    /// 按点赞数升序 (从少到多)
    /// </summary>
    ReactionAsc,

    /// <summary>
    /// 按点赞数降序 (从多到少)
    /// </summary>
    ReactionDesc,

    /// <summary>
    /// 按评论数升序 (从少到多)
    /// </summary>
    ReplyCountAsc,

    /// <summary>
    /// 按评论数降序 (从多到少)
    /// </summary>
    ReplyCountDesc,

    /// <summary>
    /// 按创建时间升序 (从旧到新)
    /// </summary>
    CreatedTimeAsc,

    /// <summary>
    /// 按创建时间降序 (从新到旧)
    /// </summary>
    CreatedTimeDesc,

    /// <summary>
    /// 按更新时间升序 (从旧到新)
    /// </summary>
    UpdateTimeAsc,

    /// <summary>
    /// 按更新时间降序 (从新到旧)
    /// </summary>
    UpdateTimeDesc,

    /// <summary>
    /// 按全文搜索相关性降序 (从最相关到最不相关)
    /// </summary>
    RelevanceDesc
}

/// <summary>
/// 添加目标类型
/// </summary>
public enum OptionType
{
    Include,
    Exclude
}

public class SearchConditions
{
    public KeywordConditions? KeywordConditions { get; set; } = null;
    public TagConditions? TagConditions { get; set; } = null;
    public List<long> AuthorIds { get; set; } = new();
    public List<long> ChannelIds { get; set; } = new();
    public PostSortOption PostSortOption { get; set; } = PostSortOption.CreatedTimeDesc;
    public long? CurrentUserId { get; set; } 
}

public class KeywordConditions
{
    public SearchMode SearchMode { get; init; } = SearchMode.Fuzzy;

    public List<string> IncludeKeywords { get; init; } = new();
    public List<string> ExcludeKeywords { get; init; } = new();
}

public class TagConditions
{
    public SearchMode SearchMode { get; init; } = SearchMode.Fuzzy;
    public List<long> IncludeTagIds { get; init; } = new();
    public List<long> ExcludeTagIds { get; init; } = new();
}

public static class SearchConditionsSerializationExtensions
{
    // 1. 创建一个私有的、静态只读的 options 实例。
    //    这是性能和一致性的关键。
    private static readonly JsonSerializerOptions _options = new()
    {
        // 将枚举序列化为字符串，可读性更高
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // 在序列化时忽略值为null的属性，让JSON更紧凑
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // 允许JSON中的属性名不区分大小写，增加反序列化的健壮性
        PropertyNameCaseInsensitive = true,
        // 允许的 Unicode 范围
        Encoder = JavaScriptEncoder.Create(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.CjkUnifiedIdeographs,           // 中日韩统一表意文字
            UnicodeRanges.CjkUnifiedIdeographsExtensionA, // CJK 扩展 A
            UnicodeRanges.CjkCompatibilityIdeographs,     // CJK 兼容汉字
            UnicodeRanges.CjkSymbolsandPunctuation        // CJK 符号和标点
        )
    };

    /// <summary>
    /// 将 SearchConditions 对象序列化为 JSON 字符串。
    /// </summary>
    public static string ToJson(this SearchConditions conditions)
    {
        return JsonSerializer.Serialize(conditions, _options);
    }

    /// <summary>
    /// 将 JSON 字符串反序列化为 SearchConditions 对象。
    /// </summary>
    public static SearchConditions? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        return JsonSerializer.Deserialize<SearchConditions>(json, _options);
    }
}

// public enum OptionType
// {
//     NoPrefix,
//     And,
//     Or,
//     Exclude
// }
// public class ParserResult
// {
//     private readonly List<List<string>> _andOrGroup = new();
//     // 多个排除词之间使用或, 不提供与类型的关系, 用户输入多个排除词绝大部分情况都是包含任意一个的内容一个都不想看见
//     private readonly List<string> _excludeGroup = new();

//     public IReadOnlyList<IReadOnlyList<string>> AndOrGroup => _andOrGroup;
//     public IReadOnlyList<string> ExcludeGroup => _excludeGroup;

//     public void AddItem(OptionType optionType, string keyword)
//     {
//         // ==== 逻辑说明 ====
//         // 核心规则：
//         // And/NoPrefix: 总是将关键词添加到最后一个 And 分组 中, 如果还没有任何分组, 则创建第一个
//         // Or: 总是创建一个全新的 And 分组 并将关键词放入
//         // Exclude: 独立添加到排除列表中
//         // NoPrefix理论可以省略, 但是保留可以使解析端的调用代码在语义上可读性更好

//         // 例子1:
//         // 你好 or 世界 and Hello - World
//         // 第一个词: 你好, OptionType.NoPrefix => _andOrGroup 为空, 创建第一个分组, 列表: _andOrGroup: [[你好]]
//         // 第二个词: 世界, OptionType.Or      => Or 总是创建新分组, 列表: _andOrGroup: [[你好], [世界]]
//         // 第三个词: Hello, OptionType.And     => And 添加到最后一个分组, 列表: _andOrGroup: [[你好], [世界, Hello]]
//         // 第四个词: World, OptionType.Exclude => 添加到排除列表, 列表: _excludeGroup: [World]

//         // 例子2:
//         // - 南瓜 or 苹果 and 香蕉 or 橘子 and 西瓜
//         // 第一个词: 南瓜, OptionType.Exclude => 添加到排除列表, 列表: _excludeGroup: [南瓜]
//         // 第二个词: 苹果, OptionType.Or      => _andOrGroup 为空, Or 创建第一个分组, 列表: _andOrGroup: [[苹果]]
//         // 第三个词: 香蕉, OptionType.And     => And 添加到最后一个分组, 列表: _andOrGroup: [[苹果, 香蕉]]
//         // 第四个词: 橘子, OptionType.Or      => Or 总是创建新分组, 列表: _andOrGroup: [[苹果, 香蕉], [橘子]]
//         // 第五个词: 西瓜, OptionType.And     => And 添加到最后一个分组, 列表: _andOrGroup: [[苹果, 香蕉], [橘子, 西瓜]]

//         switch (optionType)
//         {
//             // NoPrefix 和 And 的逻辑是相同的：将关键词添加到当前的And逻辑分组中 (包含了NoPrefix的创建整个_andOrGroup第一个子列表和内部元素的逻辑)
//             case OptionType.NoPrefix:
//             case OptionType.And:
//                 // 如果 _andOrGroup 为空, 说明这是第一个非排除词, 需要创建第一个分组
//                 if (_andOrGroup.Count == 0)
//                 {
//                     _andOrGroup.Add(new List<string>());
//                 }
//                 // 将关键词添加到最后一个(也就是当前的)And分组中
//                 _andOrGroup.Last().Add(keyword);
//                 break;

//             case OptionType.Or:
//                 // Or 操作总是开启一个新的And分组
//                 _andOrGroup.Add(new List<string> { keyword });
//                 break;

//             case OptionType.Exclude:
//                 _excludeGroup.Add(keyword);
//                 break;
//         }
//     }
// }


// // ===============
// // AST 节点定义
// // ===============

// /// <summary>
// /// 节点基类
// /// </summary>
// public abstract class BaseSearchExpressionNode { }

// /// <summary>
// /// 代表一个无法再使用 与或非 分隔具体的搜索词 (非分词)
// /// 树的叶子节点
// /// </summary>
// public class KeywordNode : BaseSearchExpressionNode
// {
//     public string Term { get; }
//     public KeywordNode(string term) { Term = term; }

// }

// /// <summary>
// /// 树的二元节点
// /// 用于or或and操作
// /// </summary>
// public class BinaryNode : BaseSearchExpressionNode
// {
//     public BaseSearchExpressionNode Left { get; }
//     public LogicalOperator Operator { get; }
//     public BaseSearchExpressionNode Right { get; }

//     public BinaryNode(BaseSearchExpressionNode left, LogicalOperator op, BaseSearchExpressionNode right)
//     {
//         Left = left;
//         Operator = op;
//         Right = right;
//     }

// }

// /// <summary>
// /// 树的一元节点
// /// 用于not操作 
// /// </summary>
// public class UnaryNode : BaseSearchExpressionNode
// {
//     public LogicalOperator Operator { get; }
//     public BaseSearchExpressionNode Operand { get; }

//     public UnaryNode(LogicalOperator op, BaseSearchExpressionNode operand)
//     {
//         // 确保是一元操作符
//         if (op != LogicalOperator.Not)
//             throw new ArgumentException("Invalid unary operator");

//         Operator = op;
//         Operand = operand;
//     }

//     public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitUnary(this);
// }

// /// <summary>
// /// 与或非枚举
// /// </summary>
// public enum LogicalOperator
// {
//     And,
//     Or,
//     Not
// }
