namespace IsTargetSleeping.UI.Pets;

/// El cocodrilo que pasea a la capibara mientras Ollama arranca: pixel art de perfil,
/// mirando a la izquierda como ella, con ceja y ojo amarillo, hocico con dientes, crestas
/// en el lomo, barriga clara y una cola que se afila. Ocupa todo el ancho de la rejilla.
/// `into`: de fuera por la derecha (0) a su sitio (1). `step`: el paso (−1 … 1): las patas
/// en diagonal se levantan por turnos y el cuerpo se mece un poco.
internal static class Crocodile
{
    /// Dónde empieza su lomo en la rejilla: la capibara se sienta aquí.
    internal const double Top = 18.7;

    private static readonly string[] Body =
    [
        "..........oooo..................",
        ".........oyykoo..oo..oo..oo.....",
        "...ooooooddddddoorroorroorro....",
        "..ommmmmmdddddddddddddddddddo...",
        ".ommmmmmmmgggggggggggggggggggoo.",
        "omwowowowooggggggggggggggggggggo",
        "ommmmmmmmmollllllllllllllllggoo.",
        ".ooooooooollllllllllllllllloo...",
        "..........ooooooooooooooooo.....",
    ];
    private static readonly string[] Leg = ["oo", "do", "dd", "oo"];

    private static uint Color(char k) => k switch
    {
        'g' => 0xFF5F913B, 'd' => 0xFF416828, 'm' => 0xFF7CA848, 'l' => 0xFFD2DE95, 'r' => 0xFF2F4D1E,
        'y' => 0xFFF6D34C, 'k' => 0xFF1C1C1C, 'w' => 0xFFF7F4EC, _ => 0xFF24361B,
    };

    internal static void Draw(PixelCanvas c, int n, double into, double step)
    {
        double k = Math.Clamp(into, 0, 1);
        if (k <= 0.01) return;
        step = Math.Clamp(step, -1, 1);
        double ox = (1 - k) * 34, oy = Top - 0.2 * Math.Abs(step);
        // Las patas, detrás del cuerpo: delantera cercana con trasera lejana, y al revés.
        int[] legs = [7, 11, 19, 23];
        for (int i = 0; i < legs.Length; i++)
        {
            double phase = i % 2 == 0 ? step : -step;
            double lift = Math.Max(0, phase) * 0.8, shift = phase * 0.4;
            c.Stamp((ox + legs[i] + shift) * n, (Top + 7 - lift) * n, Leg, Color, n);
        }
        c.Stamp(ox * n, oy * n, Body, Color, n);
    }
}
