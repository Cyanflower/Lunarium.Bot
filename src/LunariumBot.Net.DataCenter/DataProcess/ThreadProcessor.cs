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
    public async Task CreateForumThreadProcessAsync(SocketThreadChannel socketThreadChannel, SocketForumChannel socketForumChannel)
    {
        var isValid = true;

        _logger.Debug("开始处理帖子创建: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);

        // ==========================
        // 首楼消息处理
        // ========================== 
        IMessage? firstMessage = null;
        // 使用重试策略从 Discord API 获取首楼消息内容
        try
        {
            await _semaphore.WaitAsync();
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                // 获取首楼消息（帖子ID等于首楼消息ID）
                try
                {
                    firstMessage = await socketThreadChannel.GetMessageAsync(socketThreadChannel.Id, _requestOptions);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "获取帖子的首楼消息失败");
                }
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

        // 验证首楼消息是否有效, 有效则尝试获取附件
        string? attachmentUrl = null;
        if (firstMessage is not null)
        {
            var imageAttachment = firstMessage.Attachments.FirstOrDefault(a => IsImageAttachment(a));
            attachmentUrl = imageAttachment?.Url;
        }
        else
        {
            isValid = false;
            if (firstMessage is null) _logger.Debug("firstMessage Is null");
            else if (string.IsNullOrWhiteSpace(firstMessage.Content)) _logger.Debug("firstMessage Is Null Or WhiteSpace, Content: [{firstMessage.Content}]", firstMessage.Content);
        }

        var threadInterface = (IThreadChannel)socketThreadChannel;

        // ==========================
        // 标签处理
        // ========================== 
        // 获得帖子标签
        var appliedTags = socketForumChannel.Tags
            .Where(tag => socketThreadChannel.AppliedTags.Contains(tag.Id))
            .ToList();
        var appliedTagIds = appliedTags
            .Select(tag => tag.Id)
            .ToList();
        var appliedTagNames = appliedTags
            .Select(tag => tag.Name)
            .ToList();
        var tags = string.Join(", ", appliedTagNames);

        // ==========================
        // 构建数据模型
        // ========================== 
        var threadData = new ThreadData()
        {
            ThreadId = socketThreadChannel.Id,
            FirstMessageId = socketThreadChannel.Id,
            AuthorId = threadInterface.OwnerId,
            ChannelId = socketThreadChannel.ParentChannel.Id,
            ChannelName = socketThreadChannel.ParentChannel.Name,
            GuildId = socketThreadChannel.Guild.Id,
            GuildName = socketThreadChannel.Guild.Name,
            CreatedAt = socketThreadChannel.CreatedAt,
            ReactionCount = 0,
            ReplyCount = socketThreadChannel.MessageCount,
            LastActiveAt = socketThreadChannel.CreatedAt,
            JumpUrl = $"https://discord.com/channels/{socketThreadChannel.Guild.Id}/{socketThreadChannel.Id}/0",
            AttachmentUrl = attachmentUrl,

            Title = socketThreadChannel.Name,
            FirstMessageContent = firstMessage?.Content,

            TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
            ContentTokenized = firstMessage is not null ? _tokenizerService.CutForSearch(firstMessage.Content) : null,

            // 更新相关
            UpdatedAt = null,
            UpdatedJumpUrl = null,
            UpdateCount = 0,

            // Tag
            Tags = tags,
            TagsId = appliedTagIds,

            // 有效性
            IsValid = isValid,
            IsLocked = false,
            IsDeleted = false,
            InForum = true
        };

        var postMemberData = new PostMemberData()
        {
            UserId = threadInterface.OwnerId,
            ThreadId = socketThreadChannel.Id,
            IsThreadOwner = true,
            FirstJoinAt = socketThreadChannel.CreatedAt,
            LastJoinAt = socketThreadChannel.CreatedAt,
            LeaveAt = null,
            IsLeave = false,
            MessageCount = 1,
            LastMessageId = socketThreadChannel.Id,
            LastMessageAt = socketThreadChannel.CreatedAt,
        };

        var userData = new UserData()
        {
            UserId = threadInterface.OwnerId,
            UserThreadCountDelta = 1,
            UserMessageCountDelta = 1,
            UserUpdateCountDelta = 0,
            UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForThread.Level1,
            UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForThread.Level2,
            UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForThread.Level3,
        };

        // ==========================
        // 写入数据库
        // ========================== 
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {

                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.InsertThreadAsync(transaction, threadData);

                    await _databaseService.InsertTagsJunction(transaction, threadData);

                    await _databaseService.UpsertPostMemberAsync(transaction, postMemberData);

                    await _databaseService.UpsertUserDataAsync(transaction, userData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功插入帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 有效性: {IsValid}",
                socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsValid);
        }
        catch (Exception ex)
        {

            _logger.Error(ex, "❌ 创建帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    public async Task CreateTextThreadProcessAsync(SocketThreadChannel socketThreadChannel, SocketTextChannel socketTextChannel)
    {
        _logger.Debug("开始处理子区创建: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);

        var threadInterface = (IThreadChannel)socketThreadChannel;

        // ==========================
        // 构建数据模型
        // ========================== 
        var threadData = new ThreadData()
        {
            ThreadId = socketThreadChannel.Id,
            FirstMessageId = socketThreadChannel.Id,
            AuthorId = threadInterface.OwnerId,
            ChannelId = socketThreadChannel.ParentChannel.Id,
            ChannelName = socketThreadChannel.ParentChannel.Name,
            GuildId = socketThreadChannel.Guild.Id,
            GuildName = socketThreadChannel.Guild.Name,
            CreatedAt = socketThreadChannel.CreatedAt,
            ReactionCount = 0,
            ReplyCount = socketThreadChannel.MessageCount,
            LastActiveAt = socketThreadChannel.CreatedAt,
            JumpUrl = $"https://discord.com/channels/{socketThreadChannel.Guild.Id}/{socketThreadChannel.Id}/0",
            AttachmentUrl = null,

            Title = socketThreadChannel.Name,
            FirstMessageContent = socketThreadChannel.Name,

            TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
            ContentTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),

            // 更新相关
            UpdatedAt = null,
            UpdatedJumpUrl = null,
            UpdateCount = 0,

            // Tag
            Tags = null,
            TagsId = [],

            // 有效性
            IsValid = false,
            IsDeleted = false,
            InForum = false
        };

        var postMemberData = new PostMemberData()
        {
            UserId = threadInterface.OwnerId,
            ThreadId = socketThreadChannel.Id,
            IsThreadOwner = true,
            FirstJoinAt = socketThreadChannel.CreatedAt,
            LastJoinAt = socketThreadChannel.CreatedAt,
            LeaveAt = null,
            IsLeave = false,
            MessageCount = 1,
            LastMessageId = socketThreadChannel.Id,
            LastMessageAt = socketThreadChannel.CreatedAt,
        };

        var channelFactor = _configLoader.Config.CreditConfig.ChannelFactor.GetValueOrDefault(socketThreadChannel.Id, 100);

        var userData = new UserData()
        {
            UserId = threadInterface.OwnerId,
            UserThreadCountDelta = 0,
            UserMessageCountDelta = 1,
            UserUpdateCountDelta = 0,
            UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForMessage.Level1 * channelFactor,
            UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForMessage.Level2 * channelFactor,
            UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForMessage.Level3 * channelFactor,
        };

        // ==========================
        // 写入数据库
        // ========================== 
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {

                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.InsertThreadAsync(transaction, threadData);

                    await _databaseService.UpsertPostMemberAsync(transaction, postMemberData);

                    await _databaseService.UpsertUserDataAsync(transaction, userData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功插入帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 有效性: {IsValid}",
                socketThreadChannel.Guild.Name, socketThreadChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsValid);
        }
        catch (Exception ex)
        {

            _logger.Error(ex, "❌ 创建帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, socketThreadChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    /// <summary>
    /// 更新论坛帖子的信息（如标题、标签、锁定状态等）。
    /// 该方法仅更新帖子的元数据，不涉及消息内容。
    /// </summary>
    /// <param name="socketThreadChannel">Discord 帖子频道对象。</param>
    public async Task UpdateForumThreadProcessAsync(SocketThreadChannel socketThreadChannel, SocketForumChannel socketForumChannel)
    {
        _logger.Debug("开始处理帖子更新: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);

        // ==========================
        // 标签处理
        // ========================== 
        // 获得帖子标签

        var appliedTags = socketForumChannel.Tags
            .Where(tag => socketThreadChannel.AppliedTags.Contains(tag.Id))
            .ToList();
        var appliedTagIds = appliedTags
            .Select(tag => tag.Id)
            .ToList();
        var appliedTagNames = appliedTags
            .Select(tag => tag.Name)
            .ToList();
        var tags = string.Join(", ", appliedTagNames);

        // ==========================
        // 构建数据模型
        // ========================== 
        var threadData = new ThreadData()
        {
            ThreadId = socketThreadChannel.Id,
            // 标题更新
            Title = socketThreadChannel.Name,
            TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
            // Tag
            Tags = tags,
            TagsId = appliedTagIds,
            // 是否锁定
            IsValid = socketThreadChannel.IsLocked,
            IsLocked = socketThreadChannel.IsLocked,
        };

        // ==========================
        // 写入数据库
        // ========================== 
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.UpdateThreadAsync(transaction, threadData);

                    await _databaseService.ReplaceTagsJunction(transaction, threadData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功更新帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 锁定状态: {IsLocked}",
                socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsLocked);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 更新帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, socketForumChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }

    public async Task UpdateTextThreadProcessAsync(SocketThreadChannel socketThreadChannel, SocketTextChannel socketTextChannel)
    {
        _logger.Debug("开始处理帖子更新: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
            socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);

        // ==========================
        // 构建数据模型
        // ========================== 
        var threadData = new ThreadData()
        {
            ThreadId = socketThreadChannel.Id,
            // 标题更新
            Title = socketThreadChannel.Name,
            TitleTokenized = _tokenizerService.CutForSearch(socketThreadChannel.Name),
            // Tag
            Tags = null,
            TagsId = [],
            // 是否锁定
            IsValid = false,
            IsLocked = socketThreadChannel.IsLocked,
        };

        // ==========================
        // 写入数据库
        // ========================== 
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.UpdateThreadAsync(transaction, threadData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功更新帖子: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId}), 锁定状态: {IsLocked}",
                socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name,
                socketThreadChannel.Id, threadData.IsLocked);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 更新帖子失败: [{GuildName}.{ChannelName}.{ThreadName}] (ID: {ThreadId})",
                socketThreadChannel.Guild.Name, socketTextChannel.Name, socketThreadChannel.Name, socketThreadChannel.Id);
        }
    }
    
    /// <summary>
    /// 更新论坛帖子首楼消息的反应（Reaction）计数。
    /// </summary>
    /// <param name="socketThreadChannel">Discord 帖子频道对象。</param>
    public async Task UpdateTheradReactionProcessAsync(ulong threadId, NumericUpdateType reactionUpdateType)
    {
        _logger.Debug("开始更新帖子反应计数: 帖子 ID: ThreadId}", threadId);

        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.UpdateThreadReactionAsync(transaction, threadId, reactionUpdateType);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功更新帖子反应计数: 帖子 ID: ThreadId}", threadId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 更新帖子反应计数失败: 帖子 ID: {ThreadId}", threadId);
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
            int affectedRows = 0;
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    affectedRows = await _databaseService.SoftDeleteThreadAsync(transaction, threadId);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (affectedRows > 0)
            {
                _logger.Info("✅ 成功软删除帖子 (ID: {ThreadId})", threadId);
            }
            else
            {
                _logger.Warning("⚠️ 尝试软删除帖子 {ThreadId}，但该帖子未找到或已被删除。", threadId);
            }
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