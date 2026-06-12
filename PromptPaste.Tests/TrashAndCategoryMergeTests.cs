using PromptPaste.Models;

namespace PromptPaste.Tests;

public class TrashAndCategoryMergeTests
{
    [Fact]
    public void TrashRestorePermanentDeleteAndClearTrash_WorkAsExpected()
    {
        using var fixture = new TestDatabase();
        var firstId = fixture.Db.AddItem(new ClipboardItem { Title = "A", Content = "内容A" });
        var secondId = fixture.Db.AddItem(new ClipboardItem { Title = "B", Content = "内容B" });

        Assert.True(fixture.Db.MoveItemToTrash(firstId));
        Assert.Single(fixture.Db.GetTrashItems());
        Assert.DoesNotContain(fixture.Db.GetAllItems(), i => i.Id == firstId);

        Assert.True(fixture.Db.RestoreItemFromTrash(firstId));
        Assert.Empty(fixture.Db.GetTrashItems());
        Assert.Contains(fixture.Db.GetAllItems(), i => i.Id == firstId);

        Assert.True(fixture.Db.MoveItemToTrash(firstId));
        Assert.True(fixture.Db.PermanentDeleteItem(firstId));
        Assert.Null(fixture.Db.GetItem(firstId));

        Assert.True(fixture.Db.MoveItemToTrash(secondId));
        Assert.Equal(1, fixture.Db.ClearTrash());
        Assert.Empty(fixture.Db.GetTrashItems());
        Assert.Empty(fixture.Db.GetAllItems());
    }

    [Fact]
    public void RemoveItemFromCategory_OnlyRemovesSelectedRelation()
    {
        using var fixture = new TestDatabase();
        var categoryA = fixture.Db.CreateCategory("A", null);
        var categoryB = fixture.Db.CreateCategory("B", null);
        var itemId = fixture.Db.AddItem(new ClipboardItem
        {
            Title = "多分类",
            Content = "内容",
            CategoryIds = new List<int> { categoryA, categoryB },
            CategoryPaths = new List<string> { "A", "B" }
        });

        Assert.True(fixture.Db.RemoveItemFromCategory(itemId, categoryA));
        var item = fixture.Db.GetItem(itemId)!;

        Assert.DoesNotContain(categoryA, item.CategoryIds);
        Assert.Contains(categoryB, item.CategoryIds);
        Assert.Empty(fixture.Db.GetTrashItems());
    }

    [Fact]
    public void MoveOrMergeCategory_MergesSameNameCategoriesAndChildrenRecursively()
    {
        using var fixture = new TestDatabase();
        var targetParent = fixture.Db.CreateCategory("目标父级", null);
        var sourceA = fixture.Db.CreateCategory("A", null);
        var targetA = fixture.Db.CreateCategory("A", targetParent);
        var sourceChild = fixture.Db.CreateCategory("Child", sourceA);
        var targetChild = fixture.Db.CreateCategory("Child", targetA);

        var sourceItemId = fixture.Db.AddItem(new ClipboardItem
        {
            Title = "源条目",
            Content = "源内容",
            CategoryIds = new List<int> { sourceA, sourceChild },
            CategoryPaths = new List<string> { "A", "A/Child" }
        });
        var targetItemId = fixture.Db.AddItem(new ClipboardItem
        {
            Title = "目标条目",
            Content = "目标内容",
            CategoryIds = new List<int> { targetA, targetChild },
            CategoryPaths = new List<string> { "目标父级/A", "目标父级/A/Child" }
        });

        fixture.Db.MoveOrMergeCategory(sourceA, targetParent);

        var categories = fixture.Db.GetAllCategoriesFlat();
        Assert.DoesNotContain(categories, c => c.Id == sourceA || c.Id == sourceChild);
        Assert.Contains(categories, c => c.Id == targetA && c.Path == "目标父级/A");
        Assert.Contains(categories, c => c.Id == targetChild && c.Path == "目标父级/A/Child");

        var sourceItem = fixture.Db.GetItem(sourceItemId)!;
        var targetItem = fixture.Db.GetItem(targetItemId)!;
        Assert.Contains(targetA, sourceItem.CategoryIds);
        Assert.Contains(targetChild, sourceItem.CategoryIds);
        Assert.Contains(targetA, targetItem.CategoryIds);
        Assert.Contains(targetChild, targetItem.CategoryIds);
    }
}
