using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un agente propio en la lista de Ajustes: nombre, resumen, y su interruptor de activo.
public sealed record TeamAgentRow(string Title, string Hint, bool On, ICommand Toggle);

/// Agentes propios (`team\agents.json`): desde aquí se activan y desactivan; se crean y editan en la web móvil o a mano en el archivo.
public sealed partial class PanelViewModel
{
    public string TeamAgentsTitle => tr("Mis agentes");
    public string TeamAgentsDetail => tr("Agentes propios con sus instrucciones y un tope de gasto. Se crean desde la pestaña Agentes del celular o editando el archivo, y solo se ejecutan con tu confirmación.");
    public string TeamAgentsOpenButton => tr("Abrir agents.json");

    public ICommand OpenTeamAgentsFile => new Command(() =>
    {
        try
        {
            var path = AgentProfiles.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, "[]");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    });

    public IReadOnlyList<TeamAgentRow> TeamAgentRows => AgentProfiles.Load().Select(p =>
        new TeamAgentRow(p.Name, $"{p.Engine} · {p.Model} · {(p.CanWrite ? tr("escribe en una rama") : tr("solo lectura"))} · {tr("tope")} {p.MaxTokens / 1000}k", p.Enabled,
            new Command(() => SetTeamAgentEnabled(p.Id, !p.Enabled)))).ToList();

    public bool ShowTeamAgents => PetAddonInstalled;

    private void SetTeamAgentEnabled(string id, bool on)
    {
        var all = AgentProfiles.Load().Select(p => p.Id == id ? p with { Enabled = on } : p).ToList();
        try { AgentProfiles.Save(all); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Notify();
    }
}
