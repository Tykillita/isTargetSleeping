using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace IsTargetSleeping.UI;

/// Los colores del texto del editor (solo las letras: el fondo no se toca). Markdown lleva los colores del tema del usuario
/// (títulos magenta, código verde, negritas naranja); el resto de lenguajes, una paleta al estilo de «Dark+» de VS Code.
public static class SyntaxPalette
{
    private static readonly Dictionary<TokenKind, Brush> ByKind = new()
    {
        [TokenKind.Comment] = Solid(0x6A, 0x99, 0x55),
        [TokenKind.String] = Solid(0xCE, 0x91, 0x78),
        [TokenKind.Number] = Solid(0xB5, 0xCE, 0xA8),
        [TokenKind.Keyword] = Solid(0x56, 0x9C, 0xD6),
        [TokenKind.Control] = Solid(0xC5, 0x86, 0xC0),
        [TokenKind.Type] = Solid(0x4E, 0xC9, 0xB0),
        [TokenKind.Function] = Solid(0xDC, 0xDC, 0xAA),
        [TokenKind.Property] = Solid(0x9C, 0xDC, 0xFE),
        [TokenKind.Tag] = Solid(0x56, 0x9C, 0xD6),
        [TokenKind.Attribute] = Solid(0x9C, 0xDC, 0xFE),
        [TokenKind.Punctuation] = Solid(0x80, 0x80, 0x80),
        [TokenKind.Heading] = Solid(0xFF, 0x00, 0xAA),
        [TokenKind.Code] = Solid(0x96, 0xE0, 0x72),
        [TokenKind.Bold] = Solid(0xF3, 0x9C, 0x12),
        [TokenKind.Italic] = Solid(0xC7, 0x92, 0xEA),
        [TokenKind.Link] = Solid(0x5F, 0xB4, 0xF0),
        [TokenKind.Quote] = Solid(0x8A, 0x8D, 0x94),
        [TokenKind.Marker] = Solid(0xFF, 0xE6, 0x6D),
    };

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// null = el color normal del texto.
    public static Brush? For(TokenKind kind) => ByKind.GetValueOrDefault(kind);
}

/// Pinta los colores de los trozos de `SyntaxHighlighter` sobre el texto de AvalonEdit, línea a línea.
internal sealed class SyntaxColorizer : DocumentColorizingTransformer
{
    public IReadOnlyList<Token> Tokens { get; set; } = [];

    protected override void ColorizeLine(DocumentLine line)
    {
        var tokens = Tokens;
        if (tokens.Count == 0) return;
        // Los trozos van ordenados y sin solaparse: se busca el primero que acaba dentro o después de esta línea.
        int lo = 0, hi = tokens.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (tokens[mid].End <= line.Offset) lo = mid + 1; else hi = mid;
        }
        for (int i = lo; i < tokens.Count && tokens[i].Start < line.EndOffset; i++)
        {
            var token = tokens[i];
            int start = Math.Max(token.Start, line.Offset), end = Math.Min(token.End, line.EndOffset);
            if (end <= start || SyntaxPalette.For(token.Kind) is not { } brush) continue;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}

/// El editor de texto del widget Código y del editor rápido: AvalonEdit con el texto coloreado según el archivo.
/// `Source` es el texto (enlazable en los dos sentidos) y `FileName` decide el lenguaje. Con el tema de alto contraste de
/// Windows no se colorea nada.
public sealed class CodeEditor : TextEditor
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(string), typeof(CodeEditor),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((CodeEditor)d).OnSource((string?)e.NewValue)));
    public static readonly DependencyProperty FileNameProperty = DependencyProperty.Register(nameof(FileName), typeof(string), typeof(CodeEditor),
        new PropertyMetadata("", (d, _) => ((CodeEditor)d).Recolor()));

    private readonly SyntaxColorizer colorizer = new();
    private bool syncing;

    public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }

    public CodeEditor()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 11.5;
        Background = Brushes.Transparent;
        Padding = new Thickness(6, 4, 4, 4);
        WordWrap = true;   // sin scroll horizontal: las líneas largas se parten dentro del ancho
        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.EnableTextDragDrop = false;
        Options.HighlightCurrentLine = false;
        TextArea.Caret.CaretBrush = Brushes.White;
        TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x4D, 0xA3, 0xFF));
        TextArea.SelectionForeground = null;   // el texto seleccionado conserva su color
        TextArea.SelectionBorder = null;
        TextArea.TextView.LineTransformers.Add(colorizer);
        TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { SetCurrentValue(SourceProperty, Text); }
            finally { syncing = false; }
            Recolor();
        };
    }

    private void OnSource(string? value)
    {
        if (syncing || value == Text) return;
        syncing = true;
        try { Text = value ?? ""; }
        finally { syncing = false; }
        Recolor();
    }

    private void Recolor()
    {
        var language = SystemParameters.HighContrast ? null : SyntaxHighlighter.LanguageOf(FileName);
        colorizer.Tokens = SyntaxHighlighter.Tokenize(Text, language);
        TextArea.TextView.Redraw();
    }

    /// Los trozos que está pintando ahora (para las pruebas).
    internal IReadOnlyList<Token> Tokens => colorizer.Tokens;
}
