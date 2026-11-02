using System.Globalization;

namespace Strategy.Platform.Kernel.Expressions;

/// <summary>Value types of the expression language (ADR 0022). <see cref="Any"/> = not known until evaluation.</summary>
public enum ExpressionValueType
{
    Any = 0,
    Number = 1,
    Text = 2,
    Boolean = 3,
    Date = 4,
    Null = 5,
}

/// <summary>A whitelisted variable an expression may use, with its type and labels for the editor.</summary>
public sealed record ExpressionVariable(string Name, ExpressionValueType Type, string? LabelAr = null, string? LabelEn = null);

/// <summary>A compile or evaluation error: stable code, translatable message key, character position (0-based) and parameters.</summary>
public sealed record ExpressionError(string Code, string MessageKey, int Position, IReadOnlyDictionary<string, object?>? Params = null)
{
    internal static ExpressionError Of(string code, int position, params (string Key, object? Value)[] parameters) =>
        new($"expressions.{code}", $"expressions.errors.{code}", position,
            parameters.Length == 0 ? null : parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
}

/// <summary>Result of <see cref="ExpressionEngine.Compile"/>: the expression, or the errors that stopped it.</summary>
public sealed record ExpressionCompilation(CompiledExpression? Expression, IReadOnlyList<ExpressionError> Errors)
{
    public bool Success => Expression is not null && Errors.Count == 0;
}

/// <summary>Result of an evaluation: a value (decimal, string, bool, <see cref="DateOnly"/> or null) or an error.</summary>
public sealed record ExpressionEvaluation(object? Value, ExpressionError? Error)
{
    public bool Success => Error is null;

    /// <summary>The value read as a condition: true only for boolean true.</summary>
    public bool IsTrue => Value is true;
}

/// <summary>Limits that keep every expression small and every evaluation short.</summary>
public static class ExpressionLimits
{
    public const int MaxLength = 2000;
    public const int MaxNodes = 300;
    public const int MaxDepth = 40;
    public const int MaxSteps = 10_000;
}

/// <summary>
/// The shared sandboxed expression service (CLAUDE.md §15, ADR 0022): admin-written formulas and conditions over whitelisted variables.
/// No reflection, no member access, no loops; numbers are decimals. Thread-safe and stateless.
/// </summary>
public static class ExpressionEngine
{
    /// <summary>
    /// Parses and type-checks <paramref name="text"/> against <paramref name="variables"/>. With <paramref name="expected"/> the result type must
    /// match (e.g. <see cref="ExpressionValueType.Boolean"/> for conditions; null is accepted).
    /// </summary>
    public static ExpressionCompilation Compile(string? text, IReadOnlyCollection<ExpressionVariable> variables, ExpressionValueType? expected = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ExpressionCompilation(null, [ExpressionError.Of("empty", 0)]);
        }

        if (text.Length > ExpressionLimits.MaxLength)
        {
            return new ExpressionCompilation(null, [ExpressionError.Of("tooLong", ExpressionLimits.MaxLength, ("max", ExpressionLimits.MaxLength))]);
        }

        var tokens = Lexer.Tokenize(text, out var lexError);
        if (lexError is not null)
        {
            return new ExpressionCompilation(null, [lexError]);
        }

        var parser = new Parser(tokens);
        var root = parser.ParseAll(out var parseError);
        if (root is null)
        {
            return new ExpressionCompilation(null, [parseError!]);
        }

        var declared = variables.ToDictionary(v => v.Name, v => v.Type, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ExpressionError>();
        var type = TypeChecker.Check(root, declared, errors);
        if (errors.Count == 0 && expected is { } wanted && type != wanted && type is not (ExpressionValueType.Any or ExpressionValueType.Null))
        {
            errors.Add(ExpressionError.Of(wanted == ExpressionValueType.Boolean ? "notBoolean" : "resultType", 0, ("expected", Code(wanted)), ("actual", Code(type))));
        }

        return errors.Count > 0
            ? new ExpressionCompilation(null, errors)
            : new ExpressionCompilation(new CompiledExpression(text, root, type, Collect(root)), []);
    }

    internal static string Code(ExpressionValueType type) => type.ToString().ToLowerInvariant();

    private static HashSet<string> Collect(Node root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(Node node)
        {
            switch (node)
            {
                case VariableNode v:
                    names.Add(v.Name);
                    break;
                case UnaryNode u:
                    Walk(u.Operand);
                    break;
                case BinaryNode b:
                    Walk(b.Left);
                    Walk(b.Right);
                    break;
                case InNode i:
                    Walk(i.Value);
                    i.Items.ForEach(Walk);
                    break;
                case CallNode c:
                    c.Arguments.ForEach(Walk);
                    break;
            }
        }

        Walk(root);
        return names;
    }

    /// <summary>Converts an input value to the language's value set (numbers → decimal, Guid → text, DateTime → date).</summary>
    public static object? Normalise(object? value) => value switch
    {
        null => null,
        decimal or string or bool or DateOnly => value,
        int i => (decimal)i,
        long l => (decimal)l,
        short s => (decimal)s,
        double d => (decimal)d,
        float f => (decimal)f,
        Guid g => g.ToString("D"),
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
        Enum e => e.ToString(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}

/// <summary>A parsed, type-checked expression. Evaluate it as often as needed; each run is bounded by <see cref="ExpressionLimits.MaxSteps"/>.</summary>
public sealed class CompiledExpression
{
    internal CompiledExpression(string text, Node root, ExpressionValueType resultType, IReadOnlySet<string> variables)
    {
        Text = text;
        Root = root;
        ResultType = resultType;
        Variables = variables;
    }

    public string Text { get; }

    public ExpressionValueType ResultType { get; }

    /// <summary>The variable names the expression reads.</summary>
    public IReadOnlySet<string> Variables { get; }

    internal Node Root { get; }

    /// <summary>Evaluates with the given variable values (missing names are null). <paramref name="today"/> feeds <c>today()</c>.</summary>
    public ExpressionEvaluation Evaluate(IReadOnlyDictionary<string, object?> values, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var lookup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            lookup[key] = ExpressionEngine.Normalise(value);
        }

        var evaluator = new Evaluator(lookup, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        try
        {
            return new ExpressionEvaluation(evaluator.Eval(Root), null);
        }
        catch (ExpressionRuntimeException ex)
        {
            return new ExpressionEvaluation(null, ex.Error);
        }
    }
}

// ---------------------------------------------------------------- tokens

internal enum TokenKind
{
    Number,
    Text,
    Identifier,
    Operator,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    End,
}

internal sealed record Token(TokenKind Kind, string Value, int Position, decimal Number = 0m);

internal static class Lexer
{
    private static readonly string[] Operators = ["==", "!=", "<>", "<=", ">=", "&&", "||", "+", "-", "*", "/", "%", "<", ">", "=", "!"];

    public static List<Token> Tokenize(string text, out ExpressionError? error)
    {
        error = null;
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                var raw = text[start..i];
                if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    error = ExpressionError.Of("invalidNumber", start, ("text", raw));
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Number, raw, start, number));
                continue;
            }

            if (c is '\'' or '"')
            {
                var quote = c;
                var builder = new System.Text.StringBuilder();
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == quote)
                    {
                        if (i + 1 < text.Length && text[i + 1] == quote)
                        {
                            builder.Append(quote);
                            i += 2;
                            continue;
                        }

                        closed = true;
                        i++;
                        break;
                    }

                    builder.Append(text[i]);
                    i++;
                }

                if (!closed)
                {
                    error = ExpressionError.Of("unterminatedText", start);
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Text, builder.ToString(), start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i].TrimEnd('.'), start));
                continue;
            }

            var single = c switch
            {
                '(' => TokenKind.LeftParen,
                ')' => TokenKind.RightParen,
                '[' => TokenKind.LeftBracket,
                ']' => TokenKind.RightBracket,
                ',' => TokenKind.Comma,
                _ => (TokenKind?)null,
            };
            if (single is { } kind)
            {
                tokens.Add(new Token(kind, c.ToString(), start));
                i++;
                continue;
            }

            var op = Operators.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0);
            if (op is null)
            {
                error = ExpressionError.Of("unexpectedCharacter", start, ("text", c.ToString()));
                return tokens;
            }

            tokens.Add(new Token(TokenKind.Operator, op, start));
            i += op.Length;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }
}

// ---------------------------------------------------------------- syntax tree

internal abstract record Node(int Position);

internal sealed record LiteralNode(int Position, object? Value, ExpressionValueType Type) : Node(Position);

internal sealed record VariableNode(int Position, string Name) : Node(Position)
{
    public ExpressionValueType Type { get; set; }
}

internal sealed record UnaryNode(int Position, string Operator, Node Operand) : Node(Position);

internal sealed record BinaryNode(int Position, string Operator, Node Left, Node Right) : Node(Position);

internal sealed record InNode(int Position, Node Value, List<Node> Items, bool Negated) : Node(Position);

internal sealed record CallNode(int Position, string Name, List<Node> Arguments) : Node(Position);

internal sealed class ParseException(ExpressionError error) : Exception(error.Code)
{
    public ExpressionError Error { get; } = error;
}

/// <summary>Recursive descent: or → and → not → comparison / in → additive → multiplicative → unary minus → primary.</summary>
internal sealed class Parser(List<Token> tokens)
{
    private int _index;
    private int _nodes;
    private int _depth;

    private Token Current => tokens[_index];

    public Node? ParseAll(out ExpressionError? error)
    {
        error = null;
        try
        {
            var node = ParseOr();
            if (Current.Kind != TokenKind.End)
            {
                throw Unexpected();
            }

            return node;
        }
        catch (ParseException ex)
        {
            error = ex.Error;
            return null;
        }
    }

    private T Count<T>(T node)
        where T : Node
    {
        if (++_nodes > ExpressionLimits.MaxNodes)
        {
            throw new ParseException(ExpressionError.Of("tooComplex", node.Position, ("max", ExpressionLimits.MaxNodes)));
        }

        return node;
    }

    private ParseException Unexpected() => Current.Kind == TokenKind.End
        ? new ParseException(ExpressionError.Of("unexpectedEnd", Current.Position))
        : new ParseException(ExpressionError.Of("syntax", Current.Position, ("text", Current.Value)));

    private bool IsKeyword(string word) =>
        Current.Kind == TokenKind.Identifier && string.Equals(Current.Value, word, StringComparison.OrdinalIgnoreCase);

    private bool IsOperator(params string[] ops) => Current.Kind == TokenKind.Operator && ops.Contains(Current.Value, StringComparer.Ordinal);

    private Token Next() => tokens[_index++];

    private Node Enter(Func<Node> parse)
    {
        if (++_depth > ExpressionLimits.MaxDepth)
        {
            throw new ParseException(ExpressionError.Of("tooComplex", Current.Position, ("max", ExpressionLimits.MaxDepth)));
        }

        try
        {
            return parse();
        }
        finally
        {
            _depth--;
        }
    }

    private Node ParseOr() => Enter(() =>
    {
        var left = ParseAnd();
        while (IsKeyword("or") || IsOperator("||"))
        {
            var position = Next().Position;
            left = Count(new BinaryNode(position, "or", left, ParseAnd()));
        }

        return left;
    });

    private Node ParseAnd()
    {
        var left = ParseNot();
        while (IsKeyword("and") || IsOperator("&&"))
        {
            var position = Next().Position;
            left = Count(new BinaryNode(position, "and", left, ParseNot()));
        }

        return left;
    }

    private Node ParseNot()
    {
        if (IsKeyword("not") || IsOperator("!"))
        {
            var position = Next().Position;
            return Count(new UnaryNode(position, "not", Enter(ParseNot)));
        }

        return ParseComparison();
    }

    private Node ParseComparison()
    {
        var left = ParseAdditive();
        if (IsOperator("==", "=", "!=", "<>", "<", "<=", ">", ">="))
        {
            var token = Next();
            var op = token.Value switch { "=" => "==", "<>" => "!=", _ => token.Value };
            return Count(new BinaryNode(token.Position, op, left, ParseAdditive()));
        }

        var negated = false;
        if (IsKeyword("not") && _index + 1 < tokens.Count && tokens[_index + 1] is { Kind: TokenKind.Identifier } after
            && string.Equals(after.Value, "in", StringComparison.OrdinalIgnoreCase))
        {
            Next();
            negated = true;
        }

        if (IsKeyword("in"))
        {
            var position = Next().Position;
            if (Current.Kind is not (TokenKind.LeftBracket or TokenKind.LeftParen))
            {
                throw Unexpected();
            }

            var close = Next().Kind == TokenKind.LeftBracket ? TokenKind.RightBracket : TokenKind.RightParen;
            var items = new List<Node>();
            if (Current.Kind != close)
            {
                items.Add(ParseAdditive());
                while (Current.Kind == TokenKind.Comma)
                {
                    Next();
                    items.Add(ParseAdditive());
                }
            }

            if (Current.Kind != close)
            {
                throw Unexpected();
            }

            Next();
            return Count(new InNode(position, left, items, negated));
        }

        if (negated)
        {
            throw Unexpected();
        }

        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (IsOperator("+", "-"))
        {
            var token = Next();
            left = Count(new BinaryNode(token.Position, token.Value, left, ParseMultiplicative()));
        }

        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParseUnary();
        while (IsOperator("*", "/", "%"))
        {
            var token = Next();
            left = Count(new BinaryNode(token.Position, token.Value, left, ParseUnary()));
        }

        return left;
    }

    private Node ParseUnary()
    {
        if (IsOperator("-"))
        {
            var position = Next().Position;
            return Count(new UnaryNode(position, "-", Enter(ParseUnary)));
        }

        if (IsOperator("+"))
        {
            Next();
            return Enter(ParseUnary);
        }

        return ParsePrimary();
    }

    private Node ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Next();
                return Count(new LiteralNode(token.Position, token.Number, ExpressionValueType.Number));
            case TokenKind.Text:
                Next();
                return Count(new LiteralNode(token.Position, token.Value, ExpressionValueType.Text));
            case TokenKind.LeftParen:
                Next();
                var inner = ParseOr();
                if (Current.Kind != TokenKind.RightParen)
                {
                    throw Unexpected();
                }

                Next();
                return inner;
            case TokenKind.Identifier:
                Next();
                switch (token.Value.ToLowerInvariant())
                {
                    case "true":
                        return Count(new LiteralNode(token.Position, true, ExpressionValueType.Boolean));
                    case "false":
                        return Count(new LiteralNode(token.Position, false, ExpressionValueType.Boolean));
                    case "null":
                        return Count(new LiteralNode(token.Position, null, ExpressionValueType.Null));
                    case "and" or "or" or "not" or "in":
                        _index--;
                        throw Unexpected();
                }

                if (Current.Kind != TokenKind.LeftParen)
                {
                    return Count(new VariableNode(token.Position, token.Value));
                }

                Next();
                var arguments = new List<Node>();
                if (Current.Kind != TokenKind.RightParen)
                {
                    arguments.Add(ParseOr());
                    while (Current.Kind == TokenKind.Comma)
                    {
                        Next();
                        arguments.Add(ParseOr());
                    }
                }

                if (Current.Kind != TokenKind.RightParen)
                {
                    throw Unexpected();
                }

                Next();
                return Count(new CallNode(token.Position, token.Value.ToLowerInvariant(), arguments));
            default:
                throw Unexpected();
        }
    }
}

// ---------------------------------------------------------------- functions

internal sealed record FunctionSignature(string Name, int MinArgs, int MaxArgs, Func<IReadOnlyList<ExpressionValueType>, ExpressionValueType?> Result);

internal static class Functions
{
    private static ExpressionValueType? All(IReadOnlyList<ExpressionValueType> args, ExpressionValueType wanted, ExpressionValueType result) =>
        args.All(a => Fits(a, wanted)) ? result : null;

    internal static bool Fits(ExpressionValueType actual, ExpressionValueType wanted) =>
        actual == wanted || actual is ExpressionValueType.Any or ExpressionValueType.Null || wanted == ExpressionValueType.Any;

    private static ExpressionValueType Common(IEnumerable<ExpressionValueType> types)
    {
        var known = types.Where(t => t is not (ExpressionValueType.Any or ExpressionValueType.Null)).Distinct().ToList();
        return known.Count == 1 ? known[0] : known.Count == 0 && types.All(t => t == ExpressionValueType.Null) ? ExpressionValueType.Null : ExpressionValueType.Any;
    }

    public static readonly IReadOnlyDictionary<string, FunctionSignature> Table = new Dictionary<string, FunctionSignature>(StringComparer.Ordinal)
    {
        ["if"] = new("if", 3, 3, a => Fits(a[0], ExpressionValueType.Boolean) ? Common([a[1], a[2]]) : null),
        ["min"] = new("min", 1, 20, a => All(a, ExpressionValueType.Number, ExpressionValueType.Number)),
        ["max"] = new("max", 1, 20, a => All(a, ExpressionValueType.Number, ExpressionValueType.Number)),
        ["abs"] = new("abs", 1, 1, a => All(a, ExpressionValueType.Number, ExpressionValueType.Number)),
        ["round"] = new("round", 1, 2, a => All(a, ExpressionValueType.Number, ExpressionValueType.Number)),
        ["coalesce"] = new("coalesce", 1, 20, a => Common(a)),
        ["isempty"] = new("isEmpty", 1, 1, _ => ExpressionValueType.Boolean),
        ["contains"] = new("contains", 2, 2, a => All(a, ExpressionValueType.Text, ExpressionValueType.Boolean)),
        ["startswith"] = new("startsWith", 2, 2, a => All(a, ExpressionValueType.Text, ExpressionValueType.Boolean)),
        ["lower"] = new("lower", 1, 1, a => All(a, ExpressionValueType.Text, ExpressionValueType.Text)),
        ["upper"] = new("upper", 1, 1, a => All(a, ExpressionValueType.Text, ExpressionValueType.Text)),
        ["len"] = new("len", 1, 1, a => All(a, ExpressionValueType.Text, ExpressionValueType.Number)),
        ["today"] = new("today", 0, 0, _ => ExpressionValueType.Date),
        ["date"] = new("date", 1, 1, a => All(a, ExpressionValueType.Text, ExpressionValueType.Date)),
        ["daysbetween"] = new("daysBetween", 2, 2, a => All(a, ExpressionValueType.Date, ExpressionValueType.Number)),
    };

    /// <summary>Function names as shown in the editor.</summary>
    public static IEnumerable<string> Names => Table.Values.Select(f => f.Name);
}

/// <summary>Public list of the function names, for editors and documentation.</summary>
public static class ExpressionFunctions
{
    public static IReadOnlyList<string> Names { get; } = Functions.Names.ToList();
}

// ---------------------------------------------------------------- type check

internal static class TypeChecker
{
    public static ExpressionValueType Check(Node node, IReadOnlyDictionary<string, ExpressionValueType> variables, List<ExpressionError> errors)
    {
        ExpressionValueType Mismatch(Node at, string op, params ExpressionValueType[] actual)
        {
            errors.Add(ExpressionError.Of("typeMismatch", at.Position, ("operator", op), ("actual", string.Join(", ", actual.Select(ExpressionEngine.Code)))));
            return ExpressionValueType.Any;
        }

        switch (node)
        {
            case LiteralNode literal:
                return literal.Type;
            case VariableNode variable:
                if (!variables.TryGetValue(variable.Name, out var type))
                {
                    errors.Add(ExpressionError.Of("unknownVariable", variable.Position, ("name", variable.Name)));
                    return ExpressionValueType.Any;
                }

                variable.Type = type;
                return type;
            case UnaryNode unary:
                {
                    var operand = Check(unary.Operand, variables, errors);
                    var wanted = unary.Operator == "not" ? ExpressionValueType.Boolean : ExpressionValueType.Number;
                    return Functions.Fits(operand, wanted) ? wanted : Mismatch(unary, unary.Operator, operand);
                }

            case BinaryNode binary:
                {
                    var left = Check(binary.Left, variables, errors);
                    var right = Check(binary.Right, variables, errors);
                    switch (binary.Operator)
                    {
                        case "and" or "or":
                            return Functions.Fits(left, ExpressionValueType.Boolean) && Functions.Fits(right, ExpressionValueType.Boolean)
                                ? ExpressionValueType.Boolean
                                : Mismatch(binary, binary.Operator, left, right);
                        case "==" or "!=":
                            return Comparable(left, right) ? ExpressionValueType.Boolean : Mismatch(binary, binary.Operator, left, right);
                        case "<" or "<=" or ">" or ">=":
                            return Comparable(left, right) && left != ExpressionValueType.Boolean && right != ExpressionValueType.Boolean
                                ? ExpressionValueType.Boolean
                                : Mismatch(binary, binary.Operator, left, right);
                        case "+":
                            if (left == ExpressionValueType.Text || right == ExpressionValueType.Text)
                            {
                                return ExpressionValueType.Text; // text concatenation takes any value
                            }

                            return Functions.Fits(left, ExpressionValueType.Number) && Functions.Fits(right, ExpressionValueType.Number)
                                ? (left is ExpressionValueType.Any || right is ExpressionValueType.Any ? ExpressionValueType.Any : ExpressionValueType.Number)
                                : Mismatch(binary, binary.Operator, left, right);
                        default:
                            return Functions.Fits(left, ExpressionValueType.Number) && Functions.Fits(right, ExpressionValueType.Number)
                                ? ExpressionValueType.Number
                                : Mismatch(binary, binary.Operator, left, right);
                    }
                }

            case InNode @in:
                {
                    var value = Check(@in.Value, variables, errors);
                    foreach (var item in @in.Items)
                    {
                        var itemType = Check(item, variables, errors);
                        if (!Comparable(value, itemType))
                        {
                            Mismatch(item, "in", value, itemType);
                        }
                    }

                    return ExpressionValueType.Boolean;
                }

            case CallNode call:
                {
                    var args = call.Arguments.Select(a => Check(a, variables, errors)).ToList();
                    if (!Functions.Table.TryGetValue(call.Name, out var signature))
                    {
                        errors.Add(ExpressionError.Of("unknownFunction", call.Position, ("name", call.Name)));
                        return ExpressionValueType.Any;
                    }

                    if (args.Count < signature.MinArgs || args.Count > signature.MaxArgs)
                    {
                        errors.Add(ExpressionError.Of("argumentCount", call.Position, ("name", signature.Name), ("min", signature.MinArgs), ("max", signature.MaxArgs)));
                        return ExpressionValueType.Any;
                    }

                    return signature.Result(args) ?? Mismatch(call, signature.Name, [.. args]);
                }

            default:
                return ExpressionValueType.Any;
        }
    }

    private static bool Comparable(ExpressionValueType a, ExpressionValueType b) =>
        a == b || a is ExpressionValueType.Any or ExpressionValueType.Null || b is ExpressionValueType.Any or ExpressionValueType.Null;
}

// ---------------------------------------------------------------- evaluation

internal sealed class ExpressionRuntimeException(ExpressionError error) : Exception(error.Code)
{
    public ExpressionError Error { get; } = error;
}

internal sealed class Evaluator(IReadOnlyDictionary<string, object?> values, DateOnly today)
{
    private int _steps;

    private static ExpressionRuntimeException Fail(Node at, string code, params (string, object?)[] parameters) =>
        new(ExpressionError.Of(code, at.Position, parameters));

    public object? Eval(Node node)
    {
        if (++_steps > ExpressionLimits.MaxSteps)
        {
            throw Fail(node, "tooComplex", ("max", ExpressionLimits.MaxSteps));
        }

        return node switch
        {
            LiteralNode literal => literal.Value,
            VariableNode variable => values.TryGetValue(variable.Name, out var value) ? value : null,
            UnaryNode unary => Unary(unary),
            BinaryNode binary => Binary(binary),
            InNode @in => In(@in),
            CallNode call => Call(call),
            _ => throw Fail(node, "syntax", ("text", string.Empty)),
        };
    }

    private object? Unary(UnaryNode node)
    {
        var operand = Eval(node.Operand);
        if (node.Operator == "not")
        {
            return operand switch
            {
                null => true,
                bool b => !b,
                _ => throw Fail(node, "typeMismatch", ("operator", "not")),
            };
        }

        return operand switch
        {
            null => null,
            decimal d => -d,
            _ => throw Fail(node, "typeMismatch", ("operator", "-")),
        };
    }

    private object? Binary(BinaryNode node)
    {
        if (node.Operator is "and" or "or")
        {
            var left = Truth(node, Eval(node.Left));
            if (node.Operator == "and" ? !left : left)
            {
                return left;
            }

            return Truth(node, Eval(node.Right));
        }

        var a = Eval(node.Left);
        var b = Eval(node.Right);
        switch (node.Operator)
        {
            case "==":
                return AreEqual(a, b);
            case "!=":
                return !AreEqual(a, b);
            case "<" or "<=" or ">" or ">=":
                if (a is null || b is null)
                {
                    return false;
                }

                var order = Compare(node, a, b);
                return node.Operator switch { "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 };
        }

        if (a is null || b is null)
        {
            return null;
        }

        if (node.Operator == "+" && (a is string || b is string))
        {
            return Text(a) + Text(b);
        }

        if (a is not decimal x || b is not decimal y)
        {
            throw Fail(node, "typeMismatch", ("operator", node.Operator));
        }

        try
        {
            return node.Operator switch
            {
                "+" => x + y,
                "-" => x - y,
                "*" => x * y,
                "/" => y == 0 ? throw Fail(node, "divisionByZero") : x / y,
                "%" => y == 0 ? throw Fail(node, "divisionByZero") : x % y,
                _ => throw Fail(node, "syntax", ("text", node.Operator)),
            };
        }
        catch (OverflowException)
        {
            throw Fail(node, "overflow");
        }
    }

    private bool In(InNode node)
    {
        var value = Eval(node.Value);
        var found = node.Items.Any(item => AreEqual(value, Eval(item)));
        return node.Negated ? !found : found;
    }

    private object? Call(CallNode node)
    {
        switch (node.Name)
        {
            case "if":
                return Truth(node, Eval(node.Arguments[0])) ? Eval(node.Arguments[1]) : Eval(node.Arguments[2]);
            case "coalesce":
                foreach (var argument in node.Arguments)
                {
                    if (Eval(argument) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case "today":
                return today;
        }

        var args = node.Arguments.Select(Eval).ToList();
        switch (node.Name)
        {
            case "isempty":
                return args[0] is null || (args[0] is string s && s.Trim().Length == 0);
            case "min" or "max":
                var numbers = args.OfType<decimal>().ToList();
                return numbers.Count == 0 ? null : node.Name == "min" ? numbers.Min() : numbers.Max();
        }

        if (args.Any(a => a is null))
        {
            return node.Name is "contains" or "startswith" ? false : null;
        }

        return node.Name switch
        {
            "abs" => Math.Abs(Number(node, args[0])),
            "round" => Math.Round(Number(node, args[0]), args.Count > 1 ? (int)Math.Clamp(Number(node, args[1]), 0, 10) : 0, MidpointRounding.AwayFromZero),
            "contains" => Str(node, args[0]).Contains(Str(node, args[1]), StringComparison.OrdinalIgnoreCase),
            "startswith" => Str(node, args[0]).StartsWith(Str(node, args[1]), StringComparison.OrdinalIgnoreCase),
            "lower" => Str(node, args[0]).ToLowerInvariant(),
            "upper" => Str(node, args[0]).ToUpperInvariant(),
            "len" => (decimal)Str(node, args[0]).Length,
            "date" => DateOnly.TryParseExact(Str(node, args[0]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : throw Fail(node, "invalidDate", ("text", args[0])),
            "daysbetween" => (decimal)(Date(node, args[1]).DayNumber - Date(node, args[0]).DayNumber),
            _ => throw Fail(node, "unknownFunction", ("name", node.Name)),
        };
    }

    private static bool Truth(Node at, object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => throw Fail(at, "typeMismatch", ("operator", "boolean")),
    };

    private static bool AreEqual(object? a, object? b) => (a, b) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase),
        _ => a.Equals(b),
    };

    private static int Compare(Node at, object a, object b) => (a, b) switch
    {
        (decimal x, decimal y) => x.CompareTo(y),
        (DateOnly x, DateOnly y) => x.CompareTo(y),
        (string x, string y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase),
        _ => throw Fail(at, "typeMismatch", ("operator", "compare")),
    };

    private static string Text(object value) => value switch
    {
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? string.Empty,
    };

    private static decimal Number(Node at, object? value) => value as decimal? ?? throw Fail(at, "typeMismatch", ("operator", "number"));

    private static string Str(Node at, object? value) => value as string ?? throw Fail(at, "typeMismatch", ("operator", "text"));

    private static DateOnly Date(Node at, object? value) => value as DateOnly? ?? throw Fail(at, "typeMismatch", ("operator", "date"));
}
