using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunarium.Logging;
using Microsoft.Extensions.Options;

namespace Lunarium.Bot.AI.DeepSeek;

/// <summary>
///     DeepSeek Responses API 的单次推理实现。
/// </summary>
internal sealed class DeepSeekResponsesClient : IAiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly IAiKeyProvider _keyProvider;
    private readonly ILogger _logger;

    public DeepSeekResponsesClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AiOptions> options,
        IAiKeyProvider keyProvider,
        ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _keyProvider = keyProvider;
        _logger = logger.ForContext("Lunarium.AI");
    }

    public bool IsAvailable => _keyProvider.GetApiKey() is not null;

    public async Task<AiCompletion> CompleteAsync(
        AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.CurrentValue;
        var apiKey = _keyProvider.GetApiKey()
                     ?? throw new AiNotConfiguredException(
                         $"未配置 AI API Key。请设置环境变量 {options.ApiKeyEnvironmentVariable}，或在 AiOptions.ApiKey / 运行期设置中指定。");

        Validate(request, options);

        var input = request.Input;
        var model = string.IsNullOrWhiteSpace(request.Model) ? options.DefaultModel : request.Model;
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            BuildRequest(request, model), AiJsonContext.Relaxed.ResponsesRequest);
        var endpoint = BuildEndpoint(options.BaseUrl);
        using var httpClient = _httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        var stopwatch = Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(options.RequestTimeout);

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new ByteArrayContent(payload),
                };
                httpRequest.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var httpResponse = await httpClient
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                var body = await httpResponse.Content
                    .ReadAsStringAsync(timeoutCts.Token);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var error = AiErrorMapper.Map((int)httpResponse.StatusCode, body);
                    if (!error.IsRetryable || attempt >= options.MaxRetries)
                    {
                        throw error;
                    }

                    var delay = DelayFor(options, attempt);
                    _logger.Warning(
                        "AI 请求失败（HTTP {Status}），{DelayMs}ms 后重试（{Attempt}/{MaxRetries}）。",
                        error.StatusCode, (int)delay.TotalMilliseconds, attempt + 1, options.MaxRetries);

                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var completion = ParseResponse(body, request, model, stopwatch.Elapsed);
                LogSuccess(completion, request, input);
                return completion;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= options.MaxRetries)
                {
                    throw new AiTransientException($"AI 请求超时（{options.RequestTimeout.TotalSeconds:0} 秒）。");
                }

                _logger.Warning(
                    "AI 请求超时，重试（{Attempt}/{MaxRetries}）。", attempt + 1, options.MaxRetries);

                await Task.Delay(DelayFor(options, attempt), cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= options.MaxRetries)
                {
                    throw new AiTransientException("AI 请求网络异常。", ex);
                }

                _logger.Warning(
                    ex, "AI 请求网络异常，重试（{Attempt}/{MaxRetries}）。", attempt + 1, options.MaxRetries);

                await Task.Delay(DelayFor(options, attempt), cancellationToken);
            }
        }
    }

    // ─────────────────────── 请求构造 ───────────────────────

    private static void Validate(AiCompletionRequest request, AiOptions options)
    {
        if (string.IsNullOrWhiteSpace(request.Input))
        {
            throw new ArgumentException("Input 不能为空。", nameof(request));
        }

        if (request.Input.Length > options.MaxInputCharacters)
        {
            throw new AiInputTooLargeException(request.Input.Length, options.MaxInputCharacters);
        }

        if (request.MaxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxOutputTokens 必须大于 0。");
        }

        if (request.Temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Temperature 必须在 0 到 2 之间。");
        }
    }

    private static ResponsesRequest BuildRequest(AiCompletionRequest request, string model) => new()
    {
        Model = model,
        Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions,
        Input =
        [
            new InputItem
            {
                Content = [new InputContent { Text = request.Input }],
            },
        ],
        Reasoning = new ReasoningOptions { Effort = ToWire(request.Effort) },
        Text = new TextOptions { Format = ToWire(request.Output) },
        MaxOutputTokens = request.MaxOutputTokens,
        Temperature = request.Temperature,
    };

    private static string ToWire(AiReasoningEffort effort) => effort switch
    {
        AiReasoningEffort.Low => "low",
        AiReasoningEffort.High => "high",
        AiReasoningEffort.Max => "max",
        _ => "none",
    };

    private static TextFormat ToWire(AiOutputFormat format) =>
        format.Kind == AiOutputFormatKind.JsonSchema
            ? new TextFormat
            {
                Type = "json_schema",
                Name = format.SchemaName,
                Schema = format.Schema,
            }
            : new TextFormat { Type = "text" };

    private static Uri BuildEndpoint(Uri baseUrl)
    {
        // 保证 BaseUrl 以 '/' 结尾，否则相对路径会吃掉最后一段（例如 /v1）。
        var text = baseUrl.OriginalString;
        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        return new Uri(new Uri(text), "responses");
    }

    // ─────────────────────── 响应解析 ───────────────────────

    private static AiCompletion ParseResponse(
        string body, AiCompletionRequest request, string requestedModel, TimeSpan elapsed)
    {
        ResponsesResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(body, AiJsonContext.Relaxed.ResponsesResponse);
        }
        catch (JsonException ex)
        {
            throw new AiResponseFormatException("无法解析 AI 响应。", ex.Message);
        }

        if (parsed is null)
        {
            throw new AiResponseFormatException("AI 响应为空。");
        }

        if (string.Equals(parsed.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new AiRequestFailedException(
                parsed.Error?.Message ?? "模型调用失败。", parsed.Error?.Code, AiErrorMapper.Snippet(body));
        }

        var text = ExtractText(parsed);
        var finishReason = ResolveFinishReason(parsed);

        JsonElement? structured = null;
        if (request.Output.Kind == AiOutputFormatKind.JsonSchema)
        {
            if (finishReason == AiFinishReason.MaxOutputTokens)
            {
                throw new AiResponseFormatException(
                    "模型输出被 max_output_tokens 截断，结构化结果不完整。", text);
            }

            if (finishReason == AiFinishReason.ContentFilter)
            {
                throw new AiResponseFormatException("模型输出被内容过滤拦截。", text);
            }

            structured = ParseStructured(text);
        }

        var usage = parsed.Usage;

        return new AiCompletion
        {
            Text = text,
            Structured = structured,
            Usage = new AiUsage
            {
                InputTokens = usage?.InputTokens ?? 0,
                CachedInputTokens = usage?.InputTokensDetails?.CachedTokens ?? 0,
                OutputTokens = usage?.OutputTokens ?? 0,
                ReasoningTokens = usage?.OutputTokensDetails?.ReasoningTokens ?? 0,
            },
            Model = parsed.Model ?? requestedModel,
            ResponseId = parsed.Id,
            FinishReason = finishReason,
            Duration = elapsed,
        };
    }

    private static string ExtractText(ResponsesResponse response)
    {
        if (response.Output is not { Count: > 0 } output)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in output)
        {
            if (!string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase)
                || item.Content is null)
            {
                continue;
            }

            foreach (var part in item.Content)
            {
                if (string.Equals(part.Type, "output_text", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(part.Text))
                {
                    builder.Append(part.Text);
                }
            }
        }

        return builder.ToString();
    }

    private static AiFinishReason ResolveFinishReason(ResponsesResponse response)
    {
        if (string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return AiFinishReason.Completed;
        }

        if (!string.Equals(response.Status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            return AiFinishReason.Other;
        }

        return response.IncompleteDetails?.Reason?.ToLowerInvariant() switch
        {
            "max_output_tokens" => AiFinishReason.MaxOutputTokens,
            "content_filter" => AiFinishReason.ContentFilter,
            _ => AiFinishReason.Other,
        };
    }

    private static JsonElement ParseStructured(string text)
    {
        var json = StripCodeFence(text.Trim());
        if (json.Length == 0)
        {
            throw new AiResponseFormatException("模型返回了空的 JSON 输出。");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AiResponseFormatException("模型返回的内容不是合法 JSON。", json);
        }
    }

    /// <summary>容忍模型把 JSON 包在 markdown 代码块里。</summary>
    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineEnd = text.IndexOf('\n');
        if (firstLineEnd < 0)
        {
            return text;
        }

        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence <= firstLineEnd ? text : text[(firstLineEnd + 1)..lastFence].Trim();
    }

    // ─────────────────────── 杂项 ───────────────────────

    private static TimeSpan DelayFor(AiOptions options, int attempt)
    {
        var backoff = options.RetryBaseDelay * Math.Pow(2, attempt);
        return backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
    }

    private void LogSuccess(AiCompletion completion, AiCompletionRequest request, string input)
    {
        // 工单内容是用户隐私：只记录长度与哈希，不记录原文。
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..8];

        _logger.Info(
            "AI 调用完成: model={Model} effort={Effort} finish={Finish} in={InputTokens} cached={CachedTokens} out={OutputTokens} reasoning={ReasoningTokens} len={Length} hash={Hash} took={Took}s",
            completion.Model,
            request.Effort,
            completion.FinishReason,
            completion.Usage.InputTokens,
            completion.Usage.CachedInputTokens,
            completion.Usage.OutputTokens,
            completion.Usage.ReasoningTokens,
            input.Length,
            hash,
            completion.Duration.TotalSeconds.ToString("0.00"));
    }
}
