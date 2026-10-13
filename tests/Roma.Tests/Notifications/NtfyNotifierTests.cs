using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Roma.Application.Configuration;
using Roma.Infrastructure.Notifications;

namespace Roma.Tests.Notifications;

public class NtfyNotifierTests
{
    private class CapturedRequest
    {
        public HttpMethod Method { get; set; } = null!;
        public Uri? Uri { get; set; }
        public string Content { get; set; } = string.Empty;
        public HttpRequestHeaders Headers { get; set; } = null!;
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            Requests.Add(new CapturedRequest
            {
                Method = request.Method,
                Uri = request.RequestUri,
                Content = content,
                Headers = request.Headers
            });
            return new HttpResponseMessage(ResponseStatusCode);
        }
    }

    [Fact]
    public async Task SendAsync_WhenTopicConfigured_SendsPostToNtfyWithHeaders()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var options = new AppOptions
        {
            NtfyTopic = "roma-test-topic",
            NtfyServerUrl = "https://ntfy.sh"
        };
        var notifier = new NtfyNotifier(httpClient, options, NullLogger<NtfyNotifier>.Instance);

        await notifier.SendAsync("Recordatorio de evento de prueba");

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.Uri!.ToString().Should().Be("https://ntfy.sh/roma-test-topic");
        req.Content.Should().Be("Recordatorio de evento de prueba");

        req.Headers.GetValues("Title").Should().Contain("🏛️ Roma");
        req.Headers.GetValues("Priority").Should().Contain("default");
        req.Headers.GetValues("Tags").Should().Contain("calendar,bell");
    }

    [Fact]
    public async Task SendAsync_WhenTokenConfigured_IncludesBearerAuthorization()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var options = new AppOptions
        {
            NtfyTopic = "roma-secret-topic",
            NtfyToken = "tk_secure_12345"
        };
        var notifier = new NtfyNotifier(httpClient, options, NullLogger<NtfyNotifier>.Instance);

        await notifier.SendAsync("Mensaje con token");

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Headers.Authorization.Should().NotBeNull();
        req.Headers.Authorization!.Scheme.Should().Be("Bearer");
        req.Headers.Authorization.Parameter.Should().Be("tk_secure_12345");
    }

    [Fact]
    public async Task SendAsync_WhenTopicEmpty_SkipsHttpCall()
    {
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var options = new AppOptions
        {
            NtfyTopic = null
        };
        var notifier = new NtfyNotifier(httpClient, options, NullLogger<NtfyNotifier>.Instance);

        await notifier.SendAsync("Mensaje sin topic");

        handler.Requests.Should().BeEmpty();
    }
}
