using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

static class HtmlTemplate
{
    public static string Render(string template, object model)
    {
        List<Part> parts = new Parser(template).ReadBlock();
        var html = new StringBuilder();
        RenderBlock(parts, new Scope("Model", model, null), html);
        return html.ToString();
    }

    private static void RenderBlock(List<Part> parts, Scope scope, StringBuilder html)
    {
        foreach (Part part in parts)
        {
            switch (part)
            {
                case Text text:
                    html.Append(text.Content);
                    break;
                case Value value:
                    html.Append(WebUtility.HtmlEncode(Resolve(value.Expression, scope, value.Position)?.ToString()));
                    break;
                case Condition condition:
                    if (Resolve(condition.Expression, scope, condition.Position) is not bool result)
                        throw Error(condition.Position, $"условие «{condition.Expression}» должно иметь тип bool");

                    if (result)
                        RenderBlock(condition.Body, scope, html);
                    break;
                case Loop loop:
                    if (Resolve(loop.Expression, scope, loop.Position) is not IEnumerable items || items is string)
                        throw Error(loop.Position, $"выражение «{loop.Expression}» должно возвращать коллекцию");

                    foreach (object? item in items)
                        RenderBlock(loop.Body, new Scope(loop.Variable, item, scope), html);
                    break;
            }
        }
    }

    private static object? Resolve(string expression, Scope scope, int position)
    {
        string[] names = expression.Split('.');
        Scope? current = scope;
        while (current is not null && current.Name != names[0])
            current = current.Parent;

        if (current is null)
            throw Error(position, $"неизвестная переменная «{names[0]}»");

        object? value = current.Value;
        foreach (string name in names.Skip(1))
        {
            PropertyInfo? property = value?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0)
                throw Error(position, $"не удалось прочитать свойство «{name}» в выражении «{expression}»");

            value = property.GetValue(value);
        }

        return value;
    }

    private static FormatException Error(int position, string message) =>
        new($"Ошибка шаблона в позиции {position + 1}: {message}.");

    private sealed record Scope(string Name, object? Value, Scope? Parent);
    private abstract record Part;
    private sealed record Text(string Content) : Part;
    private sealed record Value(string Expression, int Position) : Part;
    private sealed record Condition(string Expression, int Position, List<Part> Body) : Part;
    private sealed record Loop(string Variable, string Expression, int Position, List<Part> Body) : Part;

    private sealed class Parser(string template)
    {
        private const string Name = @"[\p{L}_][\p{L}\p{Nd}_]*";
        private const string Path = Name + @"(?:\." + Name + ")*";

        // \G привязывает совпадение к текущей позиции разбора.
        private static readonly Regex Directive = new(@"\G@(if|foreach)\b");
        private static readonly Regex IfHeader = new(@"\G@if\s*\(\s*(?<path>" + Path + @")\s*\)\s*\{");
        private static readonly Regex ForHeader = new(@"\G@foreach\s*\(\s*var\s+(?<name>" + Name +
            @")\s+in\s+(?<path>" + Path + @")\s*\)\s*\{");
        private static readonly Regex ValueExpression = new(@"\G@(?<path>(?>" + Path + @"))(?!\.[\p{L}\p{Nd}_])");

        private int position;

        public List<Part> ReadBlock(int? opening = null)
        {
            var parts = new List<Part>();
            while (position < template.Length)
            {
                int start = position;
                switch (template[position])
                {
                    case '}':
                        if (opening is null)
                            throw Error(position, "лишняя закрывающая скобка «}»");

                        position++;
                        return parts;
                    case '@':
                        parts.Add(ReadExpression());
                        break;
                    default:
                        while (position < template.Length && template[position] is not ('@' or '}'))
                            position++;

                        parts.Add(new Text(template[start..position]));
                        break;
                }
            }

            if (opening is int blockStart)
                throw Error(blockStart, "для блока отсутствует закрывающая скобка «}»");

            return parts;
        }

        private Part ReadExpression()
        {
            int start = position;
            string directive = Directive.Match(template, position).Groups[1].Value;
            Regex pattern = directive switch
            {
                "if" => IfHeader,
                "foreach" => ForHeader,
                _ => ValueExpression
            };
            Match match = pattern.Match(template, position);
            if (!match.Success)
            {
                throw Error(position, directive switch
                {
                    "if" => "ожидалось @if (выражение) { ... }",
                    "foreach" => "ожидалось @foreach (var имя in выражение) { ... }",
                    _ => "после «@» ожидалось выражение"
                });
            }

            position += match.Length;
            string expression = match.Groups["path"].Value;
            return directive switch
            {
                "if" => new Condition(expression, start, ReadBlock(start)),
                "foreach" => new Loop(match.Groups["name"].Value, expression, start, ReadBlock(start)),
                _ => new Value(expression, start)
            };
        }
    }
}
