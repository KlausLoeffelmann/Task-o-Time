using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Modernization.Analyzers.Tests;

internal sealed class LocalizationAnalysis(AssessmentProject[] projects,
    List<(AdditionalText File, XDocument Document)> xml, Recorder r)
{
    private sealed record Resource(AdditionalText File, string Stem, string? Culture, Dictionary<string, string> Values,
        HashSet<string> TextKeys, bool Valid);
    private sealed record Accessor(IPropertySymbol Property, string Key, Resource Neutral);
    private static readonly HashSet<string> TextProperties = new(StringComparer.Ordinal)
        { "Text", "Content", "Header", "Title", "ToolTip", "Watermark", "DisplayName", "ErrorMessage", "Heading", "DaySummary", "MarkerLabel", "RecordingStatusText" };
    private readonly List<Accessor> accessors = [];
    private readonly HashSet<Accessor> used = [];
    private readonly HashSet<string> bindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> xamlProperties = new(StringComparer.Ordinal);
    private readonly HashSet<string> appliedSurfaces = new(StringComparer.Ordinal);
    private readonly List<Resource> resources = [];
    private readonly Dictionary<string, string[]> requiredSurfaces = new(StringComparer.Ordinal);
    private string[] requiredLanguages = [];
    private string? neutralLanguage;
    private string? optionsSurface;
    private bool strictExtensionsPolicy;
    private bool localizerUsed;
    private bool optionsSelector;
    private bool optionsCultureBehavior;
    private bool policyValid = true;
    private LocalizationFlow flow = null!;
    private readonly HashSet<Resource> usedProviders = [];
    private readonly HashSet<string> selectedProperties = [];
    private readonly HashSet<string> boundCommands = [];
    private readonly HashSet<string> uiHandlers = [];
    private readonly Dictionary<string, string> presentationTypes = [];
    private readonly Dictionary<string, string> presentationFiles = new(StringComparer.OrdinalIgnoreCase);
    private static string Identity(IPropertySymbol property) => property.ContainingAssembly.Name + "|" + property.GetDocumentationCommentId();

    internal void Run()
    {
        ReadPolicy();
        ReadResources();
        CheckDeclaredNeutralLanguage();
        FindAccessors();
        flow = new(projects);
        BuildPresentationScope();
        AnalyzeXaml();
        foreach (var p in projects) AnalyzeOperations(p);
        optionsCultureBehavior = flow.CultureFromSelection(selectedProperties, boundCommands, uiHandlers, presentationTypes.Keys.ToHashSet());
        CheckResources();
        CheckExtensionLocalization();
        var complete = used.Count(a => policyValid && a.Neutral.Valid && HasRequiredCultures(a.Neutral)) +
            usedProviders.Count(n => policyValid && n.Valid && HasRequiredCultures(n));
        r.Measure("Localization", Math.Max(1, resources.Count + accessors.Count), complete > 0 ? resources.Count + used.Count : 0);
        if (complete == 0)
            r.Report("Localization", OutcomeAnalyzer.Localization, Location.None,
                "No UI-consumed ResourceManager accessor or Microsoft.Extensions.Localization provider with valid neutral and required-culture .resx key parity was found.");
    }

    private static CultureInfo? KnownCulture(string value)
    {
        try
        {
            var info = CultureInfo.GetCultureInfo(value.Trim());
            return info.Name.Length > 0 && CultureInfo.GetCultures(CultureTypes.AllCultures)
                .Any(c => c.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase)) ? info : null;
        }
        catch (CultureNotFoundException) { return null; }
    }
    private void ReadPolicy()
    {
        var languages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment" &&
            x.Document.Root?.Name.LocalName == "ScenarioScope"))
        foreach (var policy in input.Document.Root!.Elements("Localization"))
        {
            foreach (var requirement in policy.Elements("RequiredLanguage"))
            {
                var language = KnownCulture(requirement.Value)?.TwoLetterISOLanguageName;
                if (language != null) languages.Add(language);
                else
                {
                    policyValid = false;
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(input.File, requirement),
                        "Unrecognized required resource language in external policy: " + requirement.Value);
                }
            }
            if (policy.Attribute("NeutralLanguage") is { } neutral)
            {
                var language = KnownCulture(neutral.Value)?.TwoLetterISOLanguageName;
                if (language == null || neutralLanguage != null && neutralLanguage != language)
                {
                    policyValid = false;
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(input.File, neutral),
                        "Invalid or conflicting neutral resource language in external policy.");
                }
                else neutralLanguage = language;
            }
            foreach (var surface in policy.Elements("Surface"))
            {
                var id = surface.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(surface.Value))
                    requiredSurfaces[id] = surface.Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            optionsSurface = policy.Element("OptionsSurface")?.Value.Trim();
            strictExtensionsPolicy |= requiredSurfaces.Count > 0;
        }
        requiredLanguages = languages.Order(StringComparer.Ordinal).ToArray();
        foreach (var language in requiredLanguages) r.Measure("LocalizationCulture." + language, 0, 0);
    }
    private Resource[] AlignedCultures(Resource neutral) => resources.Where(c => c.Stem == neutral.Stem &&
        c.Culture != null && c.Valid && c.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(neutral.Values.Keys.Order(StringComparer.Ordinal))).ToArray();
    private bool HasRequiredCultures(Resource neutral)
    {
        var aligned = AlignedCultures(neutral);
        return requiredLanguages.Length == 0 ? aligned.Length > 0 :
            requiredLanguages.All(language => aligned.Any(c => CultureInfo.GetCultureInfo(c.Culture!).TwoLetterISOLanguageName == language));
    }
    private void CheckDeclaredNeutralLanguage()
    {
        if (neutralLanguage == null) return;
        foreach (var project in projects)
        foreach (var attribute in project.Compilation.Assembly.GetAttributes().Where(a =>
            a.AttributeClass?.ToDisplayString() == "System.Resources.NeutralResourcesLanguageAttribute"))
        {
            var declared = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            if (declared == null || KnownCulture(declared)?.TwoLetterISOLanguageName != neutralLanguage)
                r.Report("Localization", OutcomeAnalyzer.Localization,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                    "Declared neutral resource language conflicts with external policy " + neutralLanguage + ".");
        }
    }

    private void ReadResources()
    {
        foreach (var input in xml.Where(x => Path.GetExtension(x.File.Path).Equals(".resx", StringComparison.OrdinalIgnoreCase)))
        {
            var root = input.Document.Root;
            var valid = root?.Name.LocalName == "root";
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var header in root?.Elements("resheader") ?? [])
                if (!headers.TryAdd(header.Attribute("name")?.Value ?? "", header.Element("value")?.Value ?? "")) valid = false;
            valid &= headers.GetValueOrDefault("resmimetype") == "text/microsoft-resx" &&
                headers.GetValueOrDefault("version")?.StartsWith("2.", StringComparison.Ordinal) == true &&
                headers.GetValueOrDefault("reader")?.Contains("System.Resources.ResXResourceReader", StringComparison.Ordinal) == true &&
                headers.GetValueOrDefault("writer")?.Contains("System.Resources.ResXResourceWriter", StringComparison.Ordinal) == true;
            var data = root?.Elements("data").ToArray() ?? [];
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var textKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in data)
            {
                var key = e.Attribute("name")?.Value;
                var value = e.Element("value");
                if (string.IsNullOrWhiteSpace(key) || value == null || !values.TryAdd(key, value.Value)) { valid = false; continue; }
                if ((e.Attribute("type") == null || e.Attribute("type")!.Value.Split(',')[0].Trim() == "System.String") &&
                    e.Attribute("mimetype") == null)
                {
                    textKeys.Add(key);
                    if (string.IsNullOrWhiteSpace(value.Value)) valid = false;
                    if (Evidence.MasterData(key) || Evidence.MasterData(value.Value))
                        r.Report("Naming", OutcomeAnalyzer.Naming, Evidence.At(input.File, e), key + " = " + value.Value);
                }
            }
            valid &= values.Count > 0;
            var stem = Path.ChangeExtension(input.File.Path, null)!;
            string? culture = null;
            var suffix = Path.GetExtension(stem).TrimStart('.');
            if (suffix.Length > 0)
            {
                try
                {
                    var info = CultureInfo.GetCultureInfo(suffix);
                    if (info.Name.Length > 0 && CultureInfo.GetCultures(CultureTypes.AllCultures).Any(c => c.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase)))
                    { culture = info.Name; stem = Path.ChangeExtension(stem, null)!; }
                }
                catch (CultureNotFoundException) { }
            }
            resources.Add(new(input.File, stem, culture, values, textKeys, valid));
        }
    }

    private void CheckResources()
    {
        var consumed = used.Select(a => a.Neutral).Concat(usedProviders).ToHashSet();
        foreach (var resource in resources.Where(a => consumed.Any(n => n.Stem == a.Stem) && !a.Valid))
            r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(resource.File), "Invalid/empty classic .resx schema, headers, values or duplicate keys.");
        foreach (var neutral in consumed)
        {
            var variants = resources.Where(c => c.Stem == neutral.Stem && c.Culture != null).ToArray();
            if (requiredLanguages.Length == 0)
            {
                if (variants.Length == 0)
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(neutral.File), "Neutral .resx has no culture variant.");
            }
            else
            {
                var aligned = AlignedCultures(neutral);
                var missing = requiredLanguages.Where(language => !aligned.Any(c =>
                    CultureInfo.GetCultureInfo(c.Culture!).TwoLetterISOLanguageName == language)).ToArray();
                foreach (var language in requiredLanguages)
                    r.Measure("LocalizationCulture." + language, 1, missing.Contains(language, StringComparer.Ordinal) ? 0 : 1);
                if (missing.Length > 0)
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(neutral.File),
                        "Neutral .resx is missing required language cultures with valid key parity: " + string.Join(", ", missing) + ".");
            }
            foreach (var culture in variants)
            {
                if (!culture.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(neutral.Values.Keys.Order(StringComparer.Ordinal)))
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(culture.File), "Culture .resx keys do not match the neutral resource.");
                else if (strictExtensionsPolicy && culture.TextKeys.All(key => culture.Values[key] == neutral.Values[key]))
                    r.Report("Localization", OutcomeAnalyzer.Localization, Evidence.At(culture.File),
                        "Culture .resx duplicates all neutral text instead of providing translated content.");
            }
        }
    }

    private void FindAccessors()
    {
        foreach (var p in projects)
        {
            var operations = Evidence.Operations(p.Compilation, generated: true).ToArray();
            var returnedValues = new Producers(p.Compilation, operations, dataOnly: true);
            var managers = operations.OfType<IObjectCreationOperation>()
                .Where(o => o.Type?.ToDisplayString() == "System.Resources.ResourceManager")
                .Select(o => (Owner: Evidence.Model(p.Compilation, o.Syntax.SyntaxTree).GetEnclosingSymbol(o.Syntax.SpanStart)?.ContainingType,
                    Base: o.Arguments.FirstOrDefault()?.Value.ConstantValue.Value as string,
                    OwnAssembly: o.Arguments.Skip(1).Any(a => Evidence.Descendants(a.Value).OfType<ITypeOfOperation>()
                        .Any(t => t.TypeOperand.ContainingAssembly?.Name == p.Compilation.Assembly.Name) ||
                        Evidence.Descendants(a.Value).OfType<IInvocationOperation>().Any(i =>
                            i.TargetMethod.ContainingType.ToDisplayString() == "System.Reflection.Assembly" && i.TargetMethod.Name == "GetExecutingAssembly"))))
                .Where(o => o.Owner != null && o.Base != null && o.OwnAssembly).ToArray();
            foreach (var call in operations.OfType<IInvocationOperation>().Where(i =>
                i.TargetMethod.ContainingType.ToDisplayString() == "System.Resources.ResourceManager" && i.TargetMethod.Name == "GetString"))
            {
                var enclosing = Evidence.Model(p.Compilation, call.Syntax.SyntaxTree).GetEnclosingSymbol(call.Syntax.SpanStart);
                var property = enclosing as IPropertySymbol ?? (enclosing as IMethodSymbol)?.AssociatedSymbol as IPropertySymbol;
                if (property is not { IsStatic: true, Type.SpecialType: SpecialType.System_String }) continue;
                if (!Evidence.Generated(call.Syntax.SyntaxTree, p) && !property.ContainingType.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute")) continue;
                var returnsLookup = operations.OfType<IReturnOperation>().Where(o => o.ReturnedValue != null &&
                        SymbolEqualityComparer.Default.Equals(Producers.ReturnOwner(p.Compilation, o), property))
                    .SelectMany(o => returnedValues.Of(o.ReturnedValue!)).OfType<IInvocationOperation>()
                    .Any(o => o.Syntax.SyntaxTree == call.Syntax.SyntaxTree && o.Syntax.Span == call.Syntax.Span);
                if (!returnsLookup) continue;
                var key = call.Arguments.FirstOrDefault()?.Value.ConstantValue.Value as string;
                if (key == null) continue;
                // Associate the actual receiver with a ResourceManager construction, not an
                // unrelated unused field in the same accessor class.
                var origins = call.Instance == null ? [] : new Producers(p.Compilation, operations).Of(call.Instance).ToArray();
                var bases = origins.OfType<IObjectCreationOperation>().Where(o => o.Type?.ToDisplayString() == "System.Resources.ResourceManager")
                    .Select(o => o.Arguments.FirstOrDefault()?.Value.ConstantValue.Value as string).Where(b => b != null).ToArray();
                foreach (var manager in managers.Where(m => SymbolEqualityComparer.Default.Equals(m.Owner, property.ContainingType) && bases.Contains(m.Base)))
                {
                    var resource = resources.FirstOrDefault(a => a.Culture == null &&
                        MatchesManifest(p, a, manager.Base!) &&
                        (p.Path.Length == 0 || a.File.Path.StartsWith(Path.GetDirectoryName(p.Path)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
                    if (resource != null && resource.TextKeys.Contains(key) && resource.Values.TryGetValue(key, out var value) && Evidence.Prose(value))
                        accessors.Add(new(property, key, resource));
                    else r.Report("Localization", OutcomeAnalyzer.Localization, call.Syntax.GetLocation(), "Accessor GetString key/base is not backed by a neutral textual .resx entry: " + key);
                }
            }
        }
    }
    private bool MatchesManifest(AssessmentProject p, Resource resource, string baseName)
    {
        var metadata = xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment" &&
            x.Document.Root?.Attribute("Name")?.Value == p.Compilation.Assembly.Name)
            .SelectMany(x => x.Document.Descendants("Resource"))
            .FirstOrDefault(e => string.Equals(e.Attribute("Path")?.Value, resource.File.Path, StringComparison.OrdinalIgnoreCase));
        if (metadata != null) return metadata.Attribute("BaseName")?.Value == baseName;
        // In-memory fixtures have no project system. A production project without evaluated
        // manifest names cannot claim successful resource lookup.
        return p.Path.Length == 0 && (baseName == Path.GetFileName(resource.Stem) ||
            baseName.EndsWith("." + Path.GetFileName(resource.Stem), StringComparison.Ordinal));
    }

    private void AnalyzeXaml()
    {
        foreach (var input in xml.Where(x => Path.GetExtension(x.File.Path).Equals(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
        var surface = presentationFiles.GetValueOrDefault(input.File.Path);
        if (strictExtensionsPolicy && surface == null) continue;
        ReadUiEntrypoints(input.Document);
        if (surface == "$options" &&
            input.Document.Descendants().Any(e => (e.Name.LocalName is "ComboBox" or "ListBox") &&
                e.Attributes().Any(a => a.Name.LocalName is "SelectedValue" or "SelectedItem" && a.Value.StartsWith("{Binding", StringComparison.Ordinal))))
            optionsSelector = true;
        if (optionsSurface != null && surface == "$options")
        {
            var selected = input.Document.Descendants().Where(e => e.Name.LocalName is "ComboBox" or "ListBox")
                .SelectMany(e => e.Attributes().Where(a => a.Name.LocalName is "SelectedValue" or "SelectedItem"))
                .Select(a => Regex.Match(a.Value, @"^\{Binding\s+(?:Path\s*=\s*)?([\w.]+)"))
                .Where(m => m.Success).Select(m => m.Groups[1].Value.Split('.').Last()).ToHashSet();
            foreach (var property in projects.SelectMany(p => Evidence.Types(p.Compilation)).SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
                .Where(p => selected.Contains(p.Name) && presentationTypes.GetValueOrDefault(LocalizationFlow.Id(p.ContainingType)) == "$options"))
                selectedProperties.Add(LocalizationFlow.Id(property));
        }
        foreach (var element in input.Document.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                var value = attribute.Value;
                if (Evidence.MasterData(value) && (TextProperties.Contains(attribute.Name.LocalName) && !value.StartsWith('{') ||
                    attribute.Name.LocalName is "Key" or "Class" or "Name"))
                    r.Report("Naming", OutcomeAnalyzer.Naming, Evidence.At(input.File, attribute), value);
                if (!TextProperties.Contains(attribute.Name.LocalName)) continue;
                if (value.StartsWith("{Binding", StringComparison.Ordinal))
                {
                    var match = Regex.Match(value, @"^\{Binding\s+(?:Path\s*=\s*)?([A-Za-z_][\w.]*)");
                    if (match.Success) bindings.Add(match.Groups[1].Value.Split('.').Last());
                }
                if (value.StartsWith("{", StringComparison.Ordinal))
                {
                    if (ConsumeMarkup(element, value) && surface != null) appliedSurfaces.Add(surface);
                    var resourceKey = Regex.Match(value, @"^\{(?:StaticResource|DynamicResource)\s+([^{}]+)\}$");
                    if (resourceKey.Success)
                    {
                        var strings = xml.SelectMany(d => d.Document.Descendants().Where(e => e.Name.LocalName == "String" &&
                            e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == resourceKey.Groups[1].Value)
                            .Select(e => (d.File, Element: e))).ToArray();
                        if (strings.Length == 1 && Evidence.Prose(strings[0].Element.Value))
                        {
                            r.Report("Localization", OutcomeAnalyzer.Literal, Evidence.At(strings[0].File, strings[0].Element), strings[0].Element.Value);
                            if (Evidence.MasterData(strings[0].Element.Value))
                                r.Report("Naming", OutcomeAnalyzer.Naming, Evidence.At(strings[0].File, strings[0].Element), strings[0].Element.Value);
                        }
                    }
                }
                else if (Evidence.Prose(value))
                    r.Report("Localization", OutcomeAnalyzer.Literal, Evidence.At(input.File, attribute), value);
            }
            // Property element syntax and x:Static nested within a displayed property.
            if (element.Name.LocalName == "Static" && element.Ancestors().Any(a => TextProperties.Contains(a.Name.LocalName.Split('.').Last())))
            {
                if (ConsumeMarkup(element, "{x:Static " + element.Attribute("Member")?.Value + "}") && surface != null)
                    appliedSurfaces.Add(surface);
            }
            var propertyElement = element.Name.LocalName.Split('.');
            if (propertyElement.Length == 2 && TextProperties.Contains(propertyElement[1]) && !element.HasElements && Evidence.Prose(element.Value))
                r.Report("Localization", OutcomeAnalyzer.Literal, Evidence.At(input.File, element), element.Value);
            if (!element.HasElements && element.Name.LocalName is "TextBlock" or "TextBox" or "Run" or "Button" or "Label" &&
                Evidence.Prose(element.Value))
            {
                r.Report("Localization", OutcomeAnalyzer.Literal, Evidence.At(input.File, element), element.Value);
                if (Evidence.MasterData(element.Value)) r.Report("Naming", OutcomeAnalyzer.Naming, Evidence.At(input.File, element), element.Value);
            }
        }
        }
    }

    private bool ConsumeMarkup(XElement element, string markup)
    {
        var extension = Regex.Match(markup, @"^\{(?<prefix>\w+):(?<type>\w+)\s+(?:(?<property>\w+)\s*=\s*)?(?<key>[\w.]+)\s*\}$");
        if (extension.Success)
        {
            var nsName = element.GetNamespaceOfPrefix(extension.Groups["prefix"].Value)?.NamespaceName ?? "";
            var clrName = Regex.Match(nsName, @"^clr-namespace:([^;]*)(?:;assembly=([^;]+))?");
            if (clrName.Success)
            foreach (var p in projects.Where(p => !clrName.Groups[2].Success || p.Compilation.Assembly.Name == clrName.Groups[2].Value))
            {
                var name = clrName.Groups[1].Value + "." + extension.Groups["type"].Value;
                var type = p.Compilation.GetTypeByMetadataName(name + "Extension") ?? p.Compilation.GetTypeByMetadataName(name);
                if (type == null || flow.MarkupBinding(type) is not { } binding ||
                    extension.Groups["property"].Success && extension.Groups["property"].Value != binding.Property) continue;
                var accepted = false;
                foreach (var lookup in flow.Indexer(binding.Source, extension.Groups["key"].Value))
                    accepted |= ConsumeLookup(lookup, Evidence.At(Input(element), element));
                if (accepted) return true;
            }
        }
        var match = Regex.Match(markup, @"^\{(?:\w+:)?Static\s+(?:Member=)?(?<prefix>\w+):(?<type>[\w.]+)\.(?<member>\w+)\s*\}$");
        if (!match.Success) return false;
        var ns = element.GetNamespaceOfPrefix(match.Groups["prefix"].Value)?.NamespaceName ?? "";
        var clr = Regex.Match(ns, @"^clr-namespace:([^;]*)(?:;assembly=([^;]+))?");
        if (!clr.Success) return false;
        var matched = false;
        var typeName = (clr.Groups[1].Value.Length == 0 ? "" : clr.Groups[1].Value + ".") + match.Groups["type"].Value;
        foreach (var p in projects.Where(p => !clr.Groups[2].Success || p.Compilation.Assembly.Name == clr.Groups[2].Value))
            foreach (var property in p.Compilation.GetTypeByMetadataName(typeName)?.GetMembers(match.Groups["member"].Value)
                .OfType<IPropertySymbol>().Where(p => p.IsStatic && p.Type.SpecialType == SpecialType.System_String) ?? [])
                xamlProperties.Add(Identity(property));
        foreach (var accessor in accessors.Where(a =>
            a.Property.ContainingType.ToDisplayString() == typeName &&
            a.Property.Name == match.Groups["member"].Value &&
            (!clr.Groups[2].Success || a.Property.ContainingAssembly.Name == clr.Groups[2].Value)))
        { used.Add(accessor); matched = true; }
        return matched;
    }

    private void AnalyzeOperations(AssessmentProject p)
    {
        var operations = Evidence.Operations(p.Compilation, generated: true).ToArray();
        var producers = new Producers(p.Compilation, operations, dataOnly: true);
        var sinks = new List<IOperation>();
        sinks.AddRange(operations.OfType<IReturnOperation>().Where(o => o.ReturnedValue != null &&
            Producers.ReturnOwner(p.Compilation, o) is IPropertySymbol property && xamlProperties.Contains(Identity(property)))
            .Select(o => o.ReturnedValue!));
        foreach (var op in operations.Where(o => !Evidence.Generated(o.Syntax.SyntaxTree, p)))
        {
            var owner = Evidence.Model(p.Compilation, op.Syntax.SyntaxTree).GetEnclosingSymbol(op.Syntax.SpanStart)?.ContainingType;
            if (strictExtensionsPolicy && (owner == null || !presentationTypes.ContainsKey(LocalizationFlow.Id(owner)))) continue;
            if (op is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation property } assignment &&
                (TextProperties.Contains(property.Property.Name) && PresentationScope.IsWpf(property.Property.ContainingType) ||
                 bindings.Contains(property.Property.Name) && presentationTypes.ContainsKey(LocalizationFlow.Id(property.Property.ContainingType))))
                sinks.Add(assignment.Value);
            if (op is IPropertyInitializerOperation initializer && initializer.InitializedProperties.Any(property =>
                bindings.Contains(property.Name) && presentationTypes.ContainsKey(LocalizationFlow.Id(property.ContainingType))))
                sinks.Add(initializer.Value);
            if (op is IInvocationOperation call && call.TargetMethod.ContainingType.ToDisplayString() == "System.Windows.MessageBox" &&
                call.TargetMethod.Name == "Show")
                sinks.AddRange(call.Arguments.Where(a => a.Parameter?.Type.SpecialType == SpecialType.System_String).Select(a => a.Value));
            if (op is IInvocationOperation setter && setter.TargetMethod.Name is "SetValue" or "SetCurrentValue" &&
                PresentationScope.IsWpf(setter.TargetMethod.ContainingType) && setter.Arguments.Length == 2)
            {
                var field = Evidence.Descendants(setter.Arguments[0].Value).OfType<IFieldReferenceOperation>().FirstOrDefault()?.Field;
                if (field != null && field.Name.EndsWith("Property", StringComparison.Ordinal) &&
                    TextProperties.Contains(field.Name[..^8]) && PresentationScope.IsWpf(field.ContainingType))
                    sinks.Add(setter.Arguments[1].Value);
            }
            if (op is IReturnOperation { ReturnedValue: { } returned })
            {
                var symbol = Evidence.Model(p.Compilation, op.Syntax.SyntaxTree).GetEnclosingSymbol(op.Syntax.SpanStart);
                var prop = symbol as IPropertySymbol ?? (symbol as IMethodSymbol)?.AssociatedSymbol as IPropertySymbol;
                if (prop?.Type.SpecialType == SpecialType.System_String && Evidence.Observable(prop.ContainingType) &&
                    (bindings.Contains(prop.Name) || !strictExtensionsPolicy && TextProperties.Contains(prop.Name))) sinks.Add(returned);
            }
        }
        foreach (var sink in sinks)
        {
            var values = producers.Of(sink).ToArray();
            var owner = Evidence.Model(p.Compilation, sink.Syntax.SyntaxTree).GetEnclosingSymbol(sink.Syntax.SpanStart)?.ContainingType;
            var surface = owner == null ? null : presentationTypes.GetValueOrDefault(LocalizationFlow.Id(owner)) ?? Surface(owner.Name);
            foreach (var reference in values.OfType<IPropertyReferenceOperation>())
                foreach (var accessor in accessors.Where(a => a.Property.GetDocumentationCommentId() == reference.Property.GetDocumentationCommentId() &&
                    a.Property.ContainingAssembly.Name == reference.Property.ContainingAssembly.Name)) used.Add(accessor);
            foreach (var value in flow.Values(sink))
            {
                if (value.Resource is { } lookup && ConsumeLookup(lookup, value.Operation.Syntax.GetLocation()))
                {
                    if (surface != null) appliedSurfaces.Add(surface);
                }
                if (value.Literal is { } literal && Evidence.Prose(literal))
                {
                    r.Report("Localization", OutcomeAnalyzer.Literal, value.Operation.Syntax.GetLocation(), literal);
                    if (Evidence.MasterData(literal)) r.Report("Naming", OutcomeAnalyzer.Naming, value.Operation.Syntax.GetLocation(), literal);
                }
            }
        }
    }

    private AdditionalText Input(XElement element) => xml.First(x => ReferenceEquals(x.Document, element.Document)).File;
    private void ReadUiEntrypoints(XDocument document)
    {
        var classes = projects.SelectMany(p => Evidence.Types(p.Compilation)).ToArray();
        var cls = document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;
        var rootType = classes.FirstOrDefault(t => t.ToDisplayString() == cls);
        foreach (var element in document.Descendants())
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.LocalName.Split('.').Last() == "Command")
            {
                var binding = Regex.Match(attribute.Value, @"^\{Binding\s+(?:Path\s*=\s*)?([\w.]+)");
                if (!binding.Success) continue;
                var name = binding.Groups[1].Value.Split('.').Last();
                foreach (var property in classes.Where(t => presentationTypes.ContainsKey(LocalizationFlow.Id(t)))
                    .SelectMany(t => t.GetMembers(name).OfType<IPropertySymbol>()).Where(p => p.Type is INamedTypeSymbol type &&
                        (type.ToDisplayString() == "System.Windows.Input.ICommand" || Evidence.Implements(type, "System.Windows.Input.ICommand"))))
                    boundCommands.Add(LocalizationFlow.Id(property));
                continue;
            }
            if (rootType == null || !Regex.IsMatch(attribute.Value, @"^[A-Za-z_]\w*$")) continue;
            INamedTypeSymbol? elementType = null;
            if (ReferenceEquals(element, document.Root)) elementType = rootType;
            else
            {
                var clr = Regex.Match(element.Name.NamespaceName, @"^clr-namespace:([^;]*)(?:;assembly=([^;]+))?");
                var names = clr.Success ? new[] { clr.Groups[1].Value + "." + element.Name.LocalName } :
                    new[] { "System.Windows.Controls." + element.Name.LocalName, "System.Windows." + element.Name.LocalName };
                elementType = projects.SelectMany(p => names.Select(p.Compilation.GetTypeByMetadataName)).OfType<INamedTypeSymbol>().FirstOrDefault();
            }
            IEventSymbol? eventSymbol = null;
            for (var type = elementType; type != null && eventSymbol == null; type = type.BaseType)
                eventSymbol = type.GetMembers(attribute.Name.LocalName.Split('.').Last()).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol?.Type is not INamedTypeSymbol { DelegateInvokeMethod: { } signature }) continue;
            foreach (var method in rootType.GetMembers(attribute.Value).OfType<IMethodSymbol>().Where(m => m.Parameters.Length == signature.Parameters.Length))
                uiHandlers.Add(LocalizationFlow.Id(method));
        }
    }

    private bool ConsumeLookup(LocalizationFlow.Lookup lookup, Location location)
    {
        var project = projects.FirstOrDefault(p => p.Compilation.Assembly.Name == lookup.Assembly);
        var neutral = project == null ? null : resources.FirstOrDefault(n => n.Culture == null && MatchesManifest(project, n, lookup.BaseName));
        if (neutral == null || !neutral.TextKeys.Contains(lookup.Key))
        {
            r.Report("Localization", OutcomeAnalyzer.Localization, location, "Consumed localizer key/base is not backed by an evaluated neutral textual resource: " + lookup.Key);
            return false;
        }
        usedProviders.Add(neutral);
        localizerUsed = true;
        return true;
    }

    private void BuildPresentationScope()
    {
        var types = projects.SelectMany(p => Evidence.Types(p.Compilation)).ToArray();
        string? Role(string value) => Surface(value) ?? (optionsSurface != null && value.Contains(optionsSurface, StringComparison.OrdinalIgnoreCase) ? "$options" : null);
        foreach (var type in types)
            if (Role(type.Name) is { } role) presentationTypes[LocalizationFlow.Id(type)] = role;
        var pathMembers = new HashSet<string>();
        var xaml = xml.Where(x => Path.GetExtension(x.File.Path).Equals(".xaml", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var input in xaml)
        {
            var cls = input.Document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;
            var role = Role(cls ?? input.File.Path);
            if (role == null && strictExtensionsPolicy) continue;
            presentationFiles[input.File.Path] = role ?? "";
        }
        for (var i = 0; i < 16; i++)
        {
            var before = presentationFiles.Count;
            foreach (var input in xaml.Where(x => presentationFiles.ContainsKey(x.File.Path)))
            foreach (var element in input.Document.Descendants())
            {
                var clr = Regex.Match(element.Name.NamespaceName, @"^clr-namespace:([^;]*)(?:;assembly=([^;]+))?");
                if (!clr.Success) continue;
                var name = clr.Groups[1].Value + "." + element.Name.LocalName;
                var role = presentationFiles[input.File.Path];
                foreach (var type in types.Where(t => t.ToDisplayString() == name &&
                    (!clr.Groups[2].Success || t.ContainingAssembly.Name == clr.Groups[2].Value)))
                    presentationTypes.TryAdd(LocalizationFlow.Id(type), role);
                foreach (var child in xaml.Where(x => x.Document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name))
                    presentationFiles.TryAdd(child.File.Path, role);
            }
            if (before == presentationFiles.Count) break;
        }
        foreach (var input in xaml.Where(x => presentationFiles.ContainsKey(x.File.Path)))
        {
            foreach (var match in input.Document.Descendants().Attributes().Select(a =>
                Regex.Match(a.Value, @"^\{Binding\s+(?:Path\s*=\s*)?([\w.]+)")).Where(m => m.Success))
                foreach (var member in match.Groups[1].Value.Split('.')) pathMembers.Add(member);
        }
        static ITypeSymbol? ValueType(IOperation value) =>
            value is IConversionOperation conversion ? ValueType(conversion.Operand) : value.Type;
        var contexts = projects.SelectMany(p => Evidence.Operations(p.Compilation).OfType<ISimpleAssignmentOperation>()
            .Where(a => a.Target is IPropertyReferenceOperation r && r.Property.Name == "DataContext")
            .Select(a => (Owner: ((IPropertyReferenceOperation)a.Target).Instance?.Type as INamedTypeSymbol ??
                Evidence.Model(p.Compilation, a.Syntax.SyntaxTree).GetEnclosingSymbol(a.Syntax.SpanStart)?.ContainingType,
                Context: ValueType(a.Value) as INamedTypeSymbol))).ToArray();
        for (var i = 0; i < 16; i++)
        {
            var before = presentationTypes.Count;
            void Add(INamedTypeSymbol? type, string role)
            {
                if (type == null) return;
                if (types.Any(t => LocalizationFlow.Id(t) == LocalizationFlow.Id(type))) presentationTypes.TryAdd(LocalizationFlow.Id(type), role);
                foreach (var argument in type.TypeArguments.OfType<INamedTypeSymbol>()) Add(argument, role);
            }
            foreach (var context in contexts)
                if (context.Owner != null && presentationTypes.TryGetValue(LocalizationFlow.Id(context.Owner), out var role)) Add(context.Context, role);
            foreach (var type in types.Where(t => presentationTypes.ContainsKey(LocalizationFlow.Id(t))))
            {
                var role = presentationTypes[LocalizationFlow.Id(type)];
                Add(type.BaseType, role);
                foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => pathMembers.Contains(p.Name)))
                    Add(property.Type as INamedTypeSymbol, role);
            }
            if (before == presentationTypes.Count) break;
        }
    }

    private string? Surface(string value) => requiredSurfaces.FirstOrDefault(pair =>
        pair.Value.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase))).Key;

    private void CheckExtensionLocalization()
    {
        if (!strictExtensionsPolicy) return;
        var dependency = projects.Any(p => p.Compilation.ReferencedAssemblyNames.Any(a =>
            a.Name.StartsWith("Microsoft.Extensions.Localization", StringComparison.Ordinal))) ||
            xml.Where(x => Path.GetExtension(x.File.Path) == ".assessment")
                .SelectMany(x => x.Document.Descendants("Package").Concat(x.Document.Descendants("Reference")))
                .Any(x => x.Attribute("Name")?.Value.StartsWith("Microsoft.Extensions.Localization", StringComparison.OrdinalIgnoreCase) == true);
        if (!dependency)
            r.Report("Localization", OutcomeAnalyzer.Localization, Location.None,
                "Microsoft.Extensions.Localization dependency is missing.");
        if (!localizerUsed)
            r.Report("Localization", OutcomeAnalyzer.Localization, Location.None,
                "No Microsoft.Extensions.Localization key lookup is meaningfully applied to presentation output.");
        foreach (var surface in requiredSurfaces.Keys.Except(appliedSurfaces))
            r.Report("Localization", OutcomeAnalyzer.Localization, Location.None,
                "Required localized UI surface has no applied resource/localizer key: " + surface + ".");
        if (!optionsSelector || !optionsCultureBehavior)
            r.Report("Localization", OutcomeAnalyzer.Localization, Location.None,
                "Options must expose a language selector connected to CultureInfo localization behavior.");
    }
}
