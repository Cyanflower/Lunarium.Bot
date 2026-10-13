// NuGet 包依赖
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

// 解决方案内引用项目
using Lunarium.Bot.Logger;

// 项目内命名空间
using Lunarium.Bot.SearchBot.AuthUtils;
using Lunarium.Bot.SearchBot.Utils;
using Lunarium.Bot.SearchBot.Models;
using Lunarium.Bot.SearchBot.Paginator;
using Lunarium.Bot.SearchBot.ApplicationServices;

namespace Lunarium.Bot.SearchBot.AppInteractionService;

public class ComponentUtils
{
    public readonly ILogger _logger;

    public ComponentUtils(ILogger logger)
    {
        // 直接使用父上下文
        _logger = logger;
    }

    public MessageComponent ErrorComponente(ServiceResult result, string title)
    {
        return BuildResultComponent(result.Status,result.Message, title);
    }

    public MessageComponent ErrorComponente<T>(ServiceResult<T> result, string title) where T : class
    {
        return BuildResultComponent(result.Status,result.Message, title);
    }

    public MessageComponent ExpiredComponente()
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Red)
                .WithTextDisplay("## 该交互已过期\n请使用命令发起新的交互")
            )
            .Build();
    }
    public MessageComponent TextErrorComponente(string content)
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Red)
                .WithTextDisplay(content)
            )
            .Build();
    }

    public MessageComponent OutBoundComponente(string title, string parameters)
    {
        string errorId = CorrelationIdGenerator.Generate();
        _logger.Error("{errorId}: title view.GetItemByIndex(indexNum) 发生数组越界, 原始参数:{parameters}",
            errorId, title, parameters);
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Red)
                .WithTextDisplay($"## {title}", 1000)
                .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
            )
            .Build();
    }

    public MessageComponent LoggerErrorComponente(string title, string logErrMsg, string? parameters = null)
    {
        string errorId = CorrelationIdGenerator.Generate();

        if (parameters is null) _logger.Error("{errorId}: {logErrMsg}, 无参数",
            errorId, logErrMsg);
        else _logger.Error("{errorId}: {logErrMsg}, 原始参数:{parameters}",
            errorId, logErrMsg, parameters);
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Red)
                .WithTextDisplay($"## {title}", 1000)
                .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
            )
            .Build();
    }

    private MessageComponent BuildResultComponent(ServiceStatus status, string? message, string title)
    {

        switch (status)
        {
            case ServiceStatus.Success:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            case ServiceStatus.Conflict:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            case ServiceStatus.Exist:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            case ServiceStatus.InvalidInput:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            case ServiceStatus.NotFound:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Orange)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            case ServiceStatus.InternalError:
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay(message, 1001)
                    )
                    .Build();
            default:
                string errorId = CorrelationIdGenerator.Generate();
                _logger.Error("{errorId}: {title}, 意外的枚举: {result.Status}(result.StatusNum)",
                    errorId, title, status.ToString(), (int)status);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"## {title}", 1000)
                        .WithTextDisplay($"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```", 1001)
                    )
                    .Build();
        }
    }
}