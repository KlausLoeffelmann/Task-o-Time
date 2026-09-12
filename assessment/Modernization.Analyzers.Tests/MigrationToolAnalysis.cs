using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Modernization.Analyzers.Tests;

internal static class MigrationToolAnalysis
{
    internal static void Run(AssessmentProject[] tooling, ImmutableArray<AdditionalText> files, Recorder r,
        IReadOnlyList<AssessmentProject>? fixtures = null)
    {
        var pipelines = 0;
        foreach (var project in tooling)
        {
            var operations = ToolOperations(project);
            pipelines += ExternalAdapterPipelines(project, operations, r);
            var producers = new Producers(project.Compilation, operations);
            var guards = operations.OfType<IConditionalOperation>().ToArray();
            foreach (var emission in operations.Where(o => o is IReturnOperation { ReturnedValue.Type.SpecialType: SpecialType.System_String } ||
                o is IInvocationOperation i && i.TargetMethod.ContainingType.ToDisplayString() == "System.IO.File" &&
                    i.TargetMethod.Name == "WriteAllText"))
            {
                var values = producers.Of(emission).ToArray();
                var calls = values.OfType<IInvocationOperation>().ToArray();
                var owner = Evidence.Model(project.Compilation, emission.Syntax.SyntaxTree).GetEnclosingSymbol(emission.Syntax.SpanStart);
                var checkedCalls = guards.Where(g => Precedes(g, emission) &&
                        SymbolEqualityComparer.Default.Equals(owner,
                            Evidence.Model(project.Compilation, g.Syntax.SyntaxTree).GetEnclosingSymbol(g.Syntax.SpanStart)))
                    .Select(g => CheckedDiagnostics(g, operations)).OfType<IInvocationOperation>().ToArray();
                bool SharedSyntax(IInvocationOperation i, Func<IInvocationOperation, bool> predicate) =>
                    predicate(i) && calls.Any(c => c.Syntax.SyntaxTree == i.Syntax.SyntaxTree && c.Syntax.Span == i.Syntax.Span);
                var checkedReceivers = checkedCalls.Where(i => i.Instance != null)
                    .Select(i => SingleDefinition(i.Instance!, operations, 0)).OfType<IInvocationOperation>().ToArray();
                var vbChecked = checkedReceivers.Any(v => SharedSyntax(v, IsVBCompilation));
                var csChecked = checkedReceivers.Any(v =>
                    v.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.CSharp.CSharpCompilation" && v.TargetMethod.Name == "Create" &&
                    producers.Of(v).OfType<IInvocationOperation>().Any(factory => SharedSyntax(factory, a =>
                        a.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.CSharp.SyntaxFactory")));
                var parse = calls.Any(i => i.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree" &&
                    i.TargetMethod.Name == "ParseText" && i.Arguments.Any(a => producers.Of(a.Value).Any(v => v is IParameterReferenceOperation)));
                var semantic = calls.Any(i => i.TargetMethod.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) &&
                    i.TargetMethod.Name is "GetDeclaredSymbol" or "GetSymbolInfo" or "GetTypeInfo");
                var construct = calls.Any(i => i.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.CSharp.SyntaxFactory" &&
                    i.Type is INamedTypeSymbol t && Evidence.Derives(t, "Microsoft.CodeAnalysis.SyntaxNode"));
                construct |= calls.Any(i => i.TargetMethod.Name == "Visit" &&
                    Evidence.Derives(i.TargetMethod.ContainingType, "Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter"));
                var deterministic = calls.Any(i => i.TargetMethod.Name == "NormalizeWhitespace" &&
                    i.TargetMethod.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)) &&
                    calls.Any(i => i.TargetMethod.Name == "ToFullString" &&
                        i.TargetMethod.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
                deterministic &= !values.OfType<IPropertyReferenceOperation>().Any(p =>
                    p.Property.ContainingType.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset" && p.Property.Name is "Now" or "UtcNow" ||
                    p.Property.ContainingType.ToDisplayString() == "System.Environment" && p.Property.Name is "TickCount" or "TickCount64" or "MachineName") &&
                    !calls.Any(i => i.TargetMethod.ContainingType.ToDisplayString() == "System.Random" ||
                        i.TargetMethod.ContainingType.ToDisplayString() == "System.Guid" && i.TargetMethod.Name == "NewGuid");
                r.Measure("MigrationToolStaticShape", 1, parse && semantic && construct && deterministic && vbChecked && csChecked ? 1 : 0);
                if (parse && semantic && construct && deterministic && vbChecked && csChecked) pipelines++;
            }
        }
        CheckFixtures(tooling, files, r, pipelines, fixtures ?? []);
    }
    private static int ExternalAdapterPipelines(AssessmentProject project, IOperation[] operations, Recorder recorder)
    {
        var flow = new AdapterFlow(project.Compilation, operations);
        var count = 0;
        foreach (var loop in operations.OfType<IForEachLoopOperation>().Where(flow.Reachable))
        {
            var engine = Evidence.Descendants(loop.Collection).OfType<IInvocationOperation>()
                .FirstOrDefault(KnownCompilerEngine);
            if (engine == null) continue;
            var result = loop.Locals.SingleOrDefault();
            if (result == null) continue;
            bool ResultCode(IOperation value) => value is IPropertyReferenceOperation { Property.Name: "ConvertedCode" } property &&
                property.Instance is { } instance && Unwrap(instance) is ILocalReferenceOperation local &&
                SymbolEqualityComparer.Default.Equals(local.Local, result);
            var guards = Evidence.Descendants(loop.Body).OfType<IConditionalOperation>().Where(g =>
                FailureTruth(g.Condition, result) == true && DefinitelyExits(g.WhenTrue)).ToArray();
            var code = Evidence.Descendants(loop.Body).Where(ResultCode).Where(c => guards.Any(g => Precedes(g, c))).ToArray();
            bool CheckedCode(IOperation value) => code.Any(c => SameOperation(c, value));
            var emitted = operations.OfType<IInvocationOperation>().Where(flow.Reachable).Any(call =>
                call.TargetMethod.ContainingType.ToDisplayString() == "System.IO.File" &&
                call.TargetMethod.Name is "WriteAllText" or "WriteAllTextAsync" &&
                call.Arguments.Length >= 2 && flow.DependsOn(call.Arguments[1].Value, CheckedCode));
            var input = engine.Arguments.Any(a => flow.DependsOn(a.Value,
                value => value is IParameterReferenceOperation parameter &&
                    parameter.Parameter.ContainingSymbol is IMethodSymbol { MethodKind: not MethodKind.AnonymousFunction } &&
                    DocumentInput(parameter.Parameter.Type) ||
                    value is IInvocationOperation read && read.TargetMethod.ContainingType.ToDisplayString() == "System.IO.File" &&
                    read.TargetMethod.Name is "ReadAllText" or "ReadAllTextAsync" or "OpenRead" or "ReadAllBytes"));
            var valid = input && emitted && code.Length > 0;
            recorder.Measure("MigrationToolExternalAdapterInput", 1, input ? 1 : 0);
            recorder.Measure("MigrationToolExternalAdapterOutput", 1, emitted ? 1 : 0);
            recorder.Measure("MigrationToolExternalAdapterGuard", 1, code.Length > 0 ? 1 : 0);
            recorder.Measure("MigrationToolExternalAdapterShape", 1, valid ? 1 : 0);
            if (valid) count++;
        }
        return count;
    }

    private static bool KnownCompilerEngine(IInvocationOperation call)
    {
        var method = call.TargetMethod;
        // This is an external API contract, not credit for a package reference or a
        // candidate-defined type with the same namespace.
        return method.DeclaringSyntaxReferences.Length == 0 &&
            method.ContainingAssembly.Name == "ICSharpCode.CodeConverter" &&
            method.ContainingType.ToDisplayString() == "ICSharpCode.CodeConverter.Common.ProjectConversion" &&
            method.Name == "ConvertDocumentsAsync" &&
            method.TypeArguments.Length == 1 &&
            method.TypeArguments[0].ToDisplayString() == "ICSharpCode.CodeConverter.CSharp.VBToCSConversion" &&
            method.TypeArguments[0].DeclaringSyntaxReferences.Length == 0 &&
            method.Parameters.Any(p => p.Type is INamedTypeSymbol type &&
                type.TypeArguments.Any(t => t.ToDisplayString() == "Microsoft.CodeAnalysis.Document" &&
                    t.ContainingAssembly.Name == "Microsoft.CodeAnalysis.Workspaces"));
    }

    private static bool DocumentInput(ITypeSymbol type) =>
        type.ToDisplayString() is "Microsoft.CodeAnalysis.Document" or "Microsoft.CodeAnalysis.Project" ||
        type is IArrayTypeSymbol array && DocumentInput(array.ElementType) ||
        type is INamedTypeSymbol named && named.TypeArguments.Any(DocumentInput);

    private static bool SameOperation(IOperation a, IOperation b) =>
        a.Syntax.SyntaxTree == b.Syntax.SyntaxTree && a.Syntax.Span == b.Syntax.Span && a.Kind == b.Kind;

    private static bool? FailureTruth(IOperation operation, ILocalSymbol result)
    {
        operation = Unwrap(operation);
        if (operation.ConstantValue is { HasValue: true, Value: bool constant }) return constant;
        if (operation is IPropertyReferenceOperation { Property.Name: "Success", Instance: { } receiver } &&
            Unwrap(receiver) is ILocalReferenceOperation local && SymbolEqualityComparer.Default.Equals(local.Local, result)) return false;
        if (operation is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unary)
            return FailureTruth(unary.Operand, result) is { } nested ? !nested : null;
        if (operation is not IBinaryOperation binary) return null;
        var left = FailureTruth(binary.LeftOperand, result);
        var right = FailureTruth(binary.RightOperand, result);
        return binary.OperatorKind switch
        {
            BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or => left == true || right == true ? true :
                left == false && right == false ? false : null,
            BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And => left == false || right == false ? false :
                left == true && right == true ? true : null,
            BinaryOperatorKind.Equals when left.HasValue && right.HasValue => left == right,
            BinaryOperatorKind.NotEquals when left.HasValue && right.HasValue => left != right,
            _ => null
        };
    }

    private sealed class AdapterFlow
    {
        private readonly Compilation compilation;
        private readonly IOperation[] operations;
        private readonly Dictionary<ISymbol, List<IOperation>> definitions = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, List<IOperation>> returns = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ISymbol> reachable = new(SymbolEqualityComparer.Default);

        internal AdapterFlow(Compilation compilation, IOperation[] operations)
        {
            this.compilation = compilation;
            this.operations = operations;
            foreach (var operation in operations)
            {
                if (operation is IVariableDeclaratorOperation { Initializer: { } initializer } variable) Add(definitions, variable.Symbol, initializer.Value);
                if (operation is ISimpleAssignmentOperation assignment && Symbol(assignment.Target) is { } target) Add(definitions, target, assignment.Value);
                if (operation is IFieldInitializerOperation field)
                    foreach (var symbol in field.InitializedFields) Add(definitions, symbol, field.Value);
                if (operation is IReturnOperation { ReturnedValue: { } value } && Owner(operation) is { } owner) Add(returns, owner, value);
            }
            var entry = compilation.GetEntryPoint(default);
            if (entry != null) reachable.Add(entry);
            else
                foreach (var owner in operations.Select(Owner).OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Public))
                    reachable.Add(owner);
            bool changed;
            do
            {
                changed = false;
                foreach (var call in operations.OfType<IInvocationOperation>().Where(Reachable))
                {
                    changed |= reachable.Add(call.TargetMethod.OriginalDefinition);
                    if (DiagnosticSequenceOperator(call.TargetMethod))
                        foreach (var lambda in call.Arguments.SelectMany(a => Evidence.Descendants(a.Value)).OfType<IAnonymousFunctionOperation>())
                            changed |= reachable.Add(lambda.Symbol);
                }
            } while (changed);
        }

        private static void Add(Dictionary<ISymbol, List<IOperation>> index, ISymbol symbol, IOperation value)
        {
            if (!index.TryGetValue(symbol, out var values)) index.Add(symbol, values = []);
            values.Add(value);
        }
        private ISymbol? Owner(IOperation value) => Evidence.Model(compilation, value.Syntax.SyntaxTree).GetEnclosingSymbol(value.Syntax.SpanStart);
        internal bool Reachable(IOperation value)
        {
            if (Owner(value) is not { } owner || !reachable.Contains(owner)) return false;
            for (var current = value; current.Parent != null; current = current.Parent)
                if (current.Parent is IConditionalOperation conditional &&
                    conditional.Condition.ConstantValue is { HasValue: true, Value: bool condition } &&
                    (ReferenceEquals(current, conditional.WhenTrue) && !condition ||
                     ReferenceEquals(current, conditional.WhenFalse) && condition)) return false;
            return true;
        }
        private static ISymbol? Symbol(IOperation operation) => Unwrap(operation) switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation field => field.Field,
            _ => null
        };
        private sealed record Bound(IOperation Value, Dictionary<ISymbol, Bound> Environment);
        internal bool DependsOn(IOperation value, Func<IOperation, bool> seed) =>
            Walk(value, seed, new(SymbolEqualityComparer.Default), [], 0);

        private bool Walk(IOperation value, Func<IOperation, bool> seed, Dictionary<ISymbol, Bound> environment,
            HashSet<(SyntaxTree, int, int, OperationKind)> seen, int depth, int? projection = null)
        {
            if (depth > 60) return false;
            value = Unwrap(value);
            if (Symbol(value) is { } parameter && environment.TryGetValue(parameter, out var bound))
                return Walk(bound.Value, seed, bound.Environment, seen, depth + 1, projection);
            if (seed(value)) return true;
            var identity = (value.Syntax.SyntaxTree, value.Syntax.SpanStart, value.Syntax.Span.Length, value.Kind);
            if (!seen.Add(identity)) return false;
            bool Next(IOperation operation) => Walk(operation, seed, environment, new(seen), depth + 1, projection);
            if (value is ITupleOperation tuple && projection is { } element)
                return element < tuple.Elements.Length && Walk(tuple.Elements[element], seed, environment, new(seen), depth + 1);
            if (value is IFieldReferenceOperation { Instance: { } tupleValue } tupleField && tupleField.Field.ContainingType.IsTupleType)
            {
                var ordinal = tupleField.Field.ContainingType.TupleElements.IndexOf(tupleField.Field);
                return ordinal >= 0 && Walk(tupleValue, seed, environment, new(seen), depth + 1, ordinal);
            }
            if (value is IAwaitOperation awaited) return Next(awaited.Operation);
            if (value is IConditionalOperation conditional)
                return conditional.WhenFalse != null && Next(conditional.WhenTrue) && Next(conditional.WhenFalse);
            if (value is IInvocationOperation call)
            {
                if (returns.TryGetValue(call.TargetMethod.OriginalDefinition, out var returned))
                {
                    var nested = new Dictionary<ISymbol, Bound>(environment, SymbolEqualityComparer.Default);
                    foreach (var argument in call.Arguments.Where(a => a.Parameter != null))
                        nested[call.TargetMethod.OriginalDefinition.Parameters[argument.Parameter!.Ordinal]] = new(argument.Value, environment);
                    return returned.Count > 0 && returned.All(r => Walk(r, seed, nested, new(seen), depth + 1, projection));
                }
                if (call.TargetMethod.DeclaringSyntaxReferences.Length > 0) return false;
                var type = call.TargetMethod.ContainingType.ToDisplayString();
                var content = type switch
                {
                    "Microsoft.CodeAnalysis.ProjectInfo" when call.TargetMethod.Name == "Create" => "documents",
                    "Microsoft.CodeAnalysis.DocumentInfo" when call.TargetMethod.Name == "Create" => "loader",
                    "Microsoft.CodeAnalysis.TextAndVersion" when call.TargetMethod.Name == "Create" => "text",
                    _ => null
                };
                if (content != null) return call.Arguments.Any(a => a.Parameter?.Name == content && Next(a.Value));
                if (DiagnosticSequenceOperator(call.TargetMethod) && call.TargetMethod.Name == "Select")
                {
                    var lambda = call.Arguments.SelectMany(a => Evidence.Descendants(a.Value)).OfType<IAnonymousFunctionOperation>().SingleOrDefault();
                    var source = SequenceArguments(call).FirstOrDefault();
                    if (lambda == null || source == null) return false;
                    var nested = new Dictionary<ISymbol, Bound>(environment, SymbolEqualityComparer.Default);
                    nested[lambda.Symbol.Parameters[0]] = new(source, environment);
                    return lambda.Body.Operations.OfType<IReturnOperation>().Where(r => r.ReturnedValue != null)
                        .Any(r => Walk(r.ReturnedValue!, seed, nested, new(seen), depth + 1));
                }
                if (call.TargetMethod.ContainingType.SpecialType == SpecialType.System_String ||
                    type.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal) ||
                    type is "System.String" or "System.IO.Path" or "System.Diagnostics.Process" or "System.IO.StreamReader" ||
                    type.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) || DiagnosticSequenceOperator(call.TargetMethod) ||
                    call.TargetMethod.Name == "ConfigureAwait")
                    return call.Instance != null && Next(call.Instance) || call.Arguments.Any(a => Next(a.Value));
                return false;
            }
            if (Symbol(value) is { } symbol)
            {
                if (definitions.TryGetValue(symbol, out var assigned) && assigned.Count > 1) return false;
                foreach (var append in operations.OfType<IInvocationOperation>().Where(Reachable))
                    if (append.TargetMethod.Name == "Add" && append.Arguments.Length == 1 &&
                        append.Instance is IPropertyReferenceOperation { Property.Name: "ArgumentList", Instance: { } start } &&
                        SymbolEqualityComparer.Default.Equals(Symbol(start), symbol) && Next(append.Arguments[0].Value))
                        return true;
                foreach (var loop in operations.OfType<IForEachLoopOperation>().Where(l => l.Locals.Contains(symbol, SymbolEqualityComparer.Default)))
                {
                    var position = loop.Locals.IndexOf((ILocalSymbol)symbol);
                    if (loop.Locals.Length == 2)
                    {
                        if (Container(loop.Collection, position, environment, seen, seed, depth + 1)) return true;
                    }
                    else if (Next(loop.Collection)) return true;
                }
                if (Container(value, 1, environment, seen, seed, depth + 1)) return true;
                if (definitions.TryGetValue(symbol, out var values) && values.Count == 1 && Next(values[0])) return true;
                foreach (var mutation in operations.OfType<IInvocationOperation>().Where(Reachable))
                {
                    if (mutation.TargetMethod.DeclaringSyntaxReferences.Length == 0) continue;
                    foreach (var argument in mutation.Arguments.Where(a => a.Parameter != null &&
                        SymbolEqualityComparer.Default.Equals(Symbol(a.Value), symbol)))
                    {
                        var nested = new Dictionary<ISymbol, Bound>(environment, SymbolEqualityComparer.Default);
                        foreach (var item in mutation.Arguments.Where(a => a.Parameter != null))
                            nested[item.Parameter!] = new(item.Value, environment);
                        foreach (var write in operations.OfType<IInvocationOperation>().Where(i =>
                            i.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.AdhocWorkspace" &&
                            i.TargetMethod.Name is "AddProject" or "AddDocument" && i.Instance != null &&
                            SymbolEqualityComparer.Default.Equals(Symbol(i.Instance), argument.Parameter)))
                            if (write.Arguments.Any(a => Walk(a.Value, seed, nested, new(seen), depth + 1))) return true;
                    }
                }
                return false;
            }
            if (value is IPropertyReferenceOperation property)
            {
                if (property.Property.GetMethod is { } getter && returns.TryGetValue(getter, out var returned))
                    return returned.Count > 0 && returned.All(Next);
                if (property.Property.DeclaringSyntaxReferences.Length > 0) return false;
                return property.Instance != null && Next(property.Instance);
            }
            if (value is IObjectCreationOperation creation)
                return creation.Arguments.Any(a => Next(a.Value));
            return value is not (ILiteralOperation or IAnonymousFunctionOperation or INameOfOperation) &&
                value.ChildOperations.Any(Next);
        }

        private bool Container(IOperation receiver, int position, Dictionary<ISymbol, Bound> environment,
            HashSet<(SyntaxTree, int, int, OperationKind)> seen, Func<IOperation, bool> seed, int depth)
        {
            var symbol = Symbol(receiver);
            if (symbol == null) return false;
            if (definitions.TryGetValue(symbol, out var assigned) && assigned.Count > 1) return false;
            if (operations.OfType<ISimpleAssignmentOperation>().Any(a =>
                a.Target is IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } target } &&
                SymbolEqualityComparer.Default.Equals(Symbol(target), symbol))) return false;
            var calls = operations.OfType<IInvocationOperation>().Where(Reachable).Where(call =>
                call.Instance != null && SymbolEqualityComparer.Default.Equals(Symbol(call.Instance), symbol)).ToArray();
            if (calls.Any(c => c.TargetMethod.Name is "Clear" or "Remove")) return false;
            var additions = calls.Where(add =>
                    add.TargetMethod.Name is "Add" or "TryAdd" && add.Arguments.Length == 2 &&
                    add.TargetMethod.ContainingType.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>")).ToArray();
            return additions.Length > 0 && additions.All(add =>
                Walk(add.Arguments[position].Value, seed, environment, new(seen), depth + 1));
        }
    }
    private static void CheckFixtures(AssessmentProject[] tooling, ImmutableArray<AdditionalText> files, Recorder r, int pipelines,
        IReadOnlyList<AssessmentProject> fixtures)
    {
        var vb = files.Where(f => Path.GetExtension(f.Path).Equals(".vb", StringComparison.OrdinalIgnoreCase)).ToArray();
        var cs = files.Where(f => Path.GetExtension(f.Path).Equals(".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        var representative = 0;
        foreach (var input in vb)
            foreach (var output in cs.Where(c => Path.GetFileNameWithoutExtension(c.Path) == Path.GetFileNameWithoutExtension(input.Path)))
            {
                var a = VisualBasicSyntaxTree.ParseText(input.GetText()!);
                var b = CSharpSyntaxTree.ParseText(output.GetText()!);
                if (a.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error) || b.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) continue;
                var ak = Constructs(a); var bk = Constructs(b);
                var common = ak.Intersect(bk).Count();
                var references = tooling.FirstOrDefault()?.Compilation.References ?? [];
                var inputCompilation = VisualBasicCompilation.Create("FixtureInput", [a], references,
                    new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On));
                var outputCompilation = CSharpCompilation.Create("FixtureOutput", [b], references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                if (common >= 3 && !inputCompilation.GetDiagnostics().Concat(outputCompilation.GetDiagnostics())
                    .Any(d => d.Severity == DiagnosticSeverity.Error)) representative++;
            }
        representative += ExecutableCanaries(tooling, fixtures, files, r);
        r.Measure("MigrationToolStaticPipelines", pipelines, pipelines);
        r.Measure("MigrationToolCompanionFixtures", representative, representative);
        r.Measure("MigrationToolStaticShape", 1, representative > 0 ? 1 : 0);
        if (pipelines == 0 || representative == 0)
            r.Report("MigrationToolStaticShape", OutcomeAnalyzer.Tool, Location.None,
                "Require an independently discovered, compiling tooling project with either guarded input-dependent VB semantic handling and C# syntax emission, or a guarded external compiler-engine adapter whose input content and converted result flow to file emission. " +
                "Companion evidence must cover at least three representative constructs through compiling VB/C# fixture pairs or a compiling tool-linked canary driver checking emitted source and before/after process output. " +
                $"Static pipeline shapes: {pipelines}; independent companion fixtures: {representative}. Actual emitted output remains unverified until trusted CLI replay.");
    }
    private static IOperation[] ToolOperations(AssessmentProject project) =>
        Evidence.Operations(project.Compilation, p: project).Concat(project.Compilation.SyntaxTrees
            .Where(t => !Evidence.Generated(t, project))
            .Select(t => Evidence.Model(project.Compilation, t).GetOperation(t.GetRoot()))
            .OfType<IOperation>().SelectMany(Evidence.Descendants))
        .DistinctBy(o => (o.Syntax.SyntaxTree, o.Syntax.Span, o.Kind)).ToArray();

    private static bool IsVBCompilation(IInvocationOperation i) =>
        i.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation" && i.TargetMethod.Name == "Create";

    private static int ExecutableCanaries(AssessmentProject[] tooling, IReadOnlyList<AssessmentProject> fixtures,
        ImmutableArray<AdditionalText> files, Recorder recorder)
    {
        var count = 0;
        foreach (var fixture in fixtures.Where(p => p.Test &&
            !p.Compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)))
        {
            var owners = tooling.Where(t => fixture.Compilation.ReferencedAssemblyNames.Any(a =>
                a.Name == t.Compilation.AssemblyName)).ToArray();
            if (owners.Length == 0) continue;
            var root = Path.GetDirectoryName(fixture.Path);
            if (root == null) continue;
            var inputs = files.Where(f => Path.GetExtension(f.Path).Equals(".vb", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(f.Path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Select(f => VisualBasicSyntaxTree.ParseText(f.GetText()!)).Where(t =>
                    !t.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)).ToArray();
            if (inputs.SelectMany(Constructs).Distinct().Count() < 3) continue;
            var operations = ToolOperations(fixture);
            var flow = new AdapterFlow(fixture.Compilation, operations);
            var assertions = operations.OfType<IInvocationOperation>().Where(flow.Reachable).Where(i =>
                Assertion(i, operations)).Select(i => i.Arguments[0].Value).ToArray();
            bool ToolName(IOperation value) => value.ConstantValue is { HasValue: true, Value: string text } &&
                owners.Any(t => text == t.Compilation.AssemblyName + ".dll" || text == t.Compilation.AssemblyName + ".exe");
            bool Exit(IOperation value) => value is IPropertyReferenceOperation property &&
                property.Property.ContainingType.ToDisplayString() == "System.Diagnostics.Process" && property.Property.Name == "ExitCode";
            bool Output(IOperation value) => value is IPropertyReferenceOperation property &&
                property.Property.ContainingType.ToDisplayString() == "System.Diagnostics.Process" && property.Property.Name == "StandardOutput";
            bool ToolCall(IOperation value) => value is IInvocationOperation call &&
                (owners.Any(t => call.TargetMethod.ContainingAssembly.Name == t.Compilation.AssemblyName) ||
                    flow.DependsOn(call, ToolName) && flow.DependsOn(call, Exit));
            var invoked = assertions.Any(a => flow.DependsOn(a, ToolCall));
            var emitted = assertions.Any(a => flow.DependsOn(a, value => value is IInvocationOperation read &&
                read.TargetMethod.ContainingType.ToDisplayString() == "System.IO.File" &&
                read.TargetMethod.Name is "ReadAllText" or "ReadAllTextAsync"));
            var comparison = assertions.SelectMany(Evidence.Descendants).OfType<IBinaryOperation>()
                .Any(b => b.OperatorKind == BinaryOperatorKind.Equals && !SameOperation(b.LeftOperand, b.RightOperand) &&
                    flow.DependsOn(b.LeftOperand, Output) && flow.DependsOn(b.RightOperand, Output) &&
                    !Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(b.LeftOperand.Syntax, b.RightOperand.Syntax));
            recorder.Measure("MigrationToolCanaryInvocation", 1, invoked ? 1 : 0);
            recorder.Measure("MigrationToolCanaryOutput", 1, emitted ? 1 : 0);
            recorder.Measure("MigrationToolCanaryComparison", 1, comparison ? 1 : 0);
            if (invoked && emitted && comparison) count++;
        }
        return count;
    }

    private static bool Assertion(IInvocationOperation call, IOperation[] operations)
    {
        if (call.Arguments.Length == 0 || call.Arguments[0].Parameter?.Type.SpecialType != SpecialType.System_Boolean) return false;
        var parameter = call.TargetMethod.Parameters[0];
        return operations.OfType<IConditionalOperation>().Any(guard =>
            Unwrap(guard.Condition) is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation &&
            Unwrap(negation.Operand) is IParameterReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(parameter, reference.Parameter) &&
            (guard.WhenTrue is IThrowOperation || guard.WhenTrue is IBlockOperation block && block.Operations.Any(o => o is IThrowOperation)));
    }

    private static bool Precedes(IOperation guard, IOperation emission)
    {
        if (guard.Parent is not IBlockOperation block) return false;
        for (var current = emission; current.Parent != null; current = current.Parent)
            if (ReferenceEquals(current.Parent, block))
                return block.Operations.IndexOf(guard) >= 0 && block.Operations.IndexOf(guard) < block.Operations.IndexOf(current);
        return false;
    }

    private static IInvocationOperation? CheckedDiagnostics(IConditionalOperation guard, IOperation[] operations)
    {
        var condition = ErrorCondition(guard.Condition, operations, 0);
        if (condition == null) return null;
        var errorBranch = condition.Value.ErrorsMakeTrue ? guard.WhenTrue : guard.WhenFalse;
        return DefinitelyExits(errorBranch) ? condition.Value.Diagnostics : null;
    }

    private static bool DefinitelyExits(IOperation? operation) => operation switch
    {
        IThrowOperation or IReturnOperation => true,
        IBlockOperation block => block.Operations.Any(DefinitelyExits),
        IConditionalOperation condition when condition.Condition.ConstantValue is { HasValue: true, Value: bool value } =>
            DefinitelyExits(value ? condition.WhenTrue : condition.WhenFalse),
        IConditionalOperation condition => DefinitelyExits(condition.WhenTrue) && DefinitelyExits(condition.WhenFalse),
        _ => false
    };

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            if (operation is IConversionOperation conversion) operation = conversion.Operand;
            else if (operation is IParenthesizedOperation parentheses) operation = parentheses.Operand;
            else return operation;
        }
    }

    private static IOperation? SingleDefinition(IOperation operation, IOperation[] operations, int depth)
    {
        if (depth > 8) return null;
        operation = Unwrap(operation);
        if (operation is not ILocalReferenceOperation local) return operation;
        var definitions = new List<IOperation>();
        foreach (var candidate in operations.Where(o => o.Syntax.SyntaxTree == local.Syntax.SyntaxTree &&
            o.Syntax.SpanStart < local.Syntax.SpanStart))
        {
            if (candidate is IVariableDeclaratorOperation v && SymbolEqualityComparer.Default.Equals(v.Symbol, local.Local) &&
                (v.Initializer ?? (v.Parent as IVariableDeclarationOperation)?.Initializer) is { } initializer)
                definitions.Add(initializer.Value);
            if (candidate is ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment &&
                SymbolEqualityComparer.Default.Equals(target.Local, local.Local)) definitions.Add(assignment.Value);
            if (candidate is ICompoundAssignmentOperation { Target: ILocalReferenceOperation compound } &&
                SymbolEqualityComparer.Default.Equals(compound.Local, local.Local)) return null;
        }
        return definitions.Count == 1 ? SingleDefinition(definitions[0], operations, depth + 1) : null;
    }

    private static (IInvocationOperation Diagnostics, bool ErrorsMakeTrue)? ErrorCondition(IOperation operation,
        IOperation[] operations, int depth)
    {
        if (depth > 8 || SingleDefinition(operation, operations, depth) is not { } resolved) return null;
        if (resolved is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation)
        {
            var nested = ErrorCondition(negation.Operand, operations, depth + 1);
            return nested == null ? null : (nested.Value.Diagnostics, !nested.Value.ErrorsMakeTrue);
        }
        if (resolved is IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison)
        {
            var left = Unwrap(comparison.LeftOperand);
            var right = Unwrap(comparison.RightOperand);
            var constant = left.ConstantValue.HasValue && left.ConstantValue.Value is bool ? left : right;
            if (constant.ConstantValue is not { HasValue: true, Value: bool value }) return null;
            var other = ReferenceEquals(constant, left) ? comparison.RightOperand : comparison.LeftOperand;
            var nested = ErrorCondition(other, operations, depth + 1);
            if (nested == null) return null;
            var keep = comparison.OperatorKind == BinaryOperatorKind.Equals ? value : !value;
            return (nested.Value.Diagnostics, keep == nested.Value.ErrorsMakeTrue);
        }
        if (resolved is not IInvocationOperation call || !DiagnosticSequenceOperator(call.TargetMethod) ||
            call.TargetMethod.Name is not ("Any" or "All")) return null;
        var arguments = SequenceArguments(call);
        if (arguments.Length == 0) return null;
        var source = SingleDefinition(arguments[0], operations, depth + 1);
        var predicate = arguments.Length == 2 ? ErrorPredicate(arguments[1]) : null;
        var errorsMakeTrue = call.TargetMethod.Name == "Any";
        if (call.TargetMethod.Name == "All" && predicate != false) return null;
        if (call.TargetMethod.Name == "Any" && arguments.Length == 2 && predicate != true) return null;
        if (source is IInvocationOperation where && DiagnosticSequenceOperator(where.TargetMethod) &&
            where.TargetMethod.Name == "Where")
        {
            var whereArguments = SequenceArguments(where);
            if (call.TargetMethod.Name != "Any" || arguments.Length != 1 || whereArguments.Length != 2 ||
                ErrorPredicate(whereArguments[1]) != true) return null;
            source = SingleDefinition(whereArguments[0], operations, depth + 1);
        }
        return source is IInvocationOperation diagnostics && diagnostics.TargetMethod.Name == "GetDiagnostics" &&
            Evidence.Derives(diagnostics.TargetMethod.ContainingType, "Microsoft.CodeAnalysis.Compilation")
                ? (diagnostics, errorsMakeTrue) : null;
    }

    private static bool DiagnosticSequenceOperator(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && method.ContainingAssembly.Name is "System.Linq" or "System.Core" ||
        method.ContainingType.ToDisplayString() == "System.Linq.ImmutableArrayExtensions" && method.ContainingAssembly.Name == "System.Collections.Immutable";
    private static IOperation[] SequenceArguments(IInvocationOperation call) =>
        (call.Instance == null ? [] : new[] { call.Instance }).Concat(call.Arguments.Select(a => a.Value)).ToArray();

    private static bool? ErrorPredicate(IOperation operation)
    {
        operation = Unwrap(operation);
        if (operation is IDelegateCreationOperation creation) operation = Unwrap(creation.Target);
        if (operation is not IAnonymousFunctionOperation lambda || lambda.Symbol.Parameters.Length != 1 ||
            lambda.Symbol.Parameters[0].Type.ToDisplayString() != "Microsoft.CodeAnalysis.Diagnostic") return null;
        // VB appends an unreachable implicit return after a single-line lambda's
        // unconditional return. Only the first executed statement decides its predicate.
        if (lambda.Body.Operations.FirstOrDefault() is not IReturnOperation { ReturnedValue: { } returned }) return null;
        var value = Unwrap(returned);
        if (value is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison) return null;
        var left = Unwrap(comparison.LeftOperand);
        var right = Unwrap(comparison.RightOperand);
        bool Severity(IOperation op) => op is IPropertyReferenceOperation { Property.Name: "Severity" } property &&
            property.Property.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.Diagnostic" &&
            property.Instance != null && Unwrap(property.Instance) is IParameterReferenceOperation parameter &&
            SymbolEqualityComparer.Default.Equals(parameter.Parameter, lambda.Symbol.Parameters[0]);
        static bool Error(IOperation op) => op is IFieldReferenceOperation { Field.Name: "Error" } field &&
            field.Field.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.DiagnosticSeverity";
        return Severity(left) && Error(right) || Severity(right) && Error(left)
            ? comparison.OperatorKind == BinaryOperatorKind.Equals : null;
    }

    private static HashSet<string> Constructs(SyntaxTree tree)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in tree.GetRoot().DescendantNodes())
        {
            var name = n.GetType().Name;
            if (name is "PropertyStatementSyntax" or "PropertyDeclarationSyntax") result.Add("property");
            if (name is "EventStatementSyntax" or "EventFieldDeclarationSyntax" or "EventDeclarationSyntax") result.Add("event");
            if (name is "TypeParameterSyntax") result.Add("generic");
            if (name is "ForEachStatementSyntax" or "ForEachBlockSyntax" or "ForStatementSyntax" or "ForBlockSyntax") result.Add("loop");
            if (name is "TryStatementSyntax" or "TryBlockSyntax") result.Add("exception");
            if (name is "SimpleLambdaExpressionSyntax" or "ParenthesizedLambdaExpressionSyntax" or "SingleLineLambdaExpressionSyntax" or "MultiLineLambdaExpressionSyntax") result.Add("lambda");
        }
        return result;
    }
}
