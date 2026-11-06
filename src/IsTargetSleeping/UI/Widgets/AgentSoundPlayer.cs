using System.Media;

namespace IsTargetSleeping.UI;

/// Reproduce los avisos de `AgentSounds` (los sintetiza una vez y los guarda).
public static class AgentSoundPlayer
{
    private static readonly Dictionary<AgentSound, SoundPlayer> players = [];

    public static void Play(AgentSound sound)
    {
        try
        {
            if (!players.TryGetValue(sound, out var player)) players[sound] = player = new SoundPlayer(new MemoryStream(AgentSounds.Wave(sound)));
            player.Play();
        }
        catch { /* sin tarjeta de sonido o con el audio ocupado: sin aviso, nunca un error */ }
    }
}
