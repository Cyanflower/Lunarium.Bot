using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Text.Json;

using Discord;
using Discord.Interactions;

using SearchBot.Database;
using SearchBot.ThreadFetcher;
using SearchBot.Utils;
using SearchBot.Model;
using SearchBot.Scheduler;
using LunariumExtensionMethod;

namespace SearchBot.AppInteractionService;

public partial class AppCommands : InteractionModuleBase<SocketInteractionContext>
{
    // 收藏该贴

    // 订阅该贴

    // 搜索该用户作品

    // 查看收藏

    // 查看订阅

    // 查看更新推送
}