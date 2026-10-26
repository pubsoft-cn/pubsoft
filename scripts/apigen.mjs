#!/usr/bin/env node
/**
 * PubSoft API client codegen — pulls a PubSoft.AspNetCore.Docs catalog and emits a
 * clean, typed client. No Swagger/OpenAPI needed.
 *
 *   node apigen.mjs <catalog-url|catalog.json> [-o out] [--lang ts|cs] [--name Acme]
 *
 *   --lang ts (default) → TypeScript fetch client (default api-client.ts)
 *   --lang cs           → C# HttpClient class + source-gen JsonSerializerContext
 *                         (default ApiClient.cs, AOT-clean)
 *
 * Type inference is example-driven: overlay `example` payloads define response types
 * precisely (far cleaner than OpenAPI schema guessing). Endpoints without an example
 * emit the declared CLR return type when mappable, else unknown/JsonElement.
 *
 * Environment notes:
 *   - Node >= 18 required (top-level await + global fetch).
 *   - The catalog endpoint is DevelopmentOnly by default — run the host with
 *     ASPNETCORE_ENVIRONMENT=Development, pass a saved catalog.json, have the host
 *     explicitly disable DevelopmentOnly, or pass --header 'Authorization: Bearer …'.
 */
import { writeFileSync, readFileSync } from 'node:fs';

const major = parseInt(process.versions.node, 10);
if (!Number.isFinite(major) || major < 18) {
    console.error(`apigen requires Node >= 18 (found ${process.versions.node})`);
    process.exit(1);
}

const [,, ...argv] = process.argv;
const src = argv.find(a => /^https?:/.test(a) || /\.json$/.test(a));
const opt = (flag, dflt) => { const i = argv.indexOf(flag); return i > -1 ? argv[i + 1] : dflt; };
const lang = opt('--lang', 'ts');
const sdkName = opt('--name', 'Api');
const headerArg = opt('--header', null); // 'Authorization: Bearer x' for gated catalogs
const out = opt('-o', lang === 'cs' ? `${sdkName}Client.cs` : 'api-client.ts');

if (!src) {
    console.error('usage: node apigen.mjs <catalog-url|catalog.json> [-o file] [--lang ts|cs] [--name X]');
    process.exit(1);
}
if (lang !== 'ts' && lang !== 'cs') { console.error(`--lang must be ts|cs, got ${lang}`); process.exit(1); }

const catalog = await loadCatalog(src);

async function loadCatalog(s) {
    if (!/^https?:/.test(s)) {
        try { return JSON.parse(readFileSync(s, 'utf8')); }
        catch (e) { console.error(`cannot read catalog file ${s}: ${e.message}`); process.exit(1); }
    }
    let res;
    try {
        const headers = {};
        if (headerArg) { const i = headerArg.indexOf(':'); headers[headerArg.slice(0, i).trim()] = headerArg.slice(i + 1).trim(); }
        res = await fetch(s, { headers });
    } catch (e) {
        console.error(`catalog unreachable: ${s} — ${e.message}`);
        process.exit(1);
    }
    const ct = res.headers.get('content-type') || '';
    if (!res.ok || !ct.includes('json')) {
        console.error(`catalog endpoint returned HTTP ${res.status} (${ct || 'no content-type'}) — ` +
            `the docs catalog is DevelopmentOnly by default: run the host in Development, ` +
            `set options.DevelopmentOnly = false deliberately, or pass --header for auth.`);
        process.exit(1);
    }
    return res.json();
}
if (!catalog || !Array.isArray(catalog.groups)) {
    console.error('response is not a PubSoft docs catalog (missing "groups") — wrong endpoint?');
    process.exit(1);
}

// ---- naming ------------------------------------------------------------------
const seen = new Map();
function fnName(method, path) {
    const segs = path.split('/').filter(s => s && s !== 'api' && !/^v\d+$/.test(s));
    const parts = segs.map(s => s.replace(/[{}]/g, '').replace(/[^a-zA-Z0-9]+(.)/g, (_, c) => c.toUpperCase()));
    let name = method.toLowerCase() + parts.map(p => p[0]?.toUpperCase() + p.slice(1)).join('');
    name = name.replace(/[^a-zA-Z0-9]/g, '');
    if (seen.has(name)) { const n = seen.get(name) + 1; seen.set(name, n); name += n; } else seen.set(name, 1);
    return name;
}
// method prefix included — same path on different verbs must not collide on one type
function ifaceName(method, path) {
    const segs = path.split('/').filter(s => s && s !== 'api' && !/^v\d+$/.test(s));
    return method[0].toUpperCase() + method.slice(1).toLowerCase()
        + segs.map(s => s.replace(/[{}]/g, '')).map(p => p[0]?.toUpperCase() + p.slice(1)).join('')
        + 'Response';
}
const pascal = s => s ? s[0].toUpperCase() + s.slice(1) : s;

// ---- example-driven type inference --------------------------------------------
const tsIfaces = new Map(), csTypes = new Map();
function tsType(v, hint) {
    if (v === null || v === undefined) return 'unknown';
    if (Array.isArray(v)) return v.length ? `${tsType(v[0], hint)}[]` : 'unknown[]';
    switch (typeof v) {
        case 'number': return 'number';
        case 'string': return 'string';
        case 'boolean': return 'boolean';
        case 'object': {
            if (!tsIfaces.has(hint)) {
                const fields = Object.entries(v).map(([k, val]) =>
                    `  ${/^[$_a-zA-Z][\w$]*$/.test(k) ? k : JSON.stringify(k)}${val === null ? '?' : ''}: ${tsType(val, hint + pascal(k))};`);
                tsIfaces.set(hint, `export interface ${hint} {\n${fields.join('\n')}\n}`);
            }
            return hint;
        }
    }
    return 'unknown';
}
function csType(v, hint) {
    if (v === null || v === undefined) return 'JsonElement?';
    if (Array.isArray(v)) return v.length ? `List<${csType(v[0], hint + 'Item')}>` : 'List<JsonElement>';
    switch (typeof v) {
        case 'number': return Number.isInteger(v) ? 'long' : 'double';
        case 'string': return 'string';
        case 'boolean': return 'bool';
        case 'object': {
            if (!csTypes.has(hint)) {
                const props = Object.entries(v).map(([k, val]) => {
                    const t = csType(val, hint + pascal(k));
                    const nullable = val === null || t.endsWith('?') ? '?' : '';
                    const req = nullable === '' ? 'required ' : '';
                    return `    [JsonPropertyName("${k}")]\n    public ${req}${t}${nullable} ${pascal(k)} { get; init; }`;
                });
                csTypes.set(hint, `public sealed record ${hint}\n{\n${props.join('\n')}\n}`);
            }
            return hint;
        }
    }
    return 'JsonElement?';
}

// ---- CLR type maps -------------------------------------------------------------
const clr2ts = t => ({
    String: 'string', Int32: 'number', Int64: 'number', Decimal: 'number', Double: 'number',
    Single: 'number', Boolean: 'boolean', DateTime: 'string', DateTimeOffset: 'string',
    'DateTime?': 'string', 'Int32?': 'number', 'Int64?': 'number', 'Decimal?': 'number',
    'Boolean?': 'boolean', Char: 'string', Guid: 'string'
}[t] ?? 'unknown');
const clr2cs = t => ({
    String: 'string', Int32: 'int', Int64: 'long', Decimal: 'decimal', Double: 'double',
    Single: 'float', Boolean: 'bool', DateTime: 'DateTime', DateTimeOffset: 'DateTimeOffset',
    'DateTime?': 'DateTime?', 'Int32?': 'int?', 'Int64?': 'long?', 'Decimal?': 'decimal?',
    'Boolean?': 'bool?', Char: 'char', Guid: 'Guid', Byte: 'byte'
}[t] ?? 'object?');
// CLR return types that are not meaningful client payloads
const retClr = t => t && !/^(IActionResult|ActionResult|IResult|ObjectResult|IAsyncEnumerable|Task|void|Void)/.test(t) ? t : null;

// ---- endpoint iteration ---------------------------------------------------------
const eps = [];
for (const g of catalog.groups) {
    for (const e of g.endpoints) {
        if (e.method === 'WS' || e.method === 'CONNECT') continue;
        const pathParams = (e.path.match(/\{(\w+)\}/g) || []).map(s => s.slice(1, -1));
        const queryParams = (e.parameters || []).filter(p =>
            p.at && !/^(path|body)$/i.test(p.at) && p.name && !pathParams.includes(p.name));
        const bodyParam = (e.parameters || []).find(p => p.at && /^body$/i.test(p.at));
        eps.push({ e, name: fnName(e.method, e.path), retName: ifaceName(e.method, e.path), pathParams, queryParams, bodyParam });
    }
}
const doc = e => [e.summary, e.note].filter(Boolean).join(' — ');

// ---- TypeScript emit -------------------------------------------------------------
function emitTs() {
    const lines = [
        `// generated by pubsoft apigen from ${src} — do not edit`,
        `// catalog: ${catalog.title || ''} @ ${catalog.generatedAt}`,
        ``,
        `const BASE = (import.meta as any).env?.VITE_API_BASE ?? '';`,
        `let token = '';`,
        `export const setToken = (t: string) => { token = t; };`,
        ``,
        `async function req<T>(method: string, path: string, query?: Record<string, any>, body?: any): Promise<T> {`,
        `  const u = new URL(BASE + path, location.origin);`,
        `  for (const [k, v] of Object.entries(query ?? {})) if (v !== undefined && v !== null) u.searchParams.set(k, String(v));`,
        `  const res = await fetch(u.toString(), {`,
        `    method,`,
        `    headers: { ...(body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: 'Bearer ' + token } : {}) },`,
        `    body: body ? JSON.stringify(body) : undefined,`,
        `  });`,
        `  if (!res.ok) throw new Error(\`\${method} \${path}: \${res.status}\`);`,
        `  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T;`,
        `  return res.json();`,
        `}`,
        ``,
        `export const api = {`
    ];
    for (const { e, name, retName, pathParams, queryParams, bodyParam } of eps) {
        const ret = e.example ? tsType(e.example, retName) : (retClr(e.returns) ? clr2ts(e.returns) : 'unknown');
        const args = pathParams.map(p => `${p}: string`);
        if (queryParams.length)
            args.push(`query?: { ${queryParams.map(p => `${p.name}${p.required ? '' : '?'}: ${clr2ts(p.type)}`).join('; ')} }`);
        if (bodyParam) args.push(`body: unknown /* ${bodyParam.type} */`);
        const safePath = String(e.path).replace(/[`$]/g, c => '\\' + c);
        const pathExpr = '`' + safePath.replace(/\{(\w+)\}/g, (_, k) => `\${${k}}`) + '`';
        lines.push(
            `${doc(e) ? `  /** ${doc(e).replace(/\*\//g, '* /')} */\n` : ''}` +
            `  ${name}: (${args.join(', ')}) => req<${ret}>('${e.method}', ${pathExpr}, ${queryParams.length ? 'query' : 'undefined'}, ${bodyParam ? 'body' : 'undefined'}),`
        );
    }
    lines.push('};', '', [...tsIfaces.values()].join('\n\n'));
    return lines.join('\n') + '\n';
}

// ---- C# emit -------------------------------------------------------------
function emitCs() {
    const client = sdkName + 'Client', ctx = sdkName + 'JsonContext';
    const methods = [];
    for (const ep of eps) {
        const { e, name, retName, pathParams, queryParams, bodyParam } = ep;
        let ret;
        if (e.example) { csType(e.example, retName); ret = retName; }
        else { const clr = retClr(e.returns); ret = clr && clr2cs(clr) !== 'object?' ? clr2cs(clr) : 'JsonElement?'; }
        const pascalName = pascal(name);
        const args = pathParams.map(pp => {
            const meta = (e.parameters || []).find(p => p.name === pp);
            const t = clr2cs(meta?.type ?? 'String');
            return `${t} ${pp}`;
        });
        if (queryParams.length) args.push('IReadOnlyDictionary<string, object?>? query = null');
        if (bodyParam) args.push('object? body = null');

        // {param} → escaped interpolation
        const pathExpr = '$"' + String(e.path).replace(/"/g, '\\"')
            .replace(/\{(\w+)\}/g, (_, k) => `{Uri.EscapeDataString(Convert.ToString(${k}, System.Globalization.CultureInfo.InvariantCulture)!)}`) + '"';
        const urlExpr = queryParams.length ? `${pathExpr} + Q(query)` : pathExpr;

            const read = ret === 'JsonElement?'
            ? `return await JsonSerializer.DeserializeAsync<JsonElement>(await res.Content.ReadAsStreamAsync());`
            : `return await res.Content.ReadFromJsonAsync(${ctx}.Default.${ret});`;

        methods.push(
            `${doc(e) ? `    /// <summary>${doc(e).replace(/</g, '&lt;')}</summary>\n` : ''}` +
            `    public async Task<${ret}${ret.endsWith('?') ? '' : '?'}> ${pascalName}Async(${args.join(', ')})\n` +
            `    {\n` +
            `        using var req = new HttpRequestMessage(new HttpMethod("${e.method}"), ${urlExpr});\n` +
            (bodyParam ? `        if (body is not null) req.Content = JsonContent.Create(body);\n` : '') +
            `        using var res = await _http.SendAsync(req);\n` +
            `        res.EnsureSuccessStatusCode();\n` +
            `        ${read}\n` +
            `    }`
        );
    }
    const ctxTypes = [...csTypes.keys()];
    return `// generated by pubsoft apigen from ${src} — do not edit
// catalog: ${catalog.title || ''} @ ${catalog.generatedAt}
#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public sealed class ${client}
{
    private readonly HttpClient _http;
    public ${client}(HttpClient http) => _http = http;

    private static string Q(IReadOnlyDictionary<string, object?>? query)
    {
        if (query is null || query.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder("?");
        foreach (var (k, v) in query)
            if (v is not null)
                sb.Append(Uri.EscapeDataString(k)).Append('=')
                  .Append(Uri.EscapeDataString(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!)).Append('&');
        return sb.ToString().TrimEnd('&');
    }

${methods.join('\n\n')}
}

${ctxTypes.map(t => `[JsonSerializable(typeof(${t}))]`).join('\n')}
public sealed partial class ${ctx} : JsonSerializerContext { }

${[...csTypes.values()].join('\n\n')}
`;
}

const source = lang === 'cs' ? emitCs() : emitTs();
writeFileSync(out, source);
console.log(`wrote ${out}: ${eps.length} endpoints, ${lang === 'cs' ? csTypes.size : tsIfaces.size} types`);
