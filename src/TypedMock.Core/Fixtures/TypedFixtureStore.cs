using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypedMock.Core.Fixtures;

public class ContractValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class TypedFixtureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly List<TypedFixture> _fixtures = [];
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public Action<string>? OnLogWarning { get; set; }

    public IReadOnlyList<TypedFixture> AllFixtures
    {
        get
        {
            lock (_lock)
            {
                return _fixtures.ToList();
            }
        }
    }

    public void AddFixture(TypedFixture fixture)
    {
        lock (_lock)
        {
            _fixtures.RemoveAll(f =>
                f.Contract == fixture.Contract &&
                f.Method == fixture.Method &&
                f.Scenario == fixture.Scenario &&
                AreParamsEqual(f.Request.PathParameters, fixture.Request.PathParameters));

            _fixtures.Add(fixture);
        }
    }

    public void LoadFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        var files = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var fixture = JsonSerializer.Deserialize<TypedFixture>(json, JsonOptions);
                if (fixture != null && !string.IsNullOrEmpty(fixture.Contract))
                {
                    AddFixture(fixture);
                }
            }
            catch (Exception ex)
            {
                var msg = $"[TypedFixtureStore] Failed to load fixture from {file}: {ex.Message}";
                if (OnLogWarning != null)
                {
                    OnLogWarning(msg);
                }
                else
                {
                    Console.WriteLine(msg);
                }
            }
        }
    }

    public async Task SaveFixtureAsync(string directoryPath, TypedFixture fixture)
    {
        var targetDir = Path.Combine(directoryPath, fixture.Contract, fixture.Method);
        Directory.CreateDirectory(targetDir);

        var sanitizedScenario = string.Join("_", fixture.Scenario.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(targetDir, $"{sanitizedScenario}.json");

        var json = JsonSerializer.Serialize(fixture, JsonOptions);

        await _fileLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(filePath, json);
        }
        finally
        {
            _fileLock.Release();
        }

        AddFixture(fixture);
    }

    public static object? ValidateAndDeserialize(TypedFixture fixture, Type targetType)
    {
        if (fixture.Response.Data is null)
        {
            return null;
        }

        try
        {
            var rawJson = fixture.Response.Data.ToJsonString();
            var result = JsonSerializer.Deserialize(rawJson, targetType, JsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            throw new ContractValidationException(
                $"Fixture '{fixture.Scenario}' for {fixture.Contract}.{fixture.Method} " +
                $"cannot be deserialized into contract type '{targetType.FullName}'. " +
                $"The contract may have changed or the fixture is malformed: {ex.Message}", ex);
        }
    }

    public TypedFixture? FindBestMatch(
        string contract,
        string method,
        string? requestedScenario,
        Dictionary<string, string> pathParams)
    {
        List<TypedFixture> candidates;
        lock (_lock)
        {
            candidates = _fixtures.Where(f =>
                string.Equals(f.Contract, contract, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Method, method, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (candidates.Count == 0) return null;

        var hasExplicitScenario = !string.IsNullOrWhiteSpace(requestedScenario) &&
                                  !string.Equals(requestedScenario, "default", StringComparison.OrdinalIgnoreCase);

        // 1. Exact scenario match requested via header / query
        if (hasExplicitScenario)
        {
            var scenarioMatch = candidates.FirstOrDefault(f =>
                string.Equals(f.Scenario, requestedScenario, StringComparison.OrdinalIgnoreCase));
            if (scenarioMatch != null) return scenarioMatch;

            // When an explicit scenario is requested, don't silently serve fallback data
            return null;
        }

        // 2. Match by path parameters
        var paramMatch = candidates.FirstOrDefault(f => AreParamsEqual(f.Request.PathParameters, pathParams));
        if (paramMatch != null) return paramMatch;

        // 3. Fallback to "default" scenario
        var defaultMatch = candidates.FirstOrDefault(f =>
            string.Equals(f.Scenario, "default", StringComparison.OrdinalIgnoreCase));
        if (defaultMatch != null) return defaultMatch;

        // 4. Fallback to any first candidate
        return candidates.FirstOrDefault();
    }

    private static bool AreParamsEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count == 0 && b.Count == 0) return true;
        if (a.Count != b.Count) return false;

        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv) || !string.Equals(v, bv, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
