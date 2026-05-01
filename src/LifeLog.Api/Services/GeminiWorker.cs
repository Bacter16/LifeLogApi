using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using LifeLog.Infrastructure.Data;

namespace LifeLog.Api.Services;

public class GeminiWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GeminiWorker> _logger;
    private readonly IConfiguration _config;

    // Base path for all user vaults
    private const string VaultsBasePath = "/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogVaults/users";

    // Backend root — Gemini CLI runs here (not in vault) to avoid agentic GEMINI.md mode
    private const string BackendRootPath = "/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogBackend";

    // Track whether the weekly review was already generated this session
    private DateTimeOffset? _lastWeeklyReviewDate;

    public GeminiWorker(IServiceProvider serviceProvider, ILogger<GeminiWorker> logger, IConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config;
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

                // --- Transcript Processing (existing logic) ---
                var job = await db.GeminiJobs
                    .FirstOrDefaultAsync(j => j.Status == "queued", stoppingToken);

                if (job != null)
                {
                    await ProcessTranscriptJobAsync(db, job, stoppingToken);
                }

                // --- Weekly Review Check (runs on Sundays at 21:00+ local time) ---
                await CheckAndGenerateWeeklyReviewAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GeminiWorker poll loop.");
            }

            await Task.Delay(5000, stoppingToken);
        }

        _logger.LogInformation("GeminiWorker stopped.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TRANSCRIPT PROCESSING
    // ═══════════════════════════════════════════════════════════════════

    private async Task ProcessTranscriptJobAsync(LifeLogDbContext db, LifeLog.Domain.Entities.GeminiJob job, CancellationToken stoppingToken)
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
            return;
        }

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

            // Step 3: Resolve vault paths
            var noteDate = transcript.OccurredAt.ToLocalTime();
            var dailyFolder = Path.Combine(workPath, "Daily");
            Directory.CreateDirectory(dailyFolder);

            var noteFileName = $"Daily-{noteDate:dd-MM-yyyy}.md";
            var noteFilePath = Path.Combine(dailyFolder, noteFileName);
            var systemPromptPath = Path.Combine(workPath, "System", "GEMINI.md");

            // Step 4: Read system prompt and existing daily note
            var systemPrompt = File.Exists(systemPromptPath)
                ? await File.ReadAllTextAsync(systemPromptPath, stoppingToken)
                : string.Empty;

            var existingDailyNote = File.Exists(noteFilePath)
                ? await File.ReadAllTextAsync(noteFilePath, stoppingToken)
                : string.Empty;

            // Step 5: Build prompt and invoke Gemini CLI
            _logger.LogInformation("Invoking Gemini CLI for transcript {EventId}...", transcript.ClientEventId);

            var prompt = GeminiPromptBuilder.BuildDailyNotePrompt(
                systemPrompt,
                existingDailyNote,
                transcript.RawText,
                noteDate,
                noteFileName);

            var geminiOutput = await InvokeGeminiAsync(prompt, BackendRootPath, stoppingToken);

            if (string.IsNullOrWhiteSpace(geminiOutput))
            {
                _logger.LogWarning("Gemini CLI returned empty output for job {JobId}. Falling back to raw append.", job.Id);
                geminiOutput = FallbackDailyNote(existingDailyNote, noteDate, noteFileName, transcript.RawText);
            }

            // Step 6: Write Gemini output to the daily note
            await File.WriteAllTextAsync(noteFilePath, geminiOutput, stoppingToken);
            _logger.LogInformation("Wrote Gemini output to {FileName}", noteFileName);

            // Step 7: Git commit in vault-work
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

            // Step 8: Push changes from vault-work back to vault-live
            _logger.LogInformation("Pushing vault-work changes to vault-live...");
            RunGit("push origin main", workPath);

            // Step 9: Update vault-live working tree
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

    // ═══════════════════════════════════════════════════════════════════
    //  WEEKLY REVIEW
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if it's Sunday 21:00+ (local time) and generates the weekly review if not already done.
    /// Uses a simple in-memory flag (_lastWeeklyReviewDate) to prevent re-generation on the same day.
    /// </summary>
    private async Task CheckAndGenerateWeeklyReviewAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.Now;

        // Only trigger on Sundays at 21:00 or later
        if (now.DayOfWeek != DayOfWeek.Sunday || now.Hour < 21)
            return;

        // Don't re-generate if already done today
        if (_lastWeeklyReviewDate.HasValue && _lastWeeklyReviewDate.Value.Date == now.Date)
            return;

        _logger.LogInformation("Sunday 21:00+ detected — triggering weekly review generation.");

        try
        {
            // For now, process for user_1 (single-user system)
            await GenerateWeeklyReviewForUserAsync("user_1", now, stoppingToken);
            _lastWeeklyReviewDate = now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate weekly review.");
        }
    }

    /// <summary>
    /// Generates the weekly review for a specific user.
    /// </summary>
    private async Task GenerateWeeklyReviewForUserAsync(string userId, DateTimeOffset now, CancellationToken stoppingToken)
    {
        var workPath = Path.Combine(VaultsBasePath, userId, "vault-work");
        var livePath = Path.Combine(VaultsBasePath, userId, "vault-live");

        // Step 1: Sync vault-work
        RunGit("pull origin main", workPath);

        // Step 2: Compute week boundaries
        var (weekStart, weekEnd) = WeeklyReviewBuilder.GetWeekBoundaries(now);
        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(weekStart.DateTime);
        var weekFileName = $"Week-{weekStart.Year}-W{weekNumber:D2}.md";

        // Step 3: Check if weekly review already exists
        var weeklyFolder = Path.Combine(workPath, "Weekly");
        Directory.CreateDirectory(weeklyFolder);
        var weeklyFilePath = Path.Combine(weeklyFolder, weekFileName);

        if (File.Exists(weeklyFilePath))
        {
            _logger.LogInformation("Weekly review {FileName} already exists — skipping.", weekFileName);
            return;
        }

        // Step 4: Read the weekly review system prompt
        var promptPath = Path.Combine(workPath, "System", "Prompts", "WeeklyReviewPrompt.md");
        if (!File.Exists(promptPath))
        {
            _logger.LogWarning("WeeklyReviewPrompt.md not found at {Path}. Skipping weekly review.", promptPath);
            return;
        }
        var weeklyReviewPrompt = await File.ReadAllTextAsync(promptPath, stoppingToken);

        // Step 5: Collect all daily notes from the week
        var dailyFolder = Path.Combine(workPath, "Daily");
        var dailyNotesContent = await WeeklyReviewBuilder.CollectWeeklyDailyNotesAsync(
            dailyFolder, weekStart, weekEnd, stoppingToken);

        if (dailyNotesContent.Contains("No daily notes found"))
        {
            _logger.LogInformation("No daily notes for {WeekLabel} — skipping weekly review.", weekFileName);
            return;
        }

        // Step 6: Build and invoke Gemini CLI
        _logger.LogInformation("Generating weekly review {FileName}...", weekFileName);

        var prompt = WeeklyReviewBuilder.BuildWeeklyReviewPrompt(
            weeklyReviewPrompt,
            dailyNotesContent,
            weekStart,
            weekEnd,
            weekFileName);

        var geminiOutput = await InvokeGeminiAsync(prompt, BackendRootPath, stoppingToken);

        if (string.IsNullOrWhiteSpace(geminiOutput))
        {
            _logger.LogWarning("Gemini CLI returned empty output for weekly review {FileName}.", weekFileName);
            return;
        }

        // Step 7: Write output
        await File.WriteAllTextAsync(weeklyFilePath, geminiOutput, stoppingToken);
        _logger.LogInformation("Wrote weekly review to {FileName}", weekFileName);

        // Step 8: Commit and push
        RunGit("add .", workPath);
        RunGit($"commit -m \"weekly: review {weekFileName}\"", workPath);
        RunGit("push origin main", workPath);
        RunGit("reset --hard HEAD", livePath);

        _logger.LogInformation("Weekly review {FileName} committed and synced to vault-live.", weekFileName);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GEMINI CLI INVOCATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Invokes the Gemini CLI with the given prompt piped via stdin and returns the response text.
    /// </summary>
    private async Task<string> InvokeGeminiAsync(string prompt, string workingDirectory, CancellationToken cancellationToken)
    {
        var cliPath = _config["Gemini:CliPath"] ?? "gemini";
        var model = _config["Gemini:Model"] ?? string.Empty;
        var timeoutSeconds = int.TryParse(_config["Gemini:TimeoutSeconds"], out var t) ? t : 120;

        // Only pass --model if explicitly configured; otherwise use CLI default
        var modelArg = string.IsNullOrWhiteSpace(model) ? string.Empty : $"--model {model} ";

        // gemini CLI: pipe prompt via stdin, -p "" triggers headless mode.
        // --yolo: auto-approve tool calls (no interactive prompt).
        // Run from BackendRootPath, NOT vault, to prevent GEMINI.md agentic mode.
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"{modelArg}--output-format text --yolo -p \"\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Write prompt to stdin and close it
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Gemini CLI exited with code {ExitCode}. Stderr: {Error}", process.ExitCode, error.Trim());
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogDebug("Gemini CLI stderr: {Error}", error.Trim());
        }

        // Strip markdown code block wrappers if Gemini wraps its output
        return StripMarkdownWrapper(output.Trim());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Strips ```markdown ... ``` or ``` ... ``` wrappers from Gemini output if present.
    /// </summary>
    private static string StripMarkdownWrapper(string output)
    {
        if (output.StartsWith("```markdown\n") || output.StartsWith("```markdown\r\n"))
        {
            output = output["```markdown".Length..].TrimStart('\r', '\n');
            if (output.EndsWith("\n```"))
                output = output[..^4];
            else if (output.EndsWith("```"))
                output = output[..^3];
            return output.Trim();
        }
        if (output.StartsWith("```\n") || output.StartsWith("```\r\n"))
        {
            output = output[3..].TrimStart('\r', '\n');
            if (output.EndsWith("\n```"))
                output = output[..^4];
            else if (output.EndsWith("```"))
                output = output[..^3];
            return output.Trim();
        }
        return output;
    }

    /// <summary>
    /// Fallback: if Gemini CLI fails, create or append a basic log entry manually.
    /// </summary>
    private static string FallbackDailyNote(string existing, DateTimeOffset noteDate, string noteFileName, string rawText)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return $"""
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
- {noteDate:HH:mm} — {rawText}

## Tasks

## Feelings & Energy

## Work Sessions

## People

## Evening Review
""";
        }

        var logEntry = $"\n- {noteDate:HH:mm} — {rawText}";
        return existing.Contains("## Log")
            ? existing.Replace("## Log", $"## Log{logEntry}")
            : existing + logEntry;
    }

    private string RunGit(string arguments, string workingDirectory)
        => RunShellCommand("git", arguments, workingDirectory);

    private string RunShellCommand(string command, string arguments, string workingDirectory)
    {
        var wrapperPath = "/Users/cristian_bacter/Documents/Projects/LifeLog/LifeLogBackend/scripts/gemini-wrapper.sh";

        if (!File.Exists(wrapperPath))
        {
            _logger.LogError("gemini-wrapper.sh not found at {WrapperPath}.", wrapperPath);
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
