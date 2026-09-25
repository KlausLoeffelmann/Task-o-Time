using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class AnalyzerTests
{
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(p => MetadataReference.CreateFromFile(p)).ToArray();

    private const string Domain = """
        namespace TaskOTime.AppServer.Models {
          public class ProjectMainDataDto { public System.Guid IdProject { get; set; } }
          public class TimeBookingItemDto { public System.Guid IdProject { get; set; } }
        }
        namespace System.Windows {
          public class DependencyObject {}
          public class Window : DependencyObject {
            public string Title { get; set; }
            public System.Windows.Media.Brush Background { get; set; }
            public System.Windows.Media.Brush Foreground { get; set; }
          }
          public static class MessageBox { public static void Show(string text, string caption) {} }
        }
        namespace System.Windows.Controls {
          public class TextBlock : System.Windows.DependencyObject { public string Text { get; set; } }
          public class UserControl : System.Windows.DependencyObject {
            public object DataContext { get; set; }
            public event System.EventHandler Loaded;
            public void Raise() => Loaded?.Invoke(this, System.EventArgs.Empty);
          }
        }
        namespace System.Windows.Media {
          public class Brush {}
          public static class Brushes { public static Brush Red => new Brush(); public static Brush White => new Brush(); public static Brush Black => new Brush(); }
          public class FontFamily { public FontFamily(string value) {} }
        }
        """;

    private static readonly MetadataReference DomainReference = CreateDomain();
    private static MetadataReference CreateDomain()
    {
        var compilation = CSharpCompilation.Create("FixtureDomain", [CSharpSyntaxTree.ParseText(Domain)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static Compilation Compile(string language, string code, string assemblyName = "Fixture")
    {
        var references = References.Append(DomainReference);
        Compilation compilation = language == LanguageNames.CSharp
            ? CSharpCompilation.Create(assemblyName, [CSharpSyntaxTree.ParseText(code, path: "Fixture.cs")], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            : VisualBasicCompilation.Create(assemblyName, [VisualBasicSyntaxTree.ParseText(code, path: "Fixture.vb")], references,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        return compilation;
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string language, string code) =>
        await Compile(language, code).WithAnalyzers([new ModernizationAnalyzer()]).GetAnalyzerDiagnosticsAsync();

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task External_scope_separates_core_and_master_data_in_one_compilation(string language)
    {
        var code = language == LanguageNames.CSharp ? """
            public class ProtectedCore : System.Windows.Controls.UserControl {
              public System.DateTime Elapsed(System.DateTime start, System.TimeSpan duration) =>
                start.AddMinutes(duration.Minutes);
            }
            public class RelocatedScreen : System.Windows.Controls.UserControl {}
            public class RenamedLogic { public RelocatedScreen View; }
            """ : """
            Public Class ProtectedCore
              Inherits System.Windows.Controls.UserControl
              Public Function Elapsed(start As System.DateTime, duration As System.TimeSpan) As System.DateTime
                Return start.AddMinutes(duration.Minutes)
              End Function
            End Class
            Public Class RelocatedScreen
              Inherits System.Windows.Controls.UserControl
            End Class
            Public Class RenamedLogic
              Public View As RelocatedScreen
            End Class
            """;
        var selection = new ExternalEvaluation.ScenarioSelection(
            ["RelocatedScreen", "RenamedLogic"], ["ProtectedCore"], [], []);
        var compilation = Compile(language, code);
        var found = await compilation.WithAnalyzers([new ModernizationAnalyzer(selection.Includes)])
            .GetAnalyzerDiagnosticsAsync();
        Assert.Single(found.Where(d => d.Id == "BUS002"));
        Assert.Contains(found, d => d.Id == "MOD002");
        Assert.DoesNotContain(found.Where(d => d.Id.StartsWith("MOD", StringComparison.Ordinal)),
            d => d.GetMessage().Contains("ProtectedCore", StringComparison.Ordinal));
        Assert.Equal(language == LanguageNames.VisualBasic ? 2 : 0, found.Count(d => d.Id == "MOD006"));
    }

    [Fact]
    public async Task Source_selection_does_not_make_a_correct_type_a_diagnostic()
    {
        var selection = new ExternalEvaluation.ScenarioSelection([], [], ["Fixture.cs"], []);
        var compilation = Compile(LanguageNames.CSharp, "public class RenamedPlainModel { public int Value { get; set; } }");
        var found = await compilation.WithAnalyzers([new ModernizationAnalyzer(selection.Includes)])
            .GetAnalyzerDiagnosticsAsync();
        Assert.Empty(found);
    }

    [Fact]
    public void Protected_type_overrides_shared_source_selection()
    {
        var compilation = Compile(LanguageNames.CSharp, "public class Core { public class Nested {} } public class Target {}");
        var selection = new ExternalEvaluation.ScenarioSelection([], ["Core"], ["Fixture.cs"], []);
        Assert.False(selection.Includes(compilation.GetTypeByMetadataName("Core")!));
        Assert.False(selection.Includes(compilation.GetTypeByMetadataName("Core+Nested")!));
        Assert.True(selection.Includes(compilation.GetTypeByMetadataName("Target")!));
    }

    private static string Business(string language, string body, string helpers = "") => language == LanguageNames.CSharp
        ? $$"""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using P = TaskOTime.AppServer.Models.ProjectMainDataDto;
            using B = TaskOTime.AppServer.Models.TimeBookingItemDto;
            public class RenamedWorkflow {
              public void Execute(List<P> candidates, P selected, TimeSpan elapsed, DateTime start) {
                {{body}}
              }
              {{helpers}}
            }
            """
        : $$"""
            Imports System
            Imports System.Linq
            Imports System.Collections.Generic
            Imports P = TaskOTime.AppServer.Models.ProjectMainDataDto
            Imports B = TaskOTime.AppServer.Models.TimeBookingItemDto
            Public Class RenamedWorkflow
              Public Sub Execute(candidates As List(Of P), selected As P, elapsed As TimeSpan, start As DateTime)
                {{body}}
              End Sub
              {{helpers}}
            End Class
            """;

    public static IEnumerable<object[]> BusinessCases()
    {
        yield return ["var p = candidates.First(); var b = new B { IdProject = p.IdProject };",
            "Dim p = candidates.First()\nDim b = New B With {.IdProject = p.IdProject}", "BUS001"];
        yield return ["var p = candidates[0]; var id = p.IdProject; var b = new B(); b.IdProject = id;",
            "Dim p = candidates(0)\nDim id = p.IdProject\nDim b = New B()\nb.IdProject = id", "BUS001"];
        yield return ["var b = new B { IdProject = candidates.FirstOrDefault().IdProject };",
            "Dim b = New B With {.IdProject = candidates.FirstOrDefault().IdProject}", "BUS001"];
        yield return ["var b = new B { IdProject = candidates.ElementAt(0).IdProject };",
            "Dim b = New B With {.IdProject = candidates.ElementAt(0).IdProject}", "BUS001"];
        yield return ["var x = start.AddMinutes(elapsed.Minutes);",
            "Dim x = start.AddMinutes(elapsed.Minutes)", "BUS002"];
        yield return ["var minutes = elapsed.Minutes; var copy = (double)minutes; var x = start.AddMinutes(copy);",
            "Dim minutes = elapsed.Minutes\nDim copy = CDbl(minutes)\nDim x = start.AddMinutes(copy)", "BUS002"];
        yield return ["var b = new B { IdProject = selected.IdProject };",
            "Dim b = New B With {.IdProject = selected.IdProject}", ""];
        yield return ["var x = candidates.First(); Console.WriteLine(x.IdProject); var b = new B { IdProject = selected.IdProject };",
            "Dim x = candidates.First()\nConsole.WriteLine(x.IdProject)\nDim b = New B With {.IdProject = selected.IdProject}", ""];
        yield return ["var b = new B { IdProject = candidates.First(p => p.IdProject == selected.IdProject).IdProject };",
            "Dim b = New B With {.IdProject = candidates.First(Function(p) p.IdProject = selected.IdProject).IdProject}", ""];
        yield return ["var chosen = candidates.Where(p => p.IdProject == selected.IdProject); var b = new B { IdProject = chosen.First().IdProject };",
            "Dim chosen = candidates.Where(Function(p) p.IdProject = selected.IdProject)\nDim b = New B With {.IdProject = chosen.First().IdProject}", ""];
        yield return ["var x = start.AddMinutes(elapsed.TotalMinutes); Console.WriteLine(elapsed.Minutes);",
            "Dim x = start.AddMinutes(elapsed.TotalMinutes)\nConsole.WriteLine(elapsed.Minutes)", ""];
        yield return ["var x = start.Date.AddHours(elapsed.Hours).AddMinutes(elapsed.Minutes);",
            "Dim x = start.Date.AddHours(elapsed.Hours).AddMinutes(elapsed.Minutes)", ""];
        yield return ["var h = elapsed.Hours; var m = elapsed.Minutes; var x = start.Date.AddHours(h).AddMinutes(m);",
            "Dim h = elapsed.Hours\nDim m = elapsed.Minutes\nDim x = start.Date.AddHours(h).AddMinutes(m)", ""];
        yield return ["var hours = start.Date.AddHours(elapsed.Hours); var result = hours.AddMinutes(elapsed.Minutes);",
            "Dim hours = start.Date.AddHours(elapsed.Hours)\nDim result = hours.AddMinutes(elapsed.Minutes)", ""];
        yield return ["var hours = start.Date.AddHours(elapsed.Hours); var alias = hours; var result = alias.AddMinutes(elapsed.Minutes);",
            "Dim hours = start.Date.AddHours(elapsed.Hours)\nDim aliasValue = hours\nDim result = aliasValue.AddMinutes(elapsed.Minutes)", ""];
        yield return ["var hours = start.Date.AddHours(elapsed.Hours); hours = start; var result = hours.AddMinutes(elapsed.Minutes);",
            "Dim hours = start.Date.AddHours(elapsed.Hours)\nhours = start\nDim result = hours.AddMinutes(elapsed.Minutes)", "BUS002"];
        yield return ["var hours = start; hours = start.Date.AddHours(elapsed.Hours); var result = hours.AddMinutes(elapsed.Minutes);",
            "Dim hours = start\nhours = start.Date.AddHours(elapsed.Hours)\nDim result = hours.AddMinutes(elapsed.Minutes)", ""];
        yield return ["var hours = start.Date.AddHours(elapsed.Hours); if (start.Year > 2020) hours = start; var result = hours.AddMinutes(elapsed.Minutes);",
            "Dim hours = start.Date.AddHours(elapsed.Hours)\nIf start.Year > 2020 Then\nhours = start\nEnd If\nDim result = hours.AddMinutes(elapsed.Minutes)", ""];
        yield return ["double minutes = elapsed.Minutes; minutes += elapsed.Hours * 60; var result = start.AddMinutes(minutes);",
            "Dim minutes As Double = elapsed.Minutes\nminutes += elapsed.Hours * 60\nDim result = start.AddMinutes(minutes)", ""];
        yield return ["double minutes = elapsed.Minutes; minutes += elapsed.Hours * 60; minutes = elapsed.Minutes; var result = start.AddMinutes(minutes);",
            "Dim minutes As Double = elapsed.Minutes\nminutes += elapsed.Hours * 60\nminutes = elapsed.Minutes\nDim result = start.AddMinutes(minutes)", "BUS002"];
        yield return ["double minutes = elapsed.Minutes; if (start.Year > 2020) minutes += elapsed.Hours * 60; var result = start.AddMinutes(minutes);",
            "Dim minutes As Double = elapsed.Minutes\nIf start.Year > 2020 Then\nminutes += elapsed.Hours * 60\nEnd If\nDim result = start.AddMinutes(minutes)", ""];
        yield return ["var pair = (elapsed.Minutes, 0); var alias = pair; var result = start.AddMinutes(alias.Item1);",
            "Dim pair = (elapsed.Minutes, 0)\nDim aliasValue = pair\nDim result = start.AddMinutes(aliasValue.Item1)", "BUS002"];
        yield return ["var p = candidates.First(); p = selected; var b = new B { IdProject = p.IdProject };",
            "Dim p = candidates.First()\np = selected\nDim b = New B With {.IdProject = p.IdProject}", ""];
        yield return ["var p = selected; if (start.Year > 2020) p = candidates.First(); var b = new B { IdProject = p.IdProject };",
            "Dim p = selected\nIf start.Year > 2020 Then\np = candidates.First()\nEnd If\nDim b = New B With {.IdProject = p.IdProject}", ""];
    }

    [Theory]
    [MemberData(nameof(BusinessCases))]
    public async Task Business_contracts_are_semantic_in_both_languages(string csharp, string vb, string expected)
    {
        foreach (var (language, body) in new[] { (LanguageNames.CSharp, csharp), (LanguageNames.VisualBasic, vb) })
        {
            var actual = await Analyze(language, Business(language, body));
            Assert.DoesNotContain(actual, d => d.Id == "AD0001");
            Assert.Equal(expected.Length == 0 ? [] : new[] { expected }, actual.Select(d => d.Id).ToArray());
        }
    }

    [Theory]
    [InlineData("var (minutes, unused) = (elapsed.Minutes, 0); start.AddMinutes(minutes);", true)]
    [InlineData("double minutes = 0; (minutes, _) = (elapsed.Minutes, 0); start.AddMinutes(minutes);", true)]
    [InlineData("var pair = (elapsed.Minutes, 0); var alias = pair; var (minutes, _) = alias; start.AddMinutes(minutes);", true)]
    [InlineData("var pair = (0, 0); pair = (elapsed.Minutes, 0); var (minutes, _) = pair; start.AddMinutes(minutes);", true)]
    [InlineData("var pair = (elapsed.Minutes, 0); pair = (0, 0); var (minutes, _) = pair; start.AddMinutes(minutes);", false)]
    [InlineData("double minutes = elapsed.Minutes; (minutes, _) = (elapsed.TotalMinutes, 0); start.AddMinutes(minutes);", false)]
    [InlineData("var (unused, (minutes, _)) = (0, (elapsed.Minutes, 0)); start.AddMinutes(minutes);", true)]
    [InlineData("var pair = (0, elapsed.Minutes); var (minutes, _) = pair; start.AddMinutes(minutes);", false)]
    [InlineData("var pair = (elapsed.Minutes, 0); var alias = pair; pair = (0, 0); var (minutes, _) = alias; start.AddMinutes(minutes);", true)]
    [InlineData("var pair = (part: elapsed.Minutes, unused: 0); start.AddMinutes(pair.part);", true)]
    [InlineData("var pair = (elapsed.Minutes, 0); pair.Item1 = 0; start.AddMinutes(pair.Item1);", false)]
    [InlineData("var pair = (elapsed.Minutes, 0); (pair.Item1, _) = (0, 0); start.AddMinutes(pair.Item1);", false)]
    public async Task CSharp_deconstruction_preserves_element_origins(string body, bool expected)
    {
        var diagnostics = await Analyze(LanguageNames.CSharp, Business(LanguageNames.CSharp, body));
        Assert.Equal(expected ? new[] { "BUS002" } : [], diagnostics.Select(d => d.Id).ToArray());
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Ordinary_project_fields_are_not_tuple_symbols(string language)
    {
        var body = language == LanguageNames.CSharp
            ? "available = candidates; var booking = new B { IdProject = available.First().IdProject };"
            : "available = candidates\nDim booking = New B With {.IdProject = available.First().IdProject}";
        var field = language == LanguageNames.CSharp
            ? "private List<P> available;"
            : "Private available As List(Of P)";
        var diagnostics = await Analyze(language, Business(language, body, field));
        Assert.Equal(new[] { "BUS001" }, diagnostics.Select(d => d.Id).ToArray());
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Renamed_helpers_preserve_provenance(string language)
    {
        var body = language == LanguageNames.CSharp
            ? "var b = new B { IdProject = Identity(Choose(candidates)) }; var x = start.AddMinutes(Part(elapsed));"
            : "Dim b = New B With {.IdProject = Identity(Choose(candidates))}\nDim x = start.AddMinutes(Part(elapsed))";
        var helpers = language == LanguageNames.CSharp
            ? "static P Choose(List<P> values) => values.First(); static Guid Identity(P p) => p.IdProject; static double Part(TimeSpan t) => t.Minutes;"
            : "Private Shared Function Choose(values As List(Of P)) As P\nReturn values.First()\nEnd Function\n" +
              "Private Shared Function Identity(p As P) As Guid\nReturn p.IdProject\nEnd Function\n" +
              "Private Shared Function Part(t As TimeSpan) As Double\nReturn t.Minutes\nEnd Function";
        var diagnostics = await Analyze(language, Business(language, body, helpers));
        Assert.Equal(new[] { "BUS001", "BUS002" }, diagnostics.Select(d => d.Id).Order().ToArray());
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Alias_inheritance_members_events_styles_and_language(string language)
    {
        var source = language == LanguageNames.CSharp
            ? """
              using V = System.Windows.Controls.UserControl;
              [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Production.Presentation")]
              [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Verification.Tests")]
              public class RenamedPanel : V {}
              public class Parent {
                protected RenamedPanel panel;
                public Parent(RenamedPanel value) { panel = value; }
              }
              public class RenamedLogic : Parent {
                public RenamedLogic(RenamedPanel value) : base(value) {
                  panel.DataContext = this;
                  panel.Loaded += Changed;
                  var brush = System.Windows.Media.Brushes.Red;
                  var font = new System.Windows.Media.FontFamily("Arial");
                }
                void Changed(object sender, System.EventArgs e) {}
              }
              """
            : """
              Imports V = System.Windows.Controls.UserControl
              <Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Production.Presentation")>
              <Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Verification.Tests")>
              Public Class RenamedPanel
                Inherits V
              End Class
              Public Class Parent
                Protected panel As RenamedPanel
                Public Sub New(value As RenamedPanel)
                  panel = value
                End Sub
              End Class
              Public Class RenamedLogic
                Inherits Parent
                Public Sub New(value As RenamedPanel)
                  MyBase.New(value)
                  panel.DataContext = Me
                  AddHandler panel.Loaded, AddressOf Changed
                  Dim brush = System.Windows.Media.Brushes.Red
                  Dim font = New System.Windows.Media.FontFamily("Arial")
                End Sub
                Private Sub Changed(sender As Object, e As System.EventArgs)
                End Sub
              End Class
              """;
        var diagnostics = await Analyze(language, source);
        foreach (var id in new[] { "MOD001", "MOD002", "MOD003", "MOD004", "MOD005" })
            Assert.Contains(diagnostics, d => d.Id == id);
        Assert.Equal(language == LanguageNames.VisualBasic, diagnostics.Any(d => d.Id == "MOD006"));
        Assert.Single(diagnostics.Where(d => d.Id == "MOD003"));
        Assert.Contains(diagnostics, d => d.Id == "MOD004" && d.GetMessage().Contains("RenamedLogic"));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Observable_view_independent_models_are_healthy(string language)
    {
        var source = language == LanguageNames.CSharp
            ? """
              using System.ComponentModel;
              public abstract class Healthy : INotifyPropertyChanged {
                public event PropertyChangedEventHandler PropertyChanged;
                public string Name { get; set; }
              }
              public class Child : Healthy {}
              """
            : """
              Imports System.ComponentModel
              Public MustInherit Class Healthy
                Implements INotifyPropertyChanged
                Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
                Public Property Name As String
              End Class
              Public Class Child
                Inherits Healthy
              End Class
              """;
        Assert.Empty(await Analyze(language, source));
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Window_views_follow_project_role_not_a_blanket_type_exclusion(string language)
    {
        var source = language == LanguageNames.CSharp
            ? """
              using W = System.Windows.Window;
              [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Presentation.Logic")]
              public class RenamedDialog : W {
                object brush = System.Windows.Media.Brushes.Red;
                object font = new System.Windows.Media.FontFamily("Arial");
              }
              public class DerivedDialog : RenamedDialog {}
              public class Coordinator { public DerivedDialog View { get; set; } }
              """
            : """
              Imports W = System.Windows.Window
              <Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Presentation.Logic")>
              Public Class RenamedDialog
                Inherits W
                Private brush As Object = System.Windows.Media.Brushes.Red
                Private font As Object = New System.Windows.Media.FontFamily("Arial")
              End Class
              Public Class DerivedDialog
                Inherits RenamedDialog
              End Class
              Public Class Coordinator
                Public Property View As DerivedDialog
              End Class
              """;
        var compilation = Compile(language, source);
        var masterData = await compilation.WithAnalyzers([new ModernizationAnalyzer(architectureEnabled: true)])
            .GetAnalyzerDiagnosticsAsync();
        foreach (var id in new[] { "MOD001", "MOD002", "MOD003", "MOD004", "MOD005" })
            Assert.Contains(masterData, d => d.Id == id);
        Assert.Equal(language == LanguageNames.VisualBasic ? 3 : 0, masterData.Count(d => d.Id == "MOD006"));
        Assert.DoesNotContain(masterData, d => d.Id == "AD0001");
        var recordingCore = await compilation.WithAnalyzers([new ModernizationAnalyzer(architectureEnabled: false)])
            .GetAnalyzerDiagnosticsAsync();
        Assert.Empty(recordingCore);
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Local_only_view_dependencies_and_observable_inheritance_are_not_name_based(string language)
    {
        var source = language == LanguageNames.CSharp
            ? """
              using System.ComponentModel;
              public abstract class ObservableToolkitBase : INotifyPropertyChanged {
                public event PropertyChangedEventHandler PropertyChanged;
              }
              public class RenamedCoordinator : ObservableToolkitBase {
                public void Execute() {
                  var screen = new System.Windows.Controls.UserControl();
                  screen.DataContext = this;
                }
              }
              """
            : """
              Imports System.ComponentModel
              Public MustInherit Class ObservableToolkitBase
                Implements INotifyPropertyChanged
                Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
              End Class
              Public Class RenamedCoordinator
                Inherits ObservableToolkitBase
                Public Sub Execute()
                  Dim screen = New System.Windows.Controls.UserControl()
                  screen.DataContext = Me
                End Sub
              End Class
              """;
        var diagnostics = await Analyze(language, source);
        Assert.Contains(diagnostics, d => d.Id == "MOD001");
        Assert.Contains(diagnostics, d => d.Id == "MOD004");
        Assert.DoesNotContain(diagnostics, d => d.Id is "MOD002" or "AD0001");
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Lookalike_member_names_are_not_business_evidence(string language)
    {
        var source = language == LanguageNames.CSharp
            ? """
              using System;
              using System.Linq;
              using P = TaskOTime.AppServer.Models.ProjectMainDataDto;
              using B = TaskOTime.AppServer.Models.TimeBookingItemDto;
              public class Clock { public int Minutes => 10; }
              public class Selection { public P First() => new P(); }
              public class Flow {
                void Run(Clock duration, Selection current, DateTime start) {
                  var end = start.AddMinutes(duration.Minutes);
                  var booking = new B { IdProject = current.First().IdProject };
                  var any = new[] { 1, 2 }.First();
                }
              }
              """
            : """
              Imports System
              Imports System.Linq
              Imports P = TaskOTime.AppServer.Models.ProjectMainDataDto
              Imports B = TaskOTime.AppServer.Models.TimeBookingItemDto
              Public Class Clock
                Public ReadOnly Property Minutes As Integer = 10
              End Class
              Public Class Selection
                Public Function First() As P
                  Return New P()
                End Function
              End Class
              Public Class Flow
                Private Sub Run(duration As Clock, current As Selection, start As DateTime)
                  Dim finish = start.AddMinutes(duration.Minutes)
                  Dim booking = New B With {.IdProject = current.First().IdProject}
                  Dim any = {1, 2}.First()
                End Sub
              End Class
              """;
        Assert.Empty(await Analyze(language, source));
    }

    [Theory]
    [InlineData(LanguageNames.CSharp, "class Broken { MissingType Value; }")]
    [InlineData(LanguageNames.VisualBasic, "Public Class Broken\nPublic Value As MissingType\nEnd Class")]
    public void Fixture_verifier_rejects_unresolved_compilations(string language, string source)
    {
        var failure = Record.Exception(() => Compile(language, source));
        Assert.NotNull(failure);
        Assert.Contains("MissingType", failure.Message);
    }

    [Theory]
    [InlineData(LanguageNames.CSharp)]
    [InlineData(LanguageNames.VisualBasic)]
    public async Task Recording_core_scope_preserves_business_rules_but_excludes_architecture(string language)
    {
        var body = language == LanguageNames.CSharp
            ? "var b = new B { IdProject = candidates.First().IdProject }; var end = start.AddMinutes(elapsed.Minutes);"
            : "Dim b = New B With {.IdProject = candidates.First().IdProject}\nDim finish = start.AddMinutes(elapsed.Minutes)";
        var panel = language == LanguageNames.CSharp
            ? """
              public class Panel : System.Windows.Controls.UserControl {}
              public class CoreWindow : System.Windows.Window {
                object brush = System.Windows.Media.Brushes.Red;
              }
              public class Coupled {
                public Panel View { get; set; }
                public void Run() { View.DataContext = this; var brush = System.Windows.Media.Brushes.Red; }
              }
              """
            : """
              Public Class Panel
                Inherits System.Windows.Controls.UserControl
              End Class
              Public Class CoreWindow
                Inherits System.Windows.Window
                Private brush As Object = System.Windows.Media.Brushes.Red
              End Class
              Public Class Coupled
                Public Property View As Panel
                Public Sub Run()
                  View.DataContext = Me
                  Dim brush = System.Windows.Media.Brushes.Red
                End Sub
              End Class
              """;
        var compilation = Compile(language, Business(language, body) + Environment.NewLine + panel);
        var protectedDiagnostics = await compilation.WithAnalyzers([new ModernizationAnalyzer(architectureEnabled: false)])
            .GetAnalyzerDiagnosticsAsync();
        Assert.Equal(new[] { "BUS001", "BUS002" }, protectedDiagnostics.Select(d => d.Id).Order().ToArray());
        var masterDataDiagnostics = await compilation.WithAnalyzers([new ModernizationAnalyzer(architectureEnabled: true)])
            .GetAnalyzerDiagnosticsAsync();
        Assert.Contains(masterDataDiagnostics, d => d.Id == "MOD001");
        Assert.Contains(masterDataDiagnostics, d => d.Id == "MOD002");
        Assert.Contains(masterDataDiagnostics, d => d.Id == "MOD004");
        Assert.Contains(masterDataDiagnostics, d => d.Id == "MOD005");
    }
}
