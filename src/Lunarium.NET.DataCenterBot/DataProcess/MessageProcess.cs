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
    #region 消息处理
    /// <summary>
    /// 处理接受消息数据: 帖子重载
    /// 处理论坛频道帖子内的消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="threadChannel"></param>
    /// <param name="parentChannel"></param>
    /// <returns></returns>
    public async Task ThreadMessageProcessAsync(SocketMessage message, SocketThreadChannel threadChannel, SocketForumChannel parentChannel)
    {
        try
        {
            IMessage? refreshMessage;
            bool isUpdate = false;
            if (message.Author.Id == threadChannel.Owner.Id && message.Id == threadChannel.Id)
            {
                // 等待 1 s
                await Task.Delay(1000);
                // 傻逼 dc api 的 MessageReceived 不会附带附件, 附件是后续 Update 上去的, 得重新获取一下 Update 的消息
                refreshMessage = await threadChannel.GetMessageAsync(message.Id);
                if (refreshMessage == null)
                {
                    _logger.Warning("无法从频道缓存中获取到最新的消息对象 (ID: {message.Id})，将使用事件参数中的消息对象。", message.Id);
                    refreshMessage = message; // 如果获取失败，回退到使用原始的 message 对象
                }
                else
                {
                    isUpdate = await CheckUpdateAsync(refreshMessage);
                }
            }

            // 准备更新消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorName = message.Author.GlobalName,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt,
                GuildId = parentChannel.Guild.Id,
                GuildName = parentChannel.Guild.Name,
                ChannelId = parentChannel.Id,
                ChannelName = parentChannel.Name,
                ThreadId = threadChannel.Id,
                ThreadTitle = threadChannel.Name,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = message.Id == threadChannel.Id,
                IsUpdate = isUpdate
            };
            // 如果是首楼消息
            // 将 posts_main 相关数据完全交由 CreateThreadProcessAsync 侧进行处理, 调用不处理 posts_main 表的方法
            if (message.Id == threadChannel.Id)
            {
                await _dbRetryPolicy.ExecuteAsync(async () =>
                {
                    await _databaseService.TextThreadMessageToDBAsync(messageData);
                });
                return;
            }
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.ForumThreadMessageToDBAsync(messageData);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    /// <summary>
    /// 处理接受消息数据: 子区重载
    /// 处理论坛频道子区内的消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="threadChannel"></param>
    /// <param name="parentChannel"></param>
    /// <returns></returns>
    /// 
    public async Task ThreadMessageProcessAsync(SocketMessage message, SocketThreadChannel threadChannel, SocketTextChannel parentChannel)
    {
        try
        {
            if (message.Id == threadChannel.Id)
            {
                // 如果是该消息创建了子区
                // 等待 1 s
                // 即使比 Created 事件先接收, 也尽量确保在 Created 事件之后
                await Task.Delay(1000);
            }
            // 准备更新消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt,
                GuildId = parentChannel.Guild.Id,
                GuildName = parentChannel.Guild.Name,
                ChannelId = parentChannel.Id,
                ChannelName = parentChannel.Name,
                ThreadId = threadChannel.Id,
                ThreadTitle = threadChannel.Name,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = message.Id == threadChannel.Id,
                IsUpdate = false
            };
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.TextThreadMessageToDBAsync(messageData);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    /// <summary>
    /// 处理消息数据: 文字频道
    /// </summary>
    /// <param name="message"></param>
    /// <param name="parentChannel"></param>
    /// <returns></returns>
    public async Task TextChannelMessageProcess(SocketMessage message, SocketTextChannel parentChannel)
    {
        try
        {
            // 准备更新消息数据
            var messageData = new MessageData()
            {
                MessageId = message.Id,
                AuthorId = message.Author.Id,
                CreatedAt = message.CreatedAt,
                GuildId = parentChannel.Guild.Id,
                GuildName = parentChannel.Guild.Name,
                ChannelId = parentChannel.Id,
                ChannelName = parentChannel.Name,
                ThreadId = null,
                ThreadTitle = null,
                MessageJumpUrl = message.GetJumpUrl(),
                IsCreateThread = false,
                IsUpdate = false
            };
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.TextChannelMessageToDB(messageData);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    public async Task MessagesDeleteProcess(List<ulong> deletedMessage)
    {
        try
        {
            await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await _databaseService.MessagesDeleteToDB(deletedMessage);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    #endregion

    #region 辅助方法-更新附件的检查
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
        _logger.Debug("CheckUpdateAsync started for message ID: {message.Id}", message.Id);
        try
        {
            if (message.Attachments.Count == 0)
            {
                _logger.Debug("No attachments found on message {message.Id}. Returning false.", message.Id);
                return false;
            }

            // --- 步骤 1: 检查 JSON 附件 (这个开销很小，可以先做) ---
            if (message.Attachments.Any(a => a.Filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.Info("JSON 检查: 在消息 {message.Id} 中检测到 JSON 附件，判定为更新。", message.Id);
                return true;
            }

            // --- 步骤 2: 过滤并处理 PNG 附件 ---
            var pngAttachments = message.Attachments
                .Where(a => a.Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pngAttachments.Count == 0)
            {
                _logger.Debug("Message {message.Id} has no PNG attachments. All checks complete.", message.Id);
                return false;
            }

            if (pngAttachments.Count >= 3)
            {
                _logger.Debug("Message {message.Id} has no PNG attachments. But >= 3, ", message.Id);
                return false;
            }

            _logger.Debug("Found {pngAttachments.Count} PNG attachments. Starting sequential processing with optimizations.", pngAttachments.Count);

            // 使用 for-loop 逐个处理，避免并行下载带来的瞬时内存/IO高峰
            foreach (var attachment in pngAttachments)
            {
                if (await IsUpdatePngAsync(attachment))
                {
                    _logger.Info("PNG 检查: 在附件 '{attachment.Filename}' 中检测到角色卡更新。", attachment.Filename);
                    return true; // 找到一个就够了，直接返回
                }
            }

            _logger.Debug("No update-triggering PNGs found after checking all. Returning false.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An unexpected error occurred in CheckUpdateAsync for message ID {message.Id}.", message.Id);
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
            // 1. 【优化】预检文件大小
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, attachment.Url);
            using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);

            if (headResponse.Content.Headers.ContentLength.HasValue && headResponse.Content.Headers.ContentLength.Value > MAX_FILE_SIZE_BYTES)
            {
                var fileSizeMB = headResponse.Content.Headers.ContentLength.Value / 1024.0 / 1024.0;
                var sizeLimitMB = MAX_FILE_SIZE_BYTES / 1024.0 / 1024.0;
                _logger.Debug("Skipping attachment '{attachment.Filename}' because its size ({fileSizeMB:F2} MB) exceeds the {sizeLimitMB:F2} MB limit.",
                    attachment.Filename,
                    fileSizeMB,
                    sizeLimitMB
                    );
                return false;
            }

            // 2. 【优化】流式下载到临时文件
            tempFilePath = Path.GetTempFileName();
            _logger.Debug("Downloading '{attachment.Filename}' to temporary file: {tempFilePath}", attachment.Filename, tempFilePath);

            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var httpStream = await _httpClient.GetStreamAsync(attachment.Url))
            {
                await httpStream.CopyToAsync(fileStream);
            }
            _logger.Debug("Successfully downloaded '{attachment.Filename}'.", attachment.Filename);

            // 3. 【优化】使用 Image.Identify 仅读取元数据
            _logger.Debug("Identifying metadata for '{tempFilePath}' without decoding the image.", tempFilePath);
            var imageInfo = await SixLabors.ImageSharp.Image.IdentifyAsync(tempFilePath);

            if (imageInfo == null)
            {
                _logger.Warning("ImageSharp could not identify image info for '{attachment.Filename}'.", attachment.Filename);
                return false;
            }

            var pngMetadata = imageInfo.Metadata.GetPngMetadata();
            _logger.Debug("Found {pngMetadata.TextData.Count} text chunks in '{attachment.Filename}'.", pngMetadata.TextData.Count, attachment.Filename);

            foreach (var textData in pngMetadata.TextData)
            {
                if (textData.Keyword.Equals("ccv3", StringComparison.OrdinalIgnoreCase) ||
                    textData.Keyword.Equals("chara", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Success! Found update keyword '{textData.Keyword}' in '{attachment.Filename}'.", textData.Keyword, attachment.Filename);
                    return true;
                }
            }

            _logger.Debug("No update keywords found in '{attachment.Filename}'.", attachment.Filename);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process PNG attachment '{attachment.Filename}'. It will be skipped.", attachment.Filename);
            return false;
        }
        finally
        {
            // 4. 关键：清理临时文件
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.Debug("Cleaned up temporary file: {tempFilePath}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete temporary file '{tempFilePath}'.", tempFilePath);
                }
            }
        }
    }
    #endregion
}