using Discord;
using Discord.WebSocket;
using Lunarium.Bot.Logger;
using System;

namespace Lunarium.Bot.ReleaseBot.Utils;

public static class DMSendRelease
{
    public static async Task<IUserMessage> Send(DiscordSocketClient client, ulong userId, MessageComponent component)
    {
        try
        {
            var user = await client.GetUserAsync(userId);
            var message = await user.SendMessageAsync(components: component);
            return message;
        }
        catch
        {
            throw;
        }
    }
}
