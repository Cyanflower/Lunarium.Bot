// .NET 核心库
using System.Collections.Concurrent;
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
                case "Reaction":
                    {
                        cache.Data.ReleaseModel = ReleaseModel.Reaction;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "ReactionOrReply":
                    {
                        cache.Data.ReleaseModel = ReleaseModel.ReactionOrReply;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "NeedPassword":
                    {
                        cache.Data.NeedPassword = !cache.Data.NeedPassword;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "FreeAccess":
                    {
                        cache.Data.IsConsumeToken = false;
                        await interactionComponent.UpdateAsync(msg => msg.Components = cache.Render());
                        return;
                    }
                case "ConsumeToken":
                    {
                        cache.Data.IsConsumeToken = true;
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

                        if ((cache.Data.ReleaseModel == ReleaseModel.Password || cache.Data.ReleaseModel == ReleaseModel.RoRAndPwd)&& string.IsNullOrWhiteSpace(cache.Data.ReleasePassword))
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
                        var workId = await _applicationService.InsertWorkDataAsync(data);
                        // 更新 posts_main 的更新相关字段
                        await _applicationService.UpdatePostUpdateInfoAsync(cache.Data.ThreadId, DateTimeOffset.UtcNow, cache.Data.MessageJumpUrl);

                        _logger.Debug("步骤 6: 数据库返回结果: {Result}", workId);

                        if (workId is null)
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
                                        mode = data.NeedPassword ? "提取码" : "无限制";
                                        break;
                                    case ReleaseModel.Reaction:
                                        mode = data.NeedPassword ? "点赞 + 提取码" : "点赞";
                                        break;
                                    case ReleaseModel.ReactionOrReply:
                                        mode = data.NeedPassword ? "点赞** 或 **评论 + 提取码" : "点赞** 或 **评论";
                                        break;
                                }
                                container.WithTextDisplay($"""
                                    ### 🎈 作品发布处
                                    请在此处交互获取本帖作品
                                    或者在直接发送 **/获取作品** 来使用命令获取本帖内最新的发布处的作品
                                    - 前置条件: **{mode}**
                                    > -# - 无限制: 可直接获取
                                    > -# - 点赞: 对帖子首楼点赞(任意反应)或在贴内回复(任意回复)
                                    > -# - 点赞或评论: 对帖子首楼点赞(任意反应)或在贴内回复(任意回复)
                                    > -# - 提取码: 寻找作者在贴内贴出的的提取码
                                    - 分享模式: **{(cache.Data.IsConsumeToken ? "每日限定" : "开放分享")}**
                                    > -# - 每日限定: 用户的每日获取作品次数耗尽后**无法**获取本作品
                                    > -# - 开放分享: 用户的每日获取作品次数耗尽后**仍可**获取本作品
                                    Tips: 
                                    > 如果出现了点击按钮后再滑到最下面发现没有作品消息
                                    > 可以滑到最下面后输入 **/获取作品** 来使用命令获取最新发布处的作品
                                    """);

                                // container.WithSeparator();

                                // container.WithSection(section => section
                                //     .WithTextDisplay("")
                                //     .WithAccessory(new ButtonBuilder("编写编写说明/注释", $"EditNote:{Context.User.Id}"))
                                // );

                                container.WithSeparator();

                                container.WithTextDisplay("作者专属交互");

                                container.WithActionRow(action => action
                                    .WithButton("移除本条发布处", $"RemoveRelease:{Context.User.Id}", ButtonStyle.Danger, emote: new Emoji("⚠️"))
                                    .WithButton("放置新的作品发布处", $"NewRelease:{Context.User.Id}", ButtonStyle.Success, emote: new Emoji("🆕"))
                                    .WithButton("标注/取消标注本消息", $"PinThisMessage:{Context.User.Id}", ButtonStyle.Primary, emote: new Emoji("📌"))
                                );

                                container.WithSeparator();
                                container.WithSection(section => section
                                    .WithTextDisplay($"> 作品ID: {workId}")
                                    .WithAccessory(new ButtonBuilder("获取作品", $"GetWork:{Context.User.Id}", ButtonStyle.Primary, emote: new Emoji("🎁")))
                                );
                            });

                            // 更新发布处
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await message.ModifyAsync(msg => msg.Components = completeComponent.Build());
                            });

                            // 更新创建发布处交互面板
                            await _httpRetryPolicy.ExecuteAsync(async () =>
                            {
                                await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = cache.Render());
                            });
                            // 如果是从有附件的消息创建的, 尝试删除原消息
                            if (cache.IsFromMessageCommand && cache.SourceMessage is not null)
                            {
                                _logger.Debug("准备删除原消息");
                                await _httpRetryPolicy.ExecuteAsync(async () =>
                                {
                                    await cache.SourceMessage.DeleteAsync();
                                });
                            }
                        }

                        _logger.Debug("步骤 8: 处理本地文件目录 (Draft -> Final)");

                        // 目录处理逻辑修改：从 Draft 目录 移动到 MessageId 目录
                        // 根目录: localCache / GuildId / ChannelId / ThreadId
                        string threadDir = Path.Combine(_appPath, localCachePath, $"{Context.Guild.Id}", $"{sourceThreadChannel.ParentChannel.Id}", $"{message!.Channel.Id}");
                        string draftDir = Path.Combine(threadDir, $"Draft_{Context.User.Id}");
                        string finalDir = Path.Combine(threadDir, $"{message.Id}");

                        try
                        {
                            if (Directory.Exists(draftDir))
                            {
                                if (Directory.Exists(finalDir))
                                {
                                    // 理论上 messageId 是唯一的，但为了安全起见
                                    Directory.Delete(finalDir, true);
                                }

                                // 移动文件夹 (相当于重命名)
                                Directory.Move(draftDir, finalDir);
                                _logger.Info("✅ 已将文件从草稿目录移动到正式目录: {Draft} -> {Final}", draftDir, finalDir);
                            }
                            else
                            {
                                _logger.Warning("⚠️ 未找到草稿目录: {DraftDir}，可能之前的步骤未下载文件", draftDir);
                                // 如果没有文件，尝试创建空目录防止报错? 或者就不做操作
                                if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);
                            }

                            // 生成 Message 级别的 Metadata (保持原逻辑)
                            var messageMetadata = new
                            {
                                CacheThreadCreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                                // 从 cache 对象读取附件名，因为文件已经移动过去了
                                SavedFiles = cache.CacheMessage?.Attachments.Select(f => f.Filename).ToArray() ?? Array.Empty<string>()
                            };

                            string metadataPath = Path.Combine(finalDir, "metadata.json");
                            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(messageMetadata, JsonSerializerConfig.OptionsWithIndented));
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "❌ 本地文件目录移动失败");
                            // 这里不阻断返回，因为数据库已经写入，Bot功能理论上可以依赖 CDN 链接工作，只是本地备份异常
                        }

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
    
    // [ComponentInteraction("EditNote:*")]
    // public async Task HandleEditNoteAsync(ulong authorId)
    // {
    //     try
    //     {
    //         if (Context.Interaction is not SocketMessageComponent interactionComponent)
    //         {
    //             _logger.Warning("交互回调: [Release:*:*] Context.Interaction is Not SocketMessageComponent");
    //             return;
    //         }

    //         if (Context.User.Id != authorId)
    //         {
    //             var component = new ComponentBuilderV2().WithContainer(c => c
    //                 .WithAccentColor(Color.Red)
    //                 .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
    //                 .WithTextDisplay("该交互仅供本帖作者使用", 1001)
    //                 );
    //             await RespondAsync(components: component.Build(), ephemeral: true);
    //             return;
    //         }

    //         var modal = new ModalBuilder()
    //             .WithTitle("说明/注释")
    //             .WithCustomId($"EditNote:Modal:{authorId}")
    //             .AddTextInput(
    //                 label: "说明/注释",
    //                 customId: "Input",
    //                 placeholder: "在此处编写说明或注释...",
    //                 maxLength: 500,
    //                 minLength: 0,
    //                 style: TextInputStyle.Paragraph
    //             );

    //         await _httpRetryPolicy.ExecuteAsync(async () =>
    //         {
    //             await RespondWithModalAsync(modal.Build());
    //         });
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex);
    //     }
    // }

    // [ModalInteraction("EditNote:Modal:*")]
    // public async Task HandleEditNoteModalAsync(ulong authorId, TextInputModal modal) // ✅ 修正点：使用 IModal 实现类
    // {
    //     try
    //     {
    //         if (Context.Interaction is not IModalInteraction modalInteraction)
    //         {
    //             _logger.Warning("交互回调: [EditNote:Modal:*] Context.Interaction is Not modalInteraction");
    //             return;
    //         }

    //         if (Context.User.Id != authorId)
    //         {
    //             var component = new ComponentBuilderV2().WithContainer(c => c
    //                 .WithAccentColor(Color.Red)
    //                 .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
    //                 .WithTextDisplay("该交互仅供本帖作者使用", 1001)
    //                 );
    //             await RespondAsync(components: component.Build(), ephemeral: true);
    //             return;
    //         }

    //         var note = modal.Text.Trim();

    //         await DeferAsync(ephemeral: true);

    //         var data = await _applicationService.GetWorkDataAsync(modalInteraction.Message.Id);

    //         if (data is null)
    //         {
    //             _logger.Error("[HandleAlertsModalAsync] ❌ 数据获取失败 (data is null)", modalInteraction.Message.Id);
    //             await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
    //             return;
    //         }

    //         var completeComponent = new ComponentBuilderV2();
    //         completeComponent.WithContainer(container =>
    //         {
    //             container.WithAccentColor(Color.Blue);
    //             var mode = "";
    //             switch (data.ReleaseModel)
    //             {
    //                 case ReleaseModel.NoLimit:
    //                     mode = "无限制";
    //                     break;
    //                 case ReleaseModel.ReactionOrReply:
    //                     mode = "点赞 或 评论";
    //                     break;
    //                 case ReleaseModel.Password:
    //                     mode = "提取码";
    //                     break;
    //             }
    //             container.WithTextDisplay($"""
    //                 ### 🎈 作品发布处
    //                 请在此处交互获取本帖作品
    //                 或者在直接发送 **/获取作品** 来使用命令获取本帖内最新的发布处的作品
    //                 - 前置条件: **{mode}**
    //                 > -# - 无限制: 可直接获取
    //                 > -# - 点赞或评论: 对帖子首楼点赞(任意反应)或在贴内回复(任意回复)
    //                 > -# - 提取码: 寻找作者在贴内贴出的的提取码
    //                 - 分享模式: **{(data.IsConsumeToken ? "每日限定" : "开放分享")}**
    //                 > -# - 每日限定: 用户的每日获取作品次数耗尽后**无法**获取本作品
    //                 > -# - 开放分享: 用户的每日获取作品次数耗尽后**仍可**获取本作品
    //                 Tips: 
    //                 > 如果出现了点击按钮后再滑到最下面发现没有作品消息
    //                 > 可以滑到最下面后输入 **/获取作品** 来使用命令获取最新发布处的作品
    //                 """);

    //             container.WithSeparator();

    //             if (string.IsNullOrWhiteSpace(note))
    //             {
    //                 container.WithSection(section => section
    //                     .WithTextDisplay("")
    //                     .WithAccessory(new ButtonBuilder("编写说明/注释", $"EditNote:{Context.User.Id}"))
    //                 );
    //             }
    //             else
    //             {
    //                 container.WithSection(section => section
    //                     .WithTextDisplay($"""
    //                         ### 说明/注释
    //                         {note}
    //                         """)
    //                     .WithAccessory(new ButtonBuilder("编写说明/注释", $"EditNote:{Context.User.Id}"))
    //                 );
    //             }

    //             container.WithSeparator();

    //             container.WithTextDisplay("作者专属交互");

    //             container.WithActionRow(action => action
    //                 .WithButton("移除本条发布处", $"RemoveRelease:{Context.User.Id}", ButtonStyle.Danger, emote: new Emoji("⚠️"))
    //                 .WithButton("放置新的作品发布处", $"NewRelease:{Context.User.Id}", ButtonStyle.Success, emote: new Emoji("🆕"))
    //                 .WithButton("标注/取消标注本消息", $"PinThisMessage:{Context.User.Id}", ButtonStyle.Primary, emote: new Emoji("📌"))
    //             );

    //             container.WithSeparator();
    //             container.WithActionRow(action => action
    //                 .WithButton("获取作品", $"GetWork:{Context.User.Id}", ButtonStyle.Primary, emote: new Emoji("🎁")));
    //         });

    //         await modalInteraction.ModifyOriginalResponseAsync(msg => msg.Components = completeComponent.Build());
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.Error(ex, "HandleEditNoteModalAsync Exception");
    //         if (!Context.Interaction.HasResponded)
    //         {
    //             await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
    //         }
    //     }
    // }

    [ComponentInteraction("PinThisMessage:*")]
    public async Task HandlePinThisMessageAsync(ulong authorId)
    {
        try
        {
            if (Context.Interaction is not SocketMessageComponent interactionComponent)
            {
                _logger.Warning("交互回调: [PinThisMessage:*] Context.Interaction is Not SocketMessageComponent");
                return;
            }

            if (Context.User.Id != authorId)
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                    .WithAccentColor(Color.Red)
                    .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                    .WithTextDisplay("该交互仅供本帖作者使用", 1001)
                    );
                return;
            }

            await interactionComponent.DeferAsync(ephemeral: true);

            if (interactionComponent.Message.IsPinned)
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await interactionComponent.Message.UnpinAsync();
                });
            }
            else
            {
                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    await interactionComponent.Message.PinAsync();
                });
            }

            await _httpRetryPolicy.ExecuteAsync(async () =>
            {
                var component = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.Red)
                .WithTextDisplay("### ✅ 成功", 1000)
                );
                await RespondAsync(components: component.Build(), ephemeral: true);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
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
                await RespondAsync(components: completeComponent.Build());
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

            await DeferAsync();

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
                .WithTextDisplay("发布处已移除, 相关缓存已删除完毕, 本消息将在2分钟后自动移除", 1001)
                );
                    
            await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = deletedComponent.Build());
            await Task.Delay(120000);
            await interactionComponent.Message.DeleteAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex);
        }
    }

    [ComponentInteraction("RemoveReleaseCancel:*")]
    public async Task HandleRemoveReleaseCancelAsync(ulong authorId)
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

            await DeferAsync();

            var deletedComponent = new ComponentBuilderV2().WithContainer(c => c
                .WithAccentColor(Color.LightGrey)
                .WithTextDisplay("### ⚠️ 确认操作", 1000)
                .WithSeparator()
                .WithTextDisplay("### 操作已取消, 本消息将在2分钟后自动移除", 1001)
                );

            await interactionComponent.ModifyOriginalResponseAsync(msg => msg.Components = deletedComponent.Build());
            await Task.Delay(120000);
            await interactionComponent.Message.DeleteAsync();

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
            var title = modal.Title;
            _logger.Debug("1");
            if (Context.Interaction is not IModalInteraction modalInteraction)
            {
                _logger.Warning("交互回调: [Release:Modal:UpFiles:*] Context.Interaction is Not modalInteraction");
                return;
            }

            var attachments = modalInteraction.Data.Attachments;

            _logger.Debug("2");
            if (Context.Channel is not SocketThreadChannel sourceThreadChannel)
            {
                await RespondAsync(components: _componentUtils.TextErrorComponente("❌ 请在作品发布帖子内使用此功能。"), ephemeral: true);
                return;
            }

            _logger.Debug("3");
            var cache = _interactionsCacheManager.GetView<ReleaseCache>(cacheId);

            if (cache is null)
            {
                await RespondAsync(components: _componentUtils.ExpiredComponente(), ephemeral: true);
                return;
            }

            _logger.Debug("4");
            await DeferAsync(ephemeral: true);

            _logger.Debug("5");
            await TransferAttachmentsToCacheAsync(cache, sourceThreadChannel, attachments);

            _logger.Debug("6");
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
    #region  AppMenu
    // =============================================
    [MessageCommand("发布此消息附件作为作品")]
    public async Task HandleReleaseByMessageAsync(IMessage message)
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

                if (Context.User.Id != message.Author.Id)
                {
                    var component = new ComponentBuilderV2().WithContainer(c => c
                        .WithAccentColor(Color.Red)
                        .WithTextDisplay("### ⛔️ 鉴权未通过", 1000)
                        .WithTextDisplay("该命令只能对自己的消息使用", 1001)
                        );
                    await RespondAsync(components: component.Build(), ephemeral: true);
                    return;
                }

                var cache = _interactionsCacheManager.RegisterView<WorkData>(Context.User.Id, new WorkData());
                var releaseCache = (ReleaseCache)cache;
                // 标记来源
                releaseCache.IsFromMessageCommand = true;

                releaseCache.SourceMessage = message;

                await DeferAsync(ephemeral: true);

                var sourceThreadChannel = (SocketThreadChannel)Context.Channel;

                // 执行传输逻辑
                bool success = await TransferAttachmentsToCacheAsync(releaseCache, sourceThreadChannel, message.Attachments);

                // 只有成功才弹出面板, 且必须使用 FollowupAsync (因为已经 Defer 了)
                if (success)
                {
                    await FollowupAsync(components: cache.Render(), ephemeral: true);
                }
                // 如果 success 为 false，TransferAttachmentsToCacheAsync 内部已经发过错误提示了，这里无需操作
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "HandleReleaseByMessage Exception");
            // 保底错误处理
            if (!Context.Interaction.HasResponded)
                await RespondAsync("❌ 发生内部错误", ephemeral: true);
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
    
    // =============================================
    #region  私有公共方法
    // =============================================
    /// <summary>
    /// 核心逻辑：将附件列表(无论来自Modal还是Message)下载并转存到目标B服的Cache Thread，
    /// 并更新 ReleaseCache 的状态。
    /// </summary>
    private async Task<bool> TransferAttachmentsToCacheAsync(
        ReleaseCache cache, 
        SocketThreadChannel sourceThreadChannel, 
        IEnumerable<IAttachment> attachments)
    {
        try 
        {
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

            // 1. 路由匹配 (源 -> 目标)
            // 检查源服务器是否有配置
            if (!deployConfig.GuildConfigs.TryGetValue(sourceGuildId, out var sourceGuildConfig))
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ 未在 GuildConfigs 中找到源服务器配置: {sourceGuildId}"), ephemeral: true);
                return false;
            }

            // 在源服务器配置中，查找哪个 Key (如 "男性向") 对应当前的 ParentChannelId
            // KeyValuePair<string, ulong>
            var categoryEntry = sourceGuildConfig.ValidChannels.FirstOrDefault(x => x.Value == sourceParentChannelId);

            // 如果 Key 为 null，说明当前帖子所在的父频道不在配置文件里
            if (string.IsNullOrEmpty(categoryEntry.Key))
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ 当前频道 {sourceParentChannelId} 未在配置文件的 ValidChannels 中定义"), ephemeral: true);
                return false;
            }
            
            // 例如: "男性向"
            string categoryName = categoryEntry.Key;
            _logger.Debug($"路由匹配成功: 分类名为 [{categoryName}]");

            // =======================================================
            // 正向查找 (在 CacheLocations 中找到 "B服目标")
            // =======================================================
            
            // 使用源服务器 ID 作为 Key 去查找 CacheLocations
            if (!deployConfig.CacheLocations.TryGetValue(sourceGuildId, out var targetLocationConfig))
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ 未在 CacheLocations 中找到源服务器对应的路由配置 {sourceGuildId}"), ephemeral: true);
                return false;
            }

            // 获取目标服务器 ID (B服)
            ulong targetGuildId = targetLocationConfig.Guild;

            // 根据分类名 ("男性向") 获取目标频道 ID
            if (!targetLocationConfig.Channels.TryGetValue(categoryName, out ulong targetChannelId))
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ 在 CacheLocations 中未找到分类 [{categoryName}] 的目标频道映射"), ephemeral: true);
                return false;
            }

            // =======================================================
            // 网络部分
            // =======================================================

            // =======================================================
            // 获取 Discord 实体对象并执行逻辑
            // =======================================================
            
            // 获取目标服务器的 SocketGuild 对象
            var targetGuild = Context.Client.GetGuild(targetGuildId);
            if (targetGuild == null)
            {
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ Bot 无法获取目标服务器实例: {targetGuildId}"), ephemeral: true);
                return false;
            }

            // 确定目标帖子 (Cache Thread)
            // 这一步逻辑：先查库，库里没有就新建
            var cacheThreadId = await _applicationService.QueryCacheChannelAsync(sourceThreadChannel.Id);
            
            // [DEBUG]
            // ulong? cacheThreadId = 1448173068706123879;
            // [DEBUG]

            IThreadChannel? cacheThread = null;

            // 如果有缓存记录
            // 获取目标帖子对象
            if (cacheThreadId is not null)
            {
                cacheThread = targetGuild.GetChannel(cacheThreadId.Value) as IThreadChannel;
                if (cacheThread == null)
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ 数据库有记录但无法获取目标频道实例: {cacheThreadId}"), ephemeral: true);
                    return false;
                }
                cache.Data.IsUpdate = true;
            }
            // 如果没有源贴的缓存记录
            // 获取目标频道对象 (通常是 IMessageChannel 或 ITextChannel)
            else
            {
                // 新建 Cache Thread 逻辑
                var targetChannel = targetGuild.GetChannel(targetChannelId) as IForumChannel;
                if (targetChannel == null)
                {
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", $"❌ Bot 无法获取目标频道实例(Forum): {targetChannelId}"), ephemeral: true);
                    return false;
                }

                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    cacheThread = await targetChannel.CreatePostAsync(Context.Channel.Name, text: $"""
                        cache thread metadata:
                        origin thread channel likn: https://discord.com/channels/{sourceThreadChannel.ParentChannel.Id}/{sourceThreadChannel.Id}
                        origin thread channel: {sourceThreadChannel.Name ?? "null"}
                        origin thread channel id: {sourceThreadChannel.Id}
                        origin thread created at: {sourceThreadChannel.CreatedAt.ToUniversalTime():O}
                        origin forum channel: {sourceThreadChannel.ParentChannel.Name ?? "null"}
                        origin forum channel id: {sourceThreadChannel.ParentChannel.Id}
                        origin author at created name: {sourceThreadChannel.Owner.DisplayName ?? "null"}
                        origin author id: {sourceThreadChannel.Owner.Id}
                        cache thread created at {DateTimeOffset.UtcNow:O}
                        """);
                });

                // ============
                // 本地缓存 Json 元数据处理
                // ============
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
                        OriginThreadChannelLink = $"https://discord.com/channels/{sourceThreadChannel.ParentChannel.Id}/{sourceThreadChannel.Id}",
                        OriginThreadChannel = sourceThreadChannel.Name ?? "null",
                        OriginThreadChannelId = sourceThreadChannel.Id,
                        OriginThreadCreatedAt = sourceThreadChannel.CreatedAt.ToUniversalTime().ToString("O"),
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
                    await FollowupAsync(components: _componentUtils.LoggerErrorComponente("上传失败", "❌ Bot 无法在目标频道创建帖子"), ephemeral: true);
                    return false;
                }
            }

            // =========================================================
            // 附件分两部分处理:
            // Discord CDN Url: 主要, 用于直接通过cdn url分发文件, 提升性能和响应速度, 同时节省带宽和流量
            // 本地文件: 作为备份, 如果 Discord CDN 让 Discord 给毙了就回退到本地文件上传
            // =========================================================
            string threadDir = Path.Combine(_appPath, localCachePath, $"{Context.Guild.Id}", $"{sourceParentChannelId}", $"{sourceThreadChannel.Id}");
            string draftDir = Path.Combine(threadDir, $"Draft_{Context.User.Id}");

            // 下载并转存附件
            var streams = new List<Stream>(); // 仅用于 finally 释放

            try
            {
                // 目录准备：存在则清空，不存在则创建
                if (Directory.Exists(draftDir))
                {
                    Directory.Delete(draftDir, true); // 递归删除
                }
                Directory.CreateDirectory(draftDir);

                _logger.Debug("准备下载文件到草稿目录: {DraftDir}", draftDir);
                
                // 遍历用户上传的所有附件
                // 3.2 下载文件到本地
                var downloadTasks = attachments.Select(async (attachment, index) =>
                {
                    try
                    {
                        await _httpRetryPolicy.ExecuteAsync(async () =>
                        {
                            // 生成有序文件名
                            var cleanOldName = attachment.Filename.Replace('_', '-');
                            var prefix = (index + 1).ToString().PadLeft(2, '0');
                            string safeFileName = $"{prefix}-{cleanOldName}";
                            string savePath = Path.Combine(draftDir, safeFileName);

                            // 下载流并写入文件
                            using var response = await _httpClient.GetAsync(attachment.Url, HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();

                            using var networkStream = await response.Content.ReadAsStreamAsync();
                            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                            
                            await networkStream.CopyToAsync(fileStream);
                            
                            _logger.Debug("已下载到本地: {FileName}", safeFileName);
                        });
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "文件下载失败: {FileName} Url: {Url}", attachment.Filename, attachment.Url);
                        return false;
                    }
                });

                var downloadResults = await Task.WhenAll(downloadTasks);
                if (downloadResults.Any(r => !r)) 
                {
                    // 可选：如果部分失败，是否中止？目前策略是继续尝试上传已成功的
                    _logger.Warning("部分文件下载失败");
                }

                // 3.3 准备上传到 Cache Thread (从本地读取)
                var localFiles = Directory.GetFiles(draftDir);
                if (localFiles.Length == 0)
                {
                    await FollowupAsync(components: _componentUtils.TextErrorComponente("❌ 未能成功下载任何文件，操作终止。"), ephemeral: true);
                    return false;
                }

                // 构造 FileAttachment 列表 (注意：FileAttachment 接受 path 会自动管理流，但这里我们要一次性发送)
                var fileAttachments = localFiles.Select(filePath => new FileAttachment(filePath)).ToList();

                IUserMessage? message = null;

                var messageMetadata = new
                {
                    CacheUpdate = DateTimeOffset.UtcNow.ToString("O")
                };

                await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    // 重新读取刚下载的文件发送
                    message = await cacheThread.SendFilesAsync(fileAttachments, text: $"cache create at {DateTimeOffset.UtcNow:O}");
                });

                // Dispose 掉 FileAttachments 中的流资源（如果 Discord.Net 没自动释放，这里显式释放是个好习惯，
                // 但 FileAttachment(string path) 模式下，SendFilesAsync 执行完通常会关闭句柄）
                foreach(var fa in fileAttachments) fa.Dispose();

                if (message is not null)
                {
                    // 4. 更新 Cache 对象状态
                    cache.CacheMessage = message;
                    cache.Data.CacheAttachmentUrls = message.Attachments.Select(a => a.Url).ToList();
                    cache.Data.CacheChannelId = targetChannelId;
                    cache.Data.CacheMessageId = message.Id;
                    cache.Data.CacheThreadId = cacheThread.Id;
                    cache.Data.CacheGuildId = cacheThread.Guild.Id;
                    cache.SetAttachment(message.Attachments.ToList());
                    
                    return true; // 成功
                }
                else
                {
                    await FollowupAsync(components: _componentUtils.TextErrorComponente("❌ 附件上传到缓存频道失败。"), ephemeral: true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TransferAttachmentsToCacheAsync 本地IO或上传异常");
                await FollowupAsync(components: _componentUtils.LoggerErrorComponente("内部错误", "处理附件本地化时发生异常"), ephemeral: true);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "TransferAttachmentsToCacheAsync 内部异常");
            await FollowupAsync(components: _componentUtils.LoggerErrorComponente("内部错误", "处理附件传输时发生异常"), ephemeral: true);
            return false;
        }
    }
    #endregion
}

public class UploadModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "提交数据";
    [ModalFileUpload("UpFiles")] 
    public IAttachment[]? Files { get; set; }
}

public class TextInputModal : IModal
{
    // IModal 接口要求的 Title，接收数据时其实不重要
    public string Title => "TextInput";

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
