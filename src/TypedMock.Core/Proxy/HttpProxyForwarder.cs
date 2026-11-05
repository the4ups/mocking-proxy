using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using TypedMock.Core.Fixtures;
using TypedMock.Core.Generators;
using TypedMock.Core.Models;

namespace TypedMock.Core.Proxy;

public class HttpProxyForwarder(HttpClient httpClient, TypedFixtureStore fixtureStore)
{
    private readonly List<RecordedInteraction> _recordedInteractions = [];

    public IReadOnlyList<RecordedInteraction> RecordedInteractions
    {
        get
        {
            lock (_recordedInteractions)
            {
                return _recordedInteractions.ToList();
            }
        }
    }

    public async Task<bool> ForwardAndRecordAsync(
        HttpContext context,
        Uri targetBaseUri,
        ContractEndpoint? matchedEndpoint,
        Dictionary<string, string> pathParams,
        string fixturesDirectory,
        string scenarioName)
    {
        var targetUrl = new Uri(targetBaseUri, context.Request.Path + context.Request.QueryString);
        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

        // Copy request headers
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.StartsWith(':') ||
                header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        // Copy request body
        JsonNode? requestBodyNode = null;
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var bodyText = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (!string.IsNullOrWhiteSpace(bodyText))
            {
                upstreamRequest.Content = new StringContent(bodyText);
                if (context.Request.ContentType != null)
                {
                    upstreamRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
                }

                try
                {
                    requestBodyNode = JsonNode.Parse(bodyText);
                }
                catch
                {
                    // Body might not be JSON
                }
            }
        }

        // Send upstream
        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "TypedMock: Failed to forward request to upstream service",
                targetUrl = targetUrl.ToString(),
                details = ex.Message
            });
            return false;
        }

        // Copy status code
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        // Copy response headers
        foreach (var header in upstreamResponse.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in upstreamResponse.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        context.Response.Headers.Remove("transfer-encoding");

        // Read response body
        var responseBytes = await upstreamResponse.Content.ReadAsByteArrayAsync();
        var responseString = System.Text.Encoding.UTF8.GetString(responseBytes);

        // If matched contract endpoint, record typed fixture
        if (matchedEndpoint != null)
        {
            JsonNode? responseNode = null;
            try
            {
                responseNode = JsonNode.Parse(responseString);
            }
            catch
            {
                // Not JSON, ignore parsing
            }

            // Determine scenario name: if custom header present, use it, else generate from params
            var effectiveScenario = scenarioName;
            if (string.Equals(effectiveScenario, "default", StringComparison.OrdinalIgnoreCase) && pathParams.Count > 0)
            {
                effectiveScenario = string.Join("_", pathParams.Select(p => $"{p.Key}_{p.Value}"));
            }

            var fixture = new TypedFixture
            {
                Contract = matchedEndpoint.InterfaceType.Name,
                Method = matchedEndpoint.Method.Name,
                Scenario = effectiveScenario,
                Request = new TypedFixtureRequest
                {
                    HttpMethod = context.Request.Method,
                    Path = context.Request.Path,
                    PathParameters = new Dictionary<string, string>(pathParams),
                    QueryParameters = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
                    Body = requestBodyNode
                },
                Response = new TypedFixtureResponse
                {
                    StatusCode = (int)upstreamResponse.StatusCode,
                    ContentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/json",
                    Data = responseNode
                }
            };

            // Contract Validation: if response type is known and 2xx, validate against DTO
            object? deserializedObj = null;
            if (matchedEndpoint.ResponseType != null && (int)upstreamResponse.StatusCode is >= 200 and < 300)
            {
                deserializedObj = TypedFixtureStore.ValidateAndDeserialize(fixture, matchedEndpoint.ResponseType);
            }

            var interaction = new RecordedInteraction
            {
                Endpoint = matchedEndpoint,
                PathParameters = new Dictionary<string, string>(pathParams),
                QueryParameters = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
                RequestBody = requestBodyNode?.ToJsonString(),
                StatusCode = (int)upstreamResponse.StatusCode,
                ResponseBody = responseString,
                DeserializedResponse = deserializedObj
            };

            lock (_recordedInteractions)
            {
                _recordedInteractions.Add(interaction);
            }

            await fixtureStore.SaveFixtureAsync(fixturesDirectory, fixture);
        }

        await context.Response.Body.WriteAsync(responseBytes);
        return true;
    }
}
