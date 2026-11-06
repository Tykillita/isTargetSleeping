using System.Windows.Input;
using IsTargetSleeping.UI.Widgets;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed partial class PanelViewModel
{
    public CameraPreview Camera { get; } = new();
    public bool ShowingCamera => view == PanelView.Camera;
    public bool CameraDockOn => Prefs.Switch(PrefKeys.CameraDock, false);
    public string CameraDockTitle => tr("Cámara en el dock");
    public string CameraDockDetail => tr("Añade un botón para abrir la vista previa; la cámara solo se activa con el interruptor.");
    public string CameraPageTitle => tr("Vista previa de la cámara");
    public string CameraPageDetail => tr("La vista previa es local, no se graba y solo se activa con el interruptor.");
    public string CameraAppsTitle => tr("Apps que usan la cámara");
    public string CameraAppsDetail => tr("La lista muestra las apps que transmiten ahora. En Comet puedes cambiar el permiso por sitio; Windows solo ofrece un control global para las demás apps de escritorio.");
    public string CameraStatusLabel => tr("Estado");
    public string CameraPreviewLabel => tr("Vista previa");
    public string CameraPreviewToggleLabel => Camera.PreviewEnabled ? tr("Desactivar vista previa") : tr("Activar vista previa");
    public string CameraIndicatorLabel => Camera.IsInUse ? tr("Cámara encendida. Abrir vista previa") : tr("Cámara apagada. Abrir vista previa");
    public string CameraPrivacyLabel => tr("Ajustes de privacidad");
    public string CameraPrivacyHelp => tr("Abrir los permisos de cámara de Windows");
    public string NotchTabCamera => tr("Cámara");
    public ICommand ToggleCameraDock { get; private set; } = null!;
    public ICommand ToggleCameraPreview { get; private set; } = null!;
    public ICommand OpenCameraPrivacy { get; private set; } = null!;

    private void InitCamera()
    {
        ToggleCameraDock = new Command(() => { Prefs.Toggle(PrefKeys.CameraDock, false); Notify(); });
        ToggleCameraPreview = new Command(() =>
        {
            if (Camera.PreviewEnabled) Camera.StopPreview();
            else if (!Ollama.IsDemo) Camera.StartPreview();
        });
        OpenCameraPrivacy = new Command(() => Paths.Open("ms-settings:privacy-webcam"));
        Camera.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Camera.PreviewEnabled))
                PropertyChanged?.Invoke(this, new(nameof(CameraPreviewToggleLabel)));
        };
        Camera.UsageChanged += () =>
        {
            PropertyChanged?.Invoke(this, new(nameof(CameraIndicatorLabel)));
        };
    }
}
