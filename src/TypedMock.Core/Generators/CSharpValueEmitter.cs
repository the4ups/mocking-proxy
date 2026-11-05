using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace TypedMock.Core.Generators;

public static class CSharpValueEmitter
{
    public static string Emit(object? value, int indentLevel = 0, ISet<string>? collectedNamespaces = null)
    {
        if (value is null)
        {
            return "null";
        }

        var indent = new string(' ', indentLevel);
        var innerIndent = new string(' ', indentLevel + 4);

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is string s)
        {
            return $"\"{EscapeString(s)}\"";
        }

        if (value is char c)
        {
            return $"'{EscapeString(c.ToString())}'";
        }

        if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (value is short sh) return sh.ToString(CultureInfo.InvariantCulture);
        if (value is byte by) return by.ToString(CultureInfo.InvariantCulture);
        if (value is sbyte sb) return sb.ToString(CultureInfo.InvariantCulture);
        if (value is ushort us) return us.ToString(CultureInfo.InvariantCulture);
        if (value is uint ui) return $"{ui.ToString(CultureInfo.InvariantCulture)}u";
        if (value is long l) return $"{l.ToString(CultureInfo.InvariantCulture)}L";
        if (value is ulong ul) return $"{ul.ToString(CultureInfo.InvariantCulture)}uL";
        if (value is float f) return $"{f.ToString(CultureInfo.InvariantCulture)}f";
        if (value is double d) return $"{d.ToString(CultureInfo.InvariantCulture)}d";
        if (value is decimal m) return $"{m.ToString(CultureInfo.InvariantCulture)}m";

        if (value is DateOnly dateOnly)
        {
            collectedNamespaces?.Add("System");
            collectedNamespaces?.Add("System.Globalization");
            return $"DateOnly.Parse(\"{dateOnly:yyyy-MM-dd}\", CultureInfo.InvariantCulture)";
        }

        if (value is TimeOnly timeOnly)
        {
            collectedNamespaces?.Add("System");
            collectedNamespaces?.Add("System.Globalization");
            return $"TimeOnly.Parse(\"{timeOnly:HH:mm:ss}\", CultureInfo.InvariantCulture)";
        }

        if (value is DateTime dt)
        {
            collectedNamespaces?.Add("System");
            collectedNamespaces?.Add("System.Globalization");
            return $"DateTime.Parse(\"{dt:O}\", CultureInfo.InvariantCulture)";
        }

        if (value is DateTimeOffset dto)
        {
            collectedNamespaces?.Add("System");
            collectedNamespaces?.Add("System.Globalization");
            return $"DateTimeOffset.Parse(\"{dto:O}\", CultureInfo.InvariantCulture)";
        }

        if (value is TimeSpan ts)
        {
            collectedNamespaces?.Add("System");
            collectedNamespaces?.Add("System.Globalization");
            return $"TimeSpan.Parse(\"{ts}\", CultureInfo.InvariantCulture)";
        }

        if (value is Guid guid)
        {
            collectedNamespaces?.Add("System");
            return $"Guid.Parse(\"{guid}\")";
        }

        var type = value.GetType();

        if (type.IsEnum)
        {
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                collectedNamespaces?.Add(type.Namespace);
            }
            return $"{type.Name}.{value}";
        }

        if (value is IEnumerable enumerable)
        {
            collectedNamespaces?.Add("System.Collections.Generic");

            Type itemType;
            if (type.IsArray)
            {
                itemType = type.GetElementType()!;
            }
            else if (type.IsGenericType && type.GetGenericArguments().Length > 0)
            {
                itemType = type.GetGenericArguments()[0];
            }
            else
            {
                itemType = typeof(object);
            }

            var cleanItemTypeName = GetCleanTypeName(itemType, collectedNamespaces);
            var items = enumerable.Cast<object?>().ToList();

            if (items.Count == 0)
            {
                return $"new List<{cleanItemTypeName}>()";
            }

            var sbList = new StringBuilder();
            sbList.AppendLine($"new List<{cleanItemTypeName}>");
            sbList.AppendLine($"{indent}{{");

            for (var idx = 0; idx < items.Count; idx++)
            {
                var itemCode = Emit(items[idx], indentLevel + 4, collectedNamespaces);
                sbList.Append(innerIndent);
                sbList.Append(itemCode);
                if (idx < items.Count - 1)
                {
                    sbList.Append(',');
                }
                sbList.AppendLine();
            }

            sbList.Append($"{indent}}}");
            return sbList.ToString();
        }

        // Complex Object / Record / DTO
        if (!string.IsNullOrEmpty(type.Namespace))
        {
            collectedNamespaces?.Add(type.Namespace);
        }

        var cleanTypeName = GetCleanTypeName(type, collectedNamespaces);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod != null && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .ToList();

        var sbObj = new StringBuilder();
        sbObj.AppendLine($"new {cleanTypeName}");
        sbObj.AppendLine($"{indent}{{");

        var emittedProps = new List<(string Name, string ValueCode)>();
        foreach (var prop in properties)
        {
            var propVal = prop.GetValue(value);
            if (propVal == null)
            {
                // Only emit null if property is required
                var isRequired = prop.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute");
                if (isRequired)
                {
                    emittedProps.Add((prop.Name, "null"));
                }
            }
            else
            {
                emittedProps.Add((prop.Name, Emit(propVal, indentLevel + 4, collectedNamespaces)));
            }
        }

        for (var idx = 0; idx < emittedProps.Count; idx++)
        {
            var (name, valCode) = emittedProps[idx];
            sbObj.Append($"{innerIndent}{name} = {valCode}");
            if (idx < emittedProps.Count - 1)
            {
                sbObj.Append(',');
            }
            sbObj.AppendLine();
        }

        sbObj.Append($"{indent}}}");
        return sbObj.ToString();
    }

    public static string GetCleanTypeName(Type type, ISet<string>? collectedNamespaces = null)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(object)) return "object";

        if (type.IsArray)
        {
            return $"{GetCleanTypeName(type.GetElementType()!, collectedNamespaces)}[]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
        {
            return $"{GetCleanTypeName(underlyingType, collectedNamespaces)}?";
        }

        if (type.IsGenericType)
        {
            var genericTypeDef = type.GetGenericTypeDefinition();
            var genericName = genericTypeDef.Name;
            var backtickIndex = genericName.IndexOf('`');
            if (backtickIndex > 0)
            {
                genericName = genericName[..backtickIndex];
            }

            if (!string.IsNullOrEmpty(type.Namespace))
            {
                collectedNamespaces?.Add(type.Namespace);
            }

            var typeArgs = type.GetGenericArguments()
                .Select(t => GetCleanTypeName(t, collectedNamespaces));
            return $"{genericName}<{string.Join(", ", typeArgs)}>";
        }

        if (!string.IsNullOrEmpty(type.Namespace))
        {
            collectedNamespaces?.Add(type.Namespace);
        }

        return type.Name;
    }

    internal static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
}
