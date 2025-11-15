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
/// DataProcessService 的部分类，专门负责消息的处理逻辑。
/// </summary>
public partial class DataProcessService
{
    #region 消息处理
    /// <summary>
    /// 处理论坛频道帖子内的消息。
    /// 该方法会检查消息是否为首楼消息，如果是作者本人发送的非首楼消息，还会检查是否包含作品更新附件。
    /// 对于首楼消息，仅记录消息数据；对于其他消息，会更新帖子的回复计数。
    /// </summary>
    /// <param name="message">接收到的消息对象</param>
    /// <param name="socketThreadChannel">消息所在的帖子频道</param>
    /// <param name="parentChannel">帖子所属的父论坛频道</param>
    public async Task ThreadMessageProcessAsync(SocketMessage message, SocketThreadChannel socketThreadChannel, SocketForumChannel parentChannel)
    {
        try
        {
            _logger.Debug("处理论坛帖子消息: [{GuildName}.{ChannelName}.{ThreadName}] 消息ID: {MessageId}, 作者: {AuthorName}",
                parentChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, message.Id, message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER");

            IMessage? refreshMessage = message;
            ulong OwnerId;
            bool isUpdate = false;

            _logger.Debug("空值检查处理: {message.Author}", message.Author);
            _logger.Debug("空值检查处理: {message.Author.Id}", message.Author.Id);

            IThreadChannel thread = (IThreadChannel)socketThreadChannel;

            if (thread.OwnerId == 0)
            {
                _logger.Debug("空值检查处理: Owner 为 null");
                thread = (IThreadChannel)await _discordSocketClient.GetChannelAsync(socketThreadChannel.Id);
                OwnerId = thread.OwnerId;
            }
            else
            {
                OwnerId = thread.OwnerId;
            }

            _logger.Debug("空值检查处理: {OwnerId}", OwnerId);

            // 如果消息是帖子作者发送的，且不是首楼消息，则检查是否包含作品更新附件
            if (message.Author.Id == OwnerId && message.Id != socketThreadChannel.Id)
            {
                // 等待 1 秒，因为 Discord API 的 MessageReceived 事件不会立即附带附件
                // 附件是后续异步上传的，需要重新获取消息以获得完整的附件列表
                await Task.Delay(1000);
                try
                {
                    await _semaphore.WaitAsync();

                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    { 
                        refreshMessage = await socketThreadChannel.GetMessageAsync(message.Id);
                    });

                    if (refreshMessage is null)
                    {
                        _logger.Warning("⚠️ 无法从频道缓存中获取到最新的消息对象 (ID: {MessageId})，将使用事件参数中的消息对象。", message.Id);
                        refreshMessage = message; // 如果获取失败，回退到使用原始的 message 对象
                    }
                    else
                    {
                        // 检查消息是否包含作品更新附件
                        isUpdate = await CheckUpdateAsync(refreshMessage);
                        if (isUpdate)
                        {
                            _logger.Info("✨ 检测到作品更新: [{GuildName}.{ChannelName}.{ThreadName}] 消息ID: {MessageId}",
                                parentChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, message.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 刷新消息对象时发生错误: 消息ID {MessageId}", message.Id);
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            // 准备更新消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt,
                GuildId = parentChannel.Guild.Id,
                ChannelId = parentChannel.Id,
                ThreadId = socketThreadChannel.Id,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = message.Id == socketThreadChannel.Id,
                IsUpdate = isUpdate
            };

            var postMemberData = new PostMemberData()
            {
                UserId = message.Author.Id,
                ThreadId = socketThreadChannel.Id,
                IsThreadOwner = message.Author.Id == OwnerId,
                FirstJoinAt = message.CreatedAt,
                LastJoinAt = message.CreatedAt,
                LeaveAt = null,
                IsLeave = false,
                MessageCount = 1,
                LastMessageId = message.Id,
                LastMessageAt = message.CreatedAt,
            };

            var userData = new UserData();
            userData.UserId = message.Author.Id;
            userData.UserName = message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER";
            if (isUpdate)
            {
                userData.UserMessageCountDelta = 1;
                userData.UserUpdateCountDelta = 1;
                userData.UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForUpdate.Level1;
                userData.UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForUpdate.Level2;
                userData.UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForUpdate.Level3;
            }
            else
            {
                userData.UserMessageCountDelta = 1;
                userData.UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForMessage.Level1;
                userData.UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForMessage.Level2;
                userData.UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForMessage.Level3;
            }

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.InsertMessageAsync(transaction, messageData);

                    await _databaseService.UpsertPostMemberAsync(transaction, postMemberData);

                    if (isUpdate)
                    {
                        await _databaseService.UpdatePostWorkAsync(transaction, socketThreadChannel.Id, message.GetJumpUrl(), message.CreatedAt);
                    }
                    
                    // 基于回复记数的更新原理:
                    // - 直接将 API 附带的数据中的准确的总数覆盖写入
                    // - 而非在数据库内的原值上做加减
                    // - 会在成功的时候直接覆盖正确的的当时总数
                    // 某次单纯更新记数失败是可接受的, 使用保存点隔离 UpdateThreadReplayCountAsync 操作
                    await transaction.SaveAsync("before_update_count");
                    try
                    {
                        await _databaseService.UpdateThreadReplayCountAsync(transaction, socketThreadChannel.Id, socketThreadChannel.MessageCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "更新帖子回复数失败 (可接受), ThreadId: {ThreadId}", socketThreadChannel.Id);
                        // 回滚到保存点
                        await transaction.RollbackAsync("before_update_count");
                    }
    
                    // 创建帖子导致的数据变更在 CreateThreadProcessAsync 方法中都处理了
                    if (message.Id != socketThreadChannel.Id)
                    {
                        await _databaseService.UpsertUserDataAsync(transaction, userData);
                    }
    
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

            });
            _logger.Debug("✅ 成功记录消息: 消息ID {MessageId}, 回复数: {MessageCount}", message.Id, socketThreadChannel.MessageCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理论坛帖子消息失败: [{GuildName}.{ChannelName}.{ThreadName}] 消息ID: {MessageId}",
                parentChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, message.Id);
        }
    }

    /// <summary>
    /// 处理文字频道子区（Thread）内的消息。
    /// 子区是从文字频道的某条消息创建的讨论线程。
    /// 如果消息ID等于帖子ID，说明该消息创建了子区，会延迟2秒以确保在 Created 事件之后处理。
    /// </summary>
    /// <param name="message">接收到的消息对象</param>
    /// <param name="socketThreadChannel">消息所在的子区频道</param>
    /// <param name="parentChannel">子区所属的父文字频道</param>
    public async Task ThreadMessageProcessAsync(SocketMessage message, SocketThreadChannel socketThreadChannel, SocketTextChannel parentChannel)
    {
        try
        {
            _logger.Debug("处理文字频道子区消息: [{GuildName}.{ChannelName}.{ThreadName}] 消息ID: {MessageId}, 作者: {AuthorName}",
                parentChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, message.Id, message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER");

            ulong OwnerId;

            _logger.Debug("空值检查处理: {message.Author}", message.Author);
            _logger.Debug("空值检查处理: {message.Author.Id}", message.Author.Id);

            IThreadChannel thread = (IThreadChannel)socketThreadChannel;

            if (thread.OwnerId == 0)
            {
                _logger.Debug("空值检查处理: Owner 为 null");
                thread = (IThreadChannel)await _discordSocketClient.GetChannelAsync(socketThreadChannel.Id);
                OwnerId = thread.OwnerId;
            }
            else
            {
                OwnerId = thread.OwnerId;
            }
            _logger.Debug("空值检查处理: {OwnerId}", OwnerId);
            // 准备更新消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt.ToUniversalTime(),
                GuildId = parentChannel.Guild.Id,
                ChannelId = parentChannel.Id,
                ThreadId = socketThreadChannel.Id,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = message.Id == socketThreadChannel.Id,
                IsUpdate = false
            };

            var postMemberData = new PostMemberData()
            {
                UserId = message.Author.Id,
                ThreadId = socketThreadChannel.Id,
                IsThreadOwner = message.Author.Id == OwnerId,
                FirstJoinAt = message.CreatedAt,
                LastJoinAt = message.CreatedAt,
                LeaveAt = null,
                IsLeave = false,
                MessageCount = 1,
                LastMessageId = message.Id,
                LastMessageAt = message.CreatedAt,
            };

            var userData = new UserData()
            {
                UserId = message.Author.Id,
                UserName = message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER",
                UserMessageCountDelta = 1,
                UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForMessage.Level1,
                UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForMessage.Level2,
                UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForMessage.Level3
            };

            try
            {
                await _dbRetryPolicy.ExecuteAsync(async () =>
                {
                    await using var connection = _databaseService.GetConnection();
                    await connection.OpenAsync();
                    await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                    try
                    {
                        await _databaseService.InsertMessageAsync(transaction, messageData);

                        await _databaseService.UpsertPostMemberAsync(transaction, postMemberData);
                        
                        // 基于回复记数的更新原理:
                        // - 直接将 API 附带的数据中的准确的总数覆盖写入
                        // - 而非在数据库内的原值上做加减
                        // - 会在成功的时候直接覆盖正确的的当时总数
                        // 某次单纯更新记数失败是可接受的, 使用保存点隔离 UpdateThreadReplayCountAsync 操作
                        await transaction.SaveAsync("before_update_count");
                        try
                        {
                            await _databaseService.UpdateThreadReplayCountAsync(transaction, socketThreadChannel.Id, socketThreadChannel.MessageCount);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "更新帖子回复数失败 (可接受), ThreadId: {ThreadId}", socketThreadChannel.Id);
                            // 回滚到保存点
                            await transaction.RollbackAsync("before_update_count");
                        }
        
                        // 创建帖子导致的数据变更在 CreateThreadProcessAsync 方法中都处理了
                        if (message.Id != socketThreadChannel.Id)
                        {
                            await _databaseService.UpsertUserDataAsync(transaction, userData);
                        }
        
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }

                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
            _logger.Debug("✅ 成功记录文字频道子区消息: 消息ID {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理文字频道子区消息失败: [{GuildName}.{ChannelName}.{ThreadName}] 消息ID: {MessageId}",
                parentChannel.Guild.Name, parentChannel.Name, socketThreadChannel.Name, message.Id);
        }
    }

    /// <summary>
    /// 处理普通文字频道中的消息（不在子区内）。
    /// </summary>
    /// <param name="message">接收到的消息对象</param>
    /// <param name="parentChannel">消息所在的文字频道</param>
    public async Task TextChannelMessageProcess(SocketMessage message, SocketTextChannel parentChannel)
    {
        try
        {
            _logger.Debug("处理文字频道消息: [{GuildName}.{ChannelName}] 消息ID: {MessageId}, 作者: {AuthorName}",
                parentChannel.Guild.Name, parentChannel.Name, message.Id, message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER");

            // 准备消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt,
                GuildId = parentChannel.Guild.Id,
                ChannelId = parentChannel.Id,
                ThreadId = null,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = false,
                IsUpdate = false
            };

            var userData = new UserData()
            {
                UserId = message.Author.Id,
                UserName = message.Author.GlobalName ?? message.Author.Username ?? "UNKNOW USER",
                UserMessageCountDelta = 1,
                UserCreditLv1Delta = _configLoader.Config.CreditConfig.ForMessage.Level1,
                UserCreditLv2Delta = _configLoader.Config.CreditConfig.ForMessage.Level2,
                UserCreditLv3Delta = _configLoader.Config.CreditConfig.ForMessage.Level3
            };

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.InsertMessageAsync(transaction, messageData);

                    await _databaseService.UpsertUserDataAsync(transaction, userData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

            });
            _logger.Debug("✅ 成功记录文字频道消息: 消息ID {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理文字频道消息失败: [{GuildName}.{ChannelName}] 消息ID: {MessageId}",
                parentChannel.Guild.Name, parentChannel.Name, message.Id);
        }
    }

    /// <summary>
    /// 处理首楼更新
    /// </summary>
    /// <param name="message">接收到的消息对象</param>
    /// <param name="channelId">消息所在频道的id</param>
    public async Task ThreadFirstMessageUpdateProcess(SocketMessage message, ulong channelId)
    {
        try
        {

            // 准备消息数据
            var threadData = new ThreadData()
            {
                FirstMessageContent = message.Content,
                ContentTokenized = message.Content is not null ? _tokenizerService.CutForSearch(message.Content) : null,
            };

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.UpdateThreadContentAsync(transaction, threadData);

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

            });
            _logger.Debug("✅ 成功更新帖子首楼消息: 消息ID {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理更新帖子首楼消息失败: 消息ID: {MessageId}, 链接: {MessageJumpUrl}",
                message.Id, message.GetJumpUrl());
        }
    }

    /// <summary>
    /// 批量处理消息删除事件。
    /// 将删除的消息ID列表传递给数据库服务进行处理。
    /// </summary>
    /// <param name="deletedMessage">被删除的消息ID列表</param>
    public async Task MessagesDeleteProcess(List<ulong> deletedMessage)
    {
        try
        {
            _logger.Debug("处理批量消息删除: 数量 {Count}", deletedMessage.Count);

            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    await _databaseService.DeleteMessagesAsync(transaction, deletedMessage);
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            _logger.Info("✅ 成功处理消息删除: 数量 {Count}", deletedMessage.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理批量消息删除失败: 数量 {Count}", deletedMessage.Count);
        }
    }

    #endregion

    #region 辅助方法 - 作品更新检查
    /// <summary>
    /// 检查一条消息是否包含可被视为“作品更新”的附件。
    /// 优化策略：
    /// 1. 使用 HEAD 请求预检文件大小，跳过超大附件（如 > 20MB）。
    /// 2. 流式下载附件到临时文件，避免将整个文件加载到内存。
    /// 3. 使用 Image.Identify 仅读取元数据，不解码整个图像，显著降低内存占用。
    /// </summary>
    /// <param name="message">要检查的消息对象。</param>
    /// <returns>如果消息包含更新，则返回 true；否则返回 false。</returns>
    public async Task<bool> CheckUpdateAsync(IMessage message)
    {
        _logger.Debug("开始检查消息更新: 消息ID {MessageId}", message.Id);
        try
        {
            if (message.Attachments.Count == 0)
            {
                _logger.Debug("消息 {MessageId} 没有附件，返回 false", message.Id);
                return false;
            }

            // --- 步骤 1: 检查 JSON 附件 ---
            if (message.Attachments.Any(a => a.Filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Info("✨ JSON 检查: 在消息 {MessageId} 中检测到 JSON 附件，判定为更新", message.Id);
                return true;
            }

            // --- 步骤 2: 过滤并处理 PNG 附件 ---
            var pngAttachments = message.Attachments
                .Where(a => a.Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pngAttachments.Count == 0)
            {
                _logger.Debug("消息 {MessageId} 没有 PNG 附件，所有检查完成", message.Id);
                return false;
            }

            if (pngAttachments.Count >= 3)
            {
                _logger.Debug("消息 {MessageId} 的 PNG 附件数量 >= 3，跳过检查", message.Id);
                return false;
            }

            _logger.Debug("发现 {Count} 个 PNG 附件，开始逐个处理", pngAttachments.Count);

            // 使用 for-loop 逐个处理，避免并行下载带来的瞬时内存/IO高峰
            foreach (var attachment in pngAttachments)
            {
                if (await IsUpdatePngAsync(attachment))
                {
                    _logger.Info("✨ PNG 检查: 在附件 '{FileName}' 中检测到角色卡更新", attachment.Filename);
                    return true; // 找到一个就够了，直接返回
                }
            }

            _logger.Debug("所有 PNG 附件检查完毕，未发现更新标记，返回 false");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 检查消息更新时发生意外错误: 消息ID {MessageId}", message.Id);
            return false;
        }
    }

    /// <summary>
    /// 辅助方法，用于检查单个PNG附件是否为更新。
    /// </summary>
    /// <param name="attachment">要检查的PNG附件。</param>
    /// <returns>如果是更新，返回 true。</returns>
    private async Task<bool> IsUpdatePngAsync(IAttachment attachment)
    {
        // 定义文件大小限制 (例如 20 MB)
        const long MAX_FILE_SIZE_BYTES = 20 * 1024 * 1024;
        string? tempFilePath = null;

        try
        {
            // 预检文件大小
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, attachment.Url);
            using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);

            if (headResponse.Content.Headers.ContentLength.HasValue && headResponse.Content.Headers.ContentLength.Value > MAX_FILE_SIZE_BYTES)
            {
                var fileSizeMB = headResponse.Content.Headers.ContentLength.Value / 1024.0 / 1024.0;
                var sizeLimitMB = MAX_FILE_SIZE_BYTES / 1024.0 / 1024.0;
                _logger.Debug("跳过附件 '{FileName}'，文件大小 ({FileSizeMB:F2} MB) 超过限制 ({SizeLimitMB:F2} MB)",
                    attachment.Filename,
                    fileSizeMB,
                    sizeLimitMB
                    );
                return false;
            }

            // 流式下载到临时文件
            tempFilePath = Path.GetTempFileName();
            _logger.Debug("开始下载附件 '{FileName}' 到临时文件: {TempFilePath}", attachment.Filename, tempFilePath);

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var httpStream = await _httpClient.GetStreamAsync(attachment.Url))
            {
                await httpStream.CopyToAsync(fileStream);
            }
            _logger.Debug("✅ 附件 '{FileName}' 下载完成", attachment.Filename);

            // 使用 Image.Identify 仅读取元数据
            _logger.Debug("正在识别 '{TempFilePath}' 的图片元数据（不解码图像）", tempFilePath);
            var imageInfo = await SixLabors.ImageSharp.Image.IdentifyAsync(tempFilePath);

            if (imageInfo is null)
            {
                _logger.Warning("⚠️ ImageSharp 无法识别图片信息: '{FileName}'", attachment.Filename);
                return false;
            }

            var pngMetadata = imageInfo.Metadata.GetPngMetadata();
            _logger.Debug("在附件 '{FileName}' 中发现 {Count} 个文本块", attachment.Filename, pngMetadata.TextData.Count);

            foreach (var textData in pngMetadata.TextData)
            {
                if (textData.Keyword.Equals("ccv3", StringComparison.OrdinalIgnoreCase) ||
                    textData.Keyword.Equals("chara", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("✅ 成功！在附件 '{FileName}' 中找到更新关键字 '{Keyword}'", attachment.Filename, textData.Keyword);
                    return true;
                }
            }

            _logger.Debug("在附件 '{FileName}' 中未找到更新关键字", attachment.Filename);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 处理 PNG 附件 '{FileName}' 时失败，将跳过", attachment.Filename);
            return false;
        }
        finally
        {
            // 清理临时文件
            if (tempFilePath is not null && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.Debug("已清理临时文件: {TempFilePath}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "❌ 删除临时文件失败: '{TempFilePath}'", tempFilePath);
                }
            }
        }
    }
    #endregion
}