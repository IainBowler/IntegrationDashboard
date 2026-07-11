using System.Net;
using System.Text;

namespace Api.Tests.Support;

/// <summary>
/// In-memory HttpMessageHandler stub with request capture, so HTTP-facing
/// services can be tested without any real network calls. The responder may
/// throw (e.g. TaskCanceledException) to simulate timeouts and network faults.
/// </summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // capture the body eagerly — the request content is disposed later
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return responder(request);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
