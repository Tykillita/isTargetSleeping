using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// La ventana normal de la app (como la de Nusa): Widgets, Procesos y Ajustes ya no viven dentro del notch. Sus páginas son pestañas arriba, en la barra
/// de título, como las de Windows Terminal. Lleva su propio modelo para que cambiar de página aquí no mueva la vista del notch.
public sealed class AppWindow : Window
{
    private static AppWindow? shown;

    private readonly PanelViewModel model;
    private readonly ContentView content;
    private readonly Grid root;
    private readonly Button widgetsTab;
    private readonly Button processesTab;
    private readonly Button settingsTab;
    private TextBlock maximizeGlyph;

    public static void Present(Supervisor supervisor, PanelView view)
    {
        if (view is not (PanelView.Widgets or PanelView.Settings or PanelView.Processes)) view = PanelView.Settings;
        if (shown is null)
        {
            shown = new AppWindow(supervisor);
            shown.Closed += (_, _) => { shown.model.SetPanelVisible(false); shown = null; };
            shown.Show();
        }
        shown.Go(view);
        if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
        shown.Activate();
    }

    /// El contenido de la ventana sin abrirla, para las capturas (`--snapshot … appwindow`).
    internal static FrameworkElement Preview(Supervisor supervisor, PanelView view)
    {
        var window = new AppWindow(supervisor);
        window.Go(view);
        window.root.Measure(new Size(980, 660));
        window.root.Arrange(new Rect(0, 0, 980, 660));
        window.root.UpdateLayout();
        window.content.ScrollToTop();
        window.root.UpdateLayout();
        window.Content = null;   // para ponerlo en otro contenedor
        return window.root;
    }

    private AppWindow(Supervisor supervisor)
    {
        Title = AppInfo.Name;
        Width = 980;
        Height = 700;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico
        Icon = Mark.AppIcon(64);
        // La barra de título es nuestra: las pestañas y los botones de ventana se pintan dentro, y lo que queda libre arrastra la ventana.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleBarHeight, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1),
            UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0),
        });

        model = new PanelViewModel(supervisor, PanelView.Settings) { WideLayout = true };
        content = new ContentView(model) { Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        content.HideHeader();
        content.Background = SurfaceFill;   // sólido y del color de la pestaña elegida, para que las dos se vean unidas

        widgetsTab = Tab("", tr("Widgets"), () => Go(PanelView.Widgets));
        processesTab = Tab("", tr("Procesos"), () => Go(PanelView.Processes));
        settingsTab = Tab("", tr("Ajustes"), () => Go(PanelView.Settings));
        var bar = TitleBar();
        Grid.SetRow(content, 1);
        root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        root.Children.Add(bar);
        root.Children.Add(content);
        Content = root;

        // El cuerpo mide lo que deja la barra de título: así la barra de pestañas de Ajustes queda fija y solo se desplaza su contenido.
        root.SizeChanged += (_, e) => model.BodyMaxHeight = Math.Max(200, e.NewSize.Height - TitleBarHeight);
        StateChanged += (_, _) => OnStateChanged();
        SourceInitialized += (_, _) => Glass.Apply(this, extendFrame: true);
        Loaded += (_, _) => model.SetPanelVisible(true);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !model.CloseTopOverlay()) Close(); };
    }

    private const double TitleBarHeight = 40;
    /// Como en Windows Terminal: la barra de pestañas es un gris algo más claro y la pestaña elegida lleva el color del contenido de abajo, sin costura.
    private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x21)), SurfaceFill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D));
    static AppWindow() { BarFill.Freeze(); SurfaceFill.Freeze(); }

    /// Icono y pestañas a la izquierda; los botones de ventana, a la derecha; lo que queda libre arrastra la ventana.
    private Grid TitleBar()
    {
        var icon = new Image { Source = Mark.AppIcon(32), Width = 16, Height = 16, Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch, Children = { widgetsTab, processesTab, settingsTab } };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, tabs } };

        var minimize = CaptionButton("", tr("Minimizar"), () => WindowState = WindowState.Minimized, out _);
        var maximize = CaptionButton("", tr("Maximizar"), () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, out maximizeGlyph);
        var close = CaptionButton("", tr("Cerrar"), Close, out _, danger: true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Children = { minimize, maximize, close } };

        // Opaca: DWM pinta su propia barra de ~31 px y, sin esto, el resto de la fila dejaba ver el acrílico (una franja gris bajo las pestañas).
        var bar = new Grid { Height = TitleBarHeight, Background = BarFill };
        bar.Children.Add(left);
        bar.Children.Add(buttons);
        return bar;
    }

    private void OnStateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        maximizeGlyph.Text = max ? "" : "";
        // Maximizada, una ventana sin marco nativo se pasa del borde de la pantalla por el ancho del marco de cambio de tamaño.
        root.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
    }

    private void Go(PanelView view)
    {
        if (view == PanelView.Processes) model.CurrentActivityTab = PanelViewModel.ActivityTab.Processes;
        model.Show(view);
        content.ScrollToTop();
        Highlight(widgetsTab, view == PanelView.Widgets);
        Highlight(processesTab, view == PanelView.Processes);
        Highlight(settingsTab, view == PanelView.Settings);
    }

    private static void Highlight(Button tab, bool on)
    {
        if (tab.Content is not Grid { Children: [Border border, System.Windows.Shapes.Path left, System.Windows.Shapes.Path right] }) return;
        if (on) border.Background = SurfaceFill;
        else border.ClearValue(Border.BackgroundProperty);   // sin valor local, manda el hover del estilo
        left.Fill = right.Fill = on ? SurfaceFill : Brushes.Transparent;
    }

    /// Un estilo de fondo que se enciende al pasar el ratón (las pestañas elegidas llevan un valor local y no cambian).
    private static Style Hover(string resource, Brush? over = null)
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(over is null ? new Setter(Border.BackgroundProperty, new DynamicResourceExtension(resource)) : new Setter(Border.BackgroundProperty, over));
        style.Triggers.Add(trigger);
        return style;
    }

    private static Button Tab(string glyph, string label, Action click)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        var text = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { icon, text } };
        // Como una pestaña de Windows Terminal: redondeada arriba y pegada al contenido de abajo.
        var border = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(14, 0, 16, 0), Child = row, Style = Hover("ButtonHover") };
        // Llega hasta el borde de abajo de la barra: así la pestaña elegida se funde con el contenido.
        // Las esquinas de abajo de la pestaña elegida se abren con una curva hacia fuera (como en Windows Terminal) y la unen al contenido.
        const double flare = 8;
        System.Windows.Shapes.Path Flare(bool leftSide) => new()
        {
            Width = flare, Height = flare, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Fill = Brushes.Transparent,
            HorizontalAlignment = leftSide ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = leftSide ? new Thickness(-flare, 0, 0, 0) : new Thickness(0, 0, -flare, 0),
            Data = Geometry.Parse(leftSide ? "M 8,0 L 8,8 L 0,8 A 8,8 0 0 0 8,0 Z" : "M 0,0 L 0,8 L 8,8 A 8,8 0 0 1 0,0 Z"),
        };
        var button = new Button { Content = new Grid { Children = { border, Flare(true), Flare(false) } }, Margin = new Thickness(8, 8, 8, 0), VerticalAlignment = VerticalAlignment.Stretch };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Plain");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    private static Button CaptionButton(string glyph, string label, Action click, out TextBlock iconBlock, bool danger = false)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        iconBlock = icon;
        var border = new Border { Width = 46, Height = 32, Child = icon, Style = danger ? Hover("", new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C))) : Hover("ButtonHover") };
        var button = new Button { Content = border };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Plain");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}
