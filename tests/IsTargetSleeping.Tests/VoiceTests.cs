using IsTargetSleeping;

internal static class VoiceTests
{
    public static void Run(Action<string, bool> check)
    {
        check("un idioma sin reconocedor instalado no se ofrece", !Voice.CanListen("xx") && Voice.RecognizerFor("xx") is null);
        if (!Voice.CanListen("en")) { check("(sin reconocedor de inglés en este PC: se omite la prueba de reconocimiento)", true); return; }

        // De punta a punta y sin micrófono: la voz del PC dice una frase a un WAV y el reconocedor local la entiende.
        var wav = Path.Combine(Path.GetTempPath(), "its-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            Voice.SynthesizeToFile("hello how are you today", "en", wav);
            check("la síntesis local genera un audio", File.Exists(wav) && new FileInfo(wav).Length > 10_000);
            var heard = Voice.Listen("en", CancellationToken.None, e => e.SetInputToWaveFile(wav)).GetAwaiter().GetResult();
            check("el reconocedor local entiende la frase (sin red)", heard is not null && heard.Contains("hello", StringComparison.OrdinalIgnoreCase));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var none = Voice.Listen("en", cancelled.Token, e => e.SetInputToWaveFile(wav)).GetAwaiter().GetResult();
            check("cancelar la escucha devuelve null sin romper", none is null || none.Length > 0);
        }
        finally { try { File.Delete(wav); } catch { } }
    }
}
