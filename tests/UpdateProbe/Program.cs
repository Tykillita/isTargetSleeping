using System.Diagnostics;
using System.Text.Json;

// Pequeño ejecutable real para comprobar el protocolo de arranque, sin abrir
// ventanas, conectarse a Ollama ni competir con la instancia del usuario.
if (args.Length != 2) return 2;
using var document = JsonDocument.Parse(File.ReadAllText(args[1]));
var session = document.RootElement;
if (args[0] == "--update-recovered")
{
    File.WriteAllText(args[1] + ".recovered", "restored");
    return 0;
}
if (args[0] != "--update-session") return 2;
if (File.Exists(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "fail-update"))) return 3;
File.WriteAllText(args[1] + ".ready", $"{session.GetProperty("Id").GetString()}:{Environment.ProcessId}:{session.GetProperty("Version").GetString()}");
Thread.Sleep(1000);
return 0;
