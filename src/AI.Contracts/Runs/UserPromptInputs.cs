namespace AI.Contracts.Runs;

using System.Globalization;
using System.Text.RegularExpressions;
using Schedules;

/// <summary>
/// The control a question is answered with when the answer is a value rather than a choice. One
/// shape for every kind, so the model learns one object, and the kind decides which fields count.
/// </summary>
/// <param name="Kind">One of <see cref="UserPromptInputKind"/>.</param>
/// <param name="Min">The low end of a number, range or scale.</param>
/// <param name="Max">The high end of a number, range or scale.</param>
/// <param name="Step">The step a slider moves by; chosen from the span when left out.</param>
/// <param name="Unit">A short unit shown beside the value and in the answer: "ms", "%", "files".</param>
/// <param name="Marks">Labelled points along a slider or scale: "Off", "Default", "Critical".</param>
/// <param name="Multiline">A text answer of several lines.</param>
/// <param name="Language">Text is code or configuration in this language and is edited in monospace.</param>
/// <param name="Pattern">A .NET regular expression the whole text answer must match.</param>
/// <param name="Placeholder">A hint shown in an empty text box or search box.</param>
/// <param name="MaxLength">The longest text answer accepted.</param>
public sealed record UserPromptInput(
    string Kind,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? Unit = null,
    IReadOnlyList<UserPromptMark>? Marks = null,
    bool Multiline = false,
    string? Language = null,
    string? Pattern = null,
    string? Placeholder = null,
    int? MaxLength = null);

/// <summary>A labelled point on a slider or scale.</summary>
public sealed record UserPromptMark(double Value, string Label);

/// <summary>The kinds of <see cref="UserPromptInput"/>.</summary>
public static class UserPromptInputKind
{
    /// <summary>One number on a slider with a box beside it for an exact value.</summary>
    public const string Number = "number";

    /// <summary>Two numbers, the ends of a span, on a slider with two handles.</summary>
    public const string Range = "range";

    /// <summary>A rating of a few whole steps, 1–5 by default, with words at its marks.</summary>
    public const string Scale = "scale";

    /// <summary>The options put in order; every option comes back, first to last.</summary>
    public const string Rank = "rank";

    /// <summary>A long list of options filtered by typing.</summary>
    public const string Search = "search";

    /// <summary>A colour, from swatches the options offer or from a colour picker.</summary>
    public const string Color = "color";

    /// <summary>Text: one line, several lines or code, optionally checked against a pattern.</summary>
    public const string Text = "text";

    public static readonly IReadOnlyList<string> All = [Number, Range, Scale, Rank, Search, Color, Text];
}

/// <summary>What a preview line can read from the answer as it stands.</summary>
/// <param name="Values">The answer's values: a number, the two ends of a range, a colour, picked values.</param>
/// <param name="Labels">The labels of the chosen options, in order.</param>
/// <param name="Text">Typed text, or a single path.</param>
/// <param name="Count">How many things are chosen: options, values or paths.</param>
public sealed record UserPromptPreviewValues(IReadOnlyList<string> Values, IReadOnlyList<string> Labels, string? Text, int Count);

/// <summary>
/// The rules of every value-like answer to <c>ask_user</c>, shared by the Host, which checks what
/// the model asked and what came back, and the Web, which draws the controls and the preview: one
/// set of rules, so the card never offers what the Host would then drop.
/// </summary>
public interface IUserPromptInputs
{
    /// <summary>The most options the question may offer.</summary>
    int MaxOptions(UserPromptQuestion question);

    /// <summary>What is wrong with the question's input, defaults and preview, said so the model can fix it; null when nothing is.</summary>
    string? Validate(UserPromptQuestion question);

    /// <summary>
    /// The one spelling of a value the question can return, or null when it cannot return it: a
    /// number off its slider, a colour that is not one, text the pattern refuses.
    /// </summary>
    string? Normalize(UserPromptQuestion question, string? value);

    /// <summary>
    /// The values an option's <c>value</c> stands for, normalized: the two ends of a range preset
    /// written "10..20", one value for every other kind; null when the question cannot return it.
    /// </summary>
    IReadOnlyList<string>? PresetValues(UserPromptQuestion question, string? value);

    /// <summary>The effective step of a number, range or scale.</summary>
    double StepOf(UserPromptInput input);

    /// <summary>The answer in words — "42 ms", "10–20 ms", "4 — Important" — or null when its values already say it.</summary>
    string? Describe(UserPromptQuestion question, IReadOnlyList<string> values);

    /// <summary>A value as the person reads it: grouped digits and the unit.</summary>
    string Format(UserPromptInput input, double value);

    /// <summary>What is wrong with a preview template, or null.</summary>
    string? ValidatePreview(string preview);

    /// <summary>The preview line for the answer as it stands, or null while it cannot be worked out.</summary>
    string? RenderPreview(string preview, UserPromptPreviewValues values);
}

public sealed class UserPromptInputs(IScheduleCalendar calendar) : IUserPromptInputs
{
    private const int DefaultMaxOptions = 8;
    private const int MaxSearchOptions = 200;
    private const int MaxRankOptions = 20;
    private const int MaxMarks = 11;
    private const int MaxMarkLength = 24;
    private const int MaxUnitLength = 12;
    private const int MaxLanguageLength = 24;
    private const int MaxPlaceholderLength = 80;
    private const int MaxPatternLength = 300;
    private const int MaxPreviewLength = 200;
    private const int MaxSliderSteps = 100_000;
    private const int MaxScaleSteps = 10;
    private const int DefaultTextLength = 4000;
    private const int MaxTextLength = 20_000;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Hex = new("^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.CultureInvariant);
    private static readonly string[] PreviewNames = ["value", "from", "to", "count", "label", "text"];

    public int MaxOptions(UserPromptQuestion question) => question.Input?.Kind switch
    {
        UserPromptInputKind.Search => MaxSearchOptions,
        UserPromptInputKind.Rank => MaxRankOptions,
        _ => DefaultMaxOptions
    };

    public string? Validate(UserPromptQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var id = question.Id;
        if (question.Input is { } input && ValidateInput(question, input) is { } invalid) return invalid;
        if (question.Preview is { } preview && ValidatePreview(preview) is { } badPreview)
            return $"The 'preview' of '{id}' is not usable: {badPreview}";
        if (question.Defaults is not { Count: > 0 } defaults) return null;

        var kind = question.Input?.Kind;
        if (kind is UserPromptInputKind.Rank)
            return $"Question '{id}' is a rank: put its options in the order you recommend instead of giving a 'default'.";
        if (kind is UserPromptInputKind.Search
            || kind is null && question.PickerKind is null && question.PathKind is null && question.Options.Count > 0)
            return $"Question '{id}' answers with options: mark the default with recommended=true instead of 'default'.";
        var expected = kind == UserPromptInputKind.Range ? 2 : question.MultiSelect || question.PathKind == "directories" ? defaults.Count : 1;
        if (defaults.Count != expected)
            return kind == UserPromptInputKind.Range
                ? $"The 'default' of '{id}' must be the two ends of the range, such as [10, 20]."
                : $"The 'default' of '{id}' must be one value.";
        foreach (var value in defaults)
            if (Normalize(question, value) is null)
                return $"The 'default' value '{value}' of '{id}' is not one this question can return{Expectation(question)}.";
        return null;
    }

    public IReadOnlyList<string>? PresetValues(UserPromptQuestion question, string? value)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (question.Input?.Kind == UserPromptInputKind.Range)
            return RangeEnds(question, value) is var (from, to) ? [from, to] : null;
        return Normalize(question, value) is { } single ? [single] : null;
    }

    private string? ValidateInput(UserPromptQuestion question, UserPromptInput input)
    {
        var id = question.Id;
        if (!UserPromptInputKind.All.Contains(input.Kind))
            return $"The 'input.kind' of '{id}' must be one of: {string.Join(", ", UserPromptInputKind.All)}.";
        if (question.PathKind is { Length: > 0 } || question.PickerKind is { Length: > 0 })
            return $"Question '{id}' cannot combine 'input' with 'pathKind' or 'pickerKind'.";
        if (question.MultiSelect && input.Kind != UserPromptInputKind.Search)
            return $"Question '{id}' cannot use 'multiSelect' with input kind '{input.Kind}'; only 'search' combines choices.";
        if (input.Unit is { Length: > MaxUnitLength }) return $"The 'unit' of '{id}' is longer than {MaxUnitLength} characters.";
        var options = question.Options;

        switch (input.Kind)
        {
            case UserPromptInputKind.Number or UserPromptInputKind.Range:
            {
                if (input.Min is not { } min || input.Max is not { } max || !double.IsFinite(min) || !double.IsFinite(max))
                    return $"Question '{id}' needs finite 'min' and 'max' for its {input.Kind} slider.";
                if (min >= max) return $"The 'min' of '{id}' must be less than its 'max'.";
                if (input.Step is { } step && (!double.IsFinite(step) || step <= 0)) return $"The 'step' of '{id}' must be greater than zero.";
                if ((max - min) / StepOf(input) > MaxSliderSteps) return $"The 'step' of '{id}' is too small for its span; use at most {MaxSliderSteps} steps.";
                if (ValidateMarks(id, input, min, max) is { } marks) return marks;
                foreach (var option in options)
                {
                    if (string.IsNullOrWhiteSpace(option.Value))
                        return $"Option '{option.Label}' of '{id}' is a preset of the slider and needs a 'value'"
                               + (input.Kind == UserPromptInputKind.Range ? " such as '10..20'." : " such as '42'.");
                    if (input.Kind == UserPromptInputKind.Range ? RangeEnds(question, option.Value) is null : Normalize(question, option.Value) is null)
                        return $"The value of option '{option.Label}' in '{id}' is not on the slider{Expectation(question)}.";
                }
                return null;
            }
            case UserPromptInputKind.Scale:
            {
                var min = input.Min ?? 1;
                var max = input.Max ?? 5;
                if (min != Math.Floor(min) || max != Math.Floor(max) || max - min < 1 || max - min > MaxScaleSteps)
                    return $"The scale of '{id}' needs whole 'min' and 'max' at most {MaxScaleSteps} apart, such as 1 and 5.";
                if (input.Step is { } step && step != 1) return $"A scale moves in whole steps; leave out the 'step' of '{id}'.";
                if (options.Count > 0) return $"Question '{id}' is a scale: name its points with 'marks' instead of options.";
                return ValidateMarks(id, input, min, max);
            }
            case UserPromptInputKind.Rank:
                return options.Count < 2 ? $"Question '{id}' is a rank and needs at least two options to order." : null;
            case UserPromptInputKind.Search:
                return options.Count == 0 ? $"Question '{id}' is a search and needs the options to search through." : null;
            case UserPromptInputKind.Color:
                foreach (var option in options)
                    if (Normalize(question, option.Value) is null)
                        return $"Option '{option.Label}' of '{id}' is a swatch and needs a 'value' such as '#3b82f6'.";
                return null;
            case UserPromptInputKind.Text:
                if (input.MaxLength is < 1 or > MaxTextLength) return $"The 'maxLength' of '{id}' must be 1..{MaxTextLength}.";
                if (input.Language is { Length: > MaxLanguageLength }) return $"The 'language' of '{id}' is longer than {MaxLanguageLength} characters.";
                if (input.Placeholder is { Length: > MaxPlaceholderLength }) return $"The 'placeholder' of '{id}' is longer than {MaxPlaceholderLength} characters.";
                if (input.Pattern is { } pattern)
                {
                    if (pattern.Length > MaxPatternLength) return $"The 'pattern' of '{id}' is longer than {MaxPatternLength} characters.";
                    try { _ = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout); }
                    catch (ArgumentException error) { return $"The 'pattern' of '{id}' is not a valid regular expression: {error.Message}"; }
                }
                return null;
        }
        return null;
    }

    private static string? ValidateMarks(string id, UserPromptInput input, double min, double max)
    {
        if (input.Marks is not { Count: > 0 } marks) return null;
        if (marks.Count > MaxMarks) return $"Question '{id}' has more than {MaxMarks} marks.";
        foreach (var mark in marks)
        {
            if (string.IsNullOrWhiteSpace(mark.Label) || mark.Label.Length > MaxMarkLength)
                return $"Every mark of '{id}' needs a label of at most {MaxMarkLength} characters.";
            if (!double.IsFinite(mark.Value) || mark.Value < min || mark.Value > max)
                return $"The mark '{mark.Label}' of '{id}' lies outside {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}.";
        }
        return null;
    }

    private static string Expectation(UserPromptQuestion question) => question.Input?.Kind switch
    {
        UserPromptInputKind.Number or UserPromptInputKind.Scale or UserPromptInputKind.Range =>
            $": use a number within {question.Input.Min?.ToString(CultureInfo.InvariantCulture) ?? "1"}..{question.Input.Max?.ToString(CultureInfo.InvariantCulture) ?? "5"} on its step",
        UserPromptInputKind.Color => ": use #rrggbb",
        UserPromptInputKind.Text => ": it must fit 'maxLength' and 'pattern'",
        _ => question.PickerKind switch
        {
            SchedulePickers.Date => ": use yyyy-MM-dd",
            SchedulePickers.Time => ": use HH:mm",
            SchedulePickers.Recurrence => ": use recurrence JSON",
            _ => string.Empty
        }
    };

    public string? Normalize(UserPromptQuestion question, string? value)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (value is null) return null;
        if (question.Input is not { } input)
        {
            if (SchedulePickers.IsSchedulePicker(question.PickerKind)) return SchedulePickers.Normalize(question.PickerKind, value, calendar);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        switch (input.Kind)
        {
            case UserPromptInputKind.Number or UserPromptInputKind.Range or UserPromptInputKind.Scale:
            {
                if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return null;
                var (min, max) = Bounds(input);
                var step = StepOf(input);
                var snapped = min + Math.Round((number - min) / step) * step;
                // A value a hair off its step is the slider's own rounding, not a different answer.
                if (Math.Abs(snapped - number) > step / 1000 || snapped < min - step / 1000 || snapped > max + step / 1000) return null;
                return Invariant(Math.Clamp(snapped, min, max));
            }
            case UserPromptInputKind.Color:
            {
                var match = Hex.Match(value.Trim());
                if (!match.Success) return null;
                var hex = match.Groups[1].Value.ToLowerInvariant();
                return "#" + (hex.Length == 3 ? string.Concat(hex.Select(digit => $"{digit}{digit}")) : hex);
            }
            case UserPromptInputKind.Text:
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                // Multiline text keeps its indentation: it may be code, where the first spaces matter.
                var text = input.Multiline ? value.TrimEnd() : value.Trim();
                if (text.Length > (input.MaxLength ?? DefaultTextLength)) return null;
                if (input.Pattern is { } pattern)
                {
                    try
                    {
                        if (!Regex.IsMatch(text, $"^(?:{pattern})$", RegexOptions.CultureInvariant, PatternTimeout)) return null;
                    }
                    catch (Exception error) when (error is ArgumentException or RegexMatchTimeoutException)
                    {
                        return null;
                    }
                }
                return text;
            }
            default:
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>The two ends of a range preset written as "10..20", or null.</summary>
    private (string From, string To)? RangeEnds(UserPromptQuestion question, string value)
    {
        var parts = value.Split("..", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || Normalize(question, parts[0]) is not { } from || Normalize(question, parts[1]) is not { } to) return null;
        return double.Parse(from, CultureInfo.InvariantCulture) <= double.Parse(to, CultureInfo.InvariantCulture) ? (from, to) : (to, from);
    }

    public double StepOf(UserPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind == UserPromptInputKind.Scale) return 1;
        if (input.Step is > 0 and var step && double.IsFinite(step)) return step;
        var (min, max) = Bounds(input);
        var span = max - min;
        if (span <= 0 || !double.IsFinite(span)) return 1;
        // Whole numbers for a span of whole numbers big enough to move through in whole steps;
        // otherwise a round step of about a hundredth of the span.
        if (span >= 10 && min == Math.Floor(min) && max == Math.Floor(max)) return 1;
        var rough = span / 100;
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        return (rough / power) switch { <= 1 => 1, <= 2 => 2, <= 5 => 5, _ => 10 } * power;
    }

    private static (double Min, double Max) Bounds(UserPromptInput input) =>
        input.Kind == UserPromptInputKind.Scale ? (input.Min ?? 1, input.Max ?? 5) : (input.Min ?? 0, input.Max ?? 100);

    public string? Describe(UserPromptQuestion question, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(values);
        if (question.Input is not { } input || values.Count == 0) return null;
        var numbers = values.Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null).ToArray();
        if (numbers.Any(number => number is null)) return null;
        switch (input.Kind)
        {
            case UserPromptInputKind.Number:
                return Format(input, numbers[0]!.Value);
            case UserPromptInputKind.Range when numbers.Length == 2:
                return $"{Number(numbers[0]!.Value)}–{Format(input, numbers[1]!.Value)}";
            case UserPromptInputKind.Scale:
            {
                var value = numbers[0]!.Value;
                var (min, max) = Bounds(input);
                var mark = input.Marks?.FirstOrDefault(item => Math.Abs(item.Value - value) < 1e-9);
                return mark is not null ? $"{Number(value)} — {mark.Label}"
                    : min is 0 or 1 ? $"{Number(value)} of {Number(max)}" : $"{Number(value)} ({Number(min)}–{Number(max)})";
            }
            default:
                return null;
        }
    }

    public string Format(UserPromptInput input, double value)
    {
        ArgumentNullException.ThrowIfNull(input);
        var number = Number(value);
        return input.Unit is not { Length: > 0 } unit ? number : unit is "%" or "°" ? number + unit : $"{number} {unit}";
    }

    private static string Number(double value) => value.ToString("#,0.######", CultureInfo.InvariantCulture);

    private static string Invariant(double value) =>
        Math.Round(value, 10).ToString("0.##########", CultureInfo.InvariantCulture);

    public string? ValidatePreview(string preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Length > MaxPreviewLength) return $"it is longer than {MaxPreviewLength} characters.";
        foreach (var placeholder in Placeholders(preview))
        {
            if (placeholder.Expression is null) return "a '{' has no matching '}'.";
            var error = PreviewExpression.Check(placeholder.Expression, PreviewNames);
            if (error is not null) return $"'{{{placeholder.Expression}}}': {error}";
            if (placeholder.Decimals is < 0 or > 6) return $"'{{{placeholder.Expression}:{placeholder.Decimals}}}': use 0..6 decimals.";
        }
        return null;
    }

    public string? RenderPreview(string preview, UserPromptPreviewValues values)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(values);
        var strings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["value"] = (values.Values.Count > 0 ? values.Values[0] : null) ?? values.Text ?? (values.Labels.Count > 0 ? string.Join(", ", values.Labels) : null),
            ["from"] = values.Values.Count > 1 ? values.Values[0] : null,
            ["to"] = values.Values.Count > 1 ? values.Values[1] : null,
            ["count"] = values.Count.ToString(CultureInfo.InvariantCulture),
            ["label"] = values.Labels.Count > 0 ? string.Join(", ", values.Labels) : null,
            ["text"] = values.Text
        };
        var builder = new System.Text.StringBuilder();
        var position = 0;
        foreach (var placeholder in Placeholders(preview))
        {
            builder.Append(preview, position, placeholder.Start - position);
            position = placeholder.End;
            if (placeholder.Expression is null) return null;
            var text = PreviewExpression.Evaluate(placeholder.Expression, strings, placeholder.Decimals);
            if (text is null) return null;
            builder.Append(text);
        }
        builder.Append(preview, position, preview.Length - position);
        return builder.ToString();
    }

    private readonly record struct Placeholder(int Start, int End, string? Expression, int? Decimals);

    private static IEnumerable<Placeholder> Placeholders(string template)
    {
        var index = 0;
        while ((index = template.IndexOf('{', index)) >= 0)
        {
            var close = template.IndexOf('}', index + 1);
            if (close < 0)
            {
                yield return new Placeholder(index, template.Length, null, null);
                yield break;
            }
            var body = template[(index + 1)..close];
            var colon = body.LastIndexOf(':');
            int? decimals = null;
            if (colon >= 0 && int.TryParse(body[(colon + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var places))
            {
                decimals = places;
                body = body[..colon];
            }
            yield return new Placeholder(index, close + 1, body.Trim(), decimals);
            index = close + 1;
        }
    }

    /// <summary>
    /// The arithmetic a preview may do with the answer: numbers, the answer's names, + − × ÷ and
    /// brackets. Small on purpose: a preview says what a choice amounts to, it is not a program.
    /// </summary>
    private sealed class PreviewExpression
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, string?>? _values;
        private readonly IReadOnlyCollection<string> _names;
        private int _position;

        private PreviewExpression(string text, IReadOnlyDictionary<string, string?>? values, IReadOnlyCollection<string> names)
        {
            _text = text;
            _values = values;
            _names = names;
        }

        public static string? Check(string expression, IReadOnlyCollection<string> names)
        {
            if (expression.Length == 0) return "it is empty.";
            try
            {
                var parser = new PreviewExpression(expression, null, names);
                parser.Sum();
                parser.Space();
                return parser._position < expression.Length ? $"unexpected '{expression[parser._position]}'." : null;
            }
            catch (FormatException error)
            {
                return error.Message;
            }
        }

        public static string? Evaluate(string expression, IReadOnlyDictionary<string, string?> values, int? decimals)
        {
            // A bare name may be text — the chosen label, a colour, a path — and is shown as it is.
            if (values.TryGetValue(expression, out var plain))
                return plain is null ? null
                    : double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Number(number, decimals) : plain;
            try
            {
                var parser = new PreviewExpression(expression, values, values.Keys.ToArray());
                var result = parser.Sum();
                return result is { } value && double.IsFinite(value) ? Number(value, decimals) : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static string Number(double value, int? decimals) =>
            value.ToString(decimals is { } places ? "#,0." + new string('0', places) : "#,0.##", CultureInfo.InvariantCulture).TrimEnd('.');

        private double? Sum()
        {
            var left = Product();
            while (Next() is '+' or '-')
            {
                var sign = _text[_position++];
                var right = Product();
                left = left is null || right is null ? null : sign == '+' ? left + right : left - right;
            }
            return left;
        }

        private double? Product()
        {
            var left = Unary();
            while (Next() is '*' or '/' or '×' or '÷')
            {
                var sign = _text[_position++];
                var right = Unary();
                left = left is null || right is null ? null : sign is '*' or '×' ? left * right : right == 0 ? null : left / right;
            }
            return left;
        }

        private double? Unary()
        {
            if (Next() == '-')
            {
                _position++;
                return -Unary();
            }
            if (Next() == '(')
            {
                _position++;
                var inner = Sum();
                if (Next() != ')') throw new FormatException("a '(' has no matching ')'.");
                _position++;
                return inner;
            }
            Space();
            var start = _position;
            if (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
            {
                while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
                return double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number : throw new FormatException($"'{_text[start.._position]}' is not a number.");
            }
            while (_position < _text.Length && char.IsLetter(_text[_position])) _position++;
            if (start == _position)
                throw new FormatException(_position < _text.Length ? $"unexpected '{_text[_position]}'." : "an operand is missing.");
            var name = _text[start.._position];
            if (!_names.Contains(name)) throw new FormatException($"unknown name '{name}'; use {string.Join(", ", PreviewNames)}.");
            if (_values is null) return 1;
            return _values.GetValueOrDefault(name) is { } text
                   && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private char? Next()
        {
            Space();
            return _position < _text.Length ? _text[_position] : null;
        }

        private void Space()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }
    }
}
