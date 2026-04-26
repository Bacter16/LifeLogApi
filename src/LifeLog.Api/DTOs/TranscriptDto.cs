namespace LifeLog.Api.DTOs;

public record TranscriptDto(
    string ClientEventId,
    string Text,
    DateTimeOffset OccurredAt,
    string Timezone,
    string Source,
    string DeviceId
);
