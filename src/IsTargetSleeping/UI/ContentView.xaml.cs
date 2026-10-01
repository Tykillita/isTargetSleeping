using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

public partial class ContentView : UserControl
{
    private bool pulsing;

    public ContentView(PanelViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += OnModelChanged;
        IsVisibleChanged += (_, _) => model.SetPanelVisible(IsVisible);
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
