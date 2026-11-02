namespace LifeVerse.Api.Data;

public sealed class AdminAudit { public Guid Id { get; set; } public Guid AdminUserId { get; set; } public required string Action { get; set; } public string TargetType { get; set; } = string.Empty; public Guid? TargetId { get; set; } public required string MetadataJson { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class ModerationCase { public Guid Id { get; set; } public Guid? ReportId { get; set; } public Guid ReviewerUserId { get; set; } public required string Decision { get; set; } public required string EvidenceJson { get; set; } public string Notes { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }
