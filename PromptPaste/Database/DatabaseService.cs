using System.IO;
using Microsoft.Data.Sqlite;
using PromptPaste.Models;
using PromptPaste.Services;

namespace PromptPaste.Database;

public class DatabaseService : IDisposable
{
    private const int CurrentSchemaVersion = 2;
    private readonly SqliteConnection _conn;

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".promptpaste");

    public static string DefaultDatabasePath => Path.Combine(AppDataDirectory, "promptpaste.db");

    public string DatabasePath { get; }

    public DatabaseService() : this(DefaultDatabasePath, seedSampleData: true) { }

    public DatabaseService(string dbPath, bool seedSampleData = false)
    {
        DatabasePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dbPath));
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath };
        _conn = new SqliteConnection(builder.ToString());
        _conn.Open();

        CreateTables();
        if (seedSampleData) SeedSampleData();
        MigrateLegacyItemTypes();
    }

    private void CreateTables()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS clipboard_items (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT NOT NULL,
                content     TEXT NOT NULL,
                item_type   TEXT NOT NULL,
                usage_count INTEGER DEFAULT 0,
                last_used   TEXT,
                created_at  TEXT,
                deleted_at  TEXT
            );

            CREATE TABLE IF NOT EXISTS categories (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT NOT NULL,
                parent_id  INTEGER,
                sort_order INTEGER DEFAULT 0,
                created_at TEXT,
                FOREIGN KEY(parent_id) REFERENCES categories(id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_parent_name
            ON categories(IFNULL(parent_id, 0), name);

            CREATE TABLE IF NOT EXISTS item_categories (
                item_id     INTEGER NOT NULL,
                category_id INTEGER NOT NULL,
                PRIMARY KEY(item_id, category_id),
                FOREIGN KEY(item_id) REFERENCES clipboard_items(id) ON DELETE CASCADE,
                FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS tags (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS item_tags (
                item_id INTEGER NOT NULL,
                tag_id  INTEGER NOT NULL,
                PRIMARY KEY(item_id, tag_id),
                FOREIGN KEY(item_id) REFERENCES clipboard_items(id) ON DELETE CASCADE,
                FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS schema_info (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        var oldVersion = GetSchemaVersion();
        if (!ColumnExists("clipboard_items", "deleted_at"))
        {
            BackupService.BackupDatabase(DatabasePath, "before-schema-migration");
            using var migrate = _conn.CreateCommand();
            migrate.CommandText = "ALTER TABLE clipboard_items ADD COLUMN deleted_at TEXT";
            migrate.ExecuteNonQuery();
        }

        if (oldVersion < CurrentSchemaVersion)
            SetSchemaVersion(CurrentSchemaVersion);
    }

    private int GetSchemaVersion()
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version'";
            var value = cmd.ExecuteScalar()?.ToString();
            return int.TryParse(value, out var version) ? version : 1;
        }
        catch
        {
            return 1;
        }
    }

    private void SetSchemaVersion(int version)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO schema_info (key, value) VALUES ('schema_version', @version)";
        cmd.Parameters.AddWithValue("@version", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private bool ColumnExists(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void MigrateLegacyItemTypes()
    {
        using var read = _conn.CreateCommand();
        read.CommandText = "SELECT id, item_type FROM clipboard_items WHERE item_type IS NOT NULL AND TRIM(item_type) <> ''";
        using var r = read.ExecuteReader();
        var pairs = new List<(int ItemId, string Type)>();
        while (r.Read()) pairs.Add((r.GetInt32(0), r.GetString(1)));

        foreach (var (itemId, type) in pairs)
        {
            var categoryId = EnsureCategoryPath(type);
            AddItemToCategory(itemId, categoryId);
        }
    }

    private void SeedSampleData()
    {
        using var check = _conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM clipboard_items";
        if ((long)check.ExecuteScalar()! > 0) return;

        Insert(new ClipboardItem { Title = "AI提示词示例", Content = "你是一名专业的{{领域}}专家，请帮我{{任务}}。", ItemType = "AI提示词" });
        Insert(new ClipboardItem { Title = "商务邮件模板", Content = "尊敬的{{收件人}}：\n\n感谢您对我们公司的关注。关于您提到的{{问题}}，我们将尽快处理。\n\n此致\n敬礼\n{{发件人}}", ItemType = "商务话术" });
        Insert(new ClipboardItem { Title = "会议记录模板", Content = "会议主题：{{主题}}\n时间：{{时间}}\n参会人员：{{人员}}\n\n会议内容：\n1. \n2. \n3. \n\n下一步计划：\n1. \n2. \n", ItemType = "其他" });
    }

    private int Insert(ClipboardItem item)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clipboard_items (title, content, item_type, usage_count, last_used, created_at)
            VALUES (@title, @content, @type, @usage, @last, @created)
            """;
        Bind(cmd, item);
        cmd.Parameters.AddWithValue("@usage", item.UsageCount);
        cmd.ExecuteNonQuery();

        using var get = _conn.CreateCommand();
        get.CommandText = "SELECT last_insert_rowid()";
        var id = Convert.ToInt32((long)get.ExecuteScalar()!);
        SetItemCategories(id, item.CategoryIds);
        SetItemTags(id, item.Tags);
        return id;
    }

    private static void Bind(SqliteCommand cmd, ClipboardItem item)
    {
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@content", item.Content);
        cmd.Parameters.AddWithValue("@type", item.CategoryPaths.FirstOrDefault() ?? item.ItemType);
        cmd.Parameters.AddWithValue("@last", item.LastUsed?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created", item.CreatedAt.ToString("o"));
    }

    public List<ClipboardItem> GetAllItems()
    {
        var items = new List<ClipboardItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM clipboard_items WHERE deleted_at IS NULL ORDER BY last_used DESC, id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) items.Add(ReadItem(r));
        EnrichItems(items);
        return items;
    }

    public List<ClipboardItem> SearchItems(string keyword)
        => GetAllItems().Where(i => i.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                 || i.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

    public ClipboardItem? GetItem(int id) => GetAllItems().FirstOrDefault(i => i.Id == id);

    public List<ClipboardItem> GetTrashItems()
    {
        var items = new List<ClipboardItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM clipboard_items WHERE deleted_at IS NOT NULL ORDER BY deleted_at DESC, id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) items.Add(ReadItem(r));
        EnrichItems(items);
        return items;
    }

    public int AddItem(ClipboardItem item) => Insert(item);

    public bool UpdateItem(ClipboardItem item)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE clipboard_items
            SET title = @title, content = @content, item_type = @type,
                usage_count = @usage, last_used = @last
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.Parameters.AddWithValue("@usage", item.UsageCount);
        Bind(cmd, item);
        var ok = cmd.ExecuteNonQuery() > 0;
        if (ok)
        {
            SetItemCategories(item.Id, item.CategoryIds);
            SetItemTags(item.Id, item.Tags);
        }
        return ok;
    }

    public bool MoveItemToTrash(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_items SET deleted_at = @deleted WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@deleted", DateTime.Now.ToString("o"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool RestoreItemFromTrash(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_items SET deleted_at = NULL WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool PermanentDeleteItem(int id)
    {
        using var delCats = _conn.CreateCommand();
        delCats.CommandText = "DELETE FROM item_categories WHERE item_id = @id";
        delCats.Parameters.AddWithValue("@id", id);
        delCats.ExecuteNonQuery();

        using var delTags = _conn.CreateCommand();
        delTags.CommandText = "DELETE FROM item_tags WHERE item_id = @id";
        delTags.Parameters.AddWithValue("@id", id);
        delTags.ExecuteNonQuery();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clipboard_items WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int ClearTrash()
    {
        using var delCats = _conn.CreateCommand();
        delCats.CommandText = "DELETE FROM item_categories WHERE item_id IN (SELECT id FROM clipboard_items WHERE deleted_at IS NOT NULL)";
        delCats.ExecuteNonQuery();

        using var delTags = _conn.CreateCommand();
        delTags.CommandText = "DELETE FROM item_tags WHERE item_id IN (SELECT id FROM clipboard_items WHERE deleted_at IS NOT NULL)";
        delTags.ExecuteNonQuery();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clipboard_items WHERE deleted_at IS NOT NULL";
        return cmd.ExecuteNonQuery();
    }

    public bool IncrementUsageCount(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE clipboard_items SET usage_count = usage_count + 1, last_used = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<string> GetItemTypes() => GetAllCategoriesFlat().Select(c => c.Path).ToList();

    public List<CategoryNode> GetCategoryTree()
    {
        var nodes = GetAllCategoriesFlat();
        var byId = nodes.ToDictionary(c => c.Id);
        foreach (var node in nodes)
        {
            if (node.ParentId is int pid && byId.TryGetValue(pid, out var parent)) parent.Children.Add(node);
        }
        foreach (var n in nodes) UpdatePath(n, byId);
        return nodes.Where(n => n.ParentId == null || !byId.ContainsKey(n.ParentId.Value)).OrderBy(n => n.SortOrder).ThenBy(n => n.Name).ToList();
    }

    public List<CategoryNode> GetAllCategoriesFlat()
    {
        var list = new List<CategoryNode>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.id, c.name, c.parent_id, c.sort_order,
                   (SELECT COUNT(*) FROM item_categories ic
                    JOIN clipboard_items i ON i.id = ic.item_id
                    WHERE ic.category_id = c.id AND i.deleted_at IS NULL) AS item_count
            FROM categories c ORDER BY c.sort_order, c.name
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CategoryNode
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                SortOrder = r.GetInt32(3),
                ItemCount = r.GetInt32(4)
            });
        }
        var byId = list.ToDictionary(c => c.Id);
        foreach (var n in list) UpdatePath(n, byId);
        return list;
    }

    private static void UpdatePath(CategoryNode node, Dictionary<int, CategoryNode> byId)
    {
        var names = new Stack<string>();
        var cur = node;
        names.Push(cur.Name);
        while (cur.ParentId is int pid && byId.TryGetValue(pid, out var parent))
        {
            names.Push(parent.Name);
            cur = parent;
        }
        node.Path = string.Join("/", names);
    }

    public int CreateCategory(string name, int? parentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO categories (name, parent_id, sort_order, created_at) VALUES (@name, @parent, 0, @created)";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@parent", parentId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
        return GetCategoryId(name, parentId)!.Value;
    }

    public void RenameCategory(int id, string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE categories SET name = @name WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.ExecuteNonQuery();
    }

    public void MoveCategory(int id, int? newParentId)
    {
        if (newParentId == id || GetDescendantCategoryIds(id).Contains(newParentId ?? -1)) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE categories SET parent_id = @parent WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@parent", newParentId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void MoveOrMergeCategory(int id, int? newParentId)
    {
        if (newParentId == id || GetDescendantCategoryIds(id).Contains(newParentId ?? -1)) return;

        var source = GetCategoryById(id);
        if (source == null) return;

        var existingId = GetCategoryId(source.Name, newParentId);
        if (existingId is int targetId && targetId != id)
        {
            MergeCategory(id, targetId);
            return;
        }

        MoveCategory(id, newParentId);
    }

    private void MergeCategory(int sourceId, int targetId)
    {
        if (sourceId == targetId) return;

        var source = GetCategoryById(sourceId);
        if (source == null) return;

        using (var rel = _conn.CreateCommand())
        {
            rel.CommandText = """
                INSERT OR IGNORE INTO item_categories (item_id, category_id)
                SELECT item_id, @target FROM item_categories WHERE category_id = @source
                """;
            rel.Parameters.AddWithValue("@source", sourceId);
            rel.Parameters.AddWithValue("@target", targetId);
            rel.ExecuteNonQuery();
        }

        foreach (var child in GetChildCategories(sourceId))
        {
            var conflictId = GetCategoryId(child.Name, targetId);
            if (conflictId is int existingChildId && existingChildId != child.Id)
                MergeCategory(child.Id, existingChildId);
            else
                MoveCategory(child.Id, targetId);
        }

        using (var delRel = _conn.CreateCommand())
        {
            delRel.CommandText = "DELETE FROM item_categories WHERE category_id = @id";
            delRel.Parameters.AddWithValue("@id", sourceId);
            delRel.ExecuteNonQuery();
        }

        using var delCat = _conn.CreateCommand();
        delCat.CommandText = "DELETE FROM categories WHERE id = @id";
        delCat.Parameters.AddWithValue("@id", sourceId);
        delCat.ExecuteNonQuery();
    }

    private CategoryNode? GetCategoryById(int id)
        => GetAllCategoriesFlat().FirstOrDefault(c => c.Id == id);

    private List<CategoryNode> GetChildCategories(int parentId)
        => GetAllCategoriesFlat().Where(c => c.ParentId == parentId).ToList();

    public void DeleteCategoryCascade(int id)
    {
        var ids = GetDescendantCategoryIds(id).Append(id).ToList();
        foreach (var cid in ids.OrderByDescending(x => x))
        {
            using var rel = _conn.CreateCommand();
            rel.CommandText = "DELETE FROM item_categories WHERE category_id = @id";
            rel.Parameters.AddWithValue("@id", cid);
            rel.ExecuteNonQuery();
            using var cat = _conn.CreateCommand();
            cat.CommandText = "DELETE FROM categories WHERE id = @id";
            cat.Parameters.AddWithValue("@id", cid);
            cat.ExecuteNonQuery();
        }
    }

    public List<int> GetDescendantCategoryIds(int id)
    {
        var all = GetAllCategoriesFlat();
        var result = new List<int>();
        void Walk(int parent)
        {
            foreach (var child in all.Where(c => c.ParentId == parent))
            {
                result.Add(child.Id);
                Walk(child.Id);
            }
        }
        Walk(id);
        return result;
    }

    public void AddItemToCategory(int itemId, int categoryId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO item_categories (item_id, category_id) VALUES (@item, @cat)";
        cmd.Parameters.AddWithValue("@item", itemId);
        cmd.Parameters.AddWithValue("@cat", categoryId);
        cmd.ExecuteNonQuery();
    }

    public bool RemoveItemFromCategory(int itemId, int categoryId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM item_categories WHERE item_id = @item AND category_id = @cat";
        cmd.Parameters.AddWithValue("@item", itemId);
        cmd.Parameters.AddWithValue("@cat", categoryId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int EnsureCategoryPath(string path)
    {
        int? parentId = null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            parentId = CreateCategory(part, parentId);
        return parentId ?? CreateCategory("未分类", null);
    }

    private int? GetCategoryId(string name, int? parentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = parentId == null
            ? "SELECT id FROM categories WHERE name = @name AND parent_id IS NULL"
            : "SELECT id FROM categories WHERE name = @name AND parent_id = @parent";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        if (parentId != null) cmd.Parameters.AddWithValue("@parent", parentId.Value);
        var value = cmd.ExecuteScalar();
        return value == null ? null : Convert.ToInt32((long)value);
    }

    public List<TagInfo> GetTags()
    {
        var tags = new List<TagInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM tags ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) tags.Add(new TagInfo { Id = r.GetInt32(0), Name = r.GetString(1) });
        return tags;
    }

    public int EnsureTag(string name)
    {
        using var insert = _conn.CreateCommand();
        insert.CommandText = "INSERT OR IGNORE INTO tags (name) VALUES (@name)";
        insert.Parameters.AddWithValue("@name", name.Trim());
        insert.ExecuteNonQuery();

        using var get = _conn.CreateCommand();
        get.CommandText = "SELECT id FROM tags WHERE name = @name";
        get.Parameters.AddWithValue("@name", name.Trim());
        return Convert.ToInt32((long)get.ExecuteScalar()!);
    }

    private void SetItemCategories(int itemId, IEnumerable<int> categoryIds)
    {
        using var del = _conn.CreateCommand();
        del.CommandText = "DELETE FROM item_categories WHERE item_id = @id";
        del.Parameters.AddWithValue("@id", itemId);
        del.ExecuteNonQuery();
        foreach (var cid in categoryIds.Distinct()) AddItemToCategory(itemId, cid);
    }

    private void SetItemTags(int itemId, IEnumerable<string> tagNames)
    {
        using var del = _conn.CreateCommand();
        del.CommandText = "DELETE FROM item_tags WHERE item_id = @id";
        del.Parameters.AddWithValue("@id", itemId);
        del.ExecuteNonQuery();
        foreach (var tag in tagNames.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tagId = EnsureTag(tag);
            using var add = _conn.CreateCommand();
            add.CommandText = "INSERT OR IGNORE INTO item_tags (item_id, tag_id) VALUES (@item, @tag)";
            add.Parameters.AddWithValue("@item", itemId);
            add.Parameters.AddWithValue("@tag", tagId);
            add.ExecuteNonQuery();
        }
    }

    public int ImportData(List<ExportClipboardItem> items)
    {
        var imported = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Content))
                continue;

            var categoryPaths = item.CategoryPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("未分类")
                .ToList();

            var categoryIds = categoryPaths.Select(EnsureCategoryPath).Distinct().ToList();
            Insert(new ClipboardItem
            {
                Title = item.Title.Trim(),
                Content = item.Content.Trim(),
                CategoryIds = categoryIds,
                CategoryPaths = categoryPaths,
                Tags = item.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                UsageCount = Math.Max(0, item.UsageCount),
                LastUsed = item.LastUsed,
                CreatedAt = item.CreatedAt == default ? DateTime.Now : item.CreatedAt
            });
            imported++;
        }
        return imported;
    }

    public List<ExportClipboardItem> ExportData() => GetAllItems().Select(i => new ExportClipboardItem
    {
        Title = i.Title,
        Content = i.Content,
        CategoryPaths = i.CategoryPaths,
        Tags = i.Tags,
        UsageCount = i.UsageCount,
        LastUsed = i.LastUsed,
        CreatedAt = i.CreatedAt
    }).ToList();

    private void EnrichItems(List<ClipboardItem> items)
    {
        var categories = GetAllCategoriesFlat().ToDictionary(c => c.Id);
        foreach (var item in items)
        {
            item.CategoryIds = GetItemCategoryIds(item.Id);
            item.CategoryPaths = item.CategoryIds.Where(categories.ContainsKey).Select(id => categories[id].Path).ToList();
            item.ItemType = item.CategoryPaths.FirstOrDefault() ?? "未分类";
            item.Tags = GetItemTags(item.Id);
        }
    }

    private List<int> GetItemCategoryIds(int itemId)
    {
        var ids = new List<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT category_id FROM item_categories WHERE item_id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt32(0));
        return ids;
    }

    private List<string> GetItemTags(int itemId)
    {
        var tags = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT t.name FROM tags t JOIN item_tags it ON it.tag_id = t.id WHERE it.item_id = @id ORDER BY t.name";
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) tags.Add(r.GetString(0));
        return tags;
    }

    private static ClipboardItem ReadItem(SqliteDataReader r)
    {
        return new ClipboardItem
        {
            Id = r.GetInt32(0),
            Title = r.GetString(1),
            Content = r.GetString(2),
            ItemType = r.GetString(3),
            UsageCount = r.GetInt32(4),
            LastUsed = r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5)),
            CreatedAt = r.IsDBNull(6) ? DateTime.Now : DateTime.Parse(r.GetString(6)),
            DeletedAt = r.FieldCount > 7 && !r.IsDBNull(7) ? DateTime.Parse(r.GetString(7)) : null,
        };
    }

    public void Dispose()
    {
        _conn.Close();
        _conn.Dispose();
    }
}
