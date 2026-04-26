using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LifeLog.Infrastructure.Data;
using LifeLog.Domain.Entities;
using LifeLog.Api.DTOs;

namespace LifeLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TranscriptsController : ControllerBase
{
    private readonly LifeLogDbContext _db;

    public TranscriptsController(LifeLogDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTranscript([FromBody] TranscriptDto request)
    {
        var existing = await _db.Transcripts.FirstOrDefaultAsync(t => t.ClientEventId == request.ClientEventId);
        if (existing != null)
        {
            return Ok(new { status = "accepted", transcriptId = existing.Id });
        }

        var transcript = new Transcript
        {
            UserId = "user_1", // Default test user
            DeviceId = request.DeviceId,
            ClientEventId = request.ClientEventId,
            RawText = request.Text,
            // Npgsql requires UTC (offset=0) for timestamptz columns
            OccurredAt = request.OccurredAt.ToUniversalTime(),
            Timezone = request.Timezone,
            Source = request.Source,
            Status = "received"
        };

        _db.Transcripts.Add(transcript);
        await _db.SaveChangesAsync();

        var job = new GeminiJob
        {
            UserId = transcript.UserId,
            TranscriptId = transcript.Id,
            Status = "queued"
        };

        _db.GeminiJobs.Add(job);
        await _db.SaveChangesAsync();

        return Ok(new { status = "accepted", transcriptId = transcript.Id });
    }
}
