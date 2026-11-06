namespace IsTargetSleeping;

public enum AgentSound { Approval, Finished }

/// Dos avisos cortos y suaves, sintetizados aquí (sin archivos ni derechos de nadie): un agente pide permiso o terminó.
public static class AgentSounds
{
    private const int Rate = 22050;
    private const double Volume = 0.18;

    private static (double Hz, double Seconds)[] Notes(AgentSound sound) => sound == AgentSound.Approval
        ? [(880, 0.09), (1175, 0.16)]
        : [(659, 0.08), (880, 0.08), (1319, 0.18)];

    /// WAV mono de 16 bits: cada nota es un seno con ataque rápido y caída suave.
    public static byte[] Wave(AgentSound sound)
    {
        var samples = new List<short>();
        foreach (var (hz, seconds) in Notes(sound))
        {
            int n = (int)(Rate * seconds);
            for (int i = 0; i < n; i++)
            {
                double envelope = Math.Min(1, i / (0.008 * Rate)) * Math.Exp(-3.0 * i / n);
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / Rate) * envelope * Volume * short.MaxValue));
            }
        }
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8); w.Write(36 + samples.Count * 2); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Count * 2);
        foreach (var s in samples) w.Write(s);
        return stream.ToArray();
    }
}
