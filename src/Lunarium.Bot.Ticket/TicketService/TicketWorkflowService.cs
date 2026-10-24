using Discord;
using Discord.WebSocket;

using Lunarium.Bot.Framework;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Logging;

using Polly.Retry;

namespace Lunarium.Bot.Ticket.TicketService;

/// <summary>操作人快照（审批人 / 拒绝人）。系统自动操作时用 <see cref="System"/>。</summary>
/// <param name="Id">用户ID；系统操作为 0。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="UserName">用户名。</param>
public sealed record TicketActor(ulong Id, string DisplayName, string UserName)
{
    /// <summary>从交互用户构造。</summary>
    public static TicketActor From(IUser user) => new(user.Id, user.GlobalName ?? user.Username, user.Username);

    /// <summary>自动通过等系统行为。</summary>
    public static TicketActor System { get; } = new(0, "系统", "system");
}

/// <summary>审批 / 拒绝的结果。</summary>
/// <param name="Success">是否完成状态迁移。</param>
/// <param name="Message">给操作人的回执文本。</param>
public sealed record WorkflowResult(bool Success, string Message)
{
    public static WorkflowResult Ok(string message) => new(true, message);

    public static WorkflowResult Fail(string message) => new(false, message);
}

/// <summary>
/// 工单生命周期编排：审核通过（建频道）、拒绝（不建频道）。
/// <para>
/// 与交互层无关，自动通过的后台任务也会复用本服务。
/// 所有 Discord 副作用都"尽力而为"：频道与状态落盘成功即视为审批成功，消息/私信失败只记日志。
/// </para>
/// </summary>
public sealed class TicketWorkflowService
{
    /// <summary>自动通过时追加到开场消息的安抚文案。</summary>
    private const string AutoApproveApology =
        "⏳ 本条工单为系统自动通过，管理组目前较为繁忙、人手有限，可能无法立即回复，请耐心等待。";

    /// <summary>工单频道的权限覆盖（提交人）。</summary>
    private static readonly OverwritePermissions CreatorPermissions = new(
        viewChannel: PermValue.Allow,
        sendMessages: PermValue.Allow,
        attachFiles: PermValue.Allow,
        embedLinks: PermValue.Allow,
        addReactions: PermValue.Allow,
        readMessageHistory: PermValue.Allow,
        useExternalEmojis: PermValue.Allow);

    /// <summary>工单频道的权限覆盖（管理组身份组）。</summary>
    private static readonly OverwritePermissions AdminPermissions = new(
        viewChannel: PermValue.Allow,
        sendMessages: PermValue.Allow,
        attachFiles: PermValue.Allow,
        embedLinks: PermValue.Allow,
        addReactions: PermValue.Allow,
        readMessageHistory: PermValue.Allow,
        useExternalEmojis: PermValue.Allow,
        manageMessages: PermValue.Allow,
        manageChannel: PermValue.Allow);

    /// <summary>工单频道的权限覆盖（Bot 自己）。</summary>
    private static readonly OverwritePermissions BotPermissions = new(
        viewChannel: PermValue.Allow,
        sendMessages: PermValue.Allow,
        attachFiles: PermValue.Allow,
        embedLinks: PermValue.Allow,
        addReactions: PermValue.Allow,
        readMessageHistory: PermValue.Allow,
        useExternalEmojis: PermValue.Allow,
        manageMessages: PermValue.Allow,
        manageChannel: PermValue.Allow);

    /// <summary>关闭后提交人只剩只读权限。</summary>
    private static readonly OverwritePermissions CreatorReadOnlyPermissions = new(
        viewChannel: PermValue.Allow,
        sendMessages: PermValue.Deny,
        attachFiles: PermValue.Deny,
        addReactions: PermValue.Deny,
        readMessageHistory: PermValue.Allow,
        embedLinks: PermValue.Allow,
        useExternalEmojis: PermValue.Allow);

    /// <summary>归档后提交人不可见（仅管理组可见）。</summary>
    private static readonly OverwritePermissions CreatorHiddenPermissions = new(
        viewChannel: PermValue.Deny);

    private readonly ILogger _logger;
    private readonly TicketManager _ticketManager;
    private readonly TicketCrawler _crawler;
    private readonly TicketArchiveService _archiveService;
    private readonly TicketDeletionQueue _deletions;
    private readonly AsyncRetryPolicy _retryPolicy;

    /// <summary>审计日志：通过/拒绝/关闭/归档等写操作只进 Audit.log。</summary>
    private readonly ILogger _audit;

    public TicketWorkflowService(
        ILogger logger,
        TicketManager ticketManager,
        TicketCrawler crawler,
        TicketArchiveService archiveService,
        TicketDeletionQueue deletions)
    {
        _logger = logger.ForContext("TicketService.TicketWorkflowService");
        _audit = logger.ForContext("Audit.TicketWorkflow");
        _ticketManager = ticketManager;
        _crawler = crawler;
        _archiveService = archiveService;
        _deletions = deletions;
        _retryPolicy = DiscordRetryPolicy.HttpRetryPolicyConfig(_logger);
    }

    /// <summary>工单频道名。频道名同时作为建频道失败时的幂等键。</summary>
    public static string GetChannelName(int ticketId) => $"ticket-{ticketId:D5}";

    /// <summary>
    /// 审核通过：建频道（或接管同名残留频道）→ 迁移状态 → 开场消息 → 刷新状态卡片与审核卡片 → 私信提交人。
    /// </summary>
    public async Task<WorkflowResult> ApproveAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket,
        TicketActor approver,
        bool isAutoApprove = false)
    {
        if (ticket.GuildId != guild.Id)
        {
            return WorkflowResult.Fail("工单不属于本服务器");
        }

        if (ticket.Status != TicketStatus.Pending)
        {
            return WorkflowResult.Fail($"工单当前状态为 {ticket.Status}，无法通过");
        }

        var channel = await GetOrCreateChannelAsync(guild, config, ticket);
        if (channel is null)
        {
            return WorkflowResult.Fail("创建工单频道失败（请检查分组配置与 Bot 权限）");
        }

        // 先写状态：即使后续消息全部失败，工单也已进入 Open 且绑定了频道
        ticket.ChannelId = channel.Id;
        ticket.ApproveInfo = new ApproveInfo(approver.Id, approver.DisplayName, approver.UserName, DateTimeOffset.UtcNow);
        ticket.IsAutoApprove = isAutoApprove;
        await _ticketManager.SaveAsync(ticket);

        _audit.Info("工单已通过: {Ticket} 审批人={ApproverId} 自动={IsAuto} 频道={ChannelId}",
            ticket, approver.Id, isAutoApprove, channel.Id);

        await TrySendStartMessageAsync(channel, ticket, isAutoApprove ? AutoApproveApology : null);
        await TryUpdateStatusCardAsync(guild, ticket);
        await TryUpdateAuditCardAsync(guild, ticket, ComponentTemplate.ApprovedAuditMessage.ToComponent(
            ticket.Id,
            ticket.TicketCreator.UserId,
            TicketCardService.BuildAuditContent(ticket),
            approver.Id,
            ticket.AutoApproveTime,
            ticket.LLMSuggestion,
            isAutoApprove));
        await TryNotifyCreatorAsync(guild, ticket, BuildApproveEmbed(guild, ticket, channel.Id));

        return WorkflowResult.Ok($"已通过 **{ticket}**，频道：<#{channel.Id}>");
    }

    /// <summary>
    /// 拒绝：不建频道，只迁移状态并刷新两张卡片，同时私信提交人拒绝理由。
    /// </summary>
    public async Task<WorkflowResult> RejectAsync(
        SocketGuild guild,
        TicketData ticket,
        TicketActor actor,
        string reason)
    {
        if (ticket.GuildId != guild.Id)
        {
            return WorkflowResult.Fail("工单不属于本服务器");
        }

        if (ticket.Status != TicketStatus.Pending)
        {
            return WorkflowResult.Fail($"工单当前状态为 {ticket.Status}，无法拒绝");
        }

        reason = reason.Trim();
        if (reason.Length == 0)
        {
            return WorkflowResult.Fail("拒绝原因不能为空");
        }

        ticket.RejectInfo = new RejectInfo(actor.Id, actor.DisplayName, actor.UserName, DateTimeOffset.UtcNow, reason);
        await _ticketManager.SaveAsync(ticket);

        _audit.Info("工单已拒绝: {Ticket} 操作人={ActorId} 原因长度={ReasonLength}",
            ticket, actor.Id, reason.Length);

        await TryUpdateStatusCardAsync(guild, ticket);
        await TryUpdateAuditCardAsync(guild, ticket, ComponentTemplate.RejectedAuditMessage.ToComponent(
            ticket.Id,
            ticket.TicketCreator.UserId,
            TicketCardService.BuildAuditContent(ticket),
            actor.Id,
            reason,
            ticket.LLMSuggestion));
        await TryNotifyCreatorAsync(guild, ticket, BuildRejectEmbed(guild, ticket, reason));

        return WorkflowResult.Ok($"已拒绝 **{ticket}**");
    }

    /// <summary>
    /// 关闭工单：迁移状态 → 提交人变只读 → 频道内通告 → 刷新状态卡片 → 私信提交人。
    /// </summary>
    /// <param name="reason">可选关闭说明，只记录在频道内，不进入 <see cref="CloseInfo"/>。</param>
    public async Task<WorkflowResult> CloseAsync(
        SocketGuild guild,
        TicketData ticket,
        TicketActor actor,
        string? reason = null)
    {
        if (ticket.GuildId != guild.Id)
        {
            return WorkflowResult.Fail("工单不属于本服务器");
        }

        if (ticket.Status != TicketStatus.Open)
        {
            return WorkflowResult.Fail($"工单当前状态为 {ticket.Status}，无法关闭");
        }

        if (ticket.ChannelId == 0 || guild.GetChannel(ticket.ChannelId) is not ITextChannel channel)
        {
            return WorkflowResult.Fail("工单频道不存在");
        }

        ticket.CloseInfo = new CloseInfo(actor.Id, actor.DisplayName, actor.UserName, DateTimeOffset.UtcNow);
        await _ticketManager.SaveAsync(ticket);

        _audit.Info("工单已关闭: {Ticket} 操作人={ActorId}", ticket, actor.Id);

        await TrySetCreatorOverwriteAsync(guild, channel, ticket, CreatorReadOnlyPermissions);

        await TrySendChannelNoticeAsync(channel, BuildCloseNoticeEmbed(ticket, actor, reason));

        await TryUpdateStatusCardAsync(guild, ticket);

        // 提交人自己关的就不必再私信
        if (actor.Id != ticket.TicketCreator.UserId)
        {
            await TryNotifyCreatorAsync(guild, ticket, BuildCloseEmbed(guild, ticket, actor));
        }

        return WorkflowResult.Ok($"已关闭 **{ticket}**");
    }

    /// <summary>
    /// 归档工单：要求状态为 Closed，归档后提交人不可见。
    /// <paramref name="deleteChannel"/> 为 <c>true</c>（默认）时，按
    /// <see cref="GuildConfig.ArchiveDeleteCountdownSeconds"/> 倒计时后删除频道；
    /// 为 <c>false</c> 时保留频道（仅管理组可见）。
    /// </summary>
    public async Task<WorkflowResult> ArchiveAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket,
        TicketActor actor,
        bool deleteChannel = true)
    {
        if (ticket.GuildId != guild.Id)
        {
            return WorkflowResult.Fail("工单不属于本服务器");
        }

        if (ticket.Status != TicketStatus.Closed)
        {
            return WorkflowResult.Fail($"工单当前状态为 {ticket.Status}，需先关闭才能归档");
        }

        ticket.ArchiveInfo = new ArchiveInfo(actor.Id, actor.DisplayName, actor.UserName, DateTimeOffset.UtcNow);

        // 先抓对话与原件（此时目录还在活跃区），失败则中止归档，频道仍可重试
        if (ticket.ChannelId != 0 && guild.GetChannel(ticket.ChannelId) is ITextChannel source)
        {
            try
            {
                await _crawler.CrawlAsync(source, ticket);
                await _ticketManager.SaveAsync(ticket);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "归档前抓取对话失败: {Ticket}", ticket);
                ticket.ArchiveInfo = null;
                return WorkflowResult.Fail("抓取工单对话失败，请稍后重试");
            }
        }

        await _ticketManager.ArchiveAsync(ticket);

        _audit.Info("工单已归档: {Ticket} 操作人={ActorId} ChannelId={ChannelId} 消息数={Messages} 删除频道={DeleteChannel}",
            ticket, actor.Id, ticket.ChannelId, ticket.TicketMessages?.Count ?? 0, deleteChannel);

        // 打包失败不阻塞归档：用户可在 /管理 获取归档 时重新触发打包
        try
        {
            await _archiveService.EnsureArchiveZipAsync(ticket);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "归档打包失败，稍后可重试: {Ticket}", ticket);
        }

        // 先通告再收权限，保证频道里能看到最后一条
        if (ticket.ChannelId != 0 && guild.GetChannel(ticket.ChannelId) is ITextChannel channel)
        {
            await TrySetCreatorOverwriteAsync(guild, channel, ticket, CreatorHiddenPermissions);

            if (deleteChannel)
            {
                var seconds = Math.Clamp(
                    config.ArchiveDeleteCountdownSeconds,
                    TicketConfigLimits.ArchiveCountdownInclusiveMin,
                    TicketConfigLimits.ArchiveCountdownInclusiveMax);
                var deleteAt = DateTimeOffset.UtcNow.AddSeconds(seconds);

                await TrySendChannelNoticeAsync(channel, BuildArchiveNoticeEmbed(ticket, actor, deleteAt));

                // 落盘队列：进程重启后由维护任务继续删除
                _deletions.Enqueue(guild.Id, channel.Id, ticket.Id, deleteAt);
                try
                {
                    await _deletions.SaveAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "保存删除队列失败（重启后将不会继续删除该频道）: {Ticket}", ticket);
                }
            }
            else
            {
                await TrySendChannelNoticeAsync(channel, BuildArchiveNoticeEmbed(ticket, actor, deleteAt: null));
            }
        }

        await TryUpdateStatusCardAsync(guild, ticket);

        return WorkflowResult.Ok($"已归档 **{ticket}**");
    }

    /// <summary>
    /// 取回工单频道：优先复用分类下同名残留频道（上次建频道后崩溃的中间态），否则新建。
    /// </summary>
    private async Task<ITextChannel?> GetOrCreateChannelAsync(
        SocketGuild guild,
        GuildConfig config,
        TicketData ticket)
    {
        if (guild.GetChannel(config.TicketCategory) is not ICategoryChannel)
        {
            _logger.Warning("工单频道分组不存在: {Ticket} CategoryId={CategoryId}", ticket, config.TicketCategory);
            return null;
        }

        var name = GetChannelName(ticket.Id);

        var existing = guild.TextChannels
            .FirstOrDefault(c => c.CategoryId == config.TicketCategory && c.Name == name);
        if (existing is not null)
        {
            _logger.Warning("发现同名工单频道，直接接管: {Ticket} ChannelId={ChannelId}", ticket, existing.Id);
            return existing;
        }

        try
        {
            var channel = await _retryPolicy.ExecuteAsync(() => guild.CreateTextChannelAsync(name, props =>
            {
                props.CategoryId = config.TicketCategory;
                props.Topic = $"工单 {ticket} | 提交人 {ticket.TicketCreator.DisplayName}";
                props.PermissionOverwrites = BuildOverwrites(guild, config, ticket);
                // 年龄限制频道：Discord 禁止把 NSFW 频道的消息转发到普通频道，
                // 工单里需要转发敏感内容时，非 NSFW 频道会直接失败
                props.IsNsfw = true;
            }));

            _logger.Info("工单频道已创建: {Ticket} ChannelId={ChannelId} ChannelName={ChannelName}",
                ticket, channel.Id, name);
            return channel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "创建工单频道失败: {Ticket} ChannelName={ChannelName}", ticket, name);
            return null;
        }
    }

    private static List<Overwrite> BuildOverwrites(SocketGuild guild, GuildConfig config, TicketData ticket)
    {
        var overwrites = new List<Overwrite>
        {
            // 分类可能已经收紧了默认权限；这里显式再拒绝一次，保证默认成员进不来
            new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
            new(ticket.TicketCreator.UserId, PermissionTarget.User, CreatorPermissions),
            new(guild.CurrentUser.Id, PermissionTarget.User, BotPermissions),
        };

        foreach (var roleId in config.AllowManageTicketAdminRoleIds)
        {
            // 角色可能已被删除，传不存在的 ID 会让建频道请求 400
            if (guild.GetRole(roleId) is not null)
            {
                overwrites.Add(new Overwrite(roleId, PermissionTarget.Role, AdminPermissions));
            }
        }

        return overwrites;
    }

    private async Task TrySendStartMessageAsync(ITextChannel channel, TicketData ticket, string? apology = null)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(() => channel.SendMessageAsync(
                components: ComponentTemplate.StartMessage.ToComponent(
                    ticket.Id, ticket.TicketCreator.UserId, ticket.Content)));

            // 组件里的提及不会触发通知，补一条真正的提及
            var notice = $"<@{ticket.TicketCreator.UserId}> 工单频道已创建，请在此与管理组沟通。";

            // 自动通过的工单没有管理员当场审核，补一句安抚，避免提交人以为被无视
            if (ticket.IsAutoApprove && !string.IsNullOrWhiteSpace(apology))
            {
                notice += $"\n\n{apology}";
            }

            await _retryPolicy.ExecuteAsync(() => channel.SendMessageAsync(notice));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "发送工单频道开场消息失败: {Ticket} ChannelId={ChannelId}", ticket, channel.Id);
        }
    }

    private async Task TrySendChannelNoticeAsync(ITextChannel channel, Embed embed)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(() => channel.SendMessageAsync(embed: embed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "发送频道通告失败: ChannelId={ChannelId}", channel.Id);
        }
    }

    private async Task TrySetCreatorOverwriteAsync(
        SocketGuild guild,
        ITextChannel channel,
        TicketData ticket,
        OverwritePermissions permissions)
    {
        try
        {
            // 未开启 GuildMembers 意图时缓存里没有成员，必须允许 REST 回源
            var creator = await ((IGuild)guild).GetUserAsync(ticket.TicketCreator.UserId);
            if (creator is null)
            {
                _logger.Warning("提交人不在服务器内，无法调整频道权限: {Ticket}", ticket);
                return;
            }

            await _retryPolicy.ExecuteAsync(() => channel.AddPermissionOverwriteAsync(creator, permissions));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "调整提交人频道权限失败: {Ticket} ChannelId={ChannelId}", ticket, channel.Id);
        }
    }

    private async Task TryUpdateStatusCardAsync(SocketGuild guild, TicketData ticket)
    {
        if (ticket.StatusChannelId == 0 || ticket.StatusMessageId == 0)
        {
            _logger.Warning("状态卡片坐标缺失，跳过刷新: {Ticket}", ticket);
            return;
        }

        if (guild.GetChannel(ticket.StatusChannelId) is not ITextChannel channel)
        {
            _logger.Warning("状态消息所在频道已不存在: {Ticket} ChannelId={ChannelId}", ticket, ticket.StatusChannelId);
            return;
        }

        try
        {
            if (await channel.GetMessageAsync(ticket.StatusMessageId) is not IUserMessage message)
            {
                _logger.Warning("状态消息已不存在: {Ticket} MessageId={MessageId}", ticket, ticket.StatusMessageId);
                return;
            }

            var embed = ComponentTemplate.TicketStatusMessage.ToEmbed(ticket);
            await _retryPolicy.ExecuteAsync(() => message.ModifyAsync(props => props.Embed = embed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "刷新状态卡片失败: {Ticket}", ticket);
        }
    }

    private async Task TryUpdateAuditCardAsync(SocketGuild guild, TicketData ticket, MessageComponent components)
    {
        if (ticket.AuditChannelId == 0 || ticket.AuditMessageId == 0)
        {
            _logger.Warning("审核卡片坐标缺失，跳过刷新: {Ticket}", ticket);
            return;
        }

        if (guild.GetChannel(ticket.AuditChannelId) is not ITextChannel channel)
        {
            _logger.Warning("审核消息所在频道已不存在: {Ticket} ChannelId={ChannelId}", ticket, ticket.AuditChannelId);
            return;
        }

        try
        {
            if (await channel.GetMessageAsync(ticket.AuditMessageId) is not IUserMessage message)
            {
                _logger.Warning("审核消息已不存在: {Ticket} MessageId={MessageId}", ticket, ticket.AuditMessageId);
                return;
            }

            await _retryPolicy.ExecuteAsync(() => message.ModifyAsync(props => props.Components = components));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "刷新审核卡片失败: {Ticket}", ticket);
        }
    }

    private async Task TryNotifyCreatorAsync(SocketGuild guild, TicketData ticket, Embed embed)
    {
        try
        {
            // 未开启 GuildMembers 意图时缓存里没有成员，必须允许 REST 回源
            var user = await ((IGuild)guild).GetUserAsync(ticket.TicketCreator.UserId);
            if (user is null)
            {
                _logger.Warning("提交人不在服务器内，跳过私信: {Ticket} UserId={UserId}", ticket, ticket.TicketCreator.UserId);
                return;
            }

            await _retryPolicy.ExecuteAsync(() => user.SendMessageAsync(embed: embed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 对方关闭了私信时 Discord 返回 403，重试策略不处理 403，这里兜底
            _logger.Warning(ex, "私信提交人失败（可能已关闭私信）: {Ticket} UserId={UserId}",
                ticket, ticket.TicketCreator.UserId);
        }
    }

    /// <summary>
    /// 通过私信：绿色 embed + 频道跳转链接。
    /// <para>
    /// 不展示具体审批人：私信面向提交人，暴露管理员身份容易让投诉转向个人。
    /// </para>
    /// </summary>
    private static Embed BuildApproveEmbed(SocketGuild guild, TicketData ticket, ulong channelId)
    {
        var auto = ticket.IsAutoApprove ? "\n-# 本次为超时自动通过。" : string.Empty;

        return new EmbedBuilder()
            .WithColor(Color.Green)
            .WithTitle("✅ 工单审核通过")
            .WithDescription($"""
                你的工单已通过审核，管理组会在专属频道中与你沟通。

                ### 🎫 {ticket}
                [进入工单频道](https://discord.com/channels/{guild.Id}/{channelId}){auto}
                """)
            .AddField("类型", ticket.TicketType.Describe(), inline: true)
            .AddField("审批", ticket.IsAutoApprove ? "系统自动通过" : "管理组", inline: true)
            .WithFooter(guild.Name)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    /// <summary>拒绝私信：红色 embed + 拒绝原因（字段上限 1024，超出截断）。</summary>
    private static Embed BuildRejectEmbed(SocketGuild guild, TicketData ticket, string reason)
    {
        return new EmbedBuilder()
            .WithColor(Color.Red)
            .WithTitle("⛔️ 工单未通过审核")
            .WithDescription($"### 🎫 {ticket}\n很抱歉，本次提交未通过审核。")
            .AddField("原因", reason.Slice(1020), inline: false)
            .WithFooter($"{guild.Name} · 如对结果有异议，可联系管理组")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    /// <summary>
    /// 关闭私信：灰色 embed。
    /// <para>
    /// 同样不展示具体操作人，只说明处理方。
    /// </para>
    /// </summary>
    private static Embed BuildCloseEmbed(SocketGuild guild, TicketData ticket, TicketActor actor)
    {
        return new EmbedBuilder()
            .WithColor(Color.LightGrey)
            .WithTitle("🔒 工单已关闭")
            .WithDescription($"### 🎫 {ticket}\n工单已关闭。如果你的问题仍未解决，可以重新提交一张工单。")
            .AddField("关闭人", actor.Id == 0 ? "系统" : "管理组", inline: true)
            .WithFooter(guild.Name)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    /// <summary>关闭通告（发在工单频道内）：灰色 embed，可选附关闭说明。</summary>
    private static Embed BuildCloseNoticeEmbed(TicketData ticket, TicketActor actor, string? reason)
    {
        var builder = new EmbedBuilder()
            .WithColor(Color.LightGrey)
            .WithTitle("📂 工单已关闭")
            .WithDescription($"### 🎫 {ticket}\n工单已由 <@{actor.Id}> 关闭，频道已设置为只读。")
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AddField("关闭说明", reason.Slice(1020), inline: false);
        }

        return builder.Build();
    }

    /// <summary>
    /// 归档通告（发在工单频道内）：深灰 embed。
    /// <paramref name="deleteAt"/> 非空时展示删除倒计时，否则说明频道已对提交人隐藏。
    /// </summary>
    private static Embed BuildArchiveNoticeEmbed(TicketData ticket, TicketActor actor, DateTimeOffset? deleteAt)
    {
        var detail = deleteAt is null
            ? "频道已对提交人隐藏。"
            : $"频道将在 <t:{deleteAt.Value.ToUnixTimeSeconds()}:R> 后删除。";

        return new EmbedBuilder()
            .WithColor(Color.DarkerGrey)
            .WithTitle("📁 工单已归档")
            .WithDescription($"### 🎫 {ticket}\n工单已由 <@{actor.Id}> 归档，{detail}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }
}
