// .NET 核心库
using System.ComponentModel;
using System.Text.RegularExpressions;

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.Interactions;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using Discord.WebSocket;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.ApplicationServices;
using System.Text.Json;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot.AppInteractionService;

public class Release : InteractionModuleBase<SocketInteractionContext>
{
    private string _appPath = AppContext.BaseDirectory;
    const string localCachePath = "localCache";
    private readonly AuthUtil _authUtil;
    private readonly ILogger _logger;
    private readonly ComponentUtils _componentUtils;
    private readonly HttpClient _httpClient;
    private readonly ApplicationService _applicationService;
    private readonly InteractionsCacheManager _interactionsCacheManager;
    private readonly ConfigLoader _configLoader;
    private readonly AsyncRetryPolicy _httpRetryPolicy;
    public Release(ILogger logger, AuthUtil authUtil, HttpClient httpClient, ApplicationService applicationService, InteractionsCacheManager interactionsCacheManager, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("Release");
        _authUtil = authUtil;
        _httpClient = httpClient;
        _applicationService = applicationService;
        _interactionsCacheManager = interactionsCacheManager;
        _componentUtils = new(_logger);
        _configLoader = configLoader;
        _httpRetryPolicy = HttpRetryPolicyConfig();
    }

    // =============================================
    #region 业务方法
    // =============================================
    #endregion

    // =============================================
    #region  交互回调
    // ============================================='
    [ComponentInteraction("Release:*:*")]
    public async Task HandleReleaseActionAsync(string action, string cacheId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            var cache = _interactionsCacheManager.GetView<ReleaseCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            switch (action)
            {
                case "NoLimit":
                    {
                        cache.Data.ReleaseModel = ReleaseModel.NoLimit;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "ReactionOrReply":
                    {
                        cache.Data.ReleaseModel = ReleaseModel.ReactionOrReply;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "Password":
                    {
                        cache.Data.ReleaseModel = ReleaseModel.Password;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "FreeAccess":
                    {
                        cache.IsConsumeToken = false;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "ConsumeToken":
                    {
                        cache.IsConsumeToken = true;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "SetPassword":
                    {
                        var modal = new ModalBuilder()
                            .WithTitle("提取码")
                            .WithCustomId($"Release:Modal:SetPassword:{cacheId}")
                            .AddTextInput(
                                label: "设置提取码 (1 到 50 字)",
                                customId: "SetPassword",
                                placeholder: "填写要设置的提取码...",
                                maxLength: 50,
                                minLength: 1,
                                required: true,
                                style: TextInputStyle.Short
                            );
                        await RespondWithModalAsync(modal.Build());
                        return;
                    }
                case "EnableAlerts":
                    {
                        cache.Data.EnableAlerts = true;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "DisabledAlerts":
                    {
                        cache.Data.EnableAlerts = false;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "Alerts":
                    {
                        var modal = new ModalBuilder()
                            .WithTitle("作者声明")
                            .WithCustomId($"Release:Modal:Alerts:{cacheId}")
                            .AddTextInput(
                                label: "作者声明",
                                customId: "Input",
                                placeholder: "在此处编写声明...",
                                maxLength: 500,
                                minLength: 1,
                                required: true,
                                style: TextInputStyle.Paragraph
                            );
                        await RespondWithModalAsync(modal.Build());
                        return;
                    }
                case "AddWorks":
                    {
                        var modal = new ModalBuilder()
                            .WithTitle("上传作品")
                            .WithCustomId($"Release:Modal:UpFiles:{cacheId}")
                            .AddFileUpload(
                                label: "请选择文件",
                                customId: "UpFiles",
                                minValues: 1,
                                maxValues: 10,
                                isRequired: true
                            );
                        await RespondWithModalAsync(modal.Build());
                        return;
                    }
                case "Confirm":
                    {
                        _logger.Debug("步骤 1: 进入 Confirm 逻辑");

                        if (Context.Channel is not SocketThreadChannel sourceThreadChannel)
                        {
                            await RespondAsync(components: _componentUtils.LoggerErrorComponente("交互失败", "❌ Context.Channel is not SocketThreadChannel: {ContextChannelId}", Context.Channel.Id.ToString()), ephemeral: true);
                            return;
                        }

                        if (cache.Data.CacheAttachmentUrls.Count < 1)
                        {
                            var attachmentCountErrorComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Blue)
                                    .WithTextDisplay("### ⚠️ 您好像还没上传任何附件哦")
                                );
                            await RespondAsync(components: attachmentCountErrorComponent.Build(), ephemeral: true);
                            return;
                        }

                        if (cache.Data.ReleaseModel == ReleaseModel.Password && string.IsNullOrWhiteSpace(cache.Data.ReleasePassword))
                        {
                            var attachmentCountErrorComponent = new ComponentBuilderV2()
                                .WithContainer(container => container
                                    .WithAccentColor(Color.Blue)
                                    .WithTextDisplay("### ⚠️ 您设置的提取码似乎为空哦")
                                );
                            await RespondAsync(components: attachmentCountErrorComponent.Build(), ephemeral: true);
                            return;
                        }

                        await DeferAsync(ephemeral: true);


                        _logger.Debug("步骤 2: 准备发送 Processing 消息");
                        
                        var component = new ComponentBuilderV2();
                        component.WithContainer(container => container
                            .WithAccentColor(Color.Blue)
                            .WithTextDisplay("### 🎁 作品发布处\n🔄 正在处理中...")
                        );

                        RestUserMessage? message = null;
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            message = await interactionComponent.Channel.SendMessageAsync(components: component.Build());
                        });

                        if (message == null)
                        {
                            _logger.Error("步骤 3 失败: message 为空，发送消息可能失败");
                            throw new Exception("SendMessageAsync 返回了 null");
                        }
                        _logger.Debug("步骤 3: 消息发送成功 ID: {MessageId}", message.Id);

                        cache.Data.ReleaseMessageId = message!.Id;
                        cache.Data.ThreadId = message.Channel.Id;
                        cache.Data.AuthorId = interactionComponent.User.Id;
                        cache.Data.ReleaseAt = DateTimeOffset.UtcNow;
                        cache.Data.GuildId = sourceThreadChannel.Guild.Id;
                        cache.Data.ChannelId = sourceThreadChannel.ParentChannel.Id;
                        cache.Data.MessageJumpUrl = message.GetJumpUrl();

                        _logger.Debug("步骤 4: 准备调用 cache.Complete()");

                        var data = cache.Complete();

                        _logger.Debug("步骤 5: 准备调用 InsertWorkDataAsync, Data: {@Data}", data);

                        // 写入数据库
                        var result = await _applicationService.InsertWorkDataAsync(data);
                        // [DEBUG]
                        // int? result = 1;
                        // [DEBUG]

                        _logger.Debug("步骤 6: 数据库返回结果: {Result}", result);
                        
                        if (result is null || result == 0)
                        {
                            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("交互失败", "❌ 数据库写入失败"), ephemeral: true);
                            return;
                        }
                        else
                        {
                            _logger.Debug("步骤 7: 数据库写入成功，准备更新 UI");

                            var completeComponent = new ComponentBuilderV2();
                            completeComponent.WithContainer(container =>
                            {
                                container.WithAccentColor(Color.Blue);
                                var mode = "";
                                switch (data.ReleaseModel)
                                {
                                    case ReleaseModel.NoLimit:
                                        mode = "无限制";
                                        break;
                                    case ReleaseModel.ReactionOrReply:
                                        mode = "点赞 或 评论";
                                        break;
                                    case ReleaseModel.Password:
                                        mode = "提取码";
                                        break;
                                }
                                container.WithTextDisplay($"""
                                    ### 🎈 作品发布处
                                    请在此处交互获取本帖作品
                                    > 前置条件: {mode}
                                    > -# - 无限制: 可直接获取
                                    > -# - 点赞或评论: 对帖子首楼点赞(任意反应)或在贴内回复(任意回复)
                                    > -# - 提取码: 寻找作者在贴内贴出的的提取码
                                    > 分享模式: {(cache.IsConsumeToken ? "每日限定" : "开放分享")}
                                    > -# - 每日限定: 每日获取作品次数耗尽后**无法**获取本作品
                                    > -# - 开放分享: 每日获取作品次数耗尽后**仍可**获取本作品
                                    """);
                                container.WithSeparator();
                                container.WithTextDisplay("作者专属交互");

                                var token = 0;
                                if (cache.IsConsumeToken)
                                {
                                    token = 1;
                                }

                                container.WithActionRow(action => action
                                    .WithButton("移除本条发布处", $"RemoveRelease:{Context.User.Id}", ButtonStyle.Danger, emote: new Emoji("⚠️"))
                                    .WithButton("放置新的作品发布处", $"NewRelease:{Context.User.Id}", ButtonStyle.Success, emote: new Emoji("🆕"))
                                );

                                container.WithSeparator();
                                if (data.EnableAlerts)
                                {
                                    switch (data.ReleaseModel)
                                    {
                                        case ReleaseModel.NoLimit:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"Alerts:GetWork:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                        case ReleaseModel.ReactionOrReply:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"Alerts:GetWorkByRoR:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                        case ReleaseModel.Password:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"Alerts:GetWorkByPwd:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                    }
                                }
                                else
                                {
                                    switch (data.ReleaseModel)
                                    {
                                        case ReleaseModel.NoLimit:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"GetWork:0:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                        case ReleaseModel.ReactionOrReply:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"GetWorkByRoR:0:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                        case ReleaseModel.Password:
                                            container.WithActionRow(action => action
                                                .WithButton("获取作品", $"GetWorkByPwd:0:{token}", ButtonStyle.Primary, emote: new Emoji("🎁"))
                                            );
                                            break;
                                    }
                                }
                            });
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await message!.ModifyAsync(msg => msg.Components = completeComponent.Build());
                                await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = cache.Render());
                            });
                        }

                        _logger.Debug("步骤 8: 开始处理文件下载逻辑");

                        // 下载到本地缓存

                        string localSaveDir = Path.Combine(_appPath, localCachePath, $"{Context.Guild.Id}", $"{sourceThreadChannel.ParentChannel.Id}", $"{message!.Channel.Id}", $"{message.Id}");
                        if (!Directory.Exists(localSaveDir))
                        {
                            Directory.CreateDirectory(localSaveDir);
                        }
                        // 发送转换后的 FileAttachment 列表

                        var messageMetadata = new
                        {
                            CacheThreadCreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                            SavedFiles = cache.CacheMessage!.Attachments.Select(f => f.Filename).ToArray()
                        };

                        string metadataPath = Path.Combine(localSaveDir, "metadata.json");

                        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(messageMetadata, JsonSerializerConfig.OptionsWithIndented));

                        var downloadTasks = cache.CacheMessage!.Attachments.Select(async attachment =>
                        {
                            try
                            {
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    string safeFileName = Path.GetFileName(attachment.Filename);
                                    string savePath = Path.Combine(localSaveDir, safeFileName);

                                    // 使用 HttpCompletionOption.ResponseHeadersRead 避免内存缓冲整个文件
                                    using var response = await _httpClient.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
                                    response.EnsureSuccessStatusCode();

                                    using var networkStream = await response.Content.ReadAsStreamAsync();
                                    // 8192 buffer size, useAsync = true
                                    using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                                    await networkStream.CopyToAsync(fileStream);

                                    _logger.Debug("已下载文件: {FileName}", safeFileName);
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "文件下载失败: {FileName} Url: {Url}", attachment.Filename, attachment.Url);
                            }
                        });

                        await Task.WhenAll(downloadTasks);

                        return;
                    }
                case "Cancel":
                    {
                        cache.Cancel();
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        _interactionsCacheManager.TryRemove(cacheId);
                        return;
                    }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleReleaseActionAsync Exception");
        }
    }

    [ComponentInteraction("NewRelease:*")]
    public async Task HandleNewReleaseAsync(ulong authorId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            if (Context.User.Id != authorId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            var cache = _interactionsCacheManager.RegisterView<WorkData>(Context.User.Id, new WorkData());

                
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await RespondAsync(components: cache.Render(), ephemeral: true);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    [ComponentInteraction("RemoveRelease:*")]
    public async Task HandleRemoveReleaseAsync(ulong authorId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            if (Context.User.Id != authorId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            var completeComponent = new ComponentBuilderV2();
            completeComponent.WithContainer(container =>
            {
                container.WithAccentColor(Color.Blue);
                container.WithTextDisplay($"""
                    ### ⚠️ 确认操作
                    是否要删除该作品发布处?
                    """);
                container.WithSeparator();
                container.WithActionRow(action => action
                    .WithButton("确认", $"RemoveReleaseConfirm:{Context.User.Id}:{interactionComponent.Message.Id}", ButtonStyle.Danger, emote: new Emoji("⚠️"))
                    .WithButton("取消", $"RemoveReleaseCancel:{Context.User.Id}", ButtonStyle.Primary, emote: new Emoji("❎"))
                );
            });
            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                await RespondAsync(components: completeComponent.Build(), ephemeral: true);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleRemoveReleaseAsync Exception");
        }
    }
    
    [ComponentInteraction("RemoveReleaseConfirm:*:*")]
    public async Task HandleRemoveReleaseConfirmAsync(ulong authorId, ulong messageId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            if (Context.User.Id != authorId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var data = await _applicationService.GetWorkDataAsync(messageId);

            if (data is null)
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("移除失败", "data is null"), ephemeral: true);
                return;
            }

            // =======================================================
            // 删除数据库记录
            // =======================================================
            await _applicationService.SoftDeleteWorkDataAsync(messageId);

            // =======================================================
            // 删除 CDN 缓存
            // =======================================================
            try
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    var guild = Context.Client.GetGuild(data.CacheGuildId);

                    var thread = await Context.Client.GetChannelAsync(data.CacheThreadId);

                    var restThread = (IThreadChannel)thread;

                    await restThread.DeleteMessageAsync(data.CacheMessageId);
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 删除远程缓存帖子失败");
            }

            // =======================================================
            // 删除本地缓存
            // =======================================================
            try
            {
                // 路径构建逻辑需与创建时保持一致：
                // Path: Base / localCache / GuildId / ChannelId(Parent) / ThreadId / MessageId
                string localSaveDir = Path.Combine(
                    _appPath,
                    localCachePath,
                    $"{data.GuildId}",       // 来源服务器ID
                    $"{data.ChannelId}",     // 来源板块ID
                    $"{data.ThreadId}",      // 来源帖子ID
                    $"{data.ReleaseMessageId}" // 发布消息ID (即 interactionComponent.Message.Id)
                );

                if (Directory.Exists(localSaveDir))
                {
                    Directory.Delete(localSaveDir, true); // true 表示递归删除子目录和文件
                    _logger.Info("✅ 本地缓存已清除: {Path}", localSaveDir);
                }
                else
                {
                    _logger.Debug("本地缓存不存在，跳过清除: {Path}", localSaveDir);
                }

                // 尝试清理上级空目录 (如果该帖子下没有其他发布消息了，可以尝试删除帖子层级的空文件夹)
                // string threadDir = Path.GetDirectoryName(localSaveDir)!;
                // if (Directory.Exists(threadDir) && !Directory.EnumerateFileSystemEntries(threadDir).Any())
                // {
                //     Directory.Delete(threadDir);
                // }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 清除本地缓存失败: {MessageId}", data.ReleaseMessageId);
            }

            await interactionComponent.Channel.DeleteMessageAsync(messageId);

            var deletedComponent = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.Red)
                .WithTextDisplay("### ✅ 处理完成", 1000)
                .WithSeparator()
                .WithTextDisplay("发布处已移除, 相关缓存已删除完毕", 1001)
                );
                    
            await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = deletedComponent.Build());
            
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    [ComponentInteraction("RemoveReleaseCancel:*")]
    public async Task HandleRemoveReleaseCancelAsync(ulong authorId, ulong message)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            if (Context.User.Id != authorId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                await RespondAsync(components: component.Build(), ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            await interactionComponent.Channel.DeleteMessageAsync(message);

            var deletedComponent = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.Red)
                .WithTextDisplay("### ⚠️ 确认操作", 1000)
                .WithSeparator()
                .WithTextDisplay("### 操作已取消", 1001)
                );

            await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = deletedComponent.Build());

        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }
    
    [ModalInteraction("Release:Modal:SetPassword:*")]
    public async Task HandleSetPasswordModalAsync(string cacheId, SetPasswordInputModal modal) // ✅ 修正点：使用 IModal 实现类
    {
        try
        {
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Release:Modal:SetPassword:*] Context.Interaction is Not modalInteraction");
                return;
            }

            var cache = _interactionsCacheManager.GetView<ReleaseCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }


            var password = modal.SetPassword.Trim();
            
            if (string.IsNullOrWhiteSpace(password))
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("### 提取码格式错误\n提取码不能为空或只有空格"), ephemeral: true);
                return;
            }

            cache.Data.ReleasePassword = password;

            await modalInteraction.UpdateAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleSetPasswordModalAsync Exception");
            if (!Context.Interaction.HasResponded)
            {
                await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
            }
        }
    }

    [ModalInteraction("Release:Modal:Alerts:*")]
    public async Task HandleAlertsModalAsync(string cacheId, TextInputModal modal) // ✅ 修正点：使用 IModal 实现类
    {
        try
        {
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Release:Modal:Alerts:*] Context.Interaction is Not modalInteraction");
                return;
            }

            var cache = _interactionsCacheManager.GetView<ReleaseCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            var alerts = modal.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(alerts))
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("### 声明格式错误\n声明不能为空或只有空格"), ephemeral: true);
                return;
            }

            cache.Data.Alerts = alerts;

            await modalInteraction.UpdateAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleAlertsModalAsync Exception");
            if (!Context.Interaction.HasResponded)
            {
                await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
            }
        }
    }

    [ModalInteraction("Release:Modal:UpFiles:*")]
    public async Task HandleUpFilesModalAsync(string cacheId, UploadModal modal)
    {
        try
        {
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Release:Modal:UpFiles:*] Context.Interaction is Not modalInteraction");
                return;
            }

            var attachments = modalInteraction.Data.Attachments;

            if (Context.Channel is not SocketThreadChannel sourceThreadChannel)
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("❌ 请在作品发布帖子内使用此功能。"), ephemeral: true);
                return;
            }

            var cache = _interactionsCacheManager.GetView<ReleaseCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            // 获取上下文信息
            // 源服务器 ID (A服)
            ulong sourceGuildId = Context.Guild.Id;
            // 源父频道 ID (A服的具体板块，如"男性向"的频道ID)
            ulong sourceParentChannelId = sourceThreadChannel.ParentChannel.Id;
            // 获取配置快照
            var deployConfig = _configLoader.Config.DeployConfig;

            // =======================================================
            // 反向查找 (在 GuildConfigs 中找到 "Key/分类名")
            // =======================================================
            
            // 检查源服务器是否有配置
            if (!deployConfig.GuildConfigs.TryGetValue(sourceGuildId, out var sourceGuildConfig))
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ 未在 GuildConfigs 中找到源服务器配置: {sourceGuildId}", sourceGuildId.ToString()), ephemeral: true);
                return;
            }

            // 在源服务器配置中，查找哪个 Key (如 "男性向") 对应当前的 ParentChannelId
            // KeyValuePair<string, ulong>
            var categoryEntry = sourceGuildConfig.ValidChannels
                .FirstOrDefault(x => x.Value == sourceParentChannelId);

            // 如果 Key 为 null，说明当前帖子所在的父频道不在配置文件里
            if (string.IsNullOrEmpty(categoryEntry.Key))
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ 当前频道 {sourceParentChannelId} 未在配置文件的 ValidChannels 中定义", sourceParentChannelId.ToString()), ephemeral: true);
                return;
            }

            string categoryName = categoryEntry.Key; // 例如: "男性向"
            _logger.Debug($"路由匹配成功: 分类名为 [{categoryName}]");

            // =======================================================
            // 正向查找 (在 CacheLocations 中找到 "B服目标")
            // =======================================================

            // 使用源服务器 ID 作为 Key 去查找 CacheLocations
            if (!deployConfig.CacheLocations.TryGetValue(sourceGuildId, out var targetLocationConfig))
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ 未在 CacheLocations 中找到源服务器对应的路由配置 {sourceGuildId}", sourceGuildId.ToString()), ephemeral: true);
                return;
            }

            // 获取目标服务器 ID (B服)
            ulong targetGuildId = targetLocationConfig.Guild;

            // 根据分类名 ("男性向") 获取目标频道 ID
            if (!targetLocationConfig.Channels.TryGetValue(categoryName, out ulong targetChannelId))
            {
                await RespondAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ 在 CacheLocations 中未找到分类 [{categoryName}] 的目标频道映射", categoryName.ToString()), ephemeral: true);
                return;
            }

            // ==============================================================================================================
            // 网络部分
            // 开始Defer
            await DeferAsync(ephemeral: true);

            // =======================================================
            // 获取 Discord 实体对象并执行逻辑
            // =======================================================

            // 获取目标服务器的 SocketGuild 对象
            var targetGuild = Context.Client.GetGuild(targetGuildId);
            if (targetGuild == null)
            {
                // 可能是 Bot 没加 B 服，或者缓存没同步
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ Bot 无法获取目标服务器实例: {targetGuildId}", targetGuildId.ToString()), ephemeral: true);
                return;
            }

            // 数据库查询

            var cacheThreadId = await _applicationService.QueryCacheChannelAsync(Context.Channel.Id);

            // [DEBUG]
            // ulong? cacheThreadId = 1448173068706123879;
            // [DEBUG]

            IThreadChannel? cacheThread = null;

            if (cacheThreadId is not null)
            {
                // 如果有缓存记录
                // 获取目标帖子对象
                cacheThread = targetGuild.GetChannel(cacheThreadId.Value) as IThreadChannel;
                if (cacheThread == null)
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ Bot 无法获取目标频道实例: {parameters}", $"{cacheThreadId} @ Guild {targetGuildId}"), ephemeral: true);
                    return;
                }
                cache.Data.IsUpdate = true;
            }
            else
            {
                // 如果没有源贴的缓存记录
                // 获取目标频道对象 (通常是 IMessageChannel 或 ITextChannel)
                var targetChannel = targetGuild.GetChannel(targetChannelId) as IForumChannel;
                if (targetChannel == null)
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ Bot 无法获取目标频道实例: {parameters}", $"{targetChannelId} @ Guild {targetGuildId}"), ephemeral: true);
                    return;
                }

                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    cacheThread = await targetChannel.CreatePostAsync(Context.Channel.Name, text: $"""
                        cache thread metadata:
                        origin thread channel: {sourceThreadChannel.Name ?? "null"}
                        origin thread channel id: {sourceThreadChannel.Id}
                        origin thread created at: {sourceThreadChannel.CreatedAt:O}
                        origin forum channel: {sourceThreadChannel.Name ?? "null"}
                        origin forum channel id: {sourceThreadChannel.Id}
                        origin author at created name: {sourceThreadChannel.Owner.DisplayName ?? "null"}
                        origin author id: {sourceThreadChannel.Owner.Id}
                        cache thread created at {DateTimeOffset.UtcNow:O}
                        """);
                });

                string localMetadataDir = Path.Combine(_appPath, localCachePath, $"{Context.Guild.Id}", $"{sourceParentChannelId}", $"{Context.Channel.Id}");
                if (!Directory.Exists(localMetadataDir))
                {
                    Directory.CreateDirectory(localMetadataDir);
                }

                var metadata = new
                {
                    // 外层属性
                    Version = "1.0",
                    GeneratedAt = DateTimeOffset.UtcNow,

                    // 嵌套对象 (CacheThreadMetadata)
                    CacheThreadMetadata = new
                    {
                        OriginThreadChannel = sourceThreadChannel.Name ?? "null",
                        OriginThreadChannelId = sourceThreadChannel.Id,
                        OriginThreadCreatedAt = sourceThreadChannel.CreatedAt.ToString("O"),
                        OriginForumChannel = sourceThreadChannel.ParentChannel?.Name ?? "null",
                        OriginForumChannelId = sourceThreadChannel.ParentChannel?.Id,
                        OriginAuthorName = sourceThreadChannel.Owner.DisplayName ?? "null",
                        OriginAuthorId = sourceThreadChannel.Owner.Id,
                        CacheThreadCreated = DateTimeOffset.UtcNow.ToString("O")
                    },
                };

                string metadataPath = Path.Combine(localMetadataDir, "metadata.json");

                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, JsonSerializerConfig.OptionsWithIndented));

                if (cacheThread == null)
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ Bot 无法在目标频道创建帖子: {parameters}", $"{targetChannelId} @ Guild {targetGuildId}"), ephemeral: true);
                    return;
                }
            }
            
            // =========================================================
            // 附件分两部分处理:
            // Discord CDN Url: 主要, 用于直接通过cdn url分发文件, 提升性能和响应速度, 同时节省带宽和流量
            // 本地文件: 作为备份, 如果 Discord CDN 让 Discord 给毙了就回退到本地文件上传
            // =========================================================
            
            var streams = new List<Stream>();
            var fileAttachments = new List<FileAttachment>();

            try
            {
                // 遍历用户上传的所有附件
                var downloadTasks = attachments.Select(async attachment =>
                {
                    try
                    {
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            // 通过 URL 下载文件流
                            var stream = await _httpClient.GetStreamAsync(attachment.Url);
                            streams.Add(stream);

                            var filename = attachment.Filename.Replace('_', '-');

                            // 创建 FileAttachment 对象
                            // 参数: 流, 文件名, 描述
                            // fileAttachments.Add(new FileAttachment(stream, attachment.Filename, attachment.Description));
                            fileAttachments.Add(new FileAttachment(stream, filename));
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                    }
                });

                await Task.WhenAll(downloadTasks);

                if (fileAttachments.Count > 0)
                {
                    // 发送转换后的 FileAttachment 列表
                    IUserMessage? message = null;
                    await _httpRetryPolicy.ExecuteAsync(async () =>
                    {
                        message = await cacheThread.SendFilesAsync(fileAttachments, text: $"""
                        release cache metadata:
                        cache thread created at {DateTimeOffset.UtcNow:O}
                        """);
                    });
                    List<string> attachmentUrls = new();
                    if (message is not null)
                    {
                        attachmentUrls = message.Attachments
                            .Select(a => a.Url)
                            .ToList();
                    }
                    cache.CacheMessage = message;
                    cache.Data.CacheAttachmentUrls = attachmentUrls;
                    cache.Data.CacheChannelId = targetChannelId;
                    cache.Data.CacheMessageId = message!.Id;
                    cache.Data.CacheThreadId = cacheThread.Id;
                    cache.Data.CacheGuildId = cacheThread.Guild.Id;
                    cache.SetAttachment(message.Attachments.ToList());
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.TextErrorComponente("❌ 未检测到附件。"), ephemeral: true);
                    return;
                }
            }
            finally
            {
                foreach (var stream in streams)
                {
                    await stream.DisposeAsync();
                }
            }

            await modalInteraction.ModifyOriginalResponseAsync(msg => msg.Components = cache.Render());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleUpFilesModalAsync Exception");
            // 只有交互未被以此回应过时，才能发送 Ephemeral 消息，否则需使用 Followup
            if (!Context.Interaction.HasResponded)
            {
                await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
            }
            else
            {
                await FollowupAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
            }
        }
    }
    #endregion

    // =============================================
    #region  斜杠命令
    // =============================================
    [SlashCommand("发布作品", "打开作品发布面板")]
    public async Task ReleaseAsync()
    {
        try
        {
            var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
            var authResult = await _authUtil.Authorization(requiredConditions, Context);
            if (authResult.Status != AuthorizationStatus.Success)
            {
                var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
                await RespondAsync(components: failedReasonComponent, ephemeral: true);
                return;
            }

            if (Context.Channel is IThreadChannel threadChannel)
            {
                if (Context.User.Id != threadChannel.OwnerId)
                {
                    var component = new ComponentBuilderV2().WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                        .WithTextDisplay("该命令仅供本帖作者使用", 1001)
                        );
                    await RespondAsync(components: component.Build(), ephemeral: true);
                    return;
                }

                var cache = _interactionsCacheManager.RegisterView<WorkData>(Context.User.Id, new WorkData());

                await RespondAsync(components: cache.Render(), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ReleaseAsync Exception");
        }
    }
    #endregion

    /// <summary>
    /// 配置 HTTP 请求重试策略（基于 Polly）。
    /// 该策略专门处理 Discord API 和网络相关的临时性错误。
    /// 使用指数退避策略：2秒、4秒、8秒。
    /// </summary>
    /// <returns>配置好的 Polly 异步重试策略</returns>
    private AsyncRetryPolicy HttpRetryPolicyConfig()
    {
        return Policy
            // 定义需要处理的异常:
            // - Discord.HttpException: 处理 5xx 服务器错误和 429 速率限制
            //   不重试: 404(资源不存在)、403(权限不足)、400(请求格式错误)
            .Handle<Discord.Net.HttpException>(ex => (int)ex.HttpCode != 404 && (int)ex.HttpCode != 403 && (int)ex.HttpCode != 400)
            // - HttpRequestException: 处理网络层级异常（SSL 握手失败、连接被重置等）
            .Or<HttpRequestException>()
            // - TaskCanceledException: 处理 HttpClient 超时
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            // - 处理 JSON 解析异常 (针对返回 HTML 错误页的情况)
            .Or<Newtonsoft.Json.JsonReaderException>() 

            // 定义重试逻辑：指数退避
            .WaitAndRetryAsync(
                retryCount: 3, // 最多重试 3 次
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2秒、4秒、8秒
                onRetry: (exception, timespan, attempt, context) =>
                {
                    // 在日志中清晰显示触发重试的异常类型
                    string errorType = exception is HttpException httpEx
                        ? $"HttpException (StatusCode: {httpEx.HttpCode})"
                        : exception.GetType().Name;

                    _logger.Warning("⚠️ HTTP 请求失败 ({ErrorType}), 将在 {Seconds:F1} 秒后进行第 {Attempt}/3 次重试",
                        errorType, timespan.TotalSeconds, attempt);
                }
            );
    }
}

public class UploadModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "提交数据";
}

public class TextInputModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "作者声明";

    [ModalTextInput("Input")]
    public string Text { get; set; } = string.Empty;
}

public class SetPasswordInputModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "提取码";

    [ModalTextInput("SetPassword")]
    public string SetPassword { get; set; } = string.Empty;
}


public class PasswordInputModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "提取码";

    [ModalTextInput("Password")]
    public string Password { get; set; } = string.Empty;
}
