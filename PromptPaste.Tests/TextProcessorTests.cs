using PromptPaste.Services;

namespace PromptPaste.Tests;

public class TextProcessorTests
{
    [Fact]
    public void ExtractVariables_ReturnsDistinctTrimmedNamesInOrder()
    {
        var variables = TextProcessor.ExtractVariables("你好 {{ name }}，请处理 {{task}}，再次 {{name}}。");

        Assert.Equal(new[] { "name", "task" }, variables);
    }

    [Fact]
    public void ExtractVariables_ReturnsEmptyList_WhenTextHasNoVariables()
    {
        var variables = TextProcessor.ExtractVariables("普通文本");

        Assert.Empty(variables);
    }

    [Fact]
    public void ReplaceVariables_ReplacesKnownVariables_AndKeepsUnknownVariables()
    {
        var result = TextProcessor.ReplaceVariables(
            "公司：{{ company }}；联系人：{{person}}；未知：{{missing}}",
            new Dictionary<string, string> { ["company"] = "Acme", ["person"] = "张三" });

        Assert.Equal("公司：Acme；联系人：张三；未知：{{missing}}", result);
    }

    [Fact]
    public void ReplaceVariables_ReturnsOriginalText_WhenVariableMapIsNullOrEmpty()
    {
        Assert.Equal("{{a}}", TextProcessor.ReplaceVariables("{{a}}", null));
        Assert.Equal("{{a}}", TextProcessor.ReplaceVariables("{{a}}", new Dictionary<string, string>()));
    }

    [Fact]
    public void Truncate_ReturnsOriginalText_WhenWithinLimit()
    {
        Assert.Equal("短文本", TextProcessor.Truncate("短文本", 10));
    }

    [Fact]
    public void Truncate_AddsSuffix_WhenTextExceedsLimit()
    {
        Assert.Equal("abcdefg...", TextProcessor.Truncate("abcdefghijklmn", 10));
    }
}
