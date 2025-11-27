// using Discord;
// using Discord.Rest;
// using Discord.WebSocket;
// using System;
// using System.Collections.Generic;
// using System.Collections.Immutable;
// using System.Linq;
// using System.Reflection;
// using System.Threading.Tasks;

// namespace LunariumBot.Net.DataCenter;

// public static class SocketTextChannelExtensions
// {
//     private static class ReflectionCache
//     {
//         public static readonly PropertyInfo? DiscordProperty;
//         public static readonly MethodInfo? GetPublicArchivedThreadsMethod;
//         public static readonly MethodInfo? GetPrivateArchivedThreadsMethod;
//         public static readonly MethodInfo? GetJoinedPrivateArchivedThreadsMethod;

//         static ReflectionCache()
//         {
//             Type? currentType = typeof(SocketTextChannel);
//             PropertyInfo? discordProp = null;
//             while (currentType != null && discordProp == null)
//             {
//                 discordProp = currentType.GetProperty("Discord", BindingFlags.NonPublic | BindingFlags.Instance);
//                 currentType = currentType.BaseType;
//             }
//             DiscordProperty = discordProp;
            
//             // 【核心修正】
//             // 从 Discord.Rest.dll 程序集中获取 internal 的 ThreadHelper 类型
//             var threadHelperType = typeof(DiscordRestClient).Assembly.GetType("Discord.Rest.ThreadHelper");
//             if (threadHelperType != null)
//             {
//                 // 定义我们要查找的方法的参数类型，以精确匹配
//                 var parameterTypes = new[] { typeof(IGuildChannel), typeof(BaseDiscordClient), typeof(int?), typeof(DateTimeOffset?), typeof(RequestOptions) };
                
//                 // 使用 BindingFlags.Static | BindingFlags.Public 来查找 internal 类中的 public static 方法
//                 GetPublicArchivedThreadsMethod = threadHelperType
//                     .GetMethod("GetPublicArchivedThreadsAsync", BindingFlags.Static | BindingFlags.Public, null, parameterTypes, null);
                
//                 GetPrivateArchivedThreadsMethod = threadHelperType
//                     .GetMethod("GetPrivateArchivedThreadsAsync", BindingFlags.Static | BindingFlags.Public, null, parameterTypes, null);
                
//                 GetJoinedPrivateArchivedThreadsMethod = threadHelperType
//                     .GetMethod("GetJoinedPrivateArchivedThreadsAsync", BindingFlags.Static | BindingFlags.Public, null, parameterTypes, null);
//             }
//         }
//     }
//     private static void ValidateReflectionCache()
//     {
//         if (ReflectionCache.DiscordProperty == null)
//             throw new InvalidOperationException("反射: 无法获取 Discord 属性");
//     }

//     private static Task<IReadOnlyCollection<RestThreadChannel>> GetArchivedThreadsInternalAsync(
//         SocketTextChannel channel,
//         MethodInfo apiMethod,
//         int? limit,
//         DateTimeOffset? before,
//         RequestOptions? options)
//     {
//         ValidateReflectionCache();

//         var discordClient = ReflectionCache.DiscordProperty!.GetValue(channel) as BaseDiscordClient;
//         if (discordClient == null)
//             throw new InvalidOperationException("反射: Discord 客户端为 null 或类型不匹配");

//         var taskResult = apiMethod.Invoke(null, new object?[] { channel, discordClient, limit, before, options }) as Task<IReadOnlyCollection<RestThreadChannel>>;

//         if (taskResult == null)
//             throw new InvalidOperationException("反射: API 方法调用失败或返回类型不正确");
        
//         return taskResult;
//     }

//     public static Task<IReadOnlyCollection<RestThreadChannel>> GetPublicArchivedThreadsAsync(
//         this SocketTextChannel channel,
//         int? limit = null,
//         DateTimeOffset? before = null,
//         RequestOptions? options = null)
//     {
//         if (ReflectionCache.GetPublicArchivedThreadsMethod == null)
//             throw new InvalidOperationException("反射: 无法获取 GetPublicArchivedThreadsAsync 方法");

//         return GetArchivedThreadsInternalAsync(
//             channel,
//             ReflectionCache.GetPublicArchivedThreadsMethod,
//             limit,
//             before,
//             options);
//     }

//     public static Task<IReadOnlyCollection<RestThreadChannel>> GetPrivateArchivedThreadsAsync(
//         this SocketTextChannel channel,
//         int? limit = null,
//         DateTimeOffset? before = null,
//         RequestOptions? options = null)
//     {
//         if (ReflectionCache.GetPrivateArchivedThreadsMethod == null)
//             throw new InvalidOperationException("反射: 无法获取 GetPrivateArchivedThreadsAsync 方法");

//         return GetArchivedThreadsInternalAsync(
//             channel,
//             ReflectionCache.GetPrivateArchivedThreadsMethod,
//             limit,
//             before,
//             options);
//     }

//     public static Task<IReadOnlyCollection<RestThreadChannel>> GetJoinedPrivateArchivedThreadsAsync(
//         this SocketTextChannel channel,
//         int? limit = null,
//         DateTimeOffset? before = null,
//         RequestOptions? options = null)
//     {
//         if (ReflectionCache.GetJoinedPrivateArchivedThreadsMethod == null)
//             throw new InvalidOperationException("反射: 无法获取 GetJoinedPrivateArchivedThreadsAsync 方法");

//         return GetArchivedThreadsInternalAsync(
//             channel,
//             ReflectionCache.GetJoinedPrivateArchivedThreadsMethod,
//             limit,
//             before,
//             options);
//     }
// }