using Npgsql;

using Dapper;

using LunariumBot.Net.DataCenter.Model;

namespace LunariumBot.Net.DataCenter.Database;

public partial class DatabaseService
{


    public async Task UpsertUserDataAsync(NpgsqlTransaction transaction, UserData userData)
    {
        const string SqlCommand = """
            -- 插入一条新记录，如果 user_id 冲突则执行更新
            INSERT INTO user_data (
                user_id, user_thread_count, user_message_count, user_update_count,
                user_credit_lv1, user_credit_lv2, user_credit_lv3
            )
            VALUES (
                @UserId, @UserThreadCount, @UserMessageCount, @UserUpdateCount,
                @UserCreditLv1, @UserCreditLv2, @UserCreditLv3
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

        var parameters = new
        {
            userData.UserId,
            userData.UserThreadCount,
            userData.UserMessageCount,
            userData.UserUpdateCount,
            userData.UserCreditLv1,
            userData.UserCreditLv2,
            userData.UserCreditLv3
        };
        
        try
        {
            await transaction.Connection.ExecuteAsync(SqlCommand, parameters, transaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ UpsertUserDataAsync: 为用户 {UserId} 插入或更新数据时失败！", userData.UserId);
            throw;
        }
    }
    
    public async Task UserDataNumericUpdateAsync(NpgsqlTransaction transaction)
    {

    }
}