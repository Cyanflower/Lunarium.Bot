using Npgsql;

using Dapper;

using LunariumBot.Net.DataCenter.Model;
using LunariumBot.Net.DataCenter.Model.Database;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{


    public async Task UpsertUserDataAsync(NpgsqlTransaction transaction, UserData userData)
    {
        const string SqlCommand = """
            -- 插入一条新记录，如果 user_id 冲突则执行更新
            INSERT INTO user_data (
                user_id, user_name, user_thread_count, user_message_count, user_update_count,
                user_credit_lv1, user_credit_lv2, user_credit_lv3
            )
            VALUES (
                @UserId, @UserName, @UserThreadCountDelta, @UserMessageCountDelta, @UserUpdateCountDelta,
                @UserCreditLv1Delta, @UserCreditLv2Delta, @UserCreditLv3Delta
            )
            ON CONFLICT (user_id) DO UPDATE 
            SET
                -- 参数的值允许正负数和0, 为0时不变
                user_thread_count = user_data.user_thread_count + EXCLUDED.user_thread_count,
                user_message_count = user_data.user_message_count + EXCLUDED.user_message_count,
                user_update_count = user_data.user_update_count + EXCLUDED.user_update_count,
                user_credit_lv1 = user_data.user_credit_lv1 + EXCLUDED.user_credit_lv1,
                user_credit_lv2 = user_data.user_credit_lv2 + EXCLUDED.user_credit_lv2,
                user_credit_lv3 = user_data.user_credit_lv3 + EXCLUDED.user_credit_lv3;
            """;

        var userDataDb = UserDataDb.FromDomain(userData);

        var parameters = new
        {
            userDataDb.UserId,
            userDataDb.UserName,
            userDataDb.UserThreadCountDelta,
            userDataDb.UserMessageCountDelta,
            userDataDb.UserUpdateCountDelta,
            userDataDb.UserCreditLv1Delta,
            userDataDb.UserCreditLv2Delta,
            userDataDb.UserCreditLv3Delta
        };
        
        try
        {
            if (transaction.Connection is null) throw new InvalidOperationException("事务连接为 null");
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ UpsertUserDataAsync: 为用户 {UserId} 插入或更新数据时失败！", userData.UserId);
            throw;
        }
    }
}