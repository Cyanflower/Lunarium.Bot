using System.Net;

using Discord;
using Discord.Net;

using LunariumBot.Net.Extensions.CacheStore;
using Lunarium.Logging;

namespace LunariumBot.Net.Archive.Archiver;

/// <summary>
/// 「服务器 → 状态报告消息」账本。
/// 把状态报告的消息 id 持久化到 <see cref="CacheStore"/>，使进程重启后能<b>认领</b>既有报告
/// （而不是再发一条、把旧报告变成孤儿），并为「消息被删除」提供自愈依据。
/// </summary>
/// <remarks>
/// CacheStore 只支持标量，因此本类只负责 id 的持久化；运行时的 <see cref="IUserMessage"/> 句柄
/// 仍由 <c>ArchiverService</c> 在内存中缓存。
/// <para>
/// 键的类型必须与读取类型严格一致（此处统一为 <see cref="ulong"/>，落在 UnsignedNumber 桶），
/// 否则 <see cref="CacheStore.Get{T}"/> 会因桶不匹配而静默返回默认值。
/// </para>
/// </remarks>
public sealed class GuildLogMessageRegistry
{
    /// <summary>状态频道 id（全局唯一，用于识别频道配置变更）。</summary>
    private const string ChannelKey = "status:channelId";

    private readonly CacheStore _store;
    private readonly ILogger _logger;

    public GuildLogMessageRegistry(CacheStore store, ILogger logger)
    {
        _store = store;
        _logger = logger.ForContext("GuildLogMessageRegistry");
    }

    private static string MessageKey(ulong guildId) => $"status:messageId:{guildId}";

    /// <summary>记录状态频道 id（幂等：值未变时不标脏，避免每个周期都产生一次落盘）。</summary>
    public void RememberChannel(ulong channelId)
    {
        if (_store.Get<ulong>(ChannelKey) == channelId)
        {
            return;
        }

        _store.Set(ChannelKey, channelId);
        _logger.Info("已记录状态频道: {channelId}", channelId);
    }

    /// <summary>记录某服务器的状态报告消息 id。</summary>
    public void RememberMessage(ulong guildId, ulong messageId)
    {
        _store.Set(MessageKey(guildId), messageId);
        _logger.Info("已记录服务器 {guildId} 的状态报告消息: {messageId}", guildId, messageId);
    }

    /// <summary>清除某服务器的状态报告记录（消息已删 / 频道已变更时使用）。</summary>
    public void Forget(ulong guildId)
    {
        if (_store.Remove(MessageKey(guildId)))
        {
            _logger.Info("已清除服务器 {guildId} 的状态报告记录", guildId);
        }
    }

    /// <summary>读取账本；频道或消息任一未记录时返回 <see langword="false"/>。</summary>
    public bool TryGetIds(ulong guildId, out ulong channelId, out ulong messageId)
    {
        channelId = _store.Get<ulong>(ChannelKey);
        messageId = _store.Get<ulong>(MessageKey(guildId));
        return channelId != 0 && messageId != 0;
    }

    /// <summary>
    /// 按账本认领既有状态报告消息。
    /// 未记录 / 频道已变更 / 消息已被删除时返回 <see langword="null"/>（并清理无效记录），由调用方重新发送。
    /// </summary>
    /// <param name="guildId">服务器 ID</param>
    /// <param name="channel">当前配置的状态频道</param>
    public async Task<IUserMessage?> TryClaimAsync(ulong guildId, IMessageChannel channel)
    {
        if (!TryGetIds(guildId, out var channelId, out var messageId))
        {
            return null;
        }

        if (channelId != channel.Id)
        {
            _logger.Warning("服务器 {guildId} 的状态频道已变更 ({oldChannelId} -> {newChannelId})，将重新发送报告",
                guildId, channelId, channel.Id);
            Forget(guildId);
            RememberChannel(channel.Id);
            return null;
        }

        try
        {
            var message = await channel.GetMessageAsync(messageId);
            if (message is IUserMessage userMessage && userMessage.Id == messageId)
            {
                _logger.Info("已认领服务器 {guildId} 的既有状态报告消息: {messageId}", guildId, messageId);
                return userMessage;
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // 10008 Unknown Message / 404：消息已被删除，走下面的失效清理
        }

        _logger.Warning("服务器 {guildId} 的状态报告消息 {messageId} 已不存在，将重新发送", guildId, messageId);
        Forget(guildId);
        return null;
    }
}
