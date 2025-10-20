-- ===================================================================
-- 数据库版本 1: 初始表结构
-- 描述: 创建所有项目的初始表格。
-- 适配: PostgreSQL
-- ===================================================================

-- ===================================================================
-- 0. 数据库信息表
-- ===================================================================

CREATE TABLE IF NOT EXISTS db_info (
    version INT PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL,
    last_sync_at TIMESTAMPTZ NOT NULL,
    initialize_at TIMESTAMPTZ NOT NULL
);

INSERT INTO db_info (version, applied_at, last_sync_at, initialize_at)
VALUES (1, NOW(), NOW(), NOW())
ON CONFLICT (version) DO NOTHING;

-- ===================================================================
-- 1. 基础实体表
-- ===================================================================

-- 表: channels
-- 描述: 存储唯一频道信息
CREATE TABLE IF NOT EXISTS channels (
    channel_id BIGINT PRIMARY KEY,   -- Discord 频道雪花ID
    channel_name TEXT NOT NULL,       -- 频道名, 便于调试
    guild_id BIGINT NOT NULL,
    guild_name TEXT NOT NULL
);

-- 表: tags
-- 描述: 存储唯一标签
CREATE TABLE IF NOT EXISTS tags (
    tag_id BIGINT PRIMARY KEY,
    channel_id BIGINT NOT NULL,
    tag_name TEXT NOT NULL,
    channel_name TEXT,
    guild_name TEXT,
    usage_count INT NOT NULL DEFAULT 0,
    
    -- 外键: 关联频道表. 删除频道时, 级联删除其下所有标签
    CONSTRAINT fk_channel
        FOREIGN KEY (channel_id) 
        REFERENCES channels(channel_id) 
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_tags_tag_name ON tags (tag_name);
CREATE INDEX IF NOT EXISTS idx_tags_channel_name ON tags (channel_name);
CREATE INDEX IF NOT EXISTS idx_tags_guild_name ON tags (guild_name);
CREATE INDEX IF NOT EXISTS idx_tags_usage_count ON tags (usage_count);

-- ===================================================================
-- 2. 核心内容表
-- ===================================================================

-- 表: posts_main
-- 描述: 存储帖子核心数据
CREATE TABLE IF NOT EXISTS posts_main (
    -- 主要数据
    thread_id BIGINT PRIMARY KEY,
    first_message_id BIGINT NOT NULL,
    author_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    channel_name TEXT NOT NULL,
    guild_id BIGINT NOT NULL,
    guild_name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    reaction_count INT DEFAULT 0,
    reply_count INT DEFAULT 0,
    last_active_at TIMESTAMPTZ NOT NULL,
    jump_url TEXT NOT NULL,
    attachment_url TEXT,

    -- 更新数据
    update_at TIMESTAMPTZ,
    update_jump_url TEXT,
    update_count INT NOT NULL DEFAULT 0,

    -- 标签
    tags TEXT,

    -- 是否已经删除
    is_valid BOOLEAN NOT NULL,
    is_locked BOOLEAN NOT NULL,
    is_deleted BOOLEAN NOT NULL,
    in_forum BOOLEAN NOT NULL,
    delete_at TIMESTAMPTZ,
    
    -- 标题和首楼内容
    title TEXT NOT NULL,
    first_message_content TEXT,

    -- 专门存储由 C# 应用层分词后、用空格连接的文本
    title_tokenized TEXT,
    content_tokenized TEXT,

    -- 用于全文搜索的 tsvector 列，将由 tokenized 列生成
    search_vector TSVECTOR

    -- 外键: 关联频道表
    -- CONSTRAINT fk_channel
    --     FOREIGN KEY (channel_id) 
    --     REFERENCES channels(channel_id) 
    --     ON DELETE CASCADE
);

-- 创建主表索引
CREATE INDEX IF NOT EXISTS idx_posts_main_author_id ON posts_main (author_id);
CREATE INDEX IF NOT EXISTS idx_posts_main_channel_id ON posts_main (channel_id);
CREATE INDEX IF NOT EXISTS idx_posts_main_created_at ON posts_main (created_at);
CREATE INDEX IF NOT EXISTS idx_posts_main_reaction_count ON posts_main (reaction_count);
CREATE INDEX IF NOT EXISTS idx_posts_main_reply_count ON posts_main (reply_count);
CREATE INDEX IF NOT EXISTS idx_posts_main_last_active_at ON posts_main (last_active_at);
-- 为全文搜索创建 GIN 索引，以获得最佳性能
CREATE INDEX IF NOT EXISTS idx_posts_main_search_vector ON posts_main USING GIN(search_vector);

-- 表: message_main
-- 描述: 存储消息统计数据
CREATE TABLE IF NOT EXISTS message_main (
    message_id BIGINT PRIMARY KEY,
    author_id BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    guild_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    thread_id BIGINT,
    message_jump_url TEXT NOT NULL,
    is_create_thread BOOLEAN NOT NULL,
    is_update BOOLEAN NOT NULL,
    is_deleted BOOLEAN NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_message_main_author_id ON message_main (author_id);
CREATE INDEX IF NOT EXISTS idx_message_main_created_at ON message_main (created_at);
CREATE INDEX IF NOT EXISTS idx_message_main_channel_id ON message_main (channel_id);
CREATE INDEX IF NOT EXISTS idx_message_main_thread_id ON message_main (thread_id);

-- ===================================================================
-- 3. 用户数据表
-- ===================================================================

-- 表: user_data
-- 描述: 存储用户统计数据
CREATE TABLE IF NOT EXISTS user_data (
    user_id BIGINT PRIMARY KEY,
    user_name TEXT NOT NULL,
    user_thread_count INTEGER DEFAULT 0,
    user_message_count INTEGER DEFAULT 0,
    user_update_count INTEGER DEFAULT 0,
    user_credit_lv1 INTEGER DEFAULT 0,
    user_credit_lv2 INTEGER DEFAULT 0,
    user_credit_lv3 INTEGER DEFAULT 0
);

-- 表: creator_data
-- 描述: 存储作者自动发放数据
CREATE TABLE IF NOT EXISTS autocreator_data (
    user_id BIGINT PRIMARY KEY,
    is_ignore_autocreator BOOLEAN NOT NULL
);

-- ===================================================================
-- 4. 关系连接表
-- ===================================================================

-- 表: post_tags_junction
-- 描述: 建立帖子与标签的多对多关系
CREATE TABLE IF NOT EXISTS post_tags_junction (
    thread_id BIGINT NOT NULL,
    tag_id BIGINT NOT NULL,
    channel_id BIGINT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (thread_id, tag_id),
    CONSTRAINT fk_thread
        FOREIGN KEY (thread_id) 
        REFERENCES posts_main(thread_id),
    CONSTRAINT fk_tag
        FOREIGN KEY (tag_id) 
        REFERENCES tags(tag_id) 
        ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_post_tags_junction_thread_id ON post_tags_junction (thread_id);
CREATE INDEX IF NOT EXISTS idx_post_tags_junction_tag_id ON post_tags_junction (tag_id);

-- 表: post_members
-- 描述: 帖子内的成员表
CREATE TABLE IF NOT EXISTS post_members (
    user_id BIGINT NOT NULL,
    thread_id BIGINT NOT NULL,
    is_thread_owner BOOLEAN NOT NULL,
    first_join_at TIMESTAMPTZ NOT NULL, 
    last_join_at TIMESTAMPTZ NOT NULL,
    last_leave_at TIMESTAMPTZ,
    is_leave BOOLEAN NOT NULL,
    message_count INTEGER DEFAULT 0,
    message_edit_history JSONB,
    -- 一些非发送消息事件也可以导致用户被添加到帖子里, 所以消息相关非必须
    last_message_id BIGINT,
    last_message_at TIMESTAMPTZ,

    PRIMARY KEY (user_id, thread_id)
);

-- ===================================================================
-- 5. 用户订阅收藏表
-- ===================================================================

-- 表: user_favorite_types
-- 描述: 记录用户收藏的帖子类型
CREATE TABLE IF NOT EXISTS user_favorite_types (
    user_id BIGINT NOT NULL,
    type_name TEXT NOT NULL,
    PRIMARY KEY (user_id, type_name)
);

-- 表: user_favorite_posts
-- 描述: 记录用户收藏的帖子
CREATE TABLE IF NOT EXISTS user_favorite_posts (
    user_id BIGINT,
    thread_id BIGINT,
    favorite_as TEXT,
    added_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_id, thread_id)
);

-- 表: user_subscriptions_post
-- 描述: 记录用户订阅的帖子
CREATE TABLE IF NOT EXISTS user_subscriptions_post (
    user_id BIGINT,
    thread_id BIGINT,
    added_at TIMESTAMPTZ,
    PRIMARY KEY (user_id, thread_id)
);

-- 表: user_push_list
-- 描述: 待推送给用户的帖子更新队列
CREATE TABLE IF NOT EXISTS user_push_list (
    user_id BIGINT,
    thread_id BIGINT,
    message_jump_url TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_id, thread_id),
    CONSTRAINT fk_subscription
        FOREIGN KEY (user_id, thread_id) 
        REFERENCES user_subscriptions_post(user_id, thread_id) 
        ON DELETE CASCADE
);

-- 表: user_fast_search_command
-- 描述: 记录用户自定义的快速搜索命令
CREATE TABLE IF NOT EXISTS user_fast_search_command (
    user_id BIGINT,
    command_name TEXT,
    criteria_json JSONB, -- 使用 JSONB 类型存储，效率更高
    PRIMARY KEY (user_id, command_name)
);

-- 表: user_blocked_authors
-- 描述: 记录用户屏蔽的作者
CREATE TABLE IF NOT EXISTS user_blocked_authors (
    user_id BIGINT NOT NULL,
    blocked_user_id BIGINT NOT NULL,
    added_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_id, blocked_user_id)
);

-- ===================================================================
-- T. 触发器函数与触发器
-- 说明: 
-- 在 PostgreSQL 中，触发器逻辑被封装在特殊的“触发器函数”中。
-- 需要首先定义一个函数来描述“做什么”，然后创建一个触发器来规定“何时做”。
-- ===================================================================

-- -------------------------------------------------------------------
-- T.1: 自动维护标签的使用次数 (usage_count)
-- -------------------------------------------------------------------

-- 函数: update_tag_usage_count()
-- 职责: 这是一个通用的计数器函数。当被触发时，它会检查操作类型（是插入还是删除），
--        并相应地增加或减少 `tags` 表中对应标签的 `usage_count` 字段。
--        这确保了标签的热度统计总是实时准确的。
CREATE OR REPLACE FUNCTION update_tag_usage_count()
RETURNS TRIGGER AS $$
BEGIN
    -- 判断触发操作的类型
    IF (TG_OP = 'INSERT') THEN
        -- 如果是插入新关系，则将对应 tag_id 的使用次数 +1
        UPDATE tags SET usage_count = usage_count + 1 WHERE tag_id = NEW.tag_id;
    ELSIF (TG_OP = 'DELETE') THEN
        -- 如果是删除关系，则将对应 tag_id 的使用次数 -1
        UPDATE tags SET usage_count = usage_count - 1 WHERE tag_id = OLD.tag_id;
    END IF;
    RETURN NULL; -- 对于 AFTER 触发器，返回值通常是 NULL，因为它不修改正在操作的行
END;
$$ LANGUAGE plpgsql;

-- 触发器: trigger_update_tag_usage_count
-- 绑定: 将 `update_tag_usage_count` 函数绑定到 `post_tags_junction` 表上。
-- 时机: 在 `post_tags_junction` 表中 每当有行被插入或删除之后 (AFTER INSERT OR DELETE)，
--        为每一行 (FOR EACH ROW) 都会自动执行一次 `update_tag_usage_count` 函数。
DROP TRIGGER IF EXISTS trigger_update_tag_usage_count ON post_tags_junction;
CREATE TRIGGER trigger_update_tag_usage_count
AFTER INSERT OR DELETE ON post_tags_junction
FOR EACH ROW EXECUTE FUNCTION update_tag_usage_count();

-- -------------------------------------------------------------------
-- T.2: 自动为全文搜索生成和更新索引向量
-- -------------------------------------------------------------------

-- 函数: update_search_vector()
-- 职责: 此函数收由 C# 应用层传入的、已经分好词并用空格隔开的文本，
--      然后使用 'simple' 配置将其高效地转换为 tsvector 格式。
--      'simple' 配置在这里非常合适，因为它能正确处理空格分隔的词汇。
CREATE OR REPLACE FUNCTION update_search_vector()
RETURNS TRIGGER AS $$
BEGIN
    -- to_tsvector 的输入源为 tokenized 列
    -- 使用 'simple' 配置，因为它能完美处理已经用空格分好词的文本。
    -- 它会把 "我 爱 lunarium" 转换成 {'我', '爱', 'lunarium'} 的 tsvector。
    NEW.search_vector := 
        setweight(to_tsvector('simple', COALESCE(NEW.title_tokenized, '')), 'A') || 
        setweight(to_tsvector('simple', COALESCE(NEW.content_tokenized, '')), 'B');
    RETURN NEW; -- 对于 BEFORE 触发器，必须返回 NEW 以便将修改后的行写入表中。
END;
$$ LANGUAGE plpgsql;

-- 触发器: trigger_update_search_vector (保持不变)
-- 绑定: 仍然绑定到 `posts_main` 表的 INSERT 或 UPDATE 事件上。
-- 时机: BEFORE
DROP TRIGGER IF EXISTS trigger_update_search_vector ON posts_main;
CREATE TRIGGER trigger_update_search_vector
BEFORE INSERT OR UPDATE ON posts_main
FOR EACH ROW EXECUTE FUNCTION update_search_vector();

-- -------------------------------------------------------------------
-- T.3: 当频道被删除时，软删除其下的所有帖子
-- -------------------------------------------------------------------

-- 函数: handle_channel_soft_delete()
-- 职责: 当一个频道 (channel) 从 `channels` 表中被删除时，此函数会被触发。
--        它不会级联删除帖子，而是将 `posts_main` 表中所有属于该频道的帖子的
--        `is_deleted` 状态标记为 TRUE，并清空其搜索相关的数据，以实现“软删除”。
--        这样做可以保留帖子的历史记录，同时让它们在应用中不再可见或可搜索。
CREATE OR REPLACE FUNCTION handle_channel_soft_delete()
RETURNS TRIGGER AS $$
BEGIN
    -- OLD 变量代表在 `channels` 表中被删除的那一行数据。
    -- 我们用它的 channel_id 来找到所有需要被软删除的帖子。
    UPDATE posts_main
    SET 
        is_deleted = TRUE,
        is_valid = FALSE, -- 逻辑上，被删除的帖子也应该是无效的
        delete_at = NOW() -- 记录一下软删除的时间
    WHERE channel_id = OLD.channel_id; -- 关键的关联条件

    -- 对于 AFTER DELETE 触发器，返回值通常是 NULL，因为它不修改正在操作的行
    RETURN NULL; 
END;
$$ LANGUAGE plpgsql;

-- 触发器: trigger_channel_soft_delete_posts
-- 绑定: 将 `handle_channel_soft_delete` 函数绑定到 `channels` 表上。
-- 时机: 在 `channels` 表的每一行被删除之后 (AFTER DELETE)，
--        为每一行 (FOR EACH ROW) 都会自动执行一次 `handle_channel_soft_delete` 函数。
DROP TRIGGER IF EXISTS trigger_channel_soft_delete_posts ON channels;
CREATE TRIGGER trigger_channel_soft_delete_posts
AFTER DELETE ON channels
FOR EACH ROW EXECUTE FUNCTION handle_channel_soft_delete();

-- -------------------------------------------------------------------
-- T.4: 当帖子被软删除时，级联处理其关联数据
-- -------------------------------------------------------------------

-- 函数: handle_post_soft_delete()
-- 职责: 监视 `posts_main` 表。当一个帖子的 `is_deleted` 字段从 FALSE 变为 TRUE 时，
--        此函数会自动触发，执行一系列清理工作：
--        1. 软删除 `message_main` 中所有与该帖子相关的消息记录。
--        2. 从 `post_tags_junction` 中删除该帖子的所有标签关系。
--        3. 从 `post_members` 中删除该帖子的所有成员记录。
--        这确保了数据的一致性，避免了因帖子被软删除而产生的“孤儿”数据。
CREATE OR REPLACE FUNCTION handle_post_soft_delete()
RETURNS TRIGGER AS $$
BEGIN
    -- 我们只关心更新(UPDATE)操作，并且只有在 `is_deleted` 从 FALSE 变为 TRUE 时才执行。
    -- `OLD.is_deleted = FALSE AND NEW.is_deleted = TRUE` 这个条件是核心！
    IF (TG_OP = 'UPDATE' AND OLD.is_deleted = FALSE AND NEW.is_deleted = TRUE) THEN
        
        -- 1. 软删除 message_main 中所有关联的消息
        UPDATE message_main
        SET is_deleted = TRUE
        WHERE thread_id = NEW.thread_id; -- NEW.thread_id 是被软删除的帖子的 ID

        -- 2. 从 post_tags_junction 删除所有标签关系
        --    因为帖子已经“消失”了，所以它的标签关系也应该被清理掉。
        DELETE FROM post_tags_junction
        WHERE thread_id = NEW.thread_id;

        -- 3. 从 post_members 删除所有成员关系
        --    同样，帖子的成员列表也需要被清理。
        DELETE FROM post_members
        WHERE thread_id = NEW.thread_id;

    END IF;

    -- 对于 AFTER 触发器，返回值会被忽略
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- 触发器: trigger_post_soft_delete_cascade
-- 绑定: 将 `handle_post_soft_delete` 函数绑定到 `posts_main` 表上。
-- 时机: 在 `posts_main` 表的每一行数据被更新之后 (AFTER UPDATE)，
--       都会自动执行一次上面的函数。
DROP TRIGGER IF EXISTS trigger_post_soft_delete_cascade ON posts_main;
CREATE TRIGGER trigger_post_soft_delete_cascade
AFTER UPDATE ON posts_main
FOR EACH ROW EXECUTE FUNCTION handle_post_soft_delete();

-- -------------------------------------------------------------------
-- T.5: 当帖子更新时, 自动为订阅者创建推送通知
-- 描述:
--   这个触发器监视 `posts_main` 表的更新操作。
--   如果一个帖子的 `update_at` 字段发生了变化 (这通常意味着有新回复),
--   它会找到所有在 `user_subscriptions_post` 中订阅了该帖子的用户,
--   然后将这个更新的帖子信息插入或更新到 `user_push_list` 表中
-- -------------------------------------------------------------------

-- 函数: create_push_notification_on_post_update()
-- 职责: 核心处理逻辑。当被触发时，它会执行 "查找订阅者并更新推送列表" 的操作。
CREATE OR REPLACE FUNCTION create_push_notification_on_post_update()
RETURNS TRIGGER AS $$
BEGIN
    -- 只关心更新(UPDATE)操作, 并且只在 `update_at` 字段真实发生变化时才执行。
    -- 使用 `IS DISTINCT FROM` 可以处理 NULL 值的情况，比 `!=` 更健壮。
    -- 同时也确保 `message_jump_url` 不为空，因为这是推送必须的链接。
    IF (TG_OP = 'UPDATE' AND NEW.update_at IS DISTINCT FROM OLD.update_at AND NEW.message_jump_url IS NOT NULL) THEN

        -- 向 `user_push_list` 表中插入新的推送记录。
        INSERT INTO user_push_list (user_id, thread_id, message_jump_url, updated_at)
        
        -- 数据来源是: 查询 `user_subscriptions_post` 表,
        -- 找出所有订阅了当前被更新帖子 (NEW.thread_id) 的用户 (user_id)。
        SELECT 
            usp.user_id, 
            NEW.thread_id, 
            NEW.message_jump_url, 
            NEW.updated_at
        FROM 
            user_subscriptions_post AS usp
        WHERE 
            usp.thread_id = NEW.thread_id

        -- 如果要插入的 (user_id, thread_id) 组合已经存在于 `user_push_list` 中
        -- (因为主键冲突), 那么不报错, 而是执行 UPDATE 操作。
        ON CONFLICT (user_id, thread_id) DO UPDATE 
        
        -- 更新已存在记录的 `message_jump_url` 和 `updated_at` 为最新的值。
        -- `EXCLUDED` 是一个特殊的记录，它代表了“本应被插入但失败了”的那一行数据。
        SET 
            message_jump_url = EXCLUDED.message_jump_url,
            updated_at = EXCLUDED.updated_at;
            
    END IF;

    -- 对于 AFTER 触发器, 返回值会被忽略, 通常返回 NULL 或 NEW 都可以。
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- 触发器: trigger_create_push_list_on_post_update
-- 绑定: 将 `create_push_notification_on_post_update` 函数绑定到 `posts_main` 表上。
-- 时机: 在 `posts_main` 表的每一行数据被更新之后 (AFTER UPDATE), 
--       都会自动执行一次上面的函数。
--       选择 AFTER 是因为可以确保 `posts_main` 的更新已经成功提交,
--       这样推送数据来源就是稳定和正确的。
DROP TRIGGER IF EXISTS trigger_create_push_list_on_post_update ON posts_main;
CREATE TRIGGER trigger_create_push_list_on_post_update
AFTER UPDATE ON posts_main
FOR EACH ROW EXECUTE FUNCTION create_push_notification_on_post_update();