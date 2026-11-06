using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PubSoft.Analyzers;

/// <summary>
/// PUBSOFT030-036 — file- and member-level layout discipline. Level-bounded
/// governance: one public type per file, ordered members, explicit modifiers.
/// Shapes mirror StyleCop SA12xx/SA14xx; opt-out via [BoundedCost("reason")]
/// or a pubsoft:exempt line comment.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutDisciplineAnalyzer : DiagnosticAnalyzer
{
    public const string UsingsOrderId = "PUBSOFT030";        // using placement/order
    public const string MemberOrderId = "PUBSOFT031";        // fields→ctors→props→methods
    public const string AccessOrderId = "PUBSOFT032";        // public→internal→protected→private within kind
    public const string ExplicitAccessId = "PUBSOFT033";     // missing explicit access modifier
    public const string ConstStaticId = "PUBSOFT034";        // const/static before instance
    public const string FilePerTypeId = "PUBSOFT035";        // file name must match its public type
    public const string MinimizeId = "PUBSOFT036";           // member could be internal/private

    private static DiagnosticDescriptor Make(string id, string title, string message)
        => new DiagnosticDescriptor(id, title, message, "Layout", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor[] Rules = {
        Make(UsingsOrderId, "Order using directives — System first, then alphabetical", "Using '{0}' out of order"),
        Make(MemberOrderId, "Order members by kind — fields, ctors, properties, methods", "'{0}' should come before earlier '{1}' members"),
        Make(AccessOrderId, "Order members by access — public first, private last", "'{0}' ({1}) should come before {2} members"),
        Make(ExplicitAccessId, "Declare the access modifier explicitly", "'{0}' has an implicit access level — declare it"),
        Make(ConstStaticId, "Constants and statics before instance members", "'{0}' should precede instance members"),
        Make(FilePerTypeId, "One public type per file, name must match", "Public type '{0}' lives in file '{1}'"),
        Make(MinimizeId, "Minimize member visibility", "Public member '{0}' — internal unless it is a consumer-facing API"),
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(CheckTree);
    }

    private static int KindRank(MemberDeclarationSyntax m) => m switch
    {
        FieldDeclarationSyntax => 0,
        ConstructorDeclarationSyntax => 1,
        PropertyDeclarationSyntax => 2,
        IndexerDeclarationSyntax => 2,
        MethodDeclarationSyntax => 3,
        EventFieldDeclarationSyntax or EventDeclarationSyntax => 2,
        OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => 3,
        DelegateDeclarationSyntax => 4,
        EnumDeclarationSyntax => 4,
        InterfaceDeclarationSyntax => 4,
        StructDeclarationSyntax => 4,
        ClassDeclarationSyntax => 4,
        RecordDeclarationSyntax => 4,
        _ => 5,
    };

    private static int AccessRank(MemberDeclarationSyntax m)
    {
        if (m.Modifiers.Any(SyntaxKind.PublicKeyword)) return 0;
        if (m.Modifiers.Any(SyntaxKind.InternalKeyword) && m.Modifiers.Any(SyntaxKind.ProtectedKeyword)) return 2;
        if (m.Modifiers.Any(SyntaxKind.InternalKeyword)) return 1;
        if (m.Modifiers.Any(SyntaxKind.ProtectedKeyword) && m.Modifiers.Any(SyntaxKind.PrivateKeyword)) return 4;
        if (m.Modifiers.Any(SyntaxKind.ProtectedKeyword)) return 3;
        if (m.Modifiers.Any(SyntaxKind.PrivateKeyword)) return 5;
        if (m is ConstructorDeclarationSyntax) return 5;        // implicit private-ish? ctor w/o modifier is private? no — ctor default is private
        return m is InterfaceDeclarationSyntax or DelegateDeclarationSyntax or EnumDeclarationSyntax ? 0 : 5; // implicit
    }

    private static string NameOf(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax x => x.Identifier.Text,
        PropertyDeclarationSyntax x => x.Identifier.Text,
        FieldDeclarationSyntax x => x.Declaration.Variables[0].Identifier.Text,
        ConstructorDeclarationSyntax x => x.Identifier.Text,
        TypeDeclarationSyntax x => x.Identifier.Text,
        DelegateDeclarationSyntax x => x.Identifier.Text,
        EnumDeclarationSyntax x => x.Identifier.Text,
        _ => m.ToString().Substring(0, System.Math.Min(30, m.ToString().Length)),
    };

    private static void CheckTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot();
        var file = System.IO.Path.GetFileName(context.Tree.FilePath);
        if (file.EndsWith(".g.cs") || file.EndsWith("AssemblyInfo.cs")) return;

        // PUBSOFT030 — using order: System.* first, then alphabetical; usings must be inside file root or single namespace
        UsingDirectiveSyntax? prev = null;
        foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = u.Name?.ToString() ?? "";
            if (prev is not null && !AnalyzerHelpers.IsExempt(u, UsingsOrderId))
            {
                var pName = prev.Name?.ToString() ?? "";
                var pSys = pName.StartsWith("System", System.StringComparison.Ordinal);
                var nSys = name.StartsWith("System", System.StringComparison.Ordinal);
                if ((!pSys && nSys) || (pSys == nSys && string.Compare(pName, name, System.StringComparison.Ordinal) > 0))
                    context.ReportDiagnostic(Diagnostic.Create(Rules[0], u.GetLocation(), name));
            }
            prev = u;
        }

        // PUBSOFT035 — file name vs the ONE public top-level type
        var publicTypes = root.DescendantNodes()
            .Where(n => n is ClassDeclarationSyntax or RecordDeclarationSyntax or InterfaceDeclarationSyntax or StructDeclarationSyntax)
            .Cast<TypeDeclarationSyntax>()
            .Where(t => t.Modifiers.Any(SyntaxKind.PublicKeyword)
                && t.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .ToList();
        if (publicTypes.Count > 0)
        {
            var first = publicTypes[0];
            var stem = System.IO.Path.GetFileNameWithoutExtension(file);
            if (!string.Equals(first.Identifier.Text, stem, System.StringComparison.Ordinal)
                && !AnalyzerHelpers.IsExempt(first, FilePerTypeId))
                context.ReportDiagnostic(Diagnostic.Create(Rules[5], first.GetLocation(), first.Identifier.Text, file));
        }

        // member-level ordering inside every type
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            MemberDeclarationSyntax? pm = null;
            foreach (var m in type.Members)
            {
                if (pm is not null)
                {
                    var pk = KindRank(pm); var ck = KindRank(m);
                    if (ck < pk && !AnalyzerHelpers.IsExempt(m, MemberOrderId))
                        context.ReportDiagnostic(Diagnostic.Create(Rules[1], m.GetLocation(), NameOf(m), NameOf(pm)));
                    else if (ck == pk && pk <= 3)
                    {
                        var pa = AccessRank(pm); var ca = AccessRank(m);
                        if (ca < pa && !AnalyzerHelpers.IsExempt(m, AccessOrderId))
                            context.ReportDiagnostic(Diagnostic.Create(Rules[2], m.GetLocation(),
                                NameOf(m), "public", NameOf(pm)));
                        // const/static before instance within same rank
                        var pStatic = pm.Modifiers.Any(SyntaxKind.StaticKeyword) || pm is FieldDeclarationSyntax f && f.Modifiers.Any(SyntaxKind.ConstKeyword);
                        var cStatic = m.Modifiers.Any(SyntaxKind.StaticKeyword) || m is FieldDeclarationSyntax f2 && f2.Modifiers.Any(SyntaxKind.ConstKeyword);
                        if (!pStatic && cStatic && ck == 0 && pa == ca && !AnalyzerHelpers.IsExempt(m, ConstStaticId))
                            context.ReportDiagnostic(Diagnostic.Create(Rules[4], m.GetLocation(), NameOf(m)));
                    }
                }
                // PUBSOFT033 — explicit access modifier on members that CAN take one
                // skip: interface members (implicit public), enum members, static ctors,
                // destructors, operators/conversions, explicit interface impls (modifier-forbidden)
                var canTakeModifier = m is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null }
                    or PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: null }
                    or EventDeclarationSyntax { ExplicitInterfaceSpecifier: null }
                    or FieldDeclarationSyntax or EventFieldDeclarationSyntax or ConstructorDeclarationSyntax
                    or TypeDeclarationSyntax or DelegateDeclarationSyntax;
                var forbidden = m is DestructorDeclarationSyntax or EnumMemberDeclarationSyntax
                    or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax;
                forbidden |= m is ConstructorDeclarationSyntax ctor && ctor.Modifiers.Any(SyntaxKind.StaticKeyword);
                if (canTakeModifier && !forbidden
                    && type is not InterfaceDeclarationSyntax
                    && m.Modifiers.All(t => t.Kind() is not SyntaxKind.PublicKeyword and not SyntaxKind.InternalKeyword
                        and not SyntaxKind.ProtectedKeyword and not SyntaxKind.PrivateKeyword)
                    && !AnalyzerHelpers.IsExempt(m, ExplicitAccessId))
                    context.ReportDiagnostic(Diagnostic.Create(Rules[3], m.GetLocation(), NameOf(m)));
            next:;
                pm = m;
            }
        }
    }
}
