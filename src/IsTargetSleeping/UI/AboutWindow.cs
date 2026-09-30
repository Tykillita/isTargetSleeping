using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// El panel «Acerca de» estándar: ícono, nombre, versión y créditos centrados.
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
        Width = 300;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico
        Icon = Mark.AppIcon(64);

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 22), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new LogoView { IsIcon = true, Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(Text(AppInfo.Name, 15, FontWeights.Bold, "Fg", top: 12));
        stack.Children.Add(Text(tr("Versión %@", AppInfo.Version) + " · Windows", 11, FontWeights.Normal, "Secondary", top: 2));

        var credits = new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            FontFamily = (FontFamily)Application.Current.FindResource("Sans"),
        };
        credits.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        credits.Inlines.Add(tr("Vigila tus modelos locales y los pone a dormir cuando no trabajan."));
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(new Run(AppInfo.Signature) { FontWeight = FontWeights.SemiBold });
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(tr("CodeSentry: desarrollo y ciberseguridad de Ruben Pino (Tykillita)."));
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(new LineBreak());
        // Créditos: la idea viene de ModelNap (MIT); la app está rediseñada por completo.
        credits.Inlines.Add(tr("Basado en la idea de ModelNap, de Erik Taveras (licencia MIT). Rediseñado por completo, con funciones nuevas y originales."));
        credits.Inlines.Add(new LineBreak());
        var link = new Hyperlink(new Run("github.com/eriktaveras/modelnap")) { NavigateUri = new Uri(AppInfo.CreditsUrl) };
        link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
        link.RequestNavigate += (_, e) => Paths.Open(e.Uri.AbsoluteUri);
        credits.Inlines.Add(link);
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(new LineBreak());
        credits.Inlines.Add(tr("Proyecto independiente, no afiliado a Ollama."));
        stack.Children.Add(credits);
        // Vidrio negro: el tinte del tema sobre el acrílico de DWM.
        var tint = new Border { Child = stack };
        tint.SetResourceReference(Border.BackgroundProperty, "PanelTint");
        Content = tint;

        SourceInitialized += (_, _) => Glass.Apply(this, extendFrame: true);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush, double top)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Margin = new Thickness(0, top, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("Sans"),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
