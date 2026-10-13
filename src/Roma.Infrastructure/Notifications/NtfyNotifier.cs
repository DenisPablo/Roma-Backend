using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Roma.Application.Configuration;
using Roma.Application.Notifications;

namespace Roma.Infrastructure.Notifications;

public class NtfyNotifier : INotifierPort
{
    private readonly HttpClient _httpClient;
    private readonly AppOptions _options;
    private readonly ILogger<NtfyNotifier> _logger;

    public NtfyNotifier(HttpClient httpClient, AppOptions options, ILogger<NtfyNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(string text, CancellationToken ct = default)
    {
        var topic = _options.NtfyTopic?.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            _logger.LogWarning("Ntfy topic is empty, skipping notification.");
            return;
        }

        var baseUrl = !string.IsNullOrWhiteSpace(_options.NtfyServerUrl)
            ? _options.NtfyServerUrl.TrimEnd('/')
            : "https://ntfy.sh";

        var endpoint = $"{baseUrl}/{topic}";

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Content = new StringContent(text, Encoding.UTF8, "text/plain");

                request.Headers.TryAddWithoutValidation("Title", "Roma");
                request.Headers.TryAddWithoutValidation("Priority", "default");
                request.Headers.TryAddWithoutValidation("Tags", "calendar,bell");

                if (!string.IsNullOrWhiteSpace(_options.NtfyToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.NtfyToken.Trim());
                }

                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex) when (attempt == 1 && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to send Ntfy notification on attempt 1. Retrying...");
                await Task.Delay(1000, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Ntfy notification after attempt {Attempt}.", attempt);
                throw;
            }
        }
    }
}
