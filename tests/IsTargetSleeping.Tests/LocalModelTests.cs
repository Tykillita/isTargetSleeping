using IsTargetSleeping;

internal static class LocalModelTests
{
    public static void Run(Action<string, bool> check)
    {
        var models = LocalModels.ParseLmStudio("""
            {"data":[{"id":"mistral-7b","type":"llm","quantization":"Q4_0","arch":"llama","state":"not-loaded"},
                     {"id":"nomic-embed","type":"embeddings","state":"not-loaded"},
                     {"id":"qwen2.5-coder-14b","type":"llm","quantization":"Q4_K_M","arch":"qwen2","state":"loaded"},
                     {"id":"gemma-3-12b","type":"vlm","state":"not-loaded"},{"type":"llm"}]}
            """);
        check("LM Studio: los modelos de embeddings y los sin id no entran", models.Select(m => m.Id).SequenceEqual(["qwen2.5-coder-14b", "gemma-3-12b", "mistral-7b"]));
        check("LM Studio: los cargados van primero y con su detalle", models[0].Loaded && models[0].Meta == "Q4_K_M · qwen2" && !models[1].Loaded && models[1].Meta == "");
        check("LM Studio: una respuesta rota o vacía no da modelos", LocalModels.ParseLmStudio("no es json").Count == 0 && LocalModels.ParseLmStudio("{}").Count == 0);
        check("los widgets nuevos están en el catálogo con sus tamaños",
            WidgetCatalog.Find("lmstudio") is { Cells: 2 } && WidgetLayout.Sizes("lmstudio").SequenceEqual([(1, 1), (2, 1), (2, 2)])
            && WidgetCatalog.Find("launcher") is { Cells: 1 } && WidgetLayout.Sizes("launcher").Count == 4
            && WidgetCatalog.Folders.Single(f => f.Id == "ai").Widgets.Contains("lmstudio") && WidgetCatalog.Folders.Single(f => f.Id == "ai").Widgets.Contains("launcher"));
    }
}
