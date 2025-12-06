using System.Net.Sockets;

using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

using LunariumBot.Net.Logger;
using LunariumBot.Net.Extensions;

using LunariumBot.Net.SearchBot.Database;
using LunariumBot.Net.SearchBot.Models;
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.ApplicationServices;

public partial class ApplicationService
{

    public async Task<ServiceResult<List<FavoriteTypeInfo>>> GetFavoriteTypeServiceAsync(ulong userId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.GetFavoriteTypeAsync(userId);
            });
            if (result.Count == 0)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您当前的收藏列表还没有任何收藏夹...\n 快去创建自己的收藏夹然后收藏自己喜欢的帖子吧!"
                };
            }
            return new()
            {
                Status = ServiceStatus.Success,
                Payload = result,
                Message = "查询成功"
            }; ;
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏夹列表列表失败: 请求人ID: {userId}, 错误ID: {errorId}", userId, errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteInfo>> CheckFavoriteExistServiceAsync(ulong userId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.CheckFavoriteExistAsync(userId, threadId);
            });

            if (result is not null)
            {
                return new()
                {
                    Status = ServiceStatus.Exist,
                    Payload = result,
                    Message = "您已收藏过该帖子了"
                };
            }
            else
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = null,
                    Message = "您尚未收藏该帖子"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏记录失败: 请求人ID: {userId}, 目标帖子ID: {threadId}, 错误ID: {errorId}", userId, threadId, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
    
    public async Task<ServiceResult> RemoveFavoriteServiceAsync(ulong userId, ulong threadId)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    return await _databaseService.RemoveFavoriteAsync(transaction, userId, threadId);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (result == DeleteResult.Success)
            {
                return new() { Status = ServiceStatus.Success, Message = "已成功取消收藏" };
            }
            else
            {
                return new() { Status = ServiceStatus.NotFound, Message = "您并未收藏该帖子" };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 移除收藏失败: 请求人ID: {userId}, 帖子ID: {threadId}, 错误ID: {errorId}", userId, threadId, errorId);
            return new() { Status = ServiceStatus.InternalError, Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```" };
        }
    }

    public async Task<ServiceResult<List<FavoriteInfo>>> QueryUserFavoritesServiceAsync(ulong userId, long? favoriteAs)
    {
        try
        {
            var result = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                return await _databaseService.QueryUserFavoritesAsync(userId, favoriteAs);
            });

            if (result.Count == 0)
            {
                return new()
                {
                    Status = ServiceStatus.NotFound,
                    Payload = result,
                    Message = "该分类下暂无收藏"
                };
            }

            return new()
            {
                Status = ServiceStatus.Success,
                Payload = result,
                Message = $"成功查询到 {result.Count} 条收藏"
            };
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 查询收藏列表失败: 请求人ID: {userId}, 分类ID: {favoriteAs}, 错误ID: {errorId}", userId, favoriteAs?.ToString() ?? "ALL", errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteTypeInfo>> CreateFavoriteTypeServiceAsync(ulong userId, string typeName)
    {
        try
        {
            // 2. 执行数据库操作
            var createdInfo = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();

                try
                {
                    return await _databaseService.CreateFavoriteTypeAsync(transaction, userId, typeName);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            // 3. 结果判断
            if (createdInfo is not null)
            {
                // 成功创建，返回完整的对象（包含生成的ID）
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = createdInfo,
                    Message = $"成功创建收藏夹: {typeName}"
                };
            }
            else
            {
                // createdInfo 为 null，说明触发了 ON CONFLICT
                return new()
                {
                    Status = ServiceStatus.Conflict,
                    Payload = null,
                    Message = $"您已经拥有名为 \"**{typeName}**\" 的收藏夹了"
                };
            }
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 创建收藏夹失败: 请求人ID: {userId}, 名称: {typeName}, 错误ID: {errorId}", userId, typeName, errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult<FavoriteInfo>> AddFavoriteServiceAsync(FavoriteInfoDb favoriteInfoDb)
    {
        try
        {
            var resultDb = await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = (Npgsql.NpgsqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    return await _databaseService.AddUserFavoriteAsync(transaction, favoriteInfoDb);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            if (resultDb is not null)
            {
                // 成功: 将数据库返回的权威数据转换为业务模型返回
                return new()
                {
                    Status = ServiceStatus.Success,
                    Payload = resultDb,
                    Message = "收藏成功"
                };
            }
            else
            {
                // resultDb 为 null，说明触发了 ON CONFLICT
                return new()
                {
                    Status = ServiceStatus.Conflict,
                    Payload = null,
                    Message = "该帖子已在您的收藏列表中"
                };
            }
        }
        catch (InvalidOperationException) // 捕获 DB 层的收藏夹不存在异常
        {
            return new()
            {
                Status = ServiceStatus.NotFound,
                Message = "操作失败: 指定的收藏夹不存在或已被删除"
            };
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 创建收藏失败: 请求人ID: {userId}, 帖子ID: {threadId}, 收藏夹ID: {favoriteAs}, 错误ID: {errorId}",
                favoriteInfoDb.UserId,
                favoriteInfoDb.ThreadId,
                favoriteInfoDb.FavoriteAs,
                errorId);

            return new()
            {
                Status = ServiceStatus.InternalError,
                Payload = null,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult> RemoveFavoriteCategoryServiceAsync(ulong userId, FavoriteTypeInfo favoriteTypeInfo)
    {
        try
        {
            return await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // 2. 删除分类
                    // 利用数据库的 ON DELETE CASCADE，这会自动删除 user_favorite_posts 中对应的记录
                    int affectedRows = await _databaseService.DeleteFavoriteCategoryAsync(transaction, userId, favoriteTypeInfo.FavoriteId);

                    await transaction.CommitAsync();

                    _logger.Info("用户 {UserId} 删除了收藏夹 {TypeName}({FavoriteId})，触发级联删除。", userId, favoriteTypeInfo.TypeName, favoriteTypeInfo.FavoriteId);

                    if (affectedRows > 0)
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.Success,
                            Message = $"已删除收藏夹 **{favoriteTypeInfo.TypeName}** 及其包含的所有收藏。"
                        };
                    }
                    else
                    {
                        throw new InvalidOperationException("源收藏夹不存在");
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 删除收藏夹失败: 用户ID: {UserId}, 收藏夹ID: {TypeName}({FavoriteId}), 错误ID: {ErrorId}", userId, favoriteTypeInfo.TypeName, favoriteTypeInfo.FavoriteId, errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult> MoveAndRemoveFavoriteCategoryServiceAsync(ulong userId, long oldId, long newId)
    {
        if (oldId == newId)
        {
            return new() { Status = ServiceStatus.InvalidInput, Message = "源收藏夹和目标收藏夹不能相同" };
        }

        try
        {
            return await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // 验证目标收藏夹是否存在, 并获取其名称
                    var newTypeName = await _databaseService.GetFavoriteTypeNameAsync(transaction, userId, newId);
                    if (string.IsNullOrEmpty(newTypeName))
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.NotFound,
                            Message = "目标收藏夹不存在，无法转移"
                        };
                    }

                    // 验证源收藏夹是否存在
                    var oldTypeName = await _databaseService.GetFavoriteTypeNameAsync(transaction, userId, oldId);
                    if (string.IsNullOrEmpty(oldTypeName))
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.NotFound,
                            Message = "要删除的源收藏夹不存在"
                        };
                    }

                    // 执行转移
                    // 返回受影响的行数，即转移了多少个帖子
                    int movedCount = await _databaseService.MoveFavoritePostsAsync(transaction, userId, oldId, newId);

                    // 删除旧分类
                    // 此时旧分类下应该没有帖子了（都移走了），所以 Cascade 不会误删帖子
                    await _databaseService.DeleteFavoriteCategoryAsync(transaction, userId, oldId);

                    // 提交事务
                    await transaction.CommitAsync();

                    _logger.Info("用户 {UserId} 将收藏夹 {OldId} 的 {Count} 个帖子转移至 {NewId} 并删除了旧分类。", userId, oldId, movedCount, newId);

                    return new ServiceResult
                    {
                        Status = ServiceStatus.Success,
                        Message = $"成功将 **{movedCount}** 个收藏从 **{oldTypeName}** 转移到 **{newTypeName}**，并删除了旧收藏夹。"
                    };
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 转移并删除收藏夹失败: 用户ID: {UserId}, 源ID: {OldId}, 目标ID: {NewId}, 错误ID: {ErrorId}", userId, oldId, newId, errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }

    public async Task<ServiceResult> RenameFavoriteCategoryServiceAsync(ulong userId, long categoryId, string newName)
    {
        // 基础校验
        if (string.IsNullOrWhiteSpace(newName))
        {
            return new() { Status = ServiceStatus.InvalidInput, Message = "收藏夹名称不能为空" };
        }
        
        newName = newName.Trim();
        if (newName.Length > 50) 
        {
            return new() { Status = ServiceStatus.InvalidInput, Message = "收藏夹名称过长" };
        }

        try
        {
            return await _dbRetryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = _databaseService.GetConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // 检查名称是否冲突
                    // 查询是否存在名为 newName 且 ID 不是当前 ID 的记录
                    bool exists = await _databaseService.CheckOtherFavoriteTypeExistsAsync(transaction, userId, newName, categoryId);
                    
                    if (exists)
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.Conflict,
                            Message = $"您已经拥有名为 **{newName}** 的收藏夹了，请换个名字。"
                        };
                    }

                    // 获取旧名称
                    var oldName = await _databaseService.GetFavoriteTypeNameAsync(transaction, userId, categoryId);
                    if (oldName == null)
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.NotFound,
                            Message = "目标收藏夹不存在或已被删除"
                        };
                    }

                    if (oldName == newName)
                    {
                        return new ServiceResult
                        {
                            Status = ServiceStatus.InvalidInput,
                            Message = "新名称与旧名称相同，无需修改"
                        };
                    }

                    // 执行更新
                    // 由于数据库触发器的存在, 只需要更新 user_favorite_types
                    int affectedRows = await _databaseService.RenameFavoriteTypeAsync(transaction, userId, categoryId, newName);

                    await transaction.CommitAsync();

                    if (affectedRows > 0)
                    {
                        _logger.Info("用户 {UserId} 将收藏夹 {OldName}({CategoryId}) 重命名为 {NewName}", userId, oldName, categoryId, newName);
                        return new ServiceResult
                        {
                            Status = ServiceStatus.Success,
                            Message = $"成功将收藏夹 **{oldName}** 重命名为 **{newName}**"
                        };
                    }
                    else
                    {
                        // 理论上前面 Check 过了, 这里不应该发生，除非并发删除
                        return new ServiceResult
                        {
                            Status = ServiceStatus.NotFound,
                            Message = "操作失败, 收藏夹可能已被删除"
                        };
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            string errorId = CorrelationIdGenerator.Generate();
            _logger.Error(ex, "❌ 重命名收藏夹失败: 用户ID: {UserId}, 收藏夹ID: {CategoryId}, 新名称: {NewName}, 错误ID: {ErrorId}", userId, categoryId, newName, errorId);
            return new()
            {
                Status = ServiceStatus.InternalError,
                Message = $"内部错误, 请联系管理组并提供此错误ID: \n```\n{errorId}\n```"
            };
        }
    }
}