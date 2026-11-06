using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Muestra una respuesta en Markdown ya maquetada (párrafos, títulos, listas, negritas, código, citas) en vez del texto crudo con asteriscos.
/// Es de solo lectura pero se puede seleccionar y copiar. Cubre lo que escriben los modelos; no es un intérprete completo de Markdown.
/// shortcut: sin tablas ni enlaces pulsables (salen como texto), hasta que alguna respuesta real los necesite.
public sealed class MarkdownView : RichTextBox
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownView),
        new PropertyMetadata("", (d, _) => ((MarkdownView)d).Rebuild()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    public MarkdownView()
    {
        IsReadOnly = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FocusVisualStyle = null;
        SetResourceReference(ForegroundProperty, "Fg");
        SetResourceReference(SelectionBrushProperty, "Accent");
        SetResourceReference(FontFamilyProperty, "Sans");
        FontSize = 12.5;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty && IsLoaded) Rebuild();
    }

    private void Rebuild() => Document = Build(Markdown ?? "", FontSize);

    /// El documento de un texto en Markdown. Público para poder probarlo sin pintar nada.
    public static FlowDocument Build(string markdown, double fontSize = 12.5)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), LineHeight = Math.Round(fontSize * 1.5), FontSize = fontSize, TextAlignment = TextAlignment.Left };
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var list = new List<(int Indent, string Marker, string Text)>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var block = new Paragraph { Margin = new Thickness(0, 0, 0, 7) };
            for (int i = 0; i < paragraph.Count; i++)
            {
                if (i > 0) block.Inlines.Add(new LineBreak());
                AddInlines(block.Inlines, paragraph[i].Trim());
            }
            document.Blocks.Add(block);
            paragraph.Clear();
        }

        void FlushList()
        {
            if (list.Count == 0) return;
            bool ordered = list[0].Marker.Length > 1 || char.IsDigit(list[0].Marker[0]);
            var block = new List { MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, MarkerOffset = 6, Padding = new Thickness(18, 0, 0, 0), Margin = new Thickness(0, 0, 0, 7) };
            foreach (var item in list)
            {
                var text = new Paragraph { Margin = new Thickness(item.Indent > 0 ? 14 : 0, 0, 0, 2) };
                AddInlines(text.Inlines, item.Text);
                block.ListItems.Add(new ListItem(text));
            }
            document.Blocks.Add(block);
            list.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(); FlushList();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                var block = new Paragraph(new Run(string.Join("\n", code))) { FontSize = fontSize * 0.92, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 7), LineHeight = Math.Round(fontSize * 1.35) };
                block.SetResourceReference(TextElement.FontFamilyProperty, "Mono");
                block.SetResourceReference(TextElement.BackgroundProperty, "Track");
                document.Blocks.Add(block);
                continue;
            }
            if (trimmed.Length == 0) { FlushParagraph(); FlushList(); continue; }

            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*$");
            if (heading.Success)
            {
                FlushParagraph(); FlushList();
                int level = heading.Groups[1].Length;
                var block = new Paragraph { FontWeight = FontWeights.SemiBold, FontSize = fontSize * (level == 1 ? 1.28 : level == 2 ? 1.18 : 1.07), Margin = new Thickness(0, document.Blocks.Count == 0 ? 0 : 5, 0, 4) };
                AddInlines(block.Inlines, heading.Groups[2].Value);
                document.Blocks.Add(block);
                continue;
            }
            if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
            {
                FlushParagraph(); FlushList();
                var rule = new Paragraph(new Run(" ")) { Margin = new Thickness(0, 2, 0, 9), FontSize = 1, LineHeight = 1 };
                rule.SetResourceReference(TextElement.BackgroundProperty, "Line");
                document.Blocks.Add(rule);
                continue;
            }
            var item = Regex.Match(line, @"^(?<indent>\s*)(?<marker>[-*+•]|\d{1,3}[.)])\s+(?<text>.*)$");
            if (item.Success)
            {
                FlushParagraph();
                var marker = item.Groups["marker"].Value;
                // Una lista ordenada y una de viñetas seguidas son dos listas.
                bool ordered = marker.Length > 1 || char.IsDigit(marker[0]);
                if (list.Count > 0 && (list[0].Marker.Length > 1 || char.IsDigit(list[0].Marker[0])) != ordered) FlushList();
                list.Add((item.Groups["indent"].Length >= 2 ? 1 : 0, marker, item.Groups["text"].Value.Trim()));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph(); FlushList();
                var quote = new Paragraph { Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(10, 1, 0, 1), BorderThickness = new Thickness(3, 0, 0, 0) };
                quote.SetResourceReference(Block.BorderBrushProperty, "Accent");
                quote.SetResourceReference(TextElement.ForegroundProperty, "Secondary");
                AddInlines(quote.Inlines, trimmed.TrimStart('>').Trim());
                document.Blocks.Add(quote);
                continue;
            }
            FlushList();
            paragraph.Add(line);
        }
        FlushParagraph(); FlushList();
        return document;
    }

    // **negrita**, *cursiva* / _cursiva_, `código`, [texto](enlace) (sale como texto); lo demás, tal cual.
    private static readonly Regex InlineToken = new(@"\*\*(?<b>.+?)\*\*|__(?<b2>.+?)__|(?<![\w*])\*(?<i>[^*\s][^*]*?)\*(?![\w*])|(?<![\w_])_(?<i2>[^_\s][^_]*?)_(?![\w_])|`(?<c>[^`]+)`|\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)\)", RegexOptions.Compiled);

    private static void AddInlines(InlineCollection inlines, string text)
    {
        int position = 0;
        foreach (Match m in InlineToken.Matches(text))
        {
            if (m.Index > position) inlines.Add(new Run(text[position..m.Index]));
            if (m.Groups["b"].Success || m.Groups["b2"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value);
                inlines.Add(bold);
            }
            else if (m.Groups["i"].Success || m.Groups["i2"].Success)
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value);
                inlines.Add(italic);
            }
            else if (m.Groups["c"].Success)
            {
                var code = new Run(m.Groups["c"].Value) { FontSize = 11.5 };
                code.SetResourceReference(TextElement.FontFamilyProperty, "Mono");
                code.SetResourceReference(TextElement.BackgroundProperty, "Track");
                inlines.Add(code);
            }
            else
            {
                var link = new Run(m.Groups["lt"].Value) { TextDecorations = TextDecorations.Underline };
                link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                inlines.Add(link);
            }
            position = m.Index + m.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
    }
}
