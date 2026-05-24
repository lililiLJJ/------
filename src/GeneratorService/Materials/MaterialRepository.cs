using Microsoft.Data.Sqlite;

namespace GeneratorService.Materials;

public sealed class MaterialRepository
{
    private readonly DirectoryInfo _rootPath;
    private readonly AppConfig _config;

    public MaterialRepository(DirectoryInfo rootPath, AppConfig config)
    {
        _rootPath = rootPath;
        _config = config;
    }

    public void EnsureCreated()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS MaterialEntry (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              MaterialName TEXT NOT NULL,
              SpecificationModel TEXT NOT NULL,
              Unit TEXT NOT NULL,
              Quantity TEXT NOT NULL,
              EntryDate TEXT NOT NULL,
              Supplier TEXT NOT NULL,
              Manufacturer TEXT NOT NULL,
              UsePart TEXT NOT NULL,
              BatchNo TEXT NOT NULL,
              Remark TEXT NOT NULL,
              StatusOverride TEXT NULL,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_material_entry_project
              ON MaterialEntry(ProjectId, EntryDate, MaterialName);

            CREATE TABLE IF NOT EXISTS MaterialCertificate (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              MaterialEntryId TEXT NOT NULL,
              FileType TEXT NOT NULL,
              CertificateNo TEXT NOT NULL,
              OriginalFileName TEXT NOT NULL,
              FilePath TEXT NOT NULL,
              UploadedAt TEXT NOT NULL,
              FOREIGN KEY(MaterialEntryId) REFERENCES MaterialEntry(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_material_certificate_entry
              ON MaterialCertificate(ProjectId, MaterialEntryId, FileType);

            CREATE TABLE IF NOT EXISTS MaterialTest (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              MaterialEntryId TEXT NOT NULL UNIQUE,
              IsRequired INTEGER NOT NULL,
              SamplingTime TEXT NULL,
              Witness TEXT NOT NULL,
              SentTime TEXT NULL,
              InspectionAgency TEXT NOT NULL,
              ReportNo TEXT NOT NULL,
              Result TEXT NOT NULL,
              ReportAttachmentId TEXT NULL,
              CreatedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              FOREIGN KEY(MaterialEntryId) REFERENCES MaterialEntry(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_material_test_project
              ON MaterialTest(ProjectId, MaterialEntryId);

            CREATE TABLE IF NOT EXISTS MaterialApproval (
              Id TEXT PRIMARY KEY,
              ProjectId TEXT NOT NULL,
              MaterialEntryId TEXT NOT NULL,
              TemplatePath TEXT NOT NULL,
              FilePath TEXT NOT NULL,
              Status TEXT NOT NULL,
              ProjectDocumentId TEXT NULL,
              GeneratedFormId TEXT NULL,
              GeneratedAt TEXT NOT NULL,
              UpdatedAt TEXT NOT NULL,
              FOREIGN KEY(MaterialEntryId) REFERENCES MaterialEntry(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_material_approval_entry
              ON MaterialApproval(ProjectId, MaterialEntryId, Status);
            """;
        command.ExecuteNonQuery();
    }

    public MaterialEntryRecord InsertEntry(string projectId, MaterialEntryCreateRequest request)
    {
        var now = DateTimeOffset.Now;
        var entry = new MaterialEntryRecord(
            $"material:{Guid.NewGuid():N}",
            projectId,
            CleanRequired(request.MaterialName, "材料名称不能为空。"),
            Clean(request.SpecificationModel),
            Clean(request.Unit),
            request.Quantity,
            request.EntryDate,
            Clean(request.Supplier),
            Clean(request.Manufacturer),
            Clean(request.UsePart),
            Clean(request.BatchNo),
            Clean(request.Remark),
            CleanStatus(request.StatusOverride),
            now,
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MaterialEntry (
                Id, ProjectId, MaterialName, SpecificationModel, Unit, Quantity,
                EntryDate, Supplier, Manufacturer, UsePart, BatchNo, Remark,
                StatusOverride, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $projectId, $materialName, $specificationModel, $unit, $quantity,
                $entryDate, $supplier, $manufacturer, $usePart, $batchNo, $remark,
                $statusOverride, $createdAt, $updatedAt
            );
            """;
        BindEntry(command, entry);
        command.ExecuteNonQuery();
        return entry;
    }

    public MaterialEntryRecord UpdateEntry(string projectId, string id, MaterialEntryUpdateRequest request)
    {
        var current = GetEntry(projectId, id) ?? throw new InvalidOperationException("材料进场记录不存在。");
        var entry = current with
        {
            MaterialName = CleanRequired(request.MaterialName, "材料名称不能为空。"),
            SpecificationModel = Clean(request.SpecificationModel),
            Unit = Clean(request.Unit),
            Quantity = request.Quantity,
            EntryDate = request.EntryDate,
            Supplier = Clean(request.Supplier),
            Manufacturer = Clean(request.Manufacturer),
            UsePart = Clean(request.UsePart),
            BatchNo = Clean(request.BatchNo),
            Remark = Clean(request.Remark),
            StatusOverride = CleanStatus(request.StatusOverride),
            UpdatedAt = DateTimeOffset.Now
        };

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MaterialEntry
            SET MaterialName = $materialName,
                SpecificationModel = $specificationModel,
                Unit = $unit,
                Quantity = $quantity,
                EntryDate = $entryDate,
                Supplier = $supplier,
                Manufacturer = $manufacturer,
                UsePart = $usePart,
                BatchNo = $batchNo,
                Remark = $remark,
                StatusOverride = $statusOverride,
                UpdatedAt = $updatedAt
            WHERE Id = $id AND ProjectId = $projectId;
            """;
        BindEntry(command, entry);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("材料进场记录不存在。");
        }

        return entry;
    }

    public IReadOnlyList<MaterialEntryRecord> ListEntries(MaterialQuery query)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string> { "ProjectId = $projectId" };
        command.Parameters.AddWithValue("$projectId", query.ProjectId);
        AddLike(command, where, "MaterialName", "$materialName", query.MaterialName);
        AddLike(command, where, "UsePart", "$usePart", query.UsePart);
        AddLike(command, where, "Supplier", "$supplier", query.Supplier);
        if (query.EntryDateFrom is not null)
        {
            where.Add("EntryDate >= $entryDateFrom");
            command.Parameters.AddWithValue("$entryDateFrom", query.EntryDateFrom.Value.ToString("yyyy-MM-dd"));
        }

        if (query.EntryDateTo is not null)
        {
            where.Add("EntryDate <= $entryDateTo");
            command.Parameters.AddWithValue("$entryDateTo", query.EntryDateTo.Value.ToString("yyyy-MM-dd"));
        }

        command.CommandText = $"""
            SELECT Id, ProjectId, MaterialName, SpecificationModel, Unit, Quantity,
                   EntryDate, Supplier, Manufacturer, UsePart, BatchNo, Remark,
                   StatusOverride, CreatedAt, UpdatedAt
            FROM MaterialEntry
            WHERE {string.Join(" AND ", where)}
            ORDER BY EntryDate DESC, CreatedAt DESC;
            """;

        using var reader = command.ExecuteReader();
        var entries = new List<MaterialEntryRecord>();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public MaterialEntryRecord? GetEntry(string projectId, string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, MaterialName, SpecificationModel, Unit, Quantity,
                   EntryDate, Supplier, Manufacturer, UsePart, BatchNo, Remark,
                   StatusOverride, CreatedAt, UpdatedAt
            FROM MaterialEntry
            WHERE ProjectId = $projectId AND Id = $id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public MaterialCertificateInfo InsertCertificate(
        string projectId,
        string materialEntryId,
        string fileType,
        string certificateNo,
        string originalFileName,
        string relativePath)
    {
        var now = DateTimeOffset.Now;
        var attachment = new MaterialCertificateInfo(
            $"material-attachment:{Guid.NewGuid():N}",
            projectId,
            materialEntryId,
            CleanRequired(fileType, "文件类型不能为空。"),
            Clean(certificateNo),
            Clean(originalFileName),
            ToPortablePath(relativePath),
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MaterialCertificate (
                Id, ProjectId, MaterialEntryId, FileType, CertificateNo,
                OriginalFileName, FilePath, UploadedAt
            )
            VALUES (
                $id, $projectId, $materialEntryId, $fileType, $certificateNo,
                $originalFileName, $filePath, $uploadedAt
            );
            """;
        command.Parameters.AddWithValue("$id", attachment.Id);
        command.Parameters.AddWithValue("$projectId", attachment.ProjectId);
        command.Parameters.AddWithValue("$materialEntryId", attachment.MaterialEntryId);
        command.Parameters.AddWithValue("$fileType", attachment.FileType);
        command.Parameters.AddWithValue("$certificateNo", attachment.CertificateNo);
        command.Parameters.AddWithValue("$originalFileName", attachment.OriginalFileName);
        command.Parameters.AddWithValue("$filePath", attachment.FilePath);
        command.Parameters.AddWithValue("$uploadedAt", attachment.UploadedAt.ToString("O"));
        command.ExecuteNonQuery();
        return attachment;
    }

    public IReadOnlyList<MaterialCertificateInfo> ListCertificates(string projectId, string materialEntryId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, MaterialEntryId, FileType, CertificateNo,
                   OriginalFileName, FilePath, UploadedAt
            FROM MaterialCertificate
            WHERE ProjectId = $projectId AND MaterialEntryId = $materialEntryId
            ORDER BY UploadedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$materialEntryId", materialEntryId);
        return ReadCertificates(command);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<MaterialCertificateInfo>> ListCertificatesByEntryIds(
        string projectId,
        IReadOnlyList<string> entryIds)
    {
        var result = entryIds.ToDictionary(id => id, _ => (IReadOnlyList<MaterialCertificateInfo>)[]);
        if (entryIds.Count == 0)
        {
            return result;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var parameters = AddInParameters(command, "$id", entryIds);
        command.CommandText = $"""
            SELECT Id, ProjectId, MaterialEntryId, FileType, CertificateNo,
                   OriginalFileName, FilePath, UploadedAt
            FROM MaterialCertificate
            WHERE ProjectId = $projectId AND MaterialEntryId IN ({parameters})
            ORDER BY UploadedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        var items = ReadCertificates(command)
            .GroupBy(item => item.MaterialEntryId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MaterialCertificateInfo>)group.ToArray());

        foreach (var item in items)
        {
            result[item.Key] = item.Value;
        }

        return result;
    }

    public MaterialTestInfo UpsertTest(string projectId, string materialEntryId, MaterialTestUpsertRequest request)
    {
        var existing = GetTest(projectId, materialEntryId);
        var now = DateTimeOffset.Now;
        var test = new MaterialTestInfo(
            existing?.Id ?? $"material-test:{Guid.NewGuid():N}",
            projectId,
            materialEntryId,
            request.IsRequired,
            request.SamplingTime,
            Clean(request.Witness),
            request.SentTime,
            Clean(request.InspectionAgency),
            Clean(request.ReportNo),
            Clean(request.Result),
            string.IsNullOrWhiteSpace(request.ReportAttachmentId) ? null : request.ReportAttachmentId.Trim(),
            existing?.CreatedAt ?? now,
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MaterialTest (
                Id, ProjectId, MaterialEntryId, IsRequired, SamplingTime, Witness,
                SentTime, InspectionAgency, ReportNo, Result, ReportAttachmentId,
                CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $projectId, $materialEntryId, $isRequired, $samplingTime, $witness,
                $sentTime, $inspectionAgency, $reportNo, $result, $reportAttachmentId,
                $createdAt, $updatedAt
            )
            ON CONFLICT(MaterialEntryId) DO UPDATE SET
                IsRequired = $isRequired,
                SamplingTime = $samplingTime,
                Witness = $witness,
                SentTime = $sentTime,
                InspectionAgency = $inspectionAgency,
                ReportNo = $reportNo,
                Result = $result,
                ReportAttachmentId = $reportAttachmentId,
                UpdatedAt = $updatedAt;
            """;
        BindTest(command, test);
        command.ExecuteNonQuery();
        return test;
    }

    public MaterialTestInfo? GetTest(string projectId, string materialEntryId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, MaterialEntryId, IsRequired, SamplingTime, Witness,
                   SentTime, InspectionAgency, ReportNo, Result, ReportAttachmentId,
                   CreatedAt, UpdatedAt
            FROM MaterialTest
            WHERE ProjectId = $projectId AND MaterialEntryId = $materialEntryId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$materialEntryId", materialEntryId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTest(reader) : null;
    }

    public IReadOnlyDictionary<string, MaterialTestInfo> ListTestsByEntryIds(string projectId, IReadOnlyList<string> entryIds)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<string, MaterialTestInfo>();
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var parameters = AddInParameters(command, "$id", entryIds);
        command.CommandText = $"""
            SELECT Id, ProjectId, MaterialEntryId, IsRequired, SamplingTime, Witness,
                   SentTime, InspectionAgency, ReportNo, Result, ReportAttachmentId,
                   CreatedAt, UpdatedAt
            FROM MaterialTest
            WHERE ProjectId = $projectId AND MaterialEntryId IN ({parameters});
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, MaterialTestInfo>();
        while (reader.Read())
        {
            var test = ReadTest(reader);
            result[test.MaterialEntryId] = test;
        }

        return result;
    }

    public MaterialApprovalInfo InsertApproval(
        string projectId,
        string materialEntryId,
        string templatePath,
        string relativePath)
    {
        var now = DateTimeOffset.Now;
        var approval = new MaterialApprovalInfo(
            $"material-approval:{Guid.NewGuid():N}",
            projectId,
            materialEntryId,
            ToPortablePath(templatePath),
            ToPortablePath(relativePath),
            "active",
            null,
            null,
            now,
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MaterialApproval (
                Id, ProjectId, MaterialEntryId, TemplatePath, FilePath, Status,
                ProjectDocumentId, GeneratedFormId, GeneratedAt, UpdatedAt
            )
            VALUES (
                $id, $projectId, $materialEntryId, $templatePath, $filePath, $status,
                $projectDocumentId, $generatedFormId, $generatedAt, $updatedAt
            );
            """;
        BindApproval(command, approval);
        command.ExecuteNonQuery();
        return approval;
    }

    public MaterialApprovalInfo? GetLatestApproval(string projectId, string materialEntryId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, MaterialEntryId, TemplatePath, FilePath, Status,
                   ProjectDocumentId, GeneratedFormId, GeneratedAt, UpdatedAt
            FROM MaterialApproval
            WHERE ProjectId = $projectId AND MaterialEntryId = $materialEntryId
            ORDER BY GeneratedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$materialEntryId", materialEntryId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadApproval(reader) : null;
    }

    public IReadOnlyDictionary<string, MaterialApprovalInfo> ListLatestApprovalsByEntryIds(
        string projectId,
        IReadOnlyList<string> entryIds)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<string, MaterialApprovalInfo>();
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var parameters = AddInParameters(command, "$id", entryIds);
        command.CommandText = $"""
            SELECT Id, ProjectId, MaterialEntryId, TemplatePath, FilePath, Status,
                   ProjectDocumentId, GeneratedFormId, GeneratedAt, UpdatedAt
            FROM MaterialApproval
            WHERE ProjectId = $projectId AND MaterialEntryId IN ({parameters})
            ORDER BY GeneratedAt DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, MaterialApprovalInfo>();
        while (reader.Read())
        {
            var approval = ReadApproval(reader);
            result.TryAdd(approval.MaterialEntryId, approval);
        }

        return result;
    }

    private SqliteConnection OpenConnection()
    {
        var dbPath = _config.GetKnowledgeBasePath(_rootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        return connection;
    }

    private static void BindEntry(SqliteCommand command, MaterialEntryRecord entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$projectId", entry.ProjectId);
        command.Parameters.AddWithValue("$materialName", entry.MaterialName);
        command.Parameters.AddWithValue("$specificationModel", entry.SpecificationModel);
        command.Parameters.AddWithValue("$unit", entry.Unit);
        command.Parameters.AddWithValue("$quantity", entry.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$entryDate", entry.EntryDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$supplier", entry.Supplier);
        command.Parameters.AddWithValue("$manufacturer", entry.Manufacturer);
        command.Parameters.AddWithValue("$usePart", entry.UsePart);
        command.Parameters.AddWithValue("$batchNo", entry.BatchNo);
        command.Parameters.AddWithValue("$remark", entry.Remark);
        command.Parameters.AddWithValue("$statusOverride", (object?)entry.StatusOverride ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
    }

    private static void BindTest(SqliteCommand command, MaterialTestInfo test)
    {
        command.Parameters.AddWithValue("$id", test.Id);
        command.Parameters.AddWithValue("$projectId", test.ProjectId);
        command.Parameters.AddWithValue("$materialEntryId", test.MaterialEntryId);
        command.Parameters.AddWithValue("$isRequired", test.IsRequired ? 1 : 0);
        command.Parameters.AddWithValue("$samplingTime", ToDbTime(test.SamplingTime));
        command.Parameters.AddWithValue("$witness", test.Witness);
        command.Parameters.AddWithValue("$sentTime", ToDbTime(test.SentTime));
        command.Parameters.AddWithValue("$inspectionAgency", test.InspectionAgency);
        command.Parameters.AddWithValue("$reportNo", test.ReportNo);
        command.Parameters.AddWithValue("$result", test.Result);
        command.Parameters.AddWithValue("$reportAttachmentId", (object?)test.ReportAttachmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", test.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", test.UpdatedAt.ToString("O"));
    }

    private static void BindApproval(SqliteCommand command, MaterialApprovalInfo approval)
    {
        command.Parameters.AddWithValue("$id", approval.Id);
        command.Parameters.AddWithValue("$projectId", approval.ProjectId);
        command.Parameters.AddWithValue("$materialEntryId", approval.MaterialEntryId);
        command.Parameters.AddWithValue("$templatePath", approval.TemplatePath);
        command.Parameters.AddWithValue("$filePath", approval.FilePath);
        command.Parameters.AddWithValue("$status", approval.Status);
        command.Parameters.AddWithValue("$projectDocumentId", (object?)approval.ProjectDocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$generatedFormId", (object?)approval.GeneratedFormId ?? DBNull.Value);
        command.Parameters.AddWithValue("$generatedAt", approval.GeneratedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", approval.UpdatedAt.ToString("O"));
    }

    private static MaterialEntryRecord ReadEntry(SqliteDataReader reader)
    {
        return new MaterialEntryRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            decimal.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(reader.GetString(6)),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)),
            DateTimeOffset.Parse(reader.GetString(14)));
    }

    private static IReadOnlyList<MaterialCertificateInfo> ReadCertificates(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var items = new List<MaterialCertificateInfo>();
        while (reader.Read())
        {
            items.Add(new MaterialCertificateInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7))));
        }

        return items;
    }

    private static MaterialTestInfo ReadTest(SqliteDataReader reader)
    {
        return new MaterialTestInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static MaterialApprovalInfo ReadApproval(SqliteDataReader reader)
    {
        return new MaterialApprovalInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9)));
    }

    private static void AddLike(
        SqliteCommand command,
        List<string> where,
        string column,
        string parameter,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        where.Add($"{column} LIKE {parameter}");
        command.Parameters.AddWithValue(parameter, $"%{value.Trim()}%");
    }

    private static string AddInParameters(SqliteCommand command, string prefix, IReadOnlyList<string> values)
    {
        var names = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            var name = $"{prefix}{index}";
            names.Add(name);
            command.Parameters.AddWithValue(name, values[index]);
        }

        return string.Join(", ", names);
    }

    private static object ToDbTime(DateTimeOffset? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }

    private static string Clean(string? value)
    {
        return value?.Trim() ?? "";
    }

    private static string CleanRequired(string? value, string message)
    {
        var cleaned = Clean(value);
        return string.IsNullOrWhiteSpace(cleaned) ? throw new InvalidOperationException(message) : cleaned;
    }

    private static string? CleanStatus(string? value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (!MaterialStatuses.All.Contains(cleaned))
        {
            throw new InvalidOperationException($"未知材料状态：{cleaned}");
        }

        return cleaned;
    }

    public static string ToPortablePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

public sealed record MaterialEntryRecord(
    string Id,
    string ProjectId,
    string MaterialName,
    string SpecificationModel,
    string Unit,
    decimal Quantity,
    DateOnly EntryDate,
    string Supplier,
    string Manufacturer,
    string UsePart,
    string BatchNo,
    string Remark,
    string? StatusOverride,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
