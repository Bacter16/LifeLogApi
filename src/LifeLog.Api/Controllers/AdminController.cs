using Microsoft.AspNetCore.Mvc;
using LifeLog.Infrastructure.Data;

namespace LifeLog.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly LifeLogDbContext _db;

    public AdminController(LifeLogDbContext db)
    {
        _db = db;
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "healthy" });
    }

    [HttpPost("jobs/{id}/rollback")]
    public async Task<IActionResult> RollbackJob(int id)
    {
        var job = await _db.GeminiJobs.FindAsync(id);
        if (job == null) return NotFound();
        if (string.IsNullOrEmpty(job.PreCommitHash)) return BadRequest("No pre-commit hash found for job.");

        var workspacePath = $"/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogVaults/users/{job.UserId}/vault-work";
        var livePath = $"/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogVaults/users/{job.UserId}/vault-live";

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"reset --hard {job.PreCommitHash}",
                WorkingDirectory = workspacePath,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit();

        process.StartInfo.Arguments = "push origin main --force";
        process.Start();
        process.WaitForExit();

        process.StartInfo.WorkingDirectory = livePath;
        process.StartInfo.Arguments = "pull origin main";
        process.Start();
        process.WaitForExit();

        job.Status = "rolled_back";
        await _db.SaveChangesAsync();

        return Ok(new { status = "rolled_back", preCommitHash = job.PreCommitHash });
    }
}
