using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.ReleaseBot.Models;

namespace LunariumBot.Net.ReleaseBot.InteractionsCache;

public class ReleaseCache : BaseInteractionsCache<WorkData>
{
    // ID 前缀常量
    public const string ActionNoLimit = "Release:NoLimit";
    public const string ActionReaction = "Release:Reaction";
    public const string ActionReactionOrReply = "Release:ReactionOrReply";
    public const string ActionNeedPassword = "Release:NeedPassword";
    public const string ActionFreeAccess = "Release:FreeAccess";
    public const string ActionConsumeToken = "Release:ConsumeToken";
    public const string ActionSetPassword = "Release:SetPassword";
    public const string ActionEnableAlerts = "Release:EnableAlerts";
    public const string ActionDisabledAlerts = "Release:DisabledAlerts";
    public const string ActionAlerts = "Release:Alerts";
    public const string ActionAddWorks = "Release:AddWorks";
    public const string ActionConfirm = "Release:Confirm";
    public const string ActionCancel = "Release:Cancel";

    public WorkData Data => _data;
    public IMessage? CacheMessage { get; set; }
    public bool IsFromMessageCommand { get; set; } = false; 
    public IMessage? SourceMessage { get; set; }

    private List<IAttachment>? _attachments;
    private bool _isCancel = false;
    private bool _isComplete = false;
    private bool IsFinished => _isCancel || _isComplete;
    public string? TempLocalPath { get; set; }

    public ReleaseCache(WorkData data, DateTimeOffset expiresAt, string id)
        : base(data, expiresAt, id) { }

    public override MessageComponent Render()
    {
        // 1
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);
            // 2
            container.WithTextDisplay("### 作品发布面板");
            // 3
            container.WithSeparator();

            string modelStr = "";
            switch (_data.ReleaseModel)
            {
                case ReleaseModel.NoLimit:
                    modelStr = _data.NeedPassword ? "提取码" : "无限制";
                    break;
                case ReleaseModel.Reaction:
                    modelStr = _data.NeedPassword ? "点赞 + 提取码" : "点赞";
                    break;
                case ReleaseModel.ReactionOrReply:
                    modelStr = _data.NeedPassword ? "点赞或回复 + 提取码" : "点赞或回复";
                    break;
            }
            // 4
            container.WithTextDisplay($"""
                ### 获取作品需求
                当前模式: **{modelStr}**
                """);
            // 5
            container.WithActionRow(row =>
            {
                // 6
                row.WithButton(
                    label: "无限制",
                    customId: $"{ActionNoLimit}:{CacheId}",
                    style: _data.ReleaseModel != ReleaseModel.NoLimit ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🔆"),
                    disabled: IsFinished
                );
                // 7
                row.WithButton(
                    label: "点赞",
                    customId: $"{ActionReaction}:{CacheId}",
                    style: _data.ReleaseModel != ReleaseModel.Reaction ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("❤️"),
                    disabled: IsFinished
                );
                // 8
                row.WithButton(
                    label: "点赞或回复",
                    customId: $"{ActionReactionOrReply}:{CacheId}",
                    style: _data.ReleaseModel != ReleaseModel.ReactionOrReply ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎁"),
                    disabled: IsFinished
                );
            });
            // 9
            container.WithSeparator();
            // 10
            container.WithTextDisplay($"""
                ### 提取码
                > -# 点击按钮切换是否启用来和上方的需求进行组合 (无限制 + 启用提取码为纯提取码模式)
                > -# 🎈 记得将提取码置于贴内
                > -# ⚠️ 开头或结尾的空格将被自动清理
                """);

            // 11
            container.WithActionRow(row =>
            {
                // 12
                row.WithButton(
                    label: _data.NeedPassword ? "提取码: 已启用" : "提取码: 已关闭",
                    customId: $"{ActionNeedPassword}:{CacheId}",
                    style: _data.NeedPassword ? ButtonStyle.Success : ButtonStyle.Primary,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });
            
            if (_data.NeedPassword)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"""
                    当前提取码: 
                    """);
                if (!string.IsNullOrWhiteSpace(_data.ReleasePassword))
                {
                    sb.AppendLine($"""
                        ```
                        {_data.ReleasePassword}
                        ```
                        """);
                }
                // 13
                container.WithSection(section => section
                    // 14
                    .WithTextDisplay(sb.ToString())
                    // 15
                    .WithAccessory(new ButtonBuilder(
                        label: "输入提取码",
                        customId: $"{ActionSetPassword}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("📝"),
                        isDisabled: IsFinished
                    ))
                );
            }

            // 16
            container.WithTextDisplay($"""
                ### 获取次数设置
                可以设置当用户的**当日获取作品次数耗尽**时, 是否依然允许其获取本作品?
                当前设置: {(_data.IsConsumeToken ? "每日限定: 用户的每日获取作品次数耗尽后**无法**获取本作品" : "开放分享: 用户的每日获取作品次数耗尽后**仍可**获取本作品")}
                """);
            // 17
            container.WithActionRow(row =>
            {
                // 18
                row.WithButton(
                    label: "开放分享",
                    customId: $"{ActionFreeAccess}:{CacheId}",
                    style: _data.IsConsumeToken ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎀"),
                    disabled: IsFinished
                );
                // 19
                row.WithButton(
                    label: "每日限定",
                    customId: $"{ActionConsumeToken}:{CacheId}",
                    style: !_data.IsConsumeToken ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎫"),
                    disabled: IsFinished
                );
            });
            // 20
            container.WithSeparator();
            // 21
            container.WithTextDisplay($"""
                ### 作者声明
                当前状态: {(_data.EnableAlerts ? "**已启用**" : "**已关闭**")}
                > 在用户下载作品前将先使用本条内容提示一遍用户, 要求用户二次确认声明内容
                """);
            // 22
            container.WithActionRow(row =>
            {
                // 23
                row.WithButton(
                    label: "启用",
                    customId: $"{ActionEnableAlerts}:{CacheId}",
                    style: _data.EnableAlerts ? ButtonStyle.Success : ButtonStyle.Secondary,
                    emote: new Emoji("🔔"),
                    disabled: IsFinished
                );
                // 24
                row.WithButton(
                    label: "关闭",
                    customId: $"{ActionDisabledAlerts}:{CacheId}",
                    style: !_data.EnableAlerts ? ButtonStyle.Danger : ButtonStyle.Secondary,
                    emote: new Emoji("🔕"),
                    disabled: IsFinished
                );
            });
            // 25
            container.WithSection(section => section
                // 26
                .WithTextDisplay($"### 当前声明内容: {(_data.EnableAlerts ? $"\n{_data.Alerts}" : "\n已禁用")}")
                // 27
                .WithAccessory(new ButtonBuilder(
                    label: "输入声明",
                    customId: $"{ActionAlerts}:{CacheId}",
                    style: ButtonStyle.Success,
                    emote: new Emoji("📝"),
                    isDisabled: !_data.EnableAlerts || IsFinished
                ))
            );
            // 28
            container.WithSeparator();

            // 总计最多2个
            // 29
            if (IsFromMessageCommand)
            {
                container.WithTextDisplay("### 作品已从消息导入\n> 在从消息导入消息附件后附件内容将固定为当时的原消息附件, 无法修改\n> 如需修改请发起新的交互面板");
            }
            else
            {
                // 29
                container.WithActionRow(row =>
                {
                    // 30
                    row.WithButton(
                        label: "添加作品",
                        customId: $"{ActionAddWorks}:{CacheId}",
                        style: ButtonStyle.Primary,
                        emote: new Emoji("➕"),
                        disabled: IsFinished
                    );
                });
            }

            // 总计最多2个
            if (_data.CacheAttachmentUrls.Count > 0)
            {
                List<string>? imageAttachment = _attachments?
                    .Where(a => IsImageAttachment(a))
                    .Select(a => a.Url)
                    .ToList();
                List<IAttachment>? otherAttachment = _attachments?
                    .Where(a => !IsImageAttachment(a))
                    .ToList();

                if (imageAttachment?.Count > 0)
                {
                    MediaGalleryBuilder media = new();
                    foreach (var attachment in imageAttachment)
                    {
                        media.AddItem(attachment);
                    }
                    // 31
                    container.WithMediaGallery(media);
                }
                if (otherAttachment?.Count > 0)
                {
                    StringBuilder sb = new();
                    foreach (var attachment in otherAttachment)
                    {
                        sb.AppendLine($"### 📄 {attachment.Filename}");
                        sb.AppendLine($"大小: {attachment.Size / 1024.0:F2} KB | 类型: {attachment.ContentType}");
                        sb.AppendLine($"[>>点击下载<<]({attachment.Url})");
                    }
                    // 32
                    container.WithTextDisplay(sb.ToString());
                }
            }

            // 33
            container.WithSeparator();

            if (_isComplete)
            {
                // 34
                container.WithTextDisplay("### 操作已完成");
            }
            else if (_isCancel)
            {
                // 34
                container.WithTextDisplay("### 操作已取消");
            }
            else
            {
                // 34
                container.WithActionRow(row =>
                {
                    // 35
                    row.WithButton(
                        label: "发布",
                        customId: $"{ActionConfirm}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("📨"),
                        disabled: IsFinished
                    );
                    // 36
                    row.WithButton(
                        label: "取消",
                        customId: $"{ActionCancel}:{CacheId}",
                        style: ButtonStyle.Danger,
                        emote: new Emoji("⚠️"),
                        disabled: IsFinished
                    );
                });
            }
            // 37
            container.WithSeparator();
            // 38
            container.WithTextDisplay("-# 如使用中有任何问题或建议请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)");
        });

        return component.Build();
    }

    public WorkData Complete()
    {
        if (_isComplete) throw new InvalidOperationException("重复确认");
        _isComplete = true;
        return _data;
    }

    public void Cancel()
    {
        _isCancel = true;
    }

    public void SetAttachment(List<IAttachment> attachments)
    {
        _attachments = attachments;
    }

    private bool IsImageAttachment(IAttachment attachment)
    {
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".gifv", ".webp", ".bmp" };
        return imageExtensions.Any(ext =>
            attachment.Filename.ToLowerInvariant().EndsWith(ext));
    }
}