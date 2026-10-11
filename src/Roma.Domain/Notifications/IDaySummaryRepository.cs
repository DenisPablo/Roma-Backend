namespace Roma.Domain.Notifications;

public interface IDaySummaryRepository
{
    Task<bool> HasSummaryBeenSentAsync(string day, CancellationToken ct = default);
    Task MarkSummarySentAsync(string day, string sentAt, CancellationToken ct = default);
}
