// // NuGet 包依赖
// using Microsoft.Data.Sqlite;
// using Discord;
// using Dapper;

// // 解决方案内引用项目
// using LunariumBot.Net.Logger;

// // 项目内命名空间
// using LunariumBot.Net.SearchBot.Models;

// namespace LunariumBot.Net.SearchBot.Database;

// public partial class DatabaseService
// {
//     // SQL模板: 插入或忽略用户订阅帖子关系表 (INSERT OR IGNORE)
//     const string userSubscriptionsPostUpsertSqlCommand = """
//         INSERT OR IGNORE INTO user_subscriptions_post (user_id, thread_id, added_at)
//         VALUES (@UserId, @ThreadId, @AddedAt);
//         """;
//     // SQL模板: 插入或忽略用户推送列表关系表 (INSERT OR IGNORE)
//     const string userPushListUpsertSqlCommand = """
//         INSERT INTO user_push_list (user_id, thread_id, message_jump_url, updated_at)
//         VALUES (@UserId, @ThreadId, @MessageJumpUrl, @UpdatedAt)
//         ON CONFLICT(user_id, thread_id) DO UPDATE SET
//             message_jump_url = excluded.message_jump_url,
//             updated_at = excluded.updated_at;
//         """;

//     /// <summary>
//     /// 更新(插入或更新)单个用户的帖子订阅。
//     /// </summary>
//     /// <param name="user">待处理的用户。</param>
//     /// <param name="thread">待处理的帖子对象。</param>
//     public async Task InsertUserSubscriptionPostAsync(IUser user, IThreadChannel thread)
//     {
//         try
//         {
//             // 校验: 用户或帖子对象为空
//             if (user == null || thread == null)
//             {
//                 _logger.Warning("用户或帖子对象为空, 操作跳过。");
//                 return;
//             }
//             var threadFallbackData = new
//             {
//                 ThreadId = thread.Id,
//                 Title = thread.Name,
//                 AuthorId = thread.OwnerId,
//                 JumpUrl = $"https://discord.com/channels/{thread.Guild.Id}/{thread.Id}/0",
//             };
//             // 准备: 构造订阅关系数据
//             var subscriptionData = new { UserId = user.Id, ThreadId = thread.Id, AddedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

//             // 创建数据库连接
//             using var connection = new SqliteConnection(_connectionString);
//             // 更新对应的帖子后备表
//             await connection.ExecuteAsync(fallbackPostsUpsertSqlCommand, threadFallbackData);
//             // 执行: 更新订阅关系
//             int affectedRows = await connection.ExecuteAsync(userSubscriptionsPostUpsertSqlCommand, subscriptionData);
//             _logger.Debug("更新用户 {user.Id} 的帖子 {thread.Id} 订阅, 影响 {affectedRows} 行。", user.Id, thread.Id, affectedRows);
//         }
//         catch (Exception ex)
//         {
//             _logger.Error("更新(插入或更新)单个用户的帖子订阅出错", ex);
//         }
//     }

//     /// <summary>
//     /// 批量更新(插入或更新)帖子到用户的推送列表。
//     /// </summary>
//     /// <param name="userIds">订阅了该帖子的用户列表</param>
//     /// <param name="thread">帖子对象</param>
//     /// <param name="message">作者发布的附带PNG/JSON附件的消息对象</param>
//     /// <returns>实例自身</returns>
//     public async Task InsertUserPushListAsync(List<ulong> userIds, IThreadChannel thread, IMessage message)
//     {
//         try
//         {
//             // 校验: 用户或帖子对象为空
//             if (userIds == null || thread == null)
//             {
//                 _logger.Warning("用户或帖子对象为空, 操作跳过。");
//                 return;
//             }
//             // 准备: 构造推送关系数据
//             var pushData = userIds.Select(u => new { UserId = u, ThreadId = thread.Id, MessageJumpUrl = message.GetJumpUrl(), UpdatedAt = message.CreatedAt.ToUnixTimeSeconds() });

//             // 创建数据库连接
//             using var connection = new SqliteConnection(_connectionString);
//             // 执行: 更新推送关系
//             int affectedRows = await connection.ExecuteAsync(userPushListUpsertSqlCommand, pushData);
//             _logger.Debug("更新 {userIds.Count} 个用户的帖子 {thread.Name}({thread.Id}) 推送, 影响 {affectedRows} 行。", userIds.Count, thread.Name, thread.Id, affectedRows);
//         }
//         catch (Exception ex)
//         {
//             _logger.Error("批量更新(插入或更新)帖子到用户的推送列表出错", ex);
//         }
//     }
// }