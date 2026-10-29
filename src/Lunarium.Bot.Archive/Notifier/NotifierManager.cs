using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Lunarium.Bot.Archive.Archiver;
using Lunarium.Bot.Archive.Model;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.RateLimiting;
using Lunarium.Common.Caching;
using Lunarium.Logging;
using Polly.Retry;

namespace Lunarium.Bot.Archive.Notifier;

/// <summary>
/// 归档状态报告：每轮归档为每个部署服务器维护一条状态消息。
/// <para>
/// 消息发往全局 <c>StatusChannelId</c>；消息 id 持久化在 <see cref="CacheStore"/>，
/// 重启后按账本<b>认领</b>既有消息并原地编辑，避免每轮/每次重启都新发一条。
/// </para>
/// </summary>
public sealed class NotifierManager : IAsyncDisposable
{
    private const string StatusChannelKey = "status:channelId";

    private static string StatusMessageKey(ulong guildId) => $"status:messageId:{guildId}";

    private readonly ILogger _logger;
    private readonly ConfigLoader<ArchiveConfig> _configLoader;
    private readonly DiscordSocketClient _client;
    private readonly CacheStore _statusStore;
    private readonly RateLimitedExecutor _rateLimitedExecutor;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly Dictionary<ulong, StatusMessageRef> _statusMessages = new();
    private readonly ConcurrentDictionary<ulong, IMessageChannel> _channelCache = new(); 
    private IMessageChannel? _statusChannel;
    private bool _started;

    public NotifierManager(
        ILogger logger,
        ConfigLoader<ArchiveConfig> configLoader,
        DiscordSocketClient client,
        RateLimitedExecutor rateLimitedExecutor,
        CacheStore statusStore)
    {
        _logger = logger.ForContext("Notifier");
        _configLoader = configLoader;
        _client = client;
        _statusStore = statusStore;
        _rateLimitedExecutor = rateLimitedExecutor;
        _httpRetryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(logger);
    }

    #region 生命周期

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        // 防抖队列里的最后一次写入必须落盘：否则重启后会认领不到上一条报告，
        // 把旧报告变成孤儿消息。
        await _statusStore.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    #endregion

    # region 通知消息

    /// <summary>发送到该服务器的置顶告警频道；未配置频道或发送失败时返回 <c>null</c>。</summary>
    public async Task<IUserMessage?> ToNotifierChannel(MessageComponent message, ulong guildId, CancellationToken cancellationToken = default)
    {
        if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guildId, out var guildConfig))
        {
            _logger.Warning("服务器 {GuildId} 不在部署配置中，无法发送通知", guildId);
            return null;
        }

        if (guildConfig.NotifierChannelId == 0)
        {
            _logger.Warning("服务器 {GuildId} 未配置置顶告警频道（NotifierChannelId = 0），跳过发送", guildId);
            return null;
        }

        try
        {
            var channel = await GetMessageChannel(guildConfig.NotifierChannelId, cancellationToken).ConfigureAwait(false);
            return await SendToChannel(message, channel, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "发送置顶告警失败: GuildId={GuildId}, ChannelId={ChannelId}",
                guildId, guildConfig.NotifierChannelId);
            return null;
        }
    }

    public async Task ToStatusChannel(MessageComponent message, CancellationToken cancellationToken = default)
    {
        await ToChannel(message, _configLoader.Value.DeployerConfig.StatusChannelId, cancellationToken);
    }
    
    public async Task ToChannel(MessageComponent message, ulong channelId, CancellationToken cancellationToken = default)
    {
        try
        {
            var channel = await GetMessageChannel(channelId, cancellationToken);
            await SendToChannel(message, channel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "通知发送到目标频道失败");
        }
    }
    
    public async Task<IUserMessage> SendToChannel(MessageComponent message, IMessageChannel channel, CancellationToken cancellationToken)
    {
        return await _httpRetryPolicy.ExecuteAsync(async () =>
        {
            return await _rateLimitedExecutor.WriteAndWaitAsync(async _ =>
            {
                return await channel.SendMessageAsync(components: message);
            }, cancellationToken);
        });
    }

    /// <summary>编辑既有通知消息（告警收尾用）；消息已删除或频道不可用时返回 <c>false</c>。</summary>
    public async Task<bool> TryEditAsync(ulong channelId, ulong messageId, MessageComponent message, CancellationToken cancellationToken)
    {
        try
        {
            var channel = await GetMessageChannel(channelId, cancellationToken).ConfigureAwait(false);
            var fetched = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
            if (fetched is not IUserMessage userMessage)
            {
                return false;
            }

            await userMessage.ModifyAsync(properties => properties.Components = message).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "编辑通知消息失败: ChannelId={ChannelId}, MessageId={MessageId}", channelId, messageId);
            return false;
        }
    }
    
    private async Task<IMessageChannel> GetMessageChannel(ulong channelId, CancellationToken cancellationToken)
    {
        if (_channelCache.TryGetValue(channelId, out var channel))
        {
            return channel;
        }

        IChannel? result = await _httpRetryPolicy.ExecuteAsync<IChannel?>(async () =>
        {
            return await _rateLimitedExecutor.WriteAndWaitAsync<IChannel?>(async _ =>
            {
                return await _client.GetChannelAsync(channelId);
            }, cancellationToken);
        });

        if (result is IMessageChannel messageChannel)
        {
            _channelCache[channelId] = messageChannel;
            return messageChannel;
        }
        else if (result is null)
        {
            throw new ArgumentNullException($"频道 ID 所指向的频道/线程不存在或无权访问", nameof(channelId));
        }

        throw new ArgumentException($"频道 ID 所指向的频道/线程不是一个可发消息的 IMessageChannel", nameof(channelId));
    }

    #endregion

    #region 状态报告

    public async Task InitializeStatusChannelIfNeededAsync(CancellationToken cancellationToken)
    {
        var channelId = _configLoader.Value.DeployerConfig.StatusChannelId;
        if (_statusChannel is not null && _statusChannel.Id == channelId)
        {
            return;
        }

        if (_statusChannel is not null && _statusChannel.Id != channelId)
        {
            _logger.Warning("状态频道配置已变更 ({OldChannelId} -> {NewChannelId})，重新初始化",
                _statusChannel.Id, channelId);
            _statusChannel = null;
            _statusMessages.Clear();
        }

        try
        {
            _statusChannel = await _client.GetChannelAsync(channelId).ConfigureAwait(false) as IMessageChannel;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "获取状态频道 {ChannelId} 失败", channelId);
            return;
        }

        if (_statusChannel is null)
        {
            _logger.Error("状态频道 {ChannelId} 不存在或不是可发送消息的频道", channelId);
        }
        else
        {
            _logger.Info("状态频道初始化成功: {ChannelId}", channelId);
        }
    }

    public async Task UpdateArchiveStatusReportAsync(
        GuildContext context,
        ArchiveOutcome outcome,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await InitializeStatusChannelIfNeededAsync(cancellationToken).ConfigureAwait(false);
        if (_statusChannel is null)
        {
            return;
        }

        var channel = _statusChannel;
        var embed = BuildStatusEmbed(context, outcome, duration);

        if (_statusMessages.TryGetValue(context.Guild.Id, out var cached)
            && cached.ChannelId == channel.Id)
        {
            await EditStatusMessageAsync(context.Guild.Id, channel, cached.Message, embed, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        _statusMessages.Remove(context.Guild.Id);

        var claimed = await TryClaimStatusMessageAsync(context.Guild.Id, channel, cancellationToken)
            .ConfigureAwait(false);
        if (claimed is not null)
        {
            _statusMessages[context.Guild.Id] = new StatusMessageRef(channel.Id, claimed);
            await EditStatusMessageAsync(context.Guild.Id, channel, claimed, embed, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SendStatusMessageAsync(context.Guild.Id, channel, embed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IUserMessage?> TryClaimStatusMessageAsync(
        ulong guildId,
        IMessageChannel channel,
        CancellationToken cancellationToken)
    {
        var storedChannelId = _statusStore.Get<ulong>(StatusChannelKey);
        var messageId = _statusStore.Get<ulong>(StatusMessageKey(guildId));

        if (storedChannelId == 0 || messageId == 0)
        {
            return null;
        }

        if (storedChannelId != channel.Id)
        {
            _logger.Warning("服务器 {GuildId} 的状态频道已变更 ({OldChannelId} -> {NewChannelId})，重新发送状态报告",
                guildId, storedChannelId, channel.Id);
            _statusStore.Remove(StatusMessageKey(guildId));
            RememberStatusChannel(channel.Id);
            return null;
        }

        try
        {
            var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
            if (message is IUserMessage userMessage && userMessage.Id == messageId)
            {
                _logger.Info("已认领服务器 {GuildId} 的既有状态报告消息: {MessageId}", guildId, messageId);
                return userMessage;
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // 消息已删除，走失效清理
        }

        _logger.Warning("服务器 {GuildId} 的状态报告消息 {MessageId} 已不存在，重新发送", guildId, messageId);
        _statusStore.Remove(StatusMessageKey(guildId));
        return null;
    }

    private async Task SendStatusMessageAsync(
        ulong guildId,
        IMessageChannel channel,
        Embed embed,
        CancellationToken cancellationToken)
    {
        try
        {
            RememberStatusChannel(channel.Id);
            var message = await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            _statusMessages[guildId] = new StatusMessageRef(channel.Id, message);
            _statusStore.Set(StatusMessageKey(guildId), message.Id);
            await _statusStore.FlushAsync().ConfigureAwait(false);
            _logger.Info("服务器 {GuildId} 的状态报告已发送: {MessageId}", guildId, message.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "服务器 {GuildId} 的状态报告发送失败", guildId);
        }
    }

    private async Task EditStatusMessageAsync(
        ulong guildId,
        IMessageChannel channel,
        IUserMessage message,
        Embed embed,
        CancellationToken cancellationToken)
    {
        try
        {
            await message.ModifyAsync(props => props.Embed = embed).ConfigureAwait(false);
            _logger.Info("服务器 {GuildId} 的状态报告已更新", guildId);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.Warning("服务器 {GuildId} 的状态报告消息已被删除，重新发送", guildId);
            _statusMessages.Remove(guildId);
            _statusStore.Remove(StatusMessageKey(guildId));
            await SendStatusMessageAsync(guildId, channel, embed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "服务器 {GuildId} 的状态报告更新失败，下一轮重试", guildId);
        }
    }

    private void RememberStatusChannel(ulong channelId)
    {
        if (_statusStore.Get<ulong>(StatusChannelKey) == channelId)
        {
            return;
        }

        _statusStore.Set(StatusChannelKey, channelId);
        _logger.Info("已记录状态频道: {ChannelId}", channelId);
    }

    private Embed BuildStatusEmbed(GuildContext context, ArchiveOutcome outcome, TimeSpan duration)
    {
        var limit = context.Config.ArchiveLimit;
        var buffer = ArchiveConfigLimits.LimitExclusiveMax - limit;
        var successful = outcome.Items.Count(x => x.Success);
        var failed = outcome.Items.Count(x => !x.Success);
        var remaining = Math.Max(0, outcome.TotalActiveThreads - successful);

        var builder = new EmbedBuilder()
            .WithTitle("🗂️ 定时归档执行报告")
            .WithFooter($"⏱️ 耗时: {duration.TotalSeconds:F2} 秒 | 间隔: {_configLoader.Value.IntervalMin} 分钟")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var description = new StringBuilder();
        description.AppendLine($"服务器: {context.Guild.Name}({context.Guild.Id})");
        description.AppendLine($"📊 **当前状态概览** (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)");
        description.AppendLine($"> 📈 活跃线程数(归档前): **{outcome.TotalActiveThreads}**");
        description.AppendLine($"> 🎯 活跃线程上限: **{limit}**");
        description.AppendLine($"> 💾 缓冲区大小: **{buffer}**");

        if (outcome.UnreadableCount > 0)
        {
            description.AppendLine($"> ❓ 无法读取最后活跃: **{outcome.UnreadableCount}** 个（本轮按最活跃处理，不会被归档）");
        }

        if (!outcome.HasExcessThreads)
        {
            builder.WithColor(Color.Green);
            description.AppendLine($"> ✅ 剩余可用额度: **{Math.Max(0, limit - outcome.TotalActiveThreads)}**，无需归档操作");
        }
        else if (outcome.Items.Count == 0)
        {
            builder.WithColor(Color.Orange);
            description.AppendLine($"> 📦 计划归档: **{outcome.PlannedCount}** 个");
            description.AppendLine("> ⚠️ 本轮没有执行归档（无候选或触发安全围栏），请检查日志");
        }
        else
        {
            builder.WithColor(failed > 0 ? Color.Red : Color.Blue);
            description.AppendLine($"> 📦 计划归档: **{outcome.PlannedCount}** 个");
            description.AppendLine($"> ✅ 归档成功: **{successful}** 个");
            description.AppendLine($"> ❌ 归档失败: **{failed}** 个");
            description.AppendLine($"> 📉 归档后预计活跃: **{remaining}**");
        }

        builder.WithDescription(description.ToString());

        var displayResults = outcome.Items.Take(24).ToList();
        for (var index = 0; index < displayResults.Count; index++)
        {
            var result = displayResults[index];
            var statusIcon = result.Success ? "✅" : "❌";
            var statusText = result.Success ? "归档成功" : "归档失败";
            var value = $"""
                > - 🔗 https://discord.com/channels/{context.Guild.Id}/{result.Thread.Id}
                > - 🕐 最后活跃: <t:{result.LastActiveAt.ToUnixTimeSeconds()}:R>
                > - 🆔 ID: `{result.Thread.Id}`
                """;

            if (!result.Success && result.Error is not null)
            {
                value += $"\n> - ⚠️ {result.Error.Message.Slice(200)}";
            }

            builder.AddField($"{statusIcon} {statusText} [{index + 1}]", value, inline: false);
        }

        if (outcome.Items.Count > displayResults.Count)
        {
            builder.AddField(
                "📄 显示限制",
                $"仅显示前 {displayResults.Count} 个结果，总共处理了 {outcome.Items.Count} 个线程",
                inline: false);
        }

        _logger.Info("[{GuildName}({GuildId})] 状态报告: 活跃={Active}, 计划归档={Planned}, 成功={Success}, 失败={Failed}, 无法读取={Unreadable}",
            context.Guild.Name, context.Guild.Id, outcome.TotalActiveThreads,
            outcome.PlannedCount, successful, failed, outcome.UnreadableCount);

        return builder.Build();
    }

    #endregion

    #region 内部结构

    private sealed record StatusMessageRef(ulong ChannelId, IUserMessage Message);

    #endregion
}
