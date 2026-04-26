namespace LifeLog.Domain.Entities;

public class GeminiJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string TranscriptId { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int? ExitCode { get; set; }
    public string StdoutSummary { get; set; } = string.Empty;
    public string StderrSummary { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int RetryCount { get; set; } = 0;
    public string PreCommitHash { get; set; } = string.Empty;
    public string PostCommitHash { get; set; } = string.Empty;
    public string ChangedFilesJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
