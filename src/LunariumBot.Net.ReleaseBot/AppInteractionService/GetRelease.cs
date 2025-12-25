// .NET 核心库
using System.Text;
using System.Text.Json;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using Discord.Interactions;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.ApplicationServices;
using LunariumBot.Net.ReleaseBot.Utils;
using LunariumBot.Net.ReleaseBot.TokenBucketServices;
using LunariumBot.Net.ReleaseBot.CacheServices;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.ReleaseBot.AppInteractionService;

public class GetRelease : InteractionModuleBase<SocketInteractionContext>
{
    private string _appPath = AppContext.BaseDirectory;
    const string localCachePath = "localCache";
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly ComponentUtils _componentUtils;
    private readonly HttpClient _httpClient;
    private readonly CacheService _cacheService;
    private readonly ApplicationService _applicationService;
    private readonly TokenBucketService _tokenBucketService;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    private readonly ReleaseBotAssistService? _assistService;
    public GetRelease(
        ILogger logger,
        AuthUtil authUtil,
        HttpClient httpClient,
        CacheService cacheService,
        ApplicationService applicationService,
        TokenBucketService tokenBucketService,
        AssistBotPayload assistBotPayload,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
    {
        _logger = logger.ForContext("GetRelease");
        _authUtil = authUtil;
        _httpClient = httpClient;
        _cacheService = cacheService;
        _applicationService = applicationService;
        _componentUtils = new(_logger);
        _tokenBucketService = tokenBucketService;
        _httpRetryPolicy = httpRetryPolicy;
        _assistService = assistBotPayload.assistBot;
    }

    // =============================================
    #region 业务方法
    // =============================================
    public async Task<(MessageComponent? Component, MessageComponent? DMComponent, bool Allowable)> GetWorkAsync(WorkData data, bool noConsume)
    {
        try
        {
            IMessage? message = null;

            // 尝试从远程 Discord 缓存获取消息
            try
            {
                message = await _cacheService.GetMessageAsync(data);
            }
            catch
            {
                _logger.Warning("无法获取远程缓存消息，尝试重建远程缓存。CacheId: {CacheMessageId}", data.CacheMessageId);
                message = null;
            }

            // 重建远程缓存
            if (message is null)
            {
                // 重建远程缓存
                message = await RebuildRemoteCacheAsync(data);
                // 更新数据库记录
                await _applicationService.UpdateWorkCacheAsync(data);
                _logger.Warning("重建远程缓存成功。CacheId: {CacheMessageId}", data.CacheMessageId);
            }

            if (message is null)
            {
                _logger.Error("重建远程缓存失败。CacheId: {CacheMessageId}", data.CacheMessageId);
                return (null, null, false);
            }

            // 如果远程获取或远程缓存重建成功，返回标准的图库/下载组件
            var _attachments = message.Attachments;
            var tokenResult = _tokenBucketService.ConsumeToken(Context.User.Id, noConsume ? 0 : 1);
            if (tokenResult.Allowable)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container =>
                    {
                        container.WithAccentColor(Color.Green); // 远程获取成功用绿色
                        container.WithTextDisplay($"""
                            ### 🎈 获取作品
                            > -# 今日剩余可获取作品量: {tokenResult.RemainingToken}/{tokenResult.TokenLimit}
                            """);
                        container.WithSeparator();

                        if (data.CacheAttachmentUrls.Count > 0)
                        {
                            // 优先显示图片
                            List<IAttachment>? imageAttachment = _attachments?
                                .Where(a => IsImageAttachment(a))
                                .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            // 其他文件显示下载链接
                            List<IAttachment>? otherAttachment = _attachments?
                                .Where(a => !IsImageAttachment(a))
                                .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (imageAttachment?.Count > 0)
                            {
                                container.WithTextDisplay($"""
                                    **以下为图片附件:**
                                    > -# 右键或长按图片下载, 或点击超链接下载
                                    """);
                                MediaGalleryBuilder media = new();
                                StringBuilder sb = new();
                                foreach (var attachment in imageAttachment)
                                {
                                    media.AddItem(attachment.Url);
                                    sb.AppendLine($"🖼 {attachment.Filename}");
                                    sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
                                    sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
                                }
                                container.WithMediaGallery(media);
                                container.WithTextDisplay(sb.ToString());
                            }

                            container.WithSeparator();
                            if (otherAttachment?.Count > 0)
                            {
                                container.WithTextDisplay($"""
                                    **以下为非图片附件:**
                                    > -# 点击超链接下载
                                    """);
                                StringBuilder sb = new();
                                foreach (var attachment in otherAttachment)
                                {
                                    sb.AppendLine($"📄 {attachment.Filename}");
                                    sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
                                    sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
                                }
                                container.WithTextDisplay(sb.ToString());
                            }

                            container.WithSeparator();
                            container.WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)");
                        }
                    });

                var dmComponent = new ComponentBuilderV2()
                    .WithContainer(container =>
                    {
                        container.WithAccentColor(Color.Green);
                        container.WithTextDisplay($"""
                            ### 🎈 成功获取作品
                            """);
                        container.WithSeparator();

                        container.WithTextDisplay($"""
                            作品发布于: <t:{data.ReleaseAt.ToUnixTimeSeconds()}:f>丨<t:{data.ReleaseAt.ToUnixTimeSeconds()}:R>
                            作品发布在: {data.MessageJumpUrl} [点击跳转]({data.MessageJumpUrl})
                            作者: <@{data.AuthorId}>
                            """);
                            
                        container.WithSeparator();

                        if (data.CacheAttachmentUrls.Count > 0)
                        {
                            // 优先显示图片
                            List<IAttachment>? imageAttachment = _attachments?
                                .Where(a => IsImageAttachment(a))
                                .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            // 其他文件显示下载链接
                            List<IAttachment>? otherAttachment = _attachments?
                                .Where(a => !IsImageAttachment(a))
                                .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (imageAttachment?.Count > 0)
                            {
                                container.WithTextDisplay($"""
                                    **以下为图片附件:**
                                    > -# 右键或长按图片下载, 或点击超链接下载
                                    """);
                                MediaGalleryBuilder media = new();
                                StringBuilder sb = new();
                                foreach (var attachment in imageAttachment)
                                {
                                    media.AddItem(attachment.Url);
                                    sb.AppendLine($"🖼 {attachment.Filename}");
                                    sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
                                    sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
                                }
                                container.WithMediaGallery(media);
                                container.WithTextDisplay(sb.ToString());
                            }

                            container.WithSeparator();
                            if (otherAttachment?.Count > 0)
                            {
                                container.WithTextDisplay($"""
                                    **以下为非图片附件:**
                                    > -# 点击超链接下载
                                    """);
                                StringBuilder sb = new();
                                foreach (var attachment in otherAttachment)
                                {
                                    sb.AppendLine($"📄 {attachment.Filename}");
                                    sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
                                    sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
                                }
                                container.WithTextDisplay(sb.ToString());
                            }
                            container.WithSeparator();
                            container.WithSection(section => section
                                .WithTextDisplay(
                                    content: """
                                    ### 备注
                                    > -# 为本条消息创建搜索用备注, 可在右上角搜索备注中的内容进行快速索引本条消息
                                    """,
                                    id: 1200)
                                .WithAccessory(new ButtonBuilder(
                                    label: "创建备注",
                                    customId: "CreateRemark",
                                    style: ButtonStyle.Success,
                                    emote: new Emoji("🏷")
                                ))
                            );

                            container.WithSeparator();
                            container.WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)");
                        }
                    });

                return (component.Build(), dmComponent.Build(), true);
            }
            else
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"""
                        ### 🎈 到达限制
                        您今日通过本 BOT 获取的作品数量已到达本日上限, 限制将在 0 点恢复
                        > -# 当前状态: {tokenResult.RemainingToken}/{tokenResult.TokenLimit}
                        """)
                );
                return (component.Build(), null, false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetWorkAsync 发生未处理异常");
            throw;
        }
    }

    // 从本地文件重建远程 Discord 缓存消息
    private async Task<IMessage?> RebuildRemoteCacheAsync(WorkData data)
    {
        try
        {
            // 定位本地文件目录
            // Path: Base / localCache / GuildId / ChannelId(Parent) / ThreadId / ReleaseMessageId
            string localSaveDir = Path.Combine(
                _appPath,
                localCachePath,
                $"{data.GuildId}",
                $"{data.ChannelId}",
                $"{data.ThreadId}",
                $"{data.ReleaseMessageId}");

            if (!Directory.Exists(localSaveDir))
            {
                _logger.Warning("重建失败: 本地缓存目录不存在: {Path}", localSaveDir);
                return null;
            }

            var filePaths = Directory.GetFiles(localSaveDir)
                .Where(f => !Path.GetFileName(f).Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f) // 保持文件名排序，确保图片顺序一致
                .ToList();

            if (filePaths.Count == 0)
            {
                _logger.Warning("重建失败: 本地目录为空");
                return null;
            }

            // 确定目标频道 (Thread)
            IThreadChannel? targetThread = null;

            try
            {
                // 尝试获取原有的 Cache Thread
                // 注意：GetChannelAsync 可能返回 null (如果 Bot 没有权限或频道已删除)
                var channel = await Context.Client.Rest.GetChannelAsync(data.CacheThreadId);
                targetThread = channel as IThreadChannel;

                // 如果原有 Thread 不存在，尝试在 Parent Channel (Forum/Text) 重建 Thread
                if (targetThread == null)
                {
                    _logger.Info("原 CacheThread ({ThreadId}) 不存在，尝试在 Parent ({ParentId}) 重建...", data.CacheThreadId, data.CacheChannelId);

                    var parentChannel = await Context.Client.GetChannelAsync(data.CacheChannelId);

                    if (parentChannel is IForumChannel forumChannel)
                    {
                        // 重建 Forum Post
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            // 使用原标题或默认标题
                            string title = $"[Restored] {data.ReleaseMessageId}";
                            targetThread = await forumChannel.CreatePostAsync(title, text: $"Restored from local backup at {DateTimeOffset.UtcNow:O}");
                        });
                    }
                    // 几乎不可能进入该分支, 除非配置文件写错了
                    else if (parentChannel is ITextChannel textChannel)
                    {
                        // 在普通文字频道重建 Thread
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            // 需要先发一条消息才能开贴，或者直接 CreateThreadAsync
                            var startMsg = await textChannel.SendMessageAsync($"Restoring cache for {data.ReleaseMessageId}...");
                            targetThread = await textChannel.CreateThreadAsync($"[Restored] {data.ReleaseMessageId}", message: startMsg);
                        });
                    }

                    if (targetThread != null)
                    {
                        // 更新 data 中的 Thread ID，因为我们创建了新的
                        data.CacheThreadId = targetThread.Id;
                        data.IsUpdate = true; // 标记数据已变更
                        _logger.Info("✅ 已创建新的 Cache Thread: {NewThreadId}", targetThread.Id);
                    }
                    else
                    {
                        _logger.Error("❌ 无法创建新的 Cache Thread (找不到父频道或权限不足)。ParentId: {ParentId}", data.CacheChannelId);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "定位或创建目标 Thread 时发生异常");
                return null;
            }

            // 上传文件
            var streams = new List<FileStream>();
            try
            {
                var attachments = new List<FileAttachment>();
                foreach (var path in filePaths)
                {
                    var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    streams.Add(fs);
                    attachments.Add(new FileAttachment(fs, Path.GetFileName(path)));
                }

                IUserMessage? newMessage = null;

                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    newMessage = await targetThread.SendFilesAsync(
                        attachments,
                        text: $"Auto-restored cache at {DateTimeOffset.UtcNow:O}"
                    );
                });

                if (newMessage == null) return null;

                // 更新 Data 对象的内存状态
                // 这一步至关重要，让上层调用者拿到的 data 是新的
                data.CacheMessageUrl = newMessage.GetJumpUrl();
                data.CacheMessageId = newMessage.Id;
                data.CacheGuildId = targetThread.Guild.Id;
                data.CacheAttachmentUrls = newMessage.Attachments.Select(a => a.Url).ToList();

                return newMessage;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "上传文件到 Cache Thread 失败");
                return null;
            }
            finally
            {
                // 确保释放文件流
                foreach (var fs in streams)
                {
                    await fs.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "RebuildRemoteCacheAsync Exception");
            return null;
        }
    }

    // 私聊状态组件
    public MessageComponent GetDMJumpComponent(IUserMessage message)
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Green)
                .WithTextDisplay($"""
                    ### 同步发送私信成功:
                    {message.GetJumpUrl()} [点击跳转]({message.GetJumpUrl()})
                    """)
            )
            .Build();
    }
    
    public MessageComponent GetDMFailedComponent()
    {
        return new ComponentBuilderV2()
            .WithContainer(container => container
                .WithAccentColor(Color.Green)
                .WithTextDisplay($"""
                    ### 同步发送私信失败:
                    可能由于您已设置拒收BOT私信等问题, 同步发送至私信失败
                    """)
            )
            .Build();
    }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================

    // 兼容旧交互
    // Alerts
    [ComponentInteraction("Alerts:GetWork:0")]
    public async Task HandleOldInteractionAG0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("Alerts:GetWork:1")]
    public async Task HandleOldInteractionAG1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);

    [ComponentInteraction("Alerts:GetWorkByRoR:0")]
    public async Task HandleOldInteractionARoR0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("Alerts:GetWorkByRoR:1")]
    public async Task HandleOldInteractionARoR1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("Alerts:GetWorkByPwd:0")]
    public async Task HandleOldInteractionAPwd0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("Alerts:GetWorkByPwd:1")]
    public async Task HandleOldInteractionAPwd1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    // No Alerts
    [ComponentInteraction("GetWork:0:0")]
    public async Task HandleOldInteractionG0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("GetWork:0:1")]
    public async Task HandleOldInteractionG1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("GetWorkByRoR:0:0")]
    public async Task HandleOldInteractionRoR0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("GetWorkByRoR:0:1")]
    public async Task HandleOldInteractionRoR1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("GetWorkByPwd:0:0")]
    public async Task HandleOldInteractionPwd0Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);
    [ComponentInteraction("GetWorkByPwd:0:1")]
    public async Task HandleOldInteractionPwd1Async() => await HandleGetWorkAsync(((IThreadChannel)Context.Channel).OwnerId);


    [ComponentInteraction("GetWork:*")]
    public async Task HandleGetWorkAsync(ulong authorId)
    {
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [GetWork] 失败 - Context.Interaction 不是 SocketMessageComponent。User: {UserId}", Context.User.Id);
            return;
        }

        var messageId = interactionComponent.Message.Id;
        var userId = Context.User.Id;
        var threadId = Context.Channel.Id;


        _logger.Debug("交互开始: [GetWork] User: {UserId}, Thread: {ThreadId}, Message: {MessageId}", userId, threadId, messageId);

    
        try
        {
            await DeferAsync(ephemeral: true);

            //================
            // 获取作品信息
            //================
            _logger.Debug("[GetWork] 正在从数据库获取 WorkData...");
            var data = await _applicationService.GetWorkDataAsync(messageId);
            if (data is null)
            {
                _logger.Error("[GetWork] 获取失败 - 数据库返回 WorkData 为空。MessageId: {MessageId}", messageId);
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleAlerts"), ephemeral: true);
                return;
            }

            //================
            // 检查 如果是作者
            //================
            if (userId == authorId)
            {
                _logger.Debug("[GetWork] 鉴权: 用户是本帖作者，跳过所有限制检查。");
                var result = await GetWorkAsync(data, true);
                try
                {
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        await FollowupAsync(components: result.Component, ephemeral: true);
                    });
                    _logger.Info("[GetWork] 作者获取作品成功。User: {UserId}", userId);
                    
                    _logger.Debug("[GetWork] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                        result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                    if (result.DMComponent is not null && _assistService is not null)
                    {
                        var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                        });
                        if (message is not null)
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                            });
                        }
                        else
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[GetWork] 作者获取作品流程发生异常。User: {UserId}", userId);
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                }
                return;
            }

            //================
            // 检查 每日获取次数限制
            //================
            _logger.Debug("[GetWork] 开始检查配额限制...");
            // 检查令牌桶
            var tokenResult = _tokenBucketService.CheckToken(userId, 1);
            // 如果有过下载记录, 视为通过
            var hasDownloaded = await _applicationService.HasDownloadRecordAsync(userId, messageId);

            _logger.Debug("[GetWork] 配额检查详情: BucketAllow={BucketAllow}, Remaining={Remaining}, HasRecord={HasRecord}, ConsumeTokenConfig={IsConsumeToken}",

                tokenResult.Allowable, tokenResult.RemainingToken, hasDownloaded, data.IsConsumeToken);
            if (hasDownloaded)
            {
                tokenResult.Allowable = true;
                _logger.Debug("[GetWork] 用户已下载过该作品，允许获取，且不再扣除Token。");
            }
            // 如果是开放分享, 视为通过
            if (!data.IsConsumeToken)
            {
                tokenResult.Allowable = true;
                _logger.Debug("[GetWork] 作品配置为开放分享(不消耗Token)，允许获取。");
            } 

            if (tokenResult.Allowable == false)
            {
                _logger.Info("[GetWork] 拒绝访问: 用户今日配额已用尽。User: {UserId}, Remaining: {Remaining}/{Limit}", 
                    userId, tokenResult.RemainingToken, tokenResult.TokenLimit);
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"""
                        ### 🎈 到达限制
                        您今日通过本 BOT 获取的作品数量已到达本日上限, 限制将在 0 点恢复
                        > -# 当前状态: {tokenResult.RemainingToken}/{tokenResult.TokenLimit}
                        """)
                );
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await FollowupAsync(components: component.Build(), ephemeral: true);
                });
                return;
            }

            //================
            // 检查 是否满足可以提前检查的条件
            //================
            _logger.Debug("[GetWork] 开始检查前置条件 (Model: {Model})...", data.ReleaseModel);
            switch (data.ReleaseModel)
            {
                // 需要 点赞
                case ReleaseModel.Reaction:
                    {
                        var hasReaction = await _applicationService.HasReactionRecordAsync(userId, threadId);
                        if (hasReaction)
                        {
                            _logger.Debug("[GetWork] 前置条件通过: 检测到点赞。");
                            break;
                        }
                        else
                        {
                            _logger.Info("[GetWork] 拒绝访问: 未满足点赞条件。User: {UserId}, Thread: {ThreadId}", userId, threadId);
                            var component = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay($"""
                                    ### ⚠️ 前置条件未满足
                                    至少先对帖子首楼点一个赞哦
                                    """)
                            );
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: component.Build(), ephemeral: true);
                            });
                            return;
                        }
                    }
                // 需要 点赞或回复
                case ReleaseModel.ReactionOrReply:
                    {
                        var hasReaction = await _applicationService.HasReactionRecordAsync(userId, threadId);
                        var hasReply = await _applicationService.HasMessageRecordAsync(userId, threadId);
                        if (hasReaction || hasReply)
                        {
                            _logger.Debug("[GetWork] 前置条件通过: 检测到点赞({HasReaction})或回复({HasReply})。", hasReaction, hasReply);
                            break;
                        }
                        else
                        {
                            _logger.Info("[GetWork] 拒绝访问: 未满足点赞或回复条件。User: {UserId}, Thread: {ThreadId}", userId, threadId);
                            var component = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay($"""
                                    ### ⚠️ 前置条件未满足
                                    至少先对帖子首楼点一个赞或发送一条回复哦
                                    """)
                            );
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: component.Build(), ephemeral: true);
                            });
                            return;
                        }
                    }
                // 不需要 点赞 or 点赞或回复
                default:
                    _logger.Debug("[GetWork] 无需特殊前置条件。");
                    break;
            }

            // 根据作品模式选择回复什么
            if (data.EnableAlerts)
            {
                _logger.Debug("[GetWork] 流程中断: 需要用户确认作者声明。");
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"""
                            ### 🎈 稍等一下!
                            这是作者的声明
                            请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
                            {(string.IsNullOrWhiteSpace(data.Alerts) ? "作者未设置具体内容" : data.Alerts.Slice(503, "..."))}
                            """)
                        .WithActionRow(action => action
                            .WithButton("我已阅读并确认", $"Alerts:{data.ReleaseMessageId}", ButtonStyle.Success, emote: new Emoji("✅"))
                        )
                );
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await FollowupAsync(components: component.Build(), ephemeral: true);
                });
                return;
            }
            else
            {
                if (data.NeedPassword)
                {
                    _logger.Debug("[GetWork] 流程中断: 需要用户输入提取码。");
                    // 需要提取码
                    var pwdComponent = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Blue)
                            .WithTextDisplay("### 🔒 需要提取码", 1000)
                            .WithTextDisplay("获取该作品需要输入提取码。", 1001)
                            .WithActionRow(row => row
                                .WithButton(
                                    label: "输入提取码",
                                    customId: $"GetWorkByPwd:{data.ReleaseMessageId}",
                                    style: ButtonStyle.Primary,
                                    emote: new Emoji("🔑"))
                            )
                        );
                    await FollowupAsync(components: pwdComponent.Build(), ephemeral: true);
                    return;
                }
                else
                {
                    _logger.Debug("[GetWork] 流程继续: 尝试执行最终获取逻辑...");
                    // 不需要需要提取码
                    var result = await GetWorkAsync(data, hasDownloaded);
                    try
                    {
                        if (result.Component is not null)
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: result.Component, ephemeral: true);
                            });
                            _logger.Info("[GetWork] 作品获取成功，正在写入日志。User: {UserId}, Work: {MessageId}", userId, messageId);
                            // 记录 log
                            var downloadLog = new DownloadLog()
                            {
                                UserId = userId,
                                ThreadId = threadId,
                                ReleaseMessageId = messageId
                            };

                            await _applicationService.InsertDownloadLogAsync(downloadLog);
                            
                            _logger.Debug("[GetWork] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                                result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                            if (result.DMComponent is not null && _assistService is not null)
                            {
                                var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                                });
                                if (message is not null)
                                {
                                    await _httpRetryPolicy.ExecuteAsync(async () =>
                                    {
                                        await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                                    });
                                }
                                else
                                {
                                    await _httpRetryPolicy.ExecuteAsync(async () =>
                                    {
                                        await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                                    });
                                }
                            }
                        }
                        else
                        {
                            _logger.Error("[GetWork] 严重错误: GetWorkAsync 返回了 null Component。User: {UserId}", userId);
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: result.Component is not null"), ephemeral: true);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "[GetWork] 最终获取阶段发生异常，正在执行 Token 回滚。User: {UserId}", userId);
                        if (result.Allowable)
                        {
                            _tokenBucketService.UndoConsumeToken(userId, 1);
                            _logger.Debug("[GetWork] Token 已回滚。");
                        }
                        _logger.Error(ex);
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                        });
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleGetWorkAsync Exception");
        }
    }
    
    [ComponentInteraction("Alerts:*")]
    public async Task HandleAlertsCommandAsync(ulong messageId)
    {
        var userId = Context.User.Id;
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [Alerts] 失败 - Context.Interaction 不是 SocketMessageComponent。User: {UserId}", userId);
            return;
        }
        try
        {
            await DeferAsync(ephemeral: true);
            _logger.Debug("交互开始: [Alerts] (用户已确认声明) User: {UserId}, Message: {MessageId}", userId, messageId);
            //================
            // 获取作品信息
            //================
            _logger.Debug("[Alerts] 正在从数据库获取 WorkData...");
            var data = await _applicationService.GetWorkDataAsync(messageId);
            if (data is null)
            {
                _logger.Error("[Alerts] 获取失败 - 数据库返回 WorkData 为空。MessageId: {MessageId}", messageId);
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleAlerts"), ephemeral: true);
                return;
            }

            // 如果有过下载记录, 视为通过
            var hasDownloaded = await _applicationService.HasDownloadRecordAsync(userId, messageId);
            if (hasDownloaded)
            {
                _logger.Debug("[GetWork] 用户已下载过该作品，不再扣除Token。");
            }

            if (data.NeedPassword)
            {
                _logger.Debug("[Alerts] 流程流转: 用户确认声明 -> 检测到 NeedPassword=true -> 跳转至提取码输入。");
                // 需要提取码
                var pwdComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Blue)
                        .WithTextDisplay("### 🔒 需要提取码", 1000)
                        .WithTextDisplay("获取该作品需要输入提取码。", 1001)
                        .WithActionRow(row => row
                            .WithButton(
                                label: "输入提取码",
                                customId: $"GetWorkByPwd:{data.ReleaseMessageId}",
                                style: ButtonStyle.Primary,
                                emote: new Emoji("🔑"))
                        )
                    );
                await FollowupAsync(components: pwdComponent.Build(), ephemeral: true);
                return;
            }
            else
            {
                _logger.Debug("[Alerts] 流程流转: 用户确认声明 -> 无提取码需求 -> 尝试执行最终获取逻辑。");
                // 不需要需要提取码
                var result = await GetWorkAsync(data, hasDownloaded);
                try
                {
                    if (result.Component is not null)
                    {
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: result.Component, ephemeral: true);
                        });
                        _logger.Info("[Alerts] 作品获取成功 (声明确认后直连)。User: {UserId}, Work: {MessageId}", userId, messageId);
                        // 记录 log
                        var downloadLog = new DownloadLog()
                        {
                            UserId = userId,
                            ThreadId = Context.Channel.Id,
                            ReleaseMessageId = interactionComponent.Message.Id
                        };

                        await _applicationService.InsertDownloadLogAsync(downloadLog);

                        _logger.Debug("[Alerts] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                            result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                        if (result.DMComponent is not null && _assistService is not null)
                        {
                            var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                            });
                            if (message is not null)
                            {
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                                });
                            }
                            else
                            {
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                                });
                            }
                        }
                    }
                    else
                    {
                        _logger.Error("[Alerts] 严重错误: GetWorkAsync 返回了 null Component。User: {UserId}", userId);
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: result.Component is not null"), ephemeral: true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[Alerts] 最终获取阶段发生异常，正在执行 Token 回滚。User: {UserId}", userId);
                    if (result.Allowable)
                    {
                        _logger.Debug("[Alerts] Token 已回滚。");
                        _tokenBucketService.UndoConsumeToken(userId, 1);
                    }
                    _logger.Error(ex);
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                    });
                }
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleAlertsCommandAsync Exception");
        }
    }

    [ComponentInteraction("GetWorkByPwd:*")]
    public async Task HandleGetWorkByPwdAsync(ulong messageId)
    {
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [GetWorkByPwd] Context.Interaction is Not SocketMessageComponent");
            return;
        }
        try
        {
            var modal = new ModalBuilder()
                .WithTitle("输入提取码")
                .WithCustomId($"GetWorkByPwd:Modal:{messageId}")
                .AddTextInput(
                    label: "设置提取码 (1 到 50 字)",
                    customId: "Password",
                    placeholder: "填写贴内的提取码...",
                    maxLength: 50,
                    minLength: 1,
                    required: true,
                    style: TextInputStyle.Short
                );

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await RespondWithModalAsync(modal.Build());
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[GetWorkByPwd] HandleGetWorkByPwdAsync 发生未处理异常。");
        }
    }
    
    [ModalInteraction("GetWorkByPwd:Modal:*")]
    public async Task HandleGetWorkByPwdModalAsync(ulong messageId, PasswordInputModal modal)
    {
        var userId = Context.User.Id;
        if (Context.Interaction is not IModalInteraction modalInteraction)
        {
            _logger.Warning("交互回调: [Modal:Pwd] 失败 - Context.Interaction 不是 IModalInteraction。User: {UserId}, Type: {Type}", userId, Context.Interaction?.GetType().Name);
            return;
        }

        try
        {
            _logger.Debug("交互开始: [Modal:Pwd] (用户提交提取码) User: {UserId}, Message: {MessageId}", userId, messageId);

            await DeferAsync(ephemeral: true);


            var password = modal.Password.Trim();

            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.Debug("[Modal:Pwd] 验证失败: 用户提交了空密码或纯空格。");
                await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 密码格式错误\n密码不能为空或只有空格"), ephemeral: true);
                return;
            }

            _logger.Debug("[Modal:Pwd] 正在从数据库获取 WorkData 以验证密码...");
            var data = await _applicationService.GetWorkDataAsync(messageId);

            if (data is null)
            {
                _logger.Error("[Modal:Pwd] 获取失败 - 数据库返回 WorkData 为空。MessageId: {MessageId}", messageId);
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleGetWorkByPwdModalAsync"), ephemeral: true);
                return;
            }

            var hasDownloaded = await _applicationService.HasDownloadRecordAsync(userId, messageId);
            if (hasDownloaded)
            {
                _logger.Debug("[GetWork] 用户已下载过该作品，不再扣除Token。");
            }

            if (password == data.ReleasePassword)
            {
                _logger.Debug("[Modal:Pwd] 密码验证成功。准备获取资源...");
                var result = await GetWorkAsync(data, hasDownloaded);
                try
                {
                    if (result.Component is not null)
                    {
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: result.Component, ephemeral: true);
                        });
                        _logger.Info("[Modal:Pwd] 作品获取成功 (密码验证通过)。User: {UserId}, Work: {MessageId}", userId, messageId);
                        // 记录 log
                        var downloadLog = new DownloadLog()
                        {
                            UserId = userId,
                            ThreadId = Context.Channel.Id,
                            ReleaseMessageId = data.ReleaseMessageId
                        };

                        await _applicationService.InsertDownloadLogAsync(downloadLog);

                        _logger.Debug("[Alerts] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                            result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                        if (result.DMComponent is not null && _assistService is not null)
                        {
                            var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                            });
                            if (message is not null)
                            {
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                                });
                            }
                            else
                            {
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                                });
                            }
                        }
                    }
                    else
                    {
                        _logger.Error("[Modal:Pwd] 严重错误: GetWorkAsync 返回了 null Component。User: {UserId}", userId);
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: result.Component is not null"), ephemeral: true);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[Modal:Pwd] 最终获取阶段发生异常，正在执行 Token 回滚。User: {UserId}", userId);
                    if (result.Allowable)
                    {
                        _tokenBucketService.UndoConsumeToken(userId, 1);
                        _logger.Debug("[Modal:Pwd] Token 已回滚。");
                    }
                    _logger.Error(ex);
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                    });
                }
            }
            else
            {
                _logger.Info("[Modal:Pwd] 密码验证失败。User: {UserId}", userId);

                var pwdErrorComponent = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"""
                        ### ⚠️ 提取码错误
                        请检查作者发布的提取码和输入的提取码
                        """)
                );
                await FollowupAsync(components: pwdErrorComponent.Build(), ephemeral: true);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[Modal:Pwd] HandleGetWorkByPwdModalAsync 发生异常。User: {UserId}", Context.User.Id);
        }
    }
    #endregion

    // =============================================
    #region  斜杠命令 
    // =============================================
    [SlashCommand("获取作品", "获取本帖发布的最新作品")]
    public async Task GetWorkCommandAsync()
    {
        try
        {
            await GetWorkByCommand(null);
        }

        catch (Exception ex)
        {
            _logger.Error(ex, "[Slash:GetWork] 命令执行发生异常。User: {UserId}", Context.User.Id);
        }
    }

    [SlashCommand("获取编号作品", "通过指定的作品ID编号获取本帖贴内发布的作品")]
    public async Task GetWorkCommandAsync([Summary("作品编号", "该作品的编号 (位于作品发布处的消息内)")] long workId)
    {
        try
        {
            await GetWorkByCommand(workId);
        }
        
        catch (Exception ex)
        {
            _logger.Error(ex, "[Slash:GetWork] 命令执行发生异常。User: {UserId}", Context.User.Id);
        }
    }
    #endregion
    
    private async Task GetWorkByCommand(long? workId)
    {
        var userId = Context.User.Id;
        var channelId = Context.Channel.Id;
        try
        {
            _logger.Debug("命令开始: [Slash:GetWork] User: {UserId}, Channel: {ChannelId}, TargetWorkId: {WorkId}",
                userId, channelId, workId?.ToString() ?? "Latest");
                
            // 基础鉴权
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                _logger.Info("[Slash:GetWork] 鉴权失败: {Status}", authResult.Status);
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }
            if (Context.Channel is not IThreadChannel threadChannel)
            {
                _logger.Warning("[Slash:GetWork] 环境错误: 当前频道不是 ThreadChannel。Id: {ChannelId}", channelId);
                await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 错误\n请在帖子内使用此命令"), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            WorkData? data;

            if (workId == null)
            {
                _logger.Debug("[Slash:GetWork] 模式: 获取本帖最新作品。ThreadId: {ThreadId}", channelId);
                data = await _applicationService.GetLatestWorkDataByThreadIdAsync(Context.Channel.Id);
                if (data is null)
                {
                    _logger.Info("[Slash:GetWork] 查询无果: 本帖内未查询到作品数据。");
                    await FollowupAsync(components: _componentUtils.TextErrorComponente("### 获取失败\n本帖内未查询到使用本BOT发布的作品"), ephemeral: true);
                    return;
                }
            }
            else
            {
                _logger.Debug("[Slash:GetWork] 模式: 获取指定ID作品。ThreadId: {ThreadId}, WorkId: {WorkId}", channelId, workId);
                data = await _applicationService.GetWorkDataByWorkIdAsync(Context.Channel.Id, workId.Value);
                
                if (data is null)
                {
                    _logger.Info("[Slash:GetWork] 查询无果: 指定ID的作品不在本帖内或不存在或已删除。WorkId: {WorkId}", workId);
                    await FollowupAsync(components: _componentUtils.TextErrorComponente("### 获取作品失败\n请检查输入的作品ID, 确保是这个帖子的作品, 且发布处仍然存在"), ephemeral: true);
                    return;
                }
            }

            //================
            // 检查 如果是作者
            //================
            if (userId == threadChannel.OwnerId)
            {
                var result = await GetWorkAsync(data, true);
                try
                {
                    _logger.Debug("[Slash:GetWork] 鉴权: 用户是本帖作者，跳过所有限制检查。");
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        await FollowupAsync(components: result.Component, ephemeral: true);
                    });
                    _logger.Info("[Slash:GetWork] 作者获取作品成功。");

                    _logger.Debug("[Alerts] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                        result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                    if (result.DMComponent is not null && _assistService is not null)
                    {
                        var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                        });
                        if (message is not null)
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                            });
                        }
                        else
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[Slash:GetWork] 作者获取流程发生异常。");
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                }
                return;
            }

            //================
            // 检查 每日获取次数限制
            //================
            _logger.Debug("[Slash:GetWork] 开始检查配额限制...");
            // 检查令牌桶
            var tokenResult = _tokenBucketService.CheckToken(userId, 1);
            // 如果有过下载记录, 视为通过
            var hasDownloaded = await _applicationService.HasDownloadRecordAsync(userId, data.ReleaseMessageId);

            _logger.Debug("[Slash:GetWork] 配额详情: BucketAllow={BucketAllow}, Remaining={Remaining}, HasRecord={HasRecord}, ConsumeTokenConfig={IsConsumeToken}",
             tokenResult.Allowable, tokenResult.RemainingToken, hasDownloaded, data.IsConsumeToken);
            if (hasDownloaded)
            {
                tokenResult.Allowable = true;
                _logger.Debug("[Slash:GetWork] 用户已下载过该作品，允许获取，且不再扣除Token。");
            }
            // 如果是开放分享, 视为通过
            if (!data.IsConsumeToken)
            {
                tokenResult.Allowable = true;
                _logger.Debug("[Slash:GetWork] 作品配置为开放分享(不消耗Token)，允许获取。");
            } 
            

            if (tokenResult.Allowable == false)
            {
                _logger.Info("[GetWork] 拒绝访问: 用户今日配额已用尽。User: {UserId}, Remaining: {Remaining}/{Limit}", 
                    userId, tokenResult.RemainingToken, tokenResult.TokenLimit);
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"""
                        ### 🎈 到达限制
                        您今日通过本 BOT 获取的作品数量已到达本日上限, 限制将在 0 点恢复
                        > -# 当前状态: {tokenResult.RemainingToken}/{tokenResult.TokenLimit}
                        """)
                );
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await FollowupAsync(components: component.Build(), ephemeral: true);
                });
                return;
            }

            //================
            // 检查 是否满足可以提前检查的条件
            //================
            _logger.Debug("[Slash:GetWork] 开始检查前置条件 (Model: {Model})...", data.ReleaseModel);

            switch (data.ReleaseModel)
            {
                // 需要点赞或回复
                case ReleaseModel.Reaction:
                    {
                        var hasReaction = await _applicationService.HasReactionRecordAsync(userId, Context.Channel.Id);
                        if (hasReaction)
                        {
                            _logger.Debug("[Slash:GetWork] 前置条件通过: 检测到点赞。");
                            break;
                        }
                        else
                        {
                            _logger.Info("[Slash:GetWork] 拒绝访问: 未满足点赞条件。");
                            var component = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay($"""
                                    ### ⚠️ 前置条件未满足
                                    至少先对帖子首楼点一个赞哦
                                    """)
                            );
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: component.Build(), ephemeral: true);
                            });
                            return;
                        }
                    }
                case ReleaseModel.ReactionOrReply:
                    {
                        
                        var hasReaction = await _applicationService.HasReactionRecordAsync(userId, Context.Channel.Id);
                        var hasReply = await _applicationService.HasMessageRecordAsync(userId, Context.Channel.Id);
                        if (hasReaction || hasReply)
                        {
                            _logger.Debug("[Slash:GetWork] 前置条件通过: 检测到点赞({HasReaction})或回复({HasReply})。", hasReaction, hasReply);
                            break;
                        }
                        else
                        {
                            _logger.Info("[Slash:GetWork] 拒绝访问: 未满足点赞或回复条件。");
                            var component = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Red)
                                    .WithTextDisplay($"""
                                    ### ⚠️ 前置条件未满足
                                    至少先对帖子首楼点一个赞或发送一条回复哦
                                    """)
                            );
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: component.Build(), ephemeral: true);
                            });
                            return;
                        }
                    }
                // 不需要点赞或回复
                default:
                    _logger.Debug("[Slash:GetWork] 无需特殊前置条件。");
                    break;
            }

            // 根据作品模式选择回复什么
            if (data.EnableAlerts)
            {
                var component = new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Green)
                        .WithTextDisplay($"""
                            ### 🎈 稍等一下!
                            这是作者的声明
                            请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
                            {(string.IsNullOrWhiteSpace(data.Alerts) ? "作者未设置具体内容" : data.Alerts.Slice(503, "..."))}
                            """)
                        .WithActionRow(action => action
                            .WithButton("我已阅读并确认", $"Alerts:{data.ReleaseMessageId}", ButtonStyle.Success, emote: new Emoji("✅"))
                        )
                );
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await FollowupAsync(components: component.Build(), ephemeral: true);
                });
                return;
            }
            else
            {
                if (data.NeedPassword)
                {
                    _logger.Debug("[Slash:GetWork] 流程中断: 需要用户输入提取码。");
                    // 需要提取码
                    var pwdComponent = new ComponentBuilderV2()
                        .WithContainer(container => container
                            .WithAccentColor(Color.Blue)
                            .WithTextDisplay("### 🔒 需要提取码", 1000)
                            .WithTextDisplay("获取该作品需要输入提取码。", 1001)
                            .WithActionRow(row => row
                                .WithButton(
                                    label: "输入提取码",
                                    customId: $"GetWorkByPwd:{data.ReleaseMessageId}",
                                    style: ButtonStyle.Primary,
                                    emote: new Emoji("🔑"))
                            )
                        );
                    await FollowupAsync(components: pwdComponent.Build(), ephemeral: true);
                    return;
                }
                else
                {
                    // 不需要需要提取码
                    _logger.Debug("[Slash:GetWork] 流程继续: 尝试执行最终获取逻辑...");
                    var result = await GetWorkAsync(data, hasDownloaded);
                    try
                    {
                        if (result.Component is not null)
                        {
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: result.Component, ephemeral: true);
                            });
                            _logger.Info("[Slash:GetWork] 作品获取成功。User: {UserId}, Work: {MessageId}", userId, data.ReleaseMessageId);
                            // 记录 log
                            var downloadLog = new DownloadLog()
                            {
                                UserId = userId,
                                ThreadId = Context.Channel.Id,
                                ReleaseMessageId = data.ReleaseMessageId
                            };
                            
                            await _applicationService.InsertDownloadLogAsync(downloadLog);

                            _logger.Debug("[Alerts] 尝试发送私信, 当前服务可用状态: DMComponent Available: {DMComponent}, AssistService Available: {AssistService}",
                                result.DMComponent is not null ? true : false, _assistService is not null ? true : false);
                            if (result.DMComponent is not null && _assistService is not null)
                            {
                                var message = await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    return await _assistService.SendWorkViaDM(userId, result.DMComponent);
                                });
                                if (message is not null)
                                {
                                    await _httpRetryPolicy.ExecuteAsync(async () =>
                                    {
                                        await FollowupAsync(components: GetDMJumpComponent(message), ephemeral: true);
                                    });
                                }
                                else
                                {
                                    await _httpRetryPolicy.ExecuteAsync(async () =>
                                    {
                                        await FollowupAsync(components: GetDMFailedComponent(), ephemeral: true);
                                    });
                                }
                            }
                        }
                        else
                        {
                            _logger.Error("[Slash:GetWork] 严重错误: GetWorkAsync 返回了 null Component。User: {UserId}", userId);
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: result.Component is not null"), ephemeral: true);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "[Slash:GetWork] 最终获取阶段发生异常，正在执行 Token 回滚。User: {UserId}", userId);
                        if (result.Allowable)
                        {
                            _tokenBucketService.UndoConsumeToken(userId, 1);
                            _logger.Debug("[Slash:GetWork] Token 已回滚。");
                        }
                        _logger.Error(ex);
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "HandleGetWorkAsync: catch Exception"), ephemeral: true);
                        });
                    }
                    return;
                }
            }
            
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetWorkByCommand Exception");
        }
    }
    
    private static bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}

