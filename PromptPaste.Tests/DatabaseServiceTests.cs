using Microsoft.Data.Sqlite;
using PromptPaste.Database;
using PromptPaste.Models;

namespace PromptPaste.Tests;

public class DatabaseServiceTests
{
    [Fact]
    public void AddUpdateSearchAndIncrementUsage_WorkOnTemporaryDatabase()
    {
        using var fixture = new TestDatabase();
        var categoryId = fixture.Db.CreateCategory("提示词", null);

        var id = fixture.Db.AddItem(new ClipboardItem
        {
            Title = "中文润色",
            Content = "请润色以下内容",
            CategoryIds = new List<int> { categoryId },
            CategoryPaths = new List<string> { "提示词" },
            Tags = new List<string> { "写作", "AI", "写作" }
        });

        var item = Assert.Single(fixture.Db.GetAllItems());
        Assert.Equal(id, item.Id);
        Assert.Equal("中文润色", item.Title);
        Assert.Contains("提示词", item.CategoryPaths);
        Assert.Contains(item.Tags, t => string.Equals(t, "写作", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(item.Tags, t => string.Equals(t, "AI", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, item.Tags.Count);

        Assert.Single(fixture.Db.SearchItems("润色"));
        Assert.True(fixture.Db.IncrementUsageCount(id));
        Assert.Equal(1, fixture.Db.GetItem(id)!.UsageCount);
        Assert.NotNull(fixture.Db.GetItem(id)!.LastUsed);

        var updated = fixture.Db.GetItem(id)!.Clone();
        updated.Title = "中文扩写";
        updated.Content = "请扩写以下内容";
        updated.Tags = new List<string> { "扩写", "AI" };
        Assert.True(fixture.Db.UpdateItem(updated));

        var updatedItem = fixture.Db.GetItem(id)!;
        Assert.Equal("中文扩写", updatedItem.Title);
        Assert.Contains("扩写", updatedItem.Tags);
        Assert.Contains("AI", updatedItem.Tags);
        Assert.DoesNotContain("写作", updatedItem.Tags);
    }

    [Fact]
    public void CategoryTreeAndDescendants_ReturnHierarchicalPaths()
    {
        using var fixture = new TestDatabase();
        var rootId = fixture.Db.CreateCategory("A", null);
        var childId = fixture.Db.CreateCategory("B", rootId);
        var grandChildId = fixture.Db.CreateCategory("C", childId);

        var all = fixture.Db.GetAllCategoriesFlat();

        Assert.Contains(all, c => c.Id == rootId && c.Path == "A");
        Assert.Contains(all, c => c.Id == childId && c.Path == "A/B");
        Assert.Contains(all, c => c.Id == grandChildId && c.Path == "A/B/C");
        Assert.Equal(new[] { childId, grandChildId }, fixture.Db.GetDescendantCategoryIds(rootId));
    }

    [Fact]
    public void LegacyItemType_IsMigratedToCategoryRelation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PromptPaste.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "legacy.db");
        try
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE clipboard_items (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        title TEXT NOT NULL,
                        content TEXT NOT NULL,
                        item_type TEXT NOT NULL,
                        usage_count INTEGER DEFAULT 0,
                        last_used TEXT,
                        created_at TEXT
                    );
                    INSERT INTO clipboard_items (title, content, item_type, usage_count, created_at)
                    VALUES ('旧数据', '内容', '旧分类/子分类', 0, '2026-06-13T00:00:00+08:00');
                    """;
                cmd.ExecuteNonQuery();
            }

            using var db = new DatabaseService(dbPath, seedSampleData: false);
            var item = Assert.Single(db.GetAllItems());

            Assert.Contains("旧分类/子分类", item.CategoryPaths);
            Assert.Contains(db.GetAllCategoriesFlat(), c => c.Path == "旧分类/子分类");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { }
        }
    }
}
