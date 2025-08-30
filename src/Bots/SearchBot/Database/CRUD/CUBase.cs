using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

using Dapper;
using Discord;

using SearchBot.Model;

namespace SearchBot.Database;

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

    /// <summary>
    /// 事务性地批量插入或更新帖子及其关联数据(FTS索引、标签关系)。
    /// </summary>
    /// <param name="dataList">待处理的帖子数据列表。</param>
    public async Task<(List<ThreadDataModel>?, InsertPostResult?)> InsertPostAsync(List<ThreadDataModel> dataList)
    {
        try
        {
            // 校验: 若无数据则提前返回, 避免空操作
            if (dataList == null || !dataList.Any())
            {
                _logger.LogWarning("无帖子数据需要插入, 操作跳过。");
                return (null, null);
            }

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 手动管理连接: 打开连接以在单个事务中执行多个命令
            await connection.OpenAsync();

            // 开始: 数据库事务, 确保操作的原子性
            await using var transaction = await connection.BeginTransactionAsync();

            int mainAffectedRows;
            int ftsAffectedRows;
            int tagJunctionAffectedRows = 0;

            try
            {
                // 步骤1: 批量插入或更新主表 (posts_main)
                mainAffectedRows = await connection.ExecuteAsync(mainUpsertSqlCommand, dataList, transaction);
                _logger.LogInformation("处理 {TotalCount} 条帖子, 影响主表 {AffectedRows} 行。", dataList.Count, mainAffectedRows);

                // 准备: 转换帖子数据为 FTS 索引格式 (执行分词)
                var ftsData = dataList.Select(data => new
                {
                    data.ThreadId,
                    TitleFts = _tokenizer.CutForSearch(data.Title),   // 分词: 标题
                    ContentFts = _tokenizer.CutForSearch(data.Content) // 分词: 内容
                }).ToList();

                // 步骤2: 批量重建 FTS 索引 (posts_fts)
                ftsAffectedRows = await connection.ExecuteAsync(ftsUpsertSql, ftsData, transaction);
                _logger.LogInformation("处理 {TotalCount} 条FTS数据, 影响FTS表 {AffectedRows} 行。", ftsData.Count, ftsAffectedRows);

                // 准备: 展平数据, 提取所有帖子与标签的关联关系
                var junctionData = dataList.SelectMany(data =>
                    data.TagsId
                        .Where(tagId => tagId > 0) // 过滤: 确保标签ID有效 (Discord ID 不会为0)
                        .Select(tagId => new { ThreadId = data.ThreadId, TagId = tagId , ChannelId = data.ChannelId})
                ).ToList();

                // 校验: 仅在存在标签关系时执行插入
                if (junctionData.Any())
                {
                    // 步骤3a: 获取数据库中现有的标签ID，避免外键约束失败
                    var allTagIds = junctionData.Select(j => j.TagId).Distinct().ToList();
                    var existingTagIds = new HashSet<ulong>(); // 默认创建一个空的 HashSet

                    // 只有当存在需要验证的标签时，才去查询数据库
                    if (allTagIds.Any())
                    {
                        // 使用参数化查询，Dapper 会自动处理 IN (...) 列表
                        const string existingTagIdsSql = "SELECT tag_id FROM tags WHERE tag_id IN @TagIds;";
                        var queryResult = await connection.QueryAsync<ulong>(existingTagIdsSql, new { TagIds = allTagIds }, transaction: transaction);
                        existingTagIds = queryResult.ToHashSet();
                    }

                    // 步骤3b: 过滤出有效的标签关系（只保留数据库中存在的标签）
                    var validJunctionData = junctionData.Where(j => existingTagIds.Contains(j.TagId)).ToList();
                    var invalidJunctionData = junctionData.Where(j => !existingTagIds.Contains(j.TagId)).ToList();
                    var invalidTagIds = allTagIds.Where(id => !existingTagIds.Contains(id)).ToList();
                    var invalidCount = invalidJunctionData.Count;

                    // 调试日志: 打印标签详情
                    _logger.LogDebug("标签验证详情: 总标签数 {TotalTags}, 有效标签数 {ValidTags}, 无效标签数 {InvalidTags}",
                        allTagIds.Count, existingTagIds.Count, invalidTagIds.Count);

                    // if (existingTagIds.Any())
                    // {
                    //     _logger.LogDebug("有效标签ID列表: [{ValidTagIds}]", string.Join(", ", existingTagIds.OrderBy(id => id)));
                    // }

                    if (invalidTagIds.Any())
                    {
                        _logger.LogWarning("无效标签ID列表(已被删除): [{InvalidTagIds}]", string.Join(", ", invalidTagIds.OrderBy(id => id)));
                    }

                    if (invalidCount > 0)
                    {
                        _logger.LogWarning("发现 {InvalidCount} 个无效标签关系(标签已被删除), 已自动跳过。", invalidCount);

                        // 详细打印引用无效标签的帖子信息
                        var invalidPostsGrouped = invalidJunctionData
                            .GroupBy(j => j.TagId)
                            .OrderBy(g => g.Key);

                        foreach (var group in invalidPostsGrouped)
                        {
                            var invalidTagId = group.Key;
                            var affectedThreadIds = group.Select(j => j.ThreadId).ToList();

                            _logger.LogWarning("无效标签 {InvalidTagId} 被以下 {PostCount} 个帖子引用:", invalidTagId, affectedThreadIds.Count);

                            // 从原始数据中找到对应的帖子信息
                            var affectedPosts = dataList
                                .Where(post => affectedThreadIds.Contains(post.ThreadId))
                                .OrderBy(post => post.ThreadId);

                            foreach (var post in affectedPosts)
                            {
                                _logger.LogWarning("  └─ 帖子ID: {ThreadId}, 标题: \"{Title}\", 链接: {JumpUrl}",
                                    post.ThreadId, post.Title.Length > 50 ? post.Title.Substring(0, 50) + "..." : post.Title, post.JumpUrl);
                            }
                        }
                    }

                    if (validJunctionData.Any())
                    {
                        // 步骤3c: 批量插入有效的帖子-标签关系 (post_tags_junction)
                        tagJunctionAffectedRows = await connection.ExecuteAsync(postTagsJunctionUpsertSqlCommand, validJunctionData, transaction);
                        _logger.LogInformation("处理 {TotalCount} 条帖子-标签关系, 影响关系表 {AffectedRows} 行。", validJunctionData.Count, tagJunctionAffectedRows);
                    }
                    else
                    {
                        _logger.LogInformation("所有标签关系均无效, 跳过关系表插入。");
                    }
                }

                // 成功: 提交事务
                await transaction.CommitAsync();
                _logger.LogInformation("事务成功提交, {TotalCount} 条帖子数据处理完毕。", dataList.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "批量处理帖子时发生错误, 事务已回滚。");
                return (null, null);
            }

            var insertPostResult = new InsertPostResult
            {
                MainAffectedRows = mainAffectedRows,
                FtsAffectedRows = ftsAffectedRows,
                TagJunctionAffectedRows = tagJunctionAffectedRows
            };

            // 返回: 已处理的列表
            return (dataList, insertPostResult);
        }
        catch (Exception ex)
        {
            _logger.LogError("InsertPostAsync {ex}", ex);
            return (null, null);
        }
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
                _logger.LogWarning("无标签字典数据需要插入, 操作跳过。");
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

            _logger.LogInformation("处理 {TotalCount} 个标签, 有效标签 {ValidCount} 个。", totalTagCount, validTagCount);

            // 校验: 若无有效标签则提前返回
            if (!allTagData.Any())
            {
                _logger.LogWarning("无有效标签数据需要插入, 操作跳过。");
                return;
            }

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 执行: 批量插入或更新标签
            int affectedRows = await connection.ExecuteAsync(tagsUpsertSqlCommand, allTagData);
            _logger.LogInformation("处理 {TotalCount} 个标签, 成功插入或更新 {AffectedRows} 个新标签。", validTagCount, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("InsertTagsAsync {ex}", ex);
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
                _logger.LogWarning("无频道数据需要插入, 操作跳过。");
                return;
            }

            // 准备: 转换数据格式, 拼接服务器与频道名
            var channelData = channelList.Select(c => new { c.Id, Name = $"{c.Guild.Name}.{c.Name}" });

            // 准备: 创建数据库连接
            using var connection = new SqliteConnection(_connectionString);
            // 执行: 批量插入或更新频道
            int affectedRows = await connection.ExecuteAsync(channelsUpsertSqlCommand, channelData);
            _logger.LogInformation("处理 {TotalCount} 个频道, 成功插入或更新 {AffectedRows} 个新频道。", channelList.Count, affectedRows);
        }
        catch (Exception ex)
        {
            _logger.LogError("InsertChannelsAsync {ex}", ex);
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
                _logger.LogInformation("成功更新帖子 {ThreadId} 的作品更新信息。", threadId);
            }
            else
            {
                _logger.LogTrace("尝试更新帖子 {ThreadId} 的作品信息, 但主表中未找到记录 (可能尚未同步), 跳过。", threadId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新帖子 {ThreadId} 的作品更新信息时发生错误。", threadId);
        }
    }
}