using Npgsql;
using Dapper;

using LunariumBot.Net.DataCenter.Models;
using LunariumBot.Net.DataCenter.Models.Database;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{
    public async Task SyncAllRolesAsync(NpgsqlTransaction transaction, List<RoleData> roleDatas)
    {
        const string sqlCommand = """
            INSERT INTO roles (
                role_id, role_name, role_position, role_icon_url,
                role_emoji, role_primary_color, role_secondary_color,
                role_tertiary_color
            )
            SELECT
                t.role_id, t.role_name, t.role_position, t.role_icon_url,
                t.role_emoji, t.role_primary_color, t.role_secondary_color,
                t.role_tertiary_color
            FROM unnest(
                @RoleIds, @RoleNames, @RolePositions, @RoleIconUrls,
                @RoleEmojis, @RolePrimaryColors, @RoleSecondaryColors,
                @RoleTertiaryColors
            ) AS t(
                role_id, role_name, role_position, role_icon_url,
                role_emoji, role_primary_color, role_secondary_color,
                role_tertiary_color
            )
            ON CONFLICT (role_id) DO UPDATE
            SET
                role_name = EXCLUDED.role_name,
                role_position = EXCLUDED.role_position,
                role_icon_url = EXCLUDED.role_icon_url,
                role_emoji = EXCLUDED.role_emoji,
                role_primary_color = EXCLUDED.role_primary_color,
                role_secondary_color = EXCLUDED.role_secondary_color,
                role_tertiary_color = EXCLUDED.role_tertiary_color;
            """;
        try
        {
            var roleDataDbs = roleDatas.Select(data => RoleDataDb.FromDomain(data)).ToList();
            var parameters = new
            {
                RoleIds = roleDataDbs.Select(data => data.RoleId).ToArray(),
                RoleNames = roleDataDbs.Select(data => data.RoleName).ToArray(),
                RolePositions = roleDataDbs.Select(data => data.RolePosition).ToArray(),
                RoleIconUrls = roleDataDbs.Select(data => data.RoleIconUrl).ToArray(),
                RoleEmojis = roleDataDbs.Select(data => data.RoleEmoji).ToArray(),
                RolePrimaryColors = roleDataDbs.Select(data => data.RolePrimaryColor).ToArray(),
                RoleSecondaryColors = roleDataDbs.Select(data => data.RoleSecondaryColor).ToArray(),
                RoleTertiaryColors = roleDataDbs.Select(data => data.RoleTertiaryColor).ToArray(),
            };

            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            await transaction.Connection.ExecuteAsync(sqlCommand, parameters, transaction, commandTimeout: 120);


        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncAllRolesAsync: 同步身份组信息表失败");
            throw;
        }
    }

    public async Task DeleteOutdatedRolesAsync(NpgsqlTransaction transaction, List<RoleData> roleDatas)
    {
        const string sqlCommand = """
            UPDATE roles
            SET
                is_deleted = TRUE,
                delete_at = NOW()
            WHERE
                role_id <> ALL(@RoleIds)
                AND is_deleted = FALSE;
            """;
        try
        {
            if (roleDatas.Count == 0) return;
            var roleDataDbs = roleDatas.Select(data => RoleDataDb.FromDomain(data)).ToList();
            var parameters = new
            {
                RoleIds = roleDataDbs.Select(data => data.RoleId).ToArray(),
            };

            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            int affectedRows = await transaction.Connection.ExecuteAsync(sqlCommand, parameters, transaction, commandTimeout: 120);

            if (affectedRows > 0)
            {
                _logger.Info("🗑️ 已软删除 {Count} 个过时的身份组", affectedRows);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ DeleteOutdatedRolesAsync: 同步身份组信息表(软删除)失败");
            throw;
        }
    }

    public async Task SyncAllMembersAsync(NpgsqlTransaction transaction, List<UserData> userDatas)
    {
        const string syncCommand = """
            INSERT INTO user_data (
                user_id, user_username, user_nickname,
                user_global_name, user_avatar_url
            )
            SELECT
                t.user_id, t.user_username, t.user_nickname,
                t.user_global_name, t.user_avatar_url
            FROM unnest(
                @UserIds, @UserUsernames, @UserNikcnames,
                @UserGlobalNames, @UserAvatarUrls
            ) AS t(
                user_id, user_username, user_nickname,
                user_global_name, user_avatar_url
            )
            ON CONFLICT (user_id) DO UPDATE
            SET
                user_username = EXCLUDED.user_username,
                user_nickname = EXCLUDED.user_nickname,
                user_global_name = EXCLUDED.user_global_name,
                user_avatar_url = EXCLUDED.user_avatar_url;
            """;
        try
        {
            var userDataDbs = userDatas.Select(data => UserDataDb.FromDomain(data)).ToList();
            var parameters = new
            {
                UserIds = userDataDbs.Select(data => data.UserId).ToArray(),
                UserUsernames = userDataDbs.Select(data => data.UserUsername).ToArray(),
                UserNikcnames = userDataDbs.Select(data => data.UserNikcname).ToArray(),
                UserGlobalNames = userDataDbs.Select(data => data.UserGlobalName).ToArray(),
                UserAvatarUrls = userDataDbs.Select(data => data.UserAvatarUrl).ToArray()
            };

            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            await transaction.Connection.ExecuteAsync(syncCommand, parameters, transaction, commandTimeout: 120);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncAllMembersAsync: 同步用户信息表失败");
            throw;
        }
    }
    
    public async Task SyncAllMemberRolesAsync(NpgsqlTransaction transaction, List<UserData> userDatas)
    {
        const string sqlCommand = """
            -- 将本次同步涉及的用户的所有角色关系标记为 "非活跃"
            -- 这样可以处理用户被移除角色的情况 (新列表中没有，但数据库里有的，经过这步后就是 false)
            UPDATE user_roles
            SET 
                is_active = FALSE,
                deactive_at = NOW(),
                updated_at = NOW()
            WHERE user_id = ANY(@UniqueUserIds);

            -- 批量插入或更新最新的关系，将其标记为 "活跃"
            INSERT INTO user_roles (user_id, role_id, deactive_at, is_active, updated_at)
            SELECT 
                t.user_id, 
                t.role_id, 
                NULL,      -- deactive_at
                TRUE,      -- is_active
                NOW()      -- updated_at
            FROM unnest(@FlatUserIds, @FlatRoleIds) AS t(user_id, role_id)
            ON CONFLICT (user_id, role_id) DO UPDATE
            SET
                is_active = TRUE,
                deactive_at = NULL,
                updated_at = NOW();
            """;

        try
        {
            if (userDatas.Count == 0) return;
        
            var userDataDbs = userDatas.Select(data => UserDataDb.FromDomain(data)).ToList();

            // 获取所有涉及的唯一 UserID (用于步骤 1 的范围限定)
            var uniqueUserIds = userDataDbs.Select(u => u.UserId).Distinct().ToArray();

            // 扁平化数据 (Flatten) (用于步骤 2 的批量插入)
            // 需要将 List<User> 转换为 User-Role 的一对一平铺列表
            var flatUserIds = new List<long>();
            var flatRoleIds = new List<long>();

            foreach (var user in userDataDbs)
            {
                if (user.UserRoles is null || user.UserRoles.Count == 0) continue;

                foreach (var roleId in user.UserRoles)
                {
                    flatUserIds.Add(user.UserId);
                    flatRoleIds.Add(roleId);
                }
            }

            // 即使 flat 列表为空（所有用户都没有角色），我们也需要执行 SQL 的第一步（把他们的角色清空）
            // 所以这里不需要判断 flatCount == 0 就 return
            
            var parameters = new
            {
                UniqueUserIds = uniqueUserIds,
                FlatUserIds = flatUserIds.ToArray(),
                FlatRoleIds = flatRoleIds.ToArray()
            };

            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");

            await transaction.Connection.ExecuteAsync(sqlCommand, parameters, transaction, commandTimeout: 120);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ SyncAllMemberRolesAsync: 同步用户角色关系表失败");
            throw;
        }
    }
}