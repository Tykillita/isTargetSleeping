using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace IsTargetSleeping.UI;

/// Glassmorphism negro: el panel es vidrio ahumado sobre el acrílico de Windows
/// 11 (desenfoca de verdad lo que hay detrás), con tarjetas de vidrio claro
/// translúcido, bordes de luz y texto blanco. La interfaz es monocroma; el azul
/// característico marca el estado encendido y el punto de la bandeja;
/// ámbar es lo que está cambiando y coral, lo crítico.
public sealed record Theme(
    Color Bg, Color Card, Color Line, Color Fg, Color Secondary, Color Muted,
    Color Accent, Color AccentSoft, Color OnAccent, Color On, Color OnSoft,
    Color Warm, Color WarmSoft, Color Danger, Color Track,
    Color Button, Color ButtonHover, Color Popup)
{
    /// El azul característico: estado encendido y punto de la bandeja.
    public static readonly Color Blue = Hex(0x4DA3FF);

    public static readonly Theme Glass = new(
        Bg: Argb(0xC7, 0x050506),          // tinte negro (78 %) sobre el acrílico: casi no se cuela color de detrás
        Card: Argb(0x0F, 0xFFFFFF),        // vidrio claro de las tarjetas
        Line: Argb(0x1A, 0xFFFFFF),
        Fg: Hex(0xF5F5F7),
        Secondary: Argb(0xA6, 0xFFFFFF),
        Muted: Argb(0x6B, 0xFFFFFF),
        Accent: Hex(0xF5F5F7),             // blanco: selección, interruptores, barra del modelo
        AccentSoft: Argb(0x1F, 0xFFFFFF),
        OnAccent: Hex(0x0A0A0B),
        On: Blue,                          // estado encendido
        OnSoft: WithOpacity(Blue, 0.18),
        Warm: Hex(0xF5BD57),
        WarmSoft: Argb(0x24, 0xF5BD57),
        Danger: Hex(0xFF7A70),
        Track: Argb(0x14, 0xFFFFFF),
        Button: Argb(0x1F, 0xFFFFFF),
        ButtonHover: Argb(0x33, 0xFFFFFF),
        Popup: Argb(0xF2, 0x121214));      // menús y tooltips (sin acrílico de DWM)

    /// Alto contraste de Windows: los colores del sistema, sin vidrio ni transparencias.
    public static Theme HighContrast => ForceHighContrast
        ? new(
            Bg: Colors.Black, Card: Colors.Black, Line: Colors.White,
            Fg: Colors.White, Secondary: Colors.White, Muted: Colors.White,
            Accent: Colors.Yellow, AccentSoft: Colors.Black, OnAccent: Colors.Black,
            On: Colors.Cyan, OnSoft: Colors.Black,
            Warm: Colors.Yellow, WarmSoft: Colors.Black, Danger: Colors.Red,
            Track: Colors.Black, Button: Colors.Black, ButtonHover: Colors.Black, Popup: Colors.Black)
        : new(
            Bg: SystemColors.WindowColor, Card: SystemColors.WindowColor, Line: SystemColors.WindowTextColor,
            Fg: SystemColors.WindowTextColor, Secondary: SystemColors.WindowTextColor, Muted: SystemColors.WindowTextColor,   // «Muted» es texto secundario, no deshabilitado: en alto contraste se lee igual que el principal
            Accent: SystemColors.HighlightColor, AccentSoft: SystemColors.WindowColor, OnAccent: SystemColors.HighlightTextColor,
            On: SystemColors.HotTrackColor, OnSoft: SystemColors.WindowColor,
            Warm: SystemColors.WindowTextColor, WarmSoft: SystemColors.WindowColor, Danger: SystemColors.WindowTextColor,
            Track: SystemColors.WindowColor, Button: SystemColors.ControlColor, ButtonHover: SystemColors.ControlColor,
            Popup: SystemColors.WindowColor);

    /// Claro con contornos: fondo blanco, texto negro y un contorno fino negro en cada superficie, sin vidrio ni transparencias. Se elige en Ajustes;
    /// el alto contraste de Windows, si está activo, manda sobre él.
    public static readonly Theme Outline = new(
        Bg: Hex(0xFFFFFF), Card: Hex(0xFFFFFF), Line: Hex(0x000000),
        Fg: Hex(0x000000), Secondary: Hex(0x2B2B2B), Muted: Hex(0x555555),
        Accent: Hex(0x000000), AccentSoft: Hex(0xEDEDED), OnAccent: Hex(0xFFFFFF),
        On: Hex(0x0A58CA), OnSoft: Hex(0xE3ECFB),
        Warm: Hex(0x8A5300), WarmSoft: Hex(0xFFF1DB), Danger: Hex(0xB3261E),
        Track: Hex(0xE2E2E2), Button: Hex(0xF2F2F2), ButtonHover: Hex(0xDEDEDE), Popup: Hex(0xFFFFFF));

    /// Solo para las capturas y pruebas: pinta el tema de alto contraste sin cambiar el de Windows.
    public static bool ForceHighContrast;
    /// El estilo «claro con contornos» elegido en Ajustes (lo fija la app al arrancar y al cambiarlo).
    public static bool OutlineStyle;
    private static bool IsHighContrast => ForceHighContrast || SystemParameters.HighContrast;
    /// Superficies lisas, con contorno y sin vidrio: el alto contraste de Windows o el estilo «claro con contornos». Lo usan los controles que dibujan a mano.
    public static bool Flat => IsHighContrast || OutlineStyle;
    public static Theme Current => IsHighContrast ? HighContrast : OutlineStyle ? Outline : Glass;
    public static bool IsDark => true;
    public static event Action? Changed;

    /// El acrílico de DWM llegó con Windows 11 22H2 (compilación 22621). En
    /// Windows 10 el panel usa el mismo vidrio negro, pero opaco.
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;

    public static Color Hex(uint rgb, double opacity = 1) =>
        Color.FromArgb((byte)Math.Round(opacity * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Color Argb(byte alpha, uint rgb) => Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithOpacity(Color c, double opacity) => Color.FromArgb((byte)Math.Round(opacity * c.A), c.R, c.G, c.B);

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Gradient(Color from, Color to, double angle = 90)
    {
        var b = new LinearGradientBrush(from, to, angle);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Gradient(params (Color Color, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (color, at) in stops) b.GradientStops.Add(new GradientStop(color, at));
        b.Freeze();
        return b;
    }

    /// La barra de tareas tiene su propio modo («Modo de Windows»): el ícono de la
    /// bandeja lo sigue aunque el panel sea siempre negro.
    public static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    private static bool watchingContrast;

    /// Publica los colores como recursos: la interfaz los usa con DynamicResource.
    public static void Apply(ResourceDictionary? resources = null)
    {
        var r = resources ?? Application.Current.Resources;
        var t = Current;
        foreach (var (name, color) in new[]
        {
            ("Bg", t.Bg), ("Card", t.Card), ("Line", t.Line), ("Fg", t.Fg), ("Secondary", t.Secondary),
            ("Muted", t.Muted), ("Accent", t.Accent), ("AccentSoft", t.AccentSoft), ("OnAccent", t.OnAccent),
            ("On", t.On), ("OnSoft", t.OnSoft),("Warm", t.Warm), ("WarmSoft", t.WarmSoft),
            ("Danger", t.Danger), ("Track", t.Track), ("ButtonBg", t.Button), ("ButtonHover", t.ButtonHover),
            ("PopupBg", t.Popup),
        })
        {
            r[name] = Brush(color);
            r[name + "Color"] = color;
        }
        r["MutedHalf"] = Brush(WithOpacity(t.Muted, 0.55));
        // Texto de apoyo atenuado (sin atenuar en alto contraste: baja el contraste bajo el mínimo, sobre todo en la fila seleccionada).
        r["SubtleOpacity"] = Flat ? 1.0 : 0.7;
        // En alto contraste el velo coral baja el contraste del texto de «Denegar» bajo el mínimo: ahí el fondo es el liso del tema.
        r["DangerSoft"] = Brush(IsHighContrast ? t.WarmSoft : WithOpacity(t.Danger, 0.16));
        // Las marcas de los agentes (Claude Code, Codex): su color de siempre; en alto contraste o con contornos, el del texto.
        r["BrandClaude"] = Brush(Flat ? t.Fg : Hex(0xD97757));
        r["BrandCodex"] = Brush(Flat ? t.Fg : Hex(0x10A37F));
        r["OnBrand"] = Brush(Flat ? t.Bg : Colors.White);
        // El amarillo de las Notas de iOS: la banda de la cabecera del widget, su texto y el tinte de la nota elegida; liso en alto contraste y con contornos.
        r["NoteBand"] = Flat ? Brush(t.Fg) : Gradient(Hex(0xFBDD4F), Hex(0xF2C23A));
        r["OnNote"] = Brush(Flat ? t.Bg : Hex(0x2B250E));
        r["NoteAccent"] = Brush(Flat ? t.Fg : Hex(0xF5C531));
        // Tareas (TodoList): los colores medidos en la grabación de Nusa; en alto contraste, los del tema.
        Color Todo(uint rgb, Color flat) => Flat ? flat : Hex(rgb);
        r["TodoGreen"] = Brush(Todo(0x3EB753, t.Fg));
        r["TodoOnGreen"] = Brush(Todo(0x121212, t.Bg));
        r["TodoGreenSoft"] = Brush(Todo(0x0E2915, t.Track));
        r["TodoBlue"] = Brush(Todo(0x364CF5, t.Fg));
        r["TodoRed"] = Brush(Todo(0xE43732, t.Danger));
        r["TodoCard"] = Brush(Todo(0x121212, t.Card));
        r["TodoPill"] = Brush(Todo(0x191919, t.Button));
        r["TodoPillOff"] = Brush(Todo(0x090909, t.Track));
        r["TodoCal"] = Brush(Todo(0x1E1E1E, t.Button));
        r["TodoField"] = Brush(Todo(0x080808, t.Bg));
        r["TodoChip"] = Brush(Todo(0x333235, t.ButtonHover));
        r["TodoCancel"] = Brush(Todo(0x212025, t.Button));
        r["TodoRow"] = Brush(Todo(0x181818, t.Track));
        r["TodoRowLine"] = Brush(Todo(0x383838, t.Line));
        r["TodoLine"] = Brush(Todo(0x212023, t.Line));
        r["TodoGlow"] = Brush(Todo(0x2C2C2C, t.Bg));
        r["TodoFill"] = Brush(Todo(0x212023, t.Bg));
        r["TodoGlyph"] = Brush(Todo(0x121212, t.Fg));
        r["TodoRim"] = Brush(Todo(0xD8D8D8, t.Fg));
        r["TodoBar"] = Brush(Todo(0xDCDCDC, t.Fg));
        r["TodoGlyphBox"] = Brush(Todo(0x292929, t.Track));
        r["TodoClear"] = Brush(Todo(0xB6B6B6, t.Secondary));
        r["TodoWhite"] = Brush(Todo(0xFFFFFF, t.Fg));
        r["TodoText"] = Brush(Todo(0xDDDDDD, t.Fg));
        r["TodoChipText"] = Brush(Todo(0xD6D5D8, t.Fg));
        r["TodoDim"] = Brush(Todo(0x9E9E9E, t.Secondary));
        r["TodoEmpty"] = Brush(Todo(0x757575, t.Secondary));
        r["TodoHint"] = Brush(Todo(0x5E5E5E, t.Muted));
        r["TodoOff"] = Brush(Todo(0x757575, t.Muted));
        r["NoteSoft"] = Brush(Flat ? t.Track : WithOpacity(Hex(0xF5C531), 0.16));
        // Estante: los grises, el azul de la selección y el rojo de «Vaciar todo» medidos en el de Nusa; en alto contraste, los del tema.
        r["ShelfChip"] = Brush(Flat ? t.Track : Hex(0x181818));
        r["ShelfChipText"] = Brush(Flat ? t.Fg : Colors.White);
        r["ShelfDoneChip"] = Brush(Flat ? t.Track : Hex(0x131C2E));
        r["ShelfDoneText"] = Brush(Flat ? t.Fg : Hex(0x6B9DEB));
        r["ShelfDangerChip"] = Brush(Flat ? t.Track : Hex(0x26100D));
        r["ShelfDangerText"] = Brush(Flat ? t.Fg : Hex(0xE0645C));
        r["ShelfSelectedFill"] = Brush(Flat ? t.Track : Hex(0x1C2947));
        r["ShelfSelectedEdge"] = Brush(Flat ? t.Fg : Hex(0x4F7BD0));
        r["ShelfCheck"] = Brush(Flat ? t.Fg : Hex(0x3B82F6));
        r["ShelfHover"] = Brush(Flat ? t.Track : Hex(0x161616));
        r["ShelfX"] = Brush(Flat ? t.Fg : Hex(0x4C4A50));
        r["ShelfXText"] = Brush(Flat ? t.Bg : Colors.White);
        r["ShelfZone"] = Brush(Flat ? t.Bg : Hex(0x080808));
        r["ShelfDash"] = Brush(Flat ? t.Fg : Hex(0x555555));
        r["ShelfDropStroke"] = Brush(Flat ? t.Fg : Hex(0xF2F2F2));
        r["ShelfDropText"] = Brush(Flat ? t.Fg : Colors.White);
        r["ShelfArrow"] = Brush(Flat ? t.Fg : Hex(0x7A7A7A));   // la flecha del icono en reposo (blanca al arrastrar encima), medida en Nusa
        r["ShelfTray"] = Brush(Flat ? t.Fg : Hex(0xE2E4E6));   // la bandeja del icono, gris muy claro (la flecha de encima, blanca), medido en Nusa
        if (Flat)
        {
            r["ShelfAuraHorizontal"] = Brush(Colors.Transparent);
            r["ShelfAuraVertical"] = Brush(Colors.Transparent);
            r["ShelfDropActive"] = Brush(t.Bg);
        }
        else
        {
            var auraHorizontal = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
            auraHorizontal.GradientStops.Add(new(Argb(0x50, 0x12505C), 0));
            auraHorizontal.GradientStops.Add(new(Colors.Transparent, 0.5));
            auraHorizontal.GradientStops.Add(new(Argb(0x68, 0x3D2C8C), 1));
            auraHorizontal.Freeze();
            r["ShelfAuraHorizontal"] = auraHorizontal;

            var auraVertical = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            auraVertical.GradientStops.Add(new(Colors.Transparent, 0));
            auraVertical.GradientStops.Add(new(Argb(0x12, 0x1A2E5E), 0.16));
            auraVertical.GradientStops.Add(new(Argb(0x28, 0x1A2E5E), 0.55));
            auraVertical.GradientStops.Add(new(Argb(0x38, 0x2E4F8E), 1));
            auraVertical.Freeze();
            r["ShelfAuraVertical"] = auraVertical;

            var drop = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            drop.GradientStops.Add(new(Argb(0x20, 0x14274A), 0));
            drop.GradientStops.Add(new(Argb(0x50, 0x294575), 0.5));
            drop.GradientStops.Add(new(Argb(0x78, 0x3A56A0), 1));
            drop.Freeze();
            r["ShelfDropActive"] = drop;
        }
        r["ShelfSize"] = Brush(Flat ? t.Secondary : Hex(0x818181));
        r["ShelfHint"] = Brush(Flat ? t.Secondary : Hex(0x747474));

        // Vidrio negro: el tinte del panel (opaco si no hay acrílico), tarjetas de
        // vidrio neutro con un canto fino, y ninguna luz de color: la profundidad la
        // da una sombra negra que cae hacia abajo, no un brillo.
        bool contrast = Flat;
        r["PanelTint"] = contrast ? Brush(t.Bg) : BackdropSupported ? Brush(t.Bg) : Brush(Hex(0x0A0A0B));
        // Liquid glass: las tarjetas y los botones redondos son cristal (más luz arriba, casi claro abajo) con un canto de luz fino.
        r["CardFill"] = contrast ? Brush(t.Card) : Gradient(Argb(0x17, 0xFFFFFF), Argb(0x07, 0xFFFFFF));
        r["CardStroke"] = contrast ? Brush(t.Line) : Gradient(Argb(0x3A, 0xFFFFFF), Argb(0x0C, 0xFFFFFF), 70);
        r["WidgetFoldersCardFill"] = contrast ? Brush(t.Card) : Gradient(Argb(0x50, 0x08090B), Argb(0x38, 0x08090B));
        Brush galleryTileFill = contrast ? Brush(t.Card) : Gradient(Hex(0x0A0A0B), Hex(0x060607));
        r["GalleryTileFill"] = galleryTileFill;
        r["GallerySideTileFill"] = galleryTileFill;
        r["GalleryShadowOpacity"] = contrast ? 0.0 : 0.28;
        r["GalleryCardShadowOpacity"] = contrast ? 0.0 : 0.12;
        r["GallerySideOpacity"] = 1.0;
        r["GallerySideRimOpacity"] = contrast ? 1.0 : 0.48;
        r["GallerySideRimBlur"] = contrast ? 0.0 : 3.0;
        r["GlassFill"] = contrast ? Brush(t.Button) : Gradient(Argb(0x3A, 0xFFFFFF), Argb(0x12, 0xFFFFFF));
        // Las carpetas de Widgets: cristal carbón, con un reflejo mínimo que no rompe el fondo oscuro.
        r["FolderFill"] = contrast ? Brush(t.Card) : Gradient(Argb(0x70, 0xF2F4F7), Argb(0x36, 0xCAD2DE));
        // La lente del dock: tinte blanco tenue, filo más luminoso por arriba y un destello corto en la cara.
        r["FolderTileFill"] = contrast ? Brush(t.Card) : Gradient(Argb(0x2C, 0xFFFFFF), Argb(0x0A, 0xFFFFFF));
        r["FolderRim"] = contrast ? Brush(t.Line) : Gradient(
            (Argb(0xD0, 0xFFFFFF), 0), (Argb(0x38, 0xFFFFFF), 0.5), (Argb(0x5A, 0xFFFFFF), 1));
        r["FolderSheen"] = contrast ? Brush(Colors.Transparent) : Gradient(
            (Argb(0x18, 0xFFFFFF), 0), (Argb(0x05, 0xFFFFFF), 0.24), (Argb(0x00, 0xFFFFFF), 0.64), (Argb(0x10, 0xFFFFFF), 1));
        r["FolderSpecular"] = contrast ? Brush(Colors.Transparent) : Gradient(
            (Argb(0xA0, 0xFFFFFF), 0), (Argb(0x00, 0xFFFFFF), 1));
        // Cristal abierto carbón: deja pasar el fondo, con reflejos neutros y menos luz que los mosaicos.
        r["FolderOpenFill"] = contrast ? Brush(t.Card) : Gradient(
            (Argb(0x72, 0x242528), 0), (Argb(0x7C, 0x1B1C1F), 0.14), (Argb(0x82, 0x141517), 0.52),
            (Argb(0x7A, 0x191A1D), 0.88), (Argb(0x72, 0x252629), 1));
        r["FolderOpenRim"] = contrast ? Brush(t.Line) : Gradient(
            (Argb(0x78, 0xFFFFFF), 0), (Argb(0x18, 0xFFFFFF), 0.5), (Argb(0x32, 0xFFFFFF), 1));
        r["FolderOpenSheen"] = contrast ? Brush(Colors.Transparent) : Gradient(
            (Argb(0x14, 0xFFFFFF), 0), (Argb(0x04, 0xFFFFFF), 0.12), (Argb(0x00, 0xFFFFFF), 0.44),
            (Argb(0x03, 0xFFFFFF), 0.78), (Argb(0x0B, 0xFFFFFF), 1));
        r["FolderOpenShadowOpacity"] = contrast ? 0.0 : 0.09;
        r["FolderScrim"] = contrast ? Brush(t.Bg) : Brush(Argb(0x30, 0x000000));
        r["FolderBackdropBlur"] = contrast ? 0.0 : 20.0;
        r["FolderIconSurface"] = contrast ? Brush(t.Card) : Brush(Colors.Black);
        r["FolderActive"] = Brush(t.Accent);
        r["FolderShadowOpacity"] = contrast ? 0.0 : 0.3;
        r["GlassRim"] = contrast ? Brush(t.Line) : Gradient(Argb(0x66, 0xFFFFFF), Argb(0x1C, 0xFFFFFF));
        // Caja de la tarea del equipo: un pozo de cristal oscuro (más hondo arriba, que se aclara abajo) con un destello fino en el borde
        // superior y otro, más tenue, en el inferior: da relieve sin sombras pesadas. En alto contraste, la superficie lisa del sistema.
        r["PromptWell"] = contrast ? Brush(t.Card) : Gradient(Argb(0x70, 0x000000), Argb(0x2E, 0x000000));
        r["PromptRim"] = contrast ? Brush(t.Line) : Gradient(Argb(0x6E, 0xFFFFFF), Argb(0x16, 0xFFFFFF));
        r["PromptSheen"] = contrast ? Brush(Colors.Transparent) : new LinearGradientBrush(
            new GradientStopCollection { new(Argb(0x00, 0xFFFFFF), 0), new(Argb(0x66, 0xFFFFFF), 0.5), new(Argb(0x00, 0xFFFFFF), 1) }, 0);
        r["Sheen"] = contrast ? Brush(Colors.Transparent) : Gradient(Argb(0x00, 0x000000), Argb(0x59, 0x000000));
        if (!watchingContrast)
        {
            watchingContrast = true;
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast)) Application.Current?.Dispatcher.Invoke(() => Apply());
            };
        }
        Changed?.Invoke();
    }

}
