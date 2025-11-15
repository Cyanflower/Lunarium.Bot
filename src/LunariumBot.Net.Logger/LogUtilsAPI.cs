namespace LunariumBot.Net.Logger;

public static class LogUtils
{
    public static DateTimeOffset GetLogSystemTimestamp()
    {
        return LogTimestampConfig.GetTimestamp();
    }
    public static string GetLogSystemFormattedTimestamp()
    {
        var formattedTimestamp = TimestampFormatConfig.TextMode switch
        {
            TextTimestampMode.Unix => LogTimestampConfig.GetTimestamp().ToUnixTimeSeconds().ToString(),
            TextTimestampMode.UnixMs => LogTimestampConfig.GetTimestamp().ToUnixTimeSeconds().ToString(),
            TextTimestampMode.ISO8601 => $"{LogTimestampConfig.GetTimestamp():O}",
            TextTimestampMode.Custom => string.Format($"0:{TimestampFormatConfig.TextCustomFormat}", LogTimestampConfig.GetTimestamp()),
            _ => $"{LogTimestampConfig.GetTimestamp():O}"
        };
        return formattedTimestamp;
    }
}