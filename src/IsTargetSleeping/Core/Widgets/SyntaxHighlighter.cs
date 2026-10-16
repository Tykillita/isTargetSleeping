using System.Text.RegularExpressions;

namespace IsTargetSleeping;

public enum TokenKind { Plain, Comment, String, Number, Keyword, Control, Type, Function, Property, Tag, Attribute, Punctuation, Heading, Code, Bold, Italic, Link, Quote, Marker }

/// Un trozo de texto con su color lógico. Los de una lista van ordenados, sin solaparse y dentro del texto.
public readonly record struct Token(int Start, int Length, TokenKind Kind)
{
    public int End => Start + Length;
}

/// El resaltado de sintaxis del editor del widget Código: parte el texto en trozos (comentario, cadena, palabra clave…) según
/// el lenguaje del archivo. Solo decide qué es cada trozo; los colores los pone la interfaz.
/// shortcut: son analizadores léxicos con expresiones regulares (sin gramática): bastan para colorear, no entienden el código
/// (un `x(` dentro de una cadena multilínea rara puede salir mal). Para análisis de verdad habría que usar tree-sitter o similar.
public static class SyntaxHighlighter
{
    /// Por encima de este tamaño no se colorea (reanalizar en cada tecla sería lento).
    public const int MaxLength = 200_000;

    private static readonly Dictionary<string, string> ByExtension = Build(
        ("markdown", "md markdown mdx"),
        ("json", "json jsonc webmanifest"),
        ("xml", "xml html htm xhtml xaml svg csproj vbproj fsproj props targets config xsd xsl xslt slnx resx plist nuspec manifest vue razor cshtml"),
        ("css", "css scss less"),
        ("python", "py pyw"),
        ("sql", "sql"),
        ("yaml", "yml yaml"),
        ("script", "sh bash zsh ps1 psm1 psd1 bat cmd toml ini cfg conf env gitignore gitattributes dockerfile makefile mk properties editorconfig"),
        ("clike", "cs csx java js mjs cjs jsx ts tsx c h cpp cc cxx hpp go rs kt kts swift php dart scala groovy gradle"));

    private static Dictionary<string, string> Build(params (string Language, string Extensions)[] groups)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, extensions) in groups)
            foreach (var extension in extensions.Split(' ')) map[extension] = language;
        return map;
    }

    /// El lenguaje de un archivo por su nombre, o null si no se reconoce (se enseña sin colores).
    public static string? LanguageOf(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var name = System.IO.Path.GetFileName(fileName);
        var extension = System.IO.Path.GetExtension(name).TrimStart('.');
        if (extension.Length == 0) extension = name.TrimStart('.');
        return ByExtension.GetValueOrDefault(extension) ?? ByExtension.GetValueOrDefault(name);
    }

    private const string Strings = "\"(?:\\\\.|[^\"\\\\\\n])*\"|'(?:\\\\.|[^'\\\\\\n])*'";
    private const string Numbers = @"\b(?:0[xX][0-9a-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)[a-zA-Z]{0,2}\b";
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private static string Words(string words) => @"\b(?:" + words.Replace(' ', '|') + @")\b";

    private static readonly Lazy<Regex> CLike = new(() => new Regex(
        @"(?<comment>//[^\n]*|/\*[\s\S]*?(?:\*/|\z))"
        + @"|(?<string>[$@]{0,2}""(?:\\.|""""|[^""\\\n])*""|'(?:\\.|[^'\\\n])*'|`(?:\\[\s\S]|[^`\\])*`)"
        + "|(?<number>" + Numbers + ")"
        + "|(?<control>" + Words("if else for foreach while do switch case default break continue return throw try catch finally await yield goto import from export using when with") + ")"
        + "|(?<keyword>" + Words("class struct interface enum namespace public private protected internal static readonly const let var function fn def new this base super void int long short uint ulong string bool boolean float double char byte object decimal true false null undefined async abstract virtual override sealed partial extends implements package type typeof instanceof in is as not and or delegate event get set value where select ref out params unsafe volatile mut impl pub mod use trait match loop dyn crate self Self func go chan defer map range nil of lambda operator explicit implicit extern fixed lock checked unchecked sizeof nameof record init required") + ")"
        + @"|(?<function>\b[A-Za-z_][A-Za-z0-9_]*(?=\s*\())"
        + @"|(?<type>\b[A-Z][A-Za-z0-9_]*\b)", Options));

    private static readonly Lazy<Regex> Python = new(() => new Regex(
        @"(?<comment>#[^\n]*)"
        + @"|(?<string>[rRbBfFuU]{0,2}(?:""""""[\s\S]*?(?:""""""|\z)|'''[\s\S]*?(?:'''|\z)|" + Strings + "))"
        + "|(?<number>" + Numbers + ")"
        + "|(?<control>" + Words("if elif else for while try except finally with return raise yield await break continue pass import from assert del match case") + ")"
        + "|(?<keyword>" + Words("def class lambda None True False and or not in is as global nonlocal async self cls print") + ")"
        + @"|(?<function>@[A-Za-z_][\w.]*|\b[A-Za-z_]\w*(?=\s*\())"
        + @"|(?<type>\b[A-Z][A-Za-z0-9_]*\b)", Options));

    private static readonly Lazy<Regex> Script = new(() => new Regex(
        @"(?<comment>(?<![\w$])#[^\n]*|^[ \t]*;[^\n]*|^[ \t]*(?:rem|REM|::)[ \t][^\n]*)"
        + @"|(?<string>""(?:\\.|`.|[^""\\\n])*""|'[^'\n]*')"
        + @"|(?<property>\$\{[^}\n]*\}|\$[A-Za-z_]\w*|%[A-Za-z_]\w*%)"
        + @"|(?<type>^\[[^\]\n]+\])"
        + @"|(?<property>^[ \t]*[A-Za-z_.][\w.\-]*(?=[ \t]*=))"
        + @"|(?<number>\b\d+(?:\.\d+)?\b)"
        + "|(?<control>" + Words("if then else elif fi for foreach in do done while until case esac switch break continue return exit try catch finally throw goto") + ")"
        + "|(?<keyword>" + Words("function param local export set unset readonly echo cd source alias true false null begin process end filter class enum using import let var const FROM RUN CMD COPY ADD ENV ARG WORKDIR EXPOSE ENTRYPOINT") + ")", Options));

    private static readonly Lazy<Regex> Yaml = new(() => new Regex(
        @"(?<comment>(?<![\w""'])#[^\n]*)"
        + @"|(?<property>^[ \t]*(?:-[ \t]+)?[\w.\-/ ]+?(?=[ \t]*:(?:[ \t]|$)))"
        + @"|(?<string>""(?:\\.|[^""\\\n])*""|'[^'\n]*')"
        + @"|(?<number>\b\d+(?:\.\d+)?\b)"
        + "|(?<keyword>" + Words("true false null yes no on off True False Null") + ")"
        + @"|(?<marker>^---|^\.\.\.|^[ \t]*-(?=[ \t])|[&*][\w-]+|[|>][+-]?(?=[ \t]*$))", Options));

    private static readonly Lazy<Regex> Json = new(() => new Regex(
        @"(?<comment>//[^\n]*|/\*[\s\S]*?(?:\*/|\z))"
        + @"|(?<property>""(?:\\.|[^""\\\n])*""(?=\s*:))"
        + @"|(?<string>""(?:\\.|[^""\\\n])*"")"
        + @"|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)"
        + @"|(?<keyword>\b(?:true|false|null)\b)", Options));

    private static readonly Lazy<Regex> Css = new(() => new Regex(
        @"(?<comment>/\*[\s\S]*?(?:\*/|\z))"
        + "|(?<string>" + Strings + ")"
        + @"|(?<number>#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b|-?\b\d+(?:\.\d+)?(?:%|[a-zA-Z]{1,4})?)"
        + @"|(?<control>@[\w-]+)"
        + @"|(?<property>-{0,2}[a-zA-Z][\w-]*(?=\s*:(?!:)))"
        + @"|(?<type>[.#][A-Za-z_][\w-]*)"
        + @"|(?<keyword>\b(?:important|inherit|initial|none|auto)\b)"
        + @"|(?<function>[A-Za-z-]+(?=\())", Options));

    private static readonly Lazy<Regex> Sql = new(() => new Regex(
        @"(?<comment>--[^\n]*|/\*[\s\S]*?(?:\*/|\z))"
        + @"|(?<string>'(?:''|[^'])*')"
        + "|(?<number>" + Numbers + ")"
        + "|(?<control>" + Words("select insert update delete from where join left right inner outer on group by order having limit offset union all distinct case when then else end begin commit rollback with") + ")"
        + "|(?<keyword>" + Words("create table alter drop index view into values set primary key foreign references default constraint unique as and or not in is null like between exists add column if int integer varchar text boolean date timestamp float double decimal bigint") + ")"
        + @"|(?<function>\b[A-Za-z_]\w*(?=\s*\())", Options | RegexOptions.IgnoreCase));

    private static readonly Lazy<Regex> XmlOuter = new(() => new Regex(
        @"(?<comment><!--[\s\S]*?(?:-->|\z))|(?<string><!\[CDATA\[[\s\S]*?(?:\]\]>|\z))|(?<tag><(?:/?[A-Za-z_][^>]*|[?][^>]*)>)", Options));

    private static readonly Lazy<Regex> XmlInner = new(() => new Regex(
        @"(?<tag><\??/?[A-Za-z_][\w:.\-]*)|(?<attribute>[A-Za-z_:][\w:.\-]*(?=\s*=))|(?<string>""[^""]*""|'[^']*')|(?<punctuation>/?\??>|\?>)", Options));

    private static readonly Lazy<Regex> Markdown = new(() => new Regex(
        @"(?<code>^[ \t]*```[^\n]*(?:\n[\s\S]*?(?:^[ \t]*```[ \t]*$|\z)|\z))"
        + @"|(?<comment><!--[\s\S]*?(?:-->|\z))"
        + @"|(?<heading>^[ \t]{0,3}\#{1,6}[ \t][^\n]*)"
        + @"|(?<quote>^[ \t]*>[^\n]*)"
        + @"|(?<marker>^[ \t]*(?:-{3,}|\*{3,}|_{3,})[ \t]*$|^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)*\|?[ \t]*$|^[ \t]*(?:[-*+]|\d+[.)])(?=[ \t])|\|)"
        + @"|(?<code>`[^`\n]+`)"
        + @"|(?<bold>\*\*[^*\n]+?\*\*|__[^_\n]+?__)"
        + @"|(?<italic>(?<![*\w])\*[^*\s][^*\n]*?\*(?!\*)|(?<![_\w])_[^_\s][^_\n]*?_(?![_\w]))"
        + @"|(?<link>!?\[[^\]\n]*\]\([^)\n]*\)|https?://[^\s)>\]]+)", Options));

    /// Dentro de un título o una cita siguen valiendo el código, la negrita y los enlaces.
    private static readonly Lazy<Regex> MarkdownInline = new(() => new Regex(
        @"(?<code>`[^`\n]+`)|(?<bold>\*\*[^*\n]+?\*\*|__[^_\n]+?__)|(?<link>!?\[[^\]\n]*\]\([^)\n]*\)|https?://[^\s)>\]]+)", Options));

    private static readonly Dictionary<string, TokenKind> Kinds = Enum.GetValues<TokenKind>().ToDictionary(k => k.ToString(), k => k, StringComparer.OrdinalIgnoreCase);

    public static List<Token> Tokenize(string text, string? language)
    {
        var tokens = new List<Token>();
        if (language is null || text.Length == 0 || text.Length > MaxLength) return tokens;
        switch (language)
        {
            case "markdown": Scan(Markdown.Value, text, 0, tokens, inline: MarkdownInline.Value); break;
            case "xml": ScanXml(text, tokens); break;
            case "clike": Scan(CLike.Value, text, 0, tokens); break;
            case "python": Scan(Python.Value, text, 0, tokens); break;
            case "script": Scan(Script.Value, text, 0, tokens); break;
            case "yaml": Scan(Yaml.Value, text, 0, tokens); break;
            case "json": Scan(Json.Value, text, 0, tokens); break;
            case "css": Scan(Css.Value, text, 0, tokens); break;
            case "sql": Scan(Sql.Value, text, 0, tokens); break;
        }
        return tokens;
    }

    private static void Scan(Regex regex, string text, int offset, List<Token> tokens, Regex? inline = null)
    {
        var names = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToArray();
        foreach (Match match in regex.Matches(text))
        {
            if (match.Length == 0) continue;
            var name = names.FirstOrDefault(n => match.Groups[n].Success);
            if (name is null || !Kinds.TryGetValue(name, out var kind)) continue;
            // Un título o una cita se parten alrededor de lo que llevan dentro (código, negrita, enlaces) para no solaparse.
            if (inline is not null && kind is TokenKind.Heading or TokenKind.Quote) AddWithInline(inline, match.Value, offset + match.Index, kind, tokens);
            else tokens.Add(new Token(offset + match.Index, match.Length, kind));
        }
    }

    private static void AddWithInline(Regex inline, string line, int start, TokenKind outer, List<Token> tokens)
    {
        int at = 0;
        foreach (Match m in inline.Matches(line))
        {
            if (m.Index > at) tokens.Add(new Token(start + at, m.Index - at, outer));
            var name = inline.GetGroupNames().Where(n => !int.TryParse(n, out _)).First(n => m.Groups[n].Success);
            tokens.Add(new Token(start + m.Index, m.Length, Kinds[name]));
            at = m.Index + m.Length;
        }
        if (at < line.Length) tokens.Add(new Token(start + at, line.Length - at, outer));
    }

    /// XML y HTML: primero las etiquetas enteras y, dentro de cada una, su nombre, sus atributos y sus valores (el texto de en medio queda sin color).
    private static void ScanXml(string text, List<Token> tokens)
    {
        foreach (Match match in XmlOuter.Value.Matches(text))
        {
            if (match.Groups["comment"].Success) tokens.Add(new Token(match.Index, match.Length, TokenKind.Comment));
            else if (match.Groups["string"].Success) tokens.Add(new Token(match.Index, match.Length, TokenKind.String));
            else Scan(XmlInner.Value, match.Value, match.Index, tokens);
        }
    }
}
