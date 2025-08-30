using Discord;
using Microsoft.Data.Sqlite;
using System;

namespace SearchBot.Database;

public static class DBTable
{
    public const int CURRENT_DB_VERSION = 2;
    public static string GetCreateVersionTableSql()
    {
        return """
        -- ===================================================================
        -- 表: db_info
        -- 描述: 存储数据库元信息，如 schema 版本号，数据库创建或初始化的时间戳
        -- ===================================================================
        CREATE TABLE IF NOT EXISTS db_info (
            version INTEGER PRIMARY KEY,
            applied_at INTEGER NOT NULL,
            initialize_at NTEGER NOT NULL,
            last_save_at INTEGER NOT NULL
        );
        INSERT OR IGNORE INTO db_info (version, applied_at, initialize_at, last_save_at) VALUES (@Version, @AppliedAt, @InitializeAt, @LastSaveAt);
        """;
    }
    public static string GetCreateAllTablesSql()
    {
        // 注意：@timestamp 是一个参数占位符,
        // 需要在使用 Dapper 或其他库执行时传入具体值。
        // 例如: connection.Execute(sql, new { timestamp = 1672531200 });
        return """
        -- ===================================================================
        -- 1. 基础实体表
        -- ===================================================================

        -- 表: channels
        -- 描述: 存储唯一频道信息
        CREATE TABLE IF NOT EXISTS channels (
            channel_id INTEGER PRIMARY KEY,   -- Discord 频道雪花ID
            channel_name TEXT NOT NULL        -- 频道名, 便于调试
        );

        -- 表: tags
        -- 描述: 存储唯一标签
        CREATE TABLE IF NOT EXISTS tags (
            tag_id INTEGER PRIMARY KEY,
            channel_id INTEGER NOT NULL,
            tag_name TEXT NOT NULL,
            channel_name TEXT,
            guild_name TEXT,
            
            -- 外键: 关联频道表. 删除频道时, 级联删除其下所有标签
            FOREIGN KEY (channel_id) REFERENCES channels(channel_id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_tag_name ON tags (tag_name);
        CREATE INDEX IF NOT EXISTS idx_channel_name ON tags (channel_name);
        CREATE INDEX IF NOT EXISTS idx_guild_name ON tags (guild_name);

        -- ===================================================================
        -- 2. 核心内容表
        -- ===================================================================

        -- 表: posts_main
        -- 描述: 存储帖子核心数据
        CREATE TABLE IF NOT EXISTS posts_main (
            thread_id INTEGER PRIMARY KEY,
            first_message_id INTEGER NOT NULL,
            author_id INTEGER NOT NULL,
            channel_id INTEGER NOT NULL,
            channel_name TEXT NOT NULL,
            guild_id INTEGER NOT NULL,
            guild_name TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            member_count INTEGER DEFAULT 0,
            reaction_count INTEGER DEFAULT 0,
            reply_count INTEGER DEFAULT 0,
            last_active_at INTEGER NOT NULL,
            jump_url TEXT NOT NULL,
            attachment_url TEXT,
            update_at INTEGER DEFAULT 0,
            message_jump_url TEXT,
            tags TEXT,
            title TEXT NOT NULL,
            content TEXT NOT NULL,

            -- 外键: 关联频道表. 删除频道时, 级联删除其下所有帖子
            FOREIGN KEY (channel_id) REFERENCES channels(channel_id) ON DELETE CASCADE
        );

        -- 创建主表索引
        CREATE INDEX IF NOT EXISTS idx_posts_author_id ON posts_main (author_id);
        CREATE INDEX IF NOT EXISTS idx_posts_channel_id ON posts_main (channel_id);
        CREATE INDEX IF NOT EXISTS idx_posts_created_at ON posts_main (created_at);
        CREATE INDEX IF NOT EXISTS idx_posts_reaction_count ON posts_main (reaction_count);
        CREATE INDEX IF NOT EXISTS idx_posts_reply_count ON posts_main (reply_count);
        CREATE INDEX IF NOT EXISTS idx_posts_last_active_at ON posts_main (last_active_at);

        -- ===================================================================
        -- 3. 关系连接表
        -- ===================================================================

        -- 表: post_tags_junction
        -- 描述: 建立帖子与标签的多对多关系
        CREATE TABLE IF NOT EXISTS post_tags_junction (
            thread_id INTEGER,
            tag_id INTEGER,
            channel_id INTEGER DEFAULT 0,
            PRIMARY KEY (thread_id, tag_id), -- 复合主键: 确保 post-tag 关系唯一
            FOREIGN KEY (thread_id) REFERENCES posts_main(thread_id) ON DELETE CASCADE,
            FOREIGN KEY (tag_id) REFERENCES tags(tag_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS idx_junction_tag_id ON post_tags_junction (tag_id);

        -- ===================================================================
        -- 4. 全文搜索 (FTS5) 虚拟表及触发器
        -- ===================================================================

        -- 虚拟表: posts_fts
        -- 描述: 存储分词数据, 通过 rowid 关联主表
        CREATE VIRTUAL TABLE IF NOT EXISTS posts_fts USING fts5(
            post_title,          -- 存储标题分词
            post_content,        -- 存储内容分词
            tokenize = 'unicode61' -- 使用 unicode61 分词器 (处理空格和标点)
        );

        -- 触发器: posts_main_after_delete
        -- 动作: posts_main 删除后, 同步删除 fts 表内容
        CREATE TRIGGER IF NOT EXISTS posts_main_after_delete
        AFTER DELETE ON posts_main
        BEGIN
            DELETE FROM posts_fts WHERE rowid = old.thread_id;
        END;

        -- ===================================================================
        -- 5. 用户订阅收藏表
        -- ===================================================================
        -- 说明: 收藏订阅表中的帖子信息字段为后备字段,
        --      用于在主表帖子被删除后, 仍能为用户提供基础信息.
        --      同步机制与主表不同, 仅在用户收藏订阅关系表所有关系中都没有该帖子时, 才删除帖子记录

        -- 表: fallback_posts
        -- 描述: 记录用户收藏和订阅的帖子的基础后备数据
        CREATE TABLE IF NOT EXISTS fallback_posts (
            thread_id INTEGER PRIMARY KEY,
            author_id INTEGER,
            title TEXT,
            jump_url TEXT,
            attachment_url TEXT
        );

        -- 表: user_favorite_tpye
        -- 描述: 记录用户收藏的帖子类型
        CREATE TABLE IF NOT EXISTS user_favorite_types (
            user_id INTEGER NOT NULL,
            type_name TEXT NOT NULL,
            PRIMARY KEY (user_id, type_name)
        );

        -- 表: user_favorite_posts
        -- 描述: 记录用户收藏的帖子
        CREATE TABLE IF NOT EXISTS user_favorite_posts (
            user_id INTEGER,
            thread_id INTEGER,
            favorite_as TEXT,
            added_at INTEGER NOT NULL,
            PRIMARY KEY (user_id, thread_id)
        );

        -- 表: user_subscriptions_post
        -- 描述: 记录用户订阅的帖子
        CREATE TABLE IF NOT EXISTS user_subscriptions_post (
            user_id INTEGER,
            thread_id INTEGER,
            added_at INTEGER,
            PRIMARY KEY (user_id, thread_id)
        );

        -- 表: user_push_list
        -- 描述: 待推送给用户的帖子更新队列
        CREATE TABLE IF NOT EXISTS user_push_list (
            user_id INTEGER,
            thread_id INTEGER,
            message_jump_url TEXT NOT NULL,
            updated_at INTEGER NOT NULL,
            PRIMARY KEY (user_id, thread_id),

            -- 外键: 关联用户订阅表. 用户取消订阅时, 级联删除待推送记录
            FOREIGN KEY (user_id, thread_id) REFERENCES user_subscriptions_post(user_id, thread_id) ON DELETE CASCADE
        );

        -- 表: user_fast_search_command
        -- 描述: 记录用户自定义的快速搜索命令
        CREATE TABLE IF NOT EXISTS user_fast_search_command (
            user_id INTEGER,
            command_name TEXT,
            criteria_json TEXT,
            PRIMARY KEY (user_id, command_name)
        );

        -- 触发器: ufasp_after_delete_on_favorite
        -- 动作: 用户取消收藏后, 检查并清理后备表记录
        CREATE TRIGGER IF NOT EXISTS ufasp_after_delete_on_favorite
        AFTER DELETE ON user_favorite_posts
        FOR EACH ROW
        BEGIN
            -- 检查此帖子是否还被任何用户收藏或订阅
            -- 如果均无记录, 则从后备表中删除
            DELETE FROM fallback_posts
            WHERE thread_id = OLD.thread_id
              AND NOT EXISTS (SELECT 1 FROM user_favorite_posts WHERE thread_id = OLD.thread_id)
              AND NOT EXISTS (SELECT 1 FROM user_subscriptions_post WHERE thread_id = OLD.thread_id);
        END;

        -- 触发器: ufasp_after_delete_on_subscription
        -- 动作: 用户取消订阅后, 检查并清理后备表记录
        CREATE TRIGGER IF NOT EXISTS ufasp_after_delete_on_subscription
        AFTER DELETE ON user_subscriptions_post
        FOR EACH ROW
        BEGIN
            -- 逻辑同上, 检查并清理
            DELETE FROM fallback_posts
            WHERE thread_id = OLD.thread_id
              AND NOT EXISTS (SELECT 1 FROM user_favorite_posts WHERE thread_id = OLD.thread_id)
              AND NOT EXISTS (SELECT 1 FROM user_subscriptions_post WHERE thread_id = OLD.thread_id);
        END;

        -- ===================================================================
        -- 6. 数据同步触发器 (Data Synchronization Triggers)
        -- ===================================================================

        -- 触发器: fp_after_insert_copy_attachment
        -- 动作: 当新记录插入 fallback_posts 后, 尝试从 posts_main 复制附件链接
        CREATE TRIGGER IF NOT EXISTS fp_after_insert_copy_attachment
        AFTER INSERT ON fallback_posts
        FOR EACH ROW
        BEGIN
            UPDATE fallback_posts
            SET attachment_url = (
                SELECT attachment_url 
                FROM posts_main 
                WHERE thread_id = NEW.thread_id
            )
            WHERE thread_id = NEW.thread_id;
        END;

        -- ===================================================================
        -- 7. 服务器总统计数据
        -- ===================================================================

        -- 表: server_data
        -- 描述: 记录服务器整体数据
        CREATE TABLE IF NOT EXISTS server_data (
            date DATE NOT NULL,
            server_id INTEGER NOT NULL,
            server_name TEXT NOT NULL,
            channel_id INTEGER NOT NULL,
            channel_name TEXT NOT NULL,
            created_posts_count INTEGER DEFAULT 0,
            reply_count INTEGER DEFAULT 0,

            PRIMARY KEY (date, server_id, channel_id)
        );
        """;
    }
    /// <summary>
    /// 升级迁移脚本
    /// </summary>
    /// <returns></returns>
    public static Dictionary<int, string> GetMigrationScripts()
    {
        return new Dictionary<int, string>
        {

        };
    }
}

// =================================================
// 迁移脚本示例
// =================================================
/*
return new Dictionary<int, string>
{
    版本x的迁移脚本示例
    [x] = """
        -- 添加新列到现有表
        ALTER TABLE posts_main ADD COLUMN view_count INTEGER DEFAULT 0;
        
        -- 创建新表
        CREATE TABLE IF NOT EXISTS user_settings (
            user_id INTEGER PRIMARY KEY,
            setting_key TEXT NOT NULL,
            setting_value TEXT,
            updated_at INTEGER NOT NULL
        );
        
        -- 创建新索引
        CREATE INDEX IF NOT EXISTS idx_posts_view_count ON posts_main (view_count);
        """,

    版本y的迁移脚本
    [y] = """
        -- 更多的数据库变更...
        ALTER TABLE channels ADD COLUMN is_active INTEGER DEFAULT 1;
        """
}

*/
// =================================================
// 历史迁移脚本区
// =================================================
/*
版本 v2 ==========================
[2] = """
    ALTER TABLE post_tags_junction ADD COLUMN channel_id INTEGER DEFAULT 0;

    CREATE TABLE IF NOT EXISTS server_data (
        date DATE NOT NULL,
        server_id INTEGER NOT NULL,
        server_name TEXT NOT NULL,
        channel_id INTEGER NOT NULL,
        channel_name TEXT NOT NULL,
        created_posts_count INTEGER DEFAULT 0,
        reply_count INTEGER DEFAULT 0,

        PRIMARY KEY (date, server_id, channel_id)
    );
    """,
=================================


*/