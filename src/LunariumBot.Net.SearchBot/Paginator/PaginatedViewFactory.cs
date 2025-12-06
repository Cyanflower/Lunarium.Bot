
using LunariumBot.Net.SearchBot.Models;

namespace LunariumBot.Net.SearchBot.Paginator;

public static class PaginatedViewFactory
{
    private static readonly Dictionary<Type, Func<object, DateTimeOffset, string, IPaginatedView>> _viewCreators = new();

    static PaginatedViewFactory()
    {
        // 向字典注册所有类型的 IPaginatedView 的 new 方法委托
        Register<BlockedInfo>((items, expires, id) => new BlockPaginatedView(items, expires, id));
        Register<SubscriptionData>((items, expires, id) => new SubscriptionPaginatedView(items, expires, id));
        Register<FavoriteTypeInfo>((items, expires, id) => new FavoriteTypePaginatedView(items, expires, id));
        Register<FavoriteDisplayItem>((items, expires, id) => new FavoriteDisplayPaginatedView(items, expires, id));
    }

    /// <summary>
    /// 注册委托函数
    /// </summary>
    /// <typeparam name="TData">类型参数: IPaginatedView 的类型</typeparam>
    /// <param name="creator"> 该类型的 IPaginatedView 的 new 方法委托</param>
    static private void Register<TData>(Func<IEnumerable<TData>, DateTimeOffset, string, IPaginatedView> creator) where TData : class
    {
        // 接受泛型参数, 并将对应的类型的 IPaginatedView 的 new 方法委托存入字典
        _viewCreators[typeof(TData)] = (data, expires, id) => creator((IEnumerable<TData>)data, expires, id);
    }

    public static IPaginatedView Create<TData>(IEnumerable<TData> data, DateTimeOffset expiresAt, string id) where TData : class
    {
        // 根据 TData 的类型查找对应的 new 方法委托
        if (!_viewCreators.TryGetValue(typeof(TData), out var creator))
        {
            throw new NotSupportedException($"未注册用于数据类型 {typeof(TData).Name} 的分页视图。");
        }
        
        // 执行 new 方法的委托并返回结果
        return creator(data, expiresAt, id);
    }
}