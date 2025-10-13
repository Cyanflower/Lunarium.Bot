// .NET 核心库
using System.Collections.Concurrent;
using System.Net;

// NuGet 包依赖
using Discord;
using Discord.WebSocket;
using SixLabors.ImageSharp;

// 项目内命名空间
using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.DataProcess;

/// <summary>
/// DataProcessService 的部分类，专门负责论坛帖子的处理逻辑。
/// </summary>
public partial class DataProcessService
{
    #region 帖子处理
    /// <summary>
    /// 创建新的论坛帖子并将其数据保存到数据库。
    /// 该方法会获取帖子的首楼消息、附件、标签等完整信息，并构建 ThreadData 对象。
    /// 如果首楼消息不存在，则标记该帖子为无效。
    /// </summary>
    /// <param name="socketThreadChannel">Discord 帖子频道对象。</param>
    public async Task CreateThreadProcessAsync(SocketThreadChannel socketThreadChannel)
    {
        // 前置检查：确保父频道是论坛频道
        if (socketThreadChannel.ParentChannel is not SocketForumChannel) return;
        
        var parentChannel = (SocketForumChannel)socketThreadChannel.ParentChannel;

        _logger.Debug("开始处理帖子创建: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);

        IMessage? firstMessage = null;
        // 使用重试策略从 Discord API 获取首楼消息内容
        try
        {
            await _semaphore.WaitAsync();
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息（帖子ID等于首楼消息ID）
                firstMessage = await socketThreadChannel.GetMessageAsync(socketThreadChannel.Id, _requestOptions);
            });
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "❌ 获取首楼消息失败: 帖子 {ThreadName} (ID: {ThreadId})",
                socketThreadChannel.Name, socketThreadChannel.Id);
        }
        finally
        {
            _semaphore.Release();
        }
        // 获取首楼消息的附件
        string? attachmentUrl = null;
        if (firstMessage != null)
        {
            var imageAttachment = firstMessage.Attachments.FirstOrDefault(a => IsImageAttachment(a));
            attachmentUrl = imageAttachment?.Url;
        }
        // 获得帖子标签
        var appliedTagNames = parentChannel.Tags
            .Where(tag => socketThreadChannel.AppliedTags.Contains(tag.Id))
            .Select(tag => tag.Name)
            .ToList();
        var tags = string.Join(", ", appliedTagNames);
        // 构建帖子标签数据
        List<TagInfo> tagInfos = parentChannel.Tags
            .Select(tag => new TagInfo
            {
                TagId = tag.Id,
                TagName = tag.Name,
                ChannelId = parentChannel.Id,
                ChannelName = parentChannel.Name,
                GuildName = parentChannel.Guild.Name,
            })
            .ToList();
        // 构建数据
        var threadData = new ThreadData()
        {
            ThreadId = socketThreadChannel.Id,
            FirstMessageId = socketThreadChannel.Id,
            AuthorId = socketThreadChannel.Owner.Id,
            ChannelId = socketThreadChannel.ParentChannel.Id,
            ChannelName = socketThreadChannel.ParentChannel.Name,
            GuildId = socketThreadChannel.Guild.Id,
            GuildName = socketThreadChannel.Guild.Name,
            CreatedAt = socketThreadChannel.CreatedAt,
            MemberCount = socketThreadChannel.MemberCount,
            ReactionCount = 0,
            ReplyCount = socketThreadChannel.MessageCount,
            LastActiveAt = socketThreadChannel.CreatedAt,
            JumpUrl = $"https://discord.com/channels/{socketThreadChannel.Guild.Id}/{socketThreadChannel.Id}/0",
            AttachmentUrl = attachmentUrl,
            FirstMessageContent = firstMessage?.Content,

            // 更新相关
            UpdateAt = null,
            UpdateJumpUrl = null,
            UpdateCount = 0,

            // Tag
            Tags = tags,
            TagsId = socketThreadChannel.AppliedTags.ToList(),

            // API请求成功, 但API返回了NULL, 此消息不存在, 那么这个帖子是个无首楼帖子
            IsValid = firstMessage != null,
            IsDeleted = false
        };
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.ForumThreadToDBAsync(threadData, tagInfos);
            });

            _logger.Info("✅ 成功创建帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 有效性: {IsValid}",
                socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsValid);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 创建帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    /// <summary>
    /// 更新论坛帖子的信息（如标题、标签、锁定状态等）。
    /// 该方法仅更新帖子的元数据，不涉及消息内容。
    /// </summary>
    /// <param name="socketThreadChannel">Discord 帖子频道对象。</param>
    public async Task UpdateThreadProcessAsync(SocketThreadChannel socketThreadChannel)
    {
        // 前置检查：确保父频道是论坛频道
        if (socketThreadChannel.ParentChannel is not SocketForumChannel) return;
        var parentChannel = (SocketForumChannel)socketThreadChannel.ParentChannel;

        _logger.Debug("开始处理帖子更新: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        // 获得帖子标签
        var appliedTagNames = parentChannel.Tags
            .Where(tag => socketThreadChannel.AppliedTags.Contains(tag.Id))
            .Select(tag => tag.Name)
            .ToList();
        var tags = string.Join(", ", appliedTagNames);
        // 构建帖子标签数据
        List<TagInfo> tagInfos = parentChannel.Tags
            .Select(tag => new TagInfo
            {
                TagId = tag.Id,
                TagName = tag.Name,
                ChannelId = parentChannel.Id,
                ChannelName = parentChannel.Name,
                GuildName = parentChannel.Guild.Name,
            })
            .ToList();
        // 构建数据
        var threadData = new ThreadData()
        {
            // 标题更新
            Title = socketThreadChannel.Name,
            TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
            // Tag
            Tags = tags,
            TagsId = socketThreadChannel.AppliedTags.ToList(),
            // 是否锁定
            IsValid = socketThreadChannel.IsLocked,
            IsLocked = socketThreadChannel.IsLocked,
        };
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.ForumThreadUpdateToDBAsync(threadData, tagInfos);
            });

            _logger.Info("✅ 成功更新帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 锁定状态: {IsLocked}",
                socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsLocked);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 更新帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    /// <summary>
    /// 更新论坛帖子首楼消息的反应（Reaction）计数。
    /// 该方法会获取首楼消息的所有反应，并找出最高的反应数量更新到数据库。
    /// </summary>
    /// <param name="socketThreadChannel">Discord 帖子频道对象。</param>
    public async Task UpdateTheradReactionProcessAsync(SocketThreadChannel socketThreadChannel)
    {
        _logger.Debug("开始更新帖子反应计数: 帖子 {ThreadName} (ID: {ThreadId})",
            socketThreadChannel.Name, socketThreadChannel.Id);

        IMessage? firstMessage = null;
        // 使用重试策略从 Discord API 获取首楼消息内容
        try
        {
            await _semaphore.WaitAsync();
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息（帖子ID等于首楼消息ID）
                firstMessage = await socketThreadChannel.GetMessageAsync(socketThreadChannel.Id, _requestOptions);
            });
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "❌ 获取首楼消息失败，无法更新反应计数: 帖子 {ThreadName} (ID: {ThreadId})",
                socketThreadChannel.Name, socketThreadChannel.Id);
            return;
        }
        finally
        {
            _semaphore.Release();
        }

        // 计算最高反应数
        int highestReactionCount = 0;
        if (firstMessage != null)
        {
            highestReactionCount = firstMessage.Reactions.Values
                .Select(r => r.ReactionCount)
                .DefaultIfEmpty(0)
                .Max();
        }

        _logger.Debug("帖子 {ThreadName} (ID: {ThreadId}) 的最高反应数: {ReactionCount}",
            socketThreadChannel.Name, socketThreadChannel.Id, highestReactionCount);

        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.UpdateThreadReactionToDBAsync(socketThreadChannel.Id, highestReactionCount);
            });

            _logger.Info("✅ 成功更新帖子反应计数: 帖子 {ThreadName} (ID: {ThreadId}), 反应数: {ReactionCount}",
                socketThreadChannel.Name, socketThreadChannel.Id, highestReactionCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 更新帖子反应计数失败: 帖子 {ThreadName} (ID: {ThreadId})",
                socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    /// <summary>
    /// 软删除论坛帖子。
    /// 该方法不会从数据库中物理删除帖子记录，而是将其标记为已删除状态。
    /// </summary>
    /// <param name="threadId">要删除的帖子ID。</param>
    public async Task DeleteThreadProcessAsync(ulong threadId)
    {
        _logger.Info("开始软删除帖子 (ID: {ThreadId})", threadId);

        try
        {
            await _databaseService.SoftDeleteThreadAsync(threadId);
            _logger.Info("✅ 成功软删除帖子 (ID: {ThreadId})", threadId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 软删除帖子失败 (ID: {ThreadId})", threadId);
        }
    }
    #endregion

    #region 辅助方法
    /// <summary>
    /// 判断 Discord 消息附件是否为图片类型。
    /// 支持的图片格式包括: jpg, jpeg, png, gif, webp, bmp。
    /// </summary>
    /// <param name="attachment">Discord 消息附件对象。</param>
    /// <returns>如果是图片类型返回 true，否则返回 false。</returns>
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
    #endregion
}