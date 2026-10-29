using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Lunarium.Bot.AI;

/// <summary>
///     无状态、单次、非流式的 AI 推理客户端。
/// </summary>
/// <remarks>
///     <para>本接口刻意只提供「一次输入、一次输出」的能力。以下能力<b>永远不属于</b>该层：</para>
///     <list type="bullet">
///         <item>会话 / 历史 / 上下文管理</item>
///         <item>工具调用、MCP、function calling</item>
///         <item>流式输出</item>
///         <item>本地 token 估算</item>
///     </list>
///     <para>需要上述能力时请使用独立的 agent 运行时，而不是扩张本接口。</para>
/// </remarks>
public interface IAiClient
{
    /// <summary>
    ///     是否已配置可用的 API Key。为 <c>false</c> 时调用会抛出 <see cref="AiNotConfiguredException" />，
    ///     调用方应先检查该属性并走降级路径。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>发起一次推理请求。</summary>
    /// <exception cref="AiNotConfiguredException">未配置 API Key。</exception>
    /// <exception cref="AiInputTooLargeException">输入超过本地字符上限。</exception>
    /// <exception cref="AiException">请求或响应失败。</exception>
    Task<AiCompletion> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IAiClient" /> 的便捷扩展。</summary>
public static class AiClientExtensions
{
    /// <summary>
    ///     按 <typeparamref name="T" /> 自动生成 JSON Schema，要求模型按该结构输出，并反序列化为 <typeparamref name="T" />。
    /// </summary>
    /// <param name="client">客户端。</param>
    /// <param name="request">请求；其 <see cref="AiCompletionRequest.Output" /> 会被覆盖。</param>
    /// <param name="typeInfo">由源生成上下文提供的类型信息（AOT 安全）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<T> CompleteAsAsync<T>(
        this IAiClient client,
        AiCompletionRequest request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var schema = AiSchema.Export(typeInfo);
        var formatted = request with
        {
            Output = AiOutputFormat.JsonSchema(AiSchema.NameFor(typeInfo.Type), schema),
        };

        var completion = await client.CompleteAsync(formatted, cancellationToken);

        if (completion.FinishReason != AiFinishReason.Completed)
        {
            throw new AiResponseFormatException(
                $"模型输出不完整（{completion.FinishReason}），无法解析为 {typeInfo.Type.Name}。",
                completion.Text);
        }

        if (completion.Structured is not { } structured)
        {
            throw new AiResponseFormatException(
                $"模型未返回可解析为 {typeInfo.Type.Name} 的结构化输出。", completion.Text);
        }

        var value = JsonSerializer.Deserialize(structured, typeInfo);
        if (value is null)
        {
            throw new AiResponseFormatException(
                $"结构化输出反序列化为 {typeInfo.Type.Name} 的结果为 null。", completion.Text);
        }

        return value;
    }
}
