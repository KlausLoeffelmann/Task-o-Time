using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class LocalizationFlowTests
{
    private const string Source = """
        using System;
        using System.Globalization;
        using Microsoft.Extensions.Localization;
        using Microsoft.Extensions.Logging.Abstractions;
        using Microsoft.Extensions.Options;
        namespace Example {
          public sealed class Words {
            public static Words Current { get; } = new Words();
            private IStringLocalizer strings;
            public Words() {
              var factory = new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Translations" }), NullLoggerFactory.Instance);
              strings = factory.Create("Captions", typeof(Words).Assembly.GetName().Name);
            }
            public string this[string key] => Evaluate(() => strings[key].Value);
            private string Evaluate(Func<string> read) => read();
            public string Format(string key, params object[] args) => Evaluate(() => strings[key, args].Value);
            public void Change(string name) { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); }
          }
          public sealed class Phrase {
            private readonly string key;
            public Phrase(string key) { this.key = key; }
            public override string ToString() => Words.Current.Format(key);
          }
          public abstract class Model : System.ComponentModel.INotifyPropertyChanged {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            protected string Text(string key) => Words.Current[key];
          }
          public sealed class DashboardModel : Model {
            public string Heading => Text("Save");
            public string Message => new Phrase("Save").ToString();
            public string Unused => Text("Unconsumed");
          }
          public sealed class PreferencesModel {
            public string CultureName { get; set; }
            public void Commit() => Words.Current.Change(CultureName);
          }
          public sealed class Authenticate : System.Windows.Window { }
          public sealed class Dashboard : System.Windows.Window { }
          public sealed class Entry : System.Windows.Window { }
          public sealed class Catalog : System.Windows.Window { }
          public sealed class Preferences : System.Windows.Window { }
          public sealed class TranslateExtension : System.Windows.Markup.MarkupExtension {
            public TranslateExtension(string key) { Key = key; }
            public string Key { get; set; }
            public override object ProvideValue(IServiceProvider provider) =>
              new System.Windows.Data.Binding("[" + Key + "]") { Source = Words.Current }.ProvideValue(provider);
          }
        }
        namespace System.Windows.Markup {
          public abstract class MarkupExtension { public abstract object ProvideValue(IServiceProvider provider); }
        }
        namespace System.Windows.Data {
          public sealed class Binding {
            public Binding(string path) { }
            public object Source { get; set; }
            public object ProvideValue(IServiceProvider provider) => this;
          }
        }
        """;
    private static InputFile File(string name, string content) => new(@"C:\independent\" + name, content);
    private static string Resource(string value) => $"""
        <root><resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
          <resheader name="version"><value>2.0</value></resheader>
          <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>
          <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>
          <data name="Save"><value>{value}</value></data></root>
        """;
    private static AdditionalText[] Files(string mutation = "")
    {
        var files = new List<AdditionalText> {
            File("Scenario.assessment", """
              <ScenarioScope><Localization NeutralLanguage="en">
                <RequiredLanguage>de</RequiredLanguage><RequiredLanguage>nl</RequiredLanguage><RequiredLanguage>es</RequiredLanguage>
                <Surface Id="SignIn">Authenticate</Surface><Surface Id="Clock">Dashboard</Surface>
                <Surface Id="Record">Entry</Surface><Surface Id="Projects">Catalog</Surface><OptionsSurface>Preferences</OptionsSurface>
              </Localization></ScenarioScope>
              """),
            File("Captions.resx", Resource("Save changes")), File("Captions.de.resx", Resource("Änderungen speichern")),
            File("Captions.nl.resx", Resource("Wijzigingen opslaan")), File("Captions.es.resx", Resource("Guardar cambios")),
            File("EmptyTemplate.resx", "<root/>"),
            File("Project.assessment", """
              <Project Name="Fixture"><Resource Path="C:\independent\Captions.resx" BaseName="Fixture.Translations.Captions"/></Project>
              """)
        };
        foreach (var surface in new[] { "Authenticate", "Dashboard", "Entry", "Catalog", "Preferences" })
        {
            var key = mutation == "missing-key" && surface == "Entry" ? "Absent" : "Save";
            var title = mutation == "literal" && surface == "Entry" ? "Literal override" : "{loc:Translate " + key + "}";
            files.Add(File("UnrelatedFileName" + surface + ".xaml", $$"""
              <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:loc="clr-namespace:Example" x:Class="Example.{{surface}}" Title="{{title}}">
                <StackPanel><TextBlock Text="{Binding Heading}"/><TextBlock Text="{Binding Message}"/>
                  <ComboBox SelectedValue="{Binding CultureName}"/></StackPanel>
              </Window>
              """));
        }
        files.Add(File("OtherMaintenance.xaml", """
          <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Title="Unrequired maintenance">
            <TextBlock Text="Another maintenance label"/>
          </Window>
          """));
        return files.ToArray();
    }
    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, AdditionalText[] files)
    {
        var c = AnalyzerTests.Compile(LanguageNames.CSharp, source);
        var analyzer = new OutcomeAnalyzer([new("", c)], _ => false, _ => false, false);
        var diagnostics = await c.WithAnalyzers([analyzer], new AnalyzerOptions(files.ToImmutableArray())).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        return diagnostics;
    }
    [Fact]
    public void Markup_binding_produces_the_source_indexer_key()
    {
        var c = AnalyzerTests.Compile(LanguageNames.CSharp, Source);
        var flow = new LocalizationFlow([new("", c)]);
        var binding = flow.MarkupBinding(c.GetTypeByMetadataName("Example.TranslateExtension")!);
        Assert.NotNull(binding);
        var lookup = Assert.Single(flow.Indexer(binding.Value.Source, "Save"));
        Assert.Equal("Save", lookup.Key);
        Assert.Equal("Fixture.Translations.Captions", lookup.BaseName);
    }
    [Theory]
    [InlineData("")]
    [InlineData("literal")]
    [InlineData("missing-key")]
    [InlineData("stub")]
    [InlineData("dead-lookup")]
    [InlineData("disconnected-options")]
    [InlineData("wrong-manifest")]
    [InlineData("missing-culture")]
    [InlineData("wrong-binding-path")]
    public async Task Factory_lambda_facade_and_live_markup_flow_requires_real_consumption_and_connected_options(string mutation)
    {
        var source = Source;
        if (mutation == "stub") source = source.Replace("Evaluate(() => strings[key].Value)", "\"Hard-coded result\"");
        if (mutation == "dead-lookup") source = source.Replace(
            "new System.Windows.Data.Binding(\"[\" + Key + \"]\") { Source = Words.Current }.ProvideValue(provider)",
            "\"Unused resource binding\"");
        if (mutation == "disconnected-options") source = source.Replace("Words.Current.Change(CultureName)", "Words.Current.Change(\"en\")");
        if (mutation == "wrong-binding-path") source = source.Replace("\"[\" + Key + \"]\"", "\"NotTheIndexer\"");
        var files = Files(mutation);
        if (mutation == "wrong-manifest") files = files.Select(f => f.Path.EndsWith("Project.assessment")
            ? File("Project.assessment", f.GetText()!.ToString().Replace("Fixture.Translations.Captions", "Fixture.Other.Captions")) : f).ToArray();
        if (mutation == "missing-culture") files = files.Where(f => !f.Path.EndsWith(".es.resx")).ToArray();
        var found = (await Analyze(source, files)).Where(d => d.Id is "LOC001" or "LOC002").ToArray();
        if (mutation.Length == 0) Assert.True(found.Length == 0, string.Join("\n", found.Select(d => d.ToString())));
        else Assert.NotEmpty(found);
        Assert.DoesNotContain(found, d => d.Location.GetLineSpan().Path?.EndsWith("EmptyTemplate.resx") == true ||
            d.GetMessage().Contains("Unrequired maintenance"));
        if (mutation == "literal") Assert.Contains(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Literal override"));
        if (mutation == "disconnected-options") Assert.Contains(found, d => d.GetMessage().Contains("Options must"));
    }

    [Fact]
    public async Task Required_bound_facade_retains_literal_mixed_with_lookup_and_ignores_dead_caller_keys()
    {
        var found = await Analyze(Source.Replace("Text(\"Save\");", "Text(\"Save\") + \"Untranslated suffix\";"), Files());
        Assert.Contains(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Untranslated suffix"));
        Assert.DoesNotContain(found, d => d.Id == "LOC001" && d.GetMessage().Contains("Unconsumed"));
    }

    [Fact]
    public async Task Interface_factory_and_renamed_selector_are_semantic_not_name_conventions()
    {
        var source = Source.Replace("var factory =", "IStringLocalizerFactory factory =").Replace("CultureName", "Choice");
        var files = Files().Select(f => File(Path.GetFileName(f.Path), f.GetText()!.ToString().Replace("CultureName", "Choice"))).ToArray();
        var found = await Analyze(source, files);
        Assert.DoesNotContain(found, d => d.Id is "LOC001" or "LOC002");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nested_control_context_and_bound_child_model_are_in_scope_but_unrelated_maintenance_is_not(bool displayed)
    {
        var source = Source + """
          namespace Widgets {
            public sealed class DetailPane : System.Windows.Controls.UserControl {
              public DetailPane(UnnamedModel value) { DataContext = value; }
            }
            public sealed class UnnamedModel : Example.Model {
              public ChildModel Child { get; } = new ChildModel();
            }
            public sealed class ChildModel : Example.Model {
              public string Heading => "Visible child caption";
            }
            public sealed class UnrelatedModel : Example.Model {
              public string Heading => "Unrequired maintenance caption";
            }
          }
          """;
        var files = Files().ToList();
        if (displayed)
        {
            var index = files.FindIndex(f => f.Path.Contains("Dashboard.xaml"));
            files[index] = File("UnrelatedFileNameDashboard.xaml", files[index].GetText()!.ToString()
                .Replace("<StackPanel>", """<StackPanel xmlns:w="clr-namespace:Widgets"><w:DetailPane/>"""));
        }
        files.Add(File("ArbitraryChild.xaml", """
          <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Widgets.DetailPane">
            <TextBlock Text="{Binding Child.Heading}"/>
          </UserControl>
          """));
        var found = await Analyze(source, files.ToArray());
        Assert.Equal(displayed, found.Any(d => d.Id == "LOC002" && d.GetMessage().Contains("Visible child caption")));
        Assert.DoesNotContain(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Unrequired maintenance caption"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Object_data_does_not_merge_unrelated_instances_but_known_presentation_initializers_remain_findings(bool known)
    {
        var render = known
            ? """public void Render() { var row = new Row { Label = "Visible literal" }; Title = row.Label; }"""
            : """public void Render(Row row) { Title = row.Label; }""";
        var source = Source.Replace("public sealed class Authenticate : System.Windows.Window { }",
            "public sealed class Authenticate : System.Windows.Window { " + render + " }") + """
              namespace Example {
                public sealed class Row { public string Label { get; set; } }
                public sealed class UnrelatedMaintenance {
                  public void Edit(Row row) { row.Label = "Unrelated stored data"; }
                }
              }
              """;
        var found = await Analyze(source, Files());
        Assert.Equal(known, found.Any(d => d.Id == "LOC002" && d.GetMessage().Contains("Visible literal")));
        Assert.DoesNotContain(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Unrelated stored data"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bound_auto_property_initializers_in_required_child_models_are_not_treated_as_unrelated_data(bool declaration)
    {
        var source = Source.Replace("public string Heading => Text(\"Save\");",
            declaration ? """public string Heading { get; } = "Visible seeded caption";""" :
                """public string Heading { get; set; } public DashboardModel() { Heading = "Visible seeded caption"; }""");
        var found = await Analyze(source, Files());
        Assert.Contains(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Visible seeded caption"));
    }
}
