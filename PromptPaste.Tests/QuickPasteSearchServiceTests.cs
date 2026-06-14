using PromptPaste.Models;
using PromptPaste.Services;

namespace PromptPaste.Tests;

public class QuickPasteSearchServiceTests
{
    [Fact]
    public void Search_ReturnsRecentItems_WhenKeywordIsEmpty()
    {
        var items = Enumerable.Range(1, 10)
            .Select(i => new ClipboardItem { Id = i, Title = $"标题{i}", Content = "内容" })
            .ToList();

        var result = QuickPasteSearchService.Search(items, "", 8);

        Assert.Equal(8, result.Count);
        Assert.Equal(Enumerable.Range(1, 8), result.Select(i => i.Id));
    }

    [Fact]
    public void Search_MatchesTitleContentAndTags_CaseInsensitive()
    {
        var items = new List<ClipboardItem>
        {
            new() { Id = 1, Title = "中文润色", Content = "普通内容", Tags = new List<string> { "写作" } },
            new() { Id = 2, Title = "普通标题", Content = "生成 TODO 文档", Tags = new List<string>() },
            new() { Id = 3, Title = "代码解释", Content = "内容", Tags = new List<string> { "Prompt" } },
            new() { Id = 4, Title = "无关", Content = "内容", Tags = new List<string>() }
        };

        Assert.Equal(1, Assert.Single(QuickPasteSearchService.Search(items, "润色")).Id);
        Assert.Equal(2, Assert.Single(QuickPasteSearchService.Search(items, "todo")).Id);
        Assert.Equal(3, Assert.Single(QuickPasteSearchService.Search(items, "prompt")).Id);
    }

    [Fact]
    public void Search_RequiresAllTerms()
    {
        var items = new List<ClipboardItem>
        {
            new() { Id = 1, Title = "AI 邮件", Content = "商务模板", Tags = new List<string> { "常用" } },
            new() { Id = 2, Title = "AI", Content = "代码", Tags = new List<string>() }
        };

        var result = QuickPasteSearchService.Search(items, "AI 商务");

        Assert.Equal(1, Assert.Single(result).Id);
    }
}
