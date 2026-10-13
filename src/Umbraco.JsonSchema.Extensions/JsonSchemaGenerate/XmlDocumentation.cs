using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Umbraco.JsonSchema.Extensions;

/// <summary>
/// Reads summaries from the XML documentation files located next to assemblies, resolving <c>inheritdoc</c> from base
/// types and interfaces.
/// </summary>
internal sealed class XmlDocumentation
{
    private const int MaxInheritDocDepth = 10;

    private readonly Func<Assembly, string?> _getAssemblyPath;
    private readonly Action<string>? _logMessage;
    private readonly Dictionary<Assembly, Dictionary<string, XElement>> _members = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocumentation" /> class.
    /// </summary>
    /// <param name="getAssemblyPath">Gets the file path of an assembly (the XML documentation file is expected next to it).</param>
    /// <param name="logMessage">Logs a message, e.g. when an XML documentation file can't be read and is ignored.</param>
    public XmlDocumentation(Func<Assembly, string?> getAssemblyPath, Action<string>? logMessage = null)
    {
        _getAssemblyPath = getAssemblyPath;
        _logMessage = logMessage;
    }

    /// <summary>
    /// Gets the summary of the specified type, property or field.
    /// </summary>
    /// <param name="member">The type, property or field.</param>
    /// <returns>The summary text, or <c>null</c> when not documented.</returns>
    public string? GetSummary(MemberInfo member)
    {
        XElement? summary = GetSummaryElement(member, 0);
        if (summary is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        AppendText(builder, summary);

        // Remove the indentation of each line and collapse consecutive empty lines
        var lines = new List<string>();
        foreach (var line in builder.ToString().Replace("\r\n", "\n").Split('\n').Select(x => x.Trim()))
        {
            if (line.Length > 0 || (lines.Count > 0 && lines[^1].Length > 0))
            {
                lines.Add(line);
            }
        }

        var text = string.Join("\n", lines).Trim();

        return text.Length > 0 ? text : null;
    }

    private static string? GetMemberId(MemberInfo member) => member switch
    {
        Type type => "T:" + GetTypeId(type),
        PropertyInfo property when property.DeclaringType is not null => $"P:{GetTypeId(property.DeclaringType)}.{property.Name}",
        FieldInfo field when field.DeclaringType is not null => $"F:{GetTypeId(field.DeclaringType)}.{field.Name}",
        _ => null,
    };

    private static string GetTypeId(Type type)
        => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName?.Replace('+', '.') ?? type.Name;

    private static IEnumerable<MemberInfo> GetBaseMembers(MemberInfo member)
    {
        Type? type = member as Type ?? member.DeclaringType;
        if (type is null)
        {
            yield break;
        }

        IEnumerable<Type> baseTypes = GetBaseTypes(type).Concat(type.GetInterfaces());
        if (member is Type)
        {
            foreach (Type baseType in baseTypes)
            {
                yield return baseType;
            }
        }
        else if (member is PropertyInfo)
        {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (Type baseType in baseTypes)
            {
                PropertyInfo? baseProperty = baseType.GetProperties(bindingFlags).FirstOrDefault(x => x.Name == member.Name && x.GetIndexParameters().Length == 0);
                if (baseProperty is not null)
                {
                    yield return baseProperty;
                }
            }
        }
    }

    private static IEnumerable<Type> GetBaseTypes(Type type)
    {
        for (Type? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            yield return baseType;
        }
    }

    private static void AppendText(StringBuilder builder, XElement element)
    {
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text)
            {
                builder.Append(text.Value);
            }
            else if (node is XElement child)
            {
                switch (child.Name.LocalName)
                {
                    case "see" or "seealso" when !child.Nodes().Any():
                        builder.Append(child.Attribute("langword")?.Value
                            ?? GetCrefName(child.Attribute("cref")?.Value)
                            ?? child.Attribute("href")?.Value);
                        break;
                    case "paramref" or "typeparamref":
                        builder.Append(child.Attribute("name")?.Value);
                        break;
                    case "para":
                        builder.Append('\n');
                        AppendText(builder, child);
                        builder.Append('\n');
                        break;
                    case "br":
                        builder.Append('\n');
                        break;
                    default:
                        AppendText(builder, child);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Gets the display name of a code reference, e.g. <c>Type</c> for <c>T:Namespace.Type</c> and <c>Type.Member</c>
    /// for <c>P:Namespace.Type.Member</c>.
    /// </summary>
    private static string? GetCrefName(string? cref)
    {
        if (string.IsNullOrEmpty(cref))
        {
            return null;
        }

        var isType = cref.StartsWith("T:", StringComparison.Ordinal);
        var name = cref.Length > 1 && cref[1] == ':' ? cref.Substring(2) : cref;

        // Remove method parameters and generic arity
        var parametersIndex = name.IndexOf('(');
        if (parametersIndex >= 0)
        {
            name = name.Substring(0, parametersIndex);
        }

        var segments = name.Split('.').Select(x => x.Split('`')[0]).ToArray();

        return string.Join(".", segments.Skip(Math.Max(0, segments.Length - (isType ? 1 : 2))));
    }

    private XElement? GetSummaryElement(MemberInfo member, int depth)
    {
        string? id = GetMemberId(member);
        if (id is null || depth > MaxInheritDocDepth || !GetMembers(member.Module.Assembly).TryGetValue(id, out XElement? element))
        {
            return null;
        }

        if (element.Element("summary") is XElement summary)
        {
            return summary;
        }

        if (element.Element("inheritdoc") is not XElement inheritDoc)
        {
            return null;
        }

        if (inheritDoc.Attribute("cref")?.Value is string cref)
        {
            // Look up the referenced member in the documentation of the assemblies in the type hierarchy
            IEnumerable<Assembly> assemblies = new[] { member.Module.Assembly }.Concat(GetBaseMembers(member).Select(x => x.Module.Assembly)).Distinct();
            foreach (Assembly assembly in assemblies)
            {
                if (GetMembers(assembly).TryGetValue(cref, out XElement? referenced) && referenced.Element("summary") is XElement referencedSummary)
                {
                    return referencedSummary;
                }
            }

            return null;
        }

        return GetBaseMembers(member).Select(x => GetSummaryElement(x, depth + 1)).FirstOrDefault(x => x is not null);
    }

    private Dictionary<string, XElement> GetMembers(Assembly assembly)
    {
        if (!_members.TryGetValue(assembly, out Dictionary<string, XElement>? members))
        {
            members = [];

            string? assemblyPath = _getAssemblyPath(assembly);
            string? xmlDocsPath = string.IsNullOrEmpty(assemblyPath) ? null : Path.ChangeExtension(assemblyPath, ".xml");
            if (xmlDocsPath is not null && File.Exists(xmlDocsPath))
            {
                foreach (XElement member in LoadMemberElements(xmlDocsPath))
                {
                    if (member.Attribute("name")?.Value is string name)
                    {
                        members[name] = member;
                    }
                }
            }

            _members[assembly] = members;
        }

        return members;
    }

    private IEnumerable<XElement> LoadMemberElements(string xmlDocsPath)
    {
        try
        {
            // Preserve whitespace between elements (e.g. the space in <c>a</c> <c>b</c>), which is otherwise dropped
            return XDocument.Load(xmlDocsPath, LoadOptions.PreserveWhitespace).Root?.Element("members")?.Elements("member") ?? [];
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            // Descriptions are optional, so ignore a file that can't be read (e.g. a malformed or unrelated XML file)
            _logMessage?.Invoke($"Ignoring XML documentation file '{xmlDocsPath}', as it can't be read: {ex.Message}");

            return [];
        }
    }
}
