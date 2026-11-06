namespace IsTargetSleeping;

/// Solo lo que PetBridge necesita de la app principal (la clase real arrastra medio proyecto WPF). Misma regla:
/// ITS_DATA_DIR redirige la carpeta de datos.
static class Paths
{
    public static string DataDir =>
        Environment.GetEnvironmentVariable("ITS_DATA_DIR") is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "isTargetSleeping");
}
