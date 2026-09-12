using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB = Microsoft.CodeAnalysis.VisualBasic.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TaskOTime.Migration;

internal static class EngineRepairs
{
    internal static async Task<string> ApplyAsync(string code, Document source, Compilation input, List<string> appliedRules)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();
        var compilation = CSharpCompilation.Create("ConversionRepair", [tree], input.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var replacements = new Dictionary<ClassDeclarationSyntax, ClassDeclarationSyntax>();
        var sourceRoot = await source.GetSyntaxRootAsync();
        var sourceModel = await source.GetSemanticModelAsync();
        var implicitDesignerTypes = sourceRoot!.DescendantNodes().OfType<VB.ClassBlockSyntax>()
            .Select(c => Microsoft.CodeAnalysis.VisualBasic.VisualBasicExtensions.GetDeclaredSymbol(sourceModel!, c.ClassStatement))
            .OfType<INamedTypeSymbol>()
            .Where(t => t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Microsoft.VisualBasic.CompilerServices.DesignerGeneratedAttribute") &&
                        t.InstanceConstructors.All(c => c.IsImplicitlyDeclared) &&
                        t.GetMembers("InitializeComponent").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 0))
            .Select(t => t.ToDisplayString()).ToHashSet(StringComparer.Ordinal);
        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
            var updated = type;
            var methods = type.Members.OfType<MethodDeclarationSyntax>().Where(m => m.ExplicitInterfaceSpecifier is not null).ToArray();
            foreach (var getter in methods.Where(m => m.Identifier.ValueText.StartsWith("get_", StringComparison.Ordinal))) {
                var iface = model.GetSymbolInfo(getter.ExplicitInterfaceSpecifier!.Name).Symbol as INamedTypeSymbol;
                var propertyName = getter.Identifier.ValueText[4..];
                var property = iface?.GetMembers().OfType<IPropertySymbol>()
                    .SingleOrDefault(p => p.MetadataName == propertyName && p.IsIndexer && p.Parameters.Length == getter.ParameterList.Parameters.Count);
                if (property is null) continue;
                var setter = methods.SingleOrDefault(m => m.Identifier.ValueText == "set_" + propertyName &&
                    m.ExplicitInterfaceSpecifier!.Name.ToString() == getter.ExplicitInterfaceSpecifier.Name.ToString() &&
                    m.ParameterList.Parameters.Count == getter.ParameterList.Parameters.Count + 1);
                var accessors = new List<AccessorDeclarationSyntax> {
                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(getter.Body)
                        .WithExpressionBody(getter.ExpressionBody).WithSemicolonToken(getter.SemicolonToken)
                };
                if (setter is not null) {
                    if (setter.ParameterList.Parameters.Last().Identifier.ValueText != "value")
                        throw new MigrationException("INDEXER_SETTER", "Engine setter value parameter requires a reviewed rename.");
                    accessors.Add(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithBody(setter.Body)
                        .WithExpressionBody(setter.ExpressionBody).WithSemicolonToken(setter.SemicolonToken));
                }
                if (property.SetMethod is not null && setter is null)
                    throw new MigrationException("INDEXER_SETTER", $"Missing setter for {iface}.{propertyName}");
                var indexer = IndexerDeclaration(getter.ReturnType)
                    .WithExplicitInterfaceSpecifier(getter.ExplicitInterfaceSpecifier)
                    .WithParameterList(BracketedParameterList(getter.ParameterList.Parameters))
                    .WithAccessorList(AccessorList(List(accessors))).WithTriviaFrom(getter).NormalizeWhitespace();
                updated = updated.WithMembers(List(updated.Members.SelectMany<MemberDeclarationSyntax, MemberDeclarationSyntax>(m =>
                    m == getter ? [indexer] : m == setter ? [] : [m])));
                appliedRules.Add("explicit-interface-indexer-accessors");
            }
            var symbol = model.GetDeclaredSymbol(type);
            if (symbol is not null && implicitDesignerTypes.Contains(symbol.ToDisplayString())) {
                var constructor = updated.Members.OfType<ConstructorDeclarationSyntax>().SingleOrDefault(c => c.ParameterList.Parameters.Count == 0);
                var initialize = ExpressionStatement(InvocationExpression(IdentifierName("InitializeComponent")));
                if (constructor is null) {
                    updated = updated.AddMembers(ConstructorDeclaration(type.Identifier).AddModifiers(Token(SyntaxKind.PublicKeyword)).WithBody(Block(initialize)));
                } else if (!constructor.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression.ToString().EndsWith("InitializeComponent", StringComparison.Ordinal))) {
                    updated = updated.ReplaceNode(constructor, constructor.WithBody(constructor.Body!.AddStatements(initialize)));
                }
                appliedRules.Add("vb-implicit-designer-initializecomponent");
            }
            if (updated != type) replacements.Add(type, updated);
        }
        return replacements.Count == 0 ? code : root.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]).NormalizeWhitespace().ToFullString();
    }
}
