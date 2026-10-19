using Discord;
using Discord.WebSocket;
using Lunarium.Bot.Framework;
using Lunarium.Bot.Framework.Configuration;

namespace Lunarium.Bot.Framework.AuthRuleEngine;

public static class BaseRuleSet
{
    public class RequireAdminUser(IReadOnlySet<ulong> adminIds) : IAuthRule, IMsgBasedAuthRule
    {
        public ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.User is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!adminIds.Contains(context.User.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是已配置的管理员 ID 列表内的成员")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }

        public ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Author is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!adminIds.Contains(msg.Author.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是已配置的管理员 ID 列表内的成员")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }
    }

    public class RequireAdminRole(IReadOnlySet<ulong> adminRoleIds) : IAuthRule, IMsgBasedAuthRule
    {
        public async ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.Guild is null)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得服务器信息"));

            var user = context.User as IGuildUser
                   ?? await context.Guild.GetUserAsync(context.User.Id);

            if (user is null)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息"));

            if (!user.RoleIds.Any(adminRoleIds.Contains))
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("缺少必要的管理员身份组"));

            return new ValidateResult(ResultType.Success, null);
        }

        public async ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Channel is null)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得消息所在频道信息"));
            if (msg.Channel.ChannelType is ChannelType.DM)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("消息不在服务器内"));
            if (msg.Channel is not IGuildChannel channel)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("消息所在频道不是预期类型"));
            if (channel.Guild is null)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得消息所在服务器信息"));

            var user = msg.Author as IGuildUser
                   ?? await channel.Guild.GetUserAsync(msg.Author.Id);

            if (user is null)
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息"));

            if (!user.RoleIds.Any(adminRoleIds.Contains))
                return new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("缺少必要的管理员身份组"));

            return new ValidateResult(ResultType.Success, null);
        }
    }

    public class RequireInGuild(IReadOnlySet<ulong> guildIds) : IAuthRule, IMsgBasedAuthRule
    {
        public ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.Guild is null || !guildIds.Contains(context.Guild.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是在已配置的服务器内")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }

        public ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Channel is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得消息所在频道信息")));
            if (msg.Channel.ChannelType is ChannelType.DM)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("消息不在服务器内")));
            if (msg.Channel is not IGuildChannel channel)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("消息所在频道不是预期类型")));

            if (!guildIds.Contains(channel.GuildId))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是在已配置的服务器内")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }
    }

    public class RequireInAnyGuild() : IAuthRule, IMsgBasedAuthRule
    {
        public ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.Guild is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("仅限在服务器内使用")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }

        public ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Channel is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得消息所在频道信息")));
            if (msg.Channel is not IGuildChannel)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("消息不在服务器内")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }
    }

    public class RequireMaintainer(IReadOnlySet<ulong> maintainerIds) : IAuthRule, IMsgBasedAuthRule
    {
        public ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.User is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!maintainerIds.Contains(context.User.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是已配置的维护者 ID 列表内的成员")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }

        public ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Author is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!maintainerIds.Contains(msg.Author.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("不是已配置的维护者 ID 列表内的成员")));

            return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));
        }
    }

    public class MaintainerBypass(IReadOnlySet<ulong> maintainerIds) : IAuthRule, IMsgBasedAuthRule
    {
        public ValueTask<ValidateResult> ValidateAsync(IInteractionContext context)
        {
            if (context.User is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!maintainerIds.Contains(context.User.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));

            return ValueTask.FromResult(new ValidateResult(ResultType.Bypass, null));
        }

        public ValueTask<ValidateResult> ValidateAsync(SocketMessage msg)
        {
            if (msg.Author is null)
                return ValueTask.FromResult(new ValidateResult(ResultType.Fail, MessagePrefab.AuthFail("无法获得用户信息")));

            if (!maintainerIds.Contains(msg.Author.Id))
                return ValueTask.FromResult(new ValidateResult(ResultType.Success, null));

            return ValueTask.FromResult(new ValidateResult(ResultType.Bypass, null));
        }
    }
}