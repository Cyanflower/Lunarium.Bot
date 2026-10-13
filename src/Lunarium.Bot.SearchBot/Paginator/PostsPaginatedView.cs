using System.Text;
using Discord;
using Lunarium.Bot.Extensions;
using Lunarium.Bot.SearchBot.Models;

namespace Lunarium.Bot.SearchBot.Paginator;

public class PostsPaginatedView : BasePaginatedView<PostDisplayModel>, IPaginatedView
{
    public PostsPaginatedView(IEnumerable<PostDisplayModel> items, DateTimeOffset expiresAt, string id) : base(items, expiresAt, id, 8) { }

    public override MessageComponent Render()
    {
        int i = 0;
        var messageComponent = new ComponentBuilderV2();
        // Container 本身占用一个组件
        messageComponent.WithContainer(c =>
            {
                c.WithAccentColor(Color.Blue);
                // 4 * 8, 30个组件
                foreach (var item in _pages[_currentPageIndex].Items)
                {
                    var sb = new StringBuilder();

                    sb.AppendLine($"## {item.Title}({item.JumpUrl})");
                    sb.AppendLine($"-# {item.ChannelName}丨作者: <@{item.AuthorId}>");
                    sb.AppendLine($"-# 帖子信息: ❤️ {item.ReactionCount}丨💬 {item.ReplyCount}丨🏷 {item.Tags}");
                    sb.AppendLine($"-# 发帖时间: <t:{item.CreatedAt}:D>丨最近活跃: <t:{item.LastActiveAt}:R>");
                    
                    if (item.UpdatedAt is not null)
                    {
                        sb.AppendLine($"-# 更新时间: <t:{item.UpdatedAt.Value.ToUnixTimeSeconds()}:R>");
                        sb.AppendLine($"-# 更新链接: {item.UpdateMessageJumpUrl}");
                    }
                    sb.AppendLine("============");
                    if (string.IsNullOrWhiteSpace(item.Content))
                    {
                        sb.AppendLine("无首楼");
                    }
                    else
                    {
                        sb.AppendLine($"{item.Content.Slice(150)}");
                    }
                    sb.AppendLine("============");
                    // 1
                    c.WithSection(section =>
                    {
                        // 2
                        section.WithTextDisplay(sb.ToString());
                        if (item.AttachmentUrls?.Count > 0)
                        {
                            // 3
                            section.WithAccessory(new ThumbnailBuilder().WithMedia(item.AttachmentUrls[0]));
                        }
                    }
                    );
                    // 4
                    c.WithSeparator();
                    i++;
                }
            }
        );
        // 翻页部分: 5 按钮 + 1 容器, 6组件
        messageComponent.WithActionRow(PaginationControlsBuilder.Build(this));
        // 32(每item行) + 6(翻页交互) + 1(Container) + 1(Separator) = 40
        // 目前 discord 限制为 40 个组件
        return messageComponent.Build();
    }
    
}