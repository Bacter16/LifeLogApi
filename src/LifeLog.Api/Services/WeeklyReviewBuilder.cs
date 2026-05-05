namespace LifeLog.Api.Services;

/// <summary>
/// Builds the Gemini prompt for weekly review generation.
/// Aggregates all daily notes from the past week and combines them with the
/// weekly review system prompt for Gemini CLI processing.
/// </summary>
public static class WeeklyReviewBuilder
{
    /// <summary>
    /// Builds the full Gemini prompt for generating a weekly review.
    /// </summary>
    /// <param name="weeklyReviewPrompt">Contents of vault-work/System/Prompts/WeeklyReviewPrompt.md</param>
    /// <param name="dailyNotesContent">Aggregated contents of all daily notes from the week</param>
    /// <param name="weekStart">The Monday of the week being reviewed</param>
    /// <param name="weekEnd">The Sunday of the week being reviewed</param>
    /// <param name="weekFileName">The target file name, e.g. Week-2026-W18.md</param>
    /// <returns>The full prompt string to pass to the Gemini CLI via stdin</returns>
    public static string BuildWeeklyReviewPrompt(
        string weeklyReviewPrompt,
        string dailyNotesContent,
        DateTimeOffset weekStart,
        DateTimeOffset weekEnd,
        string weekFileName)
    {
        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(weekStart.DateTime);
        var weekLabel = $"{weekStart.Year}-W{weekNumber:D2}";

        return $"""
{weeklyReviewPrompt}

---

## Task

You are generating a weekly review for **{weekLabel}** ({weekStart:yyyy-MM-dd} → {weekEnd:yyyy-MM-dd}).

**Target file:** `Weekly/{weekFileName}`

**Daily notes from this week:**

{dailyNotesContent}

## Instructions

1. Analyze ALL the daily notes above for recurring patterns.
2. Create the weekly review using the structure defined in the system prompt.
3. Set the YAML frontmatter `week` to `{weekLabel}`.
4. Set `date_range` to `{weekStart:yyyy-MM-dd} → {weekEnd:yyyy-MM-dd}`.
5. Determine `mood_trend` and `energy_trend` from the daily frontmatter and content.
6. Count behavior frequencies across the week (e.g., "Deep Work appeared 4 out of 7 days").
7. Analyze timing patterns: sleep/wake rhythm, work start time, meals, breaks, context switches, long gaps, and rough durations where timestamps support it.
8. Identify what seems to improve mood, energy, focus, or follow-through.
9. Identify what repeatedly drains energy, creates friction, or causes avoidance.
10. List all projects that were mentioned across the dailies under `## Projects Progressed`.
11. Consolidate tasks — mark completed ones as `[x]` and unfinished ones as `[ ]`.
12. Surface wins and honest observations.
13. Suggest 2-3 specific, low-friction focus areas for next week based on the patterns.

## Response Format

Return ONLY the complete Markdown content for `Weekly/{weekFileName}`. No explanations, no preamble. Just the raw Markdown file content.
""";
    }

    /// <summary>
    /// Collects and concatenates all daily note files from a given week.
    /// </summary>
    /// <param name="dailyFolder">Path to the Daily/ folder in the vault</param>
    /// <param name="weekStart">Monday of the week</param>
    /// <param name="weekEnd">Sunday of the week</param>
    /// <returns>Aggregated string of all daily notes with headers</returns>
    public static async Task<string> CollectWeeklyDailyNotesAsync(
        string dailyFolder,
        DateTimeOffset weekStart,
        DateTimeOffset weekEnd,
        CancellationToken cancellationToken = default)
    {
        var sb = new System.Text.StringBuilder();
        var current = weekStart;

        while (current <= weekEnd)
        {
            var fileName = $"Daily-{current:dd-MM-yyyy}.md";
            var filePath = Path.Combine(dailyFolder, fileName);

            if (File.Exists(filePath))
            {
                var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                sb.AppendLine($"### {fileName}");
                sb.AppendLine();
                sb.AppendLine("```markdown");
                sb.AppendLine(content);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            current = current.AddDays(1);
        }

        return sb.Length > 0
            ? sb.ToString()
            : "(No daily notes found for this week.)";
    }

    /// <summary>
    /// Gets the Monday of the current ISO week (or the previous week if today is Sunday).
    /// When called on Sunday, it reviews the week that just ended (Mon-Sun).
    /// </summary>
    public static (DateTimeOffset WeekStart, DateTimeOffset WeekEnd) GetWeekBoundaries(DateTimeOffset now)
    {
        // On Sunday, review the week that just ended (Monday through this Sunday)
        var sunday = now.DayOfWeek == DayOfWeek.Sunday ? now : now.AddDays(-(int)now.DayOfWeek);
        var monday = sunday.AddDays(-6);

        return (
            new DateTimeOffset(monday.Year, monday.Month, monday.Day, 0, 0, 0, now.Offset),
            new DateTimeOffset(sunday.Year, sunday.Month, sunday.Day, 23, 59, 59, now.Offset)
        );
    }
}
