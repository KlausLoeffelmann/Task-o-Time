using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Modernization.Analyzers.Tests;

internal enum Origin
{
    Unknown, DefaultProject, DefaultProjectId, MinuteComponent, HourComponent, FilteredSequence,
    ClockComposition, PossibleClockComposition
}

// A bounded, conservative source-body analysis. Mixed/unknown branch origins do not prove a defect.
internal sealed class Provenance(Compilation compilation)
{
    internal Origin Evaluate(IOperation? operation) => Evaluate(operation, new Dictionary<IParameterSymbol, Origin>(SymbolEqualityComparer.Default), new HashSet<ISymbol>(SymbolEqualityComparer.Default), 0);

    private Origin Evaluate(IOperation? operation, Dictionary<IParameterSymbol, Origin> parameters, HashSet<ISymbol> visiting, int depth)
    {
        if (operation is null || depth > 12) return Origin.Unknown;
        Origin Recur(IOperation? value) => Evaluate(value, parameters, visiting, depth + 1);
        switch (operation)
        {
            case IConversionOperation conversion: return Recur(conversion.Operand);
            case IParenthesizedOperation parenthesized: return Recur(parenthesized.Operand);
            case IArgumentOperation argument: return Recur(argument.Value);
            case IParameterReferenceOperation parameter:
                return parameters.GetValueOrDefault(parameter.Parameter);
            case ILocalReferenceOperation local:
                return EvaluateLocal(local, [], parameters, visiting, depth);
            case IFieldReferenceOperation field when TupleIndex(field.Field) is var index && index >= 0:
                return EvaluateProjected(field.Instance, [index], parameters, visiting, depth + 1);
            case IPropertyReferenceOperation property:
                if (property.Property.ContainingType.ToDisplayString() == "System.TimeSpan" && property.Property.Name == "Minutes")
                    return Origin.MinuteComponent;
                if (property.Property.ContainingType.ToDisplayString() == "System.TimeSpan" && property.Property.Name == "Hours")
                    return Origin.HourComponent;
                if (property.Property.Name == "IdProject" && IsProject(property.Property.ContainingType) && Recur(property.Instance) == Origin.DefaultProject)
                    return Origin.DefaultProjectId;
                if (property.Property.IsIndexer && IsProject(property.Type) &&
                    property.Arguments.Length == 1 && IsZero(property.Arguments[0].Value))
                    return Origin.DefaultProject;
                return SourceReturns(property.Property.GetMethod, [], parameters, visiting, depth);
            case IArrayElementReferenceOperation element when IsProject(element.Type) && element.Indices.Length == 1 && IsZero(element.Indices[0]):
                return Origin.DefaultProject;
            case IInvocationOperation invocation:
                var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
                if (IsDateArithmetic(method))
                {
                    if (method.Name == "AddHours" && invocation.Arguments.Any(a => Recur(a.Value) == Origin.HourComponent))
                        return Origin.ClockComposition;
                    return Recur(invocation.Instance);
                }
                if (method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && method.Name == "Where")
                    return Origin.FilteredSequence;
                if (method.ContainingType.ToDisplayString() == "System.Linq.Enumerable" && IsProject(invocation.Type))
                {
                    var sequence = invocation.TargetMethod.ReducedFrom is not null
                        ? invocation.Instance : invocation.Arguments.FirstOrDefault()?.Value;
                    if (Recur(sequence) == Origin.FilteredSequence) return Origin.Unknown;
                    if (method.Name is "First" or "FirstOrDefault" && method.Parameters.Length == 1)
                        return Origin.DefaultProject;
                    if (method.Name is "ElementAt" or "ElementAtOrDefault" &&
                        invocation.Arguments.LastOrDefault() is { } index && IsZero(index.Value))
                        return Origin.DefaultProject;
                }
                return SourceReturns(invocation.TargetMethod, invocation.Arguments, parameters, visiting, depth);
            case IConditionalOperation conditional:
                return Consensus([Recur(conditional.WhenTrue), Recur(conditional.WhenFalse)]);
            case ICoalesceOperation coalesce:
                return Consensus([Recur(coalesce.Value), Recur(coalesce.WhenNull)]);
            case IUnaryOperation unary when unary.OperatorKind is UnaryOperatorKind.Minus or UnaryOperatorKind.Plus:
                return Recur(unary.Operand);
        }
        return Origin.Unknown;
    }

    private Origin EvaluateLocal(ILocalReferenceOperation reference, int[] projection,
        Dictionary<IParameterSymbol, Origin> parameters, HashSet<ISymbol> visiting, int depth)
    {
        if (!visiting.Add(reference.Local)) return Origin.Unknown;
        var result = LocalOrigin(reference, projection, parameters, visiting, depth);
        visiting.Remove(reference.Local);
        return result;
    }

    private sealed record Definition(IOperation Site, IOperation? Value, int[] Projection);

    private Origin LocalOrigin(ILocalReferenceOperation reference, int[] projection,
        Dictionary<IParameterSymbol, Origin> parameters, HashSet<ISymbol> visiting, int depth)
    {
        var root = Root(reference);
        var definitions = new List<Definition>();
        foreach (var operation in root.DescendantsAndSelf().Where(o => o.Syntax.Span.End <= reference.Syntax.SpanStart))
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declaration when SymbolEqualityComparer.Default.Equals(declaration.Symbol, reference.Local):
                    definitions.Add(new(operation, declaration.Initializer?.Value ??
                        (declaration.Parent as IVariableDeclarationOperation)?.Initializer?.Value, projection));
                    break;
                case ISimpleAssignmentOperation assignment when WritesLocal(assignment.Target, reference.Local):
                    definitions.Add(new(operation, assignment.Target is ILocalReferenceOperation ? assignment.Value : null, projection));
                    break;
                case IDeconstructionAssignmentOperation deconstruction:
                    var path = DeconstructedElement(deconstruction.Target, reference.Local);
                    if (path is not null)
                        definitions.Add(new(operation, deconstruction.Value, [.. path, .. projection]));
                    else if (deconstruction.Target.DescendantsAndSelf().OfType<IFieldReferenceOperation>()
                             .Any(field => WritesLocal(field, reference.Local)))
                        definitions.Add(new(operation, null, projection));
                    break;
                case ICompoundAssignmentOperation compound when WritesLocal(compound.Target, reference.Local):
                case IIncrementOrDecrementOperation increment when WritesLocal(increment.Target, reference.Local):
                    definitions.Add(new(operation, null, projection));
                    break;
            }
        }
        definitions.Sort((left, right) => left.Site.Syntax.SpanStart.CompareTo(right.Site.Syntax.SpanStart));
        if (definitions.Count == 0) return Origin.Unknown;
        var last = definitions[^1];
        // An unconditional assignment in the same block kills the prior value. Otherwise require agreement.
        var values = EnclosingBlock(last.Site) == EnclosingBlock(reference)
            ? new[] { last }
            : definitions.AsEnumerable();
        return Consensus(values.Select(d => EvaluateProjected(d.Value, d.Projection, parameters, visiting, depth + 1)));
    }

    private Origin EvaluateProjected(IOperation? operation, int[] projection,
        Dictionary<IParameterSymbol, Origin> parameters, HashSet<ISymbol> visiting, int depth)
    {
        if (operation is null || depth > 12) return Origin.Unknown;
        if (projection.Length == 0) return Evaluate(operation, parameters, visiting, depth);
        Origin Recur(IOperation? value, int[] path) => EvaluateProjected(value, path, parameters, visiting, depth + 1);
        return operation switch
        {
            IConversionOperation conversion => Recur(conversion.Operand, projection),
            IParenthesizedOperation parenthesized => Recur(parenthesized.Operand, projection),
            ITupleOperation tuple when projection[0] < tuple.Elements.Length =>
                Recur(tuple.Elements[projection[0]], projection[1..]),
            ILocalReferenceOperation local => EvaluateLocal(local, projection, parameters, visiting, depth + 1),
            IFieldReferenceOperation field when TupleIndex(field.Field) is var index && index >= 0 =>
                Recur(field.Instance, [index, .. projection]),
            IConditionalOperation conditional => Consensus([Recur(conditional.WhenTrue, projection), Recur(conditional.WhenFalse, projection)]),
            ICoalesceOperation coalesce => Consensus([Recur(coalesce.Value, projection), Recur(coalesce.WhenNull, projection)]),
            _ => Origin.Unknown
        };
    }

    private static int[]? DeconstructedElement(IOperation target, ILocalSymbol local)
    {
        if (target is IDeclarationExpressionOperation declaration) return DeconstructedElement(declaration.Expression, local);
        if (target is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, local)) return [];
        if (target is ITupleOperation tuple)
            for (var index = 0; index < tuple.Elements.Length; index++)
                if (DeconstructedElement(tuple.Elements[index], local) is { } path) return [index, .. path];
        return null;
    }

    private static bool WritesLocal(IOperation target, ILocalSymbol local) => target switch
    {
        ILocalReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Local, local),
        IFieldReferenceOperation field when TupleIndex(field.Field) >= 0 && field.Instance is not null => WritesLocal(field.Instance, local),
        _ => false
    };

    private static int TupleIndex(IFieldSymbol field)
    {
        var elements = field.ContainingType.TupleElements;
        if (elements.IsDefaultOrEmpty) return -1;
        for (var index = 0; index < elements.Length; index++)
            if (SymbolEqualityComparer.Default.Equals(elements[index], field) ||
                (field.CorrespondingTupleField is not null &&
                 SymbolEqualityComparer.Default.Equals(elements[index].CorrespondingTupleField, field.CorrespondingTupleField)))
                return index;
        return -1;
    }

    private Origin SourceReturns(IMethodSymbol? method, IEnumerable<IArgumentOperation> arguments,
        Dictionary<IParameterSymbol, Origin> outer, HashSet<ISymbol> visiting, int depth)
    {
        if (method is null || depth > 12 || method.IsVirtual || method.IsAbstract || !visiting.Add(method))
            return Origin.Unknown;
        var bindings = new Dictionary<IParameterSymbol, Origin>(outer, SymbolEqualityComparer.Default);
        foreach (var argument in arguments)
            if (argument.Parameter is not null)
                bindings[argument.Parameter] = Evaluate(argument.Value, outer, visiting, depth + 1);
        var returns = new List<Origin>();
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var body = model.GetOperation(syntax);
            if (body is null && syntax.Parent is not null) body = model.GetOperation(syntax.Parent);
            if (body is null) continue;
            foreach (var value in body.DescendantsAndSelf().OfType<IReturnOperation>()
                         .Where(r => !r.IsImplicit || r.Language == LanguageNames.CSharp))
                returns.Add(Evaluate(value.ReturnedValue, bindings, visiting, depth + 1));
        }
        visiting.Remove(method);
        return Consensus(returns);
    }

    private static Origin Consensus(IEnumerable<Origin> origins)
    {
        var distinct = origins.Distinct().ToArray();
        if (distinct.Length == 1) return distinct[0];
        return distinct.Any(o => o is Origin.ClockComposition or Origin.PossibleClockComposition)
            ? Origin.PossibleClockComposition : Origin.Unknown;
    }
    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent is not null) operation = operation.Parent;
        return operation;
    }
    private static IOperation? EnclosingBlock(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
            if (parent is IBlockOperation or IConditionalOperation or ILoopOperation or ISwitchCaseOperation) return parent;
        return null;
    }
    private static bool IsZero(IOperation operation) => operation.ConstantValue is { HasValue: true, Value: int value } && value == 0;
    private static bool IsProject(ITypeSymbol? type) => type?.ToDisplayString() is
        "TaskOTime.AppServer.Models.ProjectMainDataDto" or "TaskOTime.DTOs.Project";

    private static bool IsDateArithmetic(IMethodSymbol method) =>
        (method.ContainingType.SpecialType == SpecialType.System_DateTime ||
         method.ContainingType.ToDisplayString() == "System.DateTimeOffset") &&
        method.Name is "AddHours" or "AddMinutes" or "AddSeconds" or "AddDays" or "AddTicks" or "AddMilliseconds";

    internal bool ComposesClock(IOperation? receiver) =>
        Evaluate(receiver) is Origin.ClockComposition or Origin.PossibleClockComposition;
}
