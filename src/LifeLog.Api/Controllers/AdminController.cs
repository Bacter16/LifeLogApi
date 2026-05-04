using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LifeLog.Infrastructure.Data;
using LifeLog.Api.Filters;
using Microsoft.AspNetCore.Authorization;

namespace LifeLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuthFilter]
public class AdminController : ControllerBase
{
    private readonly LifeLogDbContext _db;
    private readonly IConfiguration _config;

    public AdminController(LifeLogDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ─── Health ─────────────────────────────────────────────────

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0"
        });
    }

    [HttpGet("ready")]
    public async Task<IActionResult> ReadyCheck()
    {
        var vaultsBasePath = _config["LifeLog:VaultsBasePath"]
            ?? _config["LIFELOG_VAULTS_BASE_PATH"]
            ?? "/data/vaults/users";

        var dbReady = await _db.Database.CanConnectAsync();
        var userVaultPath = Path.Combine(vaultsBasePath, "user_1");
        var vaultsReady = Directory.Exists(Path.Combine(userVaultPath, "vault-work"))
            && Directory.Exists(Path.Combine(userVaultPath, "vault-live"));

        return dbReady && vaultsReady
            ? Ok(new { status = "ready", db = "ok", vaults = "ok" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "not_ready",
                db = dbReady ? "ok" : "unavailable",
                vaults = vaultsReady ? "ok" : "missing",
                vaultsBasePath
            });
    }

    // ─── Jobs Listing ───────────────────────────────────────────

    /// <summary>
    /// Lists Gemini jobs with pagination, newest first.
    /// GET /api/admin/jobs?limit=20&offset=0&status=updated
    /// </summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var query = _db.GeminiJobs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(j => j.Status == status);

        var total = await query.CountAsync();

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(j => new
            {
                j.Id,
                j.UserId,
                j.TranscriptId,
                j.Status,
                j.StartedAt,
                j.FinishedAt,
                j.PreCommitHash,
                j.PostCommitHash,
                j.RetryCount,
                j.CreatedAt,
                DurationMs = j.StartedAt.HasValue && j.FinishedAt.HasValue
                    ? (long)(j.FinishedAt.Value - j.StartedAt.Value).TotalMilliseconds
                    : (long?)null
            })
            .ToListAsync();

        return Ok(new { total, limit, offset, jobs });
    }

    /// <summary>
    /// Gets a single job with full details including error and transcript snippet.
    /// GET /api/admin/jobs/{id}
    /// </summary>
    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(string id)
    {
        var job = await _db.GeminiJobs.FindAsync(id);
        if (job == null) return NotFound(new { error = "Job not found" });

        // Load the associated transcript for context
        string? transcriptSnippet = null;
        if (!string.IsNullOrEmpty(job.TranscriptId))
        {
            var transcript = await _db.Transcripts.FindAsync(job.TranscriptId);
            if (transcript != null)
            {
                transcriptSnippet = transcript.RawText.Length > 200
                    ? transcript.RawText[..200] + "..."
                    : transcript.RawText;
            }
        }

        return Ok(new
        {
            job.Id,
            job.UserId,
            job.TranscriptId,
            TranscriptSnippet = transcriptSnippet,
            job.Status,
            job.StartedAt,
            job.FinishedAt,
            DurationMs = job.StartedAt.HasValue && job.FinishedAt.HasValue
                ? (long)(job.FinishedAt.Value - job.StartedAt.Value).TotalMilliseconds
                : (long?)null,
            job.ExitCode,
            job.StdoutSummary,
            job.StderrSummary,
            job.ErrorMessage,
            job.RetryCount,
            job.PreCommitHash,
            job.PostCommitHash,
            job.ChangedFilesJson,
            job.CreatedAt,
            job.UpdatedAt
        });
    }

    // ─── Stats ──────────────────────────────────────────────────

    /// <summary>
    /// Returns aggregate statistics about jobs and transcripts.
    /// GET /api/admin/stats
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalJobs = await _db.GeminiJobs.CountAsync();
        var jobsByStatus = await _db.GeminiJobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalTranscripts = await _db.Transcripts.CountAsync();
        var recentTranscripts = await _db.Transcripts
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new
            {
                t.Id,
                Snippet = t.RawText.Length > 80 ? t.RawText.Substring(0, 80) + "..." : t.RawText,
                t.Status,
                t.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            totalJobs,
            jobsByStatus,
            totalTranscripts,
            recentTranscripts
        });
    }

}
