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

public partial class DataProcessService
{
    #region 帖子处理
    public async Task CreateThreadProcessAsync(SocketThreadChannel socketThreadChannel)
    {
        if (socketThreadChannel.ParentChannel is not SocketForumChannel) return;
        var parentChannel = (SocketForumChannel)socketThreadChannel.ParentChannel;
        IMessage? firstMessage = null;
        // 使用重试策略进行尝试从API获取首楼消息内容
        try
        {
            await _semaphore.WaitAsync();
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息
                firstMessage = await socketThreadChannel.GetMessageAsync(socketThreadChannel.Id, _requestOptions);
            });
        }
        catch (Exception ex)
        {
            _logger.Critical(ex, "");
            // 即使如此仍然无法获得首楼消息
            // 放弃, 并将该帖子加入等待处理列表, 以期在 UpdateThread 中处理
            return;
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CreateThreadProcessAsync");
        }
    }

    public async Task UpdateThreadProcessAsync(SocketThreadChannel socketThreadChannel)
    {
        if (socketThreadChannel.ParentChannel is not SocketForumChannel) return;
        var parentChannel = (SocketForumChannel)socketThreadChannel.ParentChannel;
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CreateThreadProcessAsync");
        }
    }

    public async Task DeleteThreadProcessAsync(ulong threadId)
    {
        await _databaseService.SoftDeleteThreadAsync(threadId);
    }
    #endregion

    #region 辅助方法
    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
    #endregion
}