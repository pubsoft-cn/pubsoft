using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PubSoft.AspNetCore.Docs;

/// <summary>
/// Builds the API docs catalog once per process: EndpointDataSource enumeration
/// (covers every route regardless of ApiController), controller XML doc comments,
/// declared return types, and the optional semantic overlay.
/// </summary>
public sealed class ApiDocsCatalogService
{
    private readonly EndpointDataSource _endpoints;
    private readonly IHostEnvironment _env;
    private readonly ApiDocsOptions _options;
    private readonly object _lock = new();
    private object? _catalog;

    public ApiDocsCatalogService(EndpointDataSource endpoints, IHostEnvironment env,
        IOptions<ApiDocsOptions> options)
    { _endpoints = endpoints; _env = env; _options = options.Value; }

    /// <summary>The built catalog (cached; rebuild via <see cref="Invalidate"/>).</summary>
    public object GetCatalog()
    {
        lock (_lock) return _catalog ??= Build();
    }

    /// <summary>Drop the cached catalog so the next <see cref="GetCatalog"/> rebuilds.</summary>
    public void Invalidate()
    {
        lock (_lock) _catalog = null;
    }

    private object Build()
    {
        var xml = LoadXmlDocs();
        var (overlay, extra) = LoadOverlay();
        var items = new List<object>();

        foreach (var ep in _endpoints.Endpoints.OfType<RouteEndpoint>())
        {
            if (ep.RoutePattern.RawText is not { } raw || raw.Length == 0) continue;
            if (IsOwnEndpoint(raw)) continue;

            var path = "/" + raw.TrimStart('/');
            var methods = ep.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            var method = methods?.FirstOrDefault(m => m is not ("OPTIONS" or "HEAD")) ?? "GET";
            var cad = ep.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (cad == null && !_options.IncludeNonActionEndpoints) continue;
            if (raw.Contains('*')) continue; // catch-all SPA fallback 等永远不入目录

            string? summary = null;
            var paramDocs = new Dictionary<string, string>();
            string? returns = null;
            var ps = new List<object>();

            if (cad != null)
            {
                var key = XmlMemberKey(cad.MethodInfo);
                if (key != null && xml.TryGetValue(key, out var member))
                {
                    summary = member.Summary;
                    paramDocs = member.Params;
                }
                returns = FriendlyName(UnwrapTask(cad.MethodInfo.ReturnType));
                ps = cad.Parameters.Select(p => new
                {
                    name = p.Name,
                    at = p.BindingInfo?.BindingSource?.Id ?? "query",
                    type = FriendlyName(p.ParameterType),
                    required = p is ControllerParameterDescriptor cpd && IsRequired(cpd),
                    doc = paramDocs.TryGetValue(p.Name, out var pd) ? pd : null
                }).Cast<object>().ToList();
            }

            overlay.TryGetValue($"{method} {path}", out var ov);
            // honest labeling: only overlay or detected authorization metadata decide;
            // undetected auth must not be guessed — unauthenticated POSTs are not "jwt"
            var auth = ov?.Auth
                ?? (_options.DetectAuthMetadata && ep.Metadata.GetMetadata<IAuthorizeData>() != null
                    ? "jwt" : "unknown");

            items.Add(new
            {
                method,
                path,
                summary,
                group = ov?.Group ?? _options.GroupBy?.Invoke(method, path) ?? DefaultGroup(path),
                note = ov?.Note,
                auth,
                returns,
                parameters = ps,
                example = ov?.Example,
                controller = cad?.ControllerName
            });
        }

        foreach (var e in _options.ExtraEndpoints.Concat(extra))
            items.Add(new
            {
                method = e.Method,
                path = e.Path,
                summary = e.Summary,
                group = e.Group ?? "misc",
                note = e.Note,
                auth = e.Auth,
                returns = (string?)null,
                parameters = Array.Empty<object>(),
                example = e.Example,
                controller = (string?)null
            });

        var groups = items
            .GroupBy(i => (string)((dynamic)i).group)
            .OrderBy(g => g.Key)
            .Select(g => new { name = g.Key, endpoints = g.OrderBy(e => ((dynamic)e).path).Cast<object>().ToList() })
            .ToList();
        return new { title = _options.Title, generatedAt = DateTimeOffset.UtcNow, groups };
    }

    private bool IsOwnEndpoint(string raw)
    {
        var p = "/" + raw.TrimStart('/');
        // hosts may configure paths without a leading slash — normalize both sides
        return p == "/" + _options.CatalogPath.TrimStart('/')
            || (_options.PagePath is { } pp && p == "/" + pp.TrimStart('/'));
    }

    private static readonly NullabilityInfoContext Nullability = new();

    // path params are always required; otherwise consult NRT metadata + default value
    private static bool IsRequired(ControllerParameterDescriptor p)
        => p.BindingInfo?.BindingSource?.Id == "Path"
           || (!p.ParameterInfo.HasDefaultValue
               && Nullable.GetUnderlyingType(p.ParameterType) is null
               && Nullability.Create(p.ParameterInfo).ReadState != NullabilityState.Nullable);

    private static string DefaultGroup(string path)
    {
        var segs = path.Trim('/').Split('/');
        return segs.Length >= 2 ? $"{segs[0]}/{segs[1]}" : segs[0];
    }

    private static Type UnwrapTask(Type t)
        => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)
            ? t.GetGenericArguments()[0] : t;

    private static string FriendlyName(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } u) return FriendlyName(u) + "?";
        if (t.IsGenericType)
            return t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(",", t.GetGenericArguments().Select(FriendlyName)) + ">";
        return t.Name;
    }

    // ---------- overlay ----------

    private sealed record OverlayEntry(string? Group, string? Note, string? Auth, JsonElement? Example);

    private (Dictionary<string, OverlayEntry>, List<ApiDocExtraEndpoint>) LoadOverlay()
    {
        var map = new Dictionary<string, OverlayEntry>();
        var extra = new List<ApiDocExtraEndpoint>();
        if (string.IsNullOrEmpty(_options.OverlayPath)) return (map, extra);
        var f = Path.IsPathRooted(_options.OverlayPath)
            ? _options.OverlayPath
            : Path.Combine(_env.ContentRootPath, _options.OverlayPath);
        if (!File.Exists(f)) return (map, extra);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            if (doc.RootElement.TryGetProperty("endpoints", out var eps))
                foreach (var kv in eps.EnumerateObject())
                {
                    var v = kv.Value;
                    map[kv.Name] = new OverlayEntry(
                        v.TryGetProperty("group", out var g) ? g.GetString() : null,
                        v.TryGetProperty("note", out var n) ? n.GetString() : null,
                        v.TryGetProperty("auth", out var a) ? a.GetString() : null,
                        v.TryGetProperty("example", out var ex) ? ex.Clone() : null);
                }
            if (doc.RootElement.TryGetProperty("extra", out var xs))
                foreach (var e in xs.EnumerateArray())
                    extra.Add(new ApiDocExtraEndpoint
                    {
                        Method = e.TryGetProperty("method", out var m) ? m.GetString() ?? "WS" : "WS",
                        Path = e.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                        Summary = e.TryGetProperty("summary", out var s) ? s.GetString() : null,
                        Group = e.TryGetProperty("group", out var g) ? g.GetString() : null,
                        Note = e.TryGetProperty("note", out var n) ? n.GetString() : null,
                        Auth = e.TryGetProperty("auth", out var a) ? a.GetString() ?? "open" : "open",
                        Example = e.TryGetProperty("example", out var ex) ? ex.Clone() : null
                    });
        }
        catch { /* corrupt overlay never blocks doc generation */ }
        return (map, extra);
    }

    // ---------- XML doc ----------

    private sealed record XmlMember(string? Summary, Dictionary<string, string> Params);

    /// <summary>Parse every *.xml doc file next to the entry assembly set.</summary>
    private static Dictionary<string, XmlMember> LoadXmlDocs()
    {
        var map = new Dictionary<string, XmlMember>();
        string[] files;
        try
        {
            files = Directory.GetFiles(AppContext.BaseDirectory, "*.xml");
        }
        catch
        {
            return map;
        }
        foreach (var f in files)
        {
            try
            {
                var doc = XDocument.Load(f);
                foreach (var m in doc.Descendants("member"))
                {
                    var name = m.Attribute("name")?.Value;
                    if (name == null || !name.StartsWith("M:", StringComparison.Ordinal)) continue;
                    var ps = m.Elements("param")
                        .ToDictionary(p => p.Attribute("name")?.Value ?? "", p => p.Value.Trim());
                    map.TryAdd(name[2..], new XmlMember(m.Element("summary")?.Value.Trim(), ps));
                }
            }
            catch { /* malformed xml doc file never blocks catalog generation */ }
        }
        return map;
    }

    private static string? XmlMemberKey(MethodInfo m)
    {
        var t = m.DeclaringType;
        if (t == null) return null;
        return $"{t.FullName}.{m.Name}({string.Join(",", m.GetParameters().Select(p => XmlTypeName(p.ParameterType)))})";
    }

    private static string XmlTypeName(Type t)
    {
        if (t.IsGenericParameter) return t.Name;
        var n = t.FullName ?? t.Name;
        if (t.IsGenericType)
            n = n[..n.IndexOf('`')] + "{" + string.Join(",", t.GetGenericArguments().Select(XmlTypeName)) + "}";
        return n;
    }
}
