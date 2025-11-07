using Xunit;
using LunariumBot.Net.Logger.Parser;

namespace LunariumBot.Net.LoggerParser.Tests;

public class LogParserTests
{
    #region 基础测试

    [Fact]
    public void ParseMessage_NullString_ReturnsEmptyTemplate()
    {
        var result = LogParser.ParseMessage(null!);

        Assert.NotNull(result);
        Assert.Empty(result._messageTemplateTokens);
    }

    [Fact]
    public void ParseMessage_EmptyString_ReturnsEmptyTemplate()
    {
        var result = LogParser.ParseMessage("");

        Assert.NotNull(result);
        Assert.Empty(result._messageTemplateTokens);
    }

    [Fact]
    public void ParseMessage_PureText_ReturnsSingleTextToken()
    {
        var result = LogParser.ParseMessage("Hello World");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("Hello World", ((TextToken)token).Text);
    }

    #endregion

    #region 简单属性测试

    [Fact]
    public void ParseMessage_SimpleNamedProperty_ReturnsPropertyToken()
    {
        var result = LogParser.ParseMessage("{Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<PropertyToken>(token);

        var propToken = (PropertyToken)token;
        Assert.Equal("Name", propToken.PropertyName);
        Assert.Equal(PropertyType.Named, propToken.PropertyType);
        Assert.Null(propToken.Format);
        Assert.Null(propToken.Alignment);
        Assert.Equal(Destructuring.Default, propToken.Destructuring);
    }

    [Fact]
    public void ParseMessage_SimplePositionalProperty_ReturnsPropertyToken()
    {
        var result = LogParser.ParseMessage("{0}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<PropertyToken>(token);

        var propToken = (PropertyToken)token;
        Assert.Equal("0", propToken.PropertyName);
        Assert.Equal(PropertyType.Positional, propToken.PropertyType);
    }

    [Fact]
    public void ParseMessage_SingleLetterProperty_ReturnsPropertyToken()
    {
        var result = LogParser.ParseMessage("{A}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<PropertyToken>(token);

        var propToken = (PropertyToken)token;
        Assert.Equal("A", propToken.PropertyName);
        Assert.Equal(PropertyType.Named, propToken.PropertyType);
    }

    [Fact]
    public void ParseMessage_SingleDigitProperty_ReturnsPropertyToken()
    {
        var result = LogParser.ParseMessage("{5}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<PropertyToken>(token);

        var propToken = (PropertyToken)token;
        Assert.Equal("5", propToken.PropertyName);
        Assert.Equal(PropertyType.Positional, propToken.PropertyType);
    }

    #endregion

    #region 转义测试

    [Fact]
    public void ParseMessage_FullyEscapedBraces_ReturnsSingleTextToken()
    {
        // Arrange
        var template = "{{escaped}}";

        // Act
        var result = LogParser.ParseMessage(template);

        // Assert
        // 1. 验证整个模板被解析为单个 Token
        var token = Assert.Single(result._messageTemplateTokens);

        // 2. 验证这个 Token 是 TextToken 类型
        var textToken = Assert.IsType<TextToken>(token);

        // 3. 验证 TextToken 的内容是正确反转义后的结果
        Assert.Equal("{escaped}", textToken.Text);
    }

    [Fact]
    public void ParseMessage_MixedEscapedAndProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{{ {Name} }}");

        Assert.Equal(3, result._messageTemplateTokens.Length);

        Assert.IsType<TextToken>(result._messageTemplateTokens[0]);
        Assert.Equal("{ ", ((TextToken)result._messageTemplateTokens[0]).Text);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[1]);
        Assert.Equal("Name", ((PropertyToken)result._messageTemplateTokens[1]).PropertyName);

        Assert.IsType<TextToken>(result._messageTemplateTokens[2]);
        Assert.Equal(" }", ((TextToken)result._messageTemplateTokens[2]).Text);
    }

    #endregion

    #region 格式化测试

    [Fact]
    public void ParseMessage_PropertyWithFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Number:N0}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Number", token.PropertyName);
        Assert.Equal("N0", token.Format);
        Assert.Null(token.Alignment);
    }

    [Fact]
    public void ParseMessage_PropertyWithDateFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Date:yyyy-MM-dd}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Date", token.PropertyName);
        Assert.Equal("yyyy-MM-dd", token.Format);
    }

    #endregion

    #region 对齐测试

    [Fact]
    public void ParseMessage_PropertyWithAlignment_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Name,10}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Name", token.PropertyName);
        Assert.Equal(10, token.Alignment);
        Assert.Null(token.Format);
    }

    [Fact]
    public void ParseMessage_PropertyWithNegativeAlignment_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Name,-10}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Name", token.PropertyName);
        Assert.Equal(-10, token.Alignment);
    }

    #endregion

    #region 格式化+对齐测试

    [Fact]
    public void ParseMessage_PropertyWithAlignmentAndFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Number,10:N2}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Number", token.PropertyName);
        Assert.Equal(10, token.Alignment);
        Assert.Equal("N2", token.Format);
    }

    [Fact]
    public void ParseMessage_PropertyWithNegativeAlignmentAndFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Number,-10:N2}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Number", token.PropertyName);
        Assert.Equal(-10, token.Alignment);
        Assert.Equal("N2", token.Format);
    }

    #endregion

    #region 解构测试

    [Fact]
    public void ParseMessage_DestructureProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{@Object}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Object", token.PropertyName);
        Assert.Equal(Destructuring.Destructure, token.Destructuring);
    }

    [Fact]
    public void ParseMessage_StringifyProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{$Object}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Object", token.PropertyName);
        Assert.Equal(Destructuring.Stringify, token.Destructuring);
    }

    [Fact]
    public void ParseMessage_DestructureWithFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{@Date:yyyy-MM-dd}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Date", token.PropertyName);
        Assert.Equal(Destructuring.Destructure, token.Destructuring);
        Assert.Equal("yyyy-MM-dd", token.Format);
    }

    [Fact]
    public void ParseMessage_DestructureWithAlignmentAndFormat_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{@Object,10:N2}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("Object", token.PropertyName);
        Assert.Equal(Destructuring.Destructure, token.Destructuring);
        Assert.Equal(10, token.Alignment);
        Assert.Equal("N2", token.Format);
    }

    #endregion

    #region 命名属性测试

    [Fact]
    public void ParseMessage_PropertyWithUnderscore_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{User_Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("User_Name", token.PropertyName);
        Assert.Equal(PropertyType.Named, token.PropertyType);
    }

    [Fact]
    public void ParseMessage_PropertyWithDot_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{User.Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("User.Name", token.PropertyName);
        Assert.Equal(PropertyType.Named, token.PropertyType);
    }

    [Fact]
    public void ParseMessage_PropertyWithMultipleDots_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{User.Address.City}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("User.Address.City", token.PropertyName);
        Assert.Equal(PropertyType.Named, token.PropertyType);
    }

    [Fact]
    public void ParseMessage_PropertyWithMixedChars_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{User123_Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("User123_Name", token.PropertyName);
        Assert.Equal(PropertyType.Named, token.PropertyType);
    }

    #endregion

    #region 位置属性测试

    [Fact]
    public void ParseMessage_MultiDigitPositionalProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{123}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("123", token.PropertyName);
        Assert.Equal(PropertyType.Positional, token.PropertyType);
    }

    [Fact]
    public void ParseMessage_DestructurePositionalProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{@0}");

        Assert.Single(result._messageTemplateTokens);
        var token = (PropertyToken)result._messageTemplateTokens[0];

        Assert.Equal("0", token.PropertyName);
        Assert.Equal(PropertyType.Positional, token.PropertyType);
        Assert.Equal(Destructuring.Destructure, token.Destructuring);
    }

    #endregion

    #region 非法输入测试

    [Fact]
    public void ParseMessage_EmptyProperty_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithTrailingComma_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name,}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name,}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithTrailingColon_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name:}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name:}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithLeadingSpace_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{ Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{ Name}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithTrailingSpace_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name }");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name }", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithLeadingSpaceInFormat_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name: N0}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name: N0}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithTrailingSpaceInFormat_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name:N0 }");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name:N0 }", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithLeadingSpaceInAlignment_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name, 10}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name, 10}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithTrailingSpaceInAlignment_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name,10 }");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name,10 }", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_MixedPositionalAndLetters_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{123abc}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{123abc}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyStartingWithDot_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{.Name}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{.Name}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyEndingWithDot_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name.}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name.}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithDoubleDots_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name..Property}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name..Property}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_PropertyWithDotFollowedByDigit_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name.123}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name.123}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_UnclosedProperty_ReturnsTextToken()
    {
        var result = LogParser.ParseMessage("Hello {Name");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("Hello {Name", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_SingleOpenBrace_ReturnsTextToken()
    {
        var result = LogParser.ParseMessage("test {");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("test {", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_SingleCloseBrace_ReturnsTextToken()
    {
        var result = LogParser.ParseMessage("test }");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("test }", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_OnlySpecialCharacter_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{@}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{@}", ((TextToken)token).Text);
    }

    [Fact]
    public void ParseMessage_InvalidAlignmentNonNumeric_ReturnTextToken()
    {
        var result = LogParser.ParseMessage("{Name,abc}");

        Assert.Single(result._messageTemplateTokens);
        var token = result._messageTemplateTokens[0];
        Assert.IsType<TextToken>(token);
        Assert.Equal("{Name,abc}", ((TextToken)token).Text);
    }

    #endregion

    #region 复杂场景测试

    [Fact]
    public void ParseMessage_MultipleProperties_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Name} is {Age} years old");

        Assert.Equal(4, result._messageTemplateTokens.Length);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[0]);
        Assert.Equal("Name", ((PropertyToken)result._messageTemplateTokens[0]).PropertyName);

        Assert.IsType<TextToken>(result._messageTemplateTokens[1]);
        Assert.Equal(" is ", ((TextToken)result._messageTemplateTokens[1]).Text);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[2]);
        Assert.Equal("Age", ((PropertyToken)result._messageTemplateTokens[2]).PropertyName);

        Assert.IsType<TextToken>(result._messageTemplateTokens[3]);
        Assert.Equal(" years old", ((TextToken)result._messageTemplateTokens[3]).Text);
    }

    [Fact]
    public void ParseMessage_ConsecutiveProperties_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{First}{Second}");

        Assert.Equal(2, result._messageTemplateTokens.Length);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[0]);
        Assert.Equal("First", ((PropertyToken)result._messageTemplateTokens[0]).PropertyName);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[1]);
        Assert.Equal("Second", ((PropertyToken)result._messageTemplateTokens[1]).PropertyName);
    }

    [Fact]
    public void ParseMessage_ComplexMixedTemplate_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("User {@User} has {Count:N0} messages, ratio: {Ratio,10:F2}");

        Assert.Equal(6, result._messageTemplateTokens.Length);

        // "User "
        Assert.IsType<TextToken>(result._messageTemplateTokens[0]);
        Assert.Equal("User ", ((TextToken)result._messageTemplateTokens[0]).Text);

        // {@User}
        Assert.IsType<PropertyToken>(result._messageTemplateTokens[1]);
        var userToken = (PropertyToken)result._messageTemplateTokens[1];
        Assert.Equal("User", userToken.PropertyName);
        Assert.Equal(Destructuring.Destructure, userToken.Destructuring);

        // " has "
        Assert.IsType<TextToken>(result._messageTemplateTokens[2]);
        Assert.Equal(" has ", ((TextToken)result._messageTemplateTokens[2]).Text);

        // {Count:N0}
        Assert.IsType<PropertyToken>(result._messageTemplateTokens[3]);
        var countToken = (PropertyToken)result._messageTemplateTokens[3];
        Assert.Equal("Count", countToken.PropertyName);
        Assert.Equal("N0", countToken.Format);

        // " messages, ratio: "
        Assert.IsType<TextToken>(result._messageTemplateTokens[4]);
        Assert.Equal(" messages, ratio: ", ((TextToken)result._messageTemplateTokens[4]).Text);

        // {Ratio,10:F2}
        Assert.IsType<PropertyToken>(result._messageTemplateTokens[5]);
        var ratioToken = (PropertyToken)result._messageTemplateTokens[5];
        Assert.Equal("Ratio", ratioToken.PropertyName);
        Assert.Equal(10, ratioToken.Alignment);
        Assert.Equal("F2", ratioToken.Format);
    }

    [Fact]
    public void ParseMessage_TextEndingWithProperty_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("Result: {Value}");

        Assert.Equal(2, result._messageTemplateTokens.Length);

        Assert.IsType<TextToken>(result._messageTemplateTokens[0]);
        Assert.Equal("Result: ", ((TextToken)result._messageTemplateTokens[0]).Text);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[1]);
        Assert.Equal("Value", ((PropertyToken)result._messageTemplateTokens[1]).PropertyName);
    }

    [Fact]
    public void ParseMessage_PropertyStartingText_ParsesCorrectly()
    {
        var result = LogParser.ParseMessage("{Value} is the result");

        Assert.Equal(2, result._messageTemplateTokens.Length);

        Assert.IsType<PropertyToken>(result._messageTemplateTokens[0]);
        Assert.Equal("Value", ((PropertyToken)result._messageTemplateTokens[0]).PropertyName);

        Assert.IsType<TextToken>(result._messageTemplateTokens[1]);
        Assert.Equal(" is the result", ((TextToken)result._messageTemplateTokens[1]).Text);
    }

    #endregion

    #region 缓存测试

    [Fact]
    public void ParseMessage_SameTemplate_ReturnsCachedResult()
    {
        var template = "Hello {Name}";
        var first = LogParser.ParseMessage(template);
        var second = LogParser.ParseMessage(template);

        // 应该返回同一个实例（缓存）
        Assert.Same(first, second);
    }

    #endregion
}
