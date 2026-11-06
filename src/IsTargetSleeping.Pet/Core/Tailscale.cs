using System.Diagnostics;
using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Averigua la direccion del tailnet y publica la web local con `tailscale serve` (solo dentro del tailnet, nunca Funnel).
static class Tailscale
{
    static string Exe()
    {
        string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        return File.Exists(pf) ? pf : "tailscale";
    }

    static (int Code, string Out) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(30000)) { try { p.Kill(); } catch (InvalidOperationException) { } return (-1, ""); }
            return (p.ExitCode, o.Result);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, ""); }
    }

    /// https://<pc>.<tailnet>.ts.net, o null si Tailscale no esta conectado.
    public static string? Url()
    {
        var (code, json) = Run("status", "--json");
        if (code != 0) return null;
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            if (root.GetProperty("BackendState").GetString() != "Running") return null;
            string? dns = root.GetProperty("Self").GetProperty("DNSName").GetString()?.TrimEnd('.');
            return string.IsNullOrEmpty(dns) ? null : "https://" + dns;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException) { return null; }
    }

    public static bool Serve(int port) => Run("serve", "--bg", port.ToString()).Code == 0;
}
