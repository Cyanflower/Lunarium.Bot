// .NET 核心库
using System.Text;
using System.Text.Json;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Discord.Interactions;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.ApplicationServices;
using LunariumBot.Net.ReleaseBot.Utils;
using LunariumBot.Net.ReleaseBot.TokenBucketServices;
using LunariumBot.Net.ReleaseBot.CacheServices;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.ReleaseBot.AppInteractionService;

public class AssistBotInteraction : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    public AssistBotInteraction(
        ILogger logger,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
    {
        _logger = logger.ForContext("GetRelease");
        _httpRetryPolicy = httpRetryPolicy;
    }

    [ComponentInteraction("CreateRemark")]
    public async Task HandleCreateRemarkAsync()
    {

        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [CreateRemark] 失败 - Context.Interaction 不是 SocketMessageComponent。User: {UserId}", Context.User.Id);
            return;
        }
        try
        {
            
            var modal = new ModalBuilder()
                .WithTitle("输入备注")
                .WithCustomId($"CreateRemarkModal")
                .AddTextInput(
                    label: "备注:",
                    customId: "Input",
                    placeholder: "备注内容...",
                    maxLength: 500,
                    minLength: 1,
                    required: true,
                    style: TextInputStyle.Paragraph
                );

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await RespondWithModalAsync(modal.Build());
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }
    
    [ModalInteraction("CreateRemarkModal")]
    public async Task HandleCreateRemarkModalAsync(TextInputModal modal)
    {
        if (Context.Interaction is not IModalInteraction modalInteraction)
        {
            _logger.Warning("交互回调: [CreateRemarkModal] 失败 - Context.Interaction 不是 IModalInteraction。User: {UserId}", Context.User.Id);
            return;
        }
        try
        {
            var oldComponent = modalInteraction.Message.Components;
            var newComponents = new ComponentBuilderV2(oldComponent);
            var remarkComponent = newComponents.FindComponentById<TextDisplayBuilder>(1200);
            var remark = modal.Text.Trim();
            if (remarkComponent is not null)
            {
                remarkComponent.WithContent($"""
                    ### 备注
                    > -# 可在右上角搜索备注中的内容进行快速索引本条消息
                    {remark}
                    """);
            }
            await modalInteraction.UpdateAsync(msg => msg.Components = newComponents.Build());
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }
}

