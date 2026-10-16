using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Pets;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        int failures = 0;
        void Check(string label, bool success)
        {
            Console.WriteLine($"{(success ? "✓" : "✗")} {label}");
            if (!success) failures++;
        }
        var app = new App();
        app.InitializeComponent();
        Theme.Apply();
        string? originalSettings = File.Exists(Paths.SettingsFile) ? File.ReadAllText(Paths.SettingsFile) : null;
        foreach (var language in new[] { Language.Es, Language.En })
        {
            L10n.Init(language);
            var prefs = new Prefs();
            prefs.LoadDemo();
            prefs.SetSwitch(PrefKeys.TaskbarPet, true);
            var ollama = new OllamaController();
            ollama.LoadDemo();
            var supervisor = new Supervisor(ollama, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor, PanelView.Settings);
            var view = new ContentView(model);
            view.Measure(new Size(340, double.PositiveInfinity));
            view.Arrange(new Rect(view.DesiredSize));
            view.UpdateLayout();
            var buttons = Descendants(view).OfType<RadioButton>().Where(b => b.DataContext is PetChoice).ToArray();
            Check($"{language}: cuatro miniaturas y selección única", buttons.Length == 4 && buttons.Count(b => b.IsChecked == true) == 1);
            Check($"{language}: miniaturas estáticas reutilizadas", ReferenceEquals(PetPreview.For(PetCatalog.All[0]), model.PetChoices[0].Preview));
            string[] expected = language == Language.Es ? ["Mira", "Llama", "Capibara", "Gatito naranja"] : ["Mira", "Llama", "Capybara", "Orange kitten"];
            Check($"{language}: nombres traducidos", model.PetChoices.Select(p => p.Title).SequenceEqual(expected));
            var choices = model.PetChoices;
            for (int i = 0; i < buttons.Length; i++)
            {
                var peer = new RadioButtonAutomationPeer(buttons[i]);
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                view.UpdateLayout();
                Check($"{language}/{PetCatalog.All[i].Id}: selección accesible guarda la elección", PetCatalog.Find(prefs.PetSpecies).Id == PetCatalog.All[i].Id
                    && model.PetChoices.Count(p => p.IsSelected) == 1 && model.PetChoices[i].IsSelected
                    && buttons.Count(b => b.IsChecked == true) == 1);
                Check($"{language}/{PetCatalog.All[i].Id}: conserva los controles al seleccionar", ReferenceEquals(choices, model.PetChoices)
                    && Descendants(view).OfType<RadioButton>().Where(b => b.DataContext is PetChoice).SequenceEqual(buttons));
                var menu = TaskbarPet.SpeciesMenu(prefs)!;
                Check($"{language}/{PetCatalog.All[i].Id}: menú coincide con Ajustes", menu.Items.OfType<MenuItem>().Count(m => m.IsChecked) == 1
                    && ((MenuItem)menu.Items[i]).IsChecked && (string)((MenuItem)menu.Items[i]).Header == expected[i]);
            }
            prefs.SetPetSpecies("unknown");
            Check($"{language}: ID desconocido selecciona Mira", model.PetChoices[0].IsSelected && model.PetChoices.Count(p => p.IsSelected) == 1);
            prefs.SetSwitch(PrefKeys.TaskbarPet, false);
            Check($"{language}: se puede apagar sin perder la elección", !model.TaskbarPetOn && !model.ShowPetSpecies && prefs.PetSpecies == "unknown");
            string trayTitle = language == Language.Es ? "Fijar el ícono desde Windows" : "Pin the icon from Windows";
            string trayOpen = language == Language.Es ? "Abrir ajustes de la barra de tareas" : "Open taskbar settings";
            Check($"{language}: ayuda para fijar el ícono traducida",
                model.TrayFallbackTitle == trayTitle && model.TrayFallbackOpen == trayOpen
                && model.TrayFallbackDetail.StartsWith(language == Language.Es ? "Si queda tras la ^" : "If it stays behind the ^")
                && model.OpenTrayFallback is not null);
            Check($"{language}: interruptor directo o ayuda, nunca los dos",
                model.ShowTrayPin != model.ShowTrayFallback);
            var trayButton = Descendants(view).OfType<Button>().FirstOrDefault(b => b.Content as string == trayOpen);
            Check($"{language}: el botón de ayuda se ve cuando no hay interruptor directo",
                trayButton is not null && SeVe(trayButton) == model.ShowTrayFallback);
            var pinSwitch = Descendants(view).OfType<BrandSwitch>().FirstOrDefault(b => b.Command == model.ToggleTrayPin);
            Check($"{language}: el interruptor directo se ve cuando Windows ya registra el ícono",
                pinSwitch is not null && SeVe(pinSwitch) == model.ShowTrayPin);
            // Dock tipo notch: interruptor en Ajustes y pastilla con nombre accesible; la vista vuelve libre al apagarlo.
            Check($"{language}: el interruptor del notch empieza apagado y cambia la preferencia",
                !model.NotchDockOn && model.NotchDockTitle.Length > 0
                && model.NotchDockTitle == (language == Language.Es ? "Dock tipo notch (experimental)" : "Notch dock (experimental)"));
            model.ToggleNotchDock.Execute(null);
            Check($"{language}: encenderlo lo recuerda (en la demo, sin tocar el archivo)", model.NotchDockOn && prefs.Switch(PrefKeys.NotchDock, false));
            model.ToggleNotchDock.Execute(null);
            var notchView = new ContentView(model);
            var notch = new NotchWindow(model, notchView);
            var notchPill = Descendants((DependencyObject)notch.Content).OfType<Button>().FirstOrDefault();
            string? pillName = notchPill is null ? null : System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(notchPill)?.GetName();
            Check($"{language}: la pastilla del notch tiene nombre accesible con la app y el estado",
                pillName is not null && pillName.Contains(AppInfo.Name) && pillName.Contains(model.PillText) && pillName.Contains(model.NotchModel));
            Check($"{language}: la pastilla resume el modelo y la RAM", model.NotchModel.Length > 0 && model.NotchRam.Length > 0);
            // Diseño ancho del notch: dos columnas, más ancho y menos largo, y se deshace sin perder nada.
            foreach (var wideView in new[] { PanelView.Main, PanelView.Activity, PanelView.Settings })
            {
                model.Show(wideView);
                model.BodyMaxHeight = double.PositiveInfinity;
                var tall = new ContentView(model);
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tallHeight = tall.DesiredSize.Height;
                model.WideLayout = true;
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check($"{language}/{wideView}: en ancho mide 640 y es más bajo que en estrecho",
                    tall.DesiredSize.Width == 640 && tall.DesiredSize.Height < tallHeight);
                model.WideLayout = false;
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check($"{language}/{wideView}: al volver a estrecho recupera su tamaño", tall.DesiredSize.Width == 340 && Math.Abs(tall.DesiredSize.Height - tallHeight) < 1);
            }
            model.Show(PanelView.Settings);
            // Versión corta (hover): más baja que el panel entero, y todos sus botones y los del dock tienen nombre.
            var peekView = new NotchPeek(model);
            peekView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            model.WideLayout = true;
            model.Show(PanelView.Main);
            var fullView = new ContentView(model);
            fullView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            model.WideLayout = false;
            // Los de la versión corta solo resuelven sus bindings con la ventana viva: se muestra un instante, fuera de pantalla.
            var peekHost = new Window { Content = peekView, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 300 };
            peekHost.Show();
            peekHost.UpdateLayout();
            Check($"{language}: el hover enseña una versión corta (640 de ancho, más baja que el panel entero)",
                peekView.ActualWidth == 640 && peekView.ActualHeight > 80 && peekView.ActualHeight < 300 && peekView.ActualHeight < fullView.DesiredSize.Height - 100);
            static bool InPeek(DependencyObject d)
            {
                for (; d is not null; d = VisualTreeHelper.GetParent(d)) if (d is NotchPeek) return true;
                return false;
            }
            var notchButtons = Descendants((DependencyObject)notch.Content).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => !InPeek(b))
                .Concat(Descendants(peekView).OfType<System.Windows.Controls.Primitives.ButtonBase>()).ToArray();
            var unnamedNotch = notchButtons.Where(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is not { } n
                || n.Where(c => c is < '' or > '').All(char.IsWhiteSpace)).ToArray();
            foreach (var b in unnamedNotch) Console.WriteLine($"   sin nombre: {b.GetType().Name} content={b.Content?.GetType().Name}");
            peekHost.Close();
            Check($"{language}: los botones del notch ({notchButtons.Length}: pastilla, versión corta y dock) tienen nombre accesible",
                notchButtons.Length >= 8 && unnamedNotch.Length == 0);
            notch.Stop();
            Check($"{language}: al apagar el notch la vista queda libre para volver a la bandeja", LogicalTreeHelper.GetParent(notchView) is null);
            notch.Close();
            // Home y Widgets: widgets elegibles y ordenables, favoritos configurables y nombres accesibles.
            model.Show(PanelView.Home);
            Check($"{language}: el Home empieza con los widgets de siempre", model.HomeWidgets.Select(w => w.Id).SequenceEqual(new[] { "ollama", "system", "clock" }));
            Check($"{language}: el Home lleva el logo y el estado en la cabecera", model.ShowingBrand && !model.ShowingSecondary && model.HeaderTitle == AppInfo.Name);
            prefs.SetList(PrefKeys.NotchWidgets, new[] { "pomodoro", "awake", "clock", "battery" });
            Check($"{language}: elegir widgets cambia el Home y mantiene su orden", model.HomeWidgets.Select(w => w.Id).SequenceEqual(new[] { "pomodoro", "awake", "clock", "battery" }));
            var homeKeep = model.HomeWidgets;
            model.Notify();
            Check($"{language}: un refresco no recrea la lista del Home (no se pierde el foco)", ReferenceEquals(homeKeep, model.HomeWidgets));
            var homeHost = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 700 };
            model.WideLayout = true;
            homeHost.Show();
            homeHost.UpdateLayout();
            var homeButtons = Descendants(homeHost).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
            var unnamedHome = homeButtons.Where(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is not { Length: > 0 }).ToArray();
            Check($"{language}: los botones del Home ({homeButtons.Length}: temporizador, despierto y cabecera) tienen nombre accesible", homeButtons.Length >= 4 && unnamedHome.Length == 0);
            Check($"{language}: el Home pinta sus widgets (reloj con calendario de 42 días)", Descendants(homeHost).OfType<ItemsControl>().Any(i => i.Items.Count == 42));
            prefs.SetList(PrefKeys.NotchWidgets, new[] { "notes" });
            homeHost.UpdateLayout();
            var notesBox = Descendants(homeHost).OfType<TextBox>().FirstOrDefault(t => t.IsVisible);
            Check($"{language}: el widget de notas tiene un campo con nombre accesible",
                notesBox is not null && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(notesBox)?.GetName() == WidgetCatalog.Find("notes")!.Title);
            prefs.SetList(PrefKeys.NotchPeekWidgets, new[] { "notes" });
            Check($"{language}: la pastilla empieza con estado, modelo y RAM, y cada dato tiene texto",
                model.PillIds.SequenceEqual(new[] { "state", "model", "ram" }) && PillCatalog.All.All(p => model.PillValue(p.Id).Length > 0));
            prefs.SetList(PrefKeys.NotchPill, Array.Empty<string>());
            model.Show(PanelView.Widgets);
            homeHost.UpdateLayout();
            var widgetSwitches = Descendants(homeHost).OfType<BrandSwitch>().Where(b => b.IsVisible).ToArray();
            Check($"{language}: la página Widgets lista cada widget (Home y vista corta) y cada favorito con su interruptor con nombre",
                widgetSwitches.Length == 2 * WidgetCatalog.All.Count + PillCatalog.All.Count + ActivityCatalog.All.Count + FavoriteCatalog.All.Count
                && widgetSwitches.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 }));
            var upDown = Descendants(homeHost).OfType<Button>().Where(b => b.IsVisible && b.Content is Border { Child: TextBlock { Text: "\uE70E" or "\uE70D" } }).ToArray();
            Check($"{language}: subir y bajar tienen nombre propio por widget activo",
                upDown.Length == 4 && upDown.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { } n && n.Contains(WidgetCatalog.Find("notes")!.Title)));
            homeHost.Close();
            prefs.SetList(PrefKeys.NotchPill, new[] { "clock", "cpu" });
            Check($"{language}: la pastilla enseña lo elegido y pide a los widgets que sigan leyendo", model.PillIds.SequenceEqual(new[] { "clock", "cpu" })
                && model.PillValue("cpu").StartsWith("CPU") && model.PillRows.Count(r => r.IsOn) == 2);
            prefs.SetList(PrefKeys.NotchPill, new[] { "state", "model", "ram" });
            model.Hub.Pomodoro.Toggle.Execute(null);
            Check($"{language}: un temporizador en marcha es una actividad en vivo con su tiempo",
                model.LiveActivities.Any(a => a.Id == "pomodoro" && a.Text == "25:00"));
            prefs.SetList(PrefKeys.NotchActivities, Array.Empty<string>());
            Check($"{language}: sin actividades permitidas la pastilla no enseña ninguna", model.LiveActivities.Count == 0 && model.ActivityRows.All(r => !r.IsOn));
            prefs.SetList(PrefKeys.NotchActivities, ActivityCatalog.All.Select(a => a.Id).ToArray());
            model.Hub.Pomodoro.Toggle.Execute(null);
            Check($"{language}: la vista corta enseña lo elegido y por defecto Ollama",
                model.PeekWidgets.Select(w => w.Id).SequenceEqual(new[] { "notes" })
                && WidgetLayout.Normalize(null, WidgetCatalog.DefaultPeek).SequenceEqual(new[] { "ollama" }));
            prefs.SetList(PrefKeys.NotchPeekWidgets, new[] { "ollama" });
            model.WideLayout = false;
            prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
            // Todas las combinaciones posibles de widgets, en el Home y en la vista corta: ninguna se sale de su celda ni de la fila,
            // ninguna deja la plantilla sin pintar, ningún texto queda cortado y ningún botón sin nombre.
            {
                var comboIds = WidgetCatalog.All.Select(w => w.Id).ToList();
                var combos = new List<List<string>>();
                void Walk(List<string> current)
                {
                    if (current.Count > 0) combos.Add([.. current]);
                    foreach (var id in comboIds)
                        if (WidgetLayout.CanAdd(current, id)) { current.Add(id); Walk(current); current.RemoveAt(current.Count - 1); }
                }
                Walk([]);
                // Cada widget del catálogo tiene su plantilla (con sus variantes: ver «Widgets nuevos» en CLAUDE.md).
                Check($"{language}: cada widget del catálogo tiene modelo y plantilla propios",
                    WidgetCatalog.All.All(w => model.Hub.Find(w.Id) is { } widget
                        && Application.Current.TryFindResource("Widget" + widget.GetType().Name) is DataTemplate));
                var comboClock = System.Diagnostics.Stopwatch.StartNew();
                Check($"{language}: hay 724 combinaciones posibles de widgets", combos.Count == 724);
                // Las 724 tardan varios minutos: por defecto una de cada siete (siempre las mismas); ITS_ALL_COMBOS=1 las prueba todas.
                if (Environment.GetEnvironmentVariable("ITS_ALL_COMBOS") != "1") combos = combos.Where((_, i) => i % 7 == 0).ToList();
                int checkedBoxes = 0, checkedTexts = 0;
                model.WideLayout = true;
                model.Show(PanelView.Home);
                var comboHome = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 1600 };
                var comboPeek = new Window { Content = new NotchPeek(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 1600 };
                comboHome.Show();
                comboPeek.Show();
                var problems = new List<string>();
                void Audit(Window host, ItemsControl items, List<WidgetSlot> slots, string label)
                {
                    host.UpdateLayout();
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var tag = $"{label} #{i} {slots[i].Id}";
                        checkedBoxes++;
                        if (items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement box) { problems.Add($"{tag}: sin contenedor"); continue; }
                        var bounds = box.TransformToAncestor(items).TransformBounds(new Rect(0, 0, box.ActualWidth, box.ActualHeight));
                        if (bounds.Right > items.ActualWidth + 1) problems.Add($"{tag}: se sale de la fila ({bounds.Right:0} > {items.ActualWidth:0})");
                        if (Math.Abs(bounds.Width - (slots[i].CardWidth + 12)) > 1) problems.Add($"{tag}: ancho {bounds.Width:0} en vez de {slots[i].CardWidth + 12}");
                        if (box.ActualHeight < 40) problems.Add($"{tag}: alto {box.ActualHeight:0}");
                        var inside = Descendants(box).ToList();
                        if (!inside.OfType<TextBlock>().Any(t => t.IsVisible && !string.IsNullOrEmpty(t.Text))) problems.Add($"{tag}: plantilla sin texto");
                        foreach (var t in inside.OfType<TextBlock>().Where(t => t.IsVisible && t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.None && !string.IsNullOrEmpty(t.Text)))
                        {
                            checkedTexts++;
                            var natural = new FormattedText(t.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
                            if (natural > t.ActualWidth + 1) problems.Add($"{tag}: texto cortado «{t.Text}» ({natural:0} > {t.ActualWidth:0})");
                        }
                        foreach (var b in inside.OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible))
                            if (System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is not { Length: > 0 }) problems.Add($"{tag}: botón sin nombre");
                    }
                    // Dentro de cada fila las celdas suman a lo sumo una fila (dos celdas).
                    foreach (var row in slots.Select((slot, i) => (slot, box: items.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement)).Where(x => x.box is not null)
                        .GroupBy(x => Math.Round(x.box!.TransformToAncestor(items).Transform(new Point(0, 0)).Y)))
                        if (row.Sum(x => x.slot.Wide ? 2 : 1) > 2) problems.Add($"{label}: fila con más de dos celdas");
                }
                foreach (var combo in combos)
                {
                    prefs.SetList(PrefKeys.NotchWidgets, combo);
                    var homeItems = Descendants(comboHome).OfType<ItemsControl>().First(i => ReferenceEquals(i.ItemsSource, model.HomeWidgets));
                    Audit(comboHome, homeItems, model.HomeWidgets, $"Home [{string.Join(",", combo)}]");
                    prefs.SetList(PrefKeys.NotchPeekWidgets, combo);
                    var peekItems = Descendants(comboPeek).OfType<ItemsControl>().First(i => ReferenceEquals(i.ItemsSource, model.PeekWidgets));
                    Audit(comboPeek, peekItems, model.PeekWidgets, $"Vista corta [{string.Join(",", combo)}]");
                }
                Console.WriteLine($"   combinaciones: {comboClock.Elapsed.TotalSeconds:0} s");
                foreach (var problem in problems.Take(25)) Console.WriteLine("   " + problem);
                Check($"{language}: {combos.Count} combinaciones de widgets, en el Home y en la vista corta ({checkedBoxes} ranuras y {checkedTexts} textos revisados), caben, se pintan, no cortan texto y tienen sus botones con nombre ({problems.Count} problemas)",
                    problems.Count == 0 && checkedBoxes > 100 && checkedTexts > 300);
                comboHome.Close();
                comboPeek.Close();
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
                prefs.SetList(PrefKeys.NotchPeekWidgets, new[] { "ollama" });
                model.WideLayout = false;
            }
            int RowOf(string id) => WidgetCatalog.All.ToList().FindIndex(w => w.Id == id);
            Check($"{language}: con el Home lleno solo se pueden quitar widgets", model.WidgetsAreFull && !model.WidgetRows[RowOf("pomodoro")].CanToggle && model.WidgetRows[RowOf("clock")].CanToggle);
            model.WidgetRows[RowOf("clock")].Toggle.Execute(null);
            model.WidgetRows[RowOf("pomodoro")].Toggle.Execute(null);
            Check($"{language}: quitar uno y activar otro lo guarda en orden", model.WidgetIds.SequenceEqual(new[] { "ollama", "system", "pomodoro" }));
            model.WidgetRows[RowOf("pomodoro")].Up.Execute(null);
            Check($"{language}: subir un widget lo cambia con el de arriba", model.WidgetIds.SequenceEqual(new[] { "ollama", "pomodoro", "system" }));
            Check($"{language}: los favoritos empiezan en encender, liberar RAM y procesos", model.FavoriteIds.SequenceEqual(new[] { "power", "clean", "processes" }));
            model.FavoriteRows.First(r => r.Toggle is not null && r.IsOn is false).Toggle.Execute(null);
            Check($"{language}: un favorito más hace cuatro y el quinto queda bloqueado",
                model.FavoriteIds.Count == 4 && model.FavoriteRows.Count(r => !r.IsOn && !r.CanToggle) == FavoriteCatalog.All.Count - 4);
            var dockNotch = new NotchWindow(model, new ContentView(model));
            var dockNames = Descendants((DependencyObject)dockNotch.Content).OfType<Button>()
                .Select(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()).ToArray();
            Check($"{language}: el dock lleva las cinco páginas y los cuatro favoritos, cada uno con su nombre",
                new[] { model.NotchTabHome, model.NotchTabOllama, model.NotchTabActivity, model.NotchTabWidgets, model.NotchTabSettings, model.HeroHelp, model.CleanLabel, model.NotchTabProcesses, model.FreeHelp }
                    .All(name => dockNames.Contains(name)));
            dockNotch.Stop();
            dockNotch.Close();
            prefs.SetList(PrefKeys.NotchFavorites, Array.Empty<string>());
            prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
            foreach (var panelView in new[] { PanelView.Main, PanelView.Activity, PanelView.Settings })
            {
                var screen = new ContentView(new PanelViewModel(supervisor, panelView));
                screen.Measure(new Size(340, double.PositiveInfinity));
                screen.Arrange(new Rect(screen.DesiredSize));
                screen.UpdateLayout();
                var controls = Descendants(screen).OfType<System.Windows.Controls.Primitives.ButtonBase>().ToArray();
                // Un nombre accesible real: ni vacío ni solo el glifo de la fuente de íconos (que Narrator no lee).
                var unnamed = controls.Where(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is not { } n
                    || n.Where(c => c is < '' or > '').All(char.IsWhiteSpace)).ToArray();
                foreach (var b in unnamed)
                    Console.WriteLine($"   sin nombre: {b.GetType().Name} {(b.Content as FrameworkElement)?.GetType().Name ?? b.Content?.GetType().Name} ctx={b.DataContext?.GetType().Name} cmd={b.Command}");
                Check($"{language}/{panelView}: todos los botones e interruptores ({controls.Length}) tienen nombre accesible", controls.Length > 0 && unnamed.Length == 0);
                Check($"{language}/{panelView}: todos se alcanzan con el teclado", controls.All(b => b.Focusable));
            }
        }
        Check("la demo no modifica ajustes guardados", originalSettings == (File.Exists(Paths.SettingsFile) ? File.ReadAllText(Paths.SettingsFile) : null));
        ProcessUiTests.Run(Check);
        app.Shutdown();
        Console.WriteLine(failures == 0 ? "interfaz: todo bien" : $"interfaz: {failures} fallos");
        return failures == 0 ? 0 : 1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    // Sin ventana no hay IsVisible fiable: se ve si ningún ancestro lo oculta.
    private static bool SeVe(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }
}
