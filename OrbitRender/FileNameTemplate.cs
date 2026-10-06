using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrbitRender
{
    // Filename expressions only: no scripts, reflection, or filesystem access.
    internal static class FileNameTemplate
    {
        internal static string Expand(string template, IDictionary<string, object> variables, Func<string, string> sanitizeExpression = null)
        {
            if (template.Length > 4096) throw new FormatException("Template exceeds 4096 characters.");
            var result = new StringBuilder();
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
                { result.Append(c); i++; continue; }
                if (c == '}') throw new FormatException("Unexpected '}' at position " + (i + 1) + ". Use '}}' for a literal brace.");
                if (c != '{') { result.Append(c); continue; }
                var start = i;
                var quoted = false;
                var escaped = false;
                for (i++; i < template.Length; i++)
                {
                    c = template[i];
                    if (escaped) { escaped = false; continue; }
                    if (quoted && c == '\\') { escaped = true; continue; }
                    if (c == '"') { quoted = !quoted; continue; }
                    if (!quoted && c == '}') break;
                    if (!quoted && c == '{') throw new FormatException("Nested expressions are not supported at position " + (i + 1) + ".");
                }
                if (i == template.Length) throw new FormatException("Unclosed expression at position " + (start + 1) + ".");
                var expression = template.Substring(start + 1, i - start - 1);
                try
                {
                    var value = Evaluate(expression, variables);
                    result.Append(sanitizeExpression != null ? sanitizeExpression(value) : value);
                }
                catch (FormatException ex)
                { throw new FormatException("{" + expression + "} at position " + (start + 1) + ": " + ex.Message); }
                Require(result.Length <= 65536, "Template result is too long.");
            }
            return result.ToString();
        }

        private static string Evaluate(string expression, IDictionary<string, object> variables)
        {
            var pipeline = Split(expression, '|');
            var head = Split(pipeline[0], ':');
            var name = head[0].Trim();
            string value;
            if (name == "if")
            {
                Require(head.Count == 2, "Use if:condition,\"yes\",\"no\".");
                var args = Split(head[1], ',');
                Require(args.Count == 3, "A condition needs a variable and two quoted results.");
                var condition = args[0].Trim();
                var negate = condition.StartsWith("!", StringComparison.Ordinal);
                if (negate) condition = condition.Substring(1).Trim();
                var test = Variable(variables, condition);
                var truth = test is bool flag ? flag : !string.IsNullOrWhiteSpace(Convert.ToString(test, CultureInfo.InvariantCulture));
                // Validate both branches, even when one is not selected.
                var yes = Literal(args[1]);
                var no = Literal(args[2]);
                value = truth != negate ? yes : no;
            }
            else
            {
                var item = Variable(variables, name);
                if (item is DateTime date)
                {
                    // Colons are valid within a custom time format.
                    var colon = pipeline[0].IndexOf(':');
                    var format = colon < 0 ? (name == "time" ? "HH-mm-ss" : "yyyy-MM-dd")
                        : pipeline[0].Substring(colon + 1).Trim();
                    Require(format.Length > 0, "A date/time format cannot be empty.");
                    value = date.ToString(format, CultureInfo.InvariantCulture);
                }
                else
                {
                    Require(head.Count == 1, "Only date and time accept a format after ':'.");
                    value = item is bool boolean ? (boolean ? "true" : "false")
                        : Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }
            Require(value.Length <= 65536, "Variable value is too long.");
            for (var i = 1; i < pipeline.Count; i++)
            {
                var operation = Split(pipeline[i], ':');
                var filter = operation[0].Trim();
                Require(operation.Count <= 2, "Quote filter arguments containing ':'.");
                var args = operation.Count == 2 ? Split(operation[1], ',') : new List<string>();
                switch (filter)
                {
                    case "lower": Require(args.Count == 0, "lower takes no arguments."); value = value.ToLowerInvariant(); break;
                    case "upper": Require(args.Count == 0, "upper takes no arguments."); value = value.ToUpperInvariant(); break;
                    case "trim": Require(args.Count == 0, "trim takes no arguments."); value = value.Trim(); break;
                    case "replace":
                        Require(args.Count == 2, "Use replace:\"old\",\"new\".");
                        var old = Literal(args[0]);
                        Require(old.Length > 0, "replace cannot use an empty search string.");
                        var replacement = Literal(args[1]);
                        var count = 0;
                        for (var offset = 0; (offset = value.IndexOf(old, offset, StringComparison.Ordinal)) >= 0; offset += old.Length) count++;
                        Require(value.Length + (long)count * (replacement.Length - old.Length) <= 65536,
                            "Replacement result is too long.");
                        value = value.Replace(old, replacement); break;
                    case "truncate":
                        Require(args.Count == 1, "Use truncate:length.");
                        Require(int.TryParse(args[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                            && length >= 0 && length <= 160, "truncate length must be between 0 and 160.");
                        if (value.Length > length)
                        {
                            if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
                            value = value.Substring(0, length);
                        }
                        break;
                    case "default":
                        Require(args.Count == 1, "Use default:\"fallback\".");
                        var fallback = Literal(args[0]);
                        if (string.IsNullOrWhiteSpace(value)) value = fallback; break;
                    default: throw new FormatException("Unknown filter '" + filter + "'.");
                }
                Require(value.Length <= 65536, "Expression result is too long.");
            }
            return value;
        }

        private static object Variable(IDictionary<string, object> variables, string name)
        {
            if (!variables.TryGetValue(name, out var value)) throw new FormatException("Unknown variable '" + name + "'.");
            return value;
        }

        private static List<string> Split(string source, char separator)
        {
            var parts = new List<string>();
            var quoted = false;
            var escaped = false;
            var start = 0;
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                if (escaped) { escaped = false; continue; }
                if (quoted && c == '\\') { escaped = true; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (!quoted && c == separator) { parts.Add(source.Substring(start, i - start)); start = i + 1; }
            }
            Require(!quoted && !escaped, "Unclosed quoted string.");
            parts.Add(source.Substring(start));
            return parts;
        }

        private static string Literal(string source)
        {
            source = source.Trim();
            Require(source.Length >= 2 && source[0] == '"' && source[source.Length - 1] == '"', "String arguments must be quoted.");
            var result = new StringBuilder();
            for (var i = 1; i < source.Length - 1; i++)
            {
                var c = source[i];
                Require(c != '"', "Escape double quotes with a backslash.");
                if (c == '\\')
                {
                    Require(++i < source.Length - 1, "Incomplete escape sequence.");
                    c = source[i];
                    Require(c == '\\' || c == '"', "Only backslash and double quote escapes are supported.");
                }
                result.Append(c);
            }
            return result.ToString();
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new FormatException(message); }
    }
}
