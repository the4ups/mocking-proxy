using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using TypedMock.Core.Exceptions;
using TypedMock.Core.Generators;
using TypedMock.Core.Models;

namespace TypedMock.Core.Proxy;

public class HttpProxyForwarder(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        string? bodyText = null;
        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            bodyText = await reader.ReadToEndAsync();
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

        // If matched contract endpoint, record typed interaction
        if (matchedEndpoint != null)
        {
            // Contract Validation: validate upstream response schema against DTO
            object? deserializedObj = null;
            if (matchedEndpoint.ResponseType != null && (int)upstreamResponse.StatusCode is >= 200 and < 300)
            {
                try
                {
                    deserializedObj = JsonSerializer.Deserialize(responseString, matchedEndpoint.ResponseType, JsonOptions);
                }
                catch (Exception ex)
                {
                    throw new ContractValidationException(
                        $"Contract drift detected for '{matchedEndpoint.InterfaceType.Name}.{matchedEndpoint.Method.Name}'. Upstream response cannot be deserialized into '{matchedEndpoint.ResponseType.Name}': {ex.Message}", ex);
                }
            }

            // Request body deserialization if body type is known
            object? deserializedReqObj = null;
            if (matchedEndpoint.RequestBodyType != null && !string.IsNullOrWhiteSpace(bodyText))
            {
                try
                {
                    deserializedReqObj = JsonSerializer.Deserialize(bodyText, matchedEndpoint.RequestBodyType, JsonOptions);
                }
                catch
                {
                    // Ignore request body deserialization failure
                }
            }

            var interaction = new RecordedInteraction
            {
                Endpoint = matchedEndpoint,
                PathParameters = new Dictionary<string, string>(pathParams),
                QueryParameters = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
                RequestBody = requestBodyNode?.ToJsonString(),
                DeserializedRequestBody = deserializedReqObj,
                StatusCode = (int)upstreamResponse.StatusCode,
                ResponseBody = responseString,
                DeserializedResponse = deserializedObj
            };

            lock (_recordedInteractions)
            {
                _recordedInteractions.Add(interaction);
            }
        }

        await context.Response.Body.WriteAsync(responseBytes);
        return true;
    }
}
