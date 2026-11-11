using System.Reflection;
using System.Runtime.Loader;

namespace TypedMock.Core.Contracts;

public class DynamicContractLoader
{
    public static IReadOnlyList<Type> LoadContractsFromAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}", assemblyPath);
        }

        var fullPath = Path.GetFullPath(assemblyPath);
        Assembly assembly;

        try
        {
            // First try default load context so types match any already-loaded dependencies
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }
        catch
        {
            // Fall back to isolated ALC with directory resolution
            var loadContext = new AssemblyLoadContext($"TypedMock_ALC_{Path.GetFileNameWithoutExtension(fullPath)}_{Guid.NewGuid():N}", isCollectible: true);
            var assemblyDir = Path.GetDirectoryName(fullPath)!;
            loadContext.Resolving += (context, assemblyName) =>
            {
                var dependencyPath = Path.Combine(assemblyDir, $"{assemblyName.Name}.dll");
                return File.Exists(dependencyPath) ? context.LoadFromAssemblyPath(dependencyPath) : null;
            };
            assembly = loadContext.LoadFromAssemblyPath(fullPath);
        }

        return DiscoverContractInterfaces(assembly);
    }

    public static IReadOnlyList<Type> LoadContractsFromDirectory(string directoryPath, string searchPattern = "*.dll")
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        var discovered = new List<Type>();
        var dllFiles = Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);

        foreach (var dll in dllFiles)
        {
            // Skip common runtime / framework assemblies
            var fileName = Path.GetFileName(dll);
            if (fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var contracts = LoadContractsFromAssembly(dll);
                discovered.AddRange(contracts);
            }
            catch
            {
                // Assembly might not be a valid .NET assembly or might be a native binary
            }
        }

        return discovered.Distinct().ToList();
    }

    public static IReadOnlyList<Type> DiscoverContractInterfaces(Assembly assembly)
    {
        var discovered = new List<Type>();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
        }

        foreach (var type in types)
        {
            if (!type.IsInterface) continue;

            if (IsRestEaseContract(type))
            {
                discovered.Add(type);
            }
        }

        return discovered;
    }

    public static bool IsRestEaseContract(Type type)
    {
        if (!type.IsInterface) return false;

        // Inspect custom attribute data without strict ALC type identity requirement
        foreach (var method in type.GetMethods())
        {
            foreach (var attr in method.CustomAttributes)
            {
                var attrTypeName = attr.AttributeType.FullName ?? attr.AttributeType.Name;
                if (attrTypeName.StartsWith("RestEase.", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
