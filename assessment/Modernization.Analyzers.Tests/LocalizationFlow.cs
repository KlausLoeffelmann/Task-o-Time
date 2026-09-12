using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Modernization.Analyzers.Tests;

// Call-site parameter substitution keeps unrelated/dead callers out of presentation evidence.
internal sealed class LocalizationFlow
{
    internal sealed record Lookup(string Key, string BaseName, string Assembly);
    internal sealed record Value(IOperation Operation, string? Literal = null, Lookup? Resource = null);
    private readonly Dictionary<string, List<IOperation>> returns = [];
    private readonly Dictionary<string, List<IOperation>> definitions = [];
    private readonly List<IOperation> operations = [];
    private readonly Dictionary<string, List<IOperation>> members = [];
    internal static string Id(ISymbol symbol) => symbol.ContainingAssembly?.Name + "|" +
        (symbol.GetDocumentationCommentId() ?? symbol.ContainingSymbol?.GetDocumentationCommentId() + "|" +
            symbol.ToDisplayString() + "|" + symbol.Locations.FirstOrDefault()?.SourceSpan.Start);

    internal LocalizationFlow(AssessmentProject[] projects)
    {
        void Add(Dictionary<string, List<IOperation>> index, ISymbol symbol, IOperation value)
        {
            var id = Id(symbol);
            if (!index.TryGetValue(id, out var values)) index[id] = values = [];
            values.Add(value);
        }
        foreach (var p in projects)
        {
            foreach (var op in Evidence.Operations(p.Compilation, generated: true))
            {
                operations.Add(op);
                if (Producers.ReturnOwner(p.Compilation, op) is { } member)
                    Add(members, member, op);
                if (op is IReturnOperation { ReturnedValue: { } result } &&
                    Producers.ReturnOwner(p.Compilation, op) is { } owner) Add(returns, owner, result);
                if (op is ISimpleAssignmentOperation assignment && Target(assignment.Target) is { } target)
                    Add(definitions, target, assignment.Value);
                if (op is IVariableDeclaratorOperation { Initializer.Value: { } initial } variable)
                    Add(definitions, variable.Symbol, initial);
                if (op is IFieldInitializerOperation field)
                    foreach (var symbol in field.InitializedFields) Add(definitions, symbol, field.Value);
                if (op is IPropertyInitializerOperation property)
                    foreach (var symbol in property.InitializedProperties) Add(definitions, symbol, property.Value);
                if (op is IArgumentOperation { Parameter: { } parameter } argument)
                    Add(definitions, parameter, argument.Value);
            }
        }
    }

    private static ISymbol? Target(IOperation op) => op switch
    {
        IPropertyReferenceOperation p => p.Property, IFieldReferenceOperation f => f.Field,
        ILocalReferenceOperation l => l.Local, IParameterReferenceOperation p => p.Parameter, _ => null
    };
    private static string ParameterId(IParameterSymbol p) =>
        Id((p.ContainingSymbol as IMethodSymbol)?.AssociatedSymbol ?? p.ContainingSymbol) + "#" + p.Ordinal;
    private static bool External(ITypeSymbol type, string name) =>
        type.OriginalDefinition.ToDisplayString() == name &&
        type.ContainingAssembly.Name.StartsWith("Microsoft.Extensions.Localization", StringComparison.Ordinal) &&
        !type.Locations.Any(l => l.IsInSource);
    private static bool Localizer(ITypeSymbol? type) => type is INamedTypeSymbol named &&
        (External(named, "Microsoft.Extensions.Localization.IStringLocalizer") ||
         named.AllInterfaces.Any(i => External(i, "Microsoft.Extensions.Localization.IStringLocalizer")));
    private IEnumerable<IOperation> Bodies(ISymbol symbol) => returns.GetValueOrDefault(Id(symbol)) ?? [];
    private IEnumerable<IOperation> Definitions(ISymbol symbol) => definitions.GetValueOrDefault(Id(symbol)) ?? [];
    private static Dictionary<string, IOperation> Arguments(IEnumerable<IArgumentOperation> arguments, Dictionary<string, IOperation> env)
    {
        var next = new Dictionary<string, IOperation>(env);
        foreach (var arg in arguments)
            if (arg.Parameter != null)
                next[ParameterId(arg.Parameter)] = Substitute(arg.Value, env);
        return next;
    }
    private static IOperation Substitute(IOperation op, Dictionary<string, IOperation> env)
    {
        for (var i = 0; i < 32; i++)
        {
            if (op is IConversionOperation conversion) { op = conversion.Operand; continue; }
            if (op is IParameterReferenceOperation p && env.TryGetValue(ParameterId(p.Parameter), out var value) && !ReferenceEquals(value, op))
            { op = value; continue; }
            break;
        }
        return op;
    }

    internal IEnumerable<Value> Values(IOperation op) => Walk(op, [], [], 0);
    private IEnumerable<Value> Walk(IOperation original, Dictionary<string, IOperation> env, HashSet<string> seen, int depth)
    {
        if (depth > 40) yield break;
        var op = Substitute(original, env);
        var identity = op.Syntax.SyntaxTree.FilePath + "|" + op.Syntax.Span + "|" + op.Kind;
        if (!seen.Add(identity)) yield break;
        try
        {
            if (op is INameOfOperation) yield break;
            if (op.ConstantValue is { HasValue: true, Value: string literal })
            { yield return new(op, Literal: literal); yield break; }
            if (op is IPropertyReferenceOperation property && property.Property.IsIndexer && Localizer(property.Property.ContainingType))
            {
                var key = property.Arguments.FirstOrDefault() is { } argument ? Constant(argument.Value, env) : null;
                if (key != null && property.Instance != null)
                    foreach (var provider in Providers(property.Instance, env, [], 0))
                        yield return new(op, Resource: new(key, provider.BaseName, provider.Assembly));
                yield break;
            }
            if (op is IPropertyReferenceOperation localized && localized.Property.Name == "Value" &&
                External(localized.Property.ContainingType, "Microsoft.Extensions.Localization.LocalizedString") && localized.Instance != null)
            {
                foreach (var value in Walk(localized.Instance, env, seen, depth + 1)) yield return value;
                yield break;
            }
            if (op is IInvocationOperation call)
            {
                if (call.TargetMethod.MethodKind == MethodKind.DelegateInvoke && call.Instance != null)
                {
                    var receiver = Substitute(call.Instance, env);
                    if (receiver is IDelegateCreationOperation delegateCreation) receiver = delegateCreation.Target;
                    if (receiver is IAnonymousFunctionOperation lambda)
                        foreach (var result in Evidence.Descendants(lambda.Body).OfType<IReturnOperation>().Where(r => r.ReturnedValue != null))
                            foreach (var value in Walk(result.ReturnedValue!, env, seen, depth + 1)) yield return value;
                    yield break;
                }
                var bodies = Bodies(call.TargetMethod).ToArray();
                if (bodies.Length > 0)
                {
                    var next = Arguments(call.Arguments, env);
                    foreach (var body in bodies)
                        foreach (var value in Walk(body, next, seen, depth + 1)) yield return value;
                }
                else if (call.TargetMethod.ContainingType.SpecialType == SpecialType.System_String)
                {
                    foreach (var child in call.Arguments.Where(a => a.Parameter?.Type.ToDisplayString() != "System.IFormatProvider").Select(a => a.Value)
                        .Concat(call.Instance == null ? [] : new[] { call.Instance }))
                        foreach (var value in Walk(child, env, seen, depth + 1)) yield return value;
                }
                else if (call.TargetMethod.Name == "ToString" && call.Instance != null)
                    foreach (var value in Walk(call.Instance, env, seen, depth + 1)) yield return value;
                yield break;
            }
            if (op is IObjectCreationOperation creation)
            {
                var toString = creation.Type?.GetMembers("ToString").OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 0);
                if (toString != null)
                    foreach (var body in Bodies(toString))
                        foreach (var value in Walk(body, Arguments(creation.Arguments, env), seen, depth + 1)) yield return value;
                yield break;
            }
            if (Target(op) is { } symbol)
            {
                var next = op is IPropertyReferenceOperation indexed ? Arguments(indexed.Arguments, env) : env;
                // An unknown object's auto-property is data, not every assignment to that
                // property on every instance in the repository (for example all users' names).
                var writes = Definitions(symbol);
                if (op is IPropertyReferenceOperation { Instance: not null } member &&
                    member.Instance is not IInstanceReferenceOperation)
                {
                    writes = writes.Where(value => value.Parent is IPropertyInitializerOperation);
                    foreach (var instance in Origins(member.Instance, env, [], 0).OfType<IObjectCreationOperation>())
                    {
                        var constructorEnv = Arguments(instance.Arguments, env);
                        var assignments = (instance.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>() ?? [])
                            .Concat(instance.Constructor == null ? [] : (members.GetValueOrDefault(Id(instance.Constructor)) ?? []).OfType<ISimpleAssignmentOperation>());
                        foreach (var assignment in assignments.Where(a => a.Target is IPropertyReferenceOperation p &&
                            Id(p.Property) == Id(member.Property)))
                            foreach (var value in Walk(assignment.Value, constructorEnv, seen, depth + 1)) yield return value;
                    }
                }
                foreach (var body in Bodies(symbol).Concat(writes))
                    foreach (var value in Walk(body, next, seen, depth + 1)) yield return value;
                yield break;
            }
            var children = op switch
            {
                IConditionalOperation condition => new[] { condition.WhenTrue, condition.WhenFalse }.OfType<IOperation>(),
                ISwitchExpressionOperation expression => expression.Arms.Select(a => a.Value),
                _ => op.ChildOperations
            };
            foreach (var child in children)
                foreach (var value in Walk(child, env, seen, depth + 1)) yield return value;
        }
        finally { seen.Remove(identity); }
    }

    private string? Constant(IOperation op, Dictionary<string, IOperation> env) =>
        Walk(op, env, [], 0).Where(v => v.Literal != null).Select(v => v.Literal).Distinct().ToArray() is [var value] ? value : null;

    private IEnumerable<(string BaseName, string Assembly)> Providers(IOperation original, Dictionary<string, IOperation> env,
        HashSet<string> seen, int depth)
    {
        if (depth > 24) yield break;
        var op = Substitute(original, env);
        if (op is IInvocationOperation call && call.TargetMethod.Name == "Create" &&
            (External(call.TargetMethod.ContainingType, "Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory") ||
             External(call.TargetMethod.ContainingType, "Microsoft.Extensions.Localization.IStringLocalizerFactory")))
        {
            var baseName = call.Arguments.FirstOrDefault() is { } first ? Constant(first.Value, env) : null;
            var owner = call.Arguments.Skip(1).FirstOrDefault() is { } second ? Constant(second.Value, env) : null;
            var type = call.Arguments.SelectMany(a => Evidence.Descendants(a.Value)).OfType<ITypeOfOperation>().FirstOrDefault()?.TypeOperand;
            owner ??= type?.ContainingAssembly.Name;
            if (baseName == null && call.Arguments.Length == 1 && type != null) baseName = type.ToDisplayString();
            var factories = call.Instance == null ? [] : Origins(call.Instance, env, [], 0)
                .OfType<IObjectCreationOperation>().Where(c => External(c.Type!, "Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory")).ToArray();
            foreach (var factory in factories)
            {
                var path = factory.Arguments.SelectMany(a => Origins(a.Value, env, [], 0)).SelectMany(Evidence.Descendants)
                    .OfType<ISimpleAssignmentOperation>().Where(a => a.Target is IPropertyReferenceOperation p &&
                        p.Property.Name == "ResourcesPath" && External(p.Property.ContainingType, "Microsoft.Extensions.Localization.LocalizationOptions"))
                    .Select(a => Constant(a.Value, env)).OfType<string>().Distinct().ToArray();
                if (baseName == null || owner == null || path.Length > 1) continue;
                var relative = baseName.StartsWith(owner + ".", StringComparison.Ordinal) ? baseName[(owner.Length + 1)..] : baseName;
                yield return (owner + "." + (path.Length == 1 && path[0].Length > 0 ? path[0].Replace('\\', '.').Replace('/', '.') + "." : "") + relative, owner);
            }
            yield break;
        }
        if (Target(op) is { } symbol && seen.Add(Id(symbol)))
            foreach (var value in Bodies(symbol).Concat(Definitions(symbol)))
                foreach (var provider in Providers(value, env, seen, depth + 1)) yield return provider;
    }

    private IEnumerable<IOperation> Origins(IOperation original, Dictionary<string, IOperation> env, HashSet<string> seen, int depth)
    {
        if (depth > 24) yield break;
        var op = Substitute(original, env);
        yield return op;
        if (op is IInvocationOperation call && seen.Add(Id(call.TargetMethod)))
            foreach (var body in Bodies(call.TargetMethod))
                foreach (var value in Origins(body, Arguments(call.Arguments, env), seen, depth + 1)) yield return value;
        if (Target(op) is { } symbol && seen.Add(Id(symbol)))
            foreach (var body in Bodies(symbol).Concat(Definitions(symbol)))
                foreach (var value in Origins(body, env, seen, depth + 1)) yield return value;
        foreach (var child in op.ChildOperations)
            foreach (var value in Origins(child, env, seen, depth + 1)) yield return value;
    }

    internal (string Property, INamedTypeSymbol Source)? MarkupBinding(INamedTypeSymbol extension)
    {
        var provide = extension.GetMembers("ProvideValue").OfType<IMethodSymbol>().SingleOrDefault();
        if (provide == null || !Evidence.Derives(extension, "System.Windows.Markup.MarkupExtension")) return null;
        foreach (var body in Bodies(provide))
        {
            if (body is not IInvocationOperation call || call.TargetMethod.Name != "ProvideValue" || call.Instance == null) continue;
            foreach (var binding in Origins(call.Instance, [], [], 0).OfType<IObjectCreationOperation>()
                .Where(c => c.Type?.ToDisplayString() == "System.Windows.Data.Binding"))
            {
                var path = binding.Arguments.FirstOrDefault()?.Value;
                if (path == null) continue;
                var leaves = Evidence.Descendants(path).Where(o => o is ILiteralOperation or IPropertyReferenceOperation).ToArray();
                if (leaves.Length != 3 || leaves[0].ConstantValue.Value as string != "[" ||
                    leaves[2].ConstantValue.Value as string != "]" || leaves[1] is not IPropertyReferenceOperation key ||
                    Id(key.Property.ContainingType) != Id(extension)) continue;
                var sourceValue = binding.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>()
                    .FirstOrDefault(a => a.Target is IPropertyReferenceOperation p && p.Property.Name == "Source")?.Value;
                var source = sourceValue == null ? null : Substitute(sourceValue, []).Type as INamedTypeSymbol;
                if (source != null) return (key.Property.Name, source);
            }
        }
        return null;
    }

    internal bool CultureFromSelection(HashSet<string> selectedProperties)
    {
        bool Selected(IOperation value, Dictionary<string, IOperation> env) => Origins(value, env, [], 0)
            .OfType<IPropertyReferenceOperation>().Any(p => selectedProperties.Contains(Id(p.Property)));
        bool Check(IMethodSymbol method, Dictionary<string, IOperation> env, HashSet<string> seen, int depth)
        {
            if (depth > 12 || !seen.Add(Id(method))) return false;
            foreach (var op in members.GetValueOrDefault(Id(method)) ?? [])
            {
                if (op is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation p } a &&
                    p.Property.ContainingType.ToDisplayString() is "System.Globalization.CultureInfo" or "System.Threading.Thread" &&
                    p.Property.Name is "CurrentCulture" or "CurrentUICulture" or "DefaultThreadCurrentCulture" or "DefaultThreadCurrentUICulture" &&
                    Selected(a.Value, env)) return true;
                if (op is IInvocationOperation nested && Check(nested.TargetMethod, Arguments(nested.Arguments, env), seen, depth + 1))
                    return true;
            }
            return false;
        }
        return operations.OfType<IInvocationOperation>().Where(c => c.Arguments.Any(a => Selected(a.Value, [])))
            .Any(c => Check(c.TargetMethod, Arguments(c.Arguments, []), [], 0));
    }

    internal IEnumerable<Lookup> Indexer(INamedTypeSymbol type, string key)
    {
        var property = type.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(p => p.IsIndexer && p.Parameters.Length == 1);
        if (property == null) yield break;
        // A real literal operation supplies the markup argument to the source indexer.
        var literal = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.LiteralExpression(
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression, Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(key));
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class K { string P => " + literal + "; }");
        var c = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("MarkupKey", [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var operation = c.GetSemanticModel(tree).GetOperation(tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>().Single())!;
        var env = new Dictionary<string, IOperation> { [ParameterId(property.Parameters[0])] = operation };
        foreach (var body in Bodies(property))
            foreach (var value in Walk(body, env, [], 0))
                if (value.Resource != null) yield return value.Resource;
    }
}
