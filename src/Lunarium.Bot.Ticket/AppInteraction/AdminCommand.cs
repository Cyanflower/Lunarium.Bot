using Discord.Interactions;
using Discord;
using Discord.WebSocket;

using Lunarium.Logging;
using Lunarium.Bot.Framework.Configuration;
using Lunarium.Bot.Framework.AuthRuleEngine;
using Lunarium.Bot.Ticket.Model;
using Lunarium.Bot.Framework.Interaction;
using Lunarium.Bot.Ticket.InteractionSession;
using Lunarium.Bot.Framework;
using Lunarium.Common.UID;
using Lunarium.Bot.Ticket.Component;
using Lunarium.Bot.Ticket.TicketService;

namespace Lunarium.Bot.Ticket.AppInteraction;


[Group("管理", "管理命令")]
public sealed class AdminCommand : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ILogger _logger;
    private readonly ConfigLoader<TicketConfig> _configLoader;
    private readonly TicketManager _ticketManager;
    private readonly TicketArchiveService _archiveService;
    private readonly TicketCooldownStore _cooldowns;

    // Conmmand Name
    private const string PublishEntryPane = "发布入口面板";
    private const string GetArchive = "获取归档";
    private const string GetDatabase = "获取数据库原件";
    private const string GrantTempPermission = "临时许可";

    // Custom Id

    public AdminCommand(
        ILogger logger,
        ConfigLoader<TicketConfig> configLoader,
        TicketManager ticketManager,
        TicketArchiveService archiveService,
        TicketCooldownStore cooldowns)
    {
        _logger = logger.ForContext("AppInteraction.AdminCommand");
        _configLoader = configLoader;
        _ticketManager = ticketManager;
        _archiveService = archiveService;
        _cooldowns = cooldowns;
    }

    [SlashCommand(PublishEntryPane, "Bot 将把投诉入口面板发布到本服务器配置的入口面板频道")]
    public async Task PublishEntryPanelAsync()
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppCommand: {Command} 收到管理命令请求: GuildId={GuildId}, ChannelId={ChannelId}, UserId={UserId}", PublishEntryPane, Context.Guild?.Id, Context.Channel?.Id, Context.User.Id);

            await DeferAsync();

            var guild = Context.Guild;

            if (guild is null)
            {
                track.Warning("AppCommand: {Command} 解析服务器信息失败: GuildId={GuildId}", PublishEntryPane, guild?.Id);
                await FollowupAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"));
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
            {
                track.Warning("AppCommand: {Command} 当前配置中并无本服务器配置: GuildId={GuildId}", PublishEntryPane, guild.Id);
                await FollowupAsync(components: MessagePrefab.ConfigFail("当前配置中并无本服务器配置"));
                return;
            }
            
            track.Info("AppCommand: {Command} 读取服务器配置成功: GuildId={GuildId}, EntryPaneChannelId={EntryPaneChannelId}", PublishEntryPane, guild.Id, config.EntryPaneChannel);


            var validationResult = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
                new BaseRuleSet.RequireAdminRole(_configLoader.Value.DeployGuildConfig[guild.Id].AllowManageTicketAdminRoleIds.ToHashSet()));

            if (validationResult is not null)
            {
                track.Warning("AppCommand: {Command} 权限校验未通过: GuildId={GuildId}, UserId={UserId}", PublishEntryPane, guild.Id, Context.User.Id);
                await FollowupAsync(components: validationResult);
                return;
            }

            track.Info("AppCommand: {Command} 权限校验通过: GuildId={GuildId}, UserId={UserId}", PublishEntryPane, guild.Id, Context.User.Id);

            var channel = guild.GetChannel(config.EntryPaneChannel);

            if (channel is null)
            {
                track.Warning("AppCommand: {Command} 入口面板频道不存在: GuildId={GuildId}, EntryPaneChannelId={EntryPaneChannelId}", PublishEntryPane, guild.Id, config.EntryPaneChannel);
                await FollowupAsync(components: MessagePrefab.ConfigFail("本服务器配置中的入口面板频道不存在"));
                return;
            }
            if (channel is not ITextChannel textChannel)
            {
                track.Warning("AppCommand: {Command} 入口面板频道类型不支持: GuildId={GuildId}, EntryPaneChannelId={EntryPaneChannelId}, ChannelType={ChannelType}", PublishEntryPane, guild.Id, config.EntryPaneChannel, channel.GetType().Name);
                await FollowupAsync(components: MessagePrefab.ConfigFail("本服务器配置中的入口面板频道不是一个文字频道"));
                return;
            }

            track.Info("AppCommand: {Command} 开始发布入口面板: GuildId={GuildId}, ChannelId={ChannelId}", PublishEntryPane, guild.Id, textChannel.Id);

            var panel = ComponentTemplate.DashboardMessage.ToComponent();

            // 幂等：已发布过且消息还在，则原地更新，避免同一频道出现多份入口面板
            if (config.EntryPaneMessageId != 0
                && await textChannel.GetMessageAsync(config.EntryPaneMessageId) is IUserMessage existing)
            {
                await existing.ModifyAsync(props => props.Components = panel);

                track.Info("AppCommand: {Command} 入口面板已更新: GuildId={GuildId}, ChannelId={ChannelId}, MessageId={MessageId}, OperatorId={OperatorId}",
                    PublishEntryPane, guild.Id, textChannel.Id, existing.Id, Context.User.Id);

                await FollowupAsync(components: MessagePrefab.OperateSuccess($"入口面板已原地更新: {existing.GetJumpUrl()}"));
                return;
            }

            var message = await textChannel.SendMessageAsync(components: panel);

            track.Info("AppCommand: {Command} 入口面板发布成功: GuildId={GuildId}, ChannelId={ChannelId}, MessageId={MessageId}, OperatorId={OperatorId}",
                PublishEntryPane, guild.Id, textChannel.Id, message.Id, Context.User.Id);

            // 回写消息坐标，供后续发布判断是否需要新建
            var errors = _configLoader.TryUpdate(root =>
            {
                if (root.DeployGuildConfig.TryGetValue(guild.Id, out var guildConfig))
                {
                    guildConfig.EntryPaneMessageId = message.Id;
                }
            });

            if (errors.Count > 0)
            {
                track.Warning("AppCommand: {Command} 入口面板消息坐标回写失败: {Errors}", PublishEntryPane, string.Join(" | ", errors));
            }

            await FollowupAsync(components: MessagePrefab.OperateSuccess($"入口消息已成功发送: {message.GetJumpUrl()}"));
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppCommand: {Command} 执行失败: GuildId={GuildId}, UserId={UserId}", PublishEntryPane, Context.Guild?.Id, Context.User.Id);
        }
    }

    [SlashCommand(GetArchive, "获取指定工单的归档 zip（HTML + 数据 + 附件原件）")]
    public Task GetArchiveAsync([Summary("工单号", "例如：12")] int ticketId)
        => SendTicketZipAsync(ticketId, GetArchive, archive: true);

    [SlashCommand(GetDatabase, "获取指定工单的数据库原件 zip（数据 + 附件原件）")]
    public Task GetDatabaseAsync([Summary("工单号", "例如：12")] int ticketId)
        => SendTicketZipAsync(ticketId, GetDatabase, archive: false);

    /// <summary>获取归档包 / 数据库原件包的共同流程。</summary>
    private async Task SendTicketZipAsync(int ticketId, string command, bool archive)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppCommand: {Command} 收到请求: TicketId={TicketId}, GuildId={GuildId}, UserId={UserId}",
                command, ticketId, Context.Guild?.Id, Context.User.Id);

            await DeferAsync(ephemeral: true);

            if (Context.Guild is not SocketGuild guild)
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"), ephemeral: true);
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
            {
                await FollowupAsync(components: MessagePrefab.ConfigFail("当前配置中并无本服务器配置"), ephemeral: true);
                return;
            }

            var validationResult = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
                new BaseRuleSet.RequireAdminRole(config.AllowManageTicketAdminRoleIds.ToHashSet()));

            if (validationResult is not null)
            {
                await FollowupAsync(components: validationResult, ephemeral: true);
                return;
            }

            var ticket = _ticketManager.LoadAny(ticketId);
            if (ticket is null)
            {
                await FollowupAsync(components: MessagePrefab.AnyFail($"找不到工单 {TicketData.FormatId(ticketId)}", "### 工单不存在"), ephemeral: true);
                return;
            }

            if (ticket.GuildId != guild.Id)
            {
                await FollowupAsync(components: MessagePrefab.AuthFail("工单不属于本服务器"), ephemeral: true);
                return;
            }

            if (ticket.ArchiveInfo is null)
            {
                await FollowupAsync(components: MessagePrefab.AnyNotifier($"工单 {TicketData.FormatId(ticketId)} 尚未归档", "### 无法获取"), ephemeral: true);
                return;
            }

            var zipPath = archive
                ? await _archiveService.EnsureArchiveZipAsync(ticket)
                : await _archiveService.EnsureDatabaseZipAsync(ticket);
            var size = new FileInfo(zipPath).Length;

            // Discord 上传上限受服务器加成影响，保守地先拦 24 MB，避免请求直接失败
            if (size > 24L * 1024 * 1024)
            {
                await FollowupAsync(
                    components: MessagePrefab.AnyFail($"文件 {size / 1024d / 1024d:0.#} MB 超过上传上限，请联系维护者从服务器取回", "### 文件过大"),
                    ephemeral: true);
                return;
            }

            await FollowupWithFileAsync(
                zipPath,
                fileName: $"ticket-{ticketId:D5}{(archive ? "" : "-database")}.zip",
                text: archive
                    ? $"📁 工单归档（HTML + 数据 + 附件）\n-# {size / 1024d:0.#} KB"
                    : $"📦 工单数据库原件（数据 + 附件）\n-# {size / 1024d:0.#} KB",
                ephemeral: true);

            track.Info("AppCommand: {Command} 已发送: TicketId={TicketId}, Size={Size}", command, ticketId, size);
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppCommand: {Command} 执行失败: TicketId={TicketId}, GuildId={GuildId}, UserId={UserId}",
                command, ticketId, Context.Guild?.Id, Context.User.Id);

            try
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("生成或发送文件失败，请联系维护者"), ephemeral: true);
            }
            catch (Exception notifyEx)
            {
                track.Warning(notifyEx, "回报获取文件失败时再次失败");
            }
        }
    }

    [SlashCommand(GrantTempPermission, "为指定用户添加一次性提交冷却豁免（下一次提交自动消耗）")]
    public async Task GrantTempPermissionAsync(
        [Summary("用户", "获得豁免的用户")] SocketGuildUser user,
        [Summary("小时", "有效期 1–72 小时")] int hours)
    {
        using var track = _logger.BeginTrack(NanoId.GenerateUtf8(10));
        try
        {
            track.Info("AppCommand: {Command} 收到临时许可请求: GuildId={GuildId}, TargetId={TargetId}, Hours={Hours}, UserId={UserId}",
                GrantTempPermission, Context.Guild?.Id, user.Id, hours, Context.User.Id);

            await DeferAsync(ephemeral: true);

            if (Context.Guild is not SocketGuild guild)
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("解析服务器信息失败"), ephemeral: true);
                return;
            }

            if (!_configLoader.Value.DeployGuildConfig.TryGetValue(guild.Id, out var config))
            {
                await FollowupAsync(components: MessagePrefab.ConfigFail("当前配置中并无本服务器配置"), ephemeral: true);
                return;
            }

            var validationResult = await RuleEngine.ValidateAsync(
                Context,
                new BaseRuleSet.MaintainerBypass(_configLoader.Value.DeployerConfig.DeployerIds.ToHashSet()),
                new BaseRuleSet.RequireAdminRole(config.AllowManageTicketAdminRoleIds.ToHashSet()));

            if (validationResult is not null)
            {
                await FollowupAsync(components: validationResult, ephemeral: true);
                return;
            }

            if (!TicketConfigLimits.IsValidTempPermissionHours(hours))
            {
                await FollowupAsync(
                    components: MessagePrefab.AnyFail(
                        $"有效期必须在 {TicketConfigLimits.TempPermissionHoursInclusiveMin}–{TicketConfigLimits.TempPermissionHoursInclusiveMax} 小时之间",
                        "### 参数无效"),
                    ephemeral: true);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (!_cooldowns.GrantTempPermission(guild.Id, user.Id, TimeSpan.FromHours(hours), now, out var expireAt))
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("授予临时许可失败"), ephemeral: true);
                return;
            }

            try
            {
                await _cooldowns.SaveAsync();
            }
            catch (Exception ex)
            {
                track.Error(ex, "保存临时许可失败");
                await FollowupAsync(components: MessagePrefab.FunctionFail("临时许可保存失败，请联系维护者"), ephemeral: true);
                return;
            }

            var remainingLine = string.Empty;
            if (config.EnableTicketCd
                && _cooldowns.GetRemaining(guild.Id, user.Id, TimeSpan.FromHours(config.TicketCd), now) is { } remaining)
            {
                remainingLine = $"\n-# 该用户当前冷却剩余约 {Math.Ceiling(remaining.TotalMinutes)} 分钟，下一次提交将消耗本次豁免。";
            }

            track.Info("AppCommand: {Command} 临时许可已授予: GuildId={GuildId}, TargetId={TargetId}, ExpireAt={ExpireAt}",
                GrantTempPermission, guild.Id, user.Id, expireAt);

            await FollowupAsync(
                components: MessagePrefab.OperateSuccess(
                    $"已为 <@{user.Id}> 添加一次性提交冷却豁免。\n有效期至 <t:{expireAt.ToUnixTimeSeconds()}:F>（<t:{expireAt.ToUnixTimeSeconds()}:R>）。{remainingLine}"),
                ephemeral: true);
        }
        catch (Exception ex)
        {
            track.Error(ex, "AppCommand: {Command} 执行失败: GuildId={GuildId}, UserId={UserId}",
                GrantTempPermission, Context.Guild?.Id, Context.User.Id);

            try
            {
                await FollowupAsync(components: MessagePrefab.FunctionFail("授予临时许可时发生内部错误"), ephemeral: true);
            }
            catch (Exception notifyEx)
            {
                track.Warning(notifyEx, "回报临时许可失败时再次失败");
            }
        }
    }
}