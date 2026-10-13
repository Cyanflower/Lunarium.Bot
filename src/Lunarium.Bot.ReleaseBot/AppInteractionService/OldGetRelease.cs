// // .NET 核心库
// using System.Text;
// using System.Text.Json;
// using System.ComponentModel;
// using System.Text.RegularExpressions;
// using Microsoft.Extensions.DependencyInjection;

// // NuGet 包依赖
// using Discord;
// using Discord.Net;
// using Discord.Rest;
// using Discord.WebSocket;
// using Discord.Interactions;
// using Polly;
// using Polly.Retry;

// // 解决方案内引用项目
// using Lunarium.Bot.Logger;

// // 项目内命名空间
// using Lunarium.Bot.ReleaseBot.AuthUtils;
// using Lunarium.Bot.ReleaseBot.Models;
// using Lunarium.Bot.ReleaseBot.InteractionsCache;
// using Lunarium.Bot.ReleaseBot.ApplicationServices;
// using Lunarium.Bot.ReleaseBot.Utils;
// using Lunarium.Bot.ReleaseBot.TokenBucketServices;
// using Lunarium.Bot.ReleaseBot.CacheServices;

// namespace Lunarium.Bot.ReleaseBot.AppInteractionService;

// public class OldGetRelease : InteractionModuleBase<SocketInteractionContext>
// {
//     private string _appPath = AppContext.BaseDirectory;
//     const string localCachePath = "localCache";
//     private readonly AuthUtil _authUtil;
//     private readonly ILogger _logger;
//     private readonly ComponentUtils _componentUtils;
//     private readonly HttpClient _httpClient;
//     private readonly CacheService _cacheService;
//     private readonly ApplicationService _applicationService;
//     private readonly TokenBucketService _tokenBucketService;
//     private readonly AsyncRetryPolicy _httpRetryPolicy;
//     public OldGetRelease(
//         ILogger logger,
//         AuthUtil authUtil,
//         HttpClient httpClient,
//         CacheService cacheService,
//         ApplicationService applicationService,
//         TokenBucketService tokenBucketService,
//         [FromKeyedServices("HttpPolicy")] AsyncRetryPolicy httpRetryPolicy)
//     {
//         _logger = logger.ForContext("Release");
//         _authUtil = authUtil;
//         _httpClient = httpClient;
//         _cacheService = cacheService;
//         _applicationService = applicationService;
//         _componentUtils = new(_logger);
//         _tokenBucketService = tokenBucketService;
//         _httpRetryPolicy = httpRetryPolicy;
//     }

//     // =============================================
//     #region 业务方法
//     // =============================================
//     public async Task<MessageComponent> GetWorkAsync(WorkData data, int remainingToken, int tokenLimit, bool isConsumed)
//     {
//         try
//         {
//             IMessage? message = null;

//             // 尝试从远程 Discord 缓存获取消息
//             try
//             {
//                 message = await _cacheService.GetMessageAsync(data);
//             }
//             catch
//             {
//                 _logger.Warning("无法获取远程缓存消息，尝试回退到本地缓存。CacheId: {CacheMessageId}", data.CacheMessageId);
//                 message = null;
//             }

//             // 如果远程获取成功，返回标准的图库/下载组件
//             if (message is not null)
//             {
//                 var _attachments = message.Attachments;
//                 return new ComponentBuilderV2()
//                     .WithContainer(container =>
//                     {
//                         container.WithAccentColor(Color.Green); // 远程获取成功用绿色
//                         container.WithTextDisplay($"""
//                             ### 🎈 获取作品
//                             > -# 今日剩余可获取作品量: {remainingToken}/{tokenLimit}
//                             """);
//                         container.WithSeparator();

//                         if (data.CacheAttachmentUrls.Count > 0)
//                         {
//                             // 优先显示图片
//                             List<IAttachment>? imageAttachment = _attachments?
//                                 .Where(a => IsImageAttachment(a))
//                                 .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
//                                 .ToList();

//                             // 其他文件显示下载链接
//                             List<IAttachment>? otherAttachment = _attachments?
//                                 .Where(a => !IsImageAttachment(a))
//                                 .OrderBy(a => a.Filename, StringComparer.OrdinalIgnoreCase)
//                                 .ToList();

//                             if (imageAttachment?.Count > 0)
//                             {
//                                 container.WithTextDisplay($"""
//                                     **以下为图片附件:**
//                                     > -# 右键或长按图片下载, 或点击超链接下载
//                                     """);
//                                 MediaGalleryBuilder media = new();
//                                 StringBuilder sb = new();
//                                 foreach (var attachment in imageAttachment)
//                                 {
//                                     media.AddItem(attachment.Url);
//                                     sb.AppendLine($"🖼 {attachment.Filename}");
//                                     sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
//                                     sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
//                                 }
//                                 container.WithMediaGallery(media);
//                                 container.WithTextDisplay(sb.ToString());
//                             }
                            
//                             container.WithSeparator();
//                             if (otherAttachment?.Count > 0)
//                             {
//                                 container.WithTextDisplay($"""
//                                     **以下为非图片附件:**
//                                     > -# 点击超链接下载
//                                     """);
//                                 StringBuilder sb = new();
//                                 foreach (var attachment in otherAttachment)
//                                 {
//                                     sb.AppendLine($"📄 {attachment.Filename}");
//                                     sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB");
//                                     sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
//                                 }
//                                 container.WithTextDisplay(sb.ToString());
//                             }

//                             container.WithSeparator();
//                             container.WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)");
//                         }
//                     }).Build();
//             }

//             // 远程获取失败，进入本地回退逻辑
//             else
//             {
//                 // 构建本地存储路径 (需与 Release.cs 中的保存逻辑一致)
//                 // Path: Base / localCache / GuildId / ChannelId(Parent) / ThreadId / ReleaseMessageId
//                 string localSaveDir = Path.Combine(
//                     _appPath,
//                     localCachePath,
//                     $"{data.GuildId}",
//                     $"{data.ChannelId}",
//                     $"{data.ThreadId}",
//                     $"{data.ReleaseMessageId}");

//                 if (Directory.Exists(localSaveDir))
//                 {
//                     // 获取所有文件，排除 metadata.json
//                     var filePaths = Directory.GetFiles(localSaveDir)
//                         .Where(f => !Path.GetFileName(f).Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
//                         .ToList();

//                     if (filePaths.Count > 0)
//                     {
//                         // 准备文件附件
//                         var attachments = new List<FileAttachment>();
//                         var streams = new List<FileStream>(); // 用于后续释放资源

//                         try
//                         {
//                             foreach (var filePath in filePaths)
//                             {
//                                 // 使用 FileShare.Read 允许并发读取
//                                 var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
//                                 streams.Add(fs);
//                                 attachments.Add(new FileAttachment(fs, Path.GetFileName(filePath)));
//                             }

//                             // 直接发送文件 (Followup)
//                             // 注意：因为 GetWorkAsync 返回的是 Component，而 Component 不能带文件，
//                             // 所以这里必须直接调用 Interaction 发送文件。
//                             await Context.Interaction.FollowupWithFilesAsync(
//                                 attachments,
//                                 text: $"> ⚠️ **远程缓存不可用, 已自动从本地备份提取 {filePaths.Count} 个文件**",
//                                 ephemeral: true
//                             );

//                             // 返回一个提示性的组件，告知调用者已处理完毕
//                             return new ComponentBuilderV2()
//                                 .WithContainer(container => container
//                                     .WithAccentColor(Color.Orange) // 本地回退用橙色警告色
//                                     .WithTextDisplay($"""
//                                     ### 📂 本地备份已上传
//                                     由于远程缓存的 Discord CDN 资源失效, Bot 已尝试从服务器本地缓存上传文件。
//                                     > -# 今日剩余状态: {remainingToken}/{tokenLimit}
//                                     """)
//                                     .WithSeparator()
//                                     .WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)")
//                             ).Build();
//                         }
//                         catch (Exception ex)
//                         {
//                             _logger.Error(ex, "从本地缓存上传文件时发生错误");
//                             // 如果上传出错，流转到下方的错误返回
//                         }
//                         finally
//                         {
//                             // 清理流资源
//                             foreach (var fs in streams)
//                             {
//                                 await fs.DisposeAsync();
//                             }
//                         }
//                     }
//                 }

//                 // 远程和本地都失败
//                 _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, 1, isConsumed);
//                 return new ComponentBuilderV2()
//                     .WithContainer(container => container
//                         .WithAccentColor(Color.Red)
//                         .WithTextDisplay($"""
//                         ### ❌ 获取失败
//                         无法获取作品：远程缓存消息不存在或已被删除，且本地未找到备份文件。
//                         > -# 今日剩余状态: {_tokenBucketService.GetRemainingToken(Context.User.Id)}/{tokenLimit}
//                         """)
//                         .WithSeparator()
//                 ).Build();
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "GetWorkAsync 发生未处理异常");
//             throw;
//         }
//     }
//     #endregion

//     // =============================================
//     #region  交互回调
//     // =============================================
//     [ComponentInteraction("Alerts:*:*")]
//     public async Task HandleAlertsGetWorkAsync(string mode, int token)
//     {
//         if (Context.Interaction is not SocketMessageComponent interactionComponent)
//         {
//             _logger.Warning("交互回调: [GetWork] Context.Interaction is Not SocketMessageComponent");
//             return;
//         }
//         try
//         {
//             await DeferAsync(ephemeral: true);

//             // 2. 获取数据以读取 Alerts 内容
//             var data = await _applicationService.GetWorkDataAsync(interactionComponent.Message.Id);

//             // 如果数据丢失，返回错误
//             if (data is null)
//             {
//                 await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleAlerts"), ephemeral: true);
//                 return;
//             }

//             var component = new ComponentBuilderV2()
//                 .WithContainer(container =>
//                 {
//                     container.WithAccentColor(Color.Blue);
//                     container.WithTextDisplay("""
//                         ### 🎈 稍等一下!
//                         这是作者的声明
//                         请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
//                         """);

//                     // 核心：展示数据库中的 Alert 内容
//                     container.WithTextDisplay($"> {data.Alerts ?? "(作者未设置具体内容)"}", 1000);

//                     container.WithSeparator();

//                     // 根据传入的 mode 参数，生成对应的下一步按钮
//                     // 这些按钮的 CustomId 不带 "Alerts:" 前缀，因此点击后会直接触发原本的获取逻辑
//                     switch (mode)
//                     {
//                         case "GetWork":
//                             container.WithActionRow(action => action
//                                 .WithButton("我已阅读并确认", $"GetWork:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                             );
//                             break;

//                         case "GetWorkByRoR":
//                             container.WithActionRow(action => action
//                                .WithButton("我已阅读并确认", $"GetWorkByRoR:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                            );
//                             break;

//                         case "GetWorkByPwd":
//                             container.WithActionRow(action => action
//                                 .WithButton("我已阅读并确认", $"GetWorkByPwd:{interactionComponent.Message.Id}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                             );
//                             break;

//                         default:
//                             // 防止意外的 mode
//                             container.WithTextDisplay("⚠️ 内部错误：未知的获取模式");
//                             break;
//                     }
//                 });

//             await FollowupAsync(components: component.Build(), ephemeral: true);
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "HandleGetWorkAsync Exception");
//         }
//     }
    
//     [ComponentInteraction("AlertsCmd:*:*:*")]
//     public async Task HandleAlertsGetWorkByCommandAsync(string mode, int token, string messageId)
//     {
//         if (Context.Interaction is not SocketMessageComponent interactionComponent)
//         {
//             _logger.Warning("交互回调: [GetWork] Context.Interaction is Not SocketMessageComponent");
//             return;
//         }
//         try
//         {
//             await DeferAsync(ephemeral: true);

//             // 2. 获取数据以读取 Alerts 内容
//             var data = await _applicationService.GetWorkDataAsync(interactionComponent.Message.Id);

//             // 如果数据丢失，返回错误
//             if (data is null)
//             {
//                 await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null inside HandleAlerts"), ephemeral: true);
//                 return;
//             }

//             var component = new ComponentBuilderV2()
//                 .WithContainer(container =>
//                 {
//                     container.WithAccentColor(Color.Blue);
//                     container.WithTextDisplay("""
//                         ### 🎈 稍等一下!
//                         这是作者的声明
//                         请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
//                         """);
                    
//                     // 核心：展示数据库中的 Alert 内容
//                     container.WithTextDisplay($"> {data.Alerts ?? "(作者未设置具体内容)"}", 1000); 
                    
//                     container.WithSeparator();
                    
//                     // 根据传入的 mode 参数，生成对应的下一步按钮
//                     // 这些按钮的 CustomId 不带 "Alerts:" 前缀，因此点击后会直接触发原本的获取逻辑
//                     switch (mode)
//                     {
//                         case "GetWork":
//                             container.WithActionRow(action => action
//                                 .WithButton("我已阅读并确认", $"GetWork:{messageId}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                             );
//                             break;
                            
//                         case "GetWorkByRoR":
//                              container.WithActionRow(action => action
//                                 .WithButton("我已阅读并确认", $"GetWorkByRoR:{messageId}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                             );
//                             break;
                            
//                         case "GetWorkByPwd":
//                             container.WithActionRow(action => action
//                                 .WithButton("我已阅读并确认", $"GetWorkByPwd:{messageId}:{token}", ButtonStyle.Success, emote: new Emoji("✅"))
//                             );
//                             break;
                            
//                         default:
//                             // 防止意外的 mode
//                             container.WithTextDisplay("⚠️ 内部错误：未知的获取模式");
//                             break;
//                     }
//                 });

//             await FollowupAsync(components: component.Build(), ephemeral: true);
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "HandleGetWorkAsync Exception");
//         }
//     }
    
//     [ComponentInteraction("GetWork:*:*")]
//     public async Task HandleGetWorkAsync(ulong messageId, int token)
//     {
//         // [DEBUG] 1. 进入方法，记录上下文
//         _logger.Debug("[GetWork] 收到请求。User: {UserId}, Channel: {ChannelId}, ParamMessageId: {ParamMessageId}, ParamToken: {ParamToken}", 
//             Context.User.Id, Context.Channel.Id, messageId, token);

//         if (Context.Interaction is not SocketMessageComponent interactionComponent)
//         {
//             _logger.Warning("[GetWork] 交互上下文类型错误，预期 SocketMessageComponent，实际: {Type}", Context.Interaction?.GetType().Name);
//             return;
//         }

//         try
//         {
//             var threadChannel = (IThreadChannel)interactionComponent.Channel;
//             // 判定是否为楼主
//             if (Context.User.Id == threadChannel.OwnerId)
//             {
//                 _logger.Debug("[GetWork] 用户 {UserId} 是帖子拥有者，进入免消耗流程。", Context.User.Id);
                
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, 0);
//                 await DeferAsync(ephemeral: true);

//                 ulong targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;
//                 _logger.Debug("[GetWork] [Owner] 正在获取 WorkData, TargetId: {TargetId}", targetMsgId);

//                 var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//                 if (data is not null)
//                 {
//                     _logger.Debug("[GetWork] [Owner] 数据获取成功，准备构建组件。");
//                     var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                     await FollowupAsync(components: component, ephemeral: true);
//                 }
//                 else
//                 {
//                     _logger.Warning("[GetWork] [Owner] 数据获取失败 (data is null)。TargetId: {TargetId}", targetMsgId);
//                     await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                 }
//                 return;
//             }
//             else
//             {
//                 // 普通用户流程
//                 _logger.Debug("[GetWork] 用户 {UserId} 是普通访客，进入消耗流程。", Context.User.Id);

//                 var targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;

//                 await DeferAsync(ephemeral: true);
                
//                 // 检查是否已下载过 (决定 TokenCost)
//                 var hasDownloaded = await _applicationService.HasDownloadRecordAsync(Context.User.Id, targetMsgId);
//                 var tokenCost = 1;
//                 if (hasDownloaded)
//                 {
//                     tokenCost = 0;
//                     _logger.Debug("[GetWork] 用户 {UserId} 已有下载记录，本次 TokenCost 设为 0。", Context.User.Id);
//                 }
//                 else
//                 {
//                     _logger.Debug("[GetWork] 用户 {UserId} 无下载记录，TokenCost 设为 1。", Context.User.Id);
//                 }

//                 // 获取 Token
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, tokenCost);
                
//                 _logger.Debug("[GetWork] Token 状态: Allowable={Allowable}, IsConsumed={IsConsumed}, Remaining={Remaining}/{Limit}", 
//                     allowable, isConsumed, remainingToken, tokenLimit);

//                 try
//                 {
//                     // 特殊逻辑：如果前端传来的 token 参数为 0 (可能是某种强制覆盖标志?)
//                     if (token == 0)
//                     {
//                         _logger.Debug("[GetWork] 检测到 ParamToken 为 0，强制设置 Allowable = true。");
//                         allowable = true;
//                     }

//                     if (!allowable)
//                     {
//                         _logger.Debug("[GetWork] Token 不足且未被允许，返回限制提示。");
//                         var component = new ComponentBuilderV2()
//                             .WithContainer(container => container
//                                 .WithAccentColor(Color.Red)
//                                 .WithTextDisplay($"""
//                                 ### 🎈 到达限制
//                                 您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
//                                 > -# 当前状态: {remainingToken}/{tokenLimit}
//                                 """)
//                         );
//                         await FollowupAsync(components: component.Build(), ephemeral: true);
//                         return;
//                     }

                    
//                     _logger.Debug("[GetWork] 正在从数据库获取 WorkData, TargetId: {TargetId}", targetMsgId);
//                     var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//                     if (data is not null)
//                     {
//                         _logger.Debug("[GetWork] WorkData 获取成功，调用 GetWorkAsync 并记录下载日志。");

//                         var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                         await FollowupAsync(components: component, ephemeral: true);

//                         var downloadLog = new DownloadLog()
//                         {
//                             UserId = Context.User.Id,
//                             ThreadId = Context.Channel.Id,
//                             ReleaseMessageId = interactionComponent.Message.Id
//                         };
//                         await _applicationService.InsertDownloadLogAsync(downloadLog);
//                         _logger.Debug("[GetWork] 流程完成，下载日志已插入。");
//                     }
//                     else
//                     {
//                         _logger.Error("[GetWork] ❌ 数据获取失败 (data is null)，正在回滚 Token。TargetId: {TargetId}", targetMsgId);
                        
//                         _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                         await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                     }
//                 }
//                 catch (Exception innerEx)
//                 {
//                     _logger.Error(innerEx, "[GetWork] 内部处理发生异常，正在执行 Token 回滚。User: {UserId}", Context.User.Id);
//                     _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                     throw; // 继续抛出以便外层捕获或全局处理
//                 }
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "[GetWork] HandleGetWorkAsync 发生未处理异常。User: {UserId}", Context.User.Id);
//         }
//     }

//     [ComponentInteraction("GetWorkByRoR:*:*")]
//     public async Task HandleGetWorkByRoRAsync(ulong messageId, int token)
//     {
//         // [DEBUG] 1. 进入方法
//         _logger.Debug("[GetWorkByRoR] 收到请求。User: {UserId}, ParamMessageId: {ParamMessageId}, ParamToken: {ParamToken}",
//             Context.User.Id, messageId, token);

//         if (Context.Interaction is not SocketMessageComponent interactionComponent)
//         {
//             _logger.Warning("[GetWorkByRoR] 交互类型错误。预期 SocketMessageComponent，实际: {Type}", Context.Interaction?.GetType().Name);
//             return;
//         }

//         try
//         {
//             var threadChannel = (IThreadChannel)interactionComponent.Channel;
//             // 判定是否为楼主
//             if (Context.User.Id == threadChannel.OwnerId)
//             {
//                 _logger.Debug("[GetWorkByRoR] 用户 {UserId} 是帖子拥有者，进入免消耗流程。", Context.User.Id);

//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, 0);
//                 await DeferAsync(ephemeral: true);

//                 ulong targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;
//                 var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//                 if (data is not null)
//                 {
//                     _logger.Debug("[GetWorkByRoR] [Owner] 数据获取成功。");
//                     var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                     await FollowupAsync(components: component, ephemeral: true);
//                 }
//                 else
//                 {
//                     _logger.Warning("[GetWorkByRoR] [Owner] 数据获取失败 (data is null)。TargetId: {TargetId}", targetMsgId);
//                     await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                 }
//                 return;
//             }
//             else
//             {
//                 // 普通用户流程
//                 _logger.Debug("[GetWorkByRoR] 用户 {UserId} 是普通访客，准备申请 Token。", Context.User.Id);

//                 ulong targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;

//                 await DeferAsync(ephemeral: true);
                    
//                 // 检查是否已下载 (决定 TokenCost)
//                 var hasDownloaded = await _applicationService.HasDownloadRecordAsync(Context.User.Id, targetMsgId);
//                 var tokenCost = 1;
//                 if (hasDownloaded)
//                 {
//                     tokenCost = 0;
//                     _logger.Debug("[GetWorkByRoR] 用户 {UserId} 已有下载记录，TokenCost 设为 0。", Context.User.Id);
//                 }

//                 // 获取 Token
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, tokenCost);

//                 _logger.Debug("[GetWorkByRoR] Token 状态: Allowable={Allowable}, IsConsumed={IsConsumed}, Remaining={Remaining}/{Limit}",
//                     allowable, isConsumed, remainingToken, tokenLimit);

//                 try
//                 {
//                     if (token == 0)
//                     {
//                         _logger.Debug("[GetWorkByRoR] 检测到 ParamToken 为 0，强制设置 Allowable = true。");
//                         allowable = true;
//                     }

//                     if (!allowable)
//                     {
//                         _logger.Debug("[GetWorkByRoR] Token 不足且未被允许，返回限制提示。");
//                         var component = new ComponentBuilderV2()
//                             .WithContainer(container => container
//                                 .WithAccentColor(Color.Red)
//                                 .WithTextDisplay($"""
//                                 ### 🎈 到达限制
//                                 您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
//                                 > -# 当前状态: {remainingToken}/{tokenLimit}
//                                 """)
//                         );
//                         await FollowupAsync(components: component.Build(), ephemeral: true);
//                         return;
//                     }

//                     // ==========================================================
//                     // 核心逻辑：检查前置条件 (Reaction 或 Reply)
//                     // ==========================================================
//                     var checkChannelId = interactionComponent.Channel.Id;
//                     var checkUserId = Context.User.Id;

//                     _logger.Debug("[GetWorkByRoR] 正在检查前置条件 (RoR)。TargetChannel: {ChannelId}, User: {UserId}", checkChannelId, checkUserId);

//                     var hasReaction = await _applicationService.HasReactionRecordAsync(checkUserId, checkChannelId);
//                     var hasReply = await _applicationService.HasMessageRecordAsync(checkUserId, checkChannelId);

//                     _logger.Debug("[GetWorkByRoR] 前置条件检查结果: HasReaction={HasReaction}, HasReply={HasReply}", hasReaction, hasReply);

//                     if (hasReaction || hasReply)
//                     {
//                         _logger.Debug("[GetWorkByRoR] ✅ 前置条件满足，正在获取 WorkData...");

//                         var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//                         if (data is not null)
//                         {
//                             _logger.Debug("[GetWorkByRoR] WorkData 获取成功，调用 GetWorkAsync。");

//                             var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                             await FollowupAsync(components: component, ephemeral: true);

//                             var downloadLog = new DownloadLog()
//                             {
//                                 UserId = Context.User.Id,
//                                 ThreadId = Context.Channel.Id,
//                                 ReleaseMessageId = interactionComponent.Message.Id
//                             };
//                             await _applicationService.InsertDownloadLogAsync(downloadLog);
//                             _logger.Debug("[GetWorkByRoR] 流程结束，下载日志已记录。");
//                         }
//                         else
//                         {
//                             _logger.Error("[GetWorkByRoR] ❌ WorkData 为空，正在回滚 Token。TargetId: {TargetId}", targetMsgId);

//                             _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                             await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                         }
//                     }
//                     else
//                     {
//                         _logger.Debug("[GetWorkByRoR] ⚠️ 前置条件未满足 (无点赞或回复)，拒绝访问并回滚 Token。");

//                         _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                         var component = new ComponentBuilderV2()
//                             .WithContainer(container => container
//                                 .WithAccentColor(Color.Red)
//                                 .WithTextDisplay($"""
//                                 ### ⚠️ 前置条件未满足
//                                 至少先对帖子点一个赞或发送一条回复哦
//                                 """)
//                         );
//                         await FollowupAsync(components: component.Build(), ephemeral: true);
//                         return;
//                     }
//                 }
//                 catch (Exception innerEx)
//                 {
//                     _logger.Error(innerEx, "[GetWorkByRoR] 内部逻辑异常，正在回滚 Token。User: {UserId}", Context.User.Id);
//                     _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                     throw;
//                 }
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "[GetWorkByRoR] HandleGetWorkByRoRAsync 发生未处理异常。");
//         }
//     }

//     [ComponentInteraction("GetWorkByPwd:*:*")]
//     public async Task HandleGetWorkByPwdAsync(ulong messageId, int token)
//     {
//         // [DEBUG] 1. 进入方法
//         _logger.Debug("[GetWorkByPwd] 收到请求。User: {UserId}, ParamMessageId: {ParamMessageId}, ParamToken: {ParamToken}",
//             Context.User.Id, messageId, token);

//         try
//         {
//             if (Context.Interaction is not SocketMessageComponent interactionComponent)
//             {
//                 _logger.Warning("[GetWorkByPwd] 交互类型错误。预期 SocketMessageComponent，实际: {Type}", Context.Interaction?.GetType().Name);
//                 return;
//             }

//             var threadChannel = (IThreadChannel)interactionComponent.Channel;

//             // 判定是否为楼主
//             if (Context.User.Id == threadChannel.OwnerId)
//             {
//                 _logger.Debug("[GetWorkByPwd] 用户 {UserId} 是帖子拥有者，跳过密码验证，进入直接获取流程。", Context.User.Id);

//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, 0);
//                 await DeferAsync(ephemeral: true);

//                 ulong targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;
//                 var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//                 if (data is not null)
//                 {
//                     _logger.Debug("[GetWorkByPwd] [Owner] 数据获取成功。");
//                     var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                     await FollowupAsync(components: component, ephemeral: true);
//                 }
//                 else
//                 {
//                     _logger.Warning("[GetWorkByPwd] [Owner] 数据获取失败 (data is null)。TargetId: {TargetId}", targetMsgId);
//                     await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                 }
//                 return;
//             }
//             else
//             {
//                 // 普通用户流程
//                 _logger.Debug("[GetWorkByPwd] 用户 {UserId} 是普通访客，准备申请 Token 并弹出 Modal。", Context.User.Id);

//                 ulong targetMsgId = messageId == 0 ? interactionComponent.Message.Id : messageId;

//                 // 检查下载记录
//                 var hasDownloaded = await _applicationService.HasDownloadRecordAsync(Context.User.Id, targetMsgId);
//                 var tokenCost = 1;
//                 if (hasDownloaded)
//                 {
//                     tokenCost = 0;
//                     _logger.Debug("[GetWorkByPwd] 用户 {UserId} 已有下载记录，TokenCost 设为 0。", Context.User.Id);
//                 }

//                 // 预先获取 Token (在弹出 Modal 前就要确定是否有资格)
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, tokenCost);

//                 _logger.Debug("[GetWorkByPwd] Token 状态: Allowable={Allowable}, IsConsumed={IsConsumed}, Remaining={Remaining}/{Limit}",
//                     allowable, isConsumed, remainingToken, tokenLimit);

//                 try
//                 {
//                     if (token == 0)
//                     {
//                         _logger.Debug("[GetWorkByPwd] ParamToken 为 0，强制 Allowable = true。");
//                         allowable = true;
//                     }

//                     if (!allowable)
//                     {
//                         _logger.Debug("[GetWorkByPwd] Token 不足且未被允许，返回限制提示。");
//                         var component = new ComponentBuilderV2()
//                             .WithContainer(container => container
//                                 .WithAccentColor(Color.Red)
//                                 .WithTextDisplay($"""
//                                 ### 🎈 到达限制
//                                 您今日通过本BOT获取的作品数量已到达本日上限, 限制将在 0 点恢复
//                                 > -# 当前状态: {remainingToken}/{tokenLimit}
//                                 """)
//                         );
//                         await RespondAsync(components: component.Build(), ephemeral: true);
//                         return;
//                     }

//                     // 构建 Modal
//                     // 注意：CustomId 携带了 isConsumed 和 tokenCost 状态，以便 Modal 回调失败时回滚
//                     var modalCustomId = $"Release:Modal:EnterPassword:{targetMsgId}:{(isConsumed ? "true" : "false")}:{tokenCost}";

//                     _logger.Debug("[GetWorkByPwd] Token 预扣成功，正在构建 Modal。ModalCustomId: {CustomId}", modalCustomId);

//                     var modal = new ModalBuilder()
//                         .WithTitle("输入提取码")
//                         .WithCustomId(modalCustomId)
//                         .AddTextInput(
//                             label: "设置提取码 (1 到 50 字)",
//                             customId: "Password",
//                             placeholder: "填写贴内的提取码...",
//                             maxLength: 50,
//                             minLength: 1,
//                             required: true,
//                             style: TextInputStyle.Short
//                         );

//                     // 尝试发送 Modal
//                     await _httpRetryPolicy.ExecuteAsync(async () =>
//                     {
//                         _logger.Debug("[GetWorkByPwd] 正在发送 Modal 响应...");
//                         await RespondWithModalAsync(modal.Build());
//                     });

//                     _logger.Debug("[GetWorkByPwd] Modal 发送成功。等待用户提交...");
//                 }
//                 catch (Exception innerEx)
//                 {
//                     // 这里的异常通常是网络问题或 Interaction 已经超时/被响应
//                     _logger.Error(innerEx, "[GetWorkByPwd] 发送 Modal 失败，正在回滚 Token。User: {UserId}", Context.User.Id);

//                     _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, 1, isConsumed);
//                     throw;
//                 }
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "[GetWorkByPwd] HandleGetWorkByPwdAsync 发生未处理异常。");
//         }
//     }
    
//     [ModalInteraction("Release:Modal:EnterPassword:*:*:*")]
//     public async Task HandleGetWorkByPwdModalAsync(ulong messageId, string isConsumedStr, int tokenCost, PasswordInputModal modal)
//     {
//         // 将字符串参数转为布尔值，方便后续逻辑
//         bool isConsumed = isConsumedStr == "true";
//         ulong targetMsgId = messageId == 0 ? ((IModalInteraction)Context.Interaction).Message.Id : messageId;

//         // [DEBUG] 1. 进入方法，记录上下文状态
//         _logger.Debug("[Modal:Pwd] 收到密码提交。User: {UserId}, TargetMsgId: {TargetId}, TokenCost: {Cost}, IsConsumed: {IsConsumed}", 
//             Context.User.Id, targetMsgId, tokenCost, isConsumed);

//         if (Context.Interaction is not IModalInteraction modalInteraction)
//         {
//             _logger.Warning("[Modal:Pwd] 交互类型错误。预期 IModalInteraction，实际: {Type}", Context.Interaction?.GetType().Name);
//             return;
//         }

//         try
//         {
//             await DeferAsync(ephemeral: true);

//             var password = modal.Password.Trim();
            
//             // [DEBUG] 2. 校验输入
//             if (string.IsNullOrWhiteSpace(password))
//             {
//                 _logger.Debug("[Modal:Pwd] 用户输入了空密码或纯空格。");
//                 await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 密码格式错误\n密码不能为空或只有空格"), ephemeral: true);
//                 // 注意：这里没有回滚逻辑？通常输入格式错误也应该视为失败，建议考虑是否需要回滚，
//                 // 但原逻辑未回滚，为了保持一致性暂时保留，或者您可以在此处添加 UndoGetToken。
//                 return;
//             }

//             _logger.Debug("[Modal:Pwd] 正在获取 WorkData...");
//             var data = await _applicationService.GetWorkDataAsync(targetMsgId);

//             if (data is not null)
//             {
//                 // [DEBUG] 3. 核心：比对密码
//                 if (password != data.ReleasePassword)
//                 {
//                     _logger.Debug("[Modal:Pwd] ❌ 密码不匹配。User: {UserId}", Context.User.Id);
                    
//                     // 执行回滚
//                     _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                     _logger.Debug("[Modal:Pwd] 已回滚 Token (密码错误)。");

//                     var pwdErrorComponent = new ComponentBuilderV2()
//                         .WithContainer(container => container
//                             .WithAccentColor(Color.Red)
//                             .WithTextDisplay($"""
//                             ### ⚠️ 提取码错误
//                             请检查作者发布的提取码和输入的提取码
//                             """)
//                     );
//                     await FollowupAsync(components: pwdErrorComponent.Build(), ephemeral: true);
//                     return;
//                 }

//                 _logger.Debug("[Modal:Pwd] ✅ 密码验证通过，开始获取组件。");

//                 // 获取并发送内容
//                 var component = await GetWorkAsync(data, _tokenBucketService.GetRemainingToken(Context.User.Id), _tokenBucketService.TokenLimit, isConsumed);
//                 await FollowupAsync(components: component, ephemeral: true);

//                 // 记录下载日志
//                 var downloadLog = new DownloadLog()
//                 {
//                     UserId = Context.User.Id,
//                     ThreadId = Context.Channel.Id,
//                     ReleaseMessageId = targetMsgId
//                 };
//                 await _applicationService.InsertDownloadLogAsync(downloadLog);
//                 _logger.Debug("[Modal:Pwd] 流程结束，下载日志已记录。");
//             }
//             else
//             {
//                 _logger.Error("[Modal:Pwd] ❌ 获取 WorkData 失败 (data is null)。TargetId: {TargetId}", targetMsgId);
                
//                 // 执行回滚
//                 _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                 _logger.Debug("[Modal:Pwd] 已回滚 Token (数据丢失)。");

//                 await FollowupAsync(components: _componentUtils.LoggerErrorComponente("⚠️ 获取失败", "data is not null"), ephemeral: true);
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "[Modal:Pwd] HandleGetWorkByPwdModalAsync 发生异常。User: {UserId}", Context.User.Id);
            
//             // 确保未响应时给出提示，并回滚
//             if (!Context.Interaction.HasResponded)
//             {
//                 _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                 _logger.Debug("[Modal:Pwd] 已回滚 Token (异常捕获)。");
//                 await RespondAsync("❌ 处理文件上传时发生错误。", ephemeral: true);
//             }
//             else
//             {
//                 // 如果已经响应过（例如 Defer 后出错），这里仍需回滚
//                 _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                 _logger.Debug("[Modal:Pwd] 已回滚 Token (异常捕获 - 已响应)。");
//             }
//         }
//     }
//     #endregion

//     // =============================================
//     #region  斜杠命令 
//     // =============================================
//     [SlashCommand("获取作品", "获取本帖发布的最新作品")]
//     public async Task GetWorkCommandAsync()
//     {
//         // [DEBUG] 1. 进入命令
//         _logger.Debug("[Slash:GetWork] 收到命令请求。User: {UserId}, Channel: {ChannelId}", Context.User.Id, Context.Channel.Id);

//         try
//         {
//             // 基础鉴权
//             var requiredConditions = AuthorizationConditions.IsInAuthorizedGuild | AuthorizationConditions.IsInPermittedChannel;
//             var authResult = await _authUtil.Authorization(requiredConditions, Context);
//             if (authResult.Status != AuthorizationStatus.Success)
//             {
//                 var failedReasonComponent = AuthUtil.GetFailedReason(authResult);
//                 await RespondAsync(components: failedReasonComponent, ephemeral: true);
//                 return;
//             }

//             // 检查是否在帖子内
//             if (Context.Channel is not IThreadChannel threadChannel)
//             {
//                 _logger.Warning("[Slash:GetWork] 环境错误。当前频道不是 ThreadChannel (Type: {Type})", Context.Channel.GetType().Name);
//                 await RespondAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 错误\n请在帖子内使用此命令"), ephemeral: true);
//                 return;
//             }

//             await DeferAsync(ephemeral: true);

//             _logger.Debug("[Slash:GetWork] 正在查找本帖 ({ThreadId}) 的最新 WorkData...", threadChannel.Id);
//             var data = await _applicationService.GetLatestWorkDataByThreadIdAsync(threadChannel.Id);

//             if (data is null)
//             {
//                 _logger.Warning("[Slash:GetWork] 未找到作品数据。ThreadId: {ThreadId}", threadChannel.Id);
//                 await FollowupAsync(components: _componentUtils.TextErrorComponente("### ⚠️ 未找到作品\n本帖似乎还没有发布过作品，或者作品已被移除。"), ephemeral: true);
//                 return;
//             }
            
//             _logger.Debug("[Slash:GetWork] 找到作品数据。MsgId: {MsgId}, Model: {Model}", data.ReleaseMessageId, data.ReleaseModel);

//             // 楼主特权
//             if (Context.User.Id == threadChannel.OwnerId)
//             {
//                 _logger.Debug("[Slash:GetWork] 用户是楼主，免消耗直接获取。");
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, 0);

//                 if (data is not null) // Double check but data is checked above
//                 {
//                     var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                     await FollowupAsync(components: component, ephemeral: true);
//                 }
//                 else
//                 {
//                     // Should theoretically not happen due to check above, but safe coding
//                     await FollowupAsync(components: _componentUtils.LoggerErrorComponente("获取失败", "data is null"), ephemeral: true);
//                 }
//                 return;
//             }
//             else
//             {
//                 // 普通用户逻辑
//                 _logger.Debug("[Slash:GetWork] 用户是普通访客，开始 Token 计算逻辑。");

//                 var tokenCost = 1;
//                 if(await _applicationService.HasDownloadRecordAsync(Context.User.Id, data.ReleaseMessageId))
//                 {
//                     tokenCost = 0;
//                     _logger.Debug("[Slash:GetWork] 已有下载记录，TokenCost = 0");
//                 }
                
//                 // 预扣除 Token
//                 (var allowable, var isConsumed, var remainingToken, var tokenLimit) = _tokenBucketService.ConsumeToken(Context.User.Id, Context.Channel.Id, tokenCost);
                
//                 _logger.Debug("[Slash:GetWork] Token 初始状态: Allowable={Allowable}, Remaining={Remaining}/{Limit}", allowable, remainingToken, tokenLimit);

//                 // 如果作品配置为不消耗 Token，强制允许
//                 if (!data.IsConsumeToken)
//                 {
//                     allowable = true;
//                     _logger.Debug("[Slash:GetWork] 作品配置为 IsConsumeToken=false，强制 Allowable=true");
//                 }

//                 if (data.EnableAlerts)
//                 {
//                     var component = new ComponentBuilderV2()
//                         .WithContainer(container =>
//                         {
//                             container.WithAccentColor(Color.Blue);
//                             container.WithTextDisplay("""
//                                 ### 🎈 稍等一下!
//                                 这是作者的声明
//                                 请仔细阅读一下作者声明的内容，确认后方可获取作品哦：
//                                 """);

//                             // 核心：展示数据库中的 Alert 内容
//                             container.WithTextDisplay($"> {data.Alerts ?? "(作者未设置具体内容)"}", 1000);

//                             container.WithSeparator();

//                             // 根据传入的 mode 参数，生成对应的下一步按钮
//                             // 这些按钮的 CustomId 不带 "Alerts:" 前缀，因此点击后会直接触发原本的获取逻辑
//                             switch (data.ReleaseModel)
//                             {
//                                 case ReleaseModel.NoLimit:
//                                     container.WithActionRow(action => action
//                                         .WithButton("我已阅读并确认", $"GetWork:{data.ReleaseMessageId}:{(data.IsConsumeToken ? 1 : 0)}", ButtonStyle.Success, emote: new Emoji("✅"))
//                                     );
//                                     break;

//                                 case ReleaseModel.ReactionOrReply:
//                                     container.WithActionRow(action => action
//                                     .WithButton("我已阅读并确认", $"GetWorkByRoR:{data.ReleaseMessageId}:{(data.IsConsumeToken ? 1 : 0)}", ButtonStyle.Success, emote: new Emoji("✅"))
//                                 );
//                                     break;

//                                 case ReleaseModel.Password:
//                                     container.WithActionRow(action => action
//                                         .WithButton("我已阅读并确认", $"GetWorkByPwd:{data.ReleaseMessageId}:{(data.IsConsumeToken ? 1 : 0)}", ButtonStyle.Success, emote: new Emoji("✅"))
//                                     );
//                                     break;

//                                 default:
//                                     // 防止意外的 mode
//                                     container.WithTextDisplay("⚠️ 内部错误：未知的获取模式");
//                                     break;
//                             }
//                         });

//                     await FollowupAsync(components: component.Build(), ephemeral: true);
//                 }
//                 else
//                 {
//                     switch (data.ReleaseModel)
//                     {
//                         case ReleaseModel.NoLimit:
//                             {
//                                 _logger.Debug("[Slash:GetWork] 模式: NoLimit。直接发放。");
//                                 var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                                 await FollowupAsync(components: component, ephemeral: true);

//                                 var downloadLog = new DownloadLog() { UserId = Context.User.Id, ThreadId = Context.Channel.Id, ReleaseMessageId = data.ReleaseMessageId };
//                                 await _applicationService.InsertDownloadLogAsync(downloadLog);
//                                 return;
//                             }
//                         case ReleaseModel.ReactionOrReply:
//                             {
//                                 _logger.Debug("[Slash:GetWork] 模式: RoR。检查前置条件...");
//                                 // 检查点赞或回复
//                                 var hasReaction = await _applicationService.HasReactionRecordAsync(Context.User.Id, Context.Channel.Id);
//                                 var hasReply = await _applicationService.HasMessageRecordAsync(Context.User.Id, Context.Channel.Id);

//                                 _logger.Debug("[Slash:GetWork] RoR 检查结果: HasReaction={HasReaction}, HasReply={HasReply}", hasReaction, hasReply);

//                                 if (hasReaction || hasReply)
//                                 {
//                                     var component = await GetWorkAsync(data, remainingToken, tokenLimit, isConsumed);
//                                     await FollowupAsync(components: component, ephemeral: true);

//                                     var downloadLog = new DownloadLog() { UserId = Context.User.Id, ThreadId = Context.Channel.Id, ReleaseMessageId = data.ReleaseMessageId };
//                                     await _applicationService.InsertDownloadLogAsync(downloadLog);
//                                 }
//                                 else
//                                 {
//                                     _logger.Debug("[Slash:GetWork] ⚠️ RoR 条件未满足，回滚 Token 并提示。");
//                                     // 前置条件未满足，回滚 Token
//                                     _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);
//                                     var component = new ComponentBuilderV2()
//                                         .WithContainer(container => container
//                                             .WithAccentColor(Color.Red)
//                                             .WithTextDisplay($"""
//                                             ### ⚠️ 前置条件未满足
//                                             至少先对帖子点一个赞或发送一条回复哦
//                                             """)
//                                     );
//                                     await FollowupAsync(components: component.Build(), ephemeral: true);
//                                 }
//                                 return;
//                             }
//                         case ReleaseModel.Password:
//                             {
//                                 _logger.Debug("[Slash:GetWork] 模式: Password。");
//                                 // 关键逻辑日志：解释为什么这里要回滚
//                                 _logger.Debug("[Slash:GetWork] 密码模式需转入按钮交互流程，正在回滚预扣除的 Token，等待用户点击按钮。");

//                                 // 1. 如果是密码模式，我们回滚 Token (因为还没输入密码)。
//                                 _tokenBucketService.UndoGetToken(Context.User.Id, Context.Channel.Id, tokenCost, isConsumed);

//                                 // 2. 发送一个带有“输入提取码”按钮的消息
//                                 var pwdComponent = new ComponentBuilderV2()
//                                     .WithContainer(container => container
//                                         .WithAccentColor(Color.Blue)
//                                         .WithTextDisplay("### 🔒 需要提取码", 1000)
//                                         .WithTextDisplay("获取该作品需要输入提取码。", 1001)
//                                         .WithActionRow(row => row
//                                             // 这里的 CustomId 构造要匹配现有的 HandleGetWorkByPwdAsync 逻辑所需的格式
//                                             .WithButton(
//                                                 label: "输入提取码",
//                                                 customId: $"GetWorkByPwd:{data.ReleaseMessageId}:{(data.IsConsumeToken ? 1 : 0)}",
//                                                 style: ButtonStyle.Primary,
//                                                 emote: new Emoji("🔑"))
//                                         )
//                                     );

//                                 await FollowupAsync(components: pwdComponent.Build(), ephemeral: true);
//                                 return;
//                             }
//                     }
//                 }
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.Error(ex, "[Slash:GetWork] 命令执行发生异常。User: {UserId}", Context.User.Id);
//         }
//     }
//     #endregion
    
//     private static bool IsImageAttachment(IAttachment attachment)
//     {
//         var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
//         return imageExtensions.Any(ext =>
//             attachment.Filename.ToLowerInvariant().EndsWith(ext));
//     }
// }

