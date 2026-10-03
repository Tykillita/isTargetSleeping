using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// The process inspector is a normal, resizable window, independent of the tray flyout.
/// Same black glass as the panel: header with the logo, glass cards, segmented filters,
/// Fluent icons and coral only for the irreversible actions.
public sealed class ProcessWindow : Window
{
    // Segoe Fluent Icons
    private const string GlyphPause = "", GlyphPlay = "", GlyphRefresh = "", GlyphSearch = "",
        GlyphDown = "", GlyphUp = "", GlyphClose = "", GlyphInfo = "";

    private readonly Func<ProcessActionRequest, bool, Task<ProcessActionResult>> executeAction;
    private readonly Func<ProcessActionRequest, Task<ProcessActionPreview>> previewAction;
    private readonly Func<Task<string?>>? installAgent;
    private readonly Func<AgentStatus>? agentStatus;
    private readonly bool persistSettings;
    private readonly DataGrid table;
    private readonly Button applicationButton;
    private readonly Button processButton;
    private readonly Button treeButton;
    private readonly Button refreshButton;
    private readonly Button pauseButton;
    private readonly TextBlock pauseGlyph;
    private readonly TextBlock resultText;
    private readonly TextBlock emptyText;
    private readonly ComboBox sortBox;
    private readonly TextBlock directionGlyph;
    private bool operating;
    private bool closed;
    private bool syncingSort;

    public ProcessViewModel Model { get; }

    public ProcessWindow(Func<ProcessActionRequest, bool, Task<ProcessActionResult>> executeAction,
        Func<Task<string?>>? installAgent = null, Func<AgentStatus>? agentStatus = null,
        Func<IReadOnlySet<int>>? protectedPids = null, Func<CancellationToken, Task<ProcessSample>>? capture = null,
        Func<ProcessActionRequest, Task<ProcessActionPreview>>? previewAction = null, bool persistSettings = true)
    {
        this.executeAction = executeAction;
        this.installAgent = installAgent;
        this.agentStatus = agentStatus;
        this.previewAction = previewAction ?? (request => Task.Run(() => ProcessActions.Preview(request)));
        this.persistSettings = persistSettings;
        Model = new ProcessViewModel(capture, protectedPids, persistSettings);
        DataContext = Model;
        Title = tr("Reconocimiento de procesos");
        Width = 1000;
        Height = 680;
        MinWidth = 800;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico
        Icon = Mark.AppIcon(64);
        FontFamily = (FontFamily)Application.Current.FindResource("Sans");
        FontSize = 12;
        SetResourceReference(ForegroundProperty, "Fg");
        // The flyout's global scrollbar is vertical-only; the table also needs horizontal scrolling.
        Resources[typeof(ScrollBar)] = InspectorScrollBarStyle();

        var root = new Grid { Margin = new Thickness(18, 14, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // MARK: cabecera, la misma del panel: logo, título y subtítulo, y botones de icono a la derecha.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new LogoView { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        logo.SetResourceReference(LogoView.ColorProperty, "On");
        header.Children.Add(logo);
        var title = new StackPanel { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text(tr("Reconocimiento de procesos"), 14.5, "Fg", FontWeights.SemiBold));
        var subtitle = Text("", 10.5, "Muted");
        subtitle.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
        subtitle.TextWrapping = TextWrapping.NoWrap;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        subtitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Summary)));
        title.Children.Add(subtitle);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var sampling = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        (pauseButton, pauseGlyph) = IconButton(GlyphPause, tr("Pausar"), () => Model.Paused = !Model.Paused);
        pauseButton.ToolTip = tr("Pausa las actualizaciones automáticas de esta ventana");
        (refreshButton, _) = IconButton(GlyphRefresh, tr("Actualizar"), async () => await Model.RefreshAsync());
        refreshButton.ToolTip = tr("Actualizar") + " (F5)";
        sampling.Children.Add(pauseButton);
        sampling.Children.Add(refreshButton);
        // El marco de vidrio no deja botones de ventana a la vista: este cierra y vuelve al panel.
        var (backButton, _) = IconButton(GlyphClose, tr("Cerrar y volver a isTargetSleeping"), GoBack);
        backButton.ToolTip = tr("Cerrar y volver a isTargetSleeping") + " (Esc)";
        backButton.Margin = new Thickness(6, 0, 0, 0);
        sampling.Children.Add(backButton);
        Grid.SetColumn(sampling, 2);
        header.Children.Add(sampling);
        root.Children.Add(header);

        // MARK: descripción, búsqueda y orden
        var filters = new Grid { Margin = new Thickness(0, 12, 0, 10) };
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var intro = Text(tr("Identifica qué aplicaciones usan la RAM y finaliza las que quieras cerrar."), 11, "Secondary",
            margin: new Thickness(0, 0, 0, 10));
        Grid.SetColumnSpan(intro, 2);
        filters.Children.Add(intro);

        var search = new TextBox { Style = ResourceStyle("Field"), MinHeight = 28, Padding = new Thickness(24, 3, 6, 3),
            FontFamily = (FontFamily)Application.Current.FindResource("Sans") };
        search.SetBinding(TextBox.TextProperty, new Binding(nameof(Model.Search)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        search.ToolTip = tr("Buscar por nombre, ejecutable, ruta o PID") + " (Ctrl+F)";
        AutomationProperties.SetName(search, tr("Buscar procesos"));
        var searchArea = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        searchArea.Children.Add(search);
        var lens = Glyph(GlyphSearch, 11.5, "Muted");
        lens.HorizontalAlignment = HorizontalAlignment.Left;
        lens.Margin = new Thickness(9, 0, 0, 0);
        lens.IsHitTestVisible = false;
        searchArea.Children.Add(lens);
        var placeholder = Text(tr("Buscar por nombre, ejecutable, ruta o PID"), 11.5, "Muted", margin: new Thickness(25, 0, 0, 0));
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.TextWrapping = TextWrapping.NoWrap;
        placeholder.IsHitTestVisible = false;
        search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchArea.Children.Add(placeholder);
        Grid.SetRow(searchArea, 1);
        filters.Children.Add(searchArea);

        var options = new StackPanel { Orientation = Orientation.Horizontal };
        var minimum = Selector([new Option<int>(tr("Sin mínimo"), 0), new("≥ 100 MiB", 100), new("≥ 500 MiB", 500), new("≥ 1 GiB", 1024)],
            0, value => Model.MinimumMiB = value, 116);
        AutomationProperties.SetName(minimum, tr("RAM mínima"));
        minimum.ToolTip = tr("RAM mínima");
        options.Children.Add(minimum);
        sortBox = Selector([new Option<ProcessSort>(tr("RAM"), ProcessSort.Ram), new(tr("CPU"), ProcessSort.Cpu),
            new(tr("Nombre"), ProcessSort.Name), new(tr("Cantidad"), ProcessSort.Count), new(tr("PID"), ProcessSort.Pid)],
            Model.Sort, value => { if (!syncingSort) Model.SetSort(value); }, 104);
        AutomationProperties.SetName(sortBox, tr("Ordenar por"));
        sortBox.ToolTip = tr("Ordenar por");
        sortBox.Margin = new Thickness(8, 0, 0, 0);
        options.Children.Add(sortBox);
        (var directionButton, directionGlyph) = IconButton(Model.Descending ? GlyphDown : GlyphUp, tr("Cambiar dirección de ordenación"),
            () => Model.SetSort(Model.Sort, !Model.Descending));
        directionButton.ToolTip = tr("Cambiar dirección de ordenación");
        options.Children.Add(directionButton);
        Grid.SetRow(options, 1);
        Grid.SetColumn(options, 1);
        filters.Children.Add(options);
        Grid.SetRow(filters, 1);
        root.Children.Add(filters);

        // MARK: tipo de proceso, con el selector segmentado de Ajustes
        var category = Segments([
            new Option<ProcessFilter>(tr("Todos los procesos"), ProcessFilter.All),
            new(tr("Con ventana"), ProcessFilter.Windows), new(tr("Segundo plano"), ProcessFilter.Background),
            new(tr("Sistema"), ProcessFilter.System), new(tr("Protegidos para limpieza"), ProcessFilter.Protected)],
            ProcessFilter.All, value => Model.Filter = value);
        AutomationProperties.SetName(category, tr("Tipo"));
        category.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(category, 2);
        root.Children.Add(category);

        // MARK: tabla, dentro de una tarjeta de vidrio
        table = CreateTable();
        var tableArea = new Grid();
        tableArea.Children.Add(table);
        emptyText = Text(tr("No hay procesos con estos filtros. Cambia la búsqueda o el consumo mínimo."), 11.5, "Secondary");
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        emptyText.VerticalAlignment = VerticalAlignment.Center;
        emptyText.TextAlignment = TextAlignment.Center;
        emptyText.Visibility = Visibility.Collapsed;
        emptyText.IsHitTestVisible = false;
        tableArea.Children.Add(emptyText);
        var tableCard = new Border { Style = ResourceStyle("CardBox"), Padding = new Thickness(0),
            Child = new Border { Child = tableArea, CornerRadius = new CornerRadius(11), ClipToBounds = true } };
        Grid.SetRow(tableCard, 3);
        root.Children.Add(tableCard);

        // MARK: detalle de la selección y sus acciones
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var detailCard = new Border { Style = ResourceStyle("CardBox"), Padding = new Thickness(11, 9, 11, 9) };
        var detailDock = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        applicationButton = Button(tr("Finalizar aplicación"), async () => await Act(ProcessActionMode.Application), "DangerButton");
        processButton = Button(tr("Finalizar proceso"), async () => await Act(ProcessActionMode.Single), "DangerButton");
        treeButton = Button(tr("Finalizar árbol"), async () => await Act(ProcessActionMode.Tree), "DangerButton");
        applicationButton.ToolTip = tr("Todos los procesos de la aplicación y sus descendientes");
        processButton.ToolTip = tr("Solo el proceso seleccionado");
        treeButton.ToolTip = tr("El proceso seleccionado y sus descendientes");
        applicationButton.Margin = treeButton.Margin = new Thickness(0);
        actions.Children.Add(applicationButton);
        actions.Children.Add(processButton);
        actions.Children.Add(treeButton);
        detailDock.Children.Add(actions);
        var detailText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var detail = Text("", 10.5, "Secondary");
        detail.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Details)));
        detailText.Children.Add(new ScrollViewer { Content = detail, MaxHeight = 62, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        resultText = Text("", 11, "Secondary", margin: new Thickness(0, 6, 0, 0));
        resultText.Visibility = Visibility.Collapsed;
        detailText.Children.Add(new ScrollViewer { Content = resultText, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        detailDock.Children.Add(detailText);
        detailCard.Child = detailDock;
        footer.Children.Add(detailCard);
        var note = new DockPanel { Margin = new Thickness(2, 8, 2, 0) };
        var info = Glyph(GlyphInfo, 10.5, "Muted");
        info.VerticalAlignment = VerticalAlignment.Top;
        info.Margin = new Thickness(0, 1, 6, 0);
        DockPanel.SetDock(info, Dock.Left);
        note.Children.Add(info);
        var explanation = Text("", 10.5, "Muted");
        explanation.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.RamExplanation)));
        note.Children.Add(explanation);
        footer.Children.Add(note);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        var tint = new Border { Child = root };
        tint.SetResourceReference(Border.BackgroundProperty, "PanelTint");
        Content = tint;
        Model.PropertyChanged += ModelChanged;
        Model.Rows.CollectionChanged += RowsChanged;
        SourceInitialized += (_, _) => { Place(); Glass.Apply(this, extendFrame: true); };
        IsVisibleChanged += (_, _) => UpdateSampling();
        StateChanged += (_, _) => UpdateSampling();
        Loaded += (_, _) => UpdateSampling();
        Closing += (_, _) => SavePlacement();
        Closed += (_, _) =>
        {
            closed = true;
            Model.PropertyChanged -= ModelChanged;
            Model.Rows.CollectionChanged -= RowsChanged;
            Model.Dispose();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !operating) { GoBack(); e.Handled = true; }
            else if (e.Key == Key.F5) { _ = Model.RefreshAsync(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); e.Handled = true; }
        };
        UpdateActions();
        UpdateSort();
    }

    private void UpdateSampling() => Model.SetSamplingActive(IsVisible && WindowState != WindowState.Minimized);

    /// Al cerrar desde la ventana (botón o Esc) se vuelve al panel de la app.
    public event Action? BackRequested;

    private void GoBack()
    {
        if (operating) return;
        Close();
        BackRequested?.Invoke();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // WindowState's CLR event waits for native WM_SIZE; the property also covers hidden windows.
        if (e.Property == WindowStateProperty && Model != null) UpdateSampling();
    }
    private void RowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        emptyText.Visibility = Model.Snapshot != null && Model.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Model.Selected) or nameof(Model.IsRefreshing)) UpdateActions();
        if (e.PropertyName == nameof(Model.Paused))
        {
            string label = Model.Paused ? tr("Reanudar") : tr("Pausar");
            pauseGlyph.Text = Model.Paused ? GlyphPlay : GlyphPause;
            pauseButton.ToolTip = Model.Paused ? label : tr("Pausa las actualizaciones automáticas de esta ventana");
            AutomationProperties.SetName(pauseButton, label);
        }
        if (e.PropertyName is nameof(Model.Sort) or nameof(Model.Descending)) UpdateSort();
        if (e.PropertyName == nameof(Model.RamHeading)) table.Columns[3].Header = Model.RamHeading;
        if (e.PropertyName == nameof(Model.Snapshot)) RowsChanged(null, null!);
    }
    /// Solo se ven las acciones que aplican a la selección: un grupo se finaliza entero;
    /// un proceso, solo o con sus descendientes.
    private void UpdateActions()
    {
        var selected = Model.Selected;
        bool available = !operating && selected?.CanAct == true;
        applicationButton.IsEnabled = available && selected!.IsGroup;
        processButton.IsEnabled = treeButton.IsEnabled = available && !selected!.IsGroup;
        applicationButton.Visibility = selected?.IsGroup == true ? Visibility.Visible : Visibility.Collapsed;
        processButton.Visibility = treeButton.Visibility = selected is { IsGroup: false } ? Visibility.Visible : Visibility.Collapsed;
        refreshButton.IsEnabled = !operating && !Model.IsRefreshing;
    }
    private void UpdateSort()
    {
        syncingSort = true;
        sortBox.SelectedValue = Model.Sort;
        syncingSort = false;
        directionGlyph.Text = Model.Descending ? GlyphDown : GlyphUp;
        foreach (var column in table.Columns)
            column.SortDirection = SortFrom(column.SortMemberPath) == Model.Sort && column.CanUserSort
                ? Model.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending : null;
    }

    private DataGrid CreateTable()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserReorderColumns = false, CanUserResizeRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
            RowHeight = 40, MinRowHeight = 40, ColumnHeaderHeight = 32,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0),
            EnableRowVirtualization = true, EnableColumnVirtualization = true, Background = Brushes.Transparent,
            RowBackground = Brushes.Transparent, AlternatingRowBackground = Brushes.Transparent,
            FontFamily = (FontFamily)Application.Current.FindResource("Sans"), FontSize = 11.5,
        };
        grid.SetResourceReference(ForegroundProperty, "Fg");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "Line");
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
        AutomationProperties.SetName(grid, tr("Aplicaciones y procesos activos"));
        grid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(Model.Rows)));
        grid.SetBinding(DataGrid.SelectedItemProperty, new Binding(nameof(Model.Selected)) { Mode = BindingMode.TwoWay });

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Fg")));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Track")));
        rowStyle.Triggers.Add(hover);
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("AccentSoft")));
        selected.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Fg")));
        rowStyle.Triggers.Add(selected);
        grid.RowStyle = rowStyle;
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Fg")));
        cellStyle.Setters.Add(new Setter(FocusVisualStyleProperty, null));
        cellStyle.Setters.Add(new Setter(TemplateProperty, CellTemplate()));
        grid.CellStyle = cellStyle;
        grid.ColumnHeaderStyle = HeaderStyle();

        // Desplegar: el chevrón Fluent de los submenús de la app (derecha: plegado, abajo: desplegado).
        var expand = new FrameworkElementFactory(typeof(Button));
        expand.SetValue(StyleProperty, ResourceStyle("Plain"));
        expand.SetValue(FocusableProperty, true);
        expand.SetValue(FocusVisualStyleProperty, FocusRing());
        expand.SetValue(MarginProperty, new Thickness(6, 0, 0, 0));
        expand.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        expand.SetBinding(VisibilityProperty, new Binding(nameof(ProcessRow.IsGroup)) { Converter = new BooleanToVisibilityConverter() });
        expand.SetValue(AutomationProperties.NameProperty, tr("Desplegar o contraer los procesos de la aplicación"));
        expand.SetValue(ToolTipProperty, tr("Desplegar o contraer los procesos de la aplicación"));
        var chevronBox = new FrameworkElementFactory(typeof(Border));
        chevronBox.SetValue(WidthProperty, 22.0);
        chevronBox.SetValue(HeightProperty, 22.0);
        chevronBox.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        chevronBox.SetValue(BackgroundProperty, Brushes.Transparent);
        var chevron = new FrameworkElementFactory(typeof(TextBlock));
        chevron.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProcessRow.Expansion)));
        chevron.SetValue(TextBlock.FontFamilyProperty, Application.Current.FindResource("Icons"));
        chevron.SetValue(TextBlock.FontSizeProperty, 10.0);
        chevron.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        chevron.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        chevronBox.AppendChild(chevron);
        expand.AppendChild(chevronBox);
        expand.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) => { if (((FrameworkElement)sender).DataContext is ProcessRow row) Model.Toggle(row); }));
        grid.Columns.Add(new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = expand }, Width = 32, CanUserSort = false, CanUserResize = false });

        var name = new FrameworkElementFactory(typeof(StackPanel));
        name.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        name.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetBinding(MarginProperty, new Binding(nameof(ProcessRow.NameInset)));
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetValue(WidthProperty, 16.0);
        icon.SetValue(HeightProperty, 16.0);
        icon.SetValue(MarginProperty, new Thickness(2, 0, 9, 0));
        icon.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetBinding(Image.SourceProperty, new Binding(nameof(ProcessRow.Icon)));
        name.AppendChild(icon);
        var labels = new FrameworkElementFactory(typeof(StackPanel));
        labels.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var main = CellText(nameof(ProcessRow.Name), mono: false);
        main.SetValue(TextBlock.FontSizeProperty, 12.0);
        main.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(ProcessRow.Weight)));
        labels.AppendChild(main);
        var exe = CellText(nameof(ProcessRow.Executable), mono: true);
        exe.SetValue(TextBlock.FontSizeProperty, 10.0);
        exe.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        labels.AppendChild(exe);
        name.AppendChild(labels);
        grid.Columns.Add(new DataGridTemplateColumn { Header = tr("Nombre"), CellTemplate = new DataTemplate { VisualTree = name },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 195, SortMemberPath = nameof(ProcessRow.Name) });
        grid.Columns.Add(TextColumn(tr("Cantidad"), nameof(ProcessRow.Count), 72, align: HorizontalAlignment.Right));

        // RAM: la cifra y debajo una barra fina, como la memoria del panel.
        var ram = new FrameworkElementFactory(typeof(StackPanel));
        ram.SetValue(MarginProperty, new Thickness(10, 0, 12, 0));
        ram.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        ram.AppendChild(CellText(nameof(ProcessRow.RamText)));
        var track = new FrameworkElementFactory(typeof(ProgressBar));
        track.SetValue(RangeBase.MinimumProperty, 0.0);
        track.SetValue(RangeBase.MaximumProperty, 1.0);
        track.SetValue(HeightProperty, 3.0);
        track.SetValue(MarginProperty, new Thickness(0, 4, 0, 0));
        track.SetValue(TemplateProperty, BarTemplate());
        track.SetBinding(RangeBase.ValueProperty, new Binding(nameof(ProcessRow.RamFraction)) { Mode = BindingMode.OneWay });
        ram.AppendChild(track);
        grid.Columns.Add(new DataGridTemplateColumn { Header = Model.RamHeading, CellTemplate = new DataTemplate { VisualTree = ram },
            Width = 150, SortMemberPath = nameof(ProcessRow.Ram) });
        grid.Columns.Add(TextColumn(tr("% de RAM"), nameof(ProcessRow.RamPercent), 80, false, align: HorizontalAlignment.Right));
        grid.Columns.Add(TextColumn(tr("CPU"), nameof(ProcessRow.CpuText), 80, true, nameof(ProcessRow.Cpu), align: HorizontalAlignment.Right));
        grid.Columns.Add(TextColumn(tr("Usuario"), nameof(ProcessRow.User), 128, false, muted: true));
        grid.Columns.Add(TextColumn(tr("Estado"), nameof(ProcessRow.Status), 150, false, mono: false, muted: true));
        grid.Columns.Add(TextColumn(tr("PID"), nameof(ProcessRow.Pid), 70, align: HorizontalAlignment.Right, muted: true));
        grid.Sorting += (_, e) =>
        {
            e.Handled = true;
            if (SortFrom(e.Column.SortMemberPath) is { } value) Model.SetSort(value);
        };
        grid.MouseDoubleClick += (_, _) => { if (Model.Selected?.IsGroup == true) Model.Toggle(Model.Selected); };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Right or Key.Left && Model.Selected?.IsGroup == true)
            {
                if (e.Key == Key.Right != Model.Selected.Expanded) Model.Toggle(Model.Selected);
                e.Handled = true;
            }
        };
        return grid;
    }

    private static ProcessSort? SortFrom(string path) => path switch
    { nameof(ProcessRow.Name) => ProcessSort.Name, nameof(ProcessRow.Ram) => ProcessSort.Ram,
        nameof(ProcessRow.Cpu) => ProcessSort.Cpu, nameof(ProcessRow.Count) => ProcessSort.Count,
        nameof(ProcessRow.Pid) => ProcessSort.Pid, _ => null };

    private static DataGridTemplateColumn TextColumn(string header, string property, double width, bool sortable = true,
        string? sortProperty = null, bool mono = true, HorizontalAlignment align = HorizontalAlignment.Left, bool muted = false)
    {
        var value = CellText(property, mono);
        value.SetValue(MarginProperty, new Thickness(10, 0, 10, 0));
        value.SetValue(HorizontalAlignmentProperty, align);
        if (muted) value.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        return new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = value },
            Width = width, CanUserSort = sortable, SortMemberPath = sortProperty ?? property };
    }
    private static FrameworkElementFactory CellText(string property, bool mono = true)
    {
        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetBinding(TextBlock.TextProperty, new Binding(property));
        value.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        value.SetBinding(ToolTipProperty, new Binding(property));
        value.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        value.SetValue(TextBlock.FontFamilyProperty, Application.Current.FindResource(mono ? "Mono" : "Sans"));
        value.SetValue(TextBlock.FontSizeProperty, mono ? 11.0 : 11.5);
        return value;
    }

    private async Task Act(ProcessActionMode mode)
    {
        if (operating || Model.Request(mode) is not { } request) return;
        operating = true;
        UpdateActions();
        try
        {
            var preview = await previewAction(request);
            if (closed) return;
            if (preview.Processes.Count == 0) { ShowResult(tr("Los procesos seleccionados ya no están activos.")); await Model.RefreshAsync(); return; }
            if (!new ProcessConfirmationWindow(preview) { Owner = this }.ShowDialog().GetValueOrDefault()) return;
            ShowResult(tr("Finalizando procesos…"));
            var result = await executeAction(request, false);
            if (closed) return;
            Model.RecordAction(result);
            var allItems = result.Items.ToList();
            if (result.RetryTargets.Count > 0)
            {
                bool ready = agentStatus?.Invoke() == AgentStatus.Ready;
                string button = ready ? tr("Reintentar como administrador") : tr("Instalar agente y reintentar");
                string message = ready
                    ? tr("Windows denegó el acceso a %@ procesos. Puedes reintentar los restantes con permisos de administrador.", result.RetryTargets.Count)
                    : tr("Windows denegó el acceso a %@ procesos. Instala o actualiza el agente para reintentar con permisos de administrador.", result.RetryTargets.Count);
                var retryIds = result.RetryTargets.ToHashSet();
                var retryProcesses = preview.Processes.Where(p => retryIds.Contains(p.Identity)).ToArray();
                bool canElevate = ready || installAgent != null;
                if (canElevate && ProcessConfirmationWindow.Ask(this, tr("Acceso denegado"), message, retryProcesses, preview.UsesPrivateWorkingSet, button))
                {
                    if (!ready)
                    {
                        ShowResult(tr("Instalando agente…"));
                        string? installError = await installAgent!();
                        if (closed) return;
                        if (installError != null) { ShowResult(Describe(result) + Environment.NewLine + installError, "Danger"); return; }
                    }
                    ShowResult(tr("Finalizando procesos como administrador…"));
                    var elevatedRequest = new ProcessActionRequest(Guid.NewGuid(), request.Mode, result.RetryTargets);
                    var elevated = await executeAction(elevatedRequest, true);
                    if (closed) return;
                    Model.RecordAction(elevated);
                    var replacements = elevated.Items.ToDictionary(i => i.Identity);
                    allItems = allItems.Select(i => replacements.GetValueOrDefault(i.Identity, i)).ToList();
                    foreach (var item in elevated.Items.Where(i => allItems.All(existing => existing.Identity != i.Identity))) allItems.Add(item);
                    var outcome = elevated.Outcome == ProcessActionOutcome.Cancelled ? ProcessActionOutcome.Cancelled
                        : allItems.All(i => i.Completed) ? ProcessActionOutcome.Success : allItems.Any(i => i.Completed)
                            ? ProcessActionOutcome.Partial : elevated.Outcome;
                    result = new ProcessActionResult(request.Id, outcome, result.StartedAt, elevated.FinishedAt, allItems);
                }
            }
            ShowResult(Describe(result), result.Outcome switch
            {
                ProcessActionOutcome.Success => "Fg",
                ProcessActionOutcome.Partial or ProcessActionOutcome.Pending => "Warm",
                ProcessActionOutcome.NoWork or ProcessActionOutcome.Cancelled => "Secondary",
                _ => "Danger",
            });
        }
        catch (Exception ex) { if (!closed) ShowResult(tr("No se pudieron finalizar los procesos: %@", ex.Message), "Danger"); }
        finally
        {
            operating = false;
            if (!closed) { await Model.RefreshAsync(); UpdateActions(); }
        }
    }

    /// El resultado de la última acción, bajo el detalle: blanco si fue bien, ámbar si quedó a medias, coral si falló.
    private void ShowResult(string text, string brush = "Secondary")
    {
        resultText.Text = text;
        resultText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        resultText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public static string Describe(ProcessActionResult result)
    {
        string heading = result.Outcome switch
        {
            ProcessActionOutcome.Success => tr("Procesos finalizados"), ProcessActionOutcome.Partial => tr("Finalización parcial"),
            ProcessActionOutcome.NoWork => tr("Los procesos seleccionados ya no están activos."),
            ProcessActionOutcome.Pending => tr("La finalización sigue pendiente"), ProcessActionOutcome.Cancelled => tr("La solicitud de administrador se canceló"),
            _ => tr("No se pudieron finalizar los procesos"),
        };
        var remaining = result.Items.Where(i => !i.Completed).Select(i => tr("PID %@: %@", i.Identity.Pid, ReasonText(i.Reason, i.Outcome))
            + (i.Win32Error.HasValue ? tr(" (código %@)", i.Win32Error.Value) : ""));
        return heading + tr(" · %@ de %@ completados", result.Items.Count(i => i.Completed), result.Items.Count)
            + (result.Remaining.Count > 0 ? Environment.NewLine + string.Join(" · ", remaining) : "")
            + (result.Error != null ? Environment.NewLine + ReasonText(result.Error, ProcessTargetOutcome.Failed) : "");
    }
    private static string TargetText(ProcessTargetOutcome outcome) => outcome switch
    {
        ProcessTargetOutcome.Denied => tr("Acceso denegado"), ProcessTargetOutcome.Blocked => tr("Finalización bloqueada"),
        ProcessTargetOutcome.Changed => tr("El PID cambió de proceso"), ProcessTargetOutcome.Pending => tr("Pendiente"),
        _ => tr("Error"),
    };
    private static string ReasonText(string? reason, ProcessTargetOutcome outcome) => reason switch
    {
        null => TargetText(outcome), "TargetLimit" => tr("Se alcanzó el límite de procesos"),
        "CriticalOrUnknown" or "CriticalUnknown" => tr("No se pudo comprobar que el proceso no sea crítico"),
        "Critical" => tr("Proceso crítico de Windows"), "TerminateFailed" => tr("Windows no pudo finalizar el proceso"),
        "ExitNotConfirmed" => tr("Salida todavía no confirmada"), "Cancelled" => tr("Solicitud cancelada"),
        "Deadline" => tr("Se agotó el tiempo de espera"), "OwnOrSystemProcess" => tr("Proceso propio o del sistema"),
        "UnavailableIdentity" => tr("Identidad no disponible"), "OpenFailed" => outcome == ProcessTargetOutcome.Denied
            ? tr("Acceso denegado") : tr("No se pudo abrir el proceso"), "PidReused" => tr("El PID cambió de proceso"),
        "UnavailablePath" => tr("Ruta no disponible"), "OwnApplication" => tr("Proceso de isTargetSleeping"),
        _ => tr(reason),
    };

    private void Place()
    {
        Win32.GetCursorPos(out var point);
        if (persistSettings && Defaults.Has("processX") && Defaults.Has("processY"))
            point = new Win32.POINT { X = Defaults.GetInt("processX") + 20, Y = Defaults.GetInt("processY") + 20 };
        var monitor = Win32.MonitorFromPoint(point, 2);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) return;
        Win32.GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var work = info.rcWork;
        MinWidth = Math.Min(800, work.Width / scale);
        MinHeight = Math.Min(520, work.Height / scale);
        Width = Math.Clamp(persistSettings ? Defaults.GetInt("processWidth", 1000) : 1000, MinWidth, work.Width / scale);
        Height = Math.Clamp(persistSettings ? Defaults.GetInt("processHeight", 680) : 680, MinHeight, work.Height / scale);
        int x = persistSettings && Defaults.Has("processX") ? Defaults.GetInt("processX") : work.Left + (work.Width - (int)(Width * scale)) / 2;
        int y = persistSettings && Defaults.Has("processY") ? Defaults.GetInt("processY") : work.Top + (work.Height - (int)(Height * scale)) / 2;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - (int)(Width * scale)));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - (int)(Height * scale)));
        Win32.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, (int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale),
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
    private void SavePlacement()
    {
        if (!persistSettings || WindowState != WindowState.Normal || !Win32.GetWindowRect(new WindowInteropHelper(this).Handle, out var rect)) return;
        Defaults.Set("processX", rect.Left);
        Defaults.Set("processY", rect.Top);
        Defaults.Set("processWidth", (int)Math.Round(ActualWidth));
        Defaults.Set("processHeight", (int)Math.Round(ActualHeight));
    }

    // MARK: piezas de la interfaz con el estilo de la app

    private sealed record Option<T>(string Label, T Value);

    /// El selector segmentado de Ajustes: pista de vidrio y la opción elegida en blanco.
    private static Border Segments<T>(IReadOnlyList<Option<T>> values, T selected, Action<T> changed)
    {
        var bar = new UniformGrid { Rows = 1 };
        var pills = new List<(Border Pill, TextBlock Label, T Value)>();
        void Paint(T current)
        {
            foreach (var (pill, label, value) in pills)
            {
                bool on = EqualityComparer<T>.Default.Equals(value, current);
                if (on) pill.SetResourceReference(Border.BackgroundProperty, "Accent");
                else pill.Background = Brushes.Transparent;
                label.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Secondary");
                label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        foreach (var option in values)
        {
            var label = new TextBlock { Text = option.Label, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = (FontFamily)Application.Current.FindResource("Sans") };
            var pill = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 6), Child = label };
            var button = new Button { Style = ResourceStyle("Plain"), Content = pill, Focusable = true, FocusVisualStyle = FocusRing() };
            AutomationProperties.SetName(button, option.Label);
            button.Click += (_, _) => { Paint(option.Value); changed(option.Value); };
            pills.Add((pill, label, option.Value));
            bar.Children.Add(button);
        }
        Paint(selected);
        var track = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = bar, HorizontalAlignment = HorizontalAlignment.Left };
        track.SetResourceReference(Border.BackgroundProperty, "Track");
        return track;
    }

    private static ComboBox Selector<T>(IReadOnlyList<Option<T>> values, T selected, Action<T> changed, double width)
    {
        var selector = new ComboBox { ItemsSource = values, SelectedValuePath = "Value", SelectedValue = selected,
            Width = width, MinHeight = 28, FontSize = 11.5, Focusable = true, FocusVisualStyle = null };
        selector.ItemTemplate = new DataTemplate { VisualTree = CellText("Label", mono: false) };
        selector.SetResourceReference(ForegroundProperty, "Fg");
        selector.Template = SelectorTemplate();
        var itemBorder = new FrameworkElementFactory(typeof(Border));
        itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        itemBorder.SetValue(PaddingProperty, new Thickness(8, 5, 10, 6));
        itemBorder.SetBinding(BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Fg")));
        itemStyle.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = itemBorder }));
        var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Track")));
        itemStyle.Triggers.Add(highlighted);
        selector.ItemContainerStyle = itemStyle;
        selector.SelectionChanged += (_, _) => { if (selector.SelectedValue is T value) changed(value); };
        return selector;
    }

    /// Campo de vidrio como `Field`; la lista se abre con el vidrio y la sombra del menú de la bandeja.
    private static ControlTemplate SelectorTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Grid>
            <ToggleButton Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border x:Name="surface" CornerRadius="6" BorderThickness="1" Padding="8,0,8,0"
                          Background="{DynamicResource Track}" BorderBrush="{DynamicResource Line}">
                    <TextBlock Text="&#xE70D;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="9" HorizontalAlignment="Right"
                               Foreground="{DynamicResource Secondary}" VerticalAlignment="Center" Margin="6,1,0,0" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="surface" Property="Background" Value="{DynamicResource ButtonBg}" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <TextBlock Margin="9,0,22,1" VerticalAlignment="Center" IsHitTestVisible="False" TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource Fg}" Text="{Binding SelectedItem.Label, RelativeSource={RelativeSource TemplatedParent}}" />
            <Popup x:Name="PART_Popup" Placement="Bottom" VerticalOffset="4" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                   IsOpen="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}">
              <Border Background="{DynamicResource PopupBg}" BorderBrush="{DynamicResource CardStroke}" BorderThickness="1"
                      CornerRadius="10" Padding="4" Margin="0,0,10,10"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                <Border.Effect>
                  <DropShadowEffect Color="Black" BlurRadius="18" ShadowDepth="3" Opacity="0.6" Direction="270" />
                </Border.Effect>
                <ScrollViewer MaxHeight="280" VerticalScrollBarVisibility="Auto" CanContentScroll="True">
                  <ItemsPresenter />
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
        """);

    /// Cabecera de columna transparente, con el tono de las etiquetas de sección y la flecha Fluent del orden.
    private static Style HeaderStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               xmlns:p="clr-namespace:System.Windows.Controls.Primitives;assembly=PresentationFramework"
               TargetType="p:DataGridColumnHeader">
          <Setter Property="Foreground" Value="{DynamicResource Secondary}" />
          <Setter Property="FontSize" Value="11" />
          <Setter Property="FontWeight" Value="SemiBold" />
          <Setter Property="Cursor" Value="Hand" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="p:DataGridColumnHeader">
                <Border Background="Transparent" BorderBrush="{DynamicResource Line}" BorderThickness="0,0,0,1" Padding="10,0">
                  <DockPanel>
                    <TextBlock x:Name="arrow" DockPanel.Dock="Right" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="8.5"
                               Margin="5,1,0,0" VerticalAlignment="Center" Foreground="{DynamicResource Fg}" Visibility="Collapsed" />
                    <TextBlock Text="{TemplateBinding Content}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                  </DockPanel>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{DynamicResource Fg}" />
                  </Trigger>
                  <Trigger Property="SortDirection" Value="Ascending">
                    <Setter TargetName="arrow" Property="Text" Value="&#xE70E;" />
                    <Setter TargetName="arrow" Property="Visibility" Value="Visible" />
                    <Setter Property="Foreground" Value="{DynamicResource Fg}" />
                  </Trigger>
                  <Trigger Property="SortDirection" Value="Descending">
                    <Setter TargetName="arrow" Property="Text" Value="&#xE70D;" />
                    <Setter TargetName="arrow" Property="Visibility" Value="Visible" />
                    <Setter Property="Foreground" Value="{DynamicResource Fg}" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    /// Celda sin bordes ni relleno propio: la fila pinta el hover y la selección.
    private static ControlTemplate CellTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="DataGridCell">
          <Border Background="Transparent">
            <ContentPresenter VerticalAlignment="Stretch" />
          </Border>
        </ControlTemplate>
        """);

    /// Barra fina de 3 px: pista de vidrio y relleno gris claro, como la memoria del panel.
    private static ControlTemplate BarTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ProgressBar">
          <Grid>
            <Border x:Name="PART_Track" Background="{DynamicResource Track}" CornerRadius="1.5" />
            <Border x:Name="PART_Indicator" Background="{DynamicResource Secondary}" CornerRadius="1.5" HorizontalAlignment="Left" />
          </Grid>
        </ControlTemplate>
        """);

    private static Style FocusRing()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(MarginProperty, new Thickness(-2));
        border.SetResourceReference(Border.BorderBrushProperty, "Accent");
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = border }));
        return style;
    }

    private static Style InspectorScrollBarStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
          <Setter Property="Width" Value="8" />
          <Setter Property="Height" Value="Auto" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent">
                  <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}"
                         Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                         Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}">
                    <Track.Thumb>
                      <Thumb MinWidth="8" MinHeight="8">
                        <Thumb.Template>
                          <ControlTemplate TargetType="Thumb">
                            <Border Background="{DynamicResource Muted}" CornerRadius="2" Margin="2" Opacity="0.6" />
                          </ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                  </Track>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="Orientation" Value="Vertical">
                    <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="True" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
              <Setter Property="Width" Value="Auto" />
              <Setter Property="Height" Value="8" />
            </Trigger>
          </Style.Triggers>
        </Style>
        """);

    internal static Style ResourceStyle(string name) => (Style)Application.Current.FindResource(name);

    internal static Button Button(string label, Action clicked, string style = "Bordered")
    {
        var button = new Button { Content = label, Style = ResourceStyle(style), Focusable = true, MinHeight = 28,
            Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(11, 4, 11, 5), FocusVisualStyle = FocusRing() };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => clicked();
        return button;
    }

    /// Botón de icono como los de la cabecera del panel: glifo Fluent sobre vidrio gris.
    internal static (Button Button, TextBlock Glyph) IconButton(string glyph, string label, Action clicked)
    {
        var text = Glyph(glyph, 12.5, "Secondary");
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Child = text };
        box.SetResourceReference(Border.BackgroundProperty, "ButtonBg");
        var button = new Button { Style = ResourceStyle("Plain"), Content = box, Focusable = true, FocusVisualStyle = FocusRing(),
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        button.MouseEnter += (_, _) => box.SetResourceReference(Border.BackgroundProperty, "ButtonHover");
        button.MouseLeave += (_, _) => box.SetResourceReference(Border.BackgroundProperty, "ButtonBg");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => clicked();
        return (button, text);
    }

    internal static TextBlock Glyph(string glyph, double size, string brush)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.FindResource("Icons") };
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return text;
    }

    internal static TextBlock Text(string text, double size, string brush, FontWeight? weight = null, Thickness? margin = null)
    {
        var result = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0), FontFamily = (FontFamily)Application.Current.FindResource("Sans") };
        result.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return result;
    }
}
