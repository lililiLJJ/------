using GeneratorService.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace GeneratorService.Knowledge;

public sealed class KnowledgeRepository
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public KnowledgeRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quality_items (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              profession TEXT NOT NULL,
              division TEXT NOT NULL,
              sub_item TEXT NOT NULL,
              item_type TEXT NOT NULL,
              item_name TEXT NOT NULL,
              qualified_standard TEXT NOT NULL,
              allowable_deviation TEXT,
              check_method TEXT NOT NULL,
              standard_code TEXT NOT NULL,
              standard_version TEXT NOT NULL,
              source_note TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        if (CountItems(connection) == 0)
        {
            SeedSampleItems(connection);
        }

        Log.Information("知识库已就绪：{DbPath}", dbPath);
    }

    public IReadOnlyList<KnowledgeItem> QueryItems(string division, string subItem, string itemType)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT profession, division, sub_item, item_type, item_name,
                   qualified_standard, allowable_deviation, check_method,
                   standard_code, standard_version, source_note
            FROM quality_items
            WHERE division = $division
              AND sub_item = $subItem
              AND item_type = $itemType
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$division", division);
        command.Parameters.AddWithValue("$subItem", subItem);
        command.Parameters.AddWithValue("$itemType", itemType);

        using var reader = command.ExecuteReader();
        var items = new List<KnowledgeItem>();
        while (reader.Read())
        {
            items.Add(new KnowledgeItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return items;
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static long CountItems(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM quality_items;";
        return (long)command.ExecuteScalar()!;
    }

    private static void SeedSampleItems(SqliteConnection connection)
    {
        var items = new[]
        {
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "主控项目", "受力钢筋的品种、级别、规格和数量", "必须符合设计要求", null, "观察，钢尺检查", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据"),
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "主控项目", "钢筋连接方式", "应符合设计要求和现行标准规定", null, "观察，检查连接质量证明文件", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据"),
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "一般项目", "钢筋安装位置", "应符合设计要求，偏差应在允许范围内", null, "观察，钢尺检查", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据"),
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "一般项目", "钢筋保护层厚度", "应符合设计要求", null, "钢尺检查", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据"),
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "允许偏差", "受力钢筋间距", "应符合设计要求", "±10mm", "钢尺量两端、中间各一点，取最大偏差值", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据"),
            new KnowledgeItem("建筑结构", "主体结构", "钢筋安装", "允许偏差", "受力钢筋排距", "应符合设计要求", "±5mm", "钢尺检查", "GB50204", "2015版", "MVP示例数据，请在正式商用前替换为审核后的规范原文数据")
        };

        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO quality_items (
                    profession, division, sub_item, item_type, item_name,
                    qualified_standard, allowable_deviation, check_method,
                    standard_code, standard_version, source_note
                )
                VALUES (
                    $profession, $division, $subItem, $itemType, $itemName,
                    $qualifiedStandard, $allowableDeviation, $checkMethod,
                    $standardCode, $standardVersion, $sourceNote
                );
                """;
            command.Parameters.AddWithValue("$profession", item.Profession);
            command.Parameters.AddWithValue("$division", item.Division);
            command.Parameters.AddWithValue("$subItem", item.SubItem);
            command.Parameters.AddWithValue("$itemType", item.ItemType);
            command.Parameters.AddWithValue("$itemName", item.ItemName);
            command.Parameters.AddWithValue("$qualifiedStandard", item.QualifiedStandard);
            command.Parameters.AddWithValue("$allowableDeviation", (object?)item.AllowableDeviation ?? DBNull.Value);
            command.Parameters.AddWithValue("$checkMethod", item.CheckMethod);
            command.Parameters.AddWithValue("$standardCode", item.StandardCode);
            command.Parameters.AddWithValue("$standardVersion", item.StandardVersion);
            command.Parameters.AddWithValue("$sourceNote", item.SourceNote);
            command.ExecuteNonQuery();
        }
    }
}
