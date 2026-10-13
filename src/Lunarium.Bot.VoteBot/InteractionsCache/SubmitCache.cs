using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.VoteBot.Models;
using Lunarium.Bot.VoteBot.VoteServices;

namespace Lunarium.Bot.VoteBot.InteractionsCache;

public class SubmitCache : BaseInteractionsCache<SubmitData>
{
    // ID 前缀常量
    public const string ActionAboutMe = "Submit:AboutMe";
    public const string ActionPreferences = "Submit:Preferences"; 
    public const string ActionFirstChoice = "Submit:FirstChoice";
    public const string ActionSecondChoice  = "Submit:SecondChoice";
    public const string ActionManifesto  = "Submit:Manifesto";
    public const string ActionPressRelease  = "Submit:PressRelease";
    public const string ActionConfirm = "Submit:Confirm";
    public const string ActionCancel = "Submit:Cancel";

    public SubmitData Data => _data;
    public IMessage? CacheMessage { get; set; }
    public bool IsFromMessageCommand { get; set; } = false; 
    public IMessage? SourceMessage { get; set; }

    private bool _isCancel = false;
    private bool _isComplete = false;
    private bool IsFinished => _isCancel || _isComplete;
    public string? TempLocalPath { get; set; }

    public SubmitCache(SubmitData data, DateTimeOffset expiresAt, string id)
        : base(data, expiresAt, id) { }

    public override MessageComponent Render()
    {
        // 1
        var component = new ComponentBuilderV2().WithContainer(container =>
        {
            container.WithAccentColor(Color.Blue);

            container.WithTextDisplay("### 提交竞选表单");

            container.WithSeparator();

            // ==== 自我介绍 ===============================================
            
            container.WithSection(section => section
                .WithTextDisplay($"""
                    **自我介绍**
                    使用一句话简单介绍一下自己
                    > {_data.AboutMe.Replace("\n", "\n> ")}
                    """)
                .WithAccessory(new ButtonBuilder(
                    label: "编辑",
                    customId: $"{ActionAboutMe}:{CacheId}",
                    style: ButtonStyle.Success,
                    emote: new Emoji("✒️"),
                    isDisabled: IsFinished
                ))
            );

            container.WithSeparator();

            // ==== 内容偏好 ===============================================

            var strPreferences = new StringBuilder();

            strPreferences.AppendLine("**内容偏好**");
            strPreferences.AppendLine("根据你在社区的主要互动或创作方向选择"); 

            if (Data.Preferences.Count == 0)
            {
                strPreferences.AppendLine("> 未选择");
            }
            else
            {
                foreach (var preference in Data.Preferences)
                {
                    strPreferences.AppendLine($"> {preference.ToDescription()}");
                }
            }

            container.WithTextDisplay(strPreferences.ToString());

            var preferenceOptions = new List<SelectMenuOptionBuilder>();

            // 遍历枚举生成选项
            foreach (PreferenceType type in Enum.GetValues(typeof(PreferenceType)))
            {
                if (type == PreferenceType.None) continue; // 跳过 "未选择"

                // 如果当前数据中包含此选项，设为默认选中
                bool isSelected = _data.Preferences.Contains(type);

                preferenceOptions.Add(new SelectMenuOptionBuilder(
                    label: type.ToDescription(),
                    value: type.ToString())
                    .WithDefault(isSelected) // 关键：回显用户之前的选择
                );
            }
            
            container.WithActionRow(actionRow => actionRow
                .WithSelectMenu(
                    customId: $"{ActionPreferences}:{CacheId}", // 使用新定义的常量
                    options: preferenceOptions,
                    placeholder: "选择偏好方向 (可多选)...",
                    minValues: 1, // 允许清空
                    maxValues: preferenceOptions.Count, // 允许全选
                    disabled: IsFinished
                )
            );


            container.WithSeparator();

            // ==== 第一志愿 ===============================================

            container.WithTextDisplay($"""
                **第一志愿**
                当前选择:
                > **{_data.FirstChoice.ToDescription()}**
                """);
            container.WithActionRow(actionRow => actionRow
                .WithSelectMenu(
                    customId: $"{ActionFirstChoice}:{CacheId}",
                    options: GetChoiceOptions(_data.FirstChoice),
                    placeholder: "选择志愿...",
                    minValues: 1,
                    maxValues: 1,
                    disabled: IsFinished
                )
            );

            container.WithSeparator();
            // ==== 第二志愿 ===============================================

            container.WithTextDisplay($"""
                **第二志愿 (可选)**
                当前选择:
                > **{_data.SecondChoice.ToDescription()}**
                """);
            container.WithActionRow(actionRow => actionRow
                .WithSelectMenu(
                    customId: $"{ActionSecondChoice}:{CacheId}",
                    options: GetChoiceOptions(_data.SecondChoice),
                    placeholder: "选择志愿...",
                    minValues: 1,
                    maxValues: 1,
                    disabled: IsFinished
                )
            );
                    
            container.WithSeparator();
            // ==== 竞选宣言 ===============================================

            container.WithSection(section => section
                .WithTextDisplay($"""
                    **竞选宣言**
                    > {_data.Manifesto.Replace("\n", "\n> ")}
                    """)
                .WithAccessory(new ButtonBuilder(
                    label: "编辑",
                    customId: $"{ActionManifesto}:{CacheId}",
                    style: ButtonStyle.Success,
                    emote: new Emoji("✒️"),
                    isDisabled: IsFinished
                ))
            );

            // ==== 如果是记者 ===============================================
            if (_data.FirstChoice == Choice.Press || _data.SecondChoice == Choice.Press)
            {
                container.WithSeparator();
    
                container.WithSection(section => section
                    .WithTextDisplay($"""
                        **新闻试稿**
                        -# 请编写一篇社区新闻试稿
                        > {_data.PressRelease.Replace("\n", "\n> ")} 
                        """)
                    .WithAccessory(new ButtonBuilder(
                        label: "编辑",
                        customId: $"{ActionPressRelease}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("✒️"),
                        isDisabled: IsFinished
                    ))
                );
            }

            container.WithSeparator();

            if (_isComplete)
            {
                container.WithTextDisplay("### 操作已完成");
            }
            else if (_isCancel)
            {
                container.WithTextDisplay("### 操作已取消");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    row.WithButton(
                        label: "报名",
                        customId: $"{ActionConfirm}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("📨"),
                        disabled: IsFinished
                    );
                    row.WithButton(
                        label: "取消",
                        customId: $"{ActionCancel}:{CacheId}",
                        style: ButtonStyle.Danger,
                        emote: new Emoji("⚠️"),
                        disabled: IsFinished
                    );
                });
            }
        });

        return component.Build();
    }

    public SubmitData Complete(ulong publicMessageId, ulong auditMessageId)
    {
        if (_isComplete) throw new InvalidOperationException("重复确认");
        _data.PublicMessageId = publicMessageId;
        _data.AuditMessageId = auditMessageId;
        _data.Status = SubmissionStatus.Approved;
        _isComplete = true;
        return _data;
    }

    public void Cancel()
    {
        _isCancel = true;
    }

    /// <summary>
    /// 根据当前选中的值，生成下拉菜单选项列表
    /// </summary>
    private List<SelectMenuOptionBuilder> GetChoiceOptions(Choice currentSelection)
    {
        var options = new List<SelectMenuOptionBuilder>();

        // 遍历 Choice 枚举的所有值
        foreach (Choice choice in Enum.GetValues(typeof(Choice)))
        {
            // 判断是否是当前选中的值
            bool isSelected = choice == currentSelection;

            options.Add(new SelectMenuOptionBuilder(
                label: choice.ToDescription(),
                value: choice.ToString()
            ).WithDefault(isSelected));
        }

        return options;
    }
}