namespace Roma.Application.Notifications;

public interface INotifierPort
{
    Task SendAsync(string text, CancellationToken ct = default);
}
