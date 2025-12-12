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
    public GetRelease(
        ILogger logger,
        AuthUtil authUtil,
        HttpClient httpClient,
        CacheService cacheService,
        ApplicationService applicationService,
        TokenBucketService tokenBucketService,
        [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
    {
        _logger = logger.ForContext("Release");
        _authUtil = authUtil;
        _httpClient = httpClient;
        _cacheService = cacheService;
        _applicationService = applicationService;
        _componentUtils = new(_logger);
        _tokenBucketService = tokenBucketService;
        _httpRetryPolicy = httpRetryPolicy;
    }

    // =============================================
    #region 业务方法
    // =============================================
    public async Task<MessageComponent> GetWorkAsync(WorkData data, int remainingToken, int tokenLimit, bool isConsumed)
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
                _logger.Warning("无法获取远程缓存消息，尝试回退到本地缓存。CacheId: {CacheMessageId}", data.CacheMessageId);
                message = null;
            }

            // 如果远程获取成功，返回标准的图库/下载组件
            if (message is not null)
            {
                var _attachments = message.Attachments;
                return new ComponentBuilderV2()
                    .WithContainer(container =>
                    {
                        container.WithAccentColor(Color.Green); // 远程获取成功用绿色
                        container.WithTextDisplay($"""
                            ### 🎈 获取作品
                            > -# 今日剩余可获取作品量: {remainingToken}/{tokenLimit}
                            """);
                        container.WithSeparator();

                        if (data.CacheAttachmentUrls.Count > 0)
                        {
                            // 优先显示图片
                            List<IAttachment>? imageAttachment = _attachments?
                                .Where(a => IsImageAttachment(a))
                                .ToList();

                            // 其他文件显示下载链接
                            List<IAttachment>? otherAttachment = _attachments?
                                .Where(a => !IsImageAttachment(a))
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
                    }).Build();
            }

            // 远程获取失败，进入本地回退逻辑
            else
            {
                // 构建本地存储路径 (需与 Release.cs 中的保存逻辑一致)
                // Path: Base / localCache / GuildId / ChannelId(Parent) / ThreadId / ReleaseMessageId
                string localSaveDir = Path.Combine(
                    _appPath,
                    localCachePath,
                    $"{data.GuildId}",
                    $"{data.ChannelId}",
                    $"{data.ThreadId}",
                    $"{data.ReleaseMessageId}");

                if (Directory.Exists(localSaveDir))
                {
                    // 获取所有文件，排除 metadata.json
                    var filePaths = Directory.GetFiles(localSaveDir)
                        .Where(f => !Path.GetFileName(f).Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (filePaths.Count > 0)
                    {
                        // 准备文件附件
                        var attachments = new List<FileAttachment>();
                        var streams = new List<FileStream>(); // 用于后续释放资源

                        try
                        {
                            foreach (var filePath in filePaths)
                            {
                                // 使用 FileShare.Read 允许并发读取
                                var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                streams.Add(fs);
                                attachments.Add(new FileAttachment(fs, Path.GetFileName(filePath)));
                            }

                            // 直接发送文件 (Followup)
                            // 注意：因为 GetWorkAsync 返回的是 Component，而 Component 不能带文件，
                            // 所以这里必须直接调用 Interaction 发送文件。
                            await Context.Interaction.FollowupWithFilesAsync(
                                attachments,
                                text: $"> ⚠️ **远程缓存不可用, 已自动从本地备份提取 {filePaths.Count} 个文件**",
                                ephemeral: true
                            );

                            // 返回一个提示性的组件，告知调用者已处理完毕
                            return new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Orange) // 本地回退用橙色警告色
                                    .WithTextDisplay($"""
                                    ### 📂 本地备份已上传
                                    由于远程缓存的 Discord CDN 资源失效, Bot 已尝试从服务器本地缓存上传文件。
                                    > -# 今日剩余状态: {remainingToken}/{tokenLimit}
                                    """)
                                    .WithSeparator()
                                    .WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)")
                            ).Build();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "从本地缓存上传文件时发生错误");
                            // 如果上传出错，流转到下方的错误返回
                        }
                        finally
                        {
                            // 清理流资源
                            foreach (var fs in streams)
                            {
                                await fs.DisposeAsync();
                            }
                        }
                    }
                }

                // 远程和本地都失败
                _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, 1, isConsumed);
                return new ComponentBuilderV2()
                    .WithContainer(container => container
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay($"""
                        ### ❌ 获取失败
                        无法获取作品：远程缓存消息不存在或已被删除，且本地未找到备份文件。
                        > -# 今日剩余状态: {_tokenBucketService.GetRemainingToken(Context.User.Id)}/{tokenLimit}
                        """)
                        .WithSeparator()
                ).Build();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "GetWorkAsync 发生未处理异常");
            throw;
        }
    }
    #endregion

    // =============================================
    #region  交互回调
    // =============================================
    [ComponentInteraction("Alerts:*:*")]
    public async Task HandleAlertsGetWorkAsync(string mode, int token)
    {
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [GetWork] Context.Interaction is Not SocketMessageComponent");
            return;
        }
        try
        {
            await DeferAsync(ephemeral: true);

            // 2. 获取数据以读取 Alerts 内容
            var data = await _applicationService.GetWorkDataAsync(interactionComponent.Message.Id);

            // 如果数据丢失，返回错误
            if (data is null)
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleAlerts"), ephemeral: true);
                return;
            }

            var component = new ComponentBuilderV2()
                .WithContainer(container =>
                {
                    container.WithAccentColor(Color.Blue);
                    container.WithTextDisplay("""
                        ### 🎈 稍等一下!
                        这是作者的声明
                        请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
                        """);
                    
                    // 核心：展示数据库中的 Alert 内容
                    container.WithTextDisplay($"> {data.Alerts ?? "(作者未设置具体内容)"}", 1000); 
                    
                    container.WithSeparator();
                    
                    // 根据传入的 mode 参数，生成对应的下一步按钮
                    // 这些按钮的 CustomId 不带 "Alerts:" 前缀，因此点击后会直接触发原本的获取逻辑
                    switch (mode)
                    {
                        case "GetWork":
                            container.WithActionRow(action => action
                                .WithButton("我已阅读并确认", $"GetWork:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
                            );
                            break;
                            
                        case "GetWorkByRoR":
                             container.WithActionRow(action => action
                                .WithButton("我已阅读并确认", $"GetWorkByRoR:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
                            );
                            break;
                            
                        case "GetWorkByPwd":
                            container.WithActionRow(action => action
                                .WithButton("我已阅读并确认", $"GetWorkByPwd:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
                            );
                            break;
                            
                        default:
                            // 防止意外的 mode
                            container.WithTextDisplay("⚠️ 内部错误：未知的获取模式");
                            break;
                    }
                });

            await FollowupAsync(components: component.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleGetWorkAsync Exception");
        }
    }
    
    [ComponentInteraction("GetWork:*:*")]
    public async Task HandleGetWorkAsync(ulong messageId, int token)
    {
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [GetWork] Context.Interaction is Not SocketMessageComponent");
            return;
        }
        try
        {
            // 如果是作者自己
            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id == threadChannel.OwnerId)
            {
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, 0);
                await DeferAsync(ephemeral: true);

                var data = await _applicationService.GetWorkDataAsync(messageId == 0 ? interactionComponent.Message.Id : messageId);

                if (data is not null)
                {
                    var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                    await FollowupAsync(components: component, ephemeral: true);
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                }
                return;
            }
            else
            {
                var tokenCost = 1;
                if(await _applicationService.HasDownloadRecordAsync(Context.User.Id, messageId == 0 ? interactionComponent.Message.Id : messageId))
                {
                    tokenCost = 0;
                }
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, tokenCost);

                try
                {
                    if (token == 0)
                    {
                        allowable = true;
                    }
                    
                    if (!allowable)
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"""
                                ### 🎈 到达限制
                                您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
                                > -# 当前状态: {remainingToken}/{tokenLimit}
                                """)
                        );
                        await RespondAsync(components: component.Build(), ephemeral: true);
                        return;
                    }
    
                    await DeferAsync(ephemeral: true);
    
                    var data = await _applicationService.GetWorkDataAsync(messageId == 0 ? interactionComponent.Message.Id : messageId);
    
                    if (data is not null)
                    {
    
                        var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                        await FollowupAsync(components: component, ephemeral: true);
    
                        var downloadLog = new DownloadLog()
                        {
                            UserId = Context.User.Id,
                            ThreadId = Context.Channel.Id,
                            ReleaseMessageId = interactionComponent.Message.Id
                        };
                        await _applicationService.InsertDownloadLogAsync(downloadLog);
                    }
                    else
                    {
                        _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                        await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                    }
                }
                catch
                {
                    _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleGetWorkAsync Exception");
        }
    }

    [ComponentInteraction("GetWorkByRoR:*:*")]
    public async Task HandleGetWorkByRoRAsync(ulong messageId, int token)
    {
        if (Context.Interaction is not SocketMessageComponent interactionComponent)
        {
            _logger.Warning("交互回调: [GetWorkByRoR] Context.Interaction is Not SocketMessageComponent");
            return;
        }
        // [DEBUG] 1. 进入方法
        _logger.Debug("[GetWorkByRoR] 收到交互请求。MessageId 参数: {ParamMessageId}, 交互 MessageId: {InteractionMessageId}", 
            messageId, interactionComponent.Message.Id);
        try
        {

            // 如果是作者自己
            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id == threadChannel.OwnerId)
            {
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, 0);
                await DeferAsync(ephemeral: true);

                var data = await _applicationService.GetWorkDataAsync(messageId == 0 ? interactionComponent.Message.Id : messageId);

                if (data is not null)
                {
                    var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                    await FollowupAsync(components: component, ephemeral: true);
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                }
                return;
            }
            else
            {
                // [DEBUG] 2. 尝试获取 Token
                _logger.Debug("[GetWorkByRoR] 用户 {UserId} 尝试获取 Token...", Context.User.Id);

                var tokenCost = 1;
                if(await _applicationService.HasDownloadRecordAsync(Context.User.Id, messageId == 0 ? interactionComponent.Message.Id : messageId))
                {
                    tokenCost = 0;
                }
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, tokenCost);

                try
                {
                    if (token == 0)
                    {
                        allowable = true;
                    }
    
                    // [DEBUG] 3. Token 结果
                    _logger.Debug("[GetWorkByRoR] Token 获取结果: Allowable={Allowable}, Remaining={Remaining}/{Limit}", 
                        allowable, remainingToken, tokenLimit);
    
                    if (!allowable)
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"""
                                ### 🎈 到达限制
                                您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
                                > -# 当前状态: {remainingToken}/{tokenLimit}
                                """)
                        );
                        await RespondAsync(components: component.Build(), ephemeral: true);
                        return;
                    }
    
                    await DeferAsync(ephemeral: true);
                    // [DEBUG] 4. Defer 完成
                    _logger.Debug("[GetWorkByRoR] DeferAsync 完成，开始检查前置条件...");
    
                    // [DEBUG] 5. 检查反应和回复记录
                    var checkChannelId = interactionComponent.Channel.Id;
                    var checkUserId = Context.User.Id;
                    _logger.Debug("[GetWorkByRoR] 正在检查 ChannelId: {ChannelId}, UserId: {UserId} 的反应/回复记录", checkChannelId, checkUserId);
    
                    var hasReaction = await _applicationService.HasReactionRecordAsync(checkUserId, checkChannelId);
                    var hasReply = await _applicationService.HasMessageRecordAsync(checkUserId,checkChannelId);
    
                    // [DEBUG] 6. 检查结果
                    _logger.Debug("[GetWorkByRoR] 前置条件检查结果: HasReaction={HasReaction}, HasReply={HasReply}", hasReaction, hasReply);
    
                    if (hasReaction || hasReply)
                    {
                        // [DEBUG] 7. 准备获取 WorkData
                        var targetMessageId = messageId == 0 ? interactionComponent.Message.Id : messageId;
                        _logger.Debug("[GetWorkByRoR] 前置条件满足，正在获取 MessageId: {TargetMessageId} 的 WorkData...", targetMessageId);
    
                        var data = await _applicationService.GetWorkDataAsync(targetMessageId);
    
                        // [DEBUG] 8. 获取数据结果
                        if (data is not null)
                        {
                            _logger.Debug("[GetWorkByRoR] WorkData 获取成功，准备调用 GetWorkAsync...");
                            
                            var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                            
                            _logger.Debug("[GetWorkByRoR] GetWorkAsync 完成，正在发送 Followup...");
                            await FollowupAsync(components: component, ephemeral: true);
    
                            var downloadLog = new DownloadLog()
                            {
                                UserId = Context.User.Id,
                                ThreadId = Context.Channel.Id,
                                ReleaseMessageId = interactionComponent.Message.Id
                            };
                            await _applicationService.InsertDownloadLogAsync(downloadLog);
                        }
                        else
                        {
                            _logger.Error("[GetWorkByRoR] ❌ WorkData 获取失败 (data is null). TargetMessageId: {TargetMessageId}", targetMessageId);
                            
                            _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                        }
                    }
                    else
                    {
                        _logger.Debug("[GetWorkByRoR] ❌ 前置条件未满足，回滚 Token 并返回提示。");
    
                        _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                        var component = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"""
                                ### ⚠️ 前置条件未满足
                                至少先对帖子点一个赞或发送一条回复哦
                                """)
                        );
                        await FollowupAsync(components: component.Build(), ephemeral: true);
                        return;
                    }
                }
                catch
                {
                    _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[GetWorkByRoR] 发生异常，回滚 Token。");
        }
    }

    [ComponentInteraction("GetWorkByPwd:*:*")]
    public async Task HandleGetWorkByPwdAsync(ulong messageId, int token)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var threadChannel = (IThreadChannel)interactionComponent.Channel;
            if (Context.User.Id == threadChannel.OwnerId)
            {
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, 0);
                await DeferAsync(ephemeral: true);

                var data = await _applicationService.GetWorkDataAsync(messageId == 0 ? interactionComponent.Message.Id : messageId);

                if (data is not null)
                {
                    var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                    await FollowupAsync(components: component, ephemeral: true);
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                }
                return;
            }
            else
            {
                var tokenCost = 1;
                if(await _applicationService.HasDownloadRecordAsync(Context.User.Id, messageId == 0 ? interactionComponent.Message.Id : messageId))
                {
                    tokenCost = 0;
                }
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, tokenCost);

                try
                {
                    if (token == 0)
                    {
                        allowable = true;
                    }
    
                    if (!allowable)
                    {
                        var component = new ComponentBuilderV2()
                            .WithContainer(container => container
                                .WithAccentColor(Color.Red)
                                .WithTextDisplay($"""
                                ### 🎈 到达限制
                                您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
                                > -# 当前状态: {remainingToken}/{tokenLimit}
                                """)
                        );
                        await RespondAsync(components: component.Build(), ephemeral: true);
                        return;
                    }
    
                    var modal = new ModalBuilder()
                        .WithTitle("输入提取码")
                        .WithCustomId($"Release:Modal:EnterPassword:{(messageId == 0 ? interactionComponent.Message.Id : messageId)}:{(isConsumed ? "true" : "false")}:{tokenCost}")
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
                catch
                {
                    _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, 1, isConsumed);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleGetWorkByPwdAsync Exception");
        }
    }

    [ModalInteraction("Release:Modal:EnterPassword:*:*:*")]
    public async Task HandleGetWorkByPwdModalAsync(ulong messageId, string isConsumed, int tokenCost, PasswordInputModal modal)
    {
        if (Context.Interaction is not IModalInteraction modalInteraction)
        {
            _logger.Warning("交互回调: [Release:Modal:Alerts:*] Context.Interaction is Not modalInteraction");
            return;
        }
        try
        {
            await DeferAsync(ephemeral: true);

            var password = modal.Password.Trim();

            if (string.IsNullOrWhiteSpace(password))
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 密码格式错误\n密码不能为空或只有空格"), ephemeral: true);
                return;
            }

            var data = await _applicationService.GetWorkDataAsync(messageId == 0 ? modalInteraction.Message.Id : messageId);

            if (data is not null)
            {

                if (password != data.ReleasePassword)
                {
                    _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed == "true");
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
                var component = await GetWorkAsync(data, _tokenBucketService.GetRemainingToken(Context.User.Id), _tokenBucketService.TokenLimit, isConsumed == "true");
                await FollowupAsync(components: component, ephemeral: true);

                var downloadLog = new DownloadLog()
                {
                    UserId = Context.User.Id,
                    ThreadId = Context.Channel.Id,
                    ReleaseMessageId = messageId
                };
                await _applicationService.InsertDownloadLogAsync(downloadLog);
            }
            else
            {
                _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed == "true" ? true : false);
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("⚠️ 获取失败", "data is not null"), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleTestUploadModal Exception");
            if (!Context.Interaction.HasResponded)
            {
                _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed == "true" ? true : false);
                await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
            }
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
            // 基础鉴权
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            // 检查是否在帖子内
            if (Context.Channel is not IThreadChannel threadChannel)
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 错误\n请在帖子内使用此命令"), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var data = await _applicationService.GetLatestWorkDataByThreadIdAsync(threadChannel.Id);

            if (data is null)
            {
                await FollowupAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 未找到作品\n本帖似乎还没有发布过作品，或者作品已被移除。"), ephemeral: true);
                return;
            }

            if (Context.User.Id == threadChannel.OwnerId)
            {
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, 0);

                if (data is not null)
                {
                    var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                    await FollowupAsync(components: component, ephemeral: true);
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
                }
                return;
            }
            else
            {
                var tokenCost = 1;
                if(await _applicationService.HasDownloadRecordAsync(Context.User.Id, data.ReleaseMessageId))
                {
                    tokenCost = 0;
                }
                (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.GetToken(Context.User.Id, Context.Channel.Id, tokenCost);

                
                if (!data.IsConsumeToken)
                {
                    allowable = true;
                }

                switch (data.ReleaseModel)
                {
                    case ReleaseModel.NoLimit:
                        {
                            var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                            await FollowupAsync(components: component, ephemeral: true);
                            
                            var downloadLog = new DownloadLog() { UserId = Context.User.Id, ThreadId = Context.Channel.Id, ReleaseMessageId = data.ReleaseMessageId };
                            await _applicationService.InsertDownloadLogAsync(downloadLog);
                            return;
                        }
                    case ReleaseModel.ReactionOrReply:
                    {
                            // 检查点赞或回复
                            var hasReaction = await _applicationService.HasReactionRecordAsync(Context.User.Id, Context.Channel.Id);
                            var hasReply = await _applicationService.HasMessageRecordAsync(Context.User.Id, Context.Channel.Id);

                            if (hasReaction || hasReply)
                            {
                                var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
                                await FollowupAsync(components: component, ephemeral: true);

                                var downloadLog = new DownloadLog() { UserId = Context.User.Id, ThreadId = Context.Channel.Id, ReleaseMessageId = data.ReleaseMessageId };
                                await _applicationService.InsertDownloadLogAsync(downloadLog);
                                
                            }
                            else
                            {
                                // 前置条件未满足，回滚 Token
                                _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                                var component = new ComponentBuilderV2()
                                    .WithContainer(container => container
                                        .WithAccentColor(Color.Red)
                                        .WithTextDisplay($"""
                                        ### ⚠️ 前置条件未满足
                                        至少先对帖子点一个赞或发送一条回复哦
                                        """)
                                );
                                await FollowupAsync(components: component.Build(), ephemeral: true);
                            }
                            return;
                        }
                    case ReleaseModel.Password:
                        {
                            // 密码模式需要弹出 Modal，但 Slash Command 的 Defer 后无法直接 RespondWithModal
                            // 这是一个 Discord API 的限制：Modal 必须是对 Interaction 的直接响应，不能在 Defer 后发送。
                            // 但我们在步骤3已经 Defer 了。
                            
                            // 解决方案：
                            // 1. 如果是密码模式，我们回滚 Token (因为还没输入密码)。
                            _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
                            
                            // 2. 发送一个带有“输入提取码”按钮的消息，让用户点击按钮来触发 Modal。
                            // 这样用户体验也比较流畅。
                            
                            var pwdComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Blue)
                                    .WithTextDisplay("### 🔒 需要提取码", 1000)
                                    .WithTextDisplay("获取该作品需要输入提取码。", 1001)
                                    .WithActionRow(row => row
                                        // 这里的 CustomId 构造要匹配现有的 HandleGetWorkByPwdAsync 逻辑所需的格式
                                        // 现有逻辑: GetWorkByPwd:messageId:token
                                        .WithButton(
                                            label: "输入提取码",
                                            customId: $"GetWorkByPwd:{data.ReleaseMessageId}:{(data.IsConsumeToken ? 1 : 0)}:{(isConsumed ? "true" : "false")}",
                                            style: ButtonStyle.Primary,
                                            emote: new Emoji("🔑"))
                                    )
                                );
                            
                            await FollowupAsync(components: pwdComponent.Build(), ephemeral: true);
                            return;
                        }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }
    #endregion
    
    private static bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}

