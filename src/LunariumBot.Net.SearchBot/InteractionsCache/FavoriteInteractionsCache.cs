using Discord;
using LunariumBot.Net.Extensions;
using LunariumBot.Net.SearchBot.Models;

namespace LunariumBot.Net.SearchBot.InteractionsCache;

public class FavoriteInteractionCache : BaseInteractionsCache<FavoriteInteractionState>
{
    // ID 前缀常量
    public const string ActionSelect = "Fav:Select";
    public const string ActionFirst = "Fav:First";
    public const string ActionPrev = "Fav:Prev";
    public const string ActionNext = "Fav:Next";
    public const string ActionLast = "Fav:Last";
    public const string ActionNew = "Fav:New";
    public const string ActionPushOn = "Fav:Push:On";
    public const string ActionPushOff = "Fav:Push:Off";
    public const string ActionConfirm = "Fav:Confirm";
    public const string ActionCancel = "Fav:Cancel";

    private bool _isCancel = false;
    private bool _isComplete = false;
    private bool IsFinished => _isCancel || _isComplete;

    public FavoriteInteractionCache(FavoriteInteractionState data, DateTimeOffset expiresAt, string id) 
        : base(data, expiresAt, id) { }

    public override MessageComponent Render()
    {
        var builder = new ComponentBuilderV2();

        builder.WithContainer(container =>
        {
            if (_isCancel)
            {
                container.WithAccentColor(Color.Red);
            }
            else if (_isComplete)
            {
                container.WithAccentColor(Color.Green);
            }
            else
            {
                container.WithAccentColor(Color.Blue);
            }

            // --- 顶部 Section: 标题 ---
            container.WithSection(section =>
            {
                section.WithTextDisplay($"""
                    ## 收藏订阅
                    您正在收藏帖子: **{_data.ThreadName}**
                    请选择目标收藏夹并配置推送选项。
                    """, 1001);

                if (!string.IsNullOrEmpty(_data.AuthorAvatarUrl))
                {
                    section.WithAccessory(new ThumbnailBuilder().WithMedia(_data.AuthorAvatarUrl));
                }
            });

            container.WithSeparator();

            // --- Row 1: 下拉菜单 (仅当有收藏夹时显示) ---
            if (_data.AllFolders.Count > 0)
            {
                var options = GetPageItems()
                    .Select(f => new SelectMenuOptionBuilder(
                        label: f.TypeName,
                        value: f.FavoriteId.ToString(),
                        description: $"创建于: {f.CreatedAt:yyyy-MM-dd}",
                        // 如果该ID在已选列表中，设为默认选中
                        isDefault: _data.SelectedFolderId.Contains(f.FavoriteId.ToString())
                    ))
                    .ToList();

                // 如果当前页有数据才渲染菜单
                if (options.Any())
                {
                    container.WithActionRow(row =>
                    {
                        row.WithSelectMenu(
                            customId: $"{ActionSelect}:{CacheId}",
                            options: options,
                            placeholder: "选择收藏夹...",
                            minValues: 1,
                            maxValues: 1,
                            disabled: IsFinished
                        );
                    });
                    container.WithSeparator();
                }
            }
            else
            {
                // 没有收藏夹时的提示
                container.WithTextDisplay("-# 您当前没有任何收藏夹，请点击下方按钮新建。", 1002);
                container.WithSeparator();
            }

            if (_data.TotalPages > 1)
            {
                container.WithTextDisplay("-# 新建收藏夹后下拉选项列表会自动前往新收藏夹所在页");
            }

            // --- Row 2: 翻页 & 新建 ---
            container.WithActionRow(row =>
            {
                // 只有当文件夹数量超过一页时，才显示翻页按钮
                if (_data.TotalPages > 1)
                {
                    row.WithButton(
                        label: "首页",
                        customId: $"{ActionFirst}:{CacheId}",
                        style: ButtonStyle.Secondary,
                        emote: new Emoji("⏮️"),
                        disabled: (_data.CurrentFolderPage == 0) || IsFinished
                    );

                    row.WithButton(
                        label: "上一页",
                        customId: $"{ActionPrev}:{CacheId}",
                        style: ButtonStyle.Secondary,
                        emote: new Emoji("⏪"),
                        disabled: (_data.CurrentFolderPage == 0) || IsFinished
                    );

                    row.WithButton(
                        label: "下一页",
                        customId: $"{ActionNext}:{CacheId}",
                        style: ButtonStyle.Secondary,
                        emote: new Emoji("⏩"),
                        disabled: (_data.CurrentFolderPage >= _data.TotalPages - 1) || IsFinished
                    );

                    row.WithButton(
                        label: "尾页",
                        customId: $"{ActionLast}:{CacheId}",
                        style: ButtonStyle.Secondary,
                        emote: new Emoji("⏮️"),
                        disabled: (_data.CurrentFolderPage >= _data.TotalPages - 1) || IsFinished
                    );
                }

                row.WithButton(
                    label: "新建收藏夹",
                    customId: $"{ActionNew}:{CacheId}",
                    style: ButtonStyle.Primary,
                    emote: new Emoji("➕"),
                    disabled: IsFinished
                );
            });

            if (_data.TotalPages > 1) container.WithTextDisplay("-# 新建收藏夹后下拉选项列表会自动前往新收藏夹所在页");

            container.WithSeparator();

            if (_data.IsPushEnabled)
            {
                container.WithTextDisplay("当前作品更新推送设置: 🔔 允许推送\n-# 本更新推送只检测PNG角色卡或JSON文件形式的更新");
            }
            else
            {
                container.WithTextDisplay("当前作品更新推送设置: 🔕 禁止推送\n-# 本更新推送只检测PNG角色卡或JSON文件形式的更新");
            }

            // --- Row 3: 推送开关 ---
            // 两个按钮并排, 根据状态改变颜色
            container.WithActionRow(row =>
            {
                row.WithButton(
                    label: "允许推送",
                    customId: $"{ActionPushOn}:{CacheId}",
                    style: _data.IsPushEnabled ? ButtonStyle.Success : ButtonStyle.Secondary,
                    emote: new Emoji("🔔"),
                    disabled: _data.IsPushEnabled  || IsFinished
                );

                row.WithButton(
                    label: "禁止推送",
                    customId: $"{ActionPushOff}:{CacheId}",
                    style: !_data.IsPushEnabled ? ButtonStyle.Danger : ButtonStyle.Secondary,
                    emote: new Emoji("🔕"),
                    disabled: !_data.IsPushEnabled  || IsFinished
                );
            });


            container.WithSeparator();

            bool canConfirm = string.IsNullOrWhiteSpace(_data.SelectedFolderId);
            if (!canConfirm)
            {
                container.WithTextDisplay("-# 确认收藏之前必须选择收藏夹");
            }
            // --- Row 4: 确认/取消 ---
            if (_isCancel)
            {
                container.WithTextDisplay("## 操作已取消");
            }
            else if (_isComplete)
            {
                container.WithTextDisplay("## 操作已完成");
            }
            else
            {
                container.WithActionRow(row =>
                {
                    // 必须选择了至少一个收藏夹才能确认

                    row.WithButton(
                        label: "确认收藏",
                        customId: $"{ActionConfirm}:{CacheId}",
                        style: ButtonStyle.Success,
                        emote: new Emoji("✅"),
                        disabled: !canConfirm
                    );

                    row.WithButton(
                        label: "取消",
                        customId: $"{ActionCancel}:{CacheId}",
                        style: ButtonStyle.Secondary,
                        emote: new Emoji("❌")
                    );
                });
            }
        });

        return builder.Build();
    }

    public void UpdateSelected(string selected)
    {
        _data.SelectedFolderId = selected;
    }

    public void UpdateFolders(FavoriteTypeInfo favoriteTypeInfo)
    {
        _data.AllFolders.Add(favoriteTypeInfo);
        UpdateSelected(favoriteTypeInfo.FavoriteId.ToString());
    }
    public void Prev()
    {
        if (_data.CurrentFolderPage > 0) _data.CurrentFolderPage--;
    }
    public void Next()
    {
        if (_data.CurrentFolderPage < _data.TotalPages - 1) _data.CurrentFolderPage++;
    }
    public void ToFirst()
    {
        _data.CurrentFolderPage = 0;
    }
    public void ToLast()
    {
        _data.CurrentFolderPage = _data.TotalPages > 0 ? _data.TotalPages - 1 : 0;
    }
    public void EnablePush()
    {
        _data.IsPushEnabled = true;
    }
    public void DisablePush()
    {
        _data.IsPushEnabled = false;
    }
    public void Cancel()
    {
        _isCancel = true;
    }
    public FavoriteInfoDb Confirm()
    {
        _isComplete = true;
        return new()
        {
            UserId = _data.UserId.ToDbId(),
            ThreadId = _data.ThreadId.ToDbId(),
            FavoriteAs = long.Parse(_data.SelectedFolderId),
            FavoriteTypeName = _data.AllFolders.Where(info => info.FavoriteId == long.Parse(_data.SelectedFolderId)).First().TypeName,
            AllowPush = _data.IsPushEnabled,
            AddedAt = DateTimeOffset.UtcNow
        };
    }
    
    /// 辅助方法: 根据当前页码获取用于显示在下拉菜单中的文件夹列表子集
    /// 返回当前页对应的 FavoriteTypeInfo 集合
    private IEnumerable<FavoriteTypeInfo> GetPageItems()
    {
        // 快速检查: 如果总列表为空, 直接返回空集
        if (_data.AllFolders.Count == 0) return Enumerable.Empty<FavoriteTypeInfo>();

        // 计算起始索引
        // 公式: 当前页码(0-based) * 每页容量(25)
        int start = _data.CurrentFolderPage * FavoriteInteractionState.PageSize;

        // 计算获取数量
        // 逻辑：这里需要获取 PageSize 个元素, 但如果是最后一页
        // 剩余元素可能不足 25 个, 所以取 "PageSize" 和 "剩余元素数量" 中的较小值
        // 这样可以有效防止数组越界
        int count = Math.Min(FavoriteInteractionState.PageSize, _data.AllFolders.Count - start);
        
        // 避免数组越界
        // 如果计算出的起始索引已经超过或等于总数量(例如在极端并发或状态不同步情况下)
        // 返回空集以保证安全
        if (start >= _data.AllFolders.Count) return Enumerable.Empty<FavoriteTypeInfo>();
        
        // 截取列表
        // 使用 List.GetRange(index, count) 获取切片
        return _data.AllFolders.GetRange(start, count);
    }
}