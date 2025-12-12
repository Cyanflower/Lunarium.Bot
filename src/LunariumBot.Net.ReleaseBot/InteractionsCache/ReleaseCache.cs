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
    public const string ActionReactionOrReply = "Release:ReactionOrReply";
    public const string ActionPassword = "Release:Password";
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
                    modelStr = "无限制";
                    break;
                case ReleaseModel.ReactionOrReply:
                    modelStr = "点赞或回复";
                    break;
                case ReleaseModel.Password:
                    modelStr = "提取码";
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
                    label: "点赞或回复",
                    customId: $"{ActionReactionOrReply}:{CacheId}",
                    style: _data.ReleaseModel != ReleaseModel.ReactionOrReply ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎁"),
                    disabled: IsFinished
                );
                // 8
                row.WithButton(
                    label: "提取码",
                    customId: $"{ActionPassword}:{CacheId}",
                    style: _data.ReleaseModel != ReleaseModel.Password ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("#️⃣"),
                    disabled: IsFinished
                );
            });
            // 9
            container.WithTextDisplay($"""
                ### 获取次数设置
                可以设置当用户**当日获取作品次数耗尽**时, 是否依然允许其获取本作品?
                当前设置: **{(_data.IsConsumeToken ? "开放分享: 每日获取作品次数耗尽后**仍可**获取本作品" : "每日限定: 每日获取作品次数耗尽后**无法**获取本作品")}**
                """);
            // 10
            container.WithActionRow(row =>
            {
                // 11
                row.WithButton(
                    label: "开放分享",
                    customId: $"{ActionFreeAccess}:{CacheId}",
                    style: _data.IsConsumeToken ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎀"),
                    disabled: IsFinished
                );
                // 12
                row.WithButton(
                    label: "每日限定",
                    customId: $"{ActionConsumeToken}:{CacheId}",
                    style: !_data.IsConsumeToken ? ButtonStyle.Secondary : ButtonStyle.Success,
                    emote: new Emoji("🎫"),
                    disabled: IsFinished
                );
            });
            
            if (_data.ReleaseModel == ReleaseModel.Password)
            {
                var sb = new StringBuilder();
                sb.AppendLine("""
                    ### 提取码
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
                sb.AppendLine("""
                    > -# 🎈 记得将提取码置于贴内
                    > -# ⚠️ 开头或结尾的空格将被自动清理
                    """);
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
            container.WithSeparator();
            // 17
            container.WithTextDisplay($"""
                ### 作者声明
                -# 在用户下载作品前将先使用本条内容提示一遍用户, 要求用户二次确认声明内容
                当前状态: {(_data.EnableAlerts ? "**已启用**" : "**已关闭**")}
                """);
            // 18
            container.WithActionRow(row =>
            {
                // 19
                row.WithButton(
                    label: "启用",
                    customId: $"{ActionEnableAlerts}:{CacheId}",
                    style: _data.EnableAlerts ? ButtonStyle.Success : ButtonStyle.Secondary,
                    emote: new Emoji("🔔"),
                    disabled: IsFinished
                );
                // 20
                row.WithButton(
                    label: "关闭",
                    customId: $"{ActionDisabledAlerts}:{CacheId}",
                    style: !_data.EnableAlerts ? ButtonStyle.Danger : ButtonStyle.Secondary,
                    emote: new Emoji("🔕"),
                    disabled: IsFinished
                );
            });
            // 21
            container.WithSection(section => section
                // 22
                .WithTextDisplay($"### 当前声明内容: {(_data.EnableAlerts ? $"\n{_data.Alerts}" : "\n已禁用")}")
                // 23
                .WithAccessory(new ButtonBuilder(
                    label: "输入声明",
                    customId: $"{ActionAlerts}:{CacheId}",
                    style: ButtonStyle.Success,
                    emote: new Emoji("📝"),
                    isDisabled: !_data.EnableAlerts || IsFinished
                ))
            );
            // 24
            container.WithSeparator();
            // 25
            container.WithActionRow(row =>
            {
                // 26
                row.WithButton(
                    label: IsFromMessageCommand ? "作品已导入" : "添加作品",
                    customId: $"{ActionAddWorks}:{CacheId}",
                    style: IsFromMessageCommand ? ButtonStyle.Success : ButtonStyle.Primary,
                    emote: new Emoji("➕"),
                    disabled: IsFromMessageCommand || IsFinished
                );
            });
            if (IsFromMessageCommand)
            {
                // 27
                container.WithTextDisplay("-# 在从消息导入消息附件后附件内容将固定为当时的原消息附件, 无法修改\n如需修改请发起新的交互面板");
            }
            // 总计最多3个
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
                    container.WithTextDisplay(sb.ToString());
                }
            }

            // 30
            container.WithSeparator();

            if (_isComplete)
            {
                // 31
                container.WithTextDisplay("### 操作已完成");
            }
            else if (_isCancel)
            {
                // 32
                container.WithTextDisplay("### 操作已取消");
            }
            else
            {
                // 33
                container.WithActionRow(row =>
                {
                    // 34
                    row.WithButton(
                        label: "发布",
                        customId: $"{ActionConfirm}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("📨"),
                        disabled: IsFinished
                    );
                    // 35
                    row.WithButton(
                        label: "取消",
                        customId: $"{ActionCancel}:{CacheId}",
                        style: ButtonStyle.Danger,
                        emote: new Emoji("⚠️"),
                        disabled: IsFinished
                    );
                });
            }
            // 36
            container.WithSeparator();
            // 37
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