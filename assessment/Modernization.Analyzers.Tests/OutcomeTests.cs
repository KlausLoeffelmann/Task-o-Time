using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class OutcomeTests
{
    private static InputFile File(string name, string content) => new(@"C:\fixtures\" + name, content);
    private static async Task<ImmutableArray<Diagnostic>> Analyze(Compilation compilation, AdditionalText[]? files = null,
        AssessmentProject[]? corpus = null, bool contracts = false)
    {
        var analyzer = new OutcomeAnalyzer(corpus ?? [new("", compilation)], t => t.Name == "RenamedScreen",
            path => Path.GetFileName(path) == "Screen.xaml", contracts);
        var found = await compilation.WithAnalyzers([analyzer, new CommentLanguageAnalyzer()],
            new AnalyzerOptions((files ?? []).ToImmutableArray())).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(found, d => d.Id == "AD0001");
        return found;
    }
    private static Compilation Compile(string language, string code) => AnalyzerTests.Compile(language, code);
    private static string Plain(string language) => language == LanguageNames.CSharp ? "public class Plain {}" : "Public Class Plain\nEnd Class";

    public static IEnumerable<object[]> ProseCases()
    {
        foreach (var language in new[] { LanguageNames.CSharp, LanguageNames.VisualBasic })
        foreach (var pair in new[]
        {
            ("Returns the selected project and preserves the booking order.", false),
            ("Returns the project. De geselecteerde gebruiker wordt gebruikt.", true),
            ("Gibt die gespeicherte Zeitspanne des Benutzers zurück.", true),
            ("De volgende tijdregistratie wordt bijgewerkt.", true),
            ("TODO: SelectedGebruiker and DeVolgende are identifiers, not translated prose.", false),
            ("Uses de_volgende and die_sammlung as identifiers.", false),
            ("Uses `de volgende` as a quoted code example.", false),
            ("<summary>Returns the next item.</summary><see cref=\"DeVolgende.GeselecteerdeGebruiker\"/>", false),
            ("<summary>Returns the next item.</summary><c>de volgende</c>", false),
            ("<summary>Gibt die ausgewählte Zeitspanne zurück.</summary>", true),
            ("Der Wert bleibt erhalten, damit nicht jeder Aufrufer neu rechnen muss.", true),
            ("Weil hier Zahlen stehen, bleibt das Format stabil.", true),
            ("Noch kein Ergebnis vorhanden.", true),
            ("Der Eintrag bleibt lesbar, auch wenn weitere Stunden hinzukommen.", true),
            ("Die Anzeige zeigt gespeicherte Werte.", true),
            ("Den vorherigen Zustand brauchen wir nur beim Zurücksetzen.", true),
            ("Keeps the value so callers do not have to calculate it again.", false),
            ("Formats the numbers consistently for display.", false),
            ("No result is available yet.", false),
            ("Keeps the entry readable even when more hours accumulate.", false),
            ("Displays the stored values without changing them.", false),
            ("Uses the previous state only when resetting the value.", false),
            ("<geselecteerde>Returns the selected item.</geselecteerde>", false)
        })
            yield return [language, pair.Item1, pair.Item2];
    }
    [Theory]
    [InlineData("valid", false, false)]
    [InlineData("missing-state", false, true)]
    [InlineData("literal-override", true, false)]
    [InlineData("missing-style", false, true)]
    public async Task Calendar_style_properties_resolve_through_application_merged_dictionary_and_based_on(string variant, bool contrast, bool coverage)
    {
        const string namespaces = """xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" """;
        var buttons = string.Join("\n", new[] { "CalendarDayButton", "CalendarButton" }.Select(type =>
            ButtonStyle(type, variant == "missing-state").Replace("<Style TargetType=", $"<Style x:Key=\"{type}Palette\" TargetType=")));
        if (variant == "literal-override") buttons = buttons.Replace("Value=\"#282828\"", "Value=\"White\"");
        var overrideStyle = variant == "missing-style" ? """CalendarButtonStyle="{DynamicResource Missing}" """ : "";
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)), [
            File("App.xaml", $"""<Application {namespaces}><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Palette/Shared.xaml"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>"""),
            File(@"Palette\Shared.xaml", $$"""
                <ResourceDictionary {{namespaces}}>
                  <ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Buttons.xaml"/></ResourceDictionary.MergedDictionaries>
                  <SolidColorBrush x:Key="Surface" Color="#202020"/><SolidColorBrush x:Key="Ink" Color="#F8F8F8"/>
                  <Style x:Key="BaseCalendar" TargetType="Calendar">
                    <Setter Property="CalendarDayButtonStyle" Value="{DynamicResource CalendarDayButtonPalette}"/>
                    <Setter Property="CalendarButtonStyle" Value="{DynamicResource CalendarButtonPalette}"/>
                  </Style>
                  <Style TargetType="Calendar" BasedOn="{StaticResource BaseCalendar}"/>
                </ResourceDictionary>
                """),
            File(@"Palette\Buttons.xaml", $"""<ResourceDictionary {namespaces}>{buttons}</ResourceDictionary>"""),
            File("Screen.xaml", $$"""<Window {{namespaces}} Background="{DynamicResource Surface}" Foreground="{DynamicResource Ink}"><Calendar {{overrideStyle}}/></Window>""")
        ]);
        Assert.Equal(contrast, found.Any(d => d.Id == "THM001"));
        Assert.Equal(coverage, found.Any(d => d.Id == "THM002"));
    }

    [Theory]
    [MemberData(nameof(ProseCases))]
    public async Task Comment_and_XML_prose_is_bilingual_not_identifier_or_string_matching(string language, string prose, bool bad)
    {
        var prefix = language == LanguageNames.CSharp ? (prose.StartsWith('<') ? "/// " : "// ") :
            (prose.StartsWith('<') ? "''' " : "' ");
        var found = await Analyze(Compile(language, prefix + prose + "\n" + Plain(language)));
        Assert.Equal(bad, found.Any(d => d.Id == "ENG001"));
    }
    [Theory]
    [InlineData("This method returns the selected item and keeps the stored order for callers.", false)]
    [InlineData("Deze methode geeft de geselecteerde waarde terug en wordt alleen voor de gebruiker gebruikt.", true)]
    [InlineData("Diese Methode gibt den ausgewählten Wert zurück und wird nur für den Benutzer verwendet.", true)]
    [InlineData("The selected waarde is stored for the next call.", false)]
    public async Task Comment_language_uses_coverage_and_confidence(string prose, bool bad)
    {
        var found = await Analyze(Compile(LanguageNames.CSharp, "// " + prose + "\npublic class Plain {}"));
        Assert.Equal(bad, found.Any(d => d.Id == "ENG001"));
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Source_language_strings_are_not_comment_findings(string language)
    {
        var code = language == LanguageNames.CSharp ?
            """public class Plain { public string Id = "de volgende gebruiker wordt gebruikt"; }""" :
            """
            Public Class Plain
            Public Id As String = "de volgende gebruiker wordt gebruikt"
            End Class
            """;
        Assert.DoesNotContain(await Analyze(Compile(language, code)), d => d.Id == "ENG001");
    }

    private static string TimeInterface(string language, bool docs)
    {
        var comment = language == LanguageNames.CSharp ? "/// <summary>Preserves chronological links and elapsed durations.</summary>\n" :
            "''' <summary>Preserves chronological links and elapsed durations.</summary>\n";
        if (!docs) comment = "";
        return language == LanguageNames.CSharp ? $$"""
            {{comment}}public interface RenamedTimeline {
              {{comment}}System.DateTimeOffset? Event { get; set; }
              {{comment}}System.TimeSpan? Before { get; set; }
              {{comment}}System.TimeSpan? After { get; set; }
              {{comment}}RenamedTimeline Previous { get; set; }
              {{comment}}RenamedTimeline Next { get; set; }
            }
            """ : $$"""
            {{comment}}Public Interface RenamedTimeline
              {{comment}}Property [Event] As System.DateTimeOffset?
              {{comment}}Property Before As System.TimeSpan?
              {{comment}}Property After As System.TimeSpan?
              {{comment}}Property Previous As RenamedTimeline
              {{comment}}Property [Next] As RenamedTimeline
            End Interface
            """;
    }
    [Theory]
    [InlineData(LanguageNames.CSharp, true)]
    [InlineData(LanguageNames.CSharp, false)]
    [InlineData(LanguageNames.VisualBasic, true)]
    [InlineData(LanguageNames.VisualBasic, false)]
    public async Task Essential_time_contract_documentation_cannot_be_deleted_or_renamed_away(string language, bool docs)
    {
        Assert.Equal(!docs, (await Analyze(Compile(language, TimeInterface(language, docs)))).Any(d => d.Id == "ENG002"));
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Empty_XML_markup_is_not_meaningful_documentation(string language)
    {
        var code = TimeInterface(language, true).Replace("Preserves chronological links and elapsed durations.", """<see cref="RenamedTimeline"/>""");
        Assert.Contains(await Analyze(Compile(language, code)), d => d.Id == "ENG002");
    }
    [Fact]
    public async Task All_production_VB_including_framework_types_is_diagnosed_but_tests_are_excluded()
    {
        var vb = Compile(LanguageNames.VisualBasic, "Public Class RenamedFramework\nPublic Property Value As Integer\nEnd Class");
        var cs = Compile(LanguageNames.CSharp, "public class EntryPoint {}");
        var found = await Analyze(cs, corpus: [new("", cs), new("", vb)]);
        Assert.Single(found.Where(d => d.Id == "LNG001"));
        Assert.DoesNotContain(await Analyze(cs, corpus: [new("", cs), new("", vb, Test: true)]), d => d.Id == "LNG001");
    }
    [Fact]
    public async Task Generated_VB_is_not_an_authored_language_gate()
    {
        var cs = Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp));
        var vb = Compile(LanguageNames.VisualBasic, Plain(LanguageNames.VisualBasic));
        vb = vb.RemoveAllSyntaxTrees().AddSyntaxTrees(VisualBasicSyntaxTree.ParseText(Plain(LanguageNames.VisualBasic), path: "Anything.g.vb"));
        Assert.DoesNotContain(await Analyze(cs, corpus: [new("", cs), new("", vb)]), d => d.Id == "LNG001");
    }
    [Fact]
    public async Task Empty_corpus_and_compiler_failure_are_analyzer_input_diagnostics()
    {
        var valid = Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp));
        Assert.Contains(await Analyze(valid, corpus: []), d => d.Id == "ASM001");
        var invalid = valid.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Broken { Nonexistent Missing; }"));
        Assert.Contains(await Analyze(invalid), d => d.Id == "ASM001" && d.GetMessage().Contains("CS0246"));
        Assert.Contains(await Analyze(valid, contracts: true), d => d.Id == "SCP001");
        var serialized = RepositoryTests.Convert(@"C:\fixtures", "corpus", Diagnostic.Create(OutcomeAnalyzer.Scope, Location.None, "Missing role"));
        Assert.Equal("", serialized.Path);
        Assert.Equal(0, serialized.Line);
    }

    internal static string Resx(string key = "Save", string value = "Save changes") => $$"""
        <root>
          <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
          <resheader name="version"><value>2.0</value></resheader>
          <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>
          <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>
          <data name="{{key}}" xml:space="preserve"><value>{{value}}</value></data>
        </root>
        """;
    private static string Localized(string language, bool used = true, bool literal = false) => language == LanguageNames.CSharp ? $$"""
        using RM = System.Resources.ResourceManager;
        [System.CodeDom.Compiler.GeneratedCode("ClassicResources", "1")]
        public static class UnusuallyNamed {
          private static RM manager = new RM("Renamed", typeof(UnusuallyNamed).Assembly);
          public static string Save { get { return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture); } }
        }
        public class RenamedScreen : System.Windows.Window {
          public void Render() { {{(used ? "Title = " + (literal ? "\"Ready\"" : "UnusuallyNamed.Save") + ";" : "")}} }
        }
        """ : $$"""
        Imports RM = System.Resources.ResourceManager
        <System.CodeDom.Compiler.GeneratedCode("ClassicResources", "1")>
        Public Class UnusuallyNamed
          Private Shared manager As RM = New RM("Renamed", GetType(UnusuallyNamed).Assembly)
          Public Shared ReadOnly Property Save As String
            Get
              Return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture)
            End Get
          End Property
        End Class
        Public Class RenamedScreen
          Inherits System.Windows.Window
          Public Sub Render()
            {{(used ? "Title = " + (literal ? "\"Ready\"" : "UnusuallyNamed.Save") : "")}}
          End Sub
        End Class
        """;
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Aliased_classic_resources_must_be_consumed_not_dummy_constructions(string language)
    {
        AdditionalText[] files = [File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx(value: "Änderungen speichern"))];
        Assert.DoesNotContain(await Analyze(Compile(language, Localized(language)), files), d => d.Id is "LOC001" or "LOC002");
        Assert.Contains(await Analyze(Compile(language, Localized(language, used: false)), files), d => d.Id == "LOC001");
        var literal = await Analyze(Compile(language, Localized(language, literal: true)), files);
        Assert.Contains(literal, d => d.Id == "LOC002");
        Assert.Contains(literal, d => d.Id == "LOC001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Missing_culture_or_key_parity_is_not_localization(string language)
    {
        var code = Compile(language, Localized(language));
        Assert.Contains(await Analyze(code, [File("Renamed.resx", Resx())]), d => d.Id == "LOC001");
        Assert.Contains(await Analyze(code, [File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx("Different"))]), d => d.Id == "LOC001");
        Assert.Contains(await Analyze(code, [File("Renamed.resx", "<root><data name=\"Save\"><value>Save</value></data></root>"),
            File("Renamed.de.resx", Resx())]), d => d.Id == "LOC001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Generated_strong_accessor_and_XAML_static_usage_are_evidence(string language)
    {
        var compile = Compile(language, Localized(language, used: false));
        var tree = compile.SyntaxTrees.Single();
        compile = compile.RemoveAllSyntaxTrees().AddSyntaxTrees(language == LanguageNames.CSharp
            ? CSharpSyntaxTree.ParseText(tree.GetText(), path: "Renamed.Designer.cs")
            : VisualBasicSyntaxTree.ParseText(tree.GetText(), path: "Renamed.Designer.vb"));
        compile = compile.AddSyntaxTrees(language == LanguageNames.CSharp ? CSharpSyntaxTree.ParseText("class Authored {}") :
            VisualBasicSyntaxTree.ParseText("Class Authored\nEnd Class"));
        var xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:any="clr-namespace:">
              <TextBlock Text="{x:Static any:UnusuallyNamed.Save}"/>
            </Window>
            """;
        // Namespace-free generated classes are legal CLR resource accessors.
        Assert.DoesNotContain(await Analyze(compile, [File("Other.xaml", xaml), File("Renamed.resx", Resx()),
            File("Renamed.fr.resx", Resx(value: "Enregistrer les modifications"))]), d => d.Id is "LOC001" or "LOC002");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task MessageBox_and_observable_UI_outputs_are_sinks_but_metadata_logs_are_not(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            using Box = System.Windows.MessageBox;
            public class Model : System.ComponentModel.INotifyPropertyChanged {
              public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
              private string message;
              public string ErrorMessage { get { return message; } }
              private void SetError(string value) { message = value; }
              public void Run() { SetError("Please try again"); Box.Show("Sign in failed", "Authentication");
                System.Console.WriteLine("Log entry"); var id = nameof(ErrorMessage); }
            }
            """ : """
            Imports Box = System.Windows.MessageBox
            Public Class Model
              Implements System.ComponentModel.INotifyPropertyChanged
              Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged
              Private message As String
              Public ReadOnly Property ErrorMessage As String
                Get
                  Return message
                End Get
              End Property
              Private Sub SetError(value As String)
                message = value
              End Sub
              Public Sub Run()
                SetError("Please try again")
                Box.Show("Sign in failed", "Authentication")
                System.Console.WriteLine("Log entry")
                Dim id = NameOf(ErrorMessage)
              End Sub
            End Class
            """;
        var diagnostics = (await Analyze(Compile(language, code))).Where(d => d.Id == "LOC002").ToArray();
        Assert.Equal(3, diagnostics.Length);
        Assert.DoesNotContain(diagnostics, d => d.GetMessage().Contains("Log entry") || d.GetMessage().Contains("ErrorMessage"));
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Presentation_terminology_is_token_aware_and_renaming_is_selected(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            public class MainDataWindow : System.Windows.Window {
              public void OpenMasterData() { Title = "Master Data"; }
              public void MasterfulDatabase() { System.Console.WriteLine("master"); }
            }
            """ : """
            Public Class MainDataWindow
              Inherits System.Windows.Window
              Public Sub OpenMasterData()
                Title = "Master Data"
              End Sub
              Public Sub MasterfulDatabase()
                System.Console.WriteLine("master")
              End Sub
            End Class
            """;
        var compilation = Compile(language, code);
        var selection = new ExternalEvaluation.ScenarioSelection(["MasterDataWindow"], [], [], []);
        Assert.True(selection.Includes(compilation.GetTypeByMetadataName("MainDataWindow")!));
        var diagnostics = (await Analyze(compilation)).Where(d => d.Id == "NAM001").ToArray();
        Assert.Equal(2, diagnostics.Length);
        var corrected = code.Replace("OpenMasterData", "OpenMainData").Replace("Master Data", "Main Data");
        Assert.DoesNotContain(await Analyze(Compile(language, corrected)), d => d.Id == "NAM001");
    }
    [Theory]
    [InlineData("MasterData", true)]
    [InlineData("master-data", true)]
    [InlineData("master data", true)]
    [InlineData("Stammdaten", false)]
    [InlineData("Main Data", false)]
    [InlineData("master", false)]
    [InlineData("MasterfulDatabase", false)]
    public async Task Displayed_resources_and_XAML_use_main_data_or_local_equivalent(string value, bool bad)
    {
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Words.resx", Resx(value: value)), File("Words.de.resx", Resx(value: value)),
             File("Other.xaml", $"""<TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Text="{value}"/>""")]);
        Assert.Equal(bad, found.Any(d => d.Id == "NAM001"));
    }
    [Fact]
    public async Task Historical_comments_and_EF_DTO_contracts_are_not_renaming_targets()
    {
        var compilation = AnalyzerTests.Compile(LanguageNames.CSharp,
            "// Historically called Master Data.\npublic class MasterDataDto { public int MasterDataId {get;set;} }", "TaskOTime.DTOs");
        Assert.DoesNotContain(await Analyze(compilation), d => d.Id == "NAM001");
    }

    private static string Theme(bool bad = false, bool missing = false) => $$"""
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Background="{DynamicResource Surface}" Foreground="{DynamicResource Ink}">
          <Window.Resources>
            <SolidColorBrush x:Key="Surface" Color="{{(bad ? "White" : "#202020")}}"/>
            <SolidColorBrush x:Key="Ink" Color="{{(bad ? "Black" : "#F8F8F8")}}"/>
            {{ButtonStyle("CalendarDayButton", missing)}}
            {{ButtonStyle("CalendarButton", missing)}}
          </Window.Resources>
          <!-- White background and black foreground in a comment do not affect rendering. -->
          <Calendar />
        </Window>
        """;
    private static string ButtonStyle(string type, bool missing) => $$"""
        <Style TargetType="{x:Type {{type}}}">
          <Setter Property="Background" Value="{DynamicResource Surface}"/>
          <Setter Property="Foreground" Value="{DynamicResource Ink}"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="{x:Type {{type}}}">
                <Border Background="{TemplateBinding Background}">
                  <ContentPresenter TextElement.Foreground="{TemplateBinding Foreground}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Surface}"/></Trigger>
                  <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#282828"/></Trigger>
                  {{(missing ? "" : "<Trigger Property=\"IsInactive\" Value=\"True\"><Setter Property=\"Foreground\" Value=\"{DynamicResource Ink}\"/></Trigger>")}}
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="{DynamicResource Ink}"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Calendar_and_main_surfaces_require_dark_contrast_and_state_coverage(bool bad, bool missing)
    {
        var diagnostics = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Screen.xaml", Theme(bad, missing))]);
        Assert.Equal(bad, diagnostics.Any(d => d.Id == "THM001"));
        Assert.Equal(missing, diagnostics.Any(d => d.Id == "THM002"));
    }
    [Fact]
    public async Task Missing_resource_reference_is_unverified_not_a_dark_mode_pass()
    {
        var diagnostics = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Screen.xaml", Theme().Replace("DynamicResource Ink", "DynamicResource Missing"))]);
        Assert.Contains(diagnostics, d => d.Id == "THM002");
    }
    [Fact]
    public async Task Merged_resources_and_local_dark_style_exceptions_resolve_without_names()
    {
        var xaml = Theme().Replace("""<SolidColorBrush x:Key="Surface" Color="#202020"/>""", "")
            .Replace("""<SolidColorBrush x:Key="Ink" Color="#F8F8F8"/>""", "");
        var app = """
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Palettes/OddName.xaml"/>
              </ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources>
            </Application>
            """;
        var resources = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Color x:Key="Tone">#202020</Color><SolidColorBrush x:Key="Surface" Color="{StaticResource Tone}"/>
              <SolidColorBrush x:Key="Ink" Color="#F8F8F8"/>
            </ResourceDictionary>
            """;
        Assert.DoesNotContain(await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Screen.xaml", xaml), File("App.xaml", app), File(@"Palettes\OddName.xaml", resources)]), d => d.Id is "THM001" or "THM002");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task WPF_property_color_operations_are_resolved_with_aliases(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            using Palette = System.Windows.Media.Brushes;
            public class RenamedScreen : System.Windows.Window { public void Render() { Background = Palette.White; } }
            """ : """
            Imports Palette = System.Windows.Media.Brushes
            Public Class RenamedScreen
              Inherits System.Windows.Window
              Public Sub Render()
                Background = Palette.White
              End Sub
            End Class
            """;
        Assert.Contains(await Analyze(Compile(language, code)), d => d.Id == "THM001");
        Assert.DoesNotContain(await Analyze(Compile(language, Plain(language))), d => d.Id == "THM001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Evaluated_EF_Core_dependency_is_diagnosed_without_executing_contexts(string language)
    {
        var code = Compile(language, Plain(language));
        Assert.Contains(await Analyze(code, [File("App.csproj.assessment",
            """<Project><Package Name="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0"/></Project>""")]), d => d.Id == "EF001");
        Assert.DoesNotContain(await Analyze(code, [File("App.csproj.assessment",
            """<Project><Package Name="EntityFramework" Version="6.5.1"/></Project>""")]), d => d.Id == "EF001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Protected_observable_logic_must_not_acquire_concrete_view_dependencies(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            public class RenamedCoordinator : System.ComponentModel.INotifyPropertyChanged {
              public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
              private System.Windows.Window view;
            }
            """ : """
            Public Class RenamedCoordinator
              Implements System.ComponentModel.INotifyPropertyChanged
              Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged
              Private view As System.Windows.Window
            End Class
            """;
        Assert.Contains(await Analyze(Compile(language, code)), d => d.Id == "COR001");
        code = code.Replace("private System.Windows.Window view;", "").Replace("Private view As System.Windows.Window", "");
        Assert.DoesNotContain(await Analyze(Compile(language, code)), d => d.Id == "COR001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Login_documentation_is_anchored_to_authentication_contracts_not_class_names(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            public class Credentials { public string Password {get;set;} }
            public interface IEntryService { bool Enter(Credentials request); }
            /// <summary>Coordinates authenticated user session changes.</summary>
            public class UnexpectedName : System.ComponentModel.INotifyPropertyChanged {
              public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
              private IEntryService service;
              /// <summary>Authenticates credentials before establishing a session.</summary>
              public bool Enter(Credentials request) { return service.Enter(request); }
              /// <summary>Clears the currently authenticated local session.</summary>
              public void Leave() {}
            }
            """ : """
            Public Class Credentials
              Public Property Password As String
            End Class
            Public Interface IEntryService
              Function Enter(request As Credentials) As Boolean
            End Interface
            ''' <summary>Coordinates authenticated user session changes.</summary>
            Public Class UnexpectedName
              Implements System.ComponentModel.INotifyPropertyChanged
              Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged
              Private service As IEntryService
              ''' <summary>Authenticates credentials before establishing a session.</summary>
              Public Function Enter(request As Credentials) As Boolean
                Return service.Enter(request)
              End Function
              ''' <summary>Clears the currently authenticated local session.</summary>
              Public Sub Leave()
              End Sub
            End Class
            """;
        Assert.DoesNotContain(await Analyze(Compile(language, code)), d => d.Id == "ENG002");
        code = code.Replace("Coordinates authenticated user session changes.", "")
            .Replace("Authenticates credentials before establishing a session.", "")
            .Replace("Clears the currently authenticated local session.", "");
        Assert.Contains(await Analyze(Compile(language, code)), d => d.Id == "ENG002");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Real_EF6_symbol_identity_and_service_contract_must_remain(string language)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        var ef = CSharpCompilation.Create("EntityFramework", [CSharpSyntaxTree.ParseText(
            "namespace System.Data.Entity { public class DbContext {} public class DbSet<TEntity> {} }")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(ef.Emit(stream).Success);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var code = language == LanguageNames.CSharp ? """
            using StoreBase = System.Data.Entity.DbContext;
            public class RenamedContext : StoreBase {
              public System.Data.Entity.DbSet<string> Entries { get; set; }
            }
            public class SourceService { private RenamedContext context; }
            """ : """
            Imports StoreBase = System.Data.Entity.DbContext
            Public Class RenamedContext
              Inherits StoreBase
              Public Property Entries As System.Data.Entity.DbSet(Of String)
            End Class
            Public Class SourceService
              Private context As RenamedContext
            End Class
            """;
        Compilation Make(string source) => language == LanguageNames.CSharp
            ? CSharpCompilation.Create("DataAccess", [CSharpSyntaxTree.ParseText(source)], references.Append(reference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            : VisualBasicCompilation.Create("DataAccess", [VisualBasicSyntaxTree.ParseText(source)], references.Append(reference),
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(await Analyze(Make(code), contracts: true), d => d.Id is "EF001" or "ASM001");
        Assert.Contains(await Analyze(Make(code.Replace(": StoreBase", "").Replace("Inherits StoreBase", "")), contracts: true),
            d => d.Id == "EF001");
    }
    [Fact]
    public async Task Duplicate_resx_headers_and_empty_calendar_states_are_not_success()
    {
        var c = Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp));
        Assert.Contains(await Analyze(c, [File("Words.resx", Resx().Replace("</root>",
            """<resheader name="version"><value>2.0</value></resheader></root>"""))]), d => d.Id == "LOC001");
        var xaml = Theme().Replace("""<Trigger Property="IsInactive" Value="True"><Setter Property="Foreground" Value="{DynamicResource Ink}"/></Trigger>""",
            """<Trigger Property="IsInactive" Value="True"/>""");
        Assert.Contains(await Analyze(c, [File("Screen.xaml", xaml)]), d => d.Id == "THM002");
        Assert.Contains(await Analyze(c, [File("Screen.xaml", Theme().Replace("#F8F8F8", "#242424"))]), d => d.Id == "THM001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task UI_value_tracing_excludes_condition_environment_and_culture_metadata(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            public class Model : System.ComponentModel.INotifyPropertyChanged {
              public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
              public string Heading => System.Environment.GetEnvironmentVariable("TASKOTIME_MODE") == "Demo" ? "Demonstration session" : "Live session";
              public string Title => System.DateTime.Today.ToString("D", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            }
            """ : """
            Public Class Model
              Implements System.ComponentModel.INotifyPropertyChanged
              Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged
              Public ReadOnly Property Heading As String
                Get
                  Return If(System.Environment.GetEnvironmentVariable("TASKOTIME_MODE") = "Demo", "Demonstration session", "Live session")
                End Get
              End Property
              Public ReadOnly Property Title As String
                Get
                  Return System.DateTime.Today.ToString("D", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))
                End Get
              End Property
            End Class
            """;
        var found = (await Analyze(Compile(language, code))).Where(d => d.Id == "LOC002").ToArray();
        Assert.Equal(2, found.Length);
        Assert.All(found, d => Assert.Contains("session", d.GetMessage()));
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Application_service_symbols_are_renamed_without_matching_SQL_master(string language)
    {
        var code = language == LanguageNames.CSharp ?
            """public interface IAdminMasterDataService {} public class MasterCatalog { public string Id = "master"; }""" :
            "Public Interface IAdminMasterDataService\nEnd Interface\nPublic Class MasterCatalog\nPublic Id As String = \"master\"\nEnd Class";
        Assert.Single((await Analyze(Compile(language, code))).Where(d => d.Id == "NAM001"));
        Assert.DoesNotContain(await Analyze(Compile(language, code.Replace("MasterData", "MainData"))), d => d.Id == "NAM001");
    }
    [Fact]
    public async Task XAML_text_nodes_and_consumed_string_dictionary_entries_are_presentation_literals()
    {
        var xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib">
              <Window.Resources><s:String x:Key="Greeting">Welcome aboard</s:String></Window.Resources>
              <StackPanel><TextBlock Text="{StaticResource Greeting}"/><Button>Master Data</Button></StackPanel>
            </Window>
            """;
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)), [File("Words.xaml", xaml)]);
        Assert.Equal(2, found.Count(d => d.Id == "LOC002"));
        Assert.Single(found.Where(d => d.Id == "NAM001"));
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task ResourceManager_base_name_must_match_evaluated_manifest_identity(string language)
    {
        var compilation = Compile(language, Localized(language));
        var metadata = """<Project Name="Fixture"><Resource Path="C:\fixtures\Renamed.resx" BaseName="Renamed"/></Project>""";
        AdditionalText[] Inputs(string m) => [File("Project.assessment", m), File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx())];
        AssessmentProject[] corpus = [new(@"C:\fixtures\App.csproj", compilation)];
        Assert.DoesNotContain(await Analyze(compilation, Inputs(metadata), corpus), d => d.Id is "LOC001" or "LOC002");
        Assert.Contains(await Analyze(compilation, Inputs(metadata.Replace("BaseName=\"Renamed\"", "BaseName=\"Unrelated.Renamed\"")), corpus), d => d.Id == "LOC001");
    }
    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Anonymous_types_have_no_terminology_identity_to_rename(string language)
    {
        var code = language == LanguageNames.CSharp ? "public class Plain { public object Make() => new { Value = 1 }; }" :
            "Public Class Plain\nPublic Function Make() As Object\nReturn New With {.Value = 1}\nEnd Function\nEnd Class";
        Assert.DoesNotContain(await Analyze(Compile(language, code)), d => d.Id == "NAM001");
    }

    private static string Getter(string language, string body, bool ui = true)
    {
        var original = language == LanguageNames.CSharp
            ? """return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture);"""
            : """Return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture)""";
        return Localized(language, used: ui).Replace(original, body);
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task An_unused_GetString_call_does_not_localize_a_hardcoded_getter_return(string language)
    {
        var body = language == LanguageNames.CSharp ? """
            var ignored = manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture);
            return "Hard coded untranslated caption";
            """ : """
            Dim ignored = manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture)
            Return "Hard coded untranslated caption"
            """;
        AdditionalText[] resources = [File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx())];
        var found = await Analyze(Compile(language, Getter(language, body)), resources);
        Assert.Contains(found, d => d.Id == "LOC001");
        Assert.Contains(found, d => d.Id == "LOC002" && d.GetMessage().Contains("Hard coded untranslated caption"));
        Assert.DoesNotContain(found, d => d.Id == "LOC002" && d.GetMessage().EndsWith(": Save"));

        var xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:any="clr-namespace:">
              <TextBlock Text="{x:Static any:UnusuallyNamed.Save}"/>
            </Window>
            """;
        var xamlOnly = await Analyze(Compile(language, Getter(language, body, ui: false)),
            [.. resources, File("Consumer.xaml", xaml)]);
        Assert.Contains(xamlOnly, d => d.Id == "LOC001");
        Assert.Contains(xamlOnly, d => d.Id == "LOC002" && d.GetMessage().Contains("Hard coded untranslated caption"));
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Resource_return_branches_and_local_aliases_pass_but_literal_fallbacks_do_not(string language)
    {
        var branches = language == LanguageNames.CSharp ? """
            if (System.Globalization.CultureInfo.CurrentUICulture.Name == "de")
              return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture);
            return manager.GetString("Save", System.Globalization.CultureInfo.InvariantCulture);
            """ : """
            If System.Globalization.CultureInfo.CurrentUICulture.Name = "de" Then
              Return manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture)
            End If
            Return manager.GetString("Save", System.Globalization.CultureInfo.InvariantCulture)
            """;
        var alias = language == LanguageNames.CSharp ? """
            var translated = manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture);
            return translated;
            """ : """
            Dim translated = manager.GetString("Save", System.Globalization.CultureInfo.CurrentUICulture)
            Return translated
            """;
        AdditionalText[] resources = [File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx())];
        foreach (var body in new[] { branches, alias })
            Assert.DoesNotContain(await Analyze(Compile(language, Getter(language, body)), resources), d => d.Id is "LOC001" or "LOC002");
        var mixed = branches.Replace("""manager.GetString("Save", System.Globalization.CultureInfo.InvariantCulture)""", "\"Untranslated fallback\"");
        Assert.Contains(await Analyze(Compile(language, Getter(language, mixed)), resources),
            d => d.Id == "LOC002" && d.GetMessage().Contains("Untranslated fallback"));
    }

    private static string InheritedTheme(string variant)
    {
        var document = System.Xml.Linq.XDocument.Parse(Theme());
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = document.Root!.Elements().Single(e => e.Name.LocalName == "Window.Resources");
        foreach (var style in resources.Elements().Where(e => e.Name.LocalName == "Style").ToArray())
        {
            var target = style.Attribute("TargetType")!.Value;
            var name = "Parent" + resources.Elements().Count();
            style.SetAttributeValue(x + "Key", name);
            var derived = new System.Xml.Linq.XElement(style.Name,
                new System.Xml.Linq.XAttribute("TargetType", target),
                new System.Xml.Linq.XAttribute("BasedOn", "{StaticResource " + name + "}"));
            resources.Add(derived);
            if (variant == "missing")
                foreach (var trigger in style.Descendants().Where(e => e.Attribute("Property")?.Value == "IsInactive").ToArray()) trigger.Remove();
            if (variant == "cycle") style.SetAttributeValue("BasedOn", "{StaticResource " + name + "}");
            if (variant == "override")
                derived.Add(new System.Xml.Linq.XElement(style.Name.Namespace + "Setter",
                    new System.Xml.Linq.XAttribute("Property", "Template"),
                    new System.Xml.Linq.XElement(style.Name.Namespace + "Setter.Value",
                        new System.Xml.Linq.XElement(style.Name.Namespace + "ControlTemplate",
                            new System.Xml.Linq.XAttribute("TargetType", target)))));
            if (variant == "style-triggers")
            {
                var triggers = style.Descendants().Single(e => e.Name.LocalName == "ControlTemplate.Triggers");
                var inherited = new System.Xml.Linq.XElement(style.Name.Namespace + "Style.Triggers", triggers.Elements().ToArray());
                triggers.Remove();
                style.Add(inherited);
            }
        }
        return document.ToString();
    }

    [Theory]
    [InlineData("inherited", 0)]
    [InlineData("style-triggers", 0)]
    [InlineData("missing", 2)]
    [InlineData("override", 8)]
    [InlineData("cycle", 2)]
    public async Task Calendar_styles_inherit_effective_templates_and_triggers_without_reusing_overridden_states(string variant, int unverified)
    {
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Screen.xaml", InheritedTheme(variant))]);
        Assert.DoesNotContain(found, d => d.Id == "THM001");
        Assert.Equal(unverified, found.Count(d => d.Id == "THM002"));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("0.5", true)]
    [InlineData("{DynamicResource Fade}", true)]
    [InlineData("{Binding Fade}", true)]
    [InlineData("1", false)]
    [InlineData("1.0", false)]
    public async Task Resource_brush_opacity_is_not_discarded_during_color_resolution(string opacity, bool unverified)
    {
        var xaml = Theme().Replace("x:Key=\"Ink\" Color=\"#F8F8F8\"",
            $"x:Key=\"Ink\" Color=\"#F8F8F8\" Opacity=\"{opacity}\"");
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)), [File("Screen.xaml", xaml)]);
        Assert.Equal(unverified, found.Any(d => d.Id == "THM002"));
        Assert.DoesNotContain(found, d => d.Id == "THM001");
    }

    [Theory]
    [InlineData("#00F8F8F8", true)]
    [InlineData("#80F8F8F8", true)]
    [InlineData("#FFF8F8F8", false)]
    public async Task Resolved_color_alpha_cannot_be_mistaken_for_opaque_contrast(string color, bool unverified)
    {
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)),
            [File("Screen.xaml", Theme().Replace("#F8F8F8", color))]);
        Assert.Equal(unverified, found.Any(d => d.Id == "THM002"));
    }

    [Theory]
    [InlineData("0.5", true)]
    [InlineData("1", false)]
    public async Task Brush_property_element_opacity_survives_resource_to_color_indirection(string opacity, bool unverified)
    {
        var xaml = Theme().Replace("""<SolidColorBrush x:Key="Ink" Color="#F8F8F8"/>""", $$"""
            <Color x:Key="InkTone">#F8F8F8</Color>
            <SolidColorBrush x:Key="Ink" Color="{StaticResource InkTone}">
              <SolidColorBrush.Opacity>{{opacity}}</SolidColorBrush.Opacity>
            </SolidColorBrush>
            """);
        var found = await Analyze(Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp)), [File("Screen.xaml", xaml)]);
        Assert.Equal(unverified, found.Any(d => d.Id == "THM002"));
    }

    private static InputFile ResourcePolicy(params string[] languages) => File("ResourcePolicy.assessment",
        "<ScenarioScope><Localization NeutralLanguage=\"en\">" +
        string.Join("", languages.Select(language => "<RequiredLanguage>" + language + "</RequiredLanguage>")) +
        "</Localization></ScenarioScope>");

    public static IEnumerable<object[]> ResourceCulturePolicyCases()
    {
        foreach (var language in new[] { LanguageNames.CSharp, LanguageNames.VisualBasic })
        foreach (var item in new[]
        {
            ("de,nl", "de,nl", true),
            ("de,nl", "de-DE,nl-BE", true),
            ("de,nl", "de-DE", false),
            ("de,nl", "nl-NL", false),
            ("de,nl", "de,fr", false),
            ("de,nl", "de-DE,de-AT", false),
            ("fr", "fr-CA", true),
            ("fr", "de,nl", false)
        })
            yield return [language, item.Item1, item.Item2, item.Item3];
    }

    [Theory]
    [MemberData(nameof(ResourceCulturePolicyCases))]
    public async Task External_policy_requires_every_language_family_without_fixing_resource_names(
        string language, string required, string supplied, bool healthy)
    {
        var files = new List<AdditionalText> { ResourcePolicy(required.Split(',')), File("Renamed.resx", Resx()) };
        foreach (var culture in supplied.Split(','))
        {
            var caption = System.Globalization.CultureInfo.GetCultureInfo(culture).TwoLetterISOLanguageName switch
            {
                "de" => "Änderungen speichern", "nl" => "Wijzigingen opslaan", _ => "Enregistrer les modifications"
            };
            files.Add(File("Renamed." + culture + ".resx", Resx(value: caption)));
        }
        var found = await Analyze(Compile(language, Localized(language)), files.ToArray());
        Assert.Equal(!healthy, found.Any(d => d.Id == "LOC001"));
        Assert.DoesNotContain(found, d => d.Id == "LOC002");
        if (!healthy)
        {
            Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("missing required language cultures"));
            Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().StartsWith("No UI-consumed", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Required_culture_must_have_key_parity_and_invalid_policy_does_not_fall_back_to_any_culture(string language)
    {
        var compilation = Compile(language, Localized(language));
        var found = await Analyze(compilation, [ResourcePolicy("de", "nl"), File("Renamed.resx", Resx()),
            File("Renamed.de.resx", Resx()), File("Renamed.nl.resx", Resx("Different"))]);
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("missing required language cultures") && d.GetMessage().Contains("nl"));
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().StartsWith("No UI-consumed", StringComparison.Ordinal));
        found = await Analyze(compilation, [ResourcePolicy("not-a-culture"), File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx())]);
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("Unrecognized required resource language"));
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().StartsWith("No UI-consumed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(LanguageNames.CSharp, "en-GB", false)]
    [InlineData(LanguageNames.CSharp, "de", true)]
    [InlineData(LanguageNames.VisualBasic, "en-GB", false)]
    [InlineData(LanguageNames.VisualBasic, "de", true)]
    public async Task Declared_neutral_language_must_not_contradict_the_external_English_policy(string language, string declared, bool bad)
    {
        var source = Localized(language);
        var import = language == LanguageNames.CSharp ? "using RM = System.Resources.ResourceManager;" : "Imports RM = System.Resources.ResourceManager";
        var attribute = language == LanguageNames.CSharp
            ? $"[assembly: System.Resources.NeutralResourcesLanguage(\"{declared}\")]"
            : $"<Assembly: System.Resources.NeutralResourcesLanguage(\"{declared}\")>";
        source = source.Replace(import, import + "\n" + attribute);
        var found = await Analyze(Compile(language, source), [ResourcePolicy("de", "nl"), File("Renamed.resx", Resx()),
            File("Renamed.de.resx", Resx(value: "Änderungen speichern")), File("Renamed.nl.resx", Resx(value: "Wijzigingen opslaan"))]);
        Assert.Equal(bad, found.Any(d => d.Id == "LOC001"));
        if (bad) Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("neutral resource language conflicts"));
    }

    [Fact]
    public async Task Repository_contract_requires_sdk_style_and_net10_independently()
    {
        var compilation = Compile(LanguageNames.CSharp, Plain(LanguageNames.CSharp));
        AssessmentProject[] corpus = [new(@"C:\fixtures\App.csproj", compilation)];
        InputFile Metadata(bool sdk, string framework) => File("App.csproj.assessment",
            $"""<Project Path="C:\fixtures\App.csproj" Name="Fixture" SdkStyle="{sdk.ToString().ToLowerInvariant()}" TargetFramework="{framework}"/>""");
        var healthy = await Analyze(compilation, [Metadata(true, "net10.0-windows")], corpus, contracts: true);
        Assert.DoesNotContain(healthy, d => d.Id is "PRJ001" or "PRJ002");
        var legacy = await Analyze(compilation, [Metadata(false, "net472")], corpus, contracts: true);
        Assert.Contains(legacy, d => d.Id == "PRJ001");
        Assert.Contains(legacy, d => d.Id == "PRJ002");
    }

    [Fact]
    public async Task Namespace_only_localizer_stub_and_disconnected_options_do_not_satisfy_strict_policy()
    {
        var source = Localized(LanguageNames.CSharp) + """
            namespace Microsoft.Extensions.Localization {
              public interface IStringLocalizer { string this[string name] { get; } }
            }
            public class LoginExperience : System.Windows.Window {
              public void Render(Microsoft.Extensions.Localization.IStringLocalizer l) { Title = l["Save"]; }
            }
            public class MainWindow : System.Windows.Window {
              public void Render(Microsoft.Extensions.Localization.IStringLocalizer l) { Title = l["Save"]; }
            }
            public class TimeEntryEditDialog : System.Windows.Window {
              public void Render(Microsoft.Extensions.Localization.IStringLocalizer l) { Title = l["Save"]; }
            }
            public class ProjectView : System.Windows.Window {
              public void Render(Microsoft.Extensions.Localization.IStringLocalizer l) { Title = l["Save"]; }
            }
            public class OptionsDialog {
              public string SelectedLanguage { get; set; }
              public void Apply(System.Globalization.CultureInfo culture) {
                System.Globalization.CultureInfo.CurrentUICulture = culture;
              }
            }
            """;
        var policy = File("Scenario.assessment", """
            <ScenarioScope><Localization NeutralLanguage="en">
              <RequiredLanguage>de</RequiredLanguage><RequiredLanguage>nl</RequiredLanguage><RequiredLanguage>es</RequiredLanguage>
              <Surface Id="Login">Login</Surface><Surface Id="TimeCollection">MainWindow</Surface>
              <Surface Id="Booking">TimeEntryEdit</Surface><Surface Id="ProjectMainData">ProjectView</Surface>
              <OptionsSurface>Options</OptionsSurface>
            </Localization></ScenarioScope>
            """);
        var options = File("OptionsDialog.xaml", """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <ComboBox SelectedValue="{Binding SelectedLanguage}"/>
            </Window>
            """);
        AdditionalText[] files = [policy, options,
            File("Fixture.csproj.assessment", """<Project Name="Fixture"><Package Name="Microsoft.Extensions.Localization" Version="10.0.0"/></Project>"""),
            File("Renamed.resx", Resx()), File("Renamed.de.resx", Resx(value: "Änderungen speichern")),
            File("Renamed.nl.resx", Resx(value: "Wijzigingen opslaan")), File("Renamed.es.resx", Resx(value: "Guardar cambios"))];
        var found = await Analyze(Compile(LanguageNames.CSharp, source), files);
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("meaningfully applied"));
        Assert.Contains(found, d => d.Id == "LOC001" && d.GetMessage().Contains("Options must"));
    }
}
