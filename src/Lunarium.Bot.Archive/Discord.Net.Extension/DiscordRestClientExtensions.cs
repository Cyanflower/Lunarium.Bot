// // ======= 处理 nullable =======

// using Discord;
// using Discord.Rest;
// using System.Collections.Immutable;
// using System.Reflection;

// namespace Lunarium.Bot.Archive;

// public static class DiscordRestClientExtensions
// {
//     private static class ReflectionCache
//     {
//         public static readonly PropertyInfo? ApiClientProperty;
//         public static readonly MethodInfo? GetActiveThreadsMethod;
//         public static readonly MethodInfo? CreateThreadChannelMethod;

//         static ReflectionCache()
//         {
//             // 缓存 ApiClient 属性
//             ApiClientProperty = typeof(DiscordRestClient).BaseType
//                 ?.GetProperty("ApiClient", BindingFlags.NonPublic | BindingFlags.Instance);

//             if (ApiClientProperty != null)
//             {
//                 var apiClientType = ApiClientProperty.PropertyType;

//                 // 缓存 GetActiveThreadsAsync 方法
//                 GetActiveThreadsMethod = apiClientType
//                     .GetMethod("GetActiveThreadsAsync", BindingFlags.Public | BindingFlags.Instance);
//             }

//             // 缓存 RestThreadChannel.Create 方法
//             CreateThreadChannelMethod = typeof(RestThreadChannel)
//                 .GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
//         }
//     }

//     public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(
//         this DiscordRestClient client,
//         IGuild guild,
//         RequestOptions? options = null)
//     {
//         // 验证反射缓存是否成功
//         if (ReflectionCache.ApiClientProperty == null)
//             throw new InvalidOperationException("反射: 无法通过反射获取 ApiClient 属性");

//         if (ReflectionCache.GetActiveThreadsMethod == null)
//             throw new InvalidOperationException("反射: 无法通过反射获取 GetActiveThreadsAsync 方法");

//         if (ReflectionCache.CreateThreadChannelMethod == null)
//             throw new InvalidOperationException("反射: 无法通过反射获取 RestThreadChannel.Create 方法");

//         // 使用缓存的反射信息
//         var apiClient = ReflectionCache.ApiClientProperty.GetValue(client);
//         if (apiClient == null)
//             throw new InvalidOperationException("反射: ApiClient 为 null");

//         var taskResult = ReflectionCache.GetActiveThreadsMethod
//             .Invoke(apiClient, new object?[] { guild.Id, options });

//         if (taskResult == null)
//             throw new InvalidOperationException("反射: GetActiveThreadsAsync 返回 null");

//         await (Task)taskResult;

//         var resultProperty = taskResult.GetType().GetProperty("Result");
//         if (resultProperty == null)
//             throw new InvalidOperationException("反射: 无法获取 Result 属性");

//         var result = resultProperty.GetValue(taskResult);
//         if (result == null)
//             throw new InvalidOperationException("反射: Result 为 null");

//         var threadsProperty = result.GetType().GetProperty("Threads");
//         if (threadsProperty == null)
//             throw new InvalidOperationException("反射: 无法获取 Threads 属性");

//         var threads = threadsProperty.GetValue(result) as IEnumerable<object>;
//         if (threads == null)
//             return ImmutableArray<RestThreadChannel>.Empty;

//         return threads
//             .Select(model =>
//             {
//                 var threadChannel = ReflectionCache.CreateThreadChannelMethod
//                     .Invoke(null, new object?[] { client, guild, model }) as RestThreadChannel;
//                 return threadChannel!;
//             })
//             .ToImmutableArray();
//     }
// }


// // ======= 禁用 nullable =======
// // using Discord;
// // using Discord.Rest;
// // using System.Collections.Immutable;
// // using System.Reflection;

// // namespace Lunarium.Bot.Archive;

// // #nullable disable warnings

// // public static class DiscordRestClientExtensions
// // {
// //     private static class ReflectionCache
// //     {
// //         public static readonly PropertyInfo ApiClientProperty;
// //         public static readonly MethodInfo GetActiveThreadsMethod;
// //         public static readonly MethodInfo CreateThreadChannelMethod;

// //         static ReflectionCache()
// //         {
// //             // 缓存 ApiClient 属性
// //             ApiClientProperty = typeof(DiscordRestClient).BaseType
// //                 ?.GetProperty("ApiClient", BindingFlags.NonPublic | BindingFlags.Instance);

// //             if (ApiClientProperty != null)
// //             {
// //                 var apiClientType = ApiClientProperty.PropertyType;
                
// //                 // 缓存 GetActiveThreadsAsync 方法
// //                 GetActiveThreadsMethod = apiClientType
// //                     .GetMethod("GetActiveThreadsAsync", BindingFlags.Public | BindingFlags.Instance);
// //             }

// //             // 缓存 RestThreadChannel.Create 方法
// //             CreateThreadChannelMethod = typeof(RestThreadChannel)
// //                 .GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
// //         }
// //     }

// //     public static async Task<IReadOnlyCollection<RestThreadChannel>> GetAllActiveThreadsAsync(
// //         this DiscordRestClient client,
// //         IGuild guild,
// //         RequestOptions options = null)
// //     {
// //         // 使用缓存的反射信息
// //         var apiClient = ReflectionCache.ApiClientProperty.GetValue(client);
        
// //         var taskResult = ReflectionCache.GetActiveThreadsMethod
// //             .Invoke(apiClient, new object[] { guild.Id, options });
        
// //         await (Task)taskResult;
        
// //         var result = taskResult.GetType().GetProperty("Result").GetValue(taskResult);
// //         var threads = (IEnumerable<object>)result.GetType().GetProperty("Threads").GetValue(result);

// //         return threads
// //             .Select(model => (RestThreadChannel)ReflectionCache.CreateThreadChannelMethod
// //                 .Invoke(null, new object[] { client, guild, model }))
// //             .ToImmutableArray();
// //     }
// // }

// // #nullable restore