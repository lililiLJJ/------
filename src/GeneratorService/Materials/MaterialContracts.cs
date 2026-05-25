namespace GeneratorService.Materials;

public static class MaterialStatuses
{
    public const string MissingCertificate = "未收集合格证";
    public const string PendingTest = "待送检";
    public const string SentToTest = "已送检";
    public const string ReportIssued = "已出报告";
    public const string Qualified = "合格";
    public const string Unqualified = "不合格";
    public const string Approved = "已报审";
    public const string Archived = "已归档";

    public static readonly string[] All =
    [
        MissingCertificate,
        PendingTest,
        SentToTest,
        ReportIssued,
        Qualified,
        Unqualified,
        Approved,
        Archived
    ];
}

public sealed record MaterialEntryCreateRequest(
    string? ProjectId,
    string MaterialName,
    string? SpecificationModel,
    string? Unit,
    decimal Quantity,
    DateOnly EntryDate,
    string? Supplier,
    string? Manufacturer,
    string? UsePart,
    string? BatchNo,
    string? Remark,
    string? StatusOverride);

public sealed record MaterialEntryUpdateRequest(
    string MaterialName,
    string? SpecificationModel,
    string? Unit,
    decimal Quantity,
    DateOnly EntryDate,
    string? Supplier,
    string? Manufacturer,
    string? UsePart,
    string? BatchNo,
    string? Remark,
    string? StatusOverride);

public sealed record MaterialTestUpsertRequest(
    string? ProjectId,
    bool IsRequired,
    DateTimeOffset? SamplingTime,
    string? Witness,
    DateTimeOffset? SentTime,
    string? InspectionAgency,
    string? ReportNo,
    string? Result,
    string? ReportAttachmentId);

public sealed record MaterialQuery(
    string ProjectId,
    string? MaterialName,
    DateOnly? EntryDateFrom,
    DateOnly? EntryDateTo,
    string? UsePart,
    string? Supplier,
    string? TestStatus,
    string? ApprovalStatus,
    string? Status);

public sealed record MaterialEntryInfo(
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
    string Status,
    string TestStatus,
    string ApprovalStatus,
    IReadOnlyList<MaterialCertificateInfo> Certificates,
    MaterialTestInfo? Test,
    MaterialApprovalInfo? Approval,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MaterialCertificateInfo(
    string Id,
    string ProjectId,
    string MaterialEntryId,
    string FileType,
    string CertificateNo,
    string OriginalFileName,
    string FilePath,
    DateTimeOffset UploadedAt);

public sealed record MaterialTestInfo(
    string Id,
    string ProjectId,
    string MaterialEntryId,
    bool IsRequired,
    DateTimeOffset? SamplingTime,
    string Witness,
    DateTimeOffset? SentTime,
    string InspectionAgency,
    string ReportNo,
    string Result,
    string? ReportAttachmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MaterialApprovalInfo(
    string Id,
    string ProjectId,
    string MaterialEntryId,
    string TemplatePath,
    string FilePath,
    string Status,
    string? ProjectDocumentId,
    string? GeneratedFormId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset UpdatedAt);

public sealed record MaterialListResult(
    bool Success,
    string ProjectId,
    int Total,
    IReadOnlyList<MaterialEntryInfo> Items);

public sealed record MaterialAttachmentResult(
    bool Success,
    MaterialCertificateInfo Attachment,
    string Message);

public sealed record MaterialApprovalGenerateResult(
    bool Success,
    MaterialApprovalInfo Approval,
    string FilePath,
    string AbsoluteFilePath,
    string Message);

public sealed record MaterialApprovalBatchGenerateRequest(
    string? ProjectId,
    IReadOnlyList<string> MaterialEntryIds);

public sealed record MaterialApprovalBatchGenerateResult(
    bool Success,
    IReadOnlyList<MaterialApprovalGenerateResult> Results,
    string? FirstFilePath,
    string? FirstAbsoluteFilePath,
    string Message);

public sealed record MaterialLedgerRow(
    int Sequence,
    string Id,
    string MaterialName,
    string SpecificationModel,
    string Unit,
    decimal Quantity,
    DateOnly EntryDate,
    string Supplier,
    string Manufacturer,
    string UsePart,
    string BatchNo,
    string CertificateNos,
    string TestStatus,
    string ReportNo,
    string TestResult,
    string ApprovalStatus,
    string Status,
    string Remark);

public sealed record MaterialLedgerResult(
    bool Success,
    string ProjectId,
    int Total,
    IReadOnlyList<MaterialLedgerRow> Rows,
    string? FilePath,
    string? AbsoluteFilePath,
    string Message);

public sealed record MaterialLedgerSaveRow(
    string? Id,
    string MaterialName,
    string? SpecificationModel,
    string? Unit,
    decimal? Quantity,
    DateOnly? EntryDate,
    string? Supplier,
    string? Manufacturer,
    string? UsePart,
    string? BatchNo,
    string? CertificateNo,
    string? FactoryReportNo,
    bool? IsRequired,
    DateTimeOffset? SentTime,
    string? InspectionAgency,
    string? ReportNo,
    string? Result,
    string? Remark,
    string? StatusOverride,
    bool Delete);

public sealed record MaterialBatchSaveRequest(
    string? ProjectId,
    IReadOnlyList<MaterialLedgerSaveRow> Rows);

public sealed record MaterialLedgerExportRequest(
    string? ProjectId,
    string? MaterialName,
    DateOnly? EntryDateFrom,
    DateOnly? EntryDateTo,
    string? UsePart,
    string? Supplier,
    string? TestStatus,
    string? ApprovalStatus,
    string? Status);

public sealed record MaterialBatchSaveRowResult(
    int RowIndex,
    bool Success,
    string? Id,
    string? Message);

public sealed record MaterialBatchSaveResult(
    bool Success,
    string ProjectId,
    IReadOnlyList<MaterialBatchSaveRowResult> Results,
    IReadOnlyList<MaterialEntryInfo> Items,
    string Message);

public sealed record MaterialDeleteResult(
    bool Success,
    string Id,
    string Message);
