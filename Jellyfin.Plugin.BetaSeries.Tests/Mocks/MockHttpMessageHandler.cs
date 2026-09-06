using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.BetaSeries.Tests.Mocks;

public class CapturedRequest
{
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    public Uri? RequestUri { get; set; }

    public HttpRequestHeaders Headers { get; set; } = null!;

    public string? Content { get; set; }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Matcher, Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder)> _routes = new();
    private readonly List<CapturedRequest> _capturedRequests = new();

    public IReadOnlyList<CapturedRequest> Requests => _capturedRequests;

    public void Setup(
        HttpMethod method,
        string pathAndQueryContains,
        HttpStatusCode statusCode,
        string responseBody = "{}",
        string contentType = "application/json")
    {
        _routes.Add((
            req => req.Method == method && (req.RequestUri?.PathAndQuery.Contains(pathAndQueryContains, StringComparison.OrdinalIgnoreCase) ?? false),
            _ =>
            {
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, contentType),
                };
                return Task.FromResult(response);
            }
        ));
    }

    public void SetupCustom(
        Func<HttpRequestMessage, bool> matcher,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _routes.Add((matcher, responder));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = null;
        if (request.Content != null)
        {
            content = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        _capturedRequests.Add(new CapturedRequest
        {
            Method = request.Method,
            RequestUri = request.RequestUri,
            Headers = request.Headers,
            Content = content,
        });

        foreach (var (matcher, responder) in _routes)
        {
            if (matcher(request))
            {
                return await responder(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"errors\":[{\"code\":4001,\"text\":\"Not found\"}]}", Encoding.UTF8, "application/json"),
        };
    }
}
