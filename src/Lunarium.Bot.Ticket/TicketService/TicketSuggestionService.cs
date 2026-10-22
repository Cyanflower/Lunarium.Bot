using Lunarium.Bot.AI;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>
/// 用 LLM 为待审核工单生成"通过 / 拒绝"建议。
/// <para>
/// 本服务<b>永不抛出业务异常</b>：未配置 Key、超时、限流、返回格式异常都降级为 <see langword="null"/>，
/// 由审核卡片显示"未启用或生成失败"，绝不让 AI 故障阻断建单。
/// </para>
/// </summary>
public sealed class TicketSuggestionService
{
    /// <summary>单次建议的超时上限。审核卡片最多等这么久，超过就按"无建议"发送。</summary>
    private static readonly TimeSpan SuggestTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private readonly IAiClient _ai;

    public TicketSuggestionService(ILogger logger, IAiClient ai)
    {
        _logger = logger.ForContext("TicketSuggestionService");
        _ai = ai;
    }

    /// <summary>是否已配置可用的 API Key；未配置时调用方应直接跳过。</summary>
    public bool IsAvailable => _ai.IsAvailable;

    /// <summary>为一张待审核工单生成建议；失败返回 <see langword="null"/>。</summary>
    public async Task<LLMSuggestion?> SuggestAsync(TicketData ticket, CancellationToken cancellationToken = default)
    {
        if (!_ai.IsAvailable)
        {
            _logger.Warning("LLM 未配置 API Key，跳过建议生成: {Ticket}（环境变量 {Variable} 不存在且 AiOptions.ApiKey 为空）",
                ticket, "DEEPSEEK_API_KEY");
            return null;
        }

        var request = new AiCompletionRequest
        {
            // 指令恒定 → 命中 DeepSeek 前缀缓存
            Instructions = LLMSuggestion.FormatStructPrompt,
            Input = BuildInput(ticket),
            Effort = AiReasoningEffort.None,
            Temperature = 0.2,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SuggestTimeout);

        try
        {
            var suggestion = await _ai.CompleteAsAsync(
                request, LLMSuggestionJsonContext.Default.LLMSuggestion, timeoutCts.Token);

            // 模型可能把"通过"写成空字符串而不是省略字段；统一归一化为 null
            if (string.IsNullOrWhiteSpace(suggestion.RejectReason))
            {
                suggestion = suggestion with { RejectReason = null };
            }

            _logger.Info("LLM 建议已生成: {Ticket} 建议={Suggestion} 建议拒绝={HasRejectReason}",
                ticket, suggestion.Suggestion, suggestion.RejectReason is not null);

            return suggestion;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning("LLM 建议生成超时（{Timeout} 秒）: {Ticket}", SuggestTimeout.TotalSeconds, ticket);
            return null;
        }
        catch (AiException ex)
        {
            _logger.Warning("LLM 建议生成失败: {Ticket} {ErrorType}: {Message}", ticket, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    /// <summary>把工单信息拼成输入文本；易变内容只放 <c>Input</c>，不要写进 Instructions。</summary>
    private static string BuildInput(TicketData ticket)
    {
        var secondary = string.IsNullOrWhiteSpace(ticket.SecondaryContent)
            ? string.Empty
            : $"""

              --- 附加信息 ---
              {ticket.SecondaryContent}
              """;

        return $"""
            工单编号: {ticket}
            一级分类: {ticket.TicketType}
            二级分类: {ticket.TicketCategory}
            提交人: {ticket.TicketCreator.DisplayName} ({ticket.TicketCreator.UserName})
            提交时间: {ticket.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}

            Note: this is only the initial submission; the ticket channel has not been created yet.
            Evidence (screenshots, links, user IDs) is normally provided later inside the channel.

            --- 工单正文 ---
            {ticket.Content}{secondary}
            """;
    }
}
