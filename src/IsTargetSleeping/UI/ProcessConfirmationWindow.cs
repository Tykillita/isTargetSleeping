using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Confirmation includes the exact preview; no termination starts before the affirmative action.
/// Black glass like the rest of the app: coral warning badge, the affected processes in a glass
/// card, and coral only on the button that cannot be undone.
public sealed class ProcessConfirmationWindow : Window
{
    private const string GlyphWarning = "", GlyphShield = "";

    public ProcessConfirmationWindow(ProcessActionPreview preview)
        : this(preview.Request.Mode switch
        {
            ProcessActionMode.Application => tr("Finalizar aplicación"),
            ProcessActionMode.Tree => tr("Finalizar árbol"), _ => tr("Finalizar proceso"),
        }, Scope(preview.Request.Mode), preview.Processes, preview.UsesPrivateWorkingSet, preview.RamBytes, tr("Finalizar"), GlyphWarning) { }

    private ProcessConfirmationWindow(string title, string message, IReadOnlyList<ProcessSnapshot> processes, bool usesPrivate,
        long? totalRam, string affirmative, string glyph)
    {
        Title = title;
        Width = 460;
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico
        Icon = Mark.AppIcon(64);
        FontFamily = (FontFamily)Application.Current.FindResource("Sans");
        UseLayoutRounding = true;

        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

        // Cabecera: insignia coral con el glifo, título y alcance.
        var header = new DockPanel();
        var badgeGlyph = ProcessWindow.Glyph(glyph, 15, "Danger");
        badgeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = badgeGlyph,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        badge.SetResourceReference(Border.BackgroundProperty, "DangerSoft");
        DockPanel.SetDock(badge, Dock.Left);
        header.Children.Add(badge);
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(ProcessWindow.Text(title, 14.5, "Fg", FontWeights.SemiBold));
        heading.Children.Add(ProcessWindow.Text(message, 11.5, "Secondary", margin: new Thickness(0, 3, 0, 0)));
        header.Children.Add(heading);
        body.Children.Add(header);

        if (processes.Count > 0)
        {
            // Resumen: cuántos procesos y cuánta RAM, en la etiqueta de sección de la app.
            string ramLabel = usesPrivate ? tr("RAM privada residente") : tr("RAM residente total");
            long? ram = totalRam ?? processes.Sum(p => p.RamBytes(usesPrivate) ?? 0);
            var summary = ProcessWindow.Text(tr("%@ procesos · %@: %@", processes.Count, ramLabel, ProcessViewModel.Memory(ram)), 11.5, "Secondary",
                FontWeights.SemiBold, new Thickness(2, 14, 2, 6));
            body.Children.Add(summary);

            // Los procesos afectados, uno por fila, en una tarjeta de vidrio.
            var list = new StackPanel();
            for (int i = 0; i < processes.Count; i++)
            {
                var p = processes[i];
                var row = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
                var memory = ProcessWindow.Text(ProcessViewModel.Memory(p.RamBytes(usesPrivate)), 10.5, "Secondary");
                memory.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
                memory.VerticalAlignment = VerticalAlignment.Center;
                memory.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(memory, Dock.Right);
                row.Children.Add(memory);
                var name = ProcessWindow.Text(p.Name, 12, "Fg");
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                var pid = ProcessWindow.Text(tr("PID %@", p.Pid) + (string.IsNullOrEmpty(p.Executable) ? "" : " · " + p.Executable), 10, "Muted");
                pid.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
                pid.TextWrapping = TextWrapping.NoWrap;
                pid.TextTrimming = TextTrimming.CharacterEllipsis;
                var labels = new StackPanel();
                labels.Children.Add(name);
                labels.Children.Add(pid);
                row.Children.Add(labels);
                list.Children.Add(row);
            }
            var card = new Border { Style = ProcessWindow.ResourceStyle("CardBox"), Padding = new Thickness(11, 9, 11, 9),
                Child = new ScrollViewer { Content = list, MaxHeight = Math.Max(140, MaxHeight - 260),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            body.Children.Add(card);
        }

        // Aviso en ámbar: lo que se pierde no se recupera.
        var warning = new DockPanel { Margin = new Thickness(2, 12, 2, 0) };
        var warningGlyph = ProcessWindow.Glyph(GlyphWarning, 11, "Warm");
        warningGlyph.VerticalAlignment = VerticalAlignment.Top;
        warningGlyph.Margin = new Thickness(0, 2, 7, 0);
        DockPanel.SetDock(warningGlyph, Dock.Left);
        warning.Children.Add(warningGlyph);
        warning.Children.Add(ProcessWindow.Text(tr("El cierre es forzado. Puedes perder trabajo sin guardar."), 11, "Warm"));
        body.Children.Add(warning);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = ProcessWindow.Button(tr("Cancelar"), () => DialogResult = false);
        cancel.IsCancel = true;
        cancel.MinWidth = 88;
        var confirm = ProcessWindow.Button(affirmative, () => DialogResult = true, "DangerButton");
        confirm.Margin = new Thickness(0);
        confirm.MinWidth = 88;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        body.Children.Add(buttons);

        var tint = new Border { Child = body };
        tint.SetResourceReference(Border.BackgroundProperty, "PanelTint");
        Content = tint;
        SourceInitialized += (_, _) => Glass.Apply(this, extendFrame: true);
        Loaded += (_, _) => cancel.Focus();   // el foco empieza en Cancelar: Intro nunca finaliza por accidente
    }

    /// Acceso denegado: los procesos que faltan, y el reintento con permisos de administrador.
    public static bool Ask(Window owner, string title, string message, IReadOnlyList<ProcessSnapshot> processes, bool usesPrivate, string affirmative) =>
        new ProcessConfirmationWindow(title, message, processes, usesPrivate, null, affirmative, GlyphShield) { Owner = owner }.ShowDialog().GetValueOrDefault();

    private static string Scope(ProcessActionMode mode) => mode switch
    {
        ProcessActionMode.Application => tr("Todos los procesos de la aplicación y sus descendientes"),
        ProcessActionMode.Tree => tr("El proceso seleccionado y sus descendientes"),
        _ => tr("Solo el proceso seleccionado"),
    };

    /// El mismo contenido como texto (registro, pruebas y lectores de pantalla).
    public static string Confirmation(ProcessActionPreview preview)
    {
        string ramLabel = preview.UsesPrivateWorkingSet ? tr("RAM privada residente") : tr("RAM residente total");
        var lines = preview.Processes.Select(p => tr("%@ · PID %@ · %@", p.Name, p.Pid,
            ProcessViewModel.Memory(p.RamBytes(preview.UsesPrivateWorkingSet))));
        return Scope(preview.Request.Mode) + Environment.NewLine
            + tr("%@ procesos · %@: %@", preview.Processes.Count, ramLabel, ProcessViewModel.Memory(preview.RamBytes))
            + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines)
            + Environment.NewLine + Environment.NewLine + tr("El cierre es forzado. Puedes perder trabajo sin guardar.");
    }
}
