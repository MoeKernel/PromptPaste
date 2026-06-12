using PromptPaste.Models;

namespace PromptPaste.Tests;

public class JsonImportExportTests
{
    [Fact]
    public void ImportData_CreatesDefaultCategory_SkipsInvalidRows_AndNormalizesTags()
    {
        using var fixture = new TestDatabase();

        var imported = fixture.Db.ImportData(new List<ExportClipboardItem>
        {
            new()
            {
                Title = "  有效  ",
                Content = "  内容  ",
                CategoryPaths = new List<string>(),
                Tags = new List<string> { "AI", "ai", " 写作 ", "" },
                UsageCount = -3,
                CreatedAt = default
            },
            new() { Title = "", Content = "无标题" },
            new() { Title = "无内容", Content = "" }
        });

        Assert.Equal(1, imported);
        var item = Assert.Single(fixture.Db.GetAllItems());
        Assert.Equal("有效", item.Title);
        Assert.Equal("内容", item.Content);
        Assert.Equal(0, item.UsageCount);
        Assert.Contains("未分类", item.CategoryPaths);
        Assert.Equal(2, item.Tags.Count);
        Assert.Contains(item.Tags, t => string.Equals(t, "AI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(item.Tags, t => string.Equals(t, "写作", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportData_CreatesNestedCategoryPaths()
    {
        using var fixture = new TestDatabase();

        fixture.Db.ImportData(new List<ExportClipboardItem>
        {
            new()
            {
                Title = "嵌套分类",
                Content = "内容",
                CategoryPaths = new List<string> { "A/B", "常用" },
                Tags = new List<string>()
            }
        });

        var item = Assert.Single(fixture.Db.GetAllItems());
        Assert.Contains("A/B", item.CategoryPaths);
        Assert.Contains("常用", item.CategoryPaths);
        Assert.Contains(fixture.Db.GetAllCategoriesFlat(), c => c.Path == "A/B");
    }

    [Fact]
    public void ExportData_ExcludesTrashItems_AndUsesPortableNames()
    {
        using var fixture = new TestDatabase();
        var categoryId = fixture.Db.EnsureCategoryPath("A/B");
        var keepId = fixture.Db.AddItem(new ClipboardItem
        {
            Title = "保留",
            Content = "内容",
            CategoryIds = new List<int> { categoryId },
            CategoryPaths = new List<string> { "A/B" },
            Tags = new List<string> { "tag" },
            UsageCount = 2
        });
        var trashId = fixture.Db.AddItem(new ClipboardItem { Title = "删除", Content = "内容" });
        fixture.Db.MoveItemToTrash(trashId);

        var exported = fixture.Db.ExportData();

        var item = Assert.Single(exported);
        Assert.Equal("保留", item.Title);
        Assert.Contains("A/B", item.CategoryPaths);
        Assert.Contains("tag", item.Tags);
        Assert.Equal(2, item.UsageCount);
        Assert.NotEqual(trashId.ToString(), keepId.ToString());
    }
}
