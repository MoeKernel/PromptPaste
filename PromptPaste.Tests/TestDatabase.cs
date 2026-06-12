using PromptPaste.Database;

namespace PromptPaste.Tests;

internal sealed class TestDatabase : IDisposable
{
    private readonly string _directory;

    public DatabaseService Db { get; }
    public string Path { get; }

    public TestDatabase()
    {
        _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PromptPaste.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "test.db");
        Db = new DatabaseService(Path, seedSampleData: false);
    }

    public void Dispose()
    {
        Db.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch { }
    }
}
