namespace Lunarium.Bot.Framework.Configuration;

/// <summary>
/// 配置文件定位。优先当前工作目录（兼容 <c>dotnet run</c>），其次程序目录（兼容发布后的部署）。
/// </summary>
internal static class ConfigFilePaths
{
    public static string Resolve(string fileName)
    {
        // 显式传入绝对路径时不做任何搜索（测试与工具用）
        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName)
                ? fileName
                : throw new FileNotFoundException($"配置文件不存在: {fileName}");
        }

        string[] candidates =
        [
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
        ];

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var searched = string.Join(
            Environment.NewLine,
            candidates.Distinct(StringComparer.Ordinal).Select(path => $"  - {path}"));

        throw new FileNotFoundException(
            $"配置文件 {fileName} 不存在，已查找以下位置：{Environment.NewLine}{searched}");
    }
}
