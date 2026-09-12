using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Modernization.Analyzers.Tests;

internal static class MigrationToolAnalysis
{
    internal static void Run(AssessmentProject[] tooling, ImmutableArray<AdditionalText> files, Recorder r)
    {
        var pipelines = 0;
        foreach (var project in tooling)
        {
            var operations = Evidence.Operations(project.Compilation, p: project).ToArray();
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
        r.Measure("MigrationToolStaticShape", 1, representative > 0 ? 1 : 0);
        if (pipelines == 0 || representative == 0)
            r.Report("MigrationToolStaticShape", OutcomeAnalyzer.Tool, Location.None,
                "Require an independently discovered, compiling tooling project with input-dependent VB syntax/semantic handling, C# syntax construction/rewriting flowing to normalized source emission, guarded input/output compilation diagnostics, and paired VB/C# fixture artifacts covering at least three representative constructs. " +
                $"Static pipeline shapes: {pipelines}; independent companion fixture pairs: {representative}. Actual emitted output remains unverified until trusted CLI replay.");
    }
    private static bool IsVBCompilation(IInvocationOperation i) =>
        i.TargetMethod.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation" && i.TargetMethod.Name == "Create";

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
