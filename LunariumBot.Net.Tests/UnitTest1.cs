using System;
using Xunit;
using SearchBot.Utils; // 💖 确保引用了你的 DateParser 命名空间

namespace LunariumBot.Net.Tests;

public class DateParserTests
{
    // --- 测试相对时间格式 ---
    // 使用 [Theory] 和 [InlineData] 可以用一个方法测试多种输入情况，超级高效！
    [Theory]
    [InlineData("昨天", -1)]
    [InlineData("前天", -2)]
    [InlineData("7天前", -7)]
    [InlineData("1周前", -7)]
    [InlineData("2周前", -14)]
    [InlineData("1个月前", -1, true)] // true 表示这是一个月份测试
    [InlineData("3个月前", -3, true)] // 测试带 "个" 的情况
    [InlineData("3月前", -3, true)]  // 测试不带 "个" 的情况
    public void ParseToUnixTimestamp_ShouldHandleRelativeTime_Correctly(string input, int value, bool isMonth = false)
    {
        // Arrange (准备)
        var today = DateTime.UtcNow.Date; // 以 UTC 的今日零点为基准
        DateTime expectedDate;
        if (isMonth)
        {
            expectedDate = today.AddMonths(value);
        }
        else
        {
            expectedDate = today.AddDays(value);
        }
        // 我们期望的是这一天开始的 Unix 时间戳
        var expectedTimestamp = new DateTimeOffset(expectedDate).ToUnixTimeSeconds();

        // Act (行动)
        var result = DateParser.ParseToUnixTimestamp(input, isEndTime: false);

        // Assert (断言)
        Assert.NotNull(result);
        Assert.Equal(expectedTimestamp, result.Value);
    }

    // --- 测试标准格式 (YYYY-MM-DD 和 YYYY/MM/DD) ---
    [Theory]
    [InlineData("2024-05-20")]
    [InlineData("2023/12/31")]
    public void ParseToUnixTimestamp_ShouldHandleStandardFormat_Correctly(string input)
    {
        // Arrange
        // 从输入字符串中解析出年月日
        var parts = input.Split('-', '/');
        var year = int.Parse(parts[0]);
        var month = int.Parse(parts[1]);
        var day = int.Parse(parts[2]);
        
        var expectedDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var expectedTimestamp = new DateTimeOffset(expectedDate).ToUnixTimeSeconds();

        // Act
        var result = DateParser.ParseToUnixTimestamp(input, isEndTime: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTimestamp, result.Value);
    }

    // --- 测试中文格式 ---
    [Theory]
    [InlineData("2025年1月1日", 2025, 1, 1)]
    [InlineData("2022年11月30日", 2022, 11, 30)]
    public void ParseToUnixTimestamp_ShouldHandleFullChineseFormat_Correctly(string input, int year, int month, int day)
    {
        // Arrange
        var expectedDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var expectedTimestamp = new DateTimeOffset(expectedDate).ToUnixTimeSeconds();

        // Act
        var result = DateParser.ParseToUnixTimestamp(input, isEndTime: false);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedTimestamp, result.Value);
    }

    // --- 测试月日格式（标准 & 中文），特别是跨年的情况 ---
    // 这个测试特别重要，因为它验证了我们修复的那个小 bug！
    [Fact]
    public void ParseToUnixTimestamp_ShouldHandleMonthDayFormat_AcrossYearsCorrectly()
    {
        // Arrange
        var today = DateTime.UtcNow.Date;
        
        // 场景1: 输入一个比今天早的日期，应该解析为今年
        var pastDateInThisYear = today.AddDays(-10);
        var inputPast = $"{pastDateInThisYear:MM-dd}"; // 格式化为 "MM-dd"
        var expectedDatePast = new DateTime(today.Year, pastDateInThisYear.Month, pastDateInThisYear.Day, 0, 0, 0, DateTimeKind.Utc);
        var expectedTimestampPast = new DateTimeOffset(expectedDatePast).ToUnixTimeSeconds();
        
        // 场景2: 输入一个比今天晚的日期，应该解析为去年
        var futureDate = today.AddDays(10);
        var inputFuture = $"{futureDate:M月d日}"; // 格式化为 "M月d日"
        var expectedDateFuture = new DateTime(today.Year - 1, futureDate.Month, futureDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var expectedTimestampFuture = new DateTimeOffset(expectedDateFuture).ToUnixTimeSeconds();

        // Act
        var resultPast = DateParser.ParseToUnixTimestamp(inputPast);
        var resultFuture = DateParser.ParseToUnixTimestamp(inputFuture);

        // Assert
        Assert.NotNull(resultPast);
        Assert.Equal(expectedTimestampPast, resultPast.Value);
        Assert.NotNull(resultFuture);
        Assert.Equal(expectedTimestampFuture, resultFuture.Value);
    }

    // --- 测试 isEndTime 参数是否正确生成一天的开始和结束时间戳 ---
    [Fact]
    public void ParseToUnixTimestamp_ShouldHandle_IsEndTime_ParameterCorrectly()
    {
        // Arrange
        var input = "2024-07-26";
        var date = new DateTime(2024, 7, 26, 0, 0, 0, DateTimeKind.Utc);

        // 期望的开始时间戳 (2024-07-26 00:00:00 UTC)
        var expectedStartTimestamp = new DateTimeOffset(date).ToUnixTimeSeconds();
        
        // 期望的结束时间戳 (2024-07-26 23:59:59 UTC)
        var expectedEndTimestamp = new DateTimeOffset(date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds();

        // Act
        var resultStart = DateParser.ParseToUnixTimestamp(input, isEndTime: false);
        var resultEnd = DateParser.ParseToUnixTimestamp(input, isEndTime: true);

        // Assert
        Assert.NotNull(resultStart);
        Assert.Equal(expectedStartTimestamp, resultStart.Value);
        Assert.NotNull(resultEnd);
        Assert.Equal(expectedEndTimestamp, resultEnd.Value);
    }

    // --- 测试无效/错误输入 ---
    // 确保对于无法解析的输入，我们的方法能优雅地返回 null 而不是崩溃
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("这不是一个日期")]
    [InlineData("2024-13-01")] // 无效月份
    [InlineData("2023-02-30")] // 无效日期 (平年2月没有30号)
    [InlineData("明天")] // 不支持的相对时间
    public void ParseToUnixTimestamp_ShouldReturnNull_ForInvalidInput(string input)
    {
        // Act
        var result = DateParser.ParseToUnixTimestamp(input);

        // Assert
        Assert.Null(result);
    }
}