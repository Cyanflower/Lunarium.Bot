// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataProcess;

public partial class DataProcessService
{
    public async Task CreateOrUpdateChannelProcessAsync(SocketForumChannel socketForumChannel)
    {
        var channelData = new ChannelData()
        {
            ChannelId = socketForumChannel.Id,
            ChannelName = socketForumChannel.Name,
            GuildId = socketForumChannel.Guild.Id,
            GuildName = socketForumChannel.Guild.Name
        };
        List<TagInfo> tagInfos = new();
        foreach (var tag in socketForumChannel.Tags)
        {
            tagInfos.Add(new TagInfo()
            {
                TagId = tag.Id,
                ChannelId = socketForumChannel.Id,
                TagName = tag.Name,
                ChannelName = socketForumChannel.Name,
                GuildName = socketForumChannel.Guild.Name,
                UsageCount = 0
            });
        }
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.CreateOrUpdateChannelToDB(channelData, tagInfos);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CreateOrUpdateChannelProcessAsync");
        }
    }

    public async Task DeleteChannelProcessAsync(SocketForumChannel socketForumChannel)
    {
        List<ulong> TagIds = socketForumChannel.Tags.Select(tag => tag.Id).ToList();
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.DeleteChannelToDB(socketForumChannel.Id, TagIds);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "DeleteChannelProcessAsync");
        }
    }
}