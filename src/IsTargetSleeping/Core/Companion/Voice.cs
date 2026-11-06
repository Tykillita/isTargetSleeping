using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace IsTargetSleeping;

/// Voz local de la mascota: reconocimiento y síntesis de Windows (SAPI), sin red. Solo se escucha cuando el usuario pulsa el botón y por una sola frase;
/// nunca hay escucha permanente.
public static class Voice
{
    private static SpeechSynthesizer? synth;

    /// La cultura de un reconocedor instalado para ese idioma («es» → «es-ES»), o null si no hay.
    public static string? RecognizerFor(string language)
    {
        try { return SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture).FirstOrDefault(c => c.TwoLetterISOLanguageName == language)?.Name; }
        catch { return null; }
    }

    public static bool CanListen(string language) => RecognizerFor(language) is not null;

    /// Escucha una frase. `input` elige de dónde (por defecto, el micrófono; las pruebas pasan un archivo de audio). Devuelve el texto o null si no se oyó nada.
    public static async Task<string?> Listen(string language, CancellationToken ct, Action<SpeechRecognitionEngine>? input = null)
    {
        var culture = RecognizerFor(language) ?? throw new InvalidOperationException("no recognizer");
        using var engine = new SpeechRecognitionEngine(new CultureInfo(culture));
        engine.LoadGrammar(new DictationGrammar());
        (input ?? (e => e.SetInputToDefaultAudioDevice()))(engine);
        engine.InitialSilenceTimeout = TimeSpan.FromSeconds(6);
        engine.EndSilenceTimeout = TimeSpan.FromSeconds(0.9);
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SpeechRecognized += (_, e) => done.TrySetResult(e.Result.Text);
        engine.RecognizeCompleted += (_, e) => done.TrySetResult(e.Result?.Text);
        engine.RecognizeAsync(RecognizeMode.Single);
        using var cancel = ct.Register(() => { try { engine.RecognizeAsyncCancel(); } catch { } done.TrySetResult(null); });
        var text = (await done.Task)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void Select(SpeechSynthesizer s, string language)
    {
        try { s.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, new CultureInfo(language == "es" ? "es-ES" : "en-US")); }
        catch { /* sin voz de ese idioma: se queda con la predeterminada */ }
    }

    /// Dice el texto en voz alta (sin bloquear). Una frase nueva corta la anterior.
    public static void Speak(string text, string language)
    {
        try
        {
            synth ??= new SpeechSynthesizer();
            synth.SpeakAsyncCancelAll();
            Select(synth, language);
            synth.SpeakAsync(text);
        }
        catch { /* sin audio: la respuesta sigue escrita en el chat */ }
    }

    public static void StopSpeaking()
    {
        try { synth?.SpeakAsyncCancelAll(); } catch { }
    }

    /// Para pruebas: la misma voz, a un archivo WAV en vez de a los altavoces.
    public static void SynthesizeToFile(string text, string language, string path)
    {
        using var s = new SpeechSynthesizer();
        Select(s, language);
        s.SetOutputToWaveFile(path);
        s.Speak(text);
    }
}
