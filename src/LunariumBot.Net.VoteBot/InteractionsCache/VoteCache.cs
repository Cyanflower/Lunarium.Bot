using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.VoteBot.Models;
using LunariumBot.Net.VoteBot.VoteServices;

namespace LunariumBot.Net.VoteBot.InteractionsCache;

/// <summary>
/// 投票交互缓存类。
/// 负责维护用户投票过程中的中间状态（如已选候选人、各职位的当前页码等），
/// 并根据当前状态渲染 Discord 消息组件。
/// </summary>
public class VoteCache : BaseInteractionsCache<VoteData>
{
    // ==== 交互 ID 前缀常量 ====
    public const string ActionVoteResourcesAdmin = "Vote:ResourcesAdmin";
    public const string ActionVoteChatsAdmin = "Vote:ChatsAdmin";
    public const string ActionVotePress = "Vote:Press";
    public const string ActionVoteCompetitionAdmin = "Vote:CompetitionAdmin";
    public const string ActionConfirm = "Vote:Confirm";
    public const string ActionCancel = "Vote:Cancel";

    // ==== 投票人数限制常量 ====
    public const int ResourcesAdminLimit = 8;     // 资源区管理限投 8 人
    public const int ChatsAdminLimit = 3;         // 闲聊区管理限投 3 人
    public const int PressLimit = 2;              // 记者限投 2 人
    public const int CompetitionAdminLimit = 3;   // 赛事委员限投 3 人

    /// <summary>
    /// 获取当前缓存的投票数据。
    /// </summary>
    public VoteData Data => _data;
    private bool _isCancel = false;
    private bool _isComplete = false;

    /// <summary>
    /// 交互是否已结束（已完成或已取消）。
    /// </summary>
    private bool IsFinished => _isCancel || _isComplete;

    // ==== 各职位独立的分页索引 ====
    private int currentResourcesAdminPageIndex = 0;
    private int currentChatsAdminPageIndex = 0;
    private int currentPressPageIndex = 0;
    private int currentCompetitionAdminPageIndex = 0;

    /// <summary>
    /// 初始化投票缓存实例。
    /// </summary>
    /// <param name="data">初始投票数据</param>
    /// <param name="expiresAt">缓存过期时间</param>
    /// <param name="id">缓存唯一 ID</param>
    public VoteCache(VoteData data, DateTimeOffset expiresAt, string id)
        : base(data, expiresAt, id) { }

    /// <summary>
    /// 渲染投票面板的消息组件。
    /// </summary>
    /// <returns>构建完成的 MessageComponent</returns>
    public override MessageComponent Render()
    {
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);

            // =================================================================================
            // 1. 资源区管理 (ResourcesAdmin)
            // =================================================================================
            var resourcesAdminDesc = new StringBuilder();
            resourcesAdminDesc.AppendLine("# 提交投票");
            resourcesAdminDesc.AppendLine("## 管理组");
            resourcesAdminDesc.AppendLine("### 资源区管理");

            resourcesAdminDesc.AppendLine($"您还可投 **{ResourcesAdminLimit - _data.ResourcesAdmin.Count}** 票, 当前已选择:");

            // 只有在页数 > 1 时才显示页码
            int resourcesAdminTotalPages = VoteStaticData.GetTotalPages(Choice.ResourcesAdmin);
            if (resourcesAdminTotalPages > 1)
            {
                resourcesAdminDesc.AppendLine($"> **第 {currentResourcesAdminPageIndex + 1} / {resourcesAdminTotalPages} 页**");
            }

            // 显示当前已选中的人员列表
            if (_data.ResourcesAdmin.Count > 0)
            {
                foreach (var id in _data.ResourcesAdmin)
                {
                    resourcesAdminDesc.AppendLine($"> - <@{id}>");
                }
            }
            else
            {
                resourcesAdminDesc.AppendLine("> *(暂未选择)*");
            }
            container.WithTextDisplay(resourcesAdminDesc.ToString());

            // 获取候选人数据切片（处理分页）
            List<SubmitterData> resourcesAdminSubmitter;
            if (VoteStaticData.ResourcesAdmin.Count > 25)
            {
                resourcesAdminSubmitter = VoteStaticData.GetPageData(Choice.ResourcesAdmin, currentResourcesAdminPageIndex).ToList();
            }
            else
            {
                resourcesAdminSubmitter = VoteStaticData.ResourcesAdmin;
            }

            // 构建下拉菜单选项
            var resourcesAdminOptions = new List<SelectMenuOptionBuilder>();
            foreach (var data in resourcesAdminSubmitter)
            {
                bool isSelected = _data.ResourcesAdmin.Contains(data.SubmitterId);
                resourcesAdminOptions.Add(new SelectMenuOptionBuilder(
                    label: data.Name,
                    value: data.SubmitterId.ToString())
                    .WithDefault(isSelected)
                );
            }

            // 防呆：如果没有候选项，就不渲染菜单，否则 Discord 会报错
            if (resourcesAdminOptions.Count > 0)
            {
                container.WithActionRow(row => row
                    .WithSelectMenu(
                        customId: $"{ActionVoteResourcesAdmin}:{CacheId}",
                        options: resourcesAdminOptions,
                        placeholder: "投票给 (资源区)...",
                        minValues: 0,
                        // 注意：maxValues 不能超过选项总数。如果当前页只有 1 人，limit 是 8，Discord 会炸。
                        maxValues: Math.Min(ResourcesAdminLimit, resourcesAdminOptions.Count),
                        disabled: IsFinished
                    )
                );
            }

            // 如果总人数超过 25，显示翻页控件
            if (VoteStaticData.ResourcesAdmin.Count > 25)
            {
                container.WithActionRow(PaginationControlsBuilder.Build(CacheId, Choice.ResourcesAdmin, currentResourcesAdminPageIndex, VoteStaticData.GetTotalPages(Choice.ResourcesAdmin)));
            }

            // =================================================================================
            // 2. 聊天区管理 (ChatsAdmin)
            // =================================================================================
            var chatsAdminDesc = new StringBuilder();
            chatsAdminDesc.AppendLine("### 聊天区管理");

            chatsAdminDesc.AppendLine($"您还可投 **{ChatsAdminLimit - _data.ChatsAdmin.Count}** 票, 当前已选择:");

            int chatsAdminTotalPages = VoteStaticData.GetTotalPages(Choice.ChatsAdmin);
            if (chatsAdminTotalPages > 1)
            {
                chatsAdminDesc.AppendLine($"> **第 {currentChatsAdminPageIndex + 1} / {chatsAdminTotalPages} 页**");
            }

            if (_data.ChatsAdmin.Count > 0)
            {
                foreach (var id in _data.ChatsAdmin)
                {
                    chatsAdminDesc.AppendLine($"> - <@{id}>");
                }
            }
            else
            {
                chatsAdminDesc.AppendLine("> *(暂未选择)*");
            }
            container.WithTextDisplay(chatsAdminDesc.ToString());

            List<SubmitterData> chatsAdminSubmitter;
            if (VoteStaticData.ChatsAdmin.Count > 25)
            {
                chatsAdminSubmitter = VoteStaticData.GetPageData(Choice.ChatsAdmin, currentChatsAdminPageIndex).ToList();
            }
            else
            {
                chatsAdminSubmitter = VoteStaticData.ChatsAdmin;
            }

            var chatsAdminOptions = new List<SelectMenuOptionBuilder>();
            foreach (var data in chatsAdminSubmitter)
            {
                bool isSelected = _data.ChatsAdmin.Contains(data.SubmitterId);
                chatsAdminOptions.Add(new SelectMenuOptionBuilder(
                    label: data.Name,
                    value: data.SubmitterId.ToString())
                    .WithDefault(isSelected)
                );
            }

            if (chatsAdminOptions.Count > 0)
            {
                container.WithActionRow(row => row
                    .WithSelectMenu(
                        customId: $"{ActionVoteChatsAdmin}:{CacheId}",
                        options: chatsAdminOptions,
                        placeholder: "投票给 (聊天区)...",
                        minValues: 0,
                        maxValues: Math.Min(ChatsAdminLimit, chatsAdminOptions.Count),
                        disabled: IsFinished
                    )
                );
            }

            if (VoteStaticData.ChatsAdmin.Count > 25)
            {
                container.WithActionRow(PaginationControlsBuilder.Build(CacheId, Choice.ChatsAdmin, currentChatsAdminPageIndex, VoteStaticData.GetTotalPages(Choice.ChatsAdmin)));
            }

            // =================================================================================
            // 3. 记者 (Press) - 属于事务组
            // =================================================================================
            var pressDesc = new StringBuilder();
            pressDesc.AppendLine("## 事务组");
            pressDesc.AppendLine("### 记者");

            int pressTotalPages = VoteStaticData.GetTotalPages(Choice.Press);

            pressDesc.AppendLine($"您还可投 **{PressLimit - _data.Press.Count}** 票, 当前已选择:");

            if (pressTotalPages > 1)
            {
                pressDesc.AppendLine($"> **第 {currentPressPageIndex + 1} / {pressTotalPages} 页**");
            }

            if (_data.Press.Count > 0)
            {
                foreach (var id in _data.Press)
                {
                    pressDesc.AppendLine($"> - <@{id}>");
                }
            }
            else
            {
                pressDesc.AppendLine("> *(暂未选择)*");
            }
            container.WithTextDisplay(pressDesc.ToString());

            List<SubmitterData> pressSubmitter;
            if (VoteStaticData.Press.Count > 25)
            {
                pressSubmitter = VoteStaticData.GetPageData(Choice.Press, currentPressPageIndex).ToList();
            }
            else
            {
                pressSubmitter = VoteStaticData.Press;
            }

            var pressOptions = new List<SelectMenuOptionBuilder>();
            foreach (var data in pressSubmitter)
            {
                bool isSelected = _data.Press.Contains(data.SubmitterId);
                pressOptions.Add(new SelectMenuOptionBuilder(
                    label: data.Name,
                    value: data.SubmitterId.ToString())
                    .WithDefault(isSelected)
                );
            }

            if (pressOptions.Count > 0)
            {
                container.WithActionRow(row => row
                    .WithSelectMenu(
                        customId: $"{ActionVotePress}:{CacheId}",
                        options: pressOptions,
                        placeholder: "投票给 (记者)...",
                        minValues: 0,
                        maxValues: Math.Min(PressLimit, pressOptions.Count),
                        disabled: IsFinished
                    )
                );
            }

            if (VoteStaticData.Press.Count > 25)
            {
                container.WithActionRow(PaginationControlsBuilder.Build(CacheId, Choice.Press, currentPressPageIndex, VoteStaticData.GetTotalPages(Choice.Press)));
            }

            // =================================================================================
            // 4. 赛事委员 (CompetitionAdmin)
            // =================================================================================
            var competitionAdminDesc = new StringBuilder();
            competitionAdminDesc.AppendLine("### 赛事委员");

            int competitionAdminTotalPages = VoteStaticData.GetTotalPages(Choice.CompetitionAdmin);

            competitionAdminDesc.AppendLine($"您还可投 **{CompetitionAdminLimit - _data.CompetitionAdmin.Count}** 票, 当前已选择:");

            if (competitionAdminTotalPages > 1)
            {
                competitionAdminDesc.AppendLine($"> **第 {currentCompetitionAdminPageIndex + 1} / {competitionAdminTotalPages} 页**");
            }

            if (_data.CompetitionAdmin.Count > 0)
            {
                foreach (var id in _data.CompetitionAdmin)
                {
                    competitionAdminDesc.AppendLine($"> - <@{id}>");
                }
            }
            else
            {
                competitionAdminDesc.AppendLine("> *(暂未选择)*");
            }
            container.WithTextDisplay(competitionAdminDesc.ToString());

            List<SubmitterData> competitionAdminSubmitter;
            if (VoteStaticData.CompetitionAdmin.Count > 25)
            {
                competitionAdminSubmitter = VoteStaticData.GetPageData(Choice.CompetitionAdmin, currentCompetitionAdminPageIndex).ToList();
            }
            else
            {
                competitionAdminSubmitter = VoteStaticData.CompetitionAdmin;
            }

            var competitionAdminOptions = new List<SelectMenuOptionBuilder>();
            foreach (var data in competitionAdminSubmitter)
            {
                bool isSelected = _data.CompetitionAdmin.Contains(data.SubmitterId);
                competitionAdminOptions.Add(new SelectMenuOptionBuilder(
                    label: data.Name,
                    value: data.SubmitterId.ToString())
                    .WithDefault(isSelected)
                );
            }

            if (competitionAdminOptions.Count > 0)
            {
                container.WithActionRow(row => row
                    .WithSelectMenu(
                        customId: $"{ActionVoteCompetitionAdmin}:{CacheId}",
                        options: competitionAdminOptions,
                        placeholder: "投票给 (赛事)...",
                        minValues: 0,
                        maxValues: Math.Min(CompetitionAdminLimit, competitionAdminOptions.Count),
                        disabled: IsFinished
                    )
                );
            }

            if (VoteStaticData.CompetitionAdmin.Count > 25)
            {
                container.WithActionRow(PaginationControlsBuilder.Build(CacheId, Choice.CompetitionAdmin, currentCompetitionAdminPageIndex, VoteStaticData.GetTotalPages(Choice.CompetitionAdmin)));
            }

            container.WithSeparator();

            // =================================================================================
            // 底部按钮区域 (确认 / 取消)
            // =================================================================================
            if (_isComplete)
            {
                container.WithTextDisplay("### ✅ 操作已完成");
            }
            else if (_isCancel)
            {
                container.WithTextDisplay("### ⚠️ 操作已取消");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "确认投票",
                        customId: $"{ActionConfirm}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("📨"),
                        disabled: IsFinished
                    );
                    row.WithButton(
                        label: "取消",
                        customId: $"{ActionCancel}:{CacheId}",
                        style: ButtonStyle.Danger,
                        emote: new Emoji("✖️"),
                        disabled: IsFinished
                    );
                });
            }
        });

        return component.Build();
    }

    /// <summary>
    /// 将本次投票标记为已完成。
    /// </summary>
    public void Complete(ulong publicMessageId, ulong auditMessageId)
    {
        if (_isComplete) throw new InvalidOperationException("重复确认");
        _isComplete = true;
    }

    /// <summary>
    /// 将本次投票标记为已取消。
    /// </summary>
    public void Cancel()
    {
        _isCancel = true;
    }
}

/// <summary>
/// 分页控件构建工具类。
/// 为每个职位的候选人列表生成翻页按钮（首页、上页、页码显示、下页、末页）。
/// </summary>
public static class PaginationControlsBuilder
{
    // 定义通用的 Action 常量，用于 CustomId
    public const string ActionFirst = "first";
    public const string ActionPrev = "prev";
    public const string ActionNext = "next";
    public const string ActionLast = "last";

    // 职位名称常量，用于 CustomId 标识
    public const string ResourcesAdmin = "ResourcesAdmin";
    public const string ChatsAdmin = "ChatsAdmin";
    public const string Press = "Press";
    public const string CompetitionAdmin = "CompetitionAdmin";

    /// <summary>
    /// 构建一组翻页按钮组件。
    /// </summary>
    /// <param name="cacheId">当前缓存 ID</param>
    /// <param name="pageType">对应的职位类型</param>
    /// <param name="currentPageIndex">当前所在的页码 (0-indexed)</param>
    /// <param name="totalPages">总页数</param>
    /// <returns>包含分页按钮的 ActionRowBuilder</returns>
    public static ActionRowBuilder Build(string cacheId, Choice pageType, int currentPageIndex, int totalPages)
    {
        var currentPage = currentPageIndex + 1;

        // 首页按钮
        var firstButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionFirst}:{cacheId}:{Type(pageType)}")
            .WithEmote(new Emoji("⏮️"))
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(currentPage == 1);

        // 上一页按钮
        var prevButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionPrev}:{cacheId}:{Type(pageType)}")
            .WithEmote(new Emoji("◀️"))
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(currentPage == 1);

        // 下一页按钮
        var nextButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionNext}:{cacheId}:{Type(pageType)}")
            .WithEmote(new Emoji("▶️"))
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(currentPage == totalPages);

        // 末页按钮
        var lastButton = new ButtonBuilder()
            .WithCustomId($"pg:{ActionLast}:{cacheId}:{Type(pageType)}")
            .WithEmote(new Emoji("⏭️"))
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(currentPage == totalPages);

        return new ActionRowBuilder()
            .AddComponent(firstButton)
            .AddComponent(prevButton)
            .AddComponent(nextButton)
            .AddComponent(lastButton);
    }

    /// <summary>
    /// 将 Choice 枚举转换为字符串名称。
    /// </summary>
    private static string? Type(Choice pageType)
    {
        switch (pageType)
        {
            case Choice.ResourcesAdmin:
                return ResourcesAdmin;
            case Choice.ChatsAdmin:
                return ChatsAdmin;
            case Choice.Press:
                return Press;
            case Choice.CompetitionAdmin:
                return CompetitionAdmin;
            default:
                throw new InvalidCastException("未知的职位类型");
        }
    }
}