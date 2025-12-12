// .NET 核心库
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
// 引入 System.IO
using System.IO; 

// NuGet 包依赖
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.Interactions;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.ReleaseBot.AuthUtils;
using LunariumBot.Net.ReleaseBot.Models;
using Discord.WebSocket;
using LunariumBot.Net.ReleaseBot.InteractionsCache;
using LunariumBot.Net.ReleaseBot.ApplicationServices;
using System.Text.Json;
using LunariumBot.Net.ReleaseBot.Utils;

namespace LunariumBot.Net.ReleaseBot.Listener;

public class ListenerService
{
    private readonly DiscordSocketClient _socketClient;
    private readonly ILogger _logger;
    private readonly ConfigLoader _configLoader;
    
    // 数据锁，保证内存集合操作和文件IO的线程安全
    private readonly object _dataLock = new();
    private HashSet<ulong> _disableAutoMessageUsers = new();
    private readonly ConcurrentDictionary<ulong, byte> _processedThreads = new();

    // 定义配置文件路径
    private readonly string _configDirectory;
    private readonly string _configPath;

    /// <summary>
    /// 事件处理器标志位
    /// </summary>
    private bool _handlersAttached = false;

    public ListenerService(ILogger logger, DiscordSocketClient socketClient, ConfigLoader configLoader)
    {
        _logger = logger.ForContext("ListenerService");
        _socketClient = socketClient;
        _configLoader = configLoader;

        // 初始化路径: 可执行文件目录/autoMessageConfig/data.json
        _configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoMessageConfig");
        _configPath = Path.Combine(_configDirectory, "data.json");
        
        // [DEBUG] 打印路径信息
        _logger.Debug($"配置目录: {_configDirectory}");
        _logger.Debug($"配置文件路径: {_configPath}");

        // 构造时立即加载配置
        LoadConfig();
    }

    /// <summary>
    /// 注册所有需要监听的 Discord 客户端事件处理器。
    /// </summary>
    public void AddEventHandlers()
    {
        if (_handlersAttached)
        {
            _logger.Debug("事件处理器已存在，跳过注册。");
            return;
        }
        try
        {
            _handlersAttached = true;
            _socketClient.ThreadCreated += ThreadCreatedHandler;
            _logger.Info("✅ 监听器事件处理方法已添加");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 监听器事件处理方法添加失败, 正在执行清理...");
            RemoveEventHandlers();
            throw;
        }
    }

    public void RemoveEventHandlers()
    {
        if (!_handlersAttached)
        {
            _logger.Debug("事件处理器未注册，无需移除。");
            return;
        }
        try
        {
            _socketClient.ThreadCreated -= ThreadCreatedHandler;
            _logger.Info("✅ 监听器事件处理方法已移除");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "❌ 监听器事件处理方法移除失败");
            throw;
        }
        finally
        {
            _handlersAttached = false;
        }
    }

    private Task ThreadCreatedHandler(SocketThreadChannel socketThreadChannel)
    {
        
        var timeDifference = DateTimeOffset.UtcNow - socketThreadChannel.CreatedAt;

        if (timeDifference.TotalSeconds > 120)
        {
            _logger.Debug($"[ThreadCreatedAsync] 忽略旧帖子: {socketThreadChannel.Id} " +
                        $"(创建于: {socketThreadChannel.CreatedAt}, 距今: {timeDifference.TotalSeconds:F2}秒)");
            return Task.CompletedTask;
        }
        if (_processedThreads.TryAdd(socketThreadChannel.Id, 0))
        {     // [DEBUG] 记录原始事件触发
            _logger.Debug($"收到 ThreadCreated 事件: {socketThreadChannel.Name} (ID: {socketThreadChannel.Id})");
            _ = Task.Run(async () => { await ThreadCreatedAsync(socketThreadChannel); });
            return Task.CompletedTask;
        }
        else
        {
            _logger.Debug($"[去重] 忽略重复的 ThreadCreated 事件 (ID: {socketThreadChannel.Id})");
            return Task.CompletedTask;
        }
    }
    
    public async Task ThreadCreatedAsync(SocketThreadChannel socketThreadChannel)
    {
        try
        {
            await Task.Delay(1000);
            var parentId = socketThreadChannel.ParentChannel?.Id ?? 0;
            var ownerId = socketThreadChannel.Owner?.Id ?? 0;

            // [DEBUG] 详细上下文日志
            _logger.Debug($"[ThreadCreatedAsync] 开始处理 - 线程ID: {socketThreadChannel.Id}, 父频道ID: {parentId}, 作者ID: {ownerId}");

            // 检查是否在排除的频道列表中
            if (!_configLoader.ValidChannelSet.Contains(parentId))
            {
                _logger.Debug($"[ThreadCreatedAsync] 父频道 {parentId} 不在包含列表(ValidChannelSet)中，停止处理。");
                return;
            }

            // 检查用户配置
            if (IsUserDisabled(ownerId))
            {
                _logger.Debug($"[ThreadCreatedAsync] 用户 {ownerId} 已禁用自动消息功能，停止处理。");
                return;
            }

            // [DEBUG] 硬编码过滤
            // if (ownerId != 867658156510150716)
            // {
            //     _logger.Debug($"[ThreadCreatedAsync] 未命中硬编码屏蔽用户 ID (867658156510150716)，停止处理。");
            //     return;
            // }

            _logger.Debug($"[ThreadCreatedAsync] 通过所有检查，准备发送交互消息...");

            var component = new ComponentBuilderV2()
                .WithContainer(container => container
                    .WithAccentColor(Color.Green)
                    .WithTextDisplay($"""
                        ### 📑 作品发布
                        > 本 BOT 为反自动化爬虫脚本的防盗卡措施, 提供交互性作品发布功能
                        > 作者可选择通过本BOT发布作品, 用户通过交互性按钮获取作品进行下载
                        > 如果选择不使用本BOT, 也建议首楼尽量放置图片, 贴内放置作品, 以避免简易爬虫批量盗取首楼作品
                        作者可选获取作品方式:
                        > -# - 无限制: 用户通过点击按钮即可下载作品, 无任何限制
                        > -# - 点赞或评论: 用户对首楼进行反应或发送回复后可下载作品
                        > -# - 提取码: 作者在楼内布置提取码, 用户输入提取码后可下载作品
                        -# 如使用中有任何问题请前往: [反馈频道](https://discord.com/channels/1291925535324110879/1447557807401340928)
                        """)

                    .WithSeparator()
                    .WithSection(section => section
                        .WithTextDisplay("从贴内移除本条消息")
                        .WithAccessory(new ButtonBuilder("移除消息", $"DeleteSelf", ButtonStyle.Danger, emote: new Emoji("⚠️")))
                    )

                    .WithSeparator()
                    .WithSection(section => section
                        .WithTextDisplay("之后将不再在您的帖子内自动弹出本消息")
                        .WithAccessory(new ButtonBuilder("不再提示", $"DisableAutoMessage", ButtonStyle.Danger, emote: new Emoji("⚠️")))
                    )

                    .WithSeparator()
                    .WithSection(section => section
                        .WithTextDisplay("打开发布作品交互面板")
                        .WithAccessory(new ButtonBuilder("发布作品", $"ReleasePanel", ButtonStyle.Success, emote: new Emoji("📝")))
                    )

                );
            
            await socketThreadChannel.SendMessageAsync(components: component.Build());
            _logger.Debug($"[ThreadCreatedAsync] 消息发送成功: 线程 {socketThreadChannel.Id}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"[ThreadCreatedAsync] 处理线程 {socketThreadChannel.Id} 时发生异常");
        }
    }

    #region Config Persistence (配置持久化)

    /// <summary>
    /// 从磁盘加载配置
    /// </summary>
    private void LoadConfig()
    {
        lock (_dataLock)
        {
            try
            {
                _logger.Debug("[LoadConfig] 开始加载配置...");

                if (!Directory.Exists(_configDirectory))
                {
                    _logger.Debug($"[LoadConfig] 目录不存在，创建目录: {_configDirectory}");
                    Directory.CreateDirectory(_configDirectory);
                }

                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    // 如果文件为空或格式错误，Deserialize 可能抛出异常或返回 null
                    var data = JsonSerializer.Deserialize<HashSet<ulong>>(json);
                    
                    if (data != null)
                    {
                        _disableAutoMessageUsers = data;
                        _logger.Info($"已加载 {_disableAutoMessageUsers.Count} 个禁用自动消息的用户配置。");
                    }
                    else 
                    {
                        _logger.Debug("[LoadConfig] 反序列化结果为 null。");
                    }
                }
                else
                {
                    // 文件不存在时，初始化新的文件
                    _logger.Debug("[LoadConfig] 配置文件不存在，初始化新文件。");
                    SaveConfigInternal(); 
                }
            }
            catch (JsonException jex)
            {
                _logger.Error(jex, "配置文件 JSON 格式损坏，将使用空配置。建议检查文件内容。");
                // 此时保持 _disableAutoMessageUsers 为空，避免覆盖损坏文件以便人工检查，
                // 或者在这里根据策略决定是否重写文件。
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "加载用户配置失败");
            }
        }
    }

    /// <summary>
    /// 保存配置到磁盘 (内部调用，需在锁内)
    /// </summary>
    private void SaveConfigInternal()
    {
        try
        {
            _logger.Debug($"[SaveConfigInternal] 正在保存配置，当前禁用用户数: {_disableAutoMessageUsers.Count}");

            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            // 格式化 JSON 方便人工阅读调试
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_disableAutoMessageUsers, options);
            
            File.WriteAllText(_configPath, json);
            _logger.Debug("[SaveConfigInternal] 配置写入完成。");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存用户配置失败");
        }
    }

    // --- 封装对 _disableAutoMessageUsers 的操作，确保线程安全 ---

    /// <summary>
    /// 检查用户是否禁用了自动消息
    /// </summary>
    public bool IsUserDisabled(ulong userId)
    {
        lock (_dataLock)
        {
            var result = _disableAutoMessageUsers.Contains(userId);
            // 这里通常不需要日志，除非在调试极其频繁的锁竞争，否则会刷屏
            return result;
        }
    }

    /// <summary>
    /// 添加禁用用户并保存
    /// </summary>
    public void DisableUser(ulong userId)
    {
        lock (_dataLock)
        {
            if (_disableAutoMessageUsers.Add(userId))
            {
                _logger.Debug($"[DisableUser] 添加用户 {userId} 到禁用列表，准备保存。");
                SaveConfigInternal();
                _logger.Info($"用户 {userId} 已添加到禁用列表。");
            }
            else
            {
                _logger.Debug($"[DisableUser] 用户 {userId} 已存在于禁用列表中，跳过。");
            }
        }
    }

    /// <summary>
    /// 移除禁用用户并保存
    /// </summary>
    public void EnableUser(ulong userId)
    {
        lock (_dataLock)
        {
            if (_disableAutoMessageUsers.Remove(userId))
            {
                _logger.Debug($"[EnableUser] 从禁用列表移除用户 {userId}，准备保存。");
                SaveConfigInternal();
                _logger.Info($"用户 {userId} 已从禁用列表中移除。");
            }
            else
            {
                _logger.Debug($"[EnableUser] 用户 {userId} 不在禁用列表中，跳过。");
            }
        }
    }

    #endregion
}