using Roma.Application.Notifications;

namespace Roma.Infrastructure.Notifications;

public class NullNotifier : INotifierPort
{
    public Task SendAsync(string text, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
