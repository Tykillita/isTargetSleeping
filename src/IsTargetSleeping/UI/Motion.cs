using System.Windows.Media;
using System.Windows.Media.Animation;
using IsTargetSleeping;

namespace IsTargetSleeping.UI;

/// Movimiento compartido del panel y la lente: cada destino sustituye la animación anterior y respeta movimiento reducido.
public static class Motion
{
    public static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(180);

    public static void Spring(ScaleTransform scale, double value, int milliseconds, bool reduced, bool bounce)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (reduced) { scale.ScaleX = scale.ScaleY = value; return; }
        var easing = Ease(bounce);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, duration) { EasingFunction = easing });
    }

    public static IEasingFunction Ease(bool spring = false) => spring
        ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = Math.Clamp((1 - LensPhysics.Damping) * 1.4, .2, .75) }
        : new QuadraticEase { EasingMode = EasingMode.EaseOut };
}
