using System.Text.Json;

namespace IsTargetSleeping;

/// Reserva de actividad del complemento «Mascota conectada»: mientras la mascota conversa con el modelo, el complemento deja
/// `<datos>\pet\lease.json` ({pid, until}); la app no descarga el modelo por inactividad mientras la reserva siga vigente. Caduca
/// sola (60 s sin renovarse) y no cuenta si el proceso que la dejó ya no existe. Sin complemento no existe el archivo.
internal static class PetLease
{
    public static bool Active => IsActive(Path.Combine(Paths.DataDir, "pet"), DateTimeOffset.UtcNow);

    internal static bool IsActive(string petDir, DateTimeOffset now)
    {
        try
        {
            var path = Path.Combine(petDir, "lease.json");
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (now.ToUnixTimeSeconds() >= doc.RootElement.GetProperty("until").GetInt64()) return false;
            try { System.Diagnostics.Process.GetProcessById(doc.RootElement.GetProperty("pid").GetInt32()); return true; }
            catch (ArgumentException) { return false; }   // reserva huérfana: el complemento ya no está
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
