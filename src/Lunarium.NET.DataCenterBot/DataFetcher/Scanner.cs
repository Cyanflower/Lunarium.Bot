// .NET 核心库
using System.Collections.Concurrent;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Database;
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataFetcher;

/// <summary>
/// 负责扫描指定服务器和频道中的帖子(Thread)。
/// </summary>
public class Scanner
{
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    private readonly DiscordSocketClient _discordSocketClient;
    private HashSet<ulong> ScanChannelIDs => _configLoader.Config.GuildsConfig.ScanChannelIDs.Values.SelectMany(list => list).ToHashSet();
    private HashSet<ulong> DeployGuildIds => _configLoader.Config.GuildsConfig.DeployGuildIds.ToHashSet();
    public Scanner(ILogger logger, ConfigLoader configLoader, DiscordSocketClient discordSocketClient)
    {
        _logger = logger.ForContext("Scanner");
        _configLoader = configLoader;
        _discordSocketClient = discordSocketClient;
    }

    public async Task Sync()
    {

        _logger.Info("扫描任务开始.");
    }

    private async Task<List<SocketForumChannel>> GetAllForumChannel()
    {
        List<SocketForumChannel> socketForumChannels = new();
        foreach (var guildId in DeployGuildIds)
        {
            var guild = _discordSocketClient.GetGuild(guildId);
            if (guild != null)
            {
                foreach (var channel in guild.Channels)
                {
                    if (channel is SocketForumChannel forumChannel && channel.GetType() == typeof(SocketForumChannel))
                    {
                        socketForumChannels.Add(forumChannel);
                    }
                }
            }
        }
    }

    private async Task<List<SocketThreadChannel>> GetAllSocketThreads()
    {
        _logger.Info("扫描任务开始.");
    }
}