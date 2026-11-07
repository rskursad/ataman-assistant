using System.Globalization;
using System.Text.Json;
using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// Evaluates a safe arithmetic expression (<c>+ - * / % ( )</c>, decimals).
/// A small recursive-descent parser avoids any <c>System.Data</c> dependency
/// and never evaluates arbitrary code.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculate";

    public string Description =>
        "Evaluate an arithmetic expression and return the numeric result. " +
        "Supports +, -, *, /, % (modulo), parentheses and decimal numbers. " +
        "Use when the user asks to compute something.";

    public string? ParametersSchemaJson =>
        "{\"type\":\"object\",\"properties\":{\"expression\":{\"type\":\"string\",\"description\":\"Arithmetic expression, e.g. (2+3)*4\"}},\"required\":[\"expression\"]}";

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        string? expression = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (doc.RootElement.TryGetProperty("expression", out var el))
            {
                expression = el.GetString();
            }
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new ToolResult(Name, "{}", false, $"Invalid arguments JSON: {ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            return Task.FromResult(new ToolResult(Name, "{}", false, "Missing 'expression' argument."));
        }

        if (expression.Length > 200)
        {
            return Task.FromResult(new ToolResult(Name, "{}", false, "Expression is too long."));
        }

        var parser = new Parser(expression);
        double value;
        try
        {
            value = parser.Evaluate();
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult(Name, "{}", false, ex.Message));
        }

        var result = JsonSerializer.Serialize(new
        {
            expression,
            result = FormatNumber(value),
        });
        return Task.FromResult(new ToolResult(Name, result, true, null));
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return Math.Abs(value - Math.Round(value)) < 1e-9
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) => _text = text;

        public double Evaluate()
        {
            SkipWs();
            var value = ParseExpression();
            SkipWs();
            if (_pos < _text.Length)
            {
                throw new FormatException($"Unexpected character '{_text[_pos]}'.");
            }

            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWs();
                if (TryConsume('+'))
                {
                    value += ParseTerm();
                }
                else if (TryConsume('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWs();
                if (TryConsume('*'))
                {
                    value *= ParseFactor();
                }
                else if (TryConsume('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0)
                    {
                        throw new DivideByZeroException("Division by zero.");
                    }

                    value /= divisor;
                }
                else if (TryConsume('%'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0)
                    {
                        throw new DivideByZeroException("Modulo by zero.");
                    }

                    value %= divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParseFactor()
        {
            SkipWs();
            if (TryConsume('('))
            {
                var value = ParseExpression();
                SkipWs();
                if (!TryConsume(')'))
                {
                    throw new FormatException("Missing closing parenthesis.");
                }

                return value;
            }

            if (TryConsume('-'))
            {
                return -ParseFactor();
            }

            if (TryConsume('+'))
            {
                return ParseFactor();
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && (char.IsAsciiDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                _pos++;
            }

            if (start == _pos)
            {
                throw new FormatException(
                    _pos < _text.Length
                        ? $"Expected a number, found '{_text[_pos]}'."
                        : "Expected a number, end of input.");
            }

            var token = _text[start.._pos];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Invalid number '{token}'.");
            }

            return value;
        }

        private void SkipWs()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        private bool TryConsume(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }
    }
}