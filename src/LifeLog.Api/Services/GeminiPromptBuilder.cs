namespace LifeLog.Api.Services;

/// <summary>
/// Builds the prompt sent to the Gemini CLI for each transcript processing job.
/// Combines the vault's GEMINI.md system instructions, the existing daily note content,
/// and the raw transcript text into a single structured prompt.
/// </summary>
public static class GeminiPromptBuilder
{
    /// <summary>
    /// Constructs a full Gemini prompt for updating the daily note with a new transcript.
    /// </summary>
    /// <param name="systemPrompt">Contents of vault-work/System/GEMINI.md</param>
    /// <param name="existingDailyNote">Current content of today's daily note (empty string if new)</param>
    /// <param name="transcriptText">The raw voice transcript text from the user</param>
    /// <param name="occurredAt">The local time the transcript was recorded</param>
    /// <param name="dailyNoteFileName">The target file name, e.g. Daily-27-04-2026.md</param>
    /// <returns>The full prompt string to pass to the Gemini CLI via -p flag</returns>
    public static string BuildDailyNotePrompt(
        string systemPrompt,
        string existingDailyNote,
        string transcriptText,
        DateTimeOffset occurredAt,
        string dailyNoteFileName)
    {
        var existingSection = string.IsNullOrWhiteSpace(existingDailyNote)
            ? "(This is a new daily note — no existing content yet.)"
            : $"```markdown\n{existingDailyNote}\n```";

        return $"""
{systemPrompt}

---

## Task

You are processing a new voice transcript for the user's journal.

**Target file:** `Daily/{dailyNoteFileName}`
**Transcript recorded at:** {occurredAt:HH:mm} (local time)

**Existing daily note content:**
{existingSection}

**New transcript from user:**
> {transcriptText}

## Instructions

1. If the daily note does not exist yet, create it using the Daily Note Template structure with proper YAML frontmatter.
2. Append the transcript as a new timestamped bullet point under the `## Log` section.
3. Update the YAML frontmatter (`mood`, `energy`, `main_feelings`, `behaviors`, `people`) based on what you can infer from the transcript. Use `unknown` if not determinable.
4. Add Obsidian links (`[[Concept]]`) for any recognizable topics, people, or behaviors mentioned.
5. Add relevant tags (e.g. `behavior/deep-work`, `feeling/focused`) if appropriate.
6. If the user mentions a task, add it under `## Tasks` as a `- [ ] Task` item.
7. Do NOT invent details not present in the transcript.
8. Do NOT be judgmental.

## Response Format

Return ONLY the complete updated Markdown content for `Daily/{dailyNoteFileName}`. No explanations, no preamble. Just the raw Markdown file content.
""";
    }
}
