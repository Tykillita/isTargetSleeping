using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// El panel «Acerca de»: qué es la app hoy (notch con widgets, equipo Claude + Codex, Ollama, RAM), quién la hizo y de dónde viene la idea.
public sealed class AboutWindow : Window
{
    private static AboutWindow? shown;

    public static void Present()
    {
        if (shown is not null) { shown.Activate(); return; }
        shown = new AboutWindow();
        shown.Closed += (_, _) => shown = null;
        shown.Show();
        shown.Activate();
    }

    private AboutWindow()
    {
        Title = tr("Acerca de %@", AppInfo.Name);
        Width = 392;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico
        Icon = Mark.AppIcon(64);
        Content = Build();

        SourceInitialized += (_, _) => Glass.Apply(this, extendFrame: true);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    /// El contenido de la ventana (público para poder mirarlo y probarlo sin abrir la ventana).
    public static FrameworkElement Build()
    {
        var stack = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };

        // Cabecera: ícono, nombre, versión y qué es en una frase.
        var logo = new LogoView { IsIcon = true, Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(logo);
        stack.Children.Add(Text(AppInfo.Name, 22, FontWeights.Bold, "Fg", top: 14, center: true));
        // La pastilla del notch: negra, con su canto de luz y el punto de estado.
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
        var version = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)), CornerRadius = new CornerRadius(15), Padding = new Thickness(13, 5, 14, 6), Margin = new Thickness(0, 9, 0, 0),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, Text(tr("Versión %@", AppInfo.Version) + " · Windows", 11, FontWeights.SemiBold, "Fg", top: 0, center: true) } },
        };
        version.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        stack.Children.Add(version);
        stack.Children.Add(Text(tr("El notch de Windows para tu IA local y tu PC"), 14, FontWeights.SemiBold, "Fg", top: 16, center: true, wrap: true));
        stack.Children.Add(Text(tr("Reúne en un notch tus widgets, los agentes de IA que trabajan en tu PC y un equipo de Claude y Codex que se reparte las tareas, y vigila Ollama y tu memoria RAM. Funciona en tu propio equipo."),
            11.5, FontWeights.Normal, "Secondary", top: 6, center: true, wrap: true));

        // Lo que hace, en seis fichas.
        var features = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var (glyph, label) in new[]
        {
            ("", tr("Notch y widgets")), ("", tr("Equipo Claude + Codex")), ("", tr("Agentes de IA")),
            ("", tr("Ollama")), ("", tr("Liberar RAM")), ("", tr("Sistema")),
        })
            features.Children.Add(Tile(glyph, label));
        stack.Children.Add(features);

        // Quién la hizo y de dónde viene la idea.
        stack.Children.Add(Card(tr("Creado por"), new Run(AppInfo.Signature) { FontWeight = FontWeights.SemiBold },
            tr("CodeSentry: desarrollo y ciberseguridad de Ruben Pino (Tykillita)."), AppInfo.RepoUrl, top: 14));
        // La descripción del rediseño pertenece a isTargetSleeping; aquí solo se acredita la idea original.
        stack.Children.Add(Card(tr("Inspiración"), null,
            tr("La idea de dormir los modelos inactivos proviene de ModelNap, de Erik Taveras (licencia MIT)."), AppInfo.CreditsUrl, top: 8));

        stack.Children.Add(Text(tr("Proyecto independiente, sin afiliación con Ollama, Anthropic ni OpenAI."), 10, FontWeights.Normal, "Muted", top: 14, center: true, wrap: true));

        // Vidrio negro: el tinte del tema sobre el acrílico de DWM.
        var tint = new Border { Child = stack };
        tint.SetResourceReference(Border.BackgroundProperty, "PanelTint");
        var sheen = new Border { Height = 220, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        sheen.SetResourceReference(Border.BackgroundProperty, "Sheen");
        return new Grid { Children = { tint, sheen } };
    }

    private static Border Tile(string glyph, string label)
    {
        // El icono va en una ficha de tinta (como los widgets activados de las carpetas del notch).
        var icon = new TextBlock { Text = glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Bg");
        var ink = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(11), HorizontalAlignment = HorizontalAlignment.Center, Child = icon };
        ink.SetResourceReference(Border.BackgroundProperty, "Fg");
        var name = Text(label, 10.5, FontWeights.SemiBold, "Fg", top: 8, center: true, wrap: true);
        var tile = GlassBox(new StackPanel { Children = { ink, name } }, 20, new Thickness(6, 12, 6, 11));
        tile.Margin = new Thickness(3);
        return tile;
    }

    /// Una ficha de cristal como las del notch: relleno translúcido, canto de luz fino y sombra negra que cae hacia abajo.
    private static Border GlassBox(UIElement child, double radius, Thickness padding)
    {
        var box = new Border { CornerRadius = new CornerRadius(radius), Padding = padding, BorderThickness = new Thickness(1), Child = child, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = 0.3, Color = Colors.Black } };
        box.SetResourceReference(Border.BackgroundProperty, "FolderFill");
        box.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        return box;
    }

    /// Una tarjeta con título, texto y un enlace al final.
    private static Border Card(string title, Run? lead, string text, string url, double top)
    {
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        body.SetResourceReference(TextBlock.FontFamilyProperty, "Sans");
        body.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        if (lead is not null) { lead.SetResourceReference(TextElement.ForegroundProperty, "Fg"); body.Inlines.Add(lead); body.Inlines.Add(new LineBreak()); }
        body.Inlines.Add(text);
        body.Inlines.Add(new LineBreak());
        body.Inlines.Add(Link(url));
        var eyebrow = Text(title.ToUpperInvariant(), 9.5, FontWeights.SemiBold, "Muted", top: 0, center: false);
        var card = GlassBox(new StackPanel { Children = { eyebrow, body } }, 20, new Thickness(15, 12, 15, 13));
        card.Margin = new Thickness(3, top, 3, 0);
        return card;
    }

    /// Enlace que se abre en el navegador, mostrado sin el «https://».
    private static Hyperlink Link(string url)
    {
        var link = new Hyperlink(new Run(url.Replace("https://", ""))) { NavigateUri = new Uri(url) };
        link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
        link.RequestNavigate += (_, e) => Paths.Open(e.Uri.AbsoluteUri);
        return link;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush, double top, bool center = false, bool wrap = false, bool mono = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Margin = new Thickness(0, top, 0, 0),
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        t.SetResourceReference(TextBlock.FontFamilyProperty, mono ? "Mono" : "Sans");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
