using System;
using System.Collections.Generic;

using LunariumBot.Net.SearchBot.Models;

namespace LunariumBot.Net.SearchBot.InteractionsCache;

public static class InteractionsCacheFactory
{
    // 字典存储：Type -> 创建函数
    // Func参数: (实际数据对象, 过期时间, ID) -> 返回接口
    private static readonly Dictionary<Type, Func<object, DateTimeOffset, string, IInteractionsCache>> _cacheCreators = new();

    static InteractionsCacheFactory()
    {
        Register<FavoriteInteractionState>((data, exp, id) => new FavoriteInteractionCache(data, exp, id));
        Register<List<FavoriteTypeInfo>>((data, exp, id) => new RemoveFavoriteInteractionCache(data, exp, id));
    }

    /// <summary>
    /// 注册特定类型的缓存视图创建逻辑
    /// </summary>
    /// <typeparam name="TData">业务数据类型</typeparam>
    /// <param name="creator">创建委托：(TData, ExpiresAt, Id) => IInteractionsCache</param>
    public static void Register<TData>(Func<TData, DateTimeOffset, string, IInteractionsCache> creator) where TData : class
    {
        // 将 TData 具体的委托包装成 object 通用委托存入字典
        _cacheCreators[typeof(TData)] = (data, expires, id) => creator((TData)data, expires, id);
    }

    /// <summary>
    /// 创建缓存视图实例
    /// </summary>
    public static IInteractionsCache Create<TData>(TData data, DateTimeOffset expiresAt, string id) where TData : class
    {
        if (!_cacheCreators.TryGetValue(typeof(TData), out var creator))
        {
            throw new NotSupportedException($"未注册用于数据类型 {typeof(TData).Name} 的交互缓存视图。请在程序启动时调用 Register 进行注册。");
        }

        return creator(data, expiresAt, id);
    }
}