using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

public partial class ContentView : UserControl
{
    private bool pulsing;
    private bool wide;
    private readonly Dictionary<Panel, UIElement[]> columnized = [];

    /// En el dock ancho, cada vista se parte en dos columnas: lo marcado con esto y todo lo que le sigue pasa a la segunda.
    public static readonly DependencyProperty BreakBeforeProperty =
        DependencyProperty.RegisterAttached("BreakBefore", typeof(bool), typeof(ContentView), new PropertyMetadata(false));
    public static bool GetBreakBefore(DependencyObject d) => (bool)d.GetValue(BreakBeforeProperty);
    public static void SetBreakBefore(DependencyObject d, bool value) => d.SetValue(BreakBeforeProperty, value);

    public ContentView(PanelViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += OnModelChanged;
        IsVisibleChanged += (_, _) => model.SetPanelVisible(IsVisible);
    }

    /// Al abrir el editor el cursor ya está en el texto: se escribe sin hacer clic antes.
    private void OnEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(() => EditorBox.TextArea.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// Ancho (640) en dos columnas, o estrecho (340) en una: los mismos elementos, reacomodados.
    private void ApplyWide(bool on)
    {
        if (on == wide) return;
        wide = on;
        Width = on ? 640 : 340;
        foreach (var panel in new[] { MainPanel, ActivityPanel, SettingsPanel })
        {
            if (on) Columnize(panel);
            else Restore(panel);
        }
    }

    private void Columnize(StackPanel panel)
    {
        var items = panel.Children.Cast<UIElement>().ToArray();
        columnized[panel] = items;
        panel.Children.Clear();
        var left = new StackPanel { Margin = new Thickness(0, 0, 7, 0) };
        var right = new StackPanel { Margin = new Thickness(7, -10, 0, 0) };   // -10: sin el aire que lleva arriba cada bloque
        bool second = false;
        foreach (var item in items)
        {
            second |= GetBreakBefore(item);
            (second ? right : left).Children.Add(item);
        }
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        panel.Children.Add(grid);
    }

    private void Restore(StackPanel panel)
    {
        if (!columnized.Remove(panel, out var items)) return;
        foreach (var column in ((Grid)panel.Children[0]).Children.OfType<StackPanel>()) column.Children.Clear();
        panel.Children.Clear();
        foreach (var item in items) panel.Children.Add(item);
    }

    // Checked cubre ratón, teclado y lectores de pantalla; una actualización del enlace no vuelve a guardar.
    private void OnPetChoiceChecked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: PetChoice choice } && !choice.IsSelected)
            choice.Pick.Execute(null);
    }

    /// La cabecera mueve el panel: arrastrar lo deja donde lo sueltes y el doble clic
    /// lo devuelve junto a la bandeja. Los botones de la cabecera se quedan su clic.
    private void OnHeaderMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this) is not PanelWindow window) return;
        e.Handled = true;
        if (e.ClickCount == 2) { window.ResetPosition(); return; }
        window.BeginDrag();
        ((System.Windows.UIElement)sender).CaptureMouse();
    }

    private void OnHeaderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (((System.Windows.UIElement)sender).IsMouseCaptured && System.Windows.Window.GetWindow(this) is PanelWindow window)
            window.DragTo();
    }

    private void OnHeaderMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var header = (System.Windows.UIElement)sender;
        if (!header.IsMouseCaptured) return;
        header.ReleaseMouseCapture();
        if (System.Windows.Window.GetWindow(this) is PanelWindow window) window.EndDrag();
    }

    /// El halo del botón late mientras Ollama arranca o se apaga.
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyWide(((PanelViewModel)DataContext).WideLayout);
        var transition = ((PanelViewModel)DataContext).IsTransition;
        if (transition == pulsing) return;
        pulsing = transition;
        var pulse = transition
            ? new DoubleAnimation(1, 1.10, TimeSpan.FromSeconds(0.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            }
            : null;
        HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }
}
