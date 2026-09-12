using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace TaskOTime.Migration;

internal static class WpfEdits
{
    internal static async Task ApplyAsync(Project project, List<string> repairs)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var directory = Path.GetDirectoryName(project.FilePath)!;
        var rootNamespace = ((VisualBasicCompilationOptions)project.CompilationOptions!).RootNamespace;
        var xamlFiles = Migration.Files(directory).Where(p => p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Path: Path.Combine(directory, p), Xml: XDocument.Load(Path.Combine(directory, p), LoadOptions.PreserveWhitespace))).ToArray();
        foreach (var document in project.Documents.Where(d => d.FilePath is not null && !Migration.IsGenerated(d.FilePath))) {
            var syntax = await document.GetSyntaxRootAsync();
            var model = (await document.GetSemanticModelAsync())!;
            foreach (var assignment in syntax!.DescendantNodes().OfType<AssignmentStatementSyntax>()) {
                var target = model.GetSymbolInfo(assignment.Left).Symbol;
                if (IsGeneratedWithEvents(target))
                    throw new MigrationException("WPF_REASSIGNMENT", $"{document.FilePath}: reassigned generated WithEvents member {target!.Name} needs a reviewed lifetime adapter.");
            }
            foreach (var method in syntax.DescendantNodes().OfType<MethodStatementSyntax>().Where(m => m.HandlesClause is not null)) {
                var type = model.GetDeclaredSymbol(method)!.ContainingType.ToDisplayString();
                foreach (var item in method.HandlesClause!.Events) {
                    if (item.EventContainer is not WithEventsEventContainerSyntax container) continue;
                    var property = model.GetDeclaredSymbol(method)!.ContainingType.GetMembers(container.ToString()).SingleOrDefault();
                    if (!IsGeneratedWithEvents(property)) continue;
                    var matches = xamlFiles.Where(f => Qualified(rootNamespace, f.Xml.Root?.Attribute(x + "Class")?.Value) == type).ToArray();
                    if (matches.Length != 1) throw new MigrationException("WPF_CLASS", $"Cannot uniquely match XAML to {type}.");
                    var xml = matches[0].Xml;
                    var fields = xml.Descendants().Where(e => (e.Attribute(x + "Name") ?? e.Attribute("Name"))?.Value == property!.Name).ToArray();
                    if (fields.Length != 1) throw new MigrationException("WPF_FIELD", $"Cannot uniquely match XAML name {property!.Name}.");
                    var eventName = item.EventMember.ToString();
                    if (fields[0].Attribute(eventName) is not null)
                        throw new MigrationException("WPF_MULTIPLE_HANDLERS", $"XAML event {property!.Name}.{eventName} already has a handler; review combined event lifetime.");
                    fields[0].SetAttributeValue(eventName, method.Identifier.ValueText);
                    xml.Save(matches[0].Path, SaveOptions.DisableFormatting);
                    repairs.Add("generated-wpf-handles-to-xaml-event");
                }
            }
        }
    }

    private static bool IsGeneratedWithEvents(ISymbol? symbol) => symbol is IPropertySymbol &&
        symbol.DeclaringSyntaxReferences.Length > 0 &&
        symbol.DeclaringSyntaxReferences.All(r => Migration.IsGenerated(r.SyntaxTree.FilePath));
    private static string? Qualified(string root, string? type) => type is null ? null : root.Length == 0 ? type : root + "." + type;
}
