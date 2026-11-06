using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace IsTargetSleeping.UI;

/// El aviso del sistema dentro del notch (volumen, brillo, Bloq. Mayús, Bloq. Núm, idioma del teclado), con el diseño de Nusa: a la izquierda un icono blanco,
/// después el título o una barra gruesa blanca sobre gris, y a la derecha el estado («On» en verde, «Off» en gris) o la etiqueta del idioma («EN»).
internal sealed class NotchHudView : Grid
{
    private readonly Grid body = new() { Margin = new Thickness(18, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
    private static readonly Brush White = Frozen(Colors.White), Track = Frozen(Color.FromRgb(0x4C, 0x4C, 0x4C)), Green = Frozen(Color.FromRgb(0x30, 0xD1, 0x58)),
        Gray = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93)), Chip = Frozen(Color.FromRgb(0x3A, 0x3A, 0x3C));
    private static readonly Color LyricDim = Color.FromRgb(0x8A, 0x8A, 0x8F);   // la línea que aún no se canta: gris, como una sombra
    private string builtFor = "";
    private Grid? barGrid;
    private Border? fillBar;
    private double barTarget = -1, barWidthShown = -1;
    private bool barPlaced;
    private Canvas? sunRays;
    private ScaleTransform? sunScale;
    private RotateTransform? sunRotate;
    private double sunFraction = -1;
    // Las partes de cada icono que se mueven: cada aviso tiene su animación (volumen: ondas que aparecen; mayúsculas: la flecha salta y se rellena; número: la tecla se pulsa;
    // idioma: el globo gira sobre su eje). La nota de la letra no se anima.
    private System.Windows.Shapes.Path? volWave1, volWave2, volCross, capsArrow, numOne;
    private SolidColorBrush? capsFill;
    private ScaleTransform? volPulse, keyPress, chipPop, badgePop, devicePop;
    private TranslateTransform? textShift;
    private ScaleTransform? pulseScale;
    private TextBlock? btSub;
    private Grid? btRing;
    private System.Windows.Shapes.Path[]? meridians;
    private TranslateTransform? capsShift, stateShift;
    private double volLevel = -1;
    private bool? lockOn;
    private string lastLanguage = "";
    private TextBlock? stateText;
    /// El color de la portada, para el borde de abajo del aviso de letras.
    public Brush? Tint { get; set; }

    public NotchHudView()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        UseLayoutRounding = true;
        Background = Brushes.Black;   // tapa lo que hay debajo (la hora, la mascota, los indicadores) mientras se enseña
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18 + 14) });   // icono (18) con su respiro
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        base.Children.Add(body);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// Pone (o actualiza) lo que enseña; solo reconstruye los elementos cuando cambia el tipo de aviso.
    public void Show(HudInfo? info)
    {
        if (info is null) return;
        // La letra no incluye la línea en la clave: cambiar de línea no reconstruye nada, solo cambia el texto (el notch se queda quieto).
        var key = info.Kind == HudKind.Lyric ? "Lyric|" : info.Kind == HudKind.Bluetooth ? $"Bluetooth|{info.Title}|{info.On}" : $"{info.Kind}|{info.Title}|{info.Chip}|{info.HasBar}";
        // La letra cambia de línea como un carrusel vertical (Nusa): la nueva sube desde abajo y la vieja se va hacia arriba mientras se apaga.
        bool carousel = key == builtFor && info.Kind == HudKind.Lyric && lyricBlock is not null && lyricBlock.Text != info.Title;
        string oldLine = lyricBlock?.Text ?? "";
        double oldWidth = lyricBlock?.Width ?? double.NaN;
        Background = info.Kind == HudKind.Lyric ? Brushes.Transparent : Brushes.Black;   // con letra deja ver el degradado de la forma del notch (un fondo negro propio hacía un cuadro al abrir)
        if (key != builtFor) Build(info);
        else if (carousel) { lyricBlock!.Text = info.Title; FixWidth(lyricBlock); }
        else if (info.Kind == HudKind.Bluetooth) UpdateBluetooth(info);
        builtFor = key;
        if (carousel && Win32.AnimationsEnabled()) Roll(oldLine, oldWidth);
        if (lyricBlock?.Foreground is SolidColorBrush ink)
        {
            var target = info.Dim ? LyricDim : Colors.White;
            if (ink.Color != target) ink.BeginAnimation(SolidColorBrush.ColorProperty, Win32.AnimationsEnabled() ? new ColorAnimation(target, TimeSpan.FromMilliseconds(320)) : null);
            if (!Win32.AnimationsEnabled()) ink.Color = target;
        }
        if (fillBar is not null && info.Fraction is { } f)
        {
            barTarget = Math.Clamp(f, 0, 1);
            PlaceFill();
        }
        if (sunRays is not null && info.Fraction is { } sun && Math.Abs(sun - sunFraction) > 0.0005)
        {
            AnimateSun(Math.Clamp(sun, 0, 1), sunFraction >= 0);
            sunFraction = Math.Clamp(sun, 0, 1);
        }
        if (stateText is not null && info.On is { } on)
        {
            var text = L10n.tr(on ? "Activado" : "Desactivado");
            bool changed = stateText.Text != text;
            stateText.Text = text;
            stateText.Foreground = on ? Green : Gray;
            if (changed && stateShift is not null)
            {
                stateText.BeginAnimation(OpacityProperty, Win32.AnimationsEnabled() ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) : null);
                Spring(stateShift, TranslateTransform.XProperty, 10, 0, 420, 0.8);
            }
        }
        // Volumen: las ondas del altavoz aparecen y se van según el nivel, la cruz sale al silenciar y el altavoz late con cada cambio.
        if (volPulse is not null && volWave1 is not null && volWave2 is not null && volCross is not null && info.Fraction is { } level)
        {
            bool muted = info.Muted || level <= 0, first = volLevel < 0;
            if (first || Math.Abs(level - volLevel) > 0.0005 || muted != (volLevel <= 0))
            {
                Reveal(volCross, muted, !first);
                Reveal(volWave1, !muted, !first);
                Reveal(volWave2, !muted && level > 0.34, !first);
                if (!first) Pulse(volPulse, 1.18);
                volLevel = level;
            }
        }
        // Mayúsculas y Núm: la flecha salta y se rellena (o se vacía); la tecla se pulsa y su «1» se enciende o se apaga.
        if (info.On is { } lockState && lockOn != lockState)
        {
            lockOn = lockState;
            if (capsArrow is not null && capsFill is not null && capsShift is not null)
            {
                var to = lockState ? Colors.White : Color.FromArgb(0, 255, 255, 255);
                if (Win32.AnimationsEnabled()) capsFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(240)));
                else { capsFill.BeginAnimation(SolidColorBrush.ColorProperty, null); capsFill.Color = to; }
                Spring(capsShift, TranslateTransform.YProperty, lockState ? 6 : -4, 0, 520, 1.0);
            }
            if (numOne is not null && keyPress is not null)
            {
                numOne.BeginAnimation(OpacityProperty, Win32.AnimationsEnabled() ? new DoubleAnimation(lockState ? 1 : 0.3, TimeSpan.FromMilliseconds(220)) : null);
                if (!Win32.AnimationsEnabled()) numOne.Opacity = lockState ? 1 : 0.3;
                Spring(keyPress, ScaleTransform.ScaleXProperty, 0.82, 1, 460, 0.9);
                Spring(keyPress, ScaleTransform.ScaleYProperty, 0.82, 1, 460, 0.9);
            }
        }
        // Idioma: el globo gira sobre su propio eje, de oeste a este (arranca rápido y frena), y la etiqueta aparece con un pequeño rebote.
        if (meridians is not null && info.Title != lastLanguage)
        {
            lastLanguage = info.Title;
            BeginAnimation(GlobeAngleProperty, Win32.AnimationsEnabled() ? new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(950)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } } : null);
            if (chipPop is not null)
            {
                Spring(chipPop, ScaleTransform.ScaleXProperty, 0.5, 1, 520, 1.0);
                Spring(chipPop, ScaleTransform.ScaleYProperty, 0.5, 1, 520, 1.0);
            }
        }
    }

    private TextBlock? lyricBlock;
    private Grid? lyricHost;

    /// El ancho máximo del texto de la letra (lo que cabe en la pastilla más ancha): más allá se corta con puntos suspensivos; antes de eso el texto no se recoloca.
    public double MaxTextWidth { get; set; } = 600;


    /// El relleno de la barra se desliza hasta el valor nuevo (en vez de saltar): la primera vez se coloca directo y los cambios siguientes se animan.
    private void PlaceFill()
    {
        if (barGrid is null || fillBar is null || barTarget < 0) return;
        double full = barGrid.ActualWidth;
        if (full <= 0) return;   // aún sin medir: SizeChanged lo vuelve a llamar
        double width = full * barTarget;
        if (Math.Abs(width - barWidthShown) < 0.01) return;
        barWidthShown = width;
        if (!barPlaced || !Win32.AnimationsEnabled())
        {
            fillBar.BeginAnimation(WidthProperty, null);
            fillBar.Width = width;
            barPlaced = true;
            return;
        }
        fillBar.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// El sol sigue al brillo: los rayos se alargan y giran con él, con un pequeño rebote al llegar (la primera vez se coloca sin animar).
    private void AnimateSun(double brightness, bool animate)
    {
        if (sunScale is null || sunRotate is null) return;
        double scale = 0.7 + 0.42 * brightness, angle = 70 * brightness;
        if (!animate || !Win32.AnimationsEnabled())
        {
            sunScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            sunScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            sunRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            (sunScale.ScaleX, sunScale.ScaleY, sunRotate.Angle) = (scale, scale, angle);
            return;
        }
        var grow = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
        var span = TimeSpan.FromMilliseconds(420);
        sunScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, span) { EasingFunction = grow });
        sunScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, span) { EasingFunction = grow });
        sunRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// El giro del globo: el ángulo (en grados) con el que se dibujan sus meridianos. Se anima con un DoubleAnimation y cada paso redibuja los meridianos.
    public static readonly DependencyProperty GlobeAngleProperty = DependencyProperty.Register(nameof(GlobeAngle), typeof(double), typeof(NotchHudView),
        new PropertyMetadata(0.0, (d, _) => ((NotchHudView)d).DrawMeridians()));
    public double GlobeAngle { get => (double)GetValue(GlobeAngleProperty); set => SetValue(GlobeAngleProperty, value); }

    /// Dibuja los meridianos del globo como si girara sobre su propio eje, de oeste a este: cada meridiano es la curva x = R·cos(lat)·sen(long) y solo se ve
    /// mientras está en la cara de delante; entra por la izquierda, cruza el centro y se va por la derecha.
    private void DrawMeridians()
    {
        if (meridians is null) return;
        for (int i = 0; i < meridians.Length; i++)
        {
            double longitude = (GlobeAngle + (i - 1) * 60) * Math.PI / 180, front = Math.Cos(longitude);
            var path = meridians[i];
            if (front <= 0.02) { path.Opacity = 0; continue; }
            path.Opacity = Math.Min(1, front * 3);   // se apaga al llegar al borde, donde el meridiano se ve de canto
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (int step = 0; step <= 20; step++)
                {
                    double latitude = (-90 + step * 9) * Math.PI / 180;
                    var point = new Point(9 + 7 * Math.Cos(latitude) * Math.Sin(longitude), 9 + 7 * Math.Sin(latitude));
                    if (step == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, true);
                }
            }
            geometry.Freeze();
            path.Data = geometry;
        }
    }

    /// Anima `property` de `target` con un resorte: arranca en `from` y se asienta en `to` con un pequeño rebote. Sin animaciones, se pone directo.
    private static void Spring(DependencyObject target, DependencyProperty property, double from, double to, int ms, double amplitude)
    {
        var animatable = (IAnimatable)target;
        if (!Win32.AnimationsEnabled()) { animatable.BeginAnimation(property, null); target.SetValue(property, to); return; }
        animatable.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = amplitude } });
    }

    private static void Pulse(ScaleTransform scale, double from)
    {
        Spring(scale, ScaleTransform.ScaleXProperty, from, 1, 460, 0.7);
        Spring(scale, ScaleTransform.ScaleYProperty, from, 1, 460, 0.7);
    }

    /// Una onda (o la cruz) del altavoz: al aparecer crece desde el altavoz con un rebote y se enciende; al irse, se apaga.
    private static void Reveal(System.Windows.Shapes.Path part, bool on, bool animate)
    {
        if (part.Tag is bool current && current == on) return;
        part.Tag = on;
        var scale = (ScaleTransform)part.RenderTransform;
        if (!animate || !Win32.AnimationsEnabled())
        {
            part.BeginAnimation(OpacityProperty, null);
            part.Opacity = on ? 1 : 0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            (scale.ScaleX, scale.ScaleY) = (1, 1);
            return;
        }
        if (on)
        {
            Spring(scale, ScaleTransform.ScaleXProperty, 0.4, 1, 420, 1.0);
            Spring(scale, ScaleTransform.ScaleYProperty, 0.4, 1, 420, 1.0);
            part.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }
        else part.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)));
    }

    /// Fija el ancho del texto de la letra a su ancho natural: si no, se ajustaba al hueco de la pastilla mientras ésta crece o se recoge y se cortaba con «…» a
    /// medias. Con el ancho fijo se queda centrado y la pastilla lo descubre o lo tapa por los lados.
    private void FixWidth(TextBlock text)
    {
        text.Width = double.NaN;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        text.Width = Math.Min(Math.Ceiling(text.DesiredSize.Width) + 1, MaxTextWidth);
    }

    /// El cambio de línea, suave: la línea que se va sube un poco mientras se desvanece, y la nueva aparece ya en su sitio, sin moverse, con un fundido que empieza
    /// cuando la vieja ya va apagándose (así las dos nunca se leen a la vez ni hay saltos de posición).
    private void Roll(string oldLine, double oldWidth)
    {
        if (lyricBlock is not { } now) return;
        now.RenderTransform = Transform.Identity;
        // Opacidad con fotogramas clave: 0 desde el primer instante, quieta 120 ms (la vieja ya se va apagando) y luego sube. Con un BeginTime, mientras esperaba la animación
        // anterior (que se queda en 1) seguía mandando y la línea nueva se veía entera encima de la vieja.
        var fadeIn = new DoubleAnimationUsingKeyFrames();
        fadeIn.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fadeIn.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fadeIn.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(540)), new KeySpline(0.4, 0, 0.2, 1)));
        now.BeginAnimation(OpacityProperty, fadeIn);

        var ghost = new TextBlock
        {
            Text = oldLine, Foreground = now.Foreground, FontSize = now.FontSize, FontWeight = now.FontWeight, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, IsHitTestVisible = false, MaxWidth = MaxTextWidth, HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (!double.IsNaN(oldWidth)) ghost.Width = oldWidth;
        var leave = new TranslateTransform();
        ghost.RenderTransform = leave;
        leave.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -9, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var host = lyricHost;
        fade.Completed += (_, _) => host?.Children.Remove(ghost);
        host?.Children.Add(ghost);
        ghost.BeginAnimation(OpacityProperty, fade);
    }

    private void Build(HudInfo info)
    {
        (lyricBlock, lyricHost) = (null, null);
        body.Children.Clear();
        (barGrid, fillBar, barTarget, barWidthShown, barPlaced) = (null, null, -1, -1, false);
        (sunRays, sunScale, sunRotate, sunFraction) = (null, null, null, -1);
        (stateText, btSub, btRing) = (null, null, null);
        (volWave1, volWave2, volCross, capsArrow, numOne, capsFill) = (null, null, null, null, null, null);
        (volPulse, keyPress, chipPop, capsShift, stateShift, meridians) = (null, null, null, null, null, null);
        (volLevel, lockOn, lastLanguage) = (-1, null, "");
        if (info.Kind == HudKind.Bluetooth) { BuildBluetooth(info); return; }
        body.ColumnDefinitions[0].Width = new GridLength(18 + 14);
        var icon = BuildIcon(info);
        SetColumn(icon, 0);
        body.Children.Add(icon);
        if (info.HasBar)
        {
            barGrid = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 6, Margin = new Thickness(0, 0, 4, 0) };
            barGrid.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(3), SnapsToDevicePixels = true });
            fillBar = new Border { Background = White, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            barGrid.Children.Add(fillBar);
            barGrid.SizeChanged += (_, _) => { barWidthShown = -1; PlaceFill(); };
            SetColumn(barGrid, 1);
            SetColumnSpan(barGrid, 2);
            body.Children.Add(barGrid);
            return;
        }
        bool lyric = info.Kind == HudKind.Lyric;
        var title = new TextBlock
        {
            Text = info.Title, Foreground = lyric ? new SolidColorBrush(info.Dim ? LyricDim : Colors.White) : White, FontSize = 13, FontWeight = lyric ? FontWeights.SemiBold : FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = lyric ? TextAlignment.Center : TextAlignment.Left,
        };
        if (lyric)
        {
            // Como en Nusa: la nota a la izquierda y un hueco igual a la derecha (la línea queda centrada) y el fondo negro que se tiñe hacia abajo con el tono de la portada.
            body.ColumnDefinitions[2].Width = new GridLength(32);
        }
        else body.ColumnDefinitions[2].Width = GridLength.Auto;
        if (lyric)
        {
            // El texto no se recoloca mientras la pastilla cambia de largo (ni se corta con «…» a medias): se queda centrado a su ancho natural y la pastilla lo va
            // descubriendo (o tapando) desde los lados, sin difuminar los bordes.
            title.MaxWidth = MaxTextWidth;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            FixWidth(title);
            lyricHost = new Grid { ClipToBounds = true, Children = { title } };
            SetColumn(lyricHost, 1);
            body.Children.Add(lyricHost);
            lyricBlock = title;
        }
        else
        {
            SetColumn(title, 1);
            body.Children.Add(title);
        }
        if (info.Chip is { } chip)
        {
            var label = new TextBlock { Text = chip, Foreground = White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            var tag = new Border { Background = Chip, CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), Child = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = chipPop = new ScaleTransform(1, 1) };
            SetColumn(tag, 2);
            body.Children.Add(tag);
        }
        else
        {
            stateText = new TextBlock { FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), RenderTransform = stateShift = new TranslateTransform() };
            SetColumn(stateText, 2);
            body.Children.Add(stateText);
        }
    }

    private static readonly Brush Blue = Frozen(Color.FromRgb(0x0A, 0x9B, 0xFF));

    /// Como en Nusa: a la izquierda el símbolo Bluetooth en un círculo cuyo aro se llena con la batería, en medio el nombre y «Conectado · 50 %», a la derecha el
    /// aparato (auriculares, ratón…) con un aro azul mientras está conectado.
    private void BuildBluetooth(HudInfo info)
    {
        bool connected = info.On == true;
        body.ColumnDefinitions[0].Width = new GridLength(34 + 12);
        body.ColumnDefinitions[2].Width = GridLength.Auto;
        var badge = new Grid { Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(new Ellipse { Fill = Chip });
        btRing = new Grid();
        badge.Children.Add(btRing);
        var rune = Line("M5.3,5.6 L12.8,12.4 L9,15.8 V2.3 L12.8,5.6 L5.3,12.4");
        badge.Children.Add(new Canvas { Width = 18, Height = 18, Children = { rune }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        SetColumn(badge, 0);
        body.Children.Add(badge);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = info.Title, Foreground = White, FontSize = 15, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(btSub = new TextBlock { Text = info.Sub ?? "", Foreground = Gray, FontSize = 12.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        SetColumn(texts, 1);
        body.Children.Add(texts);

        // El aparato: aro azul con halo mientras está conectado; al desconectarse el icono pasa de blanco a gris sin aro.
        var grayColor = Color.FromRgb(0x8E, 0x8E, 0x93);
        var ink = new SolidColorBrush(connected ? Colors.White : grayColor);
        var device = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = connected ? Blue : Brushes.Transparent, BorderThickness = new Thickness(2.5), Background = connected ? Frozen(Color.FromArgb(0x26, 0x0A, 0x9B, 0xFF)) : Brushes.Transparent,
            Effect = connected ? new DropShadowEffect { Color = Color.FromRgb(0x0A, 0x9B, 0xFF), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.6 } : null, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = devicePop = new ScaleTransform(1, 1),
            Child = new DeviceIcon { Kind = info.Device, Ink = ink, Width = 19, Height = 19, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        // El latido: un aro que sale del aparato y se apaga, dos veces, al conectarse.
        var pulse = new Ellipse { Width = 34, Height = 34, Stroke = Blue, StrokeThickness = 1.5, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = pulseScale = new ScaleTransform(1, 1) };
        var deviceHost = new Grid { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { pulse, device } };
        SetColumn(deviceHost, 2);
        body.Children.Add(deviceHost);

        badge.RenderTransformOrigin = new Point(0.5, 0.5);
        badge.RenderTransform = badgePop = new ScaleTransform(1, 1);
        foreach (var pop in new[] { badgePop, devicePop })
        {
            Spring(pop, ScaleTransform.ScaleXProperty, 0.55, 1, 900, 0.9);
            Spring(pop, ScaleTransform.ScaleYProperty, 0.55, 1, 900, 0.9);
        }
        texts.RenderTransform = textShift = new TranslateTransform();
        Spring(textShift, TranslateTransform.XProperty, -10, 0, 800, 0.5);
        if (Win32.AnimationsEnabled())
        {
            texts.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
            if (connected)
            {
                var repeat = new RepeatBehavior(2);
                var span = TimeSpan.FromMilliseconds(1300);
                var delay = TimeSpan.FromMilliseconds(250);
                pulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.7, span) { RepeatBehavior = repeat, BeginTime = delay });
                pulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.7, span) { RepeatBehavior = repeat, BeginTime = delay });
                pulse.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, span) { RepeatBehavior = repeat, BeginTime = delay });
            }
            else ink.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.White, grayColor, TimeSpan.FromMilliseconds(900)));
        }
        UpdateBluetooth(info);
    }

    /// Lo que cambia sin reconstruir la tarjeta (llega la batería): el texto y el aro, que se funde al aparecer.
    private void UpdateBluetooth(HudInfo info)
    {
        if (btSub is null || btRing is null) return;
        btSub.Text = info.Sub ?? "";
        btRing.Children.Clear();
        if (info.On != true || info.Battery is not { } battery) return;
        double fraction = Math.Clamp(battery, 0.01, 0.9999), angle = fraction * 2 * Math.PI, r = 15.5;
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(new Point(17, 17 - r), false, false);
            context.ArcTo(new Point(17 + r * Math.Sin(angle), 17 - r * Math.Cos(angle)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        var ring = new Grid { Width = 34, Height = 34 };
        ring.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = 3, Margin = new Thickness(1.5) });
        ring.Children.Add(new System.Windows.Shapes.Path { Data = arc, Stroke = Blue, StrokeThickness = 3, Effect = new DropShadowEffect { Color = Color.FromRgb(0x0A, 0x9B, 0xFF), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 }, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        btRing.Children.Add(ring);
        if (Win32.AnimationsEnabled()) ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
    }

    private static System.Windows.Shapes.Path Line(string data, double thickness = 1.7, Brush? fill = null) => new()
    {
        Data = Geometry.Parse(data), Stroke = White, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, Fill = fill ?? Brushes.Transparent,
    };

    /// Una onda del altavoz: nace en el altavoz (su origen está a la izquierda) y empieza apagada.
    private static System.Windows.Shapes.Path Wave(string data)
    {
        var wave = Line(data, 1.7);
        (wave.RenderTransformOrigin, wave.RenderTransform, wave.Opacity) = (new Point(0, 0.5), new ScaleTransform(1, 1), 0);
        return wave;
    }

    /// Iconos de 18 × 18 hechos a mano, gruesos y blancos como los de la pastilla.
    private FrameworkElement BuildIcon(HudInfo info)
    {
        var canvas = new Canvas { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        switch (info.Kind)
        {
            case HudKind.Brightness:
                canvas.Children.Add(Line("M9,5.6 A3.4,3.4 0 1 1 9,12.4 A3.4,3.4 0 1 1 9,5.6 Z"));
                // Los rayos van en su propio grupo, para alargarlos y girarlos con el brillo.
                sunRays = new Canvas { Width = 18, Height = 18, RenderTransformOrigin = new Point(0.5, 0.5) };
                (sunScale, sunRotate) = (new ScaleTransform(1, 1), new RotateTransform(0));
                sunRays.RenderTransform = new TransformGroup { Children = { sunScale, sunRotate } };
                foreach (var (x1, y1, x2, y2) in new[] { (9.0, 1.2, 9.0, 2.8), (9.0, 15.2, 9.0, 16.8), (1.2, 9.0, 2.8, 9.0), (15.2, 9.0, 16.8, 9.0),
                    (3.5, 3.5, 4.6, 4.6), (13.4, 13.4, 14.5, 14.5), (3.5, 14.5, 4.6, 13.4), (13.4, 4.6, 14.5, 3.5) })
                    sunRays.Children.Add(Line($"M{x1},{y1} L{x2},{y2}", 1.6));
                canvas.Children.Add(sunRays);
                break;
            case HudKind.Volume:
                {
                    var speaker = Line("M1.8,6.8 H5 L9.4,3.2 V14.8 L5,11.2 H1.8 Z", 1.4, White);
                    (speaker.RenderTransformOrigin, speaker.RenderTransform) = (new Point(0.5, 0.5), volPulse = new ScaleTransform(1, 1));
                    canvas.Children.Add(speaker);
                    // Las ondas y la cruz salen siempre; el nivel decide cuáles se ven (y las anima).
                    volWave1 = Wave("M11.8,6.6 A3.6,3.6 0 0 1 11.8,11.4");
                    volWave2 = Wave("M14.2,4.3 A6.9,6.9 0 0 1 14.2,13.7");
                    volCross = Wave("M12,6.6 L16.6,11.4 M16.6,6.6 L12,11.4");
                    foreach (var part in new[] { volWave1, volWave2, volCross }) canvas.Children.Add(part);
                    break;
                }
            case HudKind.CapsLock:
                capsArrow = Line("M9,2.2 L15.8,9.4 H11.7 V15.6 H6.3 V9.4 H2.2 Z", 1.7, capsFill = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)));
                capsArrow.RenderTransform = capsShift = new TranslateTransform();
                canvas.Children.Add(capsArrow);
                break;
            case HudKind.NumLock:
                (canvas.RenderTransformOrigin, canvas.RenderTransform) = (new Point(0.5, 0.5), keyPress = new ScaleTransform(1, 1));
                canvas.Children.Add(Line("M3.4,2.4 H14.6 A1.4,1.4 0 0 1 16,3.8 V14.2 A1.4,1.4 0 0 1 14.6,15.6 H3.4 A1.4,1.4 0 0 1 2,14.2 V3.8 A1.4,1.4 0 0 1 3.4,2.4 Z", 1.6));
                numOne = Line("M7.4,6.2 L9.4,5.2 V12.8", 1.7);
                numOne.Opacity = 0.3;
                canvas.Children.Add(numOne);
                break;
            case HudKind.Lyric:
                {
                    var note = Line("M6.6,13 V3.6 L14,2.2 V11.4", 1.6);
                    note.Stroke = Gray;   // la nota, gris y apagada: lo que destaca es la letra
                    var heads = Line("M6.6,13 A2.3,2.3 0 1 1 4.3,10.7 A2.3,2.3 0 0 1 6.6,13 Z M14,11.4 A2.3,2.3 0 1 1 11.7,9.1 A2.3,2.3 0 0 1 14,11.4 Z", 1.6);
                    heads.Stroke = note.Stroke;
                    canvas.Children.Add(note);
                    canvas.Children.Add(heads);
                    break;
                }
            case HudKind.Language:
                canvas.Children.Add(Line("M9,2 A7,7 0 1 1 9,16 A7,7 0 1 1 9,2 Z", 1.5));
                canvas.Children.Add(Line("M2.2,9 H15.8", 1.4));
                meridians = new System.Windows.Shapes.Path[4];
                for (int i = 0; i < meridians.Length; i++) canvas.Children.Add(meridians[i] = Line("M0,0", 1.4));
                BeginAnimation(GlobeAngleProperty, null);
                SetCurrentValue(GlobeAngleProperty, 0.0);
                DrawMeridians();
                break;
        }
        return canvas;
    }
}
