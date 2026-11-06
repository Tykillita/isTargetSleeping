using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IsTargetSleeping;

internal static class ChatTests
{
    public static void Run(Action<string, bool> check)
    {
        check("sin modelos cargados no se elige ninguno", ChatService.PickModel([]) is null);
        check("se elige el primer modelo cargado", ChatService.PickModel([new("a:1b", 1, 0, null), new("b:2b", 1, 0, null)]) == "a:1b");
        var chunk = ChatService.ParseChunk("{\"message\":{\"role\":\"assistant\",\"content\":\"Hola\"},\"done\":false}");
        check("se entiende un trozo de la respuesta", chunk is { Content: "Hola", Done: false, Error: null });
        check("se entiende el final", ChatService.ParseChunk("{\"done\":true}") is { Done: true });
        check("una línea rota se ignora", ChatService.ParseChunk("{no") is null);
        var history = Enumerable.Range(0, 30).Select(i => new ChatTurn(i % 2 == 0 ? "user" : "assistant", new string('x', 9000))).ToList();
        var messages = ChatService.Body("m", "Spanish", history)["messages"]!.AsArray();
        check("el historial se recorta en turnos y en longitud", messages.Count == ChatService.MaxTurns + 1
            && messages[1]!["content"]!.GetValue<string>().Length == ChatService.MaxChars && messages[0]!["role"]!.GetValue<string>() == "system");

        check("cada mascota tiene su carácter y una desconocida no inventa uno", new[] { "mira", "llama", "capybara", "orange-cat" }.All(id => PetPersonality.For(id) is { Length: > 40 }) && PetPersonality.For("otra") is null && PetPersonality.For(null) is null);
        var personal = ChatService.Body("m", "Spanish", [new("user", "hola")], PetPersonality.For("capybara"))["messages"]![0]!["content"]!.GetValue<string>();
        check("el carácter de la mascota va al prompt del sistema", personal.Contains("capybara") && !ChatService.SystemPrompt("Spanish").Contains("Your character"));
        var service = new ChatService();
        check("sin modelo cargado no se pregunta nada", service.Ask([new("user", "hola")], null, "Spanish", null, CancellationToken.None).Result.Failure == ChatFailure.NoModel);

        // Servidor falso de Ollama: la primera respuesta llega en tres trozos y la segunda es un error.
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        var prefix = $"http://127.0.0.1:{port}/";
        using var server = new HttpListener();
        server.Prefixes.Add(prefix);
        server.Start();
        string? received = null;
        var serving = Task.Run(() =>
        {
            for (int i = 0; i < 2; i++)
            {
                var context = server.GetContext();
                received = new StreamReader(context.Request.InputStream).ReadToEnd();
                var bytes = Encoding.UTF8.GetBytes(i == 0
                    ? "{\"message\":{\"content\":\"Ho\"},\"done\":false}\n{\"message\":{\"content\":\"la\"},\"done\":false}\n{\"message\":{\"content\":\"!\"},\"done\":true}\n"
                    : "{\"error\":\"model not found\"}\n");
                context.Response.OutputStream.Write(bytes);
                context.Response.Close();
            }
        });
        var local = new ChatService(baseUri: new Uri(prefix));
        var seen = new List<string>();
        var ok = local.Ask([new("user", "hola")], "m", "Spanish", seen.Add, CancellationToken.None).Result;
        check("la respuesta llega completa y en streaming", ok is { Text: "Hola!", Failure: ChatFailure.None } && seen.SequenceEqual(["Ho", "Hola", "Hola!"]));
        check("se envía el modelo, el sistema y el mensaje", JsonNode.Parse(received!)!["model"]!.GetValue<string>() == "m" && JsonNode.Parse(received!)!["messages"]!.AsArray().Count == 2);
        var failed = local.Ask([new("user", "hola")], "m", "Spanish", null, CancellationToken.None).Result;
        check("un error de Ollama se devuelve sin romper", failed is { Failure: ChatFailure.Error, Detail: "model not found" });
        serving.Wait(2000);
        server.Stop();
        var offline = new ChatService(baseUri: new Uri(prefix)).Ask([new("user", "hola")], "m", "Spanish", null, CancellationToken.None).Result;
        check("con Ollama apagado se dice que no hay conexión", offline.Failure == ChatFailure.Offline);
    }
}
