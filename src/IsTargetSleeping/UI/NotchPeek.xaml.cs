namespace IsTargetSleeping.UI;

/// Lo que se ve al pasar el ratón por el notch: el resumen corto (un clic abre el panel entero).
public partial class NotchPeek : System.Windows.Controls.UserControl
{
    public NotchPeek(PanelViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
