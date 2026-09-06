using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly object _lock = new();
    private readonly List<(Func<HttpRequestMessage, bool> Matcher, Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder)> _routes = new();
    private readonly List<CapturedRequest> _capturedRequests = new();

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _capturedRequests.ToList();
            }
        }
    }

    public void Setup(
        HttpMethod method,
        string pathAndQueryContains,
        HttpStatusCode statusCode,
        string responseBody = "{}",
        string contentType = "application/json")
    {
        lock (_lock)
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
    }

    public void SetupCustom(
        Func<HttpRequestMessage, bool> matcher,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        lock (_lock)
        {
            _routes.Add((matcher, responder));
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? content = null;
        if (request.Content != null)
        {
            content = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        lock (_lock)
        {
            _capturedRequests.Add(new CapturedRequest
            {
                Method = request.Method,
                RequestUri = request.RequestUri,
                Headers = request.Headers,
                Content = content,
            });
        }

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
