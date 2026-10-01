namespace IsTargetSleeping;

/// Cómo se llega a una clave: a velocidad constante, con aceleración y frenada, solo
/// frenando, solo acelerando, pasándose un poco y volviendo (rebote) o de golpe.
public enum Ease { Linear, InOut, Out, In, OutBack, Step }

public static class Easing
{
    public static double Apply(Ease ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ease switch
        {
            Ease.Linear => t,
            Ease.Out => TrayMotion.EaseOutCubic(t),
            Ease.In => t * t * t,
            Ease.OutBack => TrayMotion.EaseOutBack(t),
            Ease.Step => t >= 1 ? 1 : 0,
            _ => TrayMotion.EaseInOutCubic(t),
        };
    }
}

/// Una clave: la pose en el segundo `T` y la curva con la que se llega a ella.
public readonly record struct PetKey(double T, PetPose Pose, Ease Ease = Ease.InOut);

/// Una animación hecha de claves que se interpolan: se muestrea a cualquier fps. En un
/// bucle, la última clave vuelve a la primera en el tiempo que queda hasta `Length`.
public sealed class PetClip
{
    public PetKey[] Keys { get; }
    public double Length { get; }
    public bool Loop { get; }

    public PetClip(PetKey[] keys, bool loop = true, double? length = null)
    {
        if (keys.Length == 0) keys = [new(0, new PetPose())];
        Keys = keys.OrderBy(k => k.T).ToArray();
        Loop = loop;
        Length = Math.Max(length ?? Keys[^1].T, Keys[^1].T);
    }

    public static PetClip Hold(PetPose pose) => new([new(0, pose)], loop: true, length: 1);

    public PetPose At(double seconds)
    {
        if (Keys.Length == 1) return Keys[0].Pose;
        double t = Math.Max(0, seconds);
        if (Loop && Length > 0) t %= Length;
        else if (t >= Keys[^1].T) return Keys[^1].Pose;
        for (int i = 1; i < Keys.Length; i++)
        {
            if (t > Keys[i].T) continue;
            var (from, to) = (Keys[i - 1], Keys[i]);
            double span = to.T - from.T;
            return span <= 0 ? to.Pose : PetPose.Lerp(from.Pose, to.Pose, Easing.Apply(to.Ease, (t - from.T) / span));
        }
        // Bucle: de la última clave a la primera.
        var (last, first) = (Keys[^1], Keys[0]);
        double rest = Length - last.T;
        return rest <= 0 ? first.Pose : PetPose.Lerp(last.Pose, first.Pose, Easing.Apply(first.Ease, (t - last.T) / rest));
    }
}
