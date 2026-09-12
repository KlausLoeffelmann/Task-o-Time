using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using CS = Microsoft.CodeAnalysis.CSharp;
using VB = Microsoft.CodeAnalysis.VisualBasic;

namespace Modernization.Analyzers.Tests;

[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class OutcomeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor English = Rule("ENG001", "Source-language prose", "Dutch/German prose evidence: {0}");
    internal static readonly DiagnosticDescriptor Documentation = Rule("ENG002", "Essential API documentation", "{0}: meaningful XML documentation covers {1}/{2} essential declarations (requires type prose and at least half of members)");
    internal static readonly DiagnosticDescriptor Localization = Rule("LOC001", "Classic localization evidence incomplete", "{0}");
    internal static readonly DiagnosticDescriptor Literal = Rule("LOC002", "Unlocalized presentation text", "Presentation text is hard-coded: {0}");
    internal static readonly DiagnosticDescriptor ProductionVB = Rule("LNG001", "Production Visual Basic remains", "Production type {0} has authored Visual Basic source");
    internal static readonly DiagnosticDescriptor Scope = Rule("SCP001", "Required source contract absent", "{0}");
    internal static readonly DiagnosticDescriptor Theme = Rule("THM001", "Static theme conflict", "{0}");
    internal static readonly DiagnosticDescriptor ThemeCoverage = Rule("THM002", "Theme coverage unverified", "{0}");
    internal static readonly DiagnosticDescriptor Naming = Rule("NAM001", "Obsolete presentation terminology", "Presentation/application terminology still uses Master Data: {0}");
    internal static readonly DiagnosticDescriptor Tool = new("TOOL001", "Static migration tool shape evidence incomplete", "{0}",
        "StaticToolEvidence", DiagnosticSeverity.Info, true);
    internal static readonly DiagnosticDescriptor SdkProject = Rule("PRJ001", "SDK-style project migration incomplete", "{0}");
    internal static readonly DiagnosticDescriptor Net10 = Rule("PRJ002", ".NET 10 migration incomplete", "{0}");
    internal static readonly DiagnosticDescriptor EF = Rule("EF001", "EF6 compatibility boundary", "{0}");
    internal static readonly DiagnosticDescriptor Core = Rule("COR001", "Protected architecture regression", "{0}");
    internal static readonly DiagnosticDescriptor Inputs = new("ASM001", "Invalid assessment input", "{0}",
        "AssessmentInput", DiagnosticSeverity.Error, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [English, Documentation, Localization, Literal, ProductionVB, Scope, Theme, ThemeCoverage, Naming, Tool,
            SdkProject, Net10, EF, Core, Inputs];

    private readonly IReadOnlyList<AssessmentProject>? corpus;
    private readonly Func<INamedTypeSymbol, bool> selection;
    private readonly Func<string, bool> sourceSelection;
    private readonly bool contracts;
    private readonly IReadOnlyList<AssessmentProject> toolFixtures;
    internal SortedDictionary<string, CriterionMetric> Metrics { get; } = new(StringComparer.Ordinal);

    public OutcomeAnalyzer() : this(null, _ => false, _ => false, true) { }
    internal OutcomeAnalyzer(IReadOnlyList<AssessmentProject>? corpus, Func<INamedTypeSymbol, bool>? selection = null,
        Func<string, bool>? sourceSelection = null, bool contracts = false,
        IReadOnlyList<AssessmentProject>? toolFixtures = null)
    {
        this.corpus = corpus;
        this.selection = selection ?? (_ => false);
        this.sourceSelection = sourceSelection ?? (_ => false);
        this.contracts = contracts;
        this.toolFixtures = toolFixtures ?? corpus?.Where(p => p.Test).ToArray() ?? [];
    }
    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "Modernization", DiagnosticSeverity.Warning, true);

    public override void Initialize(AnalysisContext context)
    {
        // Generated accessor and EF context symbols are required evidence. Individual rules
        // explicitly exclude generated trees when assessing authored language/prose.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(c =>
        {
            var projects = corpus ?? [new AssessmentProject("", c.Compilation)];
            var recorder = new Recorder(c.ReportDiagnostic, Metrics);
            foreach (var name in new[] { "English", "Documentation", "Localization", "Language", "Scope",
                "Theme", "Naming", "MigrationTool", "SdkStyle", "Net10", "EF6", "ProtectedCore", "Input" }) recorder.Measure(name, 0, 0);
            var production = projects.Where(p => !p.Test && !p.Tooling).ToArray();
            if (production.Length == 0 || production.All(p => !Evidence.Members(p.Compilation).Any(s => Evidence.Source(s, p))))
                recorder.Report("Input", Inputs, Location.None, "The production source corpus is empty.");
            foreach (var p in projects)
            {
                var errors = p.Compilation.GetDiagnostics(c.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                recorder.Measure("Input", p.Compilation.SyntaxTrees.Count(), errors.Length == 0 ? p.Compilation.SyntaxTrees.Count() : 0);
                foreach (var error in errors)
                    recorder.Report("Input", Inputs, error.Location, error.Id + ": " + error.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (recorder.HasInputErrors) return;
            var files = c.Options.AdditionalFiles;
            var xml = new List<(AdditionalText File, XDocument Document)>();
            foreach (var file in files.Where(f => Path.GetExtension(f.Path).ToLowerInvariant() is ".xaml" or ".resx" or ".assessment"))
            {
                try { xml.Add((file, Evidence.Xml(file))); }
                catch (System.Xml.XmlException e) { recorder.Report("Input", Inputs, Evidence.At(file), "Invalid structural XML input: " + e.Message); }
            }
            if (recorder.HasInputErrors) return;
            foreach (var p in production)
            {
                var types = Evidence.Types(p.Compilation).Where(t => Evidence.Source(t, p)).ToArray();
                foreach (var type in types)
                {
                    recorder.Measure("Language", 1, type.Language == LanguageNames.CSharp ? 1 : 0);
                    if (type.Language == LanguageNames.VisualBasic)
                        recorder.Report("Language", ProductionVB, Evidence.At(type), type.ToDisplayString());
                }
                AnalyzeNames(p, recorder);
            }
            AnalyzeContracts(production, xml, recorder);
            if (contracts) AnalyzeProjects(production, xml, recorder);
            new LocalizationAnalysis(production, xml, recorder).Run();
            new ThemeAnalysis(production, xml.Where(x => Path.GetExtension(x.File.Path) == ".xaml").ToArray(),
                selection, sourceSelection, recorder, contracts).Run();
            MigrationToolAnalysis.Run(projects.Where(p => p.Tooling && !p.Test).ToArray(), files, recorder, toolFixtures);
            AnalyzeEF(production, xml, recorder);
        });
    }

    private static void AnalyzeProjects(AssessmentProject[] projects,
        List<(AdditionalText File, XDocument Document)> xml, Recorder r)
    {
        var metadata = xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment" &&
            x.Document.Root?.Name.LocalName == "Project").ToArray();
        foreach (var project in projects)
        {
            var entry = metadata.FirstOrDefault(x =>
                string.Equals(x.Document.Root?.Attribute("Path")?.Value, project.Path, StringComparison.OrdinalIgnoreCase));
            var sdk = entry.Document?.Root?.Attribute("SdkStyle")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            var framework = entry.Document?.Root?.Attribute("TargetFramework")?.Value ?? "";
            var net10 = framework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ||
                framework.StartsWith("net10.0-", StringComparison.OrdinalIgnoreCase);
            var frameworkDisplay = framework.Length > 0 ? framework :
                (entry.Document?.Root?.Attribute("TargetFrameworkIdentifier")?.Value ?? "") + " " +
                (entry.Document?.Root?.Attribute("TargetFrameworkVersion")?.Value ?? "");
            r.Measure("SdkStyle", 1, sdk ? 1 : 0);
            r.Measure("Net10", 1, net10 ? 1 : 0);
            if (!sdk)
                r.Report("SdkStyle", SdkProject, entry.File == null ? Location.None : Evidence.At(entry.File),
                    Path.GetFileName(project.Path) + " is not evaluated as an SDK-style project.");
            if (!net10)
                r.Report("Net10", Net10, entry.File == null ? Location.None : Evidence.At(entry.File),
                    Path.GetFileName(project.Path) + " targets '" + frameworkDisplay.Trim() + "' instead of .NET 10.");
        }
    }

    private void AnalyzeNames(AssessmentProject p, Recorder r)
    {
        foreach (var symbol in Evidence.Members(p.Compilation).Where(s => Evidence.Source(s, p)))
        {
            // EF store/DTO symbols are a compatibility contract, not presentation names.
            var owner = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            if (owner == null || Boundary(owner)) continue;
            r.Measure("Naming", 1, Evidence.MasterData(symbol.Name) ? 0 : 1);
            if (Evidence.MasterData(symbol.Name))
                r.Report("Naming", Naming, Evidence.At(symbol), symbol.ToDisplayString());
            if (symbol is INamedTypeSymbol && Evidence.MasterData(owner.ContainingNamespace.ToDisplayString()))
                r.Report("Naming", Naming, Evidence.At(symbol), owner.ContainingNamespace.ToDisplayString());
        }
        foreach (var op in Evidence.Operations(p.Compilation, p: p))
        {
            var model = Evidence.Model(p.Compilation, op.Syntax.SyntaxTree);
            var owner = model.GetEnclosingSymbol(op.Syntax.SpanStart)?.ContainingType;
            if (owner == null || Boundary(owner)) continue;
            var symbol = op switch { IMemberReferenceOperation m => m.Member, IInvocationOperation i => i.TargetMethod,
                IObjectCreationOperation o => (ISymbol?)o.Constructor?.ContainingType,
                IVariableDeclaratorOperation v => v.Symbol, ILocalReferenceOperation l => l.Local,
                IParameterReferenceOperation a => a.Parameter, _ => null };
            var target = symbol as INamedTypeSymbol ?? symbol?.ContainingType;
            if (symbol != null && target != null && !Boundary(target) && Evidence.MasterData(symbol.Name))
                r.Report("Naming", Naming, op.Syntax.GetLocation(), symbol.ToDisplayString());
        }
    }
    internal static bool Boundary(INamedTypeSymbol t) =>
        t.ContainingAssembly.Name is "TaskOTime.DTOs" or "TaskOTime.DataLayer" ||
        t.Locations.Any(l => l.SourceTree is { } tree && Evidence.Generated(tree)) ||
        Evidence.Derives(t, "System.Data.Entity.DbContext");
    internal static bool Presentation(Compilation c, INamedTypeSymbol t) =>
        PresentationScope.IsWpf(t) || Evidence.Observable(t) ||
        c.ReferencedAssemblyNames.Any(a => a.Name == "PresentationFramework") && !Boundary(t) ||
        t.GetMembers().SelectMany(PresentationScope.MemberTypes).Any(PresentationScope.IsWpf);

    internal static bool TimeContract(INamedTypeSymbol t) => t.TypeKind == TypeKind.Interface &&
        t.GetMembers().OfType<IPropertySymbol>().Count(p => NullableType(p.Type) == "System.TimeSpan") >= 2 &&
        t.GetMembers().OfType<IPropertySymbol>().Any(p => NullableType(p.Type) == "System.DateTimeOffset") &&
        t.GetMembers().OfType<IPropertySymbol>().Count(p => SymbolEqualityComparer.Default.Equals(p.Type.OriginalDefinition, t.OriginalDefinition)) >= 2;
    private static string NullableType(ITypeSymbol t) => t is INamedTypeSymbol n &&
        n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? n.TypeArguments[0].ToDisplayString() : t.ToDisplayString();
    private static bool TimeCollection(INamedTypeSymbol t) => t.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IList_T) &&
        (t.TypeParameters.Any(tp => tp.ConstraintTypes.OfType<INamedTypeSymbol>().Any(TimeContract)) ||
         t.AllInterfaces.Any(i => i.TypeArguments.OfType<INamedTypeSymbol>().Any(a => a.AllInterfaces.Any(TimeContract))));
    private static bool Authentication(INamedTypeSymbol t) => t.TypeKind == TypeKind.Interface &&
        t.GetMembers().OfType<IMethodSymbol>().Any(m => m.Parameters.Any(p => p.Type.GetMembers().OfType<IPropertySymbol>()
            .Any(a => a.Name.Equals("Password", StringComparison.OrdinalIgnoreCase) && a.Type.SpecialType == SpecialType.System_String)));

    private void AnalyzeContracts(AssessmentProject[] projects, List<(AdditionalText File, XDocument Document)> xml, Recorder r)
    {
        var types = projects.SelectMany(p => Evidence.Types(p.Compilation).Where(t => Evidence.Source(t, p))).ToArray();
        var time = types.Where(TimeContract).ToArray();
        var collections = types.Where(TimeCollection).ToArray();
        var auth = types.Where(Authentication).ToArray();
        var login = types.Where(t => t.TypeKind == TypeKind.Class && t.GetMembers().SelectMany(PresentationScope.MemberTypes)
            .OfType<INamedTypeSymbol>().Any(a => auth.Any(b => a.ToDisplayString() == b.ToDisplayString())) && Evidence.Observable(t)).ToArray();
        if (contracts)
        {
            foreach (var role in new[] { ("time item contract", time.Length), ("time collection", collections.Length),
                ("authentication contract", auth.Length), ("observable login coordinator", login.Length),
                ("main-data presentation selection", types.Count(selection)) })
            {
                r.Measure("Scope", 1, role.Item2 > 0 ? 1 : 0);
                if (role.Item2 == 0) r.Report("Scope", Scope, Location.None, "Missing required " + role.Item1 + "; source/project removal is not migration.");
            }
            var entryProjects = projects.Where(p => p.Compilation.Options.OutputKind == OutputKind.WindowsApplication &&
                Evidence.Types(p.Compilation).Any(t => Evidence.Derives(t, "System.Windows.Application"))).ToArray();
            var metadata = xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment").Select(x => x.Document.Root!)
                .Where(e => e.Attribute("Path") != null).ToArray();
            if (metadata.Length > 0)
            {
                var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pending = new Queue<string>(entryProjects.Select(p => p.Path));
                while (pending.TryDequeue(out var path))
                {
                    if (!reachable.Add(path)) continue;
                    foreach (var reference in metadata.Where(e => string.Equals(e.Attribute("Path")!.Value, path, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(e => e.Elements("ProjectReference")))
                        if (reference.Attribute("Path") is { } target) pending.Enqueue(target.Value);
                }
                var assemblies = projects.Where(p => reachable.Contains(p.Path)).Select(p => p.Compilation.Assembly.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var role in new[] { ("time contract", time), ("time collection", collections), ("authentication", auth), ("login", login) })
                    if (!role.Item2.Any(t => assemblies.Contains(t.ContainingAssembly.Name)))
                        r.Report("Scope", Scope, Location.None, "Required " + role.Item1 + " is disconnected from the WPF executable source-reference graph.");
            }
        }
        foreach (var type in time.Concat(collections).Concat(login).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            var members = type.GetMembers().Where(s => !s.IsImplicitlyDeclared && s.DeclaredAccessibility == Accessibility.Public &&
                (s is IPropertySymbol || s is IMethodSymbol { MethodKind: MethodKind.Ordinary })).ToArray();
            var count = members.Count(ProseDetector.Documented);
            var typeDocumented = ProseDetector.Documented(type);
            r.Measure("Documentation", members.Length + 1, count + (typeDocumented ? 1 : 0));
            if (!typeDocumented || count * 2 < members.Length)
                r.Report("Documentation", Documentation, Evidence.At(type), type.ToDisplayString(), count, members.Length);
        }
        foreach (var type in collections.Concat(login))
        {
            r.Measure("ProtectedCore", 1, 1);
            if (!Evidence.Observable(type) || collections.Contains(type, SymbolEqualityComparer.Default) &&
                !Evidence.Implements(type, "System.Collections.Specialized.INotifyCollectionChanged"))
                r.Report("ProtectedCore", Core, Evidence.At(type), type.ToDisplayString() + " lost observable collection/property contracts.");
        }
        foreach (var p in projects)
        {
            var protectedTypes = Evidence.Types(p.Compilation).Where(t => Evidence.Observable(t) && !PresentationScope.IsWpf(t) && !selection(t)).ToArray();
            foreach (var t in protectedTypes)
            {
                foreach (var dependency in t.GetMembers().SelectMany(PresentationScope.MemberTypes).Where(PresentationScope.IsWpf))
                    r.Report("ProtectedCore", Core, Evidence.At(t), t.ToDisplayString() + " now exposes concrete WPF " + dependency.ToDisplayString());
            }
            foreach (var op in Evidence.Operations(p.Compilation, p: p))
            {
                var owner = Evidence.Model(p.Compilation, op.Syntax.SyntaxTree).GetEnclosingSymbol(op.Syntax.SpanStart)?.ContainingType;
                if (owner == null || !protectedTypes.Contains(owner, SymbolEqualityComparer.Default)) continue;
                var dependency = op switch { IMemberReferenceOperation m => m.Member.ContainingType,
                    IInvocationOperation i => i.TargetMethod.ContainingType, IObjectCreationOperation o => o.Type, _ => null };
                if (PresentationScope.IsWpf(dependency))
                    r.Report("ProtectedCore", Core, op.Syntax.GetLocation(), owner.ToDisplayString() + " now operates on concrete WPF " + dependency!.ToDisplayString());
            }
        }
        if (contracts)
        {
            var commands = types.Where(Evidence.Observable).SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
                .Count(p => p.Type.ToDisplayString() == "System.Windows.Input.ICommand");
            r.Measure("ProtectedCore", 1, commands > 0 ? 1 : 0);
            if (commands == 0) r.Report("ProtectedCore", Core, Location.None, "The observable core no longer exposes command contracts.");
        }
    }

    private void AnalyzeEF(AssessmentProject[] projects, List<(AdditionalText File, XDocument Document)> xml, Recorder r)
    {
        var contexts = projects.SelectMany(p => Evidence.Types(p.Compilation))
            .Where(t => Evidence.Derives(t.BaseType, "System.Data.Entity.DbContext") &&
                t.BaseType!.ContainingAssembly.Name == "EntityFramework").ToArray();
        foreach (var p in projects)
        {
            foreach (var a in p.Compilation.ReferencedAssemblyNames.Where(a => a.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)))
                r.Report("EF6", EF, Location.None, "EF Core reference added: " + a.Name);
        }
        foreach (var input in xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment"))
        foreach (var reference in input.Document.Descendants("Reference").Concat(input.Document.Descendants("Package")))
            if (reference.Attribute("Name")?.Value.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true)
                r.Report("EF6", EF, Evidence.At(input.File, reference), "Evaluated EF Core dependency added: " + reference.Attribute("Name")!.Value);
        r.Measure("EF6", 1, contexts.Length > 0 ? 1 : 0);
        if (contracts && (contexts.Length == 0 || !contexts.Any(t => t.GetMembers().OfType<IPropertySymbol>()
            .Any(p => IsDbSet(p.Type)))))
            r.Report("EF6", EF, Location.None, "The source EF6 DbContext/DbSet store contract is missing or replaced.");
        if (contracts && contexts.Length > 0)
        {
            var consumer = projects.Any(p => Evidence.Types(p.Compilation).Any(t => !Boundary(t) &&
                t.GetMembers().SelectMany(PresentationScope.MemberTypes).OfType<INamedTypeSymbol>()
                    .Any(m => Evidence.Derives(m, "System.Data.Entity.DbContext"))));
            if (!consumer) r.Report("EF6", EF, Location.None, "No source service/data-access member retains the EF6 context contract.");
        }
        foreach (var boundary in xml.SelectMany(x => x.Document.Descendants("EF6")))
        {
            var expected = boundary.Attribute("Context")?.Value;
            var context = contexts.FirstOrDefault(t => t.ToDisplayString() == expected);
            if (context == null)
            {
                r.Report("EF6", EF, Location.None, "The configured EF6 context compatibility contract is missing: " + expected);
                continue;
            }
            var entities = context.GetMembers().OfType<IPropertySymbol>().Select(p => p.Type).OfType<INamedTypeSymbol>()
                .Where(IsDbSet)
                .Select(t => t.TypeArguments[0].ToDisplayString()).ToHashSet(StringComparer.Ordinal);
            foreach (var entity in boundary.Elements("Entity").Select(e => e.Value))
                if (!entities.Contains(entity)) r.Report("EF6", EF, Evidence.At(context), "Required EF6 entity-set contract is missing: " + entity);
            var version = boundary.Attribute("PackageVersion")?.Value;
            foreach (var package in xml.SelectMany(x => x.Document.Descendants("Package")).Where(e => e.Attribute("Name")?.Value == "EntityFramework"))
                if (version != null && package.Attribute("Version")?.Value != version)
                    r.Report("EF6", EF, Location.None, "EF6 package version changed from the configured compatibility boundary " + version);
        }
    }
    private static bool IsDbSet(ITypeSymbol t) => t is INamedTypeSymbol n &&
        n.OriginalDefinition.MetadataName == "DbSet`1" && n.ContainingNamespace.ToDisplayString() == "System.Data.Entity" &&
        n.ContainingAssembly.Name == "EntityFramework";
}

internal sealed class Recorder(Action<Diagnostic> report, SortedDictionary<string, CriterionMetric> metrics)
{
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    internal bool HasInputErrors { get; private set; }
    internal void Measure(string criterion, int examined, int satisfied, int unverified = 0)
    {
        var m = metrics.GetValueOrDefault(criterion) ?? new(0, 0, 0, 0);
        metrics[criterion] = m with { Examined = m.Examined + examined, Satisfied = m.Satisfied + satisfied, Unverified = m.Unverified + unverified };
    }
    internal void Report(string criterion, DiagnosticDescriptor rule, Location location, params object[] arguments)
    {
        if (location.SourceTree != null)
        {
            var span = location.GetLineSpan();
            location = Location.Create(span.Path, location.SourceSpan, span.Span);
        }
        var diagnostic = Diagnostic.Create(rule, location, arguments);
        var key = rule.Id + "|" + location.GetLineSpan() + "|" + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        if (!reported.Add(key)) return;
        Measure(criterion, 0, 0);
        metrics[criterion] = metrics[criterion] with { Diagnostics = metrics[criterion].Diagnostics + 1 };
        HasInputErrors |= rule.Id == "ASM001";
        report(diagnostic);
    }
}

internal static class ProseDetector
{
    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase)
        { "the", "and", "or", "to", "of", "for", "with", "when", "only", "this", "that", "is", "are", "from",
          "returns", "uses", "keeps", "value", "selected", "stored", "without", "before", "after", "not" };
    private static readonly HashSet<string> Dutch = new(StringComparer.OrdinalIgnoreCase)
        { "de", "het", "een", "en", "of", "voor", "van", "met", "wanneer", "alleen", "wordt", "worden", "dit",
          "dat", "naar", "niet", "gebruikt", "geeft", "blijft", "waarde", "geselecteerde", "opgeslagen",
          "volgende", "vorige", "gebruiker", "verzameling", "tijdregistratie", "bijwerken", "berekent" };
    private static readonly HashSet<string> German = new(StringComparer.OrdinalIgnoreCase)
        { "der", "die", "das", "den", "dem", "des", "ein", "eine", "und", "oder", "für", "von", "mit", "wenn",
          "nur", "wird", "werden", "dies", "damit", "nicht", "gibt", "bleibt", "wert", "ausgewählte",
          "gespeicherte", "zurück", "benutzer", "sammlung", "zeitspanne", "beim", "nach", "auch", "weil",
          "noch", "kein" };
    internal static string? Detect(string prose)
    {
        // CamelCase identifiers, cref attributes and code elements do not provide prose votes.
        prose = Regex.Replace(prose, @"`[^`]*`", "");
        var words = Regex.Matches(prose, @"[\p{L}_][\p{L}\p{N}_.]*").Select(m => m.Value.TrimEnd('.'))
            .Where(w => !w.Contains('_') && !w.Contains('.') && !Regex.IsMatch(w, @"\p{Ll}\p{Lu}"))
            .Select(w => w.ToLowerInvariant()).ToArray();
        if (words.Length < 3) return null;
        var english = words.Count(English.Contains);
        var dutch = words.Count(Dutch.Contains);
        var german = words.Count(German.Contains);
        var foreign = Math.Max(dutch, german);
        var coverage = (double)foreign / words.Length;
        var confidence = foreign == 0 ? 0 : (double)(foreign - english) / foreign;
        if (foreign < 2 || coverage < 0.18 || confidence < 0.34) return null;
        var language = dutch >= german ? "Dutch" : "German";
        var lexicon = dutch >= german ? Dutch : German;
        var hits = words.Where(lexicon.Contains).Distinct(StringComparer.Ordinal).Take(6);
        return $"{language} confidence {confidence:0.00}, coverage {coverage:0.00}: {string.Join(", ", hits)}";
    }
    internal static string XmlText(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return "";
        try
        {
            var document = XDocument.Parse(xml);
            foreach (var e in document.Descendants().Where(e => e.Name.LocalName is "code" or "c" or "see" or "seealso" or "inheritdoc").ToArray()) e.Remove();
            return string.Join(" ", document.DescendantNodes().OfType<XText>().Select(t => t.Value));
        }
        catch (System.Xml.XmlException) { return ""; }
    }
    internal static bool Documented(ISymbol symbol)
    {
        var text = XmlText(symbol.GetDocumentationCommentXml(expandIncludes: false) ?? "");
        return Regex.Matches(text, @"\p{L}{2,}").Count >= 4;
    }
}
