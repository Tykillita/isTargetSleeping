using IsTargetSleeping;
using Microsoft.Win32;

internal static class TrayPinTests
{
    public static void Run(Action<string, bool> check)
    {
        // Rama temporal propia: nunca se toca la de verdad del Explorador.
        var node = $@"Software\IsTargetSleepingTest\{Guid.NewGuid():N}";
        var root = $@"{node}\NotifyIconSettings";
        try
        {
            const string exe = @"C:\Apps\isTargetSleeping.exe";
            check("sin entradas: ni disponible ni fijado",
                TrayPin.KeysFor(root, exe).Count == 0 && !TrayPin.PinnedFor(root, exe));
            check("exe nulo: vacío", TrayPin.KeysFor(root, null).Count == 0);

            using (var other = Registry.CurrentUser.CreateSubKey($@"{root}\111"))
                other.SetValue("ExecutablePath", @"C:\Otro\app.exe");
            check("la entrada de otro exe se ignora", TrayPin.KeysFor(root, exe).Count == 0);

            using (var one = Registry.CurrentUser.CreateSubKey($@"{root}\222"))
                one.SetValue("ExecutablePath", @"c:\apps\ISTARGETSLEEPING.EXE");
            check("una entrada coincide sin importar mayúsculas",
                TrayPin.KeysFor(root, exe).SequenceEqual(["222"]) && !TrayPin.PinnedFor(root, exe));

            TrayPin.SetPinnedFor(root, exe, true);
            check("fijar escribe IsPromoted=1",
                Registry.CurrentUser.OpenSubKey($@"{root}\222")?.GetValue("IsPromoted") is 1
                && TrayPin.PinnedFor(root, exe));
            TrayPin.SetPinnedFor(root, exe, false);
            check("soltar escribe IsPromoted=0",
                Registry.CurrentUser.OpenSubKey($@"{root}\222")?.GetValue("IsPromoted") is 0
                && !TrayPin.PinnedFor(root, exe));

            // Windows abrevia Program Files con su GUID de carpeta conocida.
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfExe = Path.Combine(pf, "isTargetSleeping", "isTargetSleeping.exe");
            using (var guid = Registry.CurrentUser.CreateSubKey($@"{root}\333"))
                guid.SetValue("ExecutablePath", @"{6D809377-6AF0-444B-8957-A3773F02200E}\isTargetSleeping\isTargetSleeping.exe");
            check("la ruta con GUID se resuelve a Program Files", TrayPin.KeysFor(root, pfExe).SequenceEqual(["333"]));

            using (var two = Registry.CurrentUser.CreateSubKey($@"{root}\444"))
            {
                two.SetValue("ExecutablePath", exe);
                two.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            }
            check("una entrada nueva sin promover apaga el interruptor aunque otra siga fijada",
                TrayPin.KeysFor(root, exe).Count == 2 && !TrayPin.PinnedFor(root, exe));
            TrayPin.SetPinnedFor(root, exe, true);
            check("reactivar promueve todas las entradas",
                TrayPin.PinnedFor(root, exe)
                && Registry.CurrentUser.OpenSubKey($@"{root}\222")?.GetValue("IsPromoted") is 1
                && Registry.CurrentUser.OpenSubKey($@"{root}\444")?.GetValue("IsPromoted") is 1);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(node); } catch { }
        }
    }
}
