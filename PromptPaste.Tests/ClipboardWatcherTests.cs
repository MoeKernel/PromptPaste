using PromptPaste.Services;

namespace PromptPaste.Tests;

public class ClipboardWatcherTests : IDisposable
{
    public ClipboardWatcherTests()
    {
        ClipboardWatcher.ResetInternalCopyStateForTests();
    }

    [Fact]
    public void IgnoresMarkedInternalCopyText()
    {
        ClipboardWatcher.MarkInternalCopy("替换后的变量内容");

        Assert.True(ClipboardWatcher.ShouldIgnoreInternalCopyForTests("替换后的变量内容"));
    }

    [Fact]
    public void DoesNotConsumeInternalMarkerForDifferentTextWhileMarkedTextIsPending()
    {
        ClipboardWatcher.MarkInternalCopy("替换后的变量内容");

        Assert.False(ClipboardWatcher.ShouldIgnoreInternalCopyForTests("外部复制内容"));
        Assert.True(ClipboardWatcher.ShouldIgnoreInternalCopyForTests("替换后的变量内容"));
    }

    [Fact]
    public void IgnoresRestoredClipboardTextAfterPasteServiceMarksIt()
    {
        ClipboardWatcher.MarkInternalCopy("原来的剪贴板内容");

        Assert.True(ClipboardWatcher.ShouldIgnoreInternalCopyForTests("原来的剪贴板内容"));
    }

    public void Dispose()
    {
        ClipboardWatcher.ResetInternalCopyStateForTests();
    }
}
