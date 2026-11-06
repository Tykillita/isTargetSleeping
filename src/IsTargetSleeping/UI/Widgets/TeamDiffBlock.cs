using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Un diff de git con colores: líneas añadidas en azul, quitadas en coral, cabeceras de trozo en ámbar y el resto apagado.
/// Tope de líneas para que el widget no se atasque con un cambio enorme.
public sealed class TeamDiffBlock : TextBlock
{
    public const int MaxLines = 300;
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(nameof(Diff), typeof(string), typeof(TeamDiffBlock),
        new PropertyMetadata("", (d, _) => ((TeamDiffBlock)d).Build()));
    public string Diff { get => (string)GetValue(DiffProperty); set => SetValue(DiffProperty, value); }

    public TeamDiffBlock()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 10.5;
        TextWrapping = TextWrapping.NoWrap;
        Loaded += (_, _) => { Theme.Changed += Build; Build(); };
        Unloaded += (_, _) => Theme.Changed -= Build;
    }

    private void Build()
    {
        Inlines.Clear();
        var theme = Theme.Current;
        int count = 0;
        foreach (var raw in (Diff ?? "").Split('\n'))
        {
            if (++count > MaxLines) { Inlines.Add(new Run("…") { Foreground = Theme.Brush(theme.Muted) }); break; }
            var line = raw.TrimEnd('\r');
            var color = line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("diff ") || line.StartsWith("index ") ? theme.Secondary
                : line.StartsWith('+') ? theme.On
                : line.StartsWith('-') ? theme.Danger
                : line.StartsWith("@@") ? theme.Warm
                : theme.Muted;
            Inlines.Add(new Run(line + "\n") { Foreground = Theme.Brush(color) });
        }
    }
}
