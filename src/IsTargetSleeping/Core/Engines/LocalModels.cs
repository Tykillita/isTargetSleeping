using System.Text.Json.Nodes;

namespace IsTargetSleeping;

/// Un modelo local de LM Studio listo para el lanzador: su id, una línea de detalle («Q4_K_M · qwen2») y si está cargado.
public sealed record LocalModelInfo(string Id, string Meta, bool Loaded);

public static class LocalModels
{
    /// La respuesta de LM Studio `GET /api/v0/models`: cada modelo con `id`, `type` (llm, vlm o embeddings), `quantization`, `arch` y `state` (loaded o not-loaded).
    /// Los modelos de embeddings no se pueden «lanzar» como chat, así que no entran. Los cargados primero; luego, por nombre.
    public static List<LocalModelInfo> ParseLmStudio(string json)
    {
        try
        {
            var list = new List<LocalModelInfo>();
            foreach (var m in (JsonNode.Parse(json)?["data"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var id = m["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || m["type"]?.GetValue<string>() == "embeddings") continue;
                var meta = string.Join(" · ", new[] { m["quantization"]?.GetValue<string>(), m["arch"]?.GetValue<string>() }.Where(s => !string.IsNullOrWhiteSpace(s)));
                list.Add(new(id, meta, m["state"]?.GetValue<string>() == "loaded"));
            }
            return [.. list.OrderByDescending(x => x.Loaded).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return []; }
    }
}
