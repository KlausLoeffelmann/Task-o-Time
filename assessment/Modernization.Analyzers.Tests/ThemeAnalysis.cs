using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Modernization.Analyzers.Tests;

internal sealed class ThemeAnalysis(AssessmentProject[] projects,
    (AdditionalText File, XDocument Document)[] documents,
    Func<INamedTypeSymbol, bool> selection, Func<string, bool> sourceSelection, Recorder r, bool contracts)
{
    private sealed record Value(string Text, XElement Context);
    private sealed record Color(double Luminance, bool Shared, double? Opacity = 1);
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private readonly HashSet<string> examined = new(StringComparer.Ordinal);
    private readonly Dictionary<XElement, XElement[]?> styleChains = [];
    private static string TypeName(string value) => value.Replace("{x:Type ", "").Trim('}', ' ').Split(':').Last();
    private static string? Key(string value)
    {
        var m = Regex.Match(value, @"^\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey=)?([^{}]+)\}$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
    private (AdditionalText File, XDocument Document) Input(XElement e) =>
        documents.First(d => ReferenceEquals(d.Document, e.Document));

    internal void Run()
    {
        var main = documents.Where(d => IsMain(d.File, d.Document.Root)).ToArray();
        var calendars = documents.SelectMany(d => d.Document.Descendants().Where(e => e.Name.LocalName == "Calendar")).ToArray();
        if (contracts && (main.Length == 0 || calendars.Length == 0))
            r.Report("Theme", OutcomeAnalyzer.ThemeCoverage, Location.None, "Calendar/main-data XAML coverage is missing from the supplied source graph.");
        foreach (var document in main)
        {
            var root = document.Document.Root!;
            foreach (var element in root.DescendantsAndSelf().Where(e => e == root ||
                e.Attribute("Background") != null || e.Attribute("Foreground") != null ||
                e.Name.LocalName is "TextBlock" or "TextBox" or "ListBox" or "DataGrid" or "Button" or "TabControl"))
            {
                if (element.Ancestors().Any(a => a.Name.LocalName is "Style" or "ControlTemplate" or "DataTemplate")) continue;
                var bg = Effective(element, "Background", []);
                var fg = Effective(element, "Foreground", []);
                Pair(element, bg, fg, "main-data " + element.Name.LocalName, requireShared: element == root);
            }
        }
        foreach (var calendar in calendars)
        {
            foreach (var button in new[] { "CalendarDayButton", "CalendarButton" })
            {
                var property = Effective(calendar, button + "Style", [], inherit: false);
                var style = property == null ? Lookup(calendar, "@" + button, []) :
                    property.Context.Name.LocalName == "Style" ? property.Context :
                    Lookup(property.Context, Key(property.Text) ?? "", []);
                if (style == null)
                {
                    Missing(calendar, button + " has no resolvable style/template.");
                    continue;
                }
                var chain = StyleChain(style);
                if (chain == null) continue;
                var setters = Setters(chain);
                var background = setters.GetValueOrDefault("Background");
                var foreground = setters.GetValueOrDefault("Foreground");
                Pair(style, background, foreground, button + " normal", true);
                var templateValue = setters.GetValueOrDefault("Template");
                var template = templateValue?.Context.Name.LocalName == "ControlTemplate" ? templateValue.Context :
                    templateValue != null && Key(templateValue.Text) is { } templateKey
                        ? Lookup(templateValue.Context, templateKey, []) : null;
                if (template?.Name.LocalName != "ControlTemplate") template = null;
                if (template == null) Missing(style, button + " has no inspectable template; default theme states are unverified.");
                var stateElements = chain.SelectMany(s => s.Elements().Where(e => e.Name.LocalName == "Style.Triggers"))
                    .SelectMany(e => e.Descendants()).Where(e => !e.Ancestors().Any(a => a.Name.LocalName == "ControlTemplate"))
                    .Concat(template?.Descendants() ?? []);
                foreach (var state in new[] { "selected", "inactive", "hover", "disabled" })
                {
                    var triggers = stateElements.Where(e => State(e, state)).Distinct().ToArray();
                    if (triggers.Length == 0) { Missing(style, button + " " + state + " state is missing."); continue; }
                    foreach (var trigger in triggers)
                    {
                        var stateValues = trigger.Descendants().Where(e => e.Name.LocalName == "Setter")
                            .Select(e => (Property: e.Attribute("Property")?.Value.Split('.').Last(), Value: SetterValue(e)))
                            .Where(a => a.Property != null && a.Value != null).ToArray();
                        var bg = stateValues.LastOrDefault(a => a.Property == "Background").Value ?? background;
                        var fg = stateValues.LastOrDefault(a => a.Property == "Foreground").Value ?? foreground;
                        var animations = trigger.Descendants().Where(e => e.Name.LocalName.Contains("Animation", StringComparison.Ordinal)).ToArray();
                        if (stateValues.Length == 0 && animations.Length == 0)
                            Missing(trigger, button + " " + state + " contains no inspectable visual state change.");
                        if (stateValues.Any(a => a.Property == "Opacity" && a.Value?.Text != "1"))
                            Missing(trigger, button + " " + state + " opacity compositing depends on an unmodeled backdrop.");
                        foreach (var animation in animations)
                        {
                            var target = animation.Attributes().FirstOrDefault(a => a.Name.LocalName == "TargetProperty")?.Value ?? "";
                            var to = animation.Attribute("To");
                            if (to == null) { Missing(animation, button + " " + state + " animation target value is unresolved."); continue; }
                            if (target.Contains("Background", StringComparison.Ordinal)) bg = new(to.Value, animation);
                            else if (target.Contains("Foreground", StringComparison.Ordinal)) fg = new(to.Value, animation);
                            else if (target != "Opacity") Missing(animation, button + " " + state + " animation cannot be mapped to foreground/background.");
                        }
                        Pair(trigger, bg, fg, button + " " + state, false, state == "disabled" ? 3 : 4.5);
                    }
                }
                if (template != null)
                {
                    foreach (var element in template.Descendants().Where(e => e.Attribute("Background") != null || e.Attribute("Foreground") != null))
                    {
                        Value? Attr(string name, Value? fallback)
                        {
                            var a = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Split('.').Last() == name);
                            return a == null || a.Value == "{TemplateBinding " + name + "}" ? fallback : new(a.Value, element);
                        }
                        Pair(element, Attr("Background", background), Attr("Foreground", foreground), button + " template surface", false);
                    }
                }
            }
        }
        AnalyzeCode();
    }
    private bool IsMain(AdditionalText file, XElement? root)
    {
        if (sourceSelection(file.Path)) return true;
        var cls = root?.Attribute(X + "Class")?.Value;
        return cls != null && projects.SelectMany(p => Evidence.Types(p.Compilation))
            .Any(t => selection(t) && (t.ToDisplayString() == cls || t.ToDisplayString().EndsWith("." + cls, StringComparison.Ordinal)));
    }
    private static bool State(XElement e, string state)
    {
        if (e.Name.LocalName == "VisualState")
        {
            var name = e.Attribute(X + "Name")?.Value;
            return state switch { "selected" => name is "Selected" or "SelectedToday",
                "inactive" => name == "Inactive", "hover" => name == "MouseOver", "disabled" => name == "Disabled", _ => false };
        }
        if (e.Name.LocalName != "Trigger") return false;
        var property = e.Attribute("Property")?.Value.Split('.').Last();
        var value = e.Attribute("Value")?.Value;
        return state switch { "selected" => property is "IsSelected" or "HasSelectedDays" && value == "True",
            "inactive" => property == "IsInactive" && value == "True",
            "hover" => property == "IsMouseOver" && value == "True",
            "disabled" => property == "IsEnabled" && value == "False", _ => false };
    }
    private void Missing(XElement element, string reason)
    {
        r.Measure("Theme", 1, 0, 1);
        r.Report("Theme", OutcomeAnalyzer.ThemeCoverage, Evidence.At(Input(element).File, element), reason);
    }
    private void Pair(XElement at, Value? background, Value? foreground, string role, bool requireShared, double minimum = 4.5)
    {
        var key = Input(at).File.Path + "|" + Evidence.At(Input(at).File, at).GetLineSpan() + "|" + role;
        if (!examined.Add(key)) return;
        var bg = Resolve(background, [], false);
        var fg = Resolve(foreground, [], false);
        if (bg == null || fg == null || bg.Opacity != 1 || fg.Opacity != 1)
        {
            Missing(at, role + ": foreground/background cannot both be resolved to opaque theme colors (including brush opacity and color alpha).");
            return;
        }
        var contrast = (Math.Max(bg.Luminance, fg.Luminance) + 0.05) / (Math.Min(bg.Luminance, fg.Luminance) + 0.05);
        var good = bg.Luminance <= 0.45 && contrast >= minimum && (!requireShared || bg.Shared && fg.Shared);
        r.Measure("Theme", 1, good ? 1 : 0);
        if (!good) r.Report("Theme", OutcomeAnalyzer.Theme, Evidence.At(Input(at).File, at),
            role + ": " + (bg.Luminance > 0.45 ? "fixed light background; " : "") +
            (contrast < minimum ? "contrast " + contrast.ToString("F2", CultureInfo.InvariantCulture) + " below " + minimum.ToString(CultureInfo.InvariantCulture) + "; " : "") +
            (requireShared && (!bg.Shared || !fg.Shared) ? "normal surface does not reuse theme foreground/background resources." : ""));
    }
    private Color? Resolve(Value? value, HashSet<string> seen, bool shared)
    {
        if (value == null) return null;
        var opacity = BrushOpacity(value.Context);
        var key = Key(value.Text);
        if (key != null)
        {
            var resource = Lookup(value.Context, key, []);
            if (resource == null || !seen.Add(Input(resource).File.Path + "|" + key)) return null;
            var color = resource.Attribute("Color")?.Value ?? resource.Value.Trim();
            var resolved = Resolve(new(color, resource), seen, true);
            return resolved == null ? null : resolved with { Opacity = opacity * resolved.Opacity };
        }
        return ParseColor(value.Text) is { } literal ? literal with { Shared = shared, Opacity = opacity * literal.Opacity } : null;
    }
    private static double? BrushOpacity(XElement context)
    {
        if (context.Name.LocalName != "SolidColorBrush") return 1;
        var text = context.Attribute("Opacity")?.Value ??
            context.Elements().FirstOrDefault(e => e.Name.LocalName == "SolidColorBrush.Opacity")?.Value;
        if (text == null) return 1;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value >= 0 && value <= 1 ? value : null;
    }
    internal static double? Luminance(string value)
    {
        var color = ParseColor(value);
        return color?.Opacity == 1 ? color.Luminance : null;
    }
    private static Color? ParseColor(string value)
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["White"] = "FFFFFF", ["Black"] = "000000", ["WhiteSmoke"] = "F5F5F5", ["Ivory"] = "FFFFF0",
            ["Gray"] = "808080", ["DarkGray"] = "A9A9A9", ["LightGray"] = "D3D3D3", ["Red"] = "FF0000",
            ["MidnightBlue"] = "191970", ["LightSteelBlue"] = "B0C4DE", ["DarkSlateBlue"] = "483D8B",
            ["Navy"] = "000080", ["Yellow"] = "FFFF00", ["Blue"] = "0000FF", ["Green"] = "008000",
            ["Transparent"] = "00000000"
        };
        var hex = named.GetValueOrDefault(value) ?? (value.StartsWith('#') ? value[1..] : "");
        if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
        var alpha = 1.0;
        if (hex.Length == 8)
        {
            if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var channel)) return null;
            alpha = channel / 255.0;
            hex = hex[2..];
        }
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
        static double Linear(int c) { var v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return new(.2126 * Linear((rgb >> 16) & 255) + .7152 * Linear((rgb >> 8) & 255) + .0722 * Linear(rgb & 255), false, alpha);
    }
    private Value? Effective(XElement element, string property, HashSet<XElement> seen, bool inherit = true)
    {
        if (!seen.Add(element)) return null;
        var attribute = element.Attributes().FirstOrDefault(a => a.Name.LocalName.Split('.').Last() == property);
        if (attribute != null && attribute.Value != "Transparent") return new(attribute.Value, element);
        var inline = element.Elements().FirstOrDefault(e => e.Name.LocalName == element.Name.LocalName + "." + property)?.Elements().FirstOrDefault();
        if (inline != null) return new(inline.Value, inline);
        var explicitStyle = element.Attribute("Style");
        var style = Lookup(element, explicitStyle == null ? "@" + element.Name.LocalName : Key(explicitStyle.Value) ?? "", []);
        var chain = style == null ? null : StyleChain(style);
        var value = chain == null ? null : Setters(chain).GetValueOrDefault(property);
        if (value != null) return value;
        return !inherit || element.Parent == null ? null : Effective(element.Parent, property, seen);
    }
    private static Value? SetterValue(XElement e)
    {
        var v = e.Attribute("Value")?.Value;
        if (v != null) return new(v, e);
        var brush = e.Elements().SelectMany(a => a.Elements()).FirstOrDefault();
        return brush == null ? null : new(brush.Attribute("Color")?.Value ?? brush.Value, brush);
    }
    private XElement[]? StyleChain(XElement style)
    {
        if (styleChains.TryGetValue(style, out var cached)) return cached;
        var seen = new HashSet<XElement>();
        var chain = new List<XElement>();
        var current = style;
        while (true)
        {
            if (!seen.Add(current))
            {
                Missing(style, "Style BasedOn contains a cycle; effective template and states are unverified.");
                return styleChains[style] = null;
            }
            chain.Add(current);
            if (current.Attribute("BasedOn") is not { } based) break;
            var parent = Lookup(current, Key(based.Value) ?? "", []);
            if (parent?.Name.LocalName != "Style")
            {
                Missing(current, "Style BasedOn does not resolve to an inspectable base style.");
                return styleChains[style] = null;
            }
            current = parent;
        }
        chain.Reverse();
        return styleChains[style] = chain.ToArray();
    }
    private static Dictionary<string, Value> Setters(IEnumerable<XElement> chain)
    {
        var values = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var setter in chain.SelectMany(style => style.Elements().Where(e => e.Name.LocalName == "Setter")))
            if (setter.Attribute("Property") is { } property && SetterValue(setter) is { } value)
                values[property.Value.Split('.').Last()] = value;
        return values;
    }
    private XElement? Lookup(XElement context, string key, HashSet<XElement> seen)
    {
        foreach (var ancestor in context.AncestorsAndSelf())
        {
            foreach (var resources in ancestor.Elements().Where(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
                if (In(resources, key, seen) is { } local) return local;
            if (ancestor.Name.LocalName == "ResourceDictionary" && In(ancestor, key, seen) is { } dictionary) return dictionary;
        }
        foreach (var app in documents.Where(d => d.Document.Root?.Name.LocalName == "Application"))
            foreach (var resources in app.Document.Root!.Elements())
                if (In(resources, key, seen) is { } global) return global;
        return null;
    }
    private XElement? In(XElement container, string key, HashSet<XElement> seen)
    {
        if (!seen.Add(container)) return null;
        var direct = container.Elements().LastOrDefault(e => e.Attribute(X + "Key")?.Value == key ||
            key.StartsWith('@') && e.Name.LocalName == "Style" && e.Attribute(X + "Key") == null &&
                TypeName(e.Attribute("TargetType")?.Value ?? "") == key[1..]);
        if (direct != null) return direct;
        foreach (var nested in container.Elements().Where(e => e.Name.LocalName == "ResourceDictionary").Reverse())
            if (In(nested, key, seen) is { } found) return found;
        foreach (var merged in container.Elements().Where(e => e.Name.LocalName == "ResourceDictionary.MergedDictionaries"))
        foreach (var dictionary in merged.Elements().Reverse())
        {
            if (dictionary.Attribute("Source") is not { } source)
            {
                if (In(dictionary, key, seen) is { } inline) return inline;
                continue;
            }
            var relative = source.Value.Replace('/', '\\');
            var component = relative.IndexOf(";component\\", StringComparison.OrdinalIgnoreCase);
            var file = component >= 0
                ? documents.Where(d => d.File.Path.EndsWith(relative[(component + 11)..], StringComparison.OrdinalIgnoreCase)).ToArray()
                : documents.Where(d => string.Equals(Path.GetFullPath(d.File.Path),
                    Path.GetFullPath(relative, Path.GetDirectoryName(Input(dictionary).File.Path)!), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (file.Length == 1 && file[0].Document.Root is { } root && In(root, key, seen) is { } result) return result;
        }
        return null;
    }
    private void AnalyzeCode()
    {
        foreach (var p in projects)
        foreach (var op in Evidence.Operations(p.Compilation, p: p).OfType<ISimpleAssignmentOperation>())
        {
            if (op.Target is not IPropertyReferenceOperation property ||
                property.Property.Name is not ("Background" or "Foreground") || !PresentationScope.IsWpf(property.Property.ContainingType)) continue;
            var owner = Evidence.Model(p.Compilation, op.Syntax.SyntaxTree).GetEnclosingSymbol(op.Syntax.SpanStart)?.ContainingType;
            if (owner == null || !selection(owner)) continue;
            var fixedColors = Evidence.Descendants(op.Value).OfType<IPropertyReferenceOperation>()
                .Where(a => a.Property.ContainingType.ToDisplayString() is "System.Windows.Media.Brushes" or "System.Windows.Media.Colors").ToArray();
            foreach (var color in fixedColors)
            {
                var l = Luminance(color.Property.Name);
                if (property.Property.Name == "Background" && l > .45)
                    r.Report("Theme", OutcomeAnalyzer.Theme, op.Syntax.GetLocation(), "WPF property operation fixes a light main-data background: " + color.Property.ToDisplayString());
                else
                {
                    r.Measure("Theme", 1, 0, 1);
                    r.Report("Theme", OutcomeAnalyzer.ThemeCoverage, op.Syntax.GetLocation(), "Code-assigned WPF color pair/state cannot be verified structurally.");
                }
            }
        }
    }
}
