using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Modernization.Analyzers.Tests;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class ModernizationAnalyzer : DiagnosticAnalyzer
{
    private readonly bool architectureEnabled;
    private readonly Func<INamedTypeSymbol, bool> architectureScope;

    public ModernizationAnalyzer() : this(architectureEnabled: true) { }

    internal ModernizationAnalyzer(bool architectureEnabled) : this(_ => architectureEnabled) { }

    internal ModernizationAnalyzer(Func<INamedTypeSymbol, bool> architectureScope)
    {
        architectureEnabled = true;
        this.architectureScope = architectureScope;
    }

    internal static readonly DiagnosticDescriptor Coupling = Rule("MOD001", "Concrete presentation dependency", "{0} depends on concrete WPF/view symbol {1}");
    internal static readonly DiagnosticDescriptor Notification = Rule("MOD002", "Nonobservable pseudo-view-model", "{0} is concrete-view-coupled but does not implement INotifyPropertyChanged");
    internal static readonly DiagnosticDescriptor Friend = Rule("MOD003", "Frontend internals exposed", "Frontend assembly grants internals access to non-test friend {0}");
    internal static readonly DiagnosticDescriptor Reverse = Rule("MOD004", "Reverse presentation dependency", "Semantic dependency {0} ({1}) -> {2} ({3}) points from presentation logic into a concrete view");
    internal static readonly DiagnosticDescriptor Styling = Rule("MOD005", "Hard-coded WPF styling", "Code fixes a WPF color/font through {0}; use an external resource");
    internal static readonly DiagnosticDescriptor Language = Rule("MOD006", "VB master-data presentation", "Master-data presentation type {0} is implemented in Visual Basic");
    internal static readonly DiagnosticDescriptor Booking = Rule("BUS001", "Default project used for booking", "Booking project identity flows from positional/default project selection, not the selected/task project");
    internal static readonly DiagnosticDescriptor Duration = Rule("BUS002", "Elapsed duration loses hours", "TimeSpan.Minutes flows into {0}; an elapsed duration requires TotalMinutes");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Coupling, Notification, Friend, Reverse, Styling, Language, Booking, Duration];

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, id.StartsWith("BUS", StringComparison.Ordinal) ? "BusinessCorrectness" : "Modernization",
            DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var scope = new PresentationScope(start.Compilation, architectureScope);
            if (architectureEnabled)
                start.RegisterSymbolAction(c => AnalyzeType(c, scope), SymbolKind.NamedType);
            start.RegisterOperationAction(c => AnalyzeOperation(c, scope, architectureEnabled),
                OperationKind.PropertyReference, OperationKind.FieldReference, OperationKind.Invocation,
                OperationKind.EventReference, OperationKind.ObjectCreation, OperationKind.SimpleAssignment);
            start.RegisterCompilationEndAction(c =>
            {
                if (!architectureEnabled || !scope.Types.Any(scope.IsPresentation)) return;
                foreach (var attribute in c.Compilation.Assembly.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() != "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                        continue;
                    var name = (attribute.ConstructorArguments.FirstOrDefault().Value as string)?.Split(',')[0].Trim();
                    if (name is null || name.Split('.').Any(p => p is "Tests" or "Test" || p.EndsWith("Tests", StringComparison.Ordinal)))
                        continue;
                    c.ReportDiagnostic(Diagnostic.Create(Friend, attribute.ApplicationSyntaxReference?.GetSyntax(c.CancellationToken).GetLocation(), name));
                }
            });
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, PresentationScope scope)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!scope.IsPresentation(type)) return;
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        if (type.Language == LanguageNames.VisualBasic)
            context.ReportDiagnostic(Diagnostic.Create(Language, location, type.ToDisplayString()));
        if (PresentationScope.IsWpf(type)) return;
        if (!type.AllInterfaces.Any(i => i.ToDisplayString() == "System.ComponentModel.INotifyPropertyChanged"))
            context.ReportDiagnostic(Diagnostic.Create(Notification, location, type.ToDisplayString()));
        foreach (var member in type.GetMembers())
        foreach (var dependency in PresentationScope.MemberTypes(member).Where(PresentationScope.IsWpf))
        {
            context.ReportDiagnostic(Diagnostic.Create(Coupling, member.Locations.FirstOrDefault(l => l.IsInSource) ?? location,
                member.ToDisplayString(), dependency.ToDisplayString()));
        }
        foreach (var view in scope.ViewDependencies(type).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default))
            context.ReportDiagnostic(Diagnostic.Create(Reverse, location, type.ToDisplayString(), type.ContainingAssembly.Name,
                view.ToDisplayString(), view.ContainingAssembly.Name));
    }

    private static void AnalyzeOperation(OperationAnalysisContext context, PresentationScope scope, bool architectureEnabled)
    {
        var operation = context.Operation;
        if (operation is ISimpleAssignmentOperation assignment &&
            assignment.Target is IPropertyReferenceOperation target &&
            target.Property.Name == "IdProject" && IsBooking(target.Property.ContainingType))
        {
            if (new Provenance(context.Compilation).Evaluate(assignment.Value) == Origin.DefaultProjectId)
                context.ReportDiagnostic(Diagnostic.Create(Booking, assignment.Syntax.GetLocation()));
        }
        if (operation is IInvocationOperation call && call.TargetMethod.Name == "AddMinutes" &&
            (call.TargetMethod.ContainingType.SpecialType == SpecialType.System_DateTime ||
             call.TargetMethod.ContainingType.ToDisplayString() == "System.DateTimeOffset") &&
            call.Arguments.Length == 1 &&
            new Provenance(context.Compilation).Evaluate(call.Arguments[0].Value) == Origin.MinuteComponent &&
            !new Provenance(context.Compilation).ComposesClock(call.Instance))
            context.ReportDiagnostic(Diagnostic.Create(Duration, call.Syntax.GetLocation(), call.TargetMethod.ToDisplayString()));

        if (!architectureEnabled) return;
        var owner = context.ContainingSymbol.ContainingType;
        if (owner is null || !scope.IsPresentation(owner)) return;
        var symbol = operation switch
        {
            IPropertyReferenceOperation p => (ISymbol)p.Property,
            IFieldReferenceOperation f => f.Field,
            IInvocationOperation i => i.TargetMethod,
            IEventReferenceOperation e => e.Event,
            IObjectCreationOperation o => o.Constructor,
            _ => null
        };
        if (symbol is null) return;
        if (!PresentationScope.IsWpf(owner) && PresentationScope.IsWpf(symbol.ContainingType))
            context.ReportDiagnostic(Diagnostic.Create(Coupling, operation.Syntax.GetLocation(),
                context.ContainingSymbol.ToDisplayString(), symbol.ToDisplayString()));
        var container = symbol.ContainingType?.ToDisplayString();
        var fixedStyle = container is "System.Windows.Media.Brushes" or "System.Windows.Media.Colors" or "System.Windows.SystemFonts";
        fixedStyle |= operation is IObjectCreationOperation creation &&
            container == "System.Windows.Media.FontFamily" && creation.Arguments.Any(a => a.Value.ConstantValue.HasValue);
        fixedStyle |= operation is IInvocationOperation invocation &&
            container == "System.Windows.Media.Color" && invocation.TargetMethod.Name is "FromArgb" or "FromRgb" &&
            invocation.Arguments.All(a => a.Value.ConstantValue.HasValue);
        if (fixedStyle) context.ReportDiagnostic(Diagnostic.Create(Styling, operation.Syntax.GetLocation(), symbol.ToDisplayString()));
    }

    internal static bool IsBooking(ITypeSymbol? type) => type?.ToDisplayString() is
        "TaskOTime.AppServer.Models.TimeBookingItemDto" or "TaskOTime.DTOs.TimeItem";
}

internal sealed class PresentationScope(Compilation compilation, Func<INamedTypeSymbol, bool>? selection = null)
{
    private readonly Lazy<Dictionary<INamedTypeSymbol, HashSet<ITypeSymbol>>> operationViews =
        new(() => CollectOperationViews(compilation));

    private static Dictionary<INamedTypeSymbol, HashSet<ITypeSymbol>> CollectOperationViews(Compilation compilation)
    {
        var result = new Dictionary<INamedTypeSymbol, HashSet<ITypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var operation = model.GetOperation(node);
                if (operation is null) continue;
                var dependency = operation switch
                {
                    IMemberReferenceOperation member when IsView(member.Member.ContainingType) => member.Member.ContainingType,
                    IInvocationOperation call when IsView(call.TargetMethod.ContainingType) => call.TargetMethod.ContainingType,
                    _ => operation.Type
                };
                if (!IsView(dependency)) continue;
                var owner = model.GetEnclosingSymbol(node.SpanStart)?.ContainingType;
                if (owner is null) continue;
                if (!result.TryGetValue(owner, out var values))
                    result.Add(owner, values = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
                values.Add(dependency!);
            }
        }
        return result;
    }
    internal IEnumerable<INamedTypeSymbol> Types => AllTypes(compilation.Assembly.GlobalNamespace);
    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol parent)
    {
        foreach (var member in parent.GetMembers())
        {
            if (member is INamedTypeSymbol type) yield return type;
            if (member is INamespaceOrTypeSymbol nested)
                foreach (var descendant in AllTypes(nested)) yield return descendant;
        }
    }

    internal static bool IsWpf(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array) return IsWpf(array.ElementType);
        if (type is not INamedTypeSymbol named) return false;
        return named.ContainingAssembly?.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" ||
            Bases(named).Any(t => t.ToDisplayString() == "System.Windows.DependencyObject");
    }
    private static IEnumerable<INamedTypeSymbol> Bases(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType) yield return current;
    }
    internal static bool IsView(ITypeSymbol? type) => type is INamedTypeSymbol named &&
        Bases(named).Any(t => t.ToDisplayString() is "System.Windows.Controls.UserControl" or "System.Windows.Window");
    internal static IEnumerable<ITypeSymbol> MemberTypes(ISymbol member) => member switch
    {
        IFieldSymbol field => [field.Type],
        IPropertySymbol property => new[] { property.Type }.Concat(property.Parameters.Select(p => p.Type)),
        IMethodSymbol method => new[] { method.ReturnType }.Concat(method.Parameters.Select(p => p.Type)),
        IEventSymbol @event => [@event.Type],
        _ => []
    };
    internal IEnumerable<ITypeSymbol> ViewDependencies(INamedTypeSymbol type) =>
        Bases(type).SelectMany(t => t.GetMembers()).SelectMany(MemberTypes).Where(IsView)
            .Concat(Bases(type).SelectMany(t => operationViews.Value.TryGetValue(t, out var values) ? values : []));
    internal bool IsPresentation(INamedTypeSymbol type) =>
        (selection is null || selection(type)) &&
        (IsView(type) || (!IsWpf(type) && ViewDependencies(type).Any()));
}
