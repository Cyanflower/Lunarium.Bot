// // .NET 核心库
// using System.Text.RegularExpressions;


// namespace Lunarium.Bot.SearchBot.Utils;

// /// <summary>
// /// 一个静态工具类，专门为搜索命令解析各种日期字符串。
// /// 它的核心任务是将多种人类可读的日期格式（如 "昨天", "2024-07-26", "7月26日"）
// /// 转换为标准的、统一的、与数据库存储格式一致的 UTC Unix 时间戳。
// /// 这样可以确保在进行日期范围查询时，时区和格式问题不会导致错误的结果。
// /// </summary>
// public static class DateParser
// {
//     /// <summary>
//     /// 解析日期字符串并转换为Unix时间戳。这是该类的唯一公共入口点。
//     /// </summary>
//     /// <param name="dateString">用户输入的日期字符串。</param>
//     /// <param name="isEndTime">一个布尔标志，用于指示这个日期是作为搜索的“结束日期”还是“起始日期”。
//     /// true 表示是结束日期，时间戳会调整为当天的最后一秒 (23:59:59)。
//     /// false 表示是起始日期，时间戳会调整为当天的第一秒 (00:00:00)。</param>
//     /// <returns>返回一个长整型的 Unix 时间戳。如果解析失败，则返回 null。</returns>
//     public static long? ParseToUnixTimestamp(string dateString, bool isEndTime = false)
//     {
//         // 1. 输入校验：如果字符串为空或仅包含空白，直接返回 null，避免后续不必要的处理。
//         if (string.IsNullOrWhiteSpace(dateString))
//             return null;

//         // 2. 预处理：去除首尾的空白字符，并获取当前的 UTC 时间作为基准，用于处理相对日期。
//         dateString = dateString.Trim();
//         var now = DateTime.UtcNow; // 始终使用 UTC 时间作为基准，避免时区问题。
//         DateTime? parsedDate = null;

//         try
//         {
//             // 3. 链式解析：依次尝试不同的解析方法，直到成功为止。
//             //    使用 `??` (null 合并运算符) 合并成堆的if，合成大西瓜... (
//             //    当前一个方法返回 null (表示解析失败) 时，会自动尝试下一个方法。
//             parsedDate = ParseRelativeTime(dateString, now) ??     // 尝试解析 "昨天", "3天前" 等相对时间
//                          ParseChineseFormat(dateString, now) ??      // 尝试解析 "2025年8月26日", "8月26日" 等中文格式
//                          ParseStandardFormat(dateString) ??      // 尝试解析 "2025-08-26", "2025/08/26" 等标准格式
//                          ParseMonthDayFormat(dateString, now);     // 尝试解析 "08-26", "08/26" 等月日格式

//             // 4. 结果转换：如果以上任何一种方法成功解析出了日期 (parsedDate 不为 null)
//             if (parsedDate.HasValue)
//             {
//                 // 就调用转换方法，将其变为最终的 Unix 时间戳。
//                 return ConvertToUnixTimestamp(parsedDate.Value, isEndTime);
//             }
//         }
//         catch (Exception)
//         {
//             // 5. 异常处理：如果在解析过程中发生任何意料之外的错误（例如非常规的日期格式导致异常），
//             //    捕获异常并返回 null，保证程序的健壮性，防止程序崩溃。
//             return null;
//         }

//         // 如果所有解析方法都失败了，返回 null。
//         return null;
//     }

//     /// <summary>
//     /// 解析相对时间格式，如 "昨天", "前天", "N天/周/月前"。
//     /// </summary>
//     private static DateTime? ParseRelativeTime(string input, DateTime now)
//     {
//         // `now` 是 UtcNow，所以 `now.Date` 已经是 UTC 日期的 00:00:00，可以直接进行加减。
//         if (Regex.IsMatch(input, @"^昨天$")) return now.Date.AddDays(-1);
//         if (Regex.IsMatch(input, @"^前天$")) return now.Date.AddDays(-2);
        
//         var daysMatch = Regex.Match(input, @"^(\d+)天前$");
//         if (daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out int days))
//             return now.Date.AddDays(-days);

//         var weeksMatch = Regex.Match(input, @"^(\d+)周前$");
//         if (weeksMatch.Success && int.TryParse(weeksMatch.Groups[1].Value, out int weeks))
//             return now.Date.AddDays(-weeks * 7);

//         var monthsMatch = Regex.Match(input, @"^(\d+)个?月前$");
//         if (monthsMatch.Success && int.TryParse(monthsMatch.Groups[1].Value, out int months))
//             return now.Date.AddMonths(-months);

//         return null;
//     }

//     /// <summary>
//     /// 解析中文日期格式，如 "2024年1月1日" 或 "1月15日"。
//     /// </summary>
//     private static DateTime? ParseChineseFormat(string input, DateTime now)
//     {
//         // 匹配 "YYYY年M月D日" 格式
//         var fullChineseMatch = Regex.Match(input, @"^(\d{4})年(\d{1,2})月(\d{1,2})日$");
//         if (fullChineseMatch.Success &&
//             int.TryParse(fullChineseMatch.Groups[1].Value, out int year) &&
//             int.TryParse(fullChineseMatch.Groups[2].Value, out int month) &&
//             int.TryParse(fullChineseMatch.Groups[3].Value, out int day) &&
//             IsValidDate(year, month, day))
//         {
//             // 直接创建指定为 UTC 的 DateTime 对象。
//             return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
//         }

//         // 匹配 "M月D日" 格式
//         var monthDayChineseMatch = Regex.Match(input, @"^(\d{1,2})月(\d{1,2})日$");
//         if (monthDayChineseMatch.Success &&
//             int.TryParse(monthDayChineseMatch.Groups[1].Value, out int mdMonth) &&
//             int.TryParse(monthDayChineseMatch.Groups[2].Value, out int mdDay))
//         {
//             int mdYear = now.Year;
//             // 先假设是今年的这个日期
//             var targetDate = new DateTime(mdYear, mdMonth, mdDay, 0, 0, 0, DateTimeKind.Utc);
//             // 如果这个日期比今天还晚，说明用户想表达的应该是去年的这个日期
//             if (targetDate > now.Date)
//             {
//                 // 创建去年的日期时，同样必须指定为 UTC！
//                 targetDate = new DateTime(mdYear - 1, mdMonth, mdDay, 0, 0, 0, DateTimeKind.Utc);
//             }
            
//             if (IsValidDate(targetDate.Year, targetDate.Month, targetDate.Day))
//                 return targetDate;
//         }

//         return null;
//     }

//     /// <summary>
//     /// 解析标准日期格式，如 "2024-07-26" 或 "2024/07/26"。
//     /// </summary>
//     private static DateTime? ParseStandardFormat(string input)
//     {
//         var standardMatch = Regex.Match(input, @"^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$");
//         if (standardMatch.Success &&
//             int.TryParse(standardMatch.Groups[1].Value, out int year) &&
//             int.TryParse(standardMatch.Groups[2].Value, out int month) &&
//             int.TryParse(standardMatch.Groups[3].Value, out int day) &&
//             IsValidDate(year, month, day))
//         {
//             // 直接创建指定为 UTC 的 DateTime 对象。
//             return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
//         }
//         return null;
//     }

//     /// <summary>
//     /// 解析月日格式，如 "07-26" 或 "7/26"。
//     /// </summary>
//     private static DateTime? ParseMonthDayFormat(string input, DateTime now)
//     {
//         var monthDayMatch = Regex.Match(input, @"^(\d{1,2})[-/](\d{1,2})$");
//         if (monthDayMatch.Success &&
//             int.TryParse(monthDayMatch.Groups[1].Value, out int month) &&
//             int.TryParse(monthDayMatch.Groups[2].Value, out int day))
//         {
//             int year = now.Year;
//             var targetDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
//             // 逻辑同中文月日格式，如果日期已过，则认为是去年
//             if (targetDate > now.Date)
//             {
//                 // 创建去年的日期时，同样必须指定为 UTC！
//                 targetDate = new DateTime(year - 1, month, day, 0, 0, 0, DateTimeKind.Utc);
//             }

//             if (IsValidDate(targetDate.Year, targetDate.Month, targetDate.Day))
//                 return targetDate;
//         }
//         return null;
//     }

//     /// <summary>
//     /// 验证给定的年月日是否能构成一个有效的日期。
//     /// 例如，可以防止 "2024年2月30日" 这样的无效日期导致程序异常。
//     /// </summary>
//     private static bool IsValidDate(int year, int month, int day)
//     {
//         // 简单但高效的验证：检查年份、月份范围，以及天数是否在该年该月的有效范围内（考虑了闰年）。
//         return year >= 1 && year <= 9999 &&
//                month >= 1 && month <= 12 &&
//                day >= 1 && day <= DateTime.DaysInMonth(year, month);
//     }

//     /// <summary>
//     /// 📏 将一个确定的 DateTime 对象转换为 Unix 时间戳，并根据 isEndTime 参数调整时间。
//     /// 这是实现“按天搜索”功能的关键所在。
//     /// </summary>
//     private static long ConvertToUnixTimestamp(DateTime dateTime, bool isEndTime)
//     {
//         // 确保传入的 dateTime 是 UTC
//         if (dateTime.Kind != DateTimeKind.Utc)
//         {
//             dateTime = DateTime.SpecifyKind(dateTime.Date, DateTimeKind.Utc);
//         }

//         DateTime finalDateTime;
//         if (isEndTime)
//         {
//             // 如果是“结束日期”，我们需要包含这一整天。
//             // 例如用户输入 "2024-07-26"，我们需要的时间点是当天结束的那一刻。
//             // 技巧：先获取当天 0 点 -> 加一天变成第二天 0 点 -> 再减一秒，就得到了当天的 23:59:59。
//             finalDateTime = dateTime.Date.AddDays(1).AddSeconds(-1);
//         }
//         else
//         {
//             // 如果是“起始日期”，我们需要从这一天的开始算起。
//             // `.Date` 属性会直接返回当天的 00:00:00。
//             finalDateTime = dateTime.Date;
//         }

//         return new DateTimeOffset(finalDateTime).ToUnixTimeSeconds();
//     }
// }