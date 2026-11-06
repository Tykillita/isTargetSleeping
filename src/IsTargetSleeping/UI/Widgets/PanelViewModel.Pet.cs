using System.Diagnostics;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed partial class PanelViewModel
{
    // El complemento «Mascota conectada» es un programa aparte: la app solo lo detecta y le pide el QR de emparejado.
    private static string PetAddon => Path.Combine(Paths.DataDir, "pet", "isTargetSleeping-pet.exe");
    public bool PetAddonInstalled => File.Exists(PetAddon);
    public string PhoneTitle => tr("Conectar celular");
    public string PhoneDetail => PetAddonInstalled
        ? tr("Muestra un código QR: escanéalo con la cámara del teléfono (con Tailscale activo) y podrás hablar con la mascota desde allí.")
        : tr("La web móvil se sirve desde esta PC por Tailscale y funciona sin una placa ESP32. Descarga el complemento opcional y extrae isTargetSleeping-pet.exe en %LOCALAPPDATA%\\isTargetSleeping\\pet.");
    public string PhoneButton => tr("Mostrar código QR");
    public ICommand ConnectPhone { get; private set; } = null!;
    public string DownloadPetButton => tr("Descargar complemento (opcional)");
    public ICommand DownloadPetAddon { get; private set; } = null!;

    private void InitPet()
    {
        DownloadPetAddon = new Command(() =>
        {
            var url = $"https://github.com/Tykillita/isTargetSleeping/releases/latest/download/isTargetSleeping-pet-win-{Updater.Arch}.zip";
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        });
        ConnectPhone = new Command(() =>
        {
            if (!PetAddonInstalled) return;
            try
            {
                var psi = new ProcessStartInfo(PetAddon, "pair") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(psi);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { }
        });
    }
}
