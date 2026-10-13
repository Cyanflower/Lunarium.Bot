namespace Lunarium.Bot.Logger;

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
            TextTimestampMode.UnixMs => LogTimestampConfig.GetTimestamp().ToUnixTimeMilliseconds().ToString(),
            TextTimestampMode.ISO8601 => $"{LogTimestampConfig.GetTimestamp():O}",
            TextTimestampMode.Custom => LogTimestampConfig.GetTimestamp().ToString(TimestampFormatConfig.TextCustomFormat),
            _ => $"{LogTimestampConfig.GetTimestamp():O}"
        };
        return formattedTimestamp;
    }
}