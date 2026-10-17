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
            model.CurrentSettingsTab = PanelViewModel.SettingsTab.Notch;   // la ayuda y el interruptor de la bandeja están en la pestaña Notch
            view.UpdateLayout();
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
                // Sin ventana los enlaces de visibilidad no se resuelven y todas las páginas cuentan a la vez: se muestra un instante, fuera de pantalla.
                var tallHost = new Window { Content = tall, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 3000 };
                tallHost.Show();
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tallHeight = tall.DesiredSize.Height;
                model.WideLayout = true;
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // Ajustes y Actividad van por pestañas, a una columna: en ancho solo cambia la anchura (la vista principal sí se parte en dos).
                Check($"{language}/{wideView}: en ancho mide 640" + (wideView == PanelView.Main ? " y es más bajo que en estrecho" : ""),
                    tall.DesiredSize.Width == 640 && (wideView != PanelView.Main || tall.DesiredSize.Height < tallHeight));
                model.WideLayout = false;
                tall.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Check($"{language}/{wideView}: al volver a estrecho recupera su tamaño", tall.DesiredSize.Width == 340 && Math.Abs(tall.DesiredSize.Height - tallHeight) < 1);
                tallHost.Close();
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
                model.PillIds.SequenceEqual(new[] { "state", "model", "ram" }) && PillCatalog.All.Where(p => p.Id != "media").All(p => model.PillValue(p.Id).Length > 0));
            // La música es un dato más de la pastilla (vacío, y por tanto oculto, mientras no suena nada) y entonces no se repite como actividad.
            Check($"{language}: «Música» se puede elegir en la pastilla cerrada y sin música no ocupa sitio",
                PillCatalog.Find("media") is { Glyph.Length: > 0 } && model.PillValue("media") == "" && PillCatalog.Toggle(new List<string> { "state", "model" }, "media").SequenceEqual(new[] { "state", "model", "media" })
                && model.PillRows.Any(r => r.Title == L10n.tr("Música")));
            prefs.SetList(PrefKeys.NotchPill, Array.Empty<string>());
            model.Show(PanelView.Widgets);
            homeHost.UpdateLayout();
            var widgetSwitches = Descendants(homeHost).OfType<BrandSwitch>().Where(b => b.IsVisible).ToArray();
            Check($"{language}: la página Widgets lista cada widget (Home y vista corta) y cada favorito con su interruptor con nombre",
                widgetSwitches.Length == 2 * WidgetCatalog.All.Count + PillCatalog.All.Count + ActivityCatalog.All.Count + FavoriteCatalog.All.Count + DockCatalog.Optional.Count
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
                Check($"{language}: hay 831 combinaciones posibles de widgets", combos.Count == 831);
                // Las 831 tardan varios minutos: por defecto una de cada siete (siempre las mismas); ITS_ALL_COMBOS=1 las prueba todas.
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
            Check($"{language}: por defecto el dock lleva solo Inicio, Widgets y Ajustes más los cuatro favoritos, cada uno con su nombre",
                new[] { model.NotchTabHome, model.NotchTabWidgets, model.NotchTabSettings, model.HeroHelp, model.CleanLabel, model.NotchTabProcesses, model.FreeHelp }
                    .All(name => dockNames.Contains(name)) && !dockNames.Contains(model.NotchTabOllama) && !dockNames.Contains(model.NotchTabActivity) && model.DockIds.Count == 0);
            dockNotch.Stop();
            dockNotch.Close();
            model.DockRows.First(r => r.Title == L10n.tr("Ollama")).Toggle.Execute(null);
            model.DockRows.First(r => r.Title == L10n.tr("Actividad")).Toggle.Execute(null);
            var dockNotch2 = new NotchWindow(model, new ContentView(model));
            var dockNames2 = Descendants((DependencyObject)dockNotch2.Content).OfType<Button>()
                .Select(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()).ToArray();
            Check($"{language}: las páginas de Ollama y de Actividad salen en el dock cuando se activan, y se pueden quitar otra vez",
                model.DockIds.SequenceEqual(new[] { "ollama", "activity" }) && dockNames2.Contains(model.NotchTabOllama) && dockNames2.Contains(model.NotchTabActivity)
                && model.DockRows.All(r => r.IsOn));
            dockNotch2.Stop();
            dockNotch2.Close();
            model.DockRows.First(r => r.Title == L10n.tr("Ollama")).Toggle.Execute(null);
            model.DockRows.First(r => r.Title == L10n.tr("Actividad")).Toggle.Execute(null);
            Check($"{language}: sin ellas el dock vuelve a tres botones", model.DockIds.Count == 0);
            // Bandeja de archivos: lo arrastrado aparece como filas con sus botones con nombre, y el dock gana su botón mientras hay archivos.
            {
                var trayDir = Path.Combine(Path.GetTempPath(), "its-ui-tray-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(trayDir);
                var trayFile = Path.Combine(trayDir, "uno.txt");
                File.WriteAllText(trayFile, "1");
                Check($"{language}: sin archivos la bandeja está vacía y no hay botón en el dock", model.NoTrayFiles && !model.HasTrayFiles);
                model.AddToTray(new[] { trayFile, trayDir });
                model.WideLayout = true;
                model.Show(PanelView.Tray);
                Check($"{language}: la bandeja enseña lo arrastrado y su cabecera", model.ShowingTray && model.TrayRows.Count == 2 && model.HeaderTitle == L10n.tr("Bandeja")
                    && model.TrayRows.Any(r => r.IsFolder) && model.TrayRows.Any(r => !r.IsFolder && r.Name == "uno.txt"));
                var trayHost = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 900 };
                trayHost.Show();
                trayHost.UpdateLayout();
                var trayButtons = Descendants(trayHost).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
                Check($"{language}: los botones de la bandeja ({trayButtons.Length}) tienen nombre accesible",
                    trayButtons.Length >= 9 && trayButtons.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 }));
                trayHost.Close();
                var trayNotch = new NotchWindow(model, new ContentView(model));
                var trayNames = Descendants((DependencyObject)trayNotch.Content).OfType<Button>()
                    .Select(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()).ToArray();
                Check($"{language}: con archivos esperando el dock tiene el botón Bandeja", trayNames.Contains(model.NotchTabTray));
                trayNotch.Stop();
                trayNotch.Close();
                // Editor rápido: abrir un .md de la bandeja, cambiarlo, guardarlo y cerrarlo, con sus botones con nombre.
                var mdFile = Path.Combine(trayDir, "nota.md");
                File.WriteAllText(mdFile, "# titulo\n");
                model.AddToTray(new[] { mdFile });
                var mdRow = model.TrayRows.First(r => r.Name == "nota.md");
                Check($"{language}: las filas de archivo tienen botón Editar y las de carpeta no", mdRow.CanEdit && model.TrayRows.First(r => r.IsFolder).CanEdit == false);
                mdRow.Edit.Execute(null);
                Check($"{language}: Editar abre el editor con el texto del archivo", model.ShowingEditor && model.EditText == "# titulo\n" && model.HeaderTitle == "nota.md" && !model.EditDirty);
                model.EditText = "# titulo\ncambio rápido\n";
                Check($"{language}: escribir marca cambios sin guardar", model.EditDirty && model.EditStatus == L10n.tr("Sin guardar") && model.Subtitle == L10n.tr("Cambios sin guardar"));
                var editHost = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 900 };
                editHost.Show();
                editHost.UpdateLayout();
                var editButtons = Descendants(editHost).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
                Check($"{language}: los botones del editor ({editButtons.Length}) tienen nombre accesible",
                    editButtons.Length >= 4 && editButtons.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 }));
                editHost.Close();
                model.CornerCommand.Execute(null);
                Check($"{language}: cerrar con cambios sin guardar avisa y no sale", model.ShowingEditor && model.EditStatus == L10n.tr("Hay cambios sin guardar. Pulsa Cerrar otra vez para descartarlos."));
                model.SaveEdit.Execute(null);
                Check($"{language}: guardar escribe el archivo y limpia el aviso", File.ReadAllText(mdFile) == "# titulo\ncambio rápido\n" && !model.EditDirty && model.EditStatus.StartsWith(L10n.tr("Guardado a las %@", "").TrimEnd()));
                File.WriteAllText(mdFile, "lo cambió otro programa\n");
                File.SetLastWriteTimeUtc(mdFile, DateTime.UtcNow.AddMinutes(3));
                model.EditText = "mi segundo cambio\n";
                model.SaveEdit.Execute(null);
                Check($"{language}: si el archivo cambió fuera se avisa y no se pisa", model.EditConflict && File.ReadAllText(mdFile) == "lo cambió otro programa\n");
                model.OverwriteEdit.Execute(null);
                Check($"{language}: sobrescribir guarda y quita el aviso de conflicto", !model.EditConflict && File.ReadAllText(mdFile) == "mi segundo cambio\n");
                model.CloseEdit.Execute(null);
                Check($"{language}: cerrar sin cambios vuelve a la bandeja", model.ShowingTray && !model.ShowingEditor);
                var binFile = Path.Combine(trayDir, "dato.bin");
                File.WriteAllBytes(binFile, new byte[] { 1, 0, 2 });
                model.AddToTray(new[] { binFile });
                model.TrayRows.First(r => r.Name == "dato.bin").Edit.Execute(null);
                Check($"{language}: un archivo que no es texto no se abre y lo dice en la bandeja", model.ShowingTray && model.HasTrayStatus && model.TrayStatus.Contains("dato.bin"));
                model.Tray.Remove(mdFile);
                model.Tray.Remove(binFile);
                model.Notify();
                model.TrayRows[0].Remove.Execute(null);
                model.ClearTray.Execute(null);
                Check($"{language}: vaciar la bandeja la deja sin filas", model.TrayRows.Count == 0 && model.NoTrayFiles);
                model.WideLayout = false;
                model.Show(PanelView.Main);
                try { Directory.Delete(trayDir, true); } catch { }
            }
            // Mejoras de la bandeja: plurales, contenido de las carpetas, abrir como proyecto, arrastrar todo, recordarla y el aviso de soltar.
            {
                var box = Path.Combine(Path.GetTempPath(), "its-ui-tray2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(box, "carpeta", "sub"));
                File.WriteAllText(Path.Combine(box, "carpeta", "a.txt"), new string('x', 2048));
                File.WriteAllText(Path.Combine(box, "carpeta", "sub", "b.txt"), "y");
                var single = Path.Combine(box, "suelto.txt"); File.WriteAllText(single, "z");
                void Pump(Func<bool> done)
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                    var started = DateTime.UtcNow;
                    poll.Tick += (_, _) => { if (done() || DateTime.UtcNow - started > TimeSpan.FromSeconds(4)) { poll.Stop(); frame.Continue = false; } };
                    poll.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                model.ClearTray.Execute(null);
                model.AddToTray(new[] { Path.Combine(box, "carpeta") });
                Check($"{language}: con un solo elemento el resumen va en singular y mientras mide la carpeta no inventa un tamaño",
                    model.TraySummary == $"{L10n.tr("1 elemento")} · {L10n.tr("calculando…")}" || model.TraySummary.StartsWith(L10n.tr("1 elemento")));
                Pump(() => model.TrayRows.Count == 1 && model.TrayRows[0].Detail.Contains(L10n.tr("%d elementos", 3)));
                Check($"{language}: la carpeta enseña cuántos elementos lleva (archivos y subcarpetas) y su tamaño", model.TrayRows[0].Detail.Contains(L10n.tr("%d elementos", 3)) && model.TrayRows[0].Detail.Contains("2"));
                model.AddToTray(new[] { single });
                Check($"{language}: con dos elementos el resumen va en plural y suma el contenido de la carpeta", model.TraySummary.StartsWith(L10n.tr("%d elementos", 2)) && model.TraySummary.Contains("KB"));
                Check($"{language}: arrastrar todo lleva las rutas de todos los elementos", FileDrag.Split(model.TrayAllPaths).Length == 2 && model.HasManyTrayFiles
                    && FileDrag.Split(model.TrayAllPaths).All(path => path.StartsWith(box)));
                Check($"{language}: cada fila tiene abrir, abrir como proyecto, enseñar, copiar y quitar con nombre, y las carpetas no se editan",
                    model.TrayRows.All(r => r.Open is not null && r.OpenProject is not null && r.OpenHelp.Length > 0 && r.ProjectHelp.Length > 0) && model.TrayRows.Count(r => r.CanEdit) == 1);
                var other = new PanelViewModel(supervisor);   // otro modelo: así el widget Código de las demás pruebas sigue sin carpeta
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
                Check($"{language}: con el Home lleno no se puede abrir una carpeta como proyecto y lo dice", !other.OpenInCode(Path.Combine(box, "carpeta")) && other.TrayStatus == L10n.tr("Quita otro widget para hacer sitio."));
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "clock" });
                Check($"{language}: abrir una carpeta como proyecto añade el widget Código al Home, le da la carpeta y lleva al Home",
                    other.OpenInCode(Path.Combine(box, "carpeta")) && other.WidgetIds.Contains("code") && other.Hub.Code.Root == Path.Combine(box, "carpeta") && other.ShowingHome);
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
                model.Show(PanelView.Main);
                Check($"{language}: la bandeja no se recuerda salvo que se pida, y entonces guarda solo las rutas", !model.TrayRememberOn && !(prefs.StoredList(PrefKeys.TrayItems)?.Count > 0));
                model.ToggleTrayRemember.Execute(null);
                Check($"{language}: al activarlo se guardan las rutas de lo que hay", model.TrayRememberOn && prefs.StoredList(PrefKeys.TrayItems) is { Count: 2 });
                model.ToggleTrayRemember.Execute(null);
                Check($"{language}: al apagarlo no queda nada guardado", !model.TrayRememberOn && prefs.StoredList(PrefKeys.TrayItems) is { Count: 0 });
                model.SetTrayDragging(true);
                Check($"{language}: mientras se arrastra algo se enseña el aviso de soltar, y se apaga al terminar", model.TrayDragging && model.TrayDropText.Length > 0);
                model.SetTrayDragging(false);
                Check($"{language}: …y deja de enseñarse", !model.TrayDragging);
                model.ClearTray.Execute(null);
                var crowd = new List<string>();
                for (int i = 0; i < FileTray.Max + 3; i++) { var f = Path.Combine(box, $"m{i:D2}.txt"); File.WriteAllText(f, "m"); crowd.Add(f); }
                model.AddToTray(crowd);
                Check($"{language}: con la bandeja llena avisa de que no cabe más", model.Tray.Count == FileTray.Max && model.TrayStatus == L10n.tr("La bandeja está llena (%d). Quita algo para añadir más.", FileTray.Max));
                var trayHost2 = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 1400 };
                model.WideLayout = true;
                model.Show(PanelView.Tray);
                trayHost2.Show();
                trayHost2.UpdateLayout();
                var trayButtons2 = Descendants(trayHost2).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
                Check($"{language}: la bandeja llena pinta sus {model.TrayRows.Count} filas y todos sus botones ({trayButtons2.Length}) tienen nombre accesible",
                    model.TrayRows.Count == FileTray.Max && trayButtons2.Length > 5 * FileTray.Max
                    && trayButtons2.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 }));
                trayHost2.Close();
                model.ClearTray.Execute(null);
                model.WideLayout = false;
                model.Show(PanelView.Main);
                try { Directory.Delete(box, true); } catch { }
            }
            // La lente de cristal del dock: viaja entre los botones, se agranda al pulsar, sigue al puntero y se asienta en el botón más cercano.
            {
                // La animación va a fotogramas de 1/60 s, como en la app (un solo paso largo se recorta para que el muelle sea estable).
                void Run(DockLens l, double seconds) { for (double t = 0; t < seconds; t += 1.0 / 60) l.Advance(1.0 / 60); }
                Border Button(string glyph) => new Border { Width = 28, Height = 28, Margin = new Thickness(4, 0, 4, 0), Child = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                foreach (var reduced in new[] { false, true })
                {
                    var dockButtons = new[] { Button("A"), Button("B"), Button("C") };
                    var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    foreach (var b in dockButtons) row.Children.Add(b);
                    var theLens = new DockLens(() => reduced);
                    var hostGrid = new Grid { Height = NotchLayout.DockHeight + 2 * NotchLayout.DockPad };
                    hostGrid.Children.Add(row);
                    hostGrid.Children.Add(theLens.Layer);
                    var lensHost = new Window { Content = hostGrid, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 300, Height = 80 };
                    lensHost.Show();
                    lensHost.UpdateLayout();
                    theLens.SetButtons(dockButtons.Select(b => (b, (TextBlock)b.Child)).ToList());
                    double Cx(int i) => dockButtons[i].TranslatePoint(new Point(14, 14), theLens.Layer).X;
                    string tag = reduced ? "con movimiento reducido" : "con muelle";
                    theLens.Select(1);
                    theLens.Advance(0.016);
                    Check($"{language}: la lente nace tras el botón elegido y no viaja desde el borde ({tag})", theLens.Selected == 1 && Math.Abs(theLens.CenterX - Cx(1)) < 0.5);
                    double restWidth = theLens.LensWidth;
                    theLens.BeginDrag(new Point(Cx(1), 20));
                    Run(theLens, 1);
                    Check($"{language}: al pulsar la lente crece más que la barra y sobresale por arriba y por abajo ({tag})", theLens.Dragging && theLens.LensWidth > restWidth + 20 && theLens.LensHeight > NotchLayout.DockHeight);
                    theLens.DragTo(new Point(Cx(2), 20));
                    theLens.Advance(reduced ? 0 : 0.05);
                    double whileMoving = theLens.LensWidth;
                    Run(theLens, 1);
                    Check($"{language}: arrastrando, la lente sigue al puntero hasta el otro botón ({tag})", Math.Abs(theLens.CenterX - Cx(2)) < 1 && (reduced || whileMoving > theLens.LensWidth));
                    theLens.DragTo(new Point(Cx(0) - 80, 20));
                    Run(theLens, 1);
                    Check($"{language}: …y no se pasa de los extremos más que un poco ({tag})", theLens.CenterX >= Cx(0) - 15 && theLens.CenterX <= Cx(0));
                    theLens.DragTo(new Point(Cx(2) - 3, 20));
                    int chosen = theLens.EndDrag(new Point(Cx(2) - 3, 20));
                    Run(theLens, 1.5);
                    Check($"{language}: al soltar se asienta en el botón más cercano, que es el que se abre, y vuelve a su tamaño ({tag})",
                        chosen == 2 && theLens.Selected == 2 && !theLens.Dragging && Math.Abs(theLens.CenterX - Cx(2)) < 0.5 && Math.Abs(theLens.LensWidth - restWidth) < 0.5);
                    theLens.BeginDrag(new Point(Cx(2), 20));
                    theLens.DragTo(new Point(Cx(0), 20));
                    theLens.CancelDrag();
                    Run(theLens, 1.5);
                    Check($"{language}: si se pierde el ratón a medias, la lente vuelve al botón que estaba elegido ({tag})", !theLens.Dragging && Math.Abs(theLens.CenterX - Cx(2)) < 0.5);
                    theLens.Select(-1);
                    theLens.Advance(0.1);
                    Check($"{language}: los iconos originales vuelven a verse del todo cuando la lente no los cubre ({tag})", dockButtons.All(b => ((TextBlock)b.Child).Opacity > 0.99));
                    theLens.Dispose();
                    lensHost.Close();
                }
            }
            // La canción en la pastilla: «título · artista» en un texto que se desplaza dentro de un ancho fijo, con su indicador.
            {
                Check($"{language}: la pastilla enseña título y artista, o solo el título si no hay artista",
                    PanelViewModel.MediaPillText("Canción", "Artista") == "Canción · Artista" && PanelViewModel.MediaPillText("Solo título", "") == "Solo título");
                Func<TextBlock> makeText = () => new TextBlock { FontSize = 10.5, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
                const string longTitle = "Un título larguísimo de una canción · con un artista también largo";
                var shortMarquee = new Marquee(makeText, 120, false) { Text = "Hola" };
                shortMarquee.Measure(unbounded);
                var longMarquee = new Marquee(makeText, 120, false) { Text = longTitle };
                longMarquee.Measure(unbounded);
                var reducedMarquee = new Marquee(makeText, 120, true) { Text = longTitle };
                reducedMarquee.Measure(unbounded);
                Check($"{language}: un texto corto se queda quieto con su ancho, y uno largo se desplaza sin pasar del ancho fijo",
                    !shortMarquee.Scrolling && shortMarquee.DesiredSize.Width < 120 && longMarquee.Scrolling && Math.Abs(longMarquee.DesiredSize.Width - 120) < 1);
                // El fallo que se vio en la pastilla real: un recorte de diseño de 120 px en la fila que se desplaza (se movía con ella y
                // el final del título nunca llegaba a verse). La fila debe quedar a su tamaño entero y sin recorte propio.
                longMarquee.Arrange(new Rect(longMarquee.DesiredSize));
                Check($"{language}: la fila que se desplaza mide lo que su texto (dos copias) y no lleva recorte propio, para que se vea hasta el final",
                    longMarquee.Strip.ActualWidth > 2 * 120 && System.Windows.Media.VisualTreeHelper.GetClip(longMarquee.Strip) is null
                    && System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(longMarquee.Strip) is null && Math.Abs(longMarquee.ActualWidth - 120) < 1);
                Check($"{language}: con movimiento reducido el texto largo no se desplaza y se recorta al ancho", !reducedMarquee.Scrolling && Math.Abs(reducedMarquee.DesiredSize.Width - 120) < 1);
                longMarquee.Text = "Corto";
                longMarquee.Measure(unbounded);
                Check($"{language}: si la canción cambia a un título corto deja de desplazarse y recupera su ancho", !longMarquee.Scrolling && longMarquee.DesiredSize.Width < 120);
                Check($"{language}: el indicador de reproducción son tres barras", PillEqualizer.Build(false) is System.Windows.Controls.Panel { Children.Count: 3 } && PillEqualizer.Build(true) is System.Windows.Controls.Panel { Children.Count: 3 });
            }
            // Bajo el nombre de la app va la versión de la app, sin nada de Ollama, en el panel y en el Home.
            foreach (var brandView in new[] { PanelView.Main, PanelView.Home })
            {
                model.Show(brandView);
                Check($"{language}: bajo el título de {brandView} va solo la versión de la app", model.Subtitle == $"v{AppInfo.Version}" && !model.Subtitle.Contains("Ollama", StringComparison.OrdinalIgnoreCase));
            }
            model.Show(PanelView.Main);
            // Widget Código: árbol, pestañas, visor de imágenes y editor sobre una carpeta de verdad.
            {
                var iconMap = FileIcons.Map;
                int brokenShapes = iconMap is null ? -1 : iconMap.Keys.Sum(k => FileIcons.BrokenShapes(iconMap.Get(k)!));
                Check($"{language}: los {iconMap?.Count} iconos de archivo y carpeta se leen enteros en WPF y salen congelados",
                    iconMap is { Count: > 1000 } && brokenShapes == 0 && iconMap.Keys.Take(80).All(k => FileIcons.Build(iconMap.Get(k)!).IsFrozen)
                    && FileIcons.For("main.ts", false, false) is { Width: > 0 } && FileIcons.For("src", true, true) is { Height: > 0 });
                var code = model.Hub.Code;
                var projDir = Path.Combine(Path.GetTempPath(), "its-ui-code-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(projDir, "src"));
                Directory.CreateDirectory(Path.Combine(projDir, "node_modules"));
                var mainFile = Path.Combine(projDir, "src", "main.ts"); File.WriteAllText(mainFile, "let a = 1;\n");
                var readmeFile = Path.Combine(projDir, "README.md"); File.WriteAllText(readmeFile, "# Hola\r\ncon crlf\r\n");
                var pngFile = Path.Combine(projDir, "logo.png");
                {
                    var bmp = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using var fs = File.Create(pngFile); enc.Save(fs);
                }
                var binFile = Path.Combine(projDir, "datos.bin"); File.WriteAllBytes(binFile, new byte[] { 1, 0, 2, 0 });
                Check($"{language}: sin carpeta el widget lo pide y no enseña árbol", code.NoRoot && code.Rows.Count == 0 && code.Summary == L10n.tr("Sin carpeta abierta"));
                Check($"{language}: una carpeta que no existe no se acepta", !code.SetRoot(Path.Combine(projDir, "no-existe")) && code.NoRoot);
                Check($"{language}: al elegir carpeta el árbol enseña lo suyo, sin carpetas generadas",
                    code.SetRoot(projDir) && code.HasRoot && code.Rows.Select(r => r.Name).SequenceEqual(new[] { "src", "datos.bin", "logo.png", "README.md" }));
                code.Rows[0].Click.Execute(null);
                Check($"{language}: pulsar una carpeta la abre y enseña su contenido con sangría", code.Rows.Select(r => r.Name).SequenceEqual(new[] { "src", "main.ts", "datos.bin", "logo.png", "README.md" }) && code.Rows[1].Depth == 1 && code.Rows[0].Expanded);
                Check($"{language}: cada fila lleva su icono dibujado y un nombre accesible", code.Rows.All(r => r.Icon is not null && r.Help.Length > 0) && code.Rows[0].Help == L10n.tr("Contraer «%@»", "src"));
                code.Rows[1].Click.Execute(null);
                Check($"{language}: abrir un archivo de texto crea su pestaña y la activa", code.Tabs.Count == 1 && code.ShowText && code.ActiveText == "let a = 1;\n" && code.Rows[1].Active);
                code.ActiveText = "let a = 2;\n";
                Check($"{language}: escribir marca la pestaña sin guardar y lo dice la vista corta", code.Tabs[0].Dirty && code.Status == L10n.tr("Sin guardar") && code.Summary.Contains("1"));
                code.Rows.First(r => r.Name == "README.md").Click.Execute(null);
                code.ActiveText = "# Hola\ncambiado\n";
                code.Tabs[0].Select.Execute(null);
                Check($"{language}: cada pestaña guarda su propio texto al cambiar de una a otra", code.ActiveText == "let a = 2;\n" && code.Tabs[1].Text == "# Hola\ncambiado\n" && code.DirtyCount == 2);
                code.Save.Execute(null);
                Check($"{language}: Guardar escribe el archivo activo y solo ese", File.ReadAllText(mainFile) == "let a = 2;\n" && !code.Tabs[0].Dirty && code.Tabs[1].Dirty && File.ReadAllText(readmeFile) == "# Hola\r\ncon crlf\r\n");
                Check($"{language}: tras guardar sale «Guardado», porque había cambios", code.Status == L10n.tr("Guardado"));
                code.FlashDelay = TimeSpan.FromMilliseconds(60);
                code.Save.Execute(null);
                Check($"{language}: guardar sin haber cambiado nada no enseña ningún mensaje", code.Status == "");
                code.Tabs[0].Text = "let a = 3;\n";
                code.Save.Execute(null);
                {
                    var wait = new System.Windows.Threading.DispatcherFrame();
                    var waitTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    waitTimer.Tick += (_, _) => { waitTimer.Stop(); wait.Continue = false; };
                    waitTimer.Start();
                    Check($"{language}: el «Guardado» está al guardar…", code.Status == L10n.tr("Guardado"));
                    System.Windows.Threading.Dispatcher.PushFrame(wait);
                }
                Check($"{language}: …y se apaga solo a los pocos segundos", code.Status == "" && code.ShortStatus == "");
                code.FlashDelay = TimeSpan.FromSeconds(3);
                code.Tabs[1].Select.Execute(null);
                code.Save.Execute(null);
                Check($"{language}: guarda con los saltos de línea que tenía el archivo (CRLF)", File.ReadAllText(readmeFile) == "# Hola\r\ncambiado\r\n" && code.DirtyCount == 0);
                code.Rows.First(r => r.Name == "logo.png").Click.Execute(null);
                Check($"{language}: una imagen se abre en el visor, no en el editor", code.ShowImage && !code.ShowText && code.ActiveImage is not null);
                code.Rows.First(r => r.Name == "datos.bin").Click.Execute(null);
                Check($"{language}: un binario enseña un aviso y no se edita", code.ShowMessage && code.ActiveMessage.Contains("datos.bin") && !code.ShowText);
                code.Save.Execute(null);
                Check($"{language}: guardar sin un archivo de texto activo no hace nada", File.ReadAllBytes(binFile).Length == 4);
                code.OpenFile(mainFile);
                Check($"{language}: abrir otra vez el mismo archivo no duplica su pestaña", code.Tabs.Count == 4 && code.Active?.Path == mainFile);
                File.WriteAllText(mainFile, "otro programa\n");
                File.SetLastWriteTimeUtc(mainFile, DateTime.UtcNow.AddMinutes(4));
                code.Tabs[0].Select.Execute(null);
                code.ActiveText = "mi cambio\n";
                code.Save.Execute(null);
                Check($"{language}: si el archivo cambió fuera no se pisa y se ofrece sobrescribir", code.Conflict && File.ReadAllText(mainFile) == "otro programa\n");
                code.Overwrite.Execute(null);
                Check($"{language}: sobrescribir guarda y quita el conflicto", !code.Conflict && File.ReadAllText(mainFile) == "mi cambio\n");
                code.ActiveText = "sin guardar\n";
                code.Tabs[0].Close.Execute(null);
                Check($"{language}: cerrar una pestaña con cambios avisa y no la cierra", code.Tabs.Count == 4 && code.Status.Contains("main.ts"));
                code.Tabs[0].Close.Execute(null);
                Check($"{language}: cerrarla otra vez la descarta y activa otra", code.Tabs.Count == 3 && code.Active is not null && File.ReadAllText(mainFile) == "mi cambio\n");
                for (int i = 0; i < CodeWidget.MaxTabs + 2; i++) { var extra = Path.Combine(projDir, $"e{i}.txt"); File.WriteAllText(extra, "x"); code.OpenFile(extra); }
                Check($"{language}: hay un tope de pestañas abiertas y avisa", code.Tabs.Count == CodeWidget.MaxTabs && code.Status.Contains(CodeWidget.MaxTabs.ToString()));
                while (code.Tabs.Count > 0) code.Tabs[0].Close.Execute(null);
                Check($"{language}: sin pestañas el editor pide elegir un archivo", code.NoTabs && !code.ShowText && code.Active is null);
                // El widget pintado: solo, con sus iconos, sus botones con nombre y su editor.
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "code" });
                code.OpenFile(mainFile);
                model.WideLayout = true;
                model.Show(PanelView.Home);
                var codeHost = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 1100 };
                codeHost.Show();
                codeHost.UpdateLayout();
                var codeButtons = Descendants(codeHost).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
                var codeImages = Descendants(codeHost).OfType<Image>().Where(i => i.IsVisible && i.Source is not null).ToArray();
                var codeBox = Descendants(codeHost).OfType<CodeEditor>().FirstOrDefault(t => t.IsVisible);
                Check($"{language}: el widget Código pintado tiene árbol, iconos, editor y botones con nombre ({codeButtons.Length} botones, {codeImages.Length} iconos)",
                    codeButtons.Length >= 8 && codeImages.Length >= 4 && codeBox is not null && codeBox.Text == "mi cambio\n"
                    && codeButtons.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 })
                    && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(codeBox!)?.GetName() == L10n.tr("Editor de %@", "main.ts"));
                Check($"{language}: si es el único widget el editor crece ({codeBox!.ActualHeight:0} px) sin dejar la tarjeta vacía", codeBox.ActualHeight > 200);
                codeHost.Close();
                code.Tabs.ToList().ForEach(t => t.Close.Execute(null));
                prefs.SetList(PrefKeys.NotchWidgets, new[] { "ollama", "system", "clock" });
                model.WideLayout = false;
                model.Show(PanelView.Main);
                try { Directory.Delete(projDir, true); } catch { }
            }
            // Historial del portapapeles: apagado por defecto; al encenderlo aparece su botón en el dock y sus filas tienen botones con nombre.
            {
                Check($"{language}: el historial del portapapeles viene apagado y sin botón", !model.ClipboardOn && model.NoClipItems);
                model.ToggleClipboard.Execute(null);
                model.Clip.Clear();
                model.Clip.Add("primer texto");
                model.Clip.Add("segundo texto\ncon dos líneas");
                model.WideLayout = true;
                model.Show(PanelView.Clipboard);
                model.Notify();
                Check($"{language}: el historial enseña lo copiado, lo último primero", model.ShowingClipboard && model.ClipRows.Count == 2
                    && model.ClipRows[0].Preview == "segundo texto" && model.HeaderTitle == L10n.tr("Portapapeles"));
                var clipHost = new Window { Content = new ContentView(model), WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 900 };
                clipHost.Show();
                clipHost.UpdateLayout();
                var clipButtons = Descendants(clipHost).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible).ToArray();
                Check($"{language}: los botones del historial ({clipButtons.Length}) tienen nombre accesible",
                    clipButtons.Length >= 7 && clipButtons.All(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is { Length: > 0 }));
                clipHost.Close();
                var clipNotch = new NotchWindow(model, new ContentView(model));
                var clipNames = Descendants((DependencyObject)clipNotch.Content).OfType<Button>()
                    .Select(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()).ToArray();
                Check($"{language}: con el historial activo el dock tiene el botón Portapapeles", clipNames.Contains(model.NotchTabClipboard));
                clipNotch.Stop();
                clipNotch.Close();
                model.PauseClip.Execute(null);
                Check($"{language}: pausar cambia el botón a Reanudar", model.ClipPauseLabel == L10n.tr("Reanudar"));
                model.PauseClip.Execute(null);
                model.ToggleClipboard.Execute(null);
                Check($"{language}: apagar el historial lo borra y saca de su página", !model.ClipboardOn && model.Clip.Count == 0 && !model.ShowingClipboard);
                model.WideLayout = false;
                model.Show(PanelView.Main);
            }
            // Favoritos de apps: una app añadida sale en el dock con su nombre y se quita desde la misma lista.
            {
                var demoApp = FavoriteCatalog.AppId(@"C:\Demo\mi-app.exe");
                prefs.SetList(PrefKeys.NotchFavorites, new[] { "power", demoApp });
                model.Notify();
                Check($"{language}: la lista de favoritos incluye la app añadida, activa", model.FavoriteRows.Any(r => r.Title == "mi-app" && r.IsOn));
                var appNotch = new NotchWindow(model, new ContentView(model));
                var appNames = Descendants((DependencyObject)appNotch.Content).OfType<Button>()
                    .Select(b => System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)?.GetName()).ToArray();
                Check($"{language}: el dock enseña la app con su nombre accesible", appNames.Contains(L10n.tr("Abrir %@", "mi-app")));
                appNotch.Stop();
                appNotch.Close();
                model.FavoriteRows.First(r => r.Title == "mi-app").Toggle.Execute(null);
                model.Notify();
                Check($"{language}: quitar la app la saca de la lista", model.FavoriteRows.All(r => r.Title != "mi-app"));
            }
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
        ContrastAudit.Run(Check);
        MasonryTests.Run(Check);
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
