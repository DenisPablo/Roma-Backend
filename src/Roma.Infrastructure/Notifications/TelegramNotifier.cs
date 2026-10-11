using Microsoft.Extensions.Logging;
using Roma.Application.Configuration;
using Roma.Application.Notifications;
using Telegram.Bot;

namespace Roma.Infrastructure.Notifications;

public class TelegramNotifier : INotifierPort
{
    private readonly TelegramBotClient _botClient;
    private readonly string _chatId;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(AppOptions options, ILogger<TelegramNotifier> logger)
    {
        _logger = logger;
        _chatId = options.TelegramChatId ?? string.Empty;
        _botClient = new TelegramBotClient(options.TelegramBotToken ?? string.Empty);
    }

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_chatId))
        {
            _logger.LogWarning("Telegram chat ID is empty, skipping notification.");
            return;
        }

        // Send with 1 retry on failure
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _botClient.SendMessage(
                    chatId: _chatId,
                    text: text,
                    cancellationToken: ct
                );
                return;
            }
            catch (Exception ex) when (attempt == 1 && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to send Telegram notification on attempt 1. Retrying...");
                await Task.Delay(1000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram notification after attempt {Attempt}.", attempt);
                throw;
            }
        }
    }
}
