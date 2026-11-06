using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// La letra sincronizada de lo que suena, como en Nusa: las líneas se ajustan al ancho (pueden ocupar dos renglones), la que se canta ahora va en blanco
/// y centrada en vertical, y las de alrededor se apagan hacia los bordes, donde el fundido las recorta. Se desplaza sola mientras está a la vista (cada 200 ms)
/// y, mientras busca la letra, lo dice.
internal sealed class LyricsView : Grid
{
    public static readonly DependencyProperty MediaProperty = DependencyProperty.Register(nameof(Media), typeof(MediaWidget), typeof(LyricsView));
    private const int Around = 4;                                   // líneas visibles a cada lado de la actual
    private const double Gap = 9;
    private static readonly double[] Fade = [1, 0.5, 0.32, 0.2, 0.12];   // por distancia a la actual
    private readonly TextBlock[] lines = new TextBlock[Around * 2 + 1];
    private readonly StackPanel stack = new();
    private readonly TranslateTransform shift = new();
    private readonly Canvas canvas = new() { ClipToBounds = true };
    private readonly TextBlock message = new() { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int shownIndex = int.MinValue;
    private bool placed;
    private LyricsStatus shownStatus = (LyricsStatus)(-1);

    public MediaWidget? Media { get => (MediaWidget?)GetValue(MediaProperty); set => SetValue(MediaProperty, value); }

    public LyricsView()
    {
        ClipToBounds = true;   // las líneas se pueden tocar: llevan la canción a ese momento (ver SeekToLyric)
        for (int i = 0; i < lines.Length; i++)
        {
            bool now = i == Around;
            lines[i] = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, Gap),
                Foreground = new SolidColorBrush(now ? Colors.White : Dim),
                FontSize = 16, FontWeight = FontWeights.Bold, Opacity = Fade[Math.Abs(i - Around)],
            };
            int slot = i;
            lines[i].Cursor = System.Windows.Input.Cursors.Hand;
            lines[i].Background = Brushes.Transparent;   // para que toda la línea, no solo las letras, reciba el clic
            lines[i].MouseLeftButtonUp += (_, e) =>
            {
                int at = shownIndex + slot - Around;
                if (Media is null || lines[slot].Text.Length == 0) return;
                Media.SeekToLyric(at);
                e.Handled = true;
            };
            stack.Children.Add(lines[i]);
        }
        stack.RenderTransform = shift;
        canvas.Children.Add(stack);
        canvas.OpacityMask = new LinearGradientBrush(
            [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.22), new GradientStop(Colors.Black, 0.78), new GradientStop(Colors.Transparent, 1)],
            new Point(0, 0), new Point(0, 1));
        Children.Add(canvas);
        Children.Add(message);
        SizeChanged += (_, _) => { stack.Width = Math.Max(0, ActualWidth); placed = false; Place(); };
        clock.Tick += (_, _) => Update();
        Loaded += (_, _) => { clock.Start(); Update(); };
        Unloaded += (_, _) => clock.Stop();
        // Al volver a verse parte de cero: con el reloj parado la letra avanzó varias líneas, y sin esto cada apertura del notch hacía un «salto» (la letra se fundía desde vacío).
        IsVisibleChanged += (_, _) => { if (IsVisible) { shownIndex = int.MinValue; placed = false; clock.Start(); Update(); } else clock.Stop(); };
    }

    private static bool Still => !Win32.AnimationsEnabled();   // «movimiento reducido» de Windows
    private static readonly Color Dim = Color.FromRgb(0xA8, 0xA8, 0xAD);

    /// Centra en vertical la línea actual. Con `from`, la pila parte de ese desplazamiento y se asienta con una animación corta (260 ms), de modo que al
    /// cambiar de línea el texto sube suave en vez de saltar; sin `from` se coloca de golpe (primera vez, cambio de tamaño).
    private double Target()
    {
        stack.UpdateLayout();
        var current = lines[Around];
        return ActualHeight / 2 - (current.TranslatePoint(new Point(0, 0), stack).Y + current.ActualHeight / 2);
    }

    private void Place(double? from = null)
    {
        if (ActualWidth < 1 || ActualHeight < 1 || stack.Visibility != Visibility.Visible) return;
        double target = Target();
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        if (!placed || from is null || Still) { shift.Y = target; placed = true; return; }
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(target + from.Value, target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// Los fundidos del cambio de línea: la que pasa a cantarse se enciende (gris → blanco) y la que se deja se apaga, como en Nusa; cada línea
    /// parte de la opacidad que tenía en su sitio anterior (`step` = lo que se ha avanzado, +1 o −1).
    private void Crossfade(int step)
    {
        if (Still) return;
        var span = TimeSpan.FromMilliseconds(280);
        for (int i = 0; i < lines.Length; i++)
        {
            int before = i + step;   // el hueco que ocupaba este texto antes de desplazarse
            double from = before >= 0 && before < lines.Length ? Fade[Math.Abs(before - Around)] : 0;
            lines[i].BeginAnimation(OpacityProperty, new DoubleAnimation(from, Fade[Math.Abs(i - Around)], span) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        var up = (SolidColorBrush)lines[Around].Foreground;
        up.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Dim, Colors.White, span));
        var down = (SolidColorBrush)lines[Around - step].Foreground;
        if (!ReferenceEquals(up, down)) down.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.White, Dim, span));
    }

    /// Un salto de varias líneas (avanzar o retroceder la canción de golpe): la letra entera se funde y entra deslizando unos píxeles desde el lado del salto.
    private void Jump(int direction)
    {
        if (Still) return;
        var span = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        canvas.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift.Y + direction * 36, shift.Y, span) { EasingFunction = ease });
    }

    private void Update()
    {
        if (Media is not { } media) return;
        var status = media.LyricStatus;
        if (status != shownStatus)
        {
            shownStatus = status;
            shownIndex = int.MinValue;
            placed = false;
            message.Visibility = status == LyricsStatus.Ready ? Visibility.Collapsed : Visibility.Visible;
            canvas.Visibility = status == LyricsStatus.Ready ? Visibility.Visible : Visibility.Collapsed;
            message.Text = status switch
            {
                LyricsStatus.Loading => L10n.tr("Buscando la letra…"),
                LyricsStatus.None => L10n.tr("Sin letra sincronizada para esta canción."),
                _ => "",
            };
        }
        if (status != LyricsStatus.Ready) return;
        int index = media.LyricIndex;
        if (index == shownIndex) return;
        int step = shownIndex == int.MinValue ? 0 : index - shownIndex;
        shownIndex = index;
        double oldCurrentHeight = lines[Around].ActualHeight + Gap;
        var all = media.LyricLines;
        for (int i = 0; i < lines.Length; i++)
        {
            int at = index + i - Around;
            lines[i].Text = at >= 0 && at < all.Count ? all[at].Text : "";
            lines[i].Margin = new Thickness(0, 0, 0, lines[i].Text.Length > 0 ? Gap : 0);
        }
        if (step is 1 or -1)
        {
            // Pasa a la línea de al lado: el texto ya está desplazado una fila, así que la pila parte desplazada al revés y sube/baja suave hasta su sitio.
            stack.UpdateLayout();
            double newCurrentHeight = lines[Around].ActualHeight + Gap;
            Place(step == 1 ? oldCurrentHeight : -newCurrentHeight);
            Crossfade(step);
            return;
        }
        Place();
        if (step != 0) Jump(step > 0 ? 1 : -1);
    }
}
