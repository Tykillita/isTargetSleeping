using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// Una tira de puntos de luz que enseña lo que hacen las luces: el color fijo con su brillo, el efecto que corre (saltos, fundidos, parpadeos) o,
/// con la música, tantos puntos encendidos como volumen. Solo se anima mientras hay algo que animar.
public sealed class LightStrip : FrameworkElement
{
    private const int Dots = 30;
    private static readonly DispatcherTimer Clock = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private static readonly System.Diagnostics.Stopwatch Time = System.Diagnostics.Stopwatch.StartNew();
    private bool attached;

    public static readonly DependencyProperty ColorProperty = Prop(nameof(Color), typeof(Color), Colors.White);
    public static readonly DependencyProperty BrightnessProperty = Prop(nameof(Brightness), typeof(double), 1.0);
    public static readonly DependencyProperty EffectProperty = Prop(nameof(Effect), typeof(int), 0);
    public static readonly DependencyProperty IsOnProperty = Prop(nameof(IsOn), typeof(bool), false);
    /// El volumen de la música de 0 a 1, o un valor negativo si la música no manda.
    public static readonly DependencyProperty LevelProperty = Prop(nameof(Level), typeof(double), -1.0);
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public int Effect { get => (int)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }
    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    private static DependencyProperty Prop(string name, Type type, object value) =>
        DependencyProperty.Register(name, type, typeof(LightStrip), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public LightStrip()
    {
        Height = 14;
        IsHitTestVisible = false;
        Loaded += (_, _) => { Theme.Changed += OnThemeChanged; Clock.Tick += Step; attached = true; Sync(); };
        Unloaded += (_, _) => { Theme.Changed -= OnThemeChanged; Clock.Tick -= Step; attached = false; Sync(); };
        IsVisibleChanged += (_, _) => Sync();
    }

    private static int active;
    private bool Animated => IsOn && (Effect > 0 || Level >= 0) && !Theme.ForceHighContrast && !SystemParameters.HighContrast;
    private bool counted;

    /// El reloj común corre mientras haya alguna tira animándose.
    private void Sync()
    {
        bool want = attached && IsVisible && Animated;
        if (want == counted) return;
        counted = want;
        active += want ? 1 : -1;
        if (active > 0) Clock.Start(); else Clock.Stop();
    }
    private void Step(object? sender, EventArgs e) { if (counted) InvalidateVisual(); }
    private void OnThemeChanged() { Sync(); InvalidateVisual(); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == EffectProperty || e.Property == IsOnProperty || e.Property == LevelProperty) Sync();
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// El color de un punto (0 a 1 a lo largo de la tira) en el instante t, y su intensidad.
    private (Color Color, double Glow) DotAt(double position, double t, IReadOnlyList<Color>? palette)
    {
        if (!IsOn) return (Theme.WithOpacity(Theme.Current.Fg, 0.18), 0.0);
        double level = 0.3 + 0.7 * Math.Clamp(Brightness, 0, 1);
        if (Level >= 0)
        {
            var rainbow = palette!;
            double spot = position * (rainbow.Count - 1);
            var color = Mix(rainbow[(int)Math.Floor(spot)], rainbow[Math.Min(rainbow.Count - 1, (int)Math.Floor(spot) + 1)], spot - Math.Floor(spot));
            return position <= Level ? (color, level) : (Theme.WithOpacity(Theme.Current.Fg, 0.18), 0.0);
        }
        if (LedEffects.Find(Effect) is not { } effect) return (Color, level);
        palette ??= LedEffects.PaletteOf(effect).Select(Rgb).ToList();
        switch (LedEffects.KindOf(effect))
        {
            case LedEffectKind.Jump:
                return (palette[(int)(t / 0.7) % palette.Count], level);
            case LedEffectKind.Blink:
                return ((int)(t / 0.45) % 2 == 0 ? (palette[0], level) : (Theme.WithOpacity(Theme.Current.Fg, 0.18), 0.0));
            default:
                if (palette.Count == 1) return (palette[0], level * (0.35 + 0.65 * (0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 2.4))));
                if (palette.Count == 2) { double w = 0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 3.0); return (Mix(palette[0], palette[1], w), level); }
                double p = ((position + t * 0.12) % 1.0 + 1.0) % 1.0 * palette.Count, i0 = Math.Floor(p);
                return (Mix(palette[(int)i0 % palette.Count], palette[((int)i0 + 1) % palette.Count], p - i0), level);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 40) return;
        double t = Time.Elapsed.TotalSeconds, step = (ActualWidth - 8) / (Dots - 1), cy = ActualHeight / 2;
        var palette = !IsOn ? null : Level >= 0 ? LedEffects.Rainbow.Select(Rgb).ToList()
            : LedEffects.Find(Effect) is { } effect ? LedEffects.PaletteOf(effect).Select(Rgb).ToList() : null;
        for (int i = 0; i < Dots; i++)
        {
            double position = i / (double)(Dots - 1);
            var (color, glow) = DotAt(position, t, palette);
            double x = 4 + i * step;
            if (glow > 0) dc.DrawEllipse(Theme.Brush(Theme.WithOpacity(color, 0.22 * glow)), null, new Point(x, cy), 6.2, 6.2);
            dc.DrawEllipse(Theme.Brush(glow > 0 ? Theme.WithOpacity(color, 0.45 + 0.55 * glow) : color), null, new Point(x, cy), 3.1, 3.1);
        }
    }
}
