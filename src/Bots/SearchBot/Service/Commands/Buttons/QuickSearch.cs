using Microsoft.Extensions.Logging;
using System.Text.Json;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.Scheduler;
using SearchBot.Utils;
using SearchBot.Model;

namespace SearchBot.AppInteractionService;

public partial class Buttons : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("qs_execute_*", true)]
    public async Task HandleQuickSearchExecuteSelect(ulong userId, string[] selectedValues)
    {
        if (userId != Context.User.Id)
        {
            await RespondAsync("这是别人的菜单哦！", ephemeral: true);
            return;
        }

        var commandName = selectedValues.First();
        await DeferAsync(ephemeral: true);

        var quickSearchCommands = await _databaseService.GetUserQuickSearchesAsync(userId);
        var commandToExecute = quickSearchCommands.FirstOrDefault(c => c.CommandName == commandName);

        if (commandToExecute == null)
        {
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("执行失败", "找不到这个快捷命令了，可能已经被删除了哦。"), ephemeral: true);
            return;
        }

        try
        {
            var criteria = JsonSerializer.Deserialize<SearchCriteria>(commandToExecute.CriteriaJson);
            if (criteria == null)
            {
                await FollowupAsync(embed: EmbedUtils.FailedEmbed("执行失败", "无法解析保存的搜索数据。"), ephemeral: true);
                return;
            }
            
            // 复用 AppCommands 中的搜索执行与显示逻辑
            await SearchUtils.ExecuteAndDisplaySearchAsync(_logger, _databaseService, _paginatorManager, criteria, Context.Interaction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行快捷搜索 {CommandName} 时出错", commandName);
            await FollowupAsync(embed: EmbedUtils.FailedEmbed("执行失败", "执行快捷搜索时发生了意想不到的错误。"), ephemeral: true);
        }
    }

    [ComponentInteraction("qs_delete_select_*", true)]
    public async Task HandleQuickSearchDeleteSelect(ulong userId, string[] selectedValues)
    {
        if (userId != Context.User.Id)
        {
            await RespondAsync("这是别人的菜单哦！", ephemeral: true);
            return;
        }
        
        var commandName = selectedValues.First();
        
        var quickSearchCommands = await _databaseService.GetUserQuickSearchesAsync(userId);
        var commandToDelete = quickSearchCommands.FirstOrDefault(c => c.CommandName == commandName);

        if (commandToDelete == null)
        {
             await (Context.Interaction as SocketMessageComponent)!.UpdateAsync(props =>
            {
                props.Embed = EmbedUtils.FailedEmbed("操作失败", "找不到这个快捷命令了，可能已经被删除了哦。");
                props.Components = new ComponentBuilder().Build();
            });
            return;
        }

        var criteria = JsonSerializer.Deserialize<SearchCriteria>(commandToDelete.CriteriaJson);
        var commandString = SearchParameterMapper.ToCommandString(criteria);

        var embed = new EmbedBuilder()
            .WithTitle("⚠️ 确认删除")
            .WithDescription($"您确定要删除快捷命令 **{commandName}** 吗？\n\n该命令的内容如下：\n```{commandString}```")
            .WithColor(Color.Orange)
            .Build();

        var components = new ComponentBuilder()
            .WithButton("确认删除", $"qs_delete_confirm_{userId}_{commandName}", ButtonStyle.Danger)
            .WithButton("取消", "general_cancel", ButtonStyle.Secondary)
            .Build();

        await (Context.Interaction as SocketMessageComponent)!.UpdateAsync(props =>
        {
            props.Embed = embed;
            props.Components = components;
        });
    }

    [ComponentInteraction("qs_delete_confirm_*_*", true)]
    public async Task HandleQuickSearchDeleteConfirm(ulong userId, string commandName)
    {
        if (userId != Context.User.Id)
        {
            await RespondAsync("这是别人的按钮哦！", ephemeral: true);
            return;
        }

        var affectedRows = await _databaseService.RemoveQuickSearchAsync(userId, commandName);

        if (affectedRows > 0)
        {
             await (Context.Interaction as SocketMessageComponent)!.UpdateAsync(props =>
            {
                props.Embed = EmbedUtils.SuccessEmbed($"快捷命令 **{commandName}** 已成功删除！");
                props.Components = new ComponentBuilder().Build();
            });
        }
        else
        {
            await (Context.Interaction as SocketMessageComponent)!.UpdateAsync(props =>
            {
                props.Embed = EmbedUtils.FailedEmbed("删除失败", "在数据库中没有找到这个命令，可能已经被其他操作移除了。");
                props.Components = new ComponentBuilder().Build();
            });
        }
    }
}