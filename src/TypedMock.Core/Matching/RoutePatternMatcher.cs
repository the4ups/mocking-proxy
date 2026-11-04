namespace TypedMock.Core.Matching;

public record RouteMatchResult(bool IsMatch, Dictionary<string, string> PathParameters)
{
    public static RouteMatchResult Failure { get; } = new(false, []);

    public static RouteMatchResult Success(Dictionary<string, string> parameters) =>
        new(true, parameters);
}

public static class RoutePatternMatcher
{
    public static RouteMatchResult Match(string template, string actualPath)
    {
        var templateSegments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var actualSegments = actualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (templateSegments.Length != actualSegments.Length)
        {
            return RouteMatchResult.Failure;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < templateSegments.Length; i++)
        {
            var t = templateSegments[i];
            var a = actualSegments[i];

            if (t.StartsWith('{') && t.EndsWith('}'))
            {
                var paramName = t[1..^1].Trim();
                parameters[paramName] = Uri.UnescapeDataString(a);
            }
            else if (!string.Equals(t, a, StringComparison.OrdinalIgnoreCase))
            {
                return RouteMatchResult.Failure;
            }
        }

        return RouteMatchResult.Success(parameters);
    }
}
