// NuGet 包依赖
using Microsoft.Data.Sqlite;
using Dapper;
using Discord;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Model;
using LunariumBot.Net.Extensions;

namespace LunariumBot.Net.SearchBot.Database;

public partial class DatabaseService
{
    // === SQL 模板定义===

    // --- 基础实体表 ---
    // SQL模板: 插入或更新频道表 (INSERT OR ON CONFLICT(...) DO UPDATE SET)
    const string channelsUpsertSqlCommand = """
        INSERT INTO channels (channel_id, channel_name) 
        VALUES (@Id, @Name)
        ON CONFLICT(channel_id) DO UPDATE SET
            channel_name = excluded.channel_name;
        """;
    // SQL模板: 插入或更新标签表 (INSERT OR ON CONFLICT(...) DO UPDATE SET)
    const string tagsUpsertSqlCommand = """
        INSERT INTO tags (tag_id, channel_id, tag_name, channel_name, guild_name)
        VALUES (@Id, @ChannelId, @Name, @ChannelName, @GuildName)
        ON CONFLICT(tag_id) DO UPDATE SET
            channel_id = excluded.channel_id,
            tag_name = excluded.tag_name,
            channel_name = excluded.channel_name,
            guild_name = excluded.guild_name;
        """;

    // --- 核心内容表 ---
    // SQL模板: 插入或更新主表 (UPSERT)
    const string mainUpsertSqlCommand = """
        INSERT INTO posts_main (
            thread_id, first_message_id, author_id, channel_id, channel_name, guild_id, 
            guild_name, created_at, member_count, reaction_count, reply_count, 
            last_active_at, jump_url, attachment_url, tags, title, content
        ) 
        VALUES (
            @ThreadId, @FirstMessageId, @AuthorId, @ChannelId, @ChannelName, @GuildId, 
            @GuildName, @CreatedAt, @MemberCount, @ReactionCount, @ReplyCount, 
            @LastActiveAt, @JumpUrl, @AttachmentUrl, @Tags, @Title, @Content
        )
        ON CONFLICT(thread_id) DO UPDATE SET
            first_message_id = excluded.first_message_id,
            author_id = excluded.author_id,
            channel_id = excluded.channel_id,
            channel_name = excluded.channel_name,
            guild_id = excluded.guild_id,
            guild_name = excluded.guild_name,
            created_at = excluded.created_at,
            member_count = excluded.member_count,
            reaction_count = excluded.reaction_count,
            reply_count = excluded.reply_count,
            last_active_at = excluded.last_active_at,
            jump_url = excluded.jump_url,
            attachment_url = excluded.attachment_url,
            tags = excluded.tags,
            title = excluded.title,
            content = excluded.content;
        """;

    // SQL模板: 插入或更新帖子-标签关系表 (INSERT OR IGNORE)
    const string postTagsJunctionUpsertSqlCommand = """
        INSERT INTO post_tags_junction (thread_id, tag_id, channel_id)
        VALUES (@ThreadId, @TagId, @ChannelId)
        ON CONFLICT(thread_id, tag_id) DO UPDATE SET
            channel_id = excluded.channel_id;
        """;
    // --- 关系/连接表 ---

    // SQL模板: 插入或忽略用户收藏和订阅的帖子后备表 (INSERT OR IGNORE)
    const string fallbackPostsUpsertSqlCommand = """
        INSERT INTO fallback_posts (thread_id, title, author_id, jump_url)
        VALUES (@ThreadId, @Title, @AuthorId, @JumpUrl)
        ON CONFLICT(thread_id) DO UPDATE SET
            title = excluded.title,
            author_id = excluded.author_id,
            jump_url = excluded.jump_url
        """;

    // --- 全文搜索 (FTS5) 虚拟表 ---
    // SQL模板: 插入或更新 FTS 索引表 (DELETE + INSERT, 虚拟表不能 UPSERT)
    const string ftsUpsertSql = """
        DELETE FROM posts_fts WHERE rowid = @ThreadId;
        INSERT INTO posts_fts (rowid, post_title, post_content) 
        VALUES (@ThreadId, @TitleFts, @ContentFts);
        """;

    // SQL模板: 更新主表中帖子的作品更新信息
    const string mainUpdateInfoSqlCommand = """
        UPDATE posts_main
        SET
            update_at = @UpdatedAt,
            message_jump_url = @MessageJumpUrl
        WHERE
            thread_id = @ThreadId;
        """;
    // SQL模板: 根据 Thread ID 列表批量删除帖子-标签关系
    const string deletePostTagsJunctionByThreadIdsSql = "DELETE FROM post_tags_junction WHERE thread_id IN @ThreadIds;";
    
    /// <summary>
    /// 事务性地批量插入或更新帖子及其关联数据。
    /// </summary>
    /// <param name="dataList">待处理的帖子数据列表。</param>
    public async Task<(List<ThreadDataModel>?, InsertPostResult?)> InsertPostAsync(List<ThreadDataModel> dataList)
    {
        // 校验: 若无数据则提前返回, 避免空操作
        if (dataList == null || !dataList.Any())
        {
            _logger.Warning("无帖子数据需要插入, 操作跳过。");
            return (null, null);
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // 步骤 1: 批量插入或更新主表 (posts_main)
                int mainAffectedRows = await UpsertMainPostsAsync(connection, transaction, dataList);

                // 步骤 2: 批量重建 FTS 索引 (posts_fts)
                int ftsAffectedRows = await RebuildFtsIndexAsync(connection, transaction, dataList);

                // 步骤 3: 同步帖子与标签的关系
                (int deleted, int inserted) = await SyncPostTagsJunctionAsync(connection, transaction, dataList);
                
                // 成功: 提交事务
                await transaction.CommitAsync();
                _logger.Info("事务成功提交, {dataList.Count} 条帖子数据处理完毕。", dataList.Count);
                
                var insertPostResult = new InsertPostResult
                {
                    MainAffectedRows = mainAffectedRows,
                    FtsAffectedRows = ftsAffectedRows,
                    TagJunctionAffectedRows = inserted
                };
                
                // 返回: 已处理的列表
                return (dataList, insertPostResult);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error("批量处理帖子时发生错误, 事务已回滚。", ex);
                return (null, null);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("InsertPostAsync (Outer) 发生数据库连接或事务启动错误。", ex);
            return (null, null);
        }
    }

    /// <summary>
    /// 私有辅助方法: 批量插入或更新主表 (posts_main)。
    /// </summary>
    private async Task<int> UpsertMainPostsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, List<ThreadDataModel> dataList)
    {
        int affectedRows = await connection.ExecuteAsync(mainUpsertSqlCommand, dataList, transaction);
        _logger.Info("处理 {dataList.Count} 条帖子, 影响主表 {affectedRows} 行。", dataList.Count, affectedRows);
        return affectedRows;
    }

    /// <summary>
    /// 私有辅助方法: 批量重建 FTS 索引 (posts_fts)。
    /// </summary>
    private async Task<int> RebuildFtsIndexAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, List<ThreadDataModel> dataList)
    {
        var ftsData = dataList.Select(data => new
        {
            data.ThreadId,
            TitleFts = _tokenizer.CutForSearch(data.Title),   // 分词: 标题
            ContentFts = _tokenizer.CutForSearch(data.Content) // 分词: 内容
        }).ToList();

        int affectedRows = await connection.ExecuteAsync(ftsUpsertSql, ftsData, transaction);
        _logger.Info("处理 {ftsData.Count} 条FTS数据, 影响FTS表 {affectedRows} 行。", ftsData.Count, affectedRows);
        return affectedRows;
    }

    /// <summary>
    /// 私有辅助方法: 同步帖子与标签的关系（先删后增）。
    /// </summary>
    /// <returns>返回一个元组，包含删除的旧关系行数和插入的新关系行数。</returns>
    private async Task<(int DeletedRows, int InsertedRows)> SyncPostTagsJunctionAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, List<ThreadDataModel> dataList)
    {
        // 步骤 3a: 为本次更新的所有帖子创建一个“干净”的标签环境
        var threadIdsToUpdate = dataList.Select(d => d.ThreadId).ToList();
        int deletedRows = 0;
        if (threadIdsToUpdate.Any())
        {
            deletedRows = await connection.ExecuteAsync(deletePostTagsJunctionByThreadIdsSql, new { ThreadIds = threadIdsToUpdate }, transaction);
            _logger.Info("为 {threadIdsToUpdate.Count} 个帖子清空旧标签关系, 删除了 {deletedRows} 条旧记录。", threadIdsToUpdate.Count, deletedRows);
        }

        // 准备: 展平数据, 提取所有帖子与标签的关联关系
        var junctionData = dataList.SelectMany(data =>
            data.TagsId
                .Where(tagId => tagId > 0) // 过滤: 确保标签ID有效
                .Select(tagId => new { ThreadId = data.ThreadId, TagId = tagId, ChannelId = data.ChannelId })
        ).ToList();

        if (!junctionData.Any())
        {
            _logger.Info("没有发现任何帖子-标签关系需要处理。");
            return (deletedRows, 0);
        }

        // 步骤 3b: 验证标签是否存在于数据库中
        var allTagIdsInBatch = junctionData.Select(j => j.TagId).Distinct().ToList();
        const string existingTagIdsSql = "SELECT tag_id FROM tags WHERE tag_id IN @TagIds;";
        var existingTagIds = (await connection.QueryAsync<ulong>(existingTagIdsSql, new { TagIds = allTagIdsInBatch }, transaction)).ToHashSet();

        // 步骤 3c: 过滤出有效的关系并记录无效关系
        var validJunctionData = junctionData.Where(j => existingTagIds.Contains(j.TagId)).ToList();
        
        // --- 日志记录无效标签 ---
        var invalidTagIds = allTagIdsInBatch.Where(id => !existingTagIds.Contains(id)).ToList();
        if (invalidTagIds.Any())
        {
            _logger.Warning("发现 {invalidTagIds.Count} 个无效标签ID (可能已被删除): [{invalidTagIds}]", invalidTagIds.Count, string.Join(", ", invalidTagIds.OrderBy(id => id)));
            
            var invalidJunctionData = junctionData.Where(j => !existingTagIds.Contains(j.TagId)).ToList();
            var invalidPostsGrouped = invalidJunctionData.GroupBy(j => j.TagId);
            
            foreach (var group in invalidPostsGrouped)
            {
                var affectedPosts = dataList
                    .Where(post => group.Select(j => j.ThreadId).Contains(post.ThreadId))
                    .OrderBy(post => post.ThreadId);

                _logger.Warning("无效标签 {group.Key} 被以下 {affectedPosts.Count()} 个帖子引用:", group.Key, affectedPosts.Count());
                foreach (var post in affectedPosts)
                {
                    _logger.Warning("  └─ 帖子ID: {post.ThreadId}, 标题: \"{post.Title}\", 链接: {post.JumpUrl}", post.ThreadId, post.Title.Slice(50, "..."), post.JumpUrl);
                }
            }
        }
        // --- 日志记录结束 ---

        // 步骤 3d: 批量插入所有新的、有效的帖子-标签关系
        int insertedRows = 0;
        if (validJunctionData.Any())
        {
            insertedRows = await connection.ExecuteAsync(postTagsJunctionUpsertSqlCommand, validJunctionData, transaction);
            _logger.Info("处理 {validJunctionData.Count} 条新的帖子-标签关系, 影响关系表 {insertedRows} 行。", validJunctionData.Count, insertedRows);
        }
        else
        {
            _logger.Info("所有待处理的标签关系均无效, 跳过关系表插入。");
        }
        
        return (deletedRows, insertedRows);
    }
    
    /// <summary>
    /// 批量更新(插入或更新)论坛标签。
    /// </summary>
    /// <param name="tagDict">待处理的标签字典，键为频道，值为该频道的标签列表。</param>
    public async Task InsertTagsAsync(Dictionary<IForumChannel, List<IForumTag>> tagDict)
    {
        try
        {
            // 校验: 若无字典数据则提前返回
            if (tagDict == null || !tagDict.Any())
            {
                _logger.Warning("无标签字典数据需要插入, 操作跳过。");
                return;
            }

            // 展平所有标签并进行过滤和转换
            var allTagData = new List<object>();
            int totalTagCount = 0;
            int validTagCount = 0;

            foreach (var kvp in tagDict)
            {
                var channel = kvp.Key;
                var tags = kvp.Value;

                totalTagCount += tags.Count;

                // 过滤: 移除ID为空的无效标签
                var validTags = tags.Where(t => t.Id.HasValue).ToList();
                validTagCount += validTags.Count;

                // 转换标签数据，包含频道与服务器信息
                var channelTagData = validTags.Select(t => new
                {
                    Id = t.Id,
                    Name = t.Name,
                    ChannelId = channel.Id,
                    ChannelName = channel.Name,
                    GuildName = channel.Guild.Name
                });

                allTagData.AddRange(channelTagData);
            }

            _logger.Info("处理 {totalTagCount} 个标签, 有效标签 {validTagCount} 个。", totalTagCount, validTagCount);

            // 校验: 若无有效标签则提前返回
            if (!allTagData.Any())
            {
                _logger.Warning("无有效标签数据需要插入, 操作跳过。");
                return;
            }

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 执行: 批量插入或更新标签
            int affectedRows = await connection.ExecuteAsync(tagsUpsertSqlCommand, allTagData);
            _logger.Info("处理 {validTagCount} 个标签, 成功插入或更新 {affectedRows} 个新标签。", validTagCount, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.Error("InsertTagsAsync Ex:", ex);
        }
    }

    /// <summary>
    /// 批量更新(插入或更新)频道。
    /// </summary>
    /// <param name="channelList">待处理的频道数据列表。</param>
    public async Task InsertChannelsAsync(List<IForumChannel> channelList)
    {
        try
        {
            // 校验: 若无频道数据则提前返回
            if (channelList == null || !channelList.Any())
            {
                _logger.Warning("无频道数据需要插入, 操作跳过。");
                return;
            }

            // 准备: 转换数据格式, 拼接服务器与频道名
            var channelData = channelList.Select(c => new { c.Id, Name = $"{c.Guild.Name}.{c.Name}" });

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 执行: 批量插入或更新频道
            int affectedRows = await connection.ExecuteAsync(channelsUpsertSqlCommand, channelData);
            _logger.Info("处理 {channelList.Count} 个频道, 成功插入或更新 {affectedRows} 个新频道。", channelList.Count, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.Error("InsertChannelsAsync Ex:", ex);
        }
    }

    public async Task UpdatePostUpdateInfoAsync(ulong threadId, IMessage message)
    {
        try
        {
            // 准备: 构造更新所需的数据
            var updateData = new
            {
                ThreadId = threadId,
                UpdatedAt = message.CreatedAt.ToUnixTimeSeconds(),
                MessageJumpUrl = message.GetJumpUrl()
            };

            // 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            
            // 执行: 更新主表记录
            int affectedRows = await connection.ExecuteAsync(mainUpdateInfoSqlCommand, updateData);

            if (affectedRows > 0)
            {
                _logger.Info("成功更新帖子 {threadId} 的作品更新信息。", threadId);
            }
            else
            {
                _logger.Info("尝试更新帖子 {threadId} 的作品信息, 但主表中未找到记录 (可能尚未同步), 跳过。", threadId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "更新帖子 {threadId} 的作品更新信息时发生错误。", threadId);
        }
    }
}