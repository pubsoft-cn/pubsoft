using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT038 — public/protected API surface must carry a /// doc comment. Doc
/// presence is what ships IntelliSense to consumers; missing docs mean empty
/// tooltips in the package. Exempt: overrides, explicit interface
/// implementations, compiler-generated members, pubsoft:exempt comments.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocPresenceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PUBSOFT038";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Public API surface must carry a doc comment",
        "'{0}' lacks a /// doc comment — consumers get an empty IntelliSense entry; add a summary or <inheritdoc/>",
        "Documentation",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            // Gated: packages opt in with <PubSoftDocDiscipline>full</PubSoftDocDiscipline> —
            // a 4k-site sweep cannot land in one pass, so activation doubles as the
            // per-package progress ledger. Enable per project as docs get written.
            var enabled = startContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue("build_property.PubSoftDocDiscipline", out var value)
                && string.Equals(value, "full", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
                return;
            startContext.RegisterSymbolAction(Check, SymbolKind.NamedType, SymbolKind.Method,
                SymbolKind.Property, SymbolKind.Field, SymbolKind.Event);
        });
    }

    private static void Check(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;
        if (symbol.IsImplicitlyDeclared)
            return;

        var accessibility = symbol.DeclaredAccessibility;
        if (accessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            return;

        // Only externally visible members: every containing type must be public/protected too.
        for (var container = symbol.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.DeclaredAccessibility is not (Accessibility.Public
                or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return;
        }

        if (symbol is IMethodSymbol method)
        {
            // Accessors are documented through their property/event; operators and
            // conversion members are self-describing; ctors/docs ride the type.
            if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise)
                return;
            if (method.MethodKind is MethodKind.ExplicitInterfaceImplementation or MethodKind.StaticConstructor)
                return;
        }
        if (symbol.IsOverride)
            return;

        if (!string.IsNullOrWhiteSpace(symbol.GetDocumentationCommentXml(cancellationToken: context.CancellationToken)))
            return;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax(context.CancellationToken);
            if (AnalyzerHelpers.IsExempt(node, DiagnosticId))
                continue;
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, node.GetLocation(), symbol.ToDisplayString()));
            return; // one diagnostic per symbol is enough
        }
    }
}
