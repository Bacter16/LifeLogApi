using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using LifeLog.Infrastructure.Data;

namespace LifeLog.Api.Services;

public class GeminiWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GeminiWorker> _logger;

    // Base path for all user vaults
    private const string VaultsBasePath = "/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogVaults/users";

    public GeminiWorker(IServiceProvider serviceProvider, ILogger<GeminiWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GeminiWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeLogDbContext>();

                var job = await db.GeminiJobs
                    .FirstOrDefaultAsync(j => j.Status == "queued", stoppingToken);

                if (job != null)
                {
                    _logger.LogInformation("Processing job {JobId} for transcript {TranscriptId}", job.Id, job.TranscriptId);

                    job.Status = "processing";
                    job.StartedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);

                    var transcript = await db.Transcripts.FindAsync(new object[] { job.TranscriptId }, stoppingToken);

                    if (transcript == null)
                    {
                        _logger.LogWarning("Transcript {TranscriptId} not found for job {JobId}. Marking failed.", job.TranscriptId, job.Id);
                        job.Status = "failed";
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    else
                    {
                        try
                        {
                            var workPath = Path.Combine(VaultsBasePath, job.UserId, "vault-work");
                            var livePath = Path.Combine(VaultsBasePath, job.UserId, "vault-live");

                            // Step 1: Sync vault-work from vault-live (always start fresh from live state)
                            _logger.LogInformation("Pulling vault-work from vault-live...");
                            RunGit("pull origin main", workPath);

                            // Step 2: Record pre-commit hash
                            var preHash = RunGit("rev-parse HEAD", workPath).Trim();
                            job.PreCommitHash = preHash;

                            // Step 3: Write/update the daily note in vault-work
                            var noteDate = transcript.OccurredAt.ToLocalTime();
                            var dailyFolder = Path.Combine(workPath, "Daily");
                            Directory.CreateDirectory(dailyFolder);

                            var noteFileName = $"Daily-{noteDate:dd-MM-yyyy}.md";
                            var noteFilePath = Path.Combine(dailyFolder, noteFileName);

                            // Create the file with frontmatter if it doesn't already exist
                            if (!File.Exists(noteFilePath))
                            {
                                _logger.LogInformation("Creating new daily note: {FileName}", noteFileName);
                                var frontmatter = $"""
---
type: daily
date: {noteDate:yyyy-MM-dd}
tags:
  - journal/daily
mood: unknown
energy: unknown
main_feelings: []
behaviors: []
people: []
---

# {noteDate:dd-MM-yyyy}

## Log

## Tasks

## Feelings & Energy

## Work Sessions

## People

## Evening Review

""";
                                await File.WriteAllTextAsync(noteFilePath, frontmatter, stoppingToken);
                            }

                            // Step 4: Append the log entry to the ## Log section
                            var logEntry = $"\n- {noteDate:HH:mm} — {transcript.RawText}";
                            var noteContent = await File.ReadAllTextAsync(noteFilePath, stoppingToken);

                            if (noteContent.Contains("## Log"))
                            {
                                noteContent = noteContent.Replace("## Log", $"## Log{logEntry}");
                            }
                            else
                            {
                                noteContent += logEntry;
                            }

                            await File.WriteAllTextAsync(noteFilePath, noteContent, stoppingToken);
                            _logger.LogInformation("Appended log entry to {FileName}", noteFileName);

                            // Step 5: Git commit in vault-work
                            RunGit("add .", workPath);
                            var commitResult = RunGit(
                                $"commit -m \"journal: transcript {transcript.ClientEventId} ({noteDate:HH:mm})\"",
                                workPath
                            );

                            if (commitResult.Contains("nothing to commit"))
                            {
                                _logger.LogWarning("Nothing to commit for job {JobId} — transcript may be a duplicate.", job.Id);
                            }

                            var postHash = RunGit("rev-parse HEAD", workPath).Trim();
                            job.PostCommitHash = postHash;

                            // Step 6: Push changes from vault-work back to vault-live
                            _logger.LogInformation("Pushing vault-work changes to vault-live...");
                            RunGit("push origin main", workPath);

                            // Step 7: Update vault-live working tree (push only updates git objects, not the working tree)
                            _logger.LogInformation("Updating vault-live working tree...");
                            RunGit("reset --hard HEAD", livePath);

                            // Update job and transcript status
                            job.Status = "updated";
                            job.FinishedAt = DateTimeOffset.UtcNow;
                            transcript.Status = "updated";

                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Successfully processed job {JobId}. Post-hash: {PostHash}", job.Id, postHash);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing transcript for job {JobId}", job.Id);
                            job.Status = "failed";
                            job.FinishedAt = DateTimeOffset.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GeminiWorker poll loop.");
            }

            await Task.Delay(5000, stoppingToken);
        }

        _logger.LogInformation("GeminiWorker stopped.");
    }

    /// <summary>
    /// Runs a git command via the gemini-wrapper.sh allowlist script.
    /// </summary>
    private string RunGit(string arguments, string workingDirectory)
    {
        return RunShellCommand("git", arguments, workingDirectory);
    }

    /// <summary>
    /// Runs a shell command through the gemini-wrapper.sh allowlist script.
    /// </summary>
    private string RunShellCommand(string command, string arguments, string workingDirectory)
    {
        var wrapperPath = "/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogBackend/scripts/gemini-wrapper.sh";

        if (!File.Exists(wrapperPath))
        {
            _logger.LogError("gemini-wrapper.sh not found at {WrapperPath}. Refusing to execute command.", wrapperPath);
            return string.Empty;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = wrapperPath,
                Arguments = $"{command} {arguments}",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !err.Contains("nothing to commit") && !err.Contains("Everything up-to-date"))
        {
            _logger.LogWarning("Command exited {ExitCode}: {Command} {Arguments} | stdout: {Output} | stderr: {Error}",
                process.ExitCode, command, arguments, output.Trim(), err.Trim());
        }

        return output;
    }
}
