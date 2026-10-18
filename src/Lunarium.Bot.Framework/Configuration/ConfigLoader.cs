using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Lunarium.Logging;

namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 读取、校验、写回并重载一个配置文件。
/// <para>
/// 校验规则写在配置模型自己身上（特性 + <see cref="IValidatableObject"/>），本类不含任何业务规则，
/// 只负责：定位文件、反序列化（拒绝未知键）、校验、原子写回、整对象替换。
/// </para>
/// <para>
/// <b>并发契约</b>：<see cref="Value"/> 只在整对象替换时变化，读取无需加锁；
/// 运行期<b>不得</b>直接修改 <see cref="Value"/> 上的属性（会绕过校验与写回），需要修改请走 <see cref="TryUpdate"/>。
/// </para>
/// <para>
/// <b>写回语义</b>：<see cref="TryUpdate"/> 以<b>磁盘当前内容</b>为基准（而不是内存快照），
/// 因此不会覆盖运行期对文件的合法手工修改；文件当前非法时更新会失败并保持原样。
/// </para>
/// </summary>
/// <typeparam name="T">配置模型类型。</typeparam>
public class ConfigLoader<T> where T : class, IValidatableObject, new()
{
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private T _value = null!;

    /// <summary>使用默认配置文件名 <c>appsettings.json</c> 加载。</summary>
    public ConfigLoader(ILogger logger)
        : this(logger, "appsettings.json")
    {
    }

    /// <summary>
    /// 加载并校验配置。失败会抛出 <see cref="ConfigValidationException"/> 或 <see cref="InvalidOperationException"/>，
    /// 使程序在启动阶段即失败。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="fileName">配置文件名，或绝对路径。</param>
    public ConfigLoader(ILogger logger, string fileName)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _logger = logger.ForContext("ConfigLoader");
        FilePath = ConfigFilePaths.Resolve(fileName);

        lock (_lock)
        {
            try
            {
                _value = Load();
                _logger.Info("⚙️ 配置文件加载并验证成功: {Path}", FilePath);
            }
            catch (Exception ex)
            {
                _logger.Critical(ex, "配置加载或验证失败");
                throw;
            }
        }
    }

    /// <summary>实际使用的配置文件绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>当前生效的配置快照。整对象替换，读取安全。</summary>
    public T Value => Volatile.Read(ref _value);

    /// <summary>离线校验一份候选配置（供测试或 <c>--check-config</c> 使用）。</summary>
    public static IReadOnlyList<string> Validate(T candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return ConfigValidation.Validate(candidate);
    }

    /// <summary>
    /// 在磁盘当前内容的基础上应用修改，校验通过后原子写回并替换快照；任一步失败则完全回滚。
    /// </summary>
    /// <returns>成功为 true；校验失败或 IO 失败为 false（详细原因已写日志）。</returns>
    public bool TryUpdate(Action<T> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_lock)
        {
            _logger.Info("⚙️ 正在尝试更新配置...");

            try
            {
                // Load() 每次都重新反序列化，得到的就是全新对象，无需再深拷贝
                var copy = Load();
                mutate(copy);

                var errors = ConfigValidation.Validate(copy);
                if (errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        _logger.Error("❌ 配置校验未通过: {Error}", error);
                    }

                    return false;
                }

                Save(copy);
                Volatile.Write(ref _value, copy);
                _logger.Info("✅ 配置已更新并写回文件。");

                OnConfigApplied(copy);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 配置更新失败，内存与文件均保持原样。");
                return false;
            }
        }
    }

    /// <summary>
    /// 从磁盘重新加载。失败时保留旧配置（不抛异常），便于运行期热重载。
    /// </summary>
    public bool TryReload()
    {
        lock (_lock)
        {
            _logger.Info("⚙️ 正在重新加载配置文件...");

            try
            {
                var config = Load();
                Volatile.Write(ref _value, config);
                _logger.Info("✅ 配置已重新加载，新配置已生效。");

                OnConfigApplied(config);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ 重新加载失败，继续使用旧配置。");
                return false;
            }
        }
    }

    /// <summary>
    /// 配置被替换（更新或重载）后的回调。派生类可借此重建派生状态
    /// （如按配置算出来的频道集合、通知下游服务刷新参数）。
    /// </summary>
    protected virtual void OnConfigApplied(T config)
    {
    }

    private T Load()
    {
        T? config;

        try
        {
            var json = File.ReadAllText(FilePath);
            config = JsonSerializer.Deserialize<T>(json, JsonConfigOptions.Read);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"加载配置失败: JSON 格式错误 - {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidOperationException("配置文件内容为空或格式不正确。");
        }

        var errors = ConfigValidation.Validate(config);
        if (errors.Count > 0)
        {
            throw new ConfigValidationException(errors);
        }

        return config;
    }

    /// <summary>
    /// 原子写入：先写同目录临时文件，再整体替换。
    /// 直接覆盖时进程若崩在中途会留下截断的配置，下一次启动就再也读不出来。
    /// </summary>
    private void Save(T config)
    {
        var json = JsonSerializer.Serialize(config, JsonConfigOptions.Write);
        var tempPath = FilePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // 清理失败不应掩盖原始异常
            }

            throw;
        }

        _logger.Info("💾 配置已写回: {Path}", FilePath);
    }
}
