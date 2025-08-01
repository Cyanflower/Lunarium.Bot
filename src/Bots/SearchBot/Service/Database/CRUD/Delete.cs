using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Dapper;

namespace SearchBot.Database;

public partial class DatabaseService 
{
    // === SQL 模板定义 ===
    // SQL模板: 基于ID列表删除帖子
    const string postsDeleteSqlCommand = "DELETE FROM posts_main WHERE thread_id IN @PostIds;";
    // SQL模板: 基于ID列表删除标签
    const string tagsDeleteSqlCommand = "DELETE FROM tags WHERE tag_id IN @TagIds;";
    // SQL模板: 基于ID列表删除频道
    const string channelsDeleteSqlCommand = "DELETE FROM channels WHERE channel_id IN @ChannelIds;";
    // SQL模板: 删除单条用户收藏记录
    const string favoriteDeleteOneSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId AND thread_id = @ThreadId;";
    // SQL模板: 删除用户所有收藏记录
    const string favoriteDeleteAllByUserSqlCommand = "DELETE FROM user_favorite_posts WHERE user_id = @UserId;";
    // SQL模板: 删除单条用户订阅记录
    const string subscriptionDeleteOneSqlCommand = "DELETE FROM user_subscriptions_post WHERE user_id = @UserId AND thread_id = @ThreadId;";
    // SQL模板: 删除用户所有订阅记录
    const string subscriptionDeleteAllByUserSqlCommand = "DELETE FROM user_subscriptions_post WHERE user_id = @UserId;";
    // SQL模板: 删除用户收藏和订阅的帖子后备表
    const string FavoriteAndSubscriptionPostsDeleteSqlCommand = "DELETE FROM user_favorite_and_subscription_posts WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户收藏的帖子
    const string userFavoritePostsDeleteSqlCommand = "DELETE FROM user_favorite_posts WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户订阅的帖子
    const string userSubscriptionsPostDeleteSqlCommand = "DELETE FROM user_subscriptions_post WHERE thread_id IN @ThreadIds;";
    // SQL模板: 删除用户待推送的帖子
    const string userPushListDeleteSqlCommand = "DELETE FROM user_push_list WHERE thread_id IN @ThreadIds;";
    
    /// <summary>
    /// 批量移除帖子及其关联数据 (帖子对象列表)。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="threadChannelList">要移除的帖子对象列表。</param>
    /// <returns>数据库成功更改的行数</returns>
    public async Task<int> RemovePostAsync(List<IThreadChannel> threadChannelList)
    {
        // 校验: 若无数据则提前返回
        if (threadChannelList == null || !threadChannelList.Any())
        {
            _logger.LogWarning("无帖子数据需要移除, 操作跳过。");
            return 0;
        }

        // 准备: 提取所有帖子的ID
        var postIds = threadChannelList.Select(t => t.Id).ToList();
        
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        // 开始: 数据库事务
        await using var transaction = await connection.BeginTransactionAsync();

        int affectedRows = 0;

        try
        {
            // 执行: Dapper 批量删除
            // Dapper 会自动将 @PostIds 展开为 IN 子句所需的参数列表
            affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);

            // 成功: 提交事务
            await transaction.CommitAsync();
            _logger.LogInformation("成功移除 {AffectedRows} 条帖子及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, postIds.Count);
        }
        catch (Exception ex)
        {
            // 失败: 回滚事务
            await transaction.RollbackAsync();
            _logger.LogError(ex, "批量删除帖子时发生错误, 事务已回滚。");
            return 0;
        }
        return affectedRows;
    }
    /// <summary>
    /// 批量移除帖子及其关联数据 (帖子ID列表)。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="postIds">要移除的帖子ID列表</param>
    /// <returns>数据库成功更改的行数</returns>
    public async Task<int> RemovePostAsync(List<ulong> postIds)
    {
        // 校验: 若无数据则提前返回
        if (postIds == null || !postIds.Any())
        {
            _logger.LogWarning("无帖子数据需要移除, 操作跳过。");
            return 0;
        }

        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        // 开始: 数据库事务
        await using var transaction = await connection.BeginTransactionAsync();

        int affectedRows;

        try
        {
            // 执行: Dapper 批量删除
            // Dapper 会自动将 @PostIds 展开为 IN 子句所需的参数列表
            affectedRows = await connection.ExecuteAsync(postsDeleteSqlCommand, new { PostIds = postIds }, transaction);

            // 成功: 提交事务
            await transaction.CommitAsync();
            _logger.LogInformation("成功移除 {AffectedRows} 条帖子及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, postIds.Count);
        }
        catch (Exception ex)
        {
            // 失败: 回滚事务
            await transaction.RollbackAsync();
            _logger.LogError(ex, "批量删除帖子时发生错误, 事务已回滚。");
            throw;
        }
        return affectedRows;
    }
    
    /// <summary>
    /// 批量移除标签及其关联数据。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="tagList">要移除的标签列表。</param>
    /// <returns>服务实例。</returns>
    public async Task<DatabaseService> RemoveTagAsync(List<IForumTag> tagList)
    {
        // 过滤: 移除ID为空的无效标签
        var validTagIds = tagList?
            .Where(t => t.Id.HasValue)
            .Select(t => t.Id!.Value)
            .ToList();

        // 校验: 若无有效标签则提前返回
        if (validTagIds == null || !validTagIds.Any())
        {
            _logger.LogWarning("无有效标签数据需要移除, 操作跳过。");
            return this;
        }
        
        // 调用已优化的基于ID列表的重载方法, 避免代码重复
        return await RemoveTagAsync(validTagIds);
    }
    
    /// <summary>
    /// 批量移除标签及其关联数据。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="tagIdList">要移除的标签ID列表。</param>
    /// <returns>服务实例。</returns>
    public async Task<DatabaseService> RemoveTagAsync(List<ulong> tagIdList)
    {
        // 校验: 若无数据则提前返回
        if (tagIdList == null || !tagIdList.Any())
        {
            _logger.LogWarning("无标签ID需要移除, 操作跳过。");
            return this;
        }

        // SQL: 基于ID列表删除标签表记录 (已移至类常量)

        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        // 执行: Dapper 批量删除
        int affectedRows = await connection.ExecuteAsync(tagsDeleteSqlCommand, new { TagIds = tagIdList });
        
        _logger.LogInformation("成功移除 {AffectedRows} 个标签及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, tagIdList.Count);
        return this;
    }

    /// <summary>
    /// 批量移除频道及其关联数据。
    /// 依赖 ON DELETE CASCADE 自动清理关系表。
    /// </summary>
    /// <param name="channelList">要移除的频道列表。</param>
    /// <returns>服务实例。</returns>
    public async Task<DatabaseService> RemoveChannelAsync(List<IChannel> channelList)
    {
        // 校验: 若无数据则提前返回
        if (channelList == null || !channelList.Any())
        {
            _logger.LogWarning("无频道数据需要移除, 操作跳过。");
            return this;
        }

        // 准备: 提取所有频道的ID
        var channelIds = channelList.Select(c => c.Id).ToList();

        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        // 执行: Dapper 批量删除
        int affectedRows = await connection.ExecuteAsync(channelsDeleteSqlCommand, new { ChannelIds = channelIds });
        
        _logger.LogInformation("成功移除 {AffectedRows} 个频道及其关联数据 (处理了 {TotalCount} 个ID)。", affectedRows, channelIds.Count);
        return this;
    }

    /// <summary>
    /// 移除指定用户的某一个帖子收藏。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="threadId">帖子ID</param>
    /// <returns>服务实例</returns>
    public async Task<DatabaseService> RemoveFavoriteAsync(ulong userId, ulong threadId)
    {
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        
        // 执行: 基于复合主键删除单条收藏记录
        int affectedRows = await connection.ExecuteAsync(favoriteDeleteOneSqlCommand, new { UserId = userId, ThreadId = threadId });
        
        _logger.LogInformation("用户 {UserId} 的帖子 {ThreadId} 收藏已移除, 影响行数: {AffectedRows}。", userId, threadId, affectedRows);
        return this;
    }

    /// <summary>
    /// 清空指定用户的所有帖子收藏。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>服务实例</returns>
    public async Task<DatabaseService> ClearFavoritesAsync(ulong userId)
    {
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        
        // 执行: 基于用户ID删除其所有收藏记录
        int affectedRows = await connection.ExecuteAsync(favoriteDeleteAllByUserSqlCommand, new { UserId = userId });
        
        _logger.LogInformation("已清空用户 {UserId} 的所有帖子收藏, 共移除 {AffectedRows} 条记录。", userId, affectedRows);
        return this;
    }

    /// <summary>
    /// 移除指定用户的某一个帖子订阅。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="threadId">帖子ID</param>
    /// <returns>服务实例</returns>
    public async Task<DatabaseService> RemoveSubscriptionAsync(ulong userId, ulong threadId)
    {
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);

        // 执行: 基于复合主键删除单条订阅记录
        int affectedRows = await connection.ExecuteAsync(subscriptionDeleteOneSqlCommand, new { UserId = userId, ThreadId = threadId });
        
        _logger.LogInformation("用户 {UserId} 的帖子 {ThreadId} 订阅已移除, 影响行数: {AffectedRows}。", userId, threadId, affectedRows);
        return this;
    }

    /// <summary>
    /// 清空指定用户的所有帖子订阅。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>服务实例</returns>
    public async Task<DatabaseService> ClearSubscriptionsAsync(ulong userId)
    {
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        
        // 执行: 基于用户ID删除其所有订阅记录
        int affectedRows = await connection.ExecuteAsync(subscriptionDeleteAllByUserSqlCommand, new { UserId = userId });
        
        _logger.LogInformation("已清空用户 {UserId} 的所有帖子订阅, 共移除 {AffectedRows} 条记录。", userId, affectedRows);
        return this;
    }

    /// <summary>
    /// 批量移除用户收藏和订阅的帖子后备表
    /// </summary>
    /// <param name="threadIds">要移除的帖子ID列表</param>
    /// <returns>服务实例</returns>
    public async Task<DatabaseService> RemoveFavoriteAndSubscriptionPostsAsync(List<ulong> threadIds)
    {
        // 准备: 创建数据库连接
        using var connection = new SqliteConnection(_connectionString);
        // 执行: 删除用户收藏的帖子
        await connection.ExecuteAsync(userFavoritePostsDeleteSqlCommand, new { ThreadIds = threadIds });
        // 执行: 删除用户订阅的帖子
        await connection.ExecuteAsync(userSubscriptionsPostDeleteSqlCommand, new { ThreadIds = threadIds });
        // 执行: 删除用户待推送的帖子
        await connection.ExecuteAsync(userPushListDeleteSqlCommand, new { ThreadIds = threadIds });
        // 执行: 基于ID列表删除后备表记录
        int affectedRows = await connection.ExecuteAsync(FavoriteAndSubscriptionPostsDeleteSqlCommand, new { ThreadIds = threadIds });
        _logger.LogInformation("成功移除 {AffectedRows} 个用户收藏和订阅的帖子后备表 (处理了 {TotalCount} 个ID)。", affectedRows, threadIds.Count);
        return this;
    }

    /// <summary>
    /// 清理数据库中不再存在于当前配置中的频道和标签
    /// </summary>
    /// <param name="validForumChannels">当前有效的论坛频道列表</param>
    /// <param name="allTags">当前所有标签列表</param>
    public async Task CleanupObsoleteDataAsync(List<IForumChannel> validForumChannels, List<IForumTag> allValidTags)
    {
        _logger.LogInformation("开始清理过时的频道和标签数据...");

        // 准备: 创建数据库连接并开启事务
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // === 1. 清理过时频道 ===
            
            // 准备: 获取当前有效频道ID集合
            var currentChannelIds = validForumChannels.Select(c => c.Id).ToHashSet();
            
            // 查询: 从数据库获取所有频道ID
            var dbChannelIds = await connection.QueryAsync<ulong>("SELECT channel_id FROM channels;", transaction: transaction);
            
            // 计算: 找出待删除的过时频道ID
            var channelsToDelete = dbChannelIds.Where(id => !currentChannelIds.Contains(id)).ToList();

            if (channelsToDelete.Any())
            {
                // 执行: 批量删除过时频道 (ON DELETE CASCADE将自动清理其帖子和标签)
                int affectedRows = await connection.ExecuteAsync(channelsDeleteSqlCommand, new { ChannelIds = channelsToDelete }, transaction);
                _logger.LogInformation("成功清理 {Count} 个过时的频道。", affectedRows);
            }
            else
            {
                _logger.LogInformation("无过时频道需要清理。");
            }

            // === 2. 清理过时标签 ===

            // 准备: 获取当前有效标签ID集合
            var currentTagIds = allValidTags
                .Where(t => t.Id.HasValue) // 确保标签ID存在
                .Select(t => t.Id!.Value) // 安全地获取值
                .ToHashSet();
            
            // 查询: 从数据库获取所有标签ID
            var dbTagIds = await connection.QueryAsync<ulong>("SELECT tag_id FROM tags;", transaction: transaction);

            // 计算: 找出待删除的过时标签ID
            var tagsToDelete = dbTagIds.Where(id => !currentTagIds.Contains(id)).ToList();

            if (tagsToDelete.Any())
            {
                // 执行: 批量删除过时标签 (ON DELETE CASCADE将自动清理其与帖子的关系)
                int affectedRows = await connection.ExecuteAsync(tagsDeleteSqlCommand, new { TagIds = tagsToDelete }, transaction);
                _logger.LogInformation("成功清理 {Count} 个过时的标签。", affectedRows);
            }
            else
            {
                _logger.LogInformation("无过时标签需要清理。");
            }

            // 成功: 提交事务
            await transaction.CommitAsync();
            _logger.LogInformation("数据清理完成。保留频道 {ChannelCount} 个, 标签 {TagCount} 个。", 
                currentChannelIds.Count, currentTagIds.Count);
        }
        catch (Exception ex)
        {
            // 失败: 回滚事务
            await transaction.RollbackAsync();
            _logger.LogError(ex, "清理过时数据时发生错误, 事务已回滚。");
            // 清理失败不影响主流程, 按设计不重抛异常
        }
    }
}