using System.IO;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// El widget Equipo en pantalla (fuera de ella: la ventana está a -20000): cada escena de demostración, en las tres vistas y en sus
/// tamaños, se pinta sin textos ilegibles ni botones sin nombre, y lo que tiene que haber está y se puede usar.
internal static class TeamUiTests
{
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    static (Window Host, FrameworkElement View, PanelViewModel Model, TeamWidget Team) Open(string[] widgets, string scene, TeamViewStyle view, bool peek = false)
    {
        var prefs = new Prefs();
        prefs.LoadDemo();
        prefs.SetList(peek ? PrefKeys.NotchPeekWidgets : PrefKeys.NotchWidgets, widgets);
        prefs.SetList(PrefKeys.TeamView, [view.ToString()]);
        var ollama = new OllamaController();
        ollama.LoadDemo();
        var supervisor = new Supervisor(ollama, prefs);
        supervisor.LoadDemo();
        var model = new PanelViewModel(supervisor, PanelView.Home);
        model.Show(PanelView.Home);
        var team = (TeamWidget)model.Hub.Find("team")!;
        team.LoadDemo(scene);
        FrameworkElement content = peek ? new NotchPeek(model) { Width = 640 } : new ContentView(model) { Width = 640 };
        var host = new Window
        {
            Content = content, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
            Width = 664, SizeToContent = SizeToContent.Height, Background = Brushes.Black,
        };
        host.Show();
        host.UpdateLayout();
        if (Environment.GetEnvironmentVariable("ITS_DEBUG_TEAM") == "1") Console.WriteLine($"   [debug] {scene}: ShowingHome={model.ShowingHome} widgets={model.HomeWidgets.Count} presentadores={Descendants(content).OfType<ContentPresenter>().Count(c => c.Content is WidgetSlot)} insignias={Descendants(content).OfType<TeamAgentBadge>().Count()}");
        return (host, content, model, team);
    }

    /// La tarjeta del widget Equipo (el dock y la cabecera ya los cubre el resto de pruebas).
    static FrameworkElement Cards(FrameworkElement view, PanelViewModel model, bool peek) =>
        Descendants(view).OfType<ContentPresenter>().FirstOrDefault(c => c.Content is WidgetSlot { Model: TeamWidget }) ?? view;

    static List<string> Problems(FrameworkElement view)
    {
        var found = new List<string>();
        found.AddRange(ContrastAudit.Offenders(view));
        foreach (var b in Descendants(view).OfType<ButtonBase>().Where(b => b.IsVisible))
            if (UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() is not { Length: > 0 })
                found.Add($"botón sin nombre ({b.GetType().Name}, {b.DataContext?.GetType().Name})");
        foreach (var t in Descendants(view).OfType<TextBlock>().Where(t => t.IsVisible && t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.None && !string.IsNullOrEmpty(t.Text) && t.ActualWidth > 0))
        {
            var natural = new FormattedText(t.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
            if (natural > t.ActualWidth + 1) found.Add($"texto cortado «{t.Text}»");
        }
        return found;
    }

    static int Badges(FrameworkElement v, StepState? state = null) => Descendants(v).OfType<TeamAgentBadge>().Count(b => b.IsVisible && (state is null || b.State == state));

    public static void Run(Action<string, bool> check)
    {
        string lang = L10n.Effective;
        var scenes = new[] { "idle", "plan", "running", "pending", "done", "concern", "failed", "history", "schedules", "editor", "board" };
        foreach (var view in Enum.GetValues<TeamViewStyle>())
        {
            var problems = new List<string>();
            foreach (var scene in scenes)
            {
                var (host, v, m, _) = Open(["team@2"], scene, view);
                foreach (var p in Problems(Cards(v, m, false))) problems.Add($"{scene}: {p}");
                host.Close();
            }
            foreach (var scene in new[] { "idle", "plan", "running", "done" })
            {
                var (host, v, m, _) = Open(["team", "system"], scene, view);
                foreach (var p in Problems(Cards(v, m, false))) problems.Add($"una celda/{scene}: {p}");
                host.Close();
            }
            foreach (var scene in new[] { "running", "done" })
            {
                var (host, v, m, _) = Open(["team"], scene, view, peek: true);
                foreach (var p in Problems(Cards(v, m, true))) problems.Add($"vista corta/{scene}: {p}");
                host.Close();
            }
            foreach (var p in problems.Distinct()) Console.WriteLine($"   equipo/{view}: {p}");
            check($"{lang}: equipo/{view}: todas las escenas se leen y tienen sus botones con nombre ({problems.Distinct().Count()} problemas)", problems.Count == 0);
        }

        // La tubería en cada vista y tamaño.
        var (h1, plan, _, team) = Open(["team@2"], "plan", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: el plan previsto pinta un nodo por paso (tubería)", team.Steps.Count == 6 && Badges(plan) == 6);
        check($"{lang}: equipo: lo detectado se enseña como etiquetas y el router explica el plan", team.ContextChips.Count >= 3 && team.PlanHint.Length > 0);
        check($"{lang}: equipo: en demostración no se puede lanzar", !team.CanLaunch);
        var cycled = team.Steps[1];
        var seen = new List<string>();
        for (int i = 0; i < 3 + Math.Max(1, team.Models.Count) + 1; i++)
        {
            cycled.Cycle.Execute(null);
            seen.Add(!cycled.Overridden ? "router" : cycled.IsCodex ? cycled.Model?.Slug ?? "codex" : cycled.Agent.ToString());
        }
        check($"{lang}: equipo: pulsar el icono de un paso rota por Haiku, Sonnet, Opus y todos los modelos de Codex, y vuelve a lo del router",
            seen.Contains("Haiku") && seen.Contains("Opus") && team.Models.All(m => seen.Contains(m.Slug)) && seen[^1] == "router");
        h1.Close();

        var (hb, box, _, boxTeam) = Open(["team@2"], "idle", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: la caja de la tarea tiene adjuntar, reparto, esfuerzo y enviar, todos con nombre",
            Descendants(box).OfType<Button>().Any(b => b.Command == boxTeam.AttachImages && AutomationProperties.GetName(b).Length > 0)
            && Descendants(box).OfType<Button>().Any(b => b.Command == boxTeam.Launch && AutomationProperties.GetName(b).Length > 0)
            && Descendants(box).OfType<System.Windows.Controls.Primitives.ToggleButton>().Any(t => AutomationProperties.GetName(t) == boxTeam.EffortButtonHelp)
            && Descendants(box).OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == boxTeam.PresetButtonHelp) && Descendants(box).OfType<TeamGauge>().Any());
        boxTeam.EffortIndex = 4;
        var gauge = Descendants(box).OfType<TeamGauge>().First();
        check($"{lang}: equipo: el esfuerzo se recuerda y mueve la aguja del tacómetro (Máximo arriba del todo)", boxTeam.EffortLevel == TeamEffortLevel.Max && gauge.Fraction == 1);
        boxTeam.EffortIndex = 0;
        check($"{lang}: equipo: el esfuerzo Auto no tiene aguja (negativo) y se llama Auto", boxTeam.EffortLevel == TeamEffortLevel.Auto && gauge.Fraction < 0 && boxTeam.EffortText == TeamWidget.EffortName(TeamEffortLevel.Auto));
        var png = Path.Combine(Path.GetTempPath(), $"its-ui-{Guid.NewGuid():N}.png");
        var pixels = new System.Windows.Media.Imaging.WriteableBitmap(8, 8, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pixels));
        using (var file = File.Create(png)) encoder.Save(file);
        int Thumbnails() { box.UpdateLayout(); return Descendants(box).OfType<Image>().Count(i => i.Source is not null && i.IsVisible); }
        boxTeam.AddImages([png]);
        int afterFirst = Thumbnails();
        boxTeam.AddImages([png]);
        int afterSecond = Thumbnails();
        boxTeam.Attachments[0].Remove.Execute(null);
        int afterRemove = Thumbnails();
        check($"{lang}: equipo: se pueden pegar varias imágenes una tras otra y quitar la primera: las miniaturas en pantalla siguen a la lista",
            afterFirst == 1 && afterSecond == 2 && afterRemove == 1 && boxTeam.Attachments.Count == 1);
        boxTeam.Attachments[0].Remove.Execute(null);
        int added = boxTeam.AddImages(Enumerable.Repeat(png, TeamEffort.MaxImages + 2));
        box.UpdateLayout();
        check($"{lang}: equipo: se adjuntan como mucho {TeamEffort.MaxImages} imágenes, con miniatura y con su aviso",
            added == TeamEffort.MaxImages && boxTeam.Attachments.Count == TeamEffort.MaxImages && boxTeam.HasAttachments && boxTeam.Status.Length > 0
            && Descendants(box).OfType<Image>().Count(i => i.Source is not null && i.DataContext is TeamAttachmentRow) == TeamEffort.MaxImages);
        foreach (var attachment in boxTeam.Attachments.ToList()) attachment.Remove.Execute(null);
        var heavy = Path.Combine(Path.GetTempPath(), $"its-ui-pesada-{Guid.NewGuid():N}.png");
        using (var heavyFile = File.Create(heavy)) heavyFile.SetLength(TeamEffort.HeavyImageBytes + 1);
        boxTeam.AddImages([heavy]);
        box.UpdateLayout();
        check($"{lang}: equipo: una imagen de más de 10 MB no se adjunta sola: avisa del coste en tokens y espera Continuar o Cancelar",
            boxTeam.HasHeavyWarning && boxTeam.Attachments.Count == 0 && boxTeam.HeavyWarningText.Length > 0
            && Descendants(box).OfType<Button>().Any(b => b.IsVisible && b.Command == boxTeam.HeavyContinue && AutomationProperties.GetName(b).Length > 0)
            && Descendants(box).OfType<Button>().Any(b => b.IsVisible && b.Command == boxTeam.HeavyCancel));
        boxTeam.Launch.Execute(null);
        check($"{lang}: equipo: con una imagen pesada sin decidir no se lanza y se explica", boxTeam.Status.Length > 0);
        boxTeam.HeavyContinue.Execute(null);
        check($"{lang}: equipo: Continuar adjunta la imagen pesada y quita el aviso", !boxTeam.HasHeavyWarning && boxTeam.Attachments.Count == 1 && Thumbnails() >= 0);
        boxTeam.Attachments[0].Remove.Execute(null);
        boxTeam.AddImages([heavy]);
        boxTeam.HeavyCancel.Execute(null);
        check($"{lang}: equipo: Cancelar descarta la imagen pesada", !boxTeam.HasHeavyWarning && boxTeam.Attachments.Count == 0);
        File.Delete(heavy);
        boxTeam.AddImages(Enumerable.Repeat(png, TeamEffort.MaxImages + 2));
        boxTeam.Attachments[0].Remove.Execute(null);
        check($"{lang}: equipo: quitar una imagen la borra de la tarea y de la pantalla", boxTeam.Attachments.Count == TeamEffort.MaxImages - 1 && Thumbnails() == TeamEffort.MaxImages - 1);
        foreach (var attachment in boxTeam.Attachments.ToList()) attachment.Remove.Execute(null);
        check($"{lang}: equipo: sin imágenes la caja vuelve a estar limpia", !boxTeam.HasAttachments && boxTeam.AddImages(["notas.txt"]) == 0);
        File.Delete(png);
        hb.Close();

        var (hd, boardView, _, boardTeam) = Open(["team@2"], "board", TeamViewStyle.Pipeline);
        var columns = boardTeam.BoardColumns;
        check($"{lang}: equipo: el tablero reparte las tareas en «necesita tu atención», «en marcha» y «hecho»",
            boardTeam.IsBoardTab && columns.Count == 3 && columns[0].Count == 4 && columns[1].Count == 1 && columns[2].Count == 4 && boardTeam.Tabs.Count == 5);
        check($"{lang}: equipo: cada tarjeta del tablero es un botón con nombre que se alcanza con el teclado",
            Descendants(boardView).OfType<Button>().Where(b => b.IsVisible && columns.SelectMany(c => c.Cards).Any(card => card.Help == AutomationProperties.GetName(b))).Count() >= 5
            && Descendants(boardView).OfType<Button>().Where(b => columns.SelectMany(c => c.Cards).Any(card => card.Help == AutomationProperties.GetName(b))).All(b => b.Focusable && AutomationProperties.GetName(b).Length > 0));
        boardTeam.BoardViews[1].Pick.Execute(null);
        boardView.UpdateLayout();
        check($"{lang}: equipo: la vista de lista enseña los mismos grupos uno bajo otro", boardTeam.BoardIsList && !boardTeam.BoardIsBoard
            && Descendants(boardView).OfType<Button>().Count(b => b.IsVisible && columns.SelectMany(c => c.Cards).Any(card => card.Help == AutomationProperties.GetName(b))) >= 5);
        var need = boardTeam.BoardColumns[0].Cards;
        var failedCard = boardTeam.BoardColumns[2].Cards.FirstOrDefault(c => c.ShowRetry);
        check($"{lang}: equipo: en el tablero el plan por aprobar se aprueba o se rechaza desde su tarjeta, y una tarea fallida se reintenta",
            need.Any(c => c.ShowApprove && c.ShowReject && c.ShowOpen) && failedCard is { ShowOpen: true } && !need.Where(c => !c.ShowApprove).Any(c => c.ShowApprove)
            && boardTeam.BoardColumns[1].Cards.All(c => !c.ShowCancel)
            && !boardTeam.BoardColumns[1].Cards.Any(c => need.Any(n => n.ShowApprove && n.Title == c.Title)));
        var outside = columns.SelectMany(c => c.Cards).Where(c => c.AgentLabel.Contains("Claude Code") || c.AgentLabel.Contains("Codex") && c.Meta.StartsWith("Codex")).ToList();
        check($"{lang}: equipo: el tablero también enseña las sesiones de Claude Code y Codex lanzadas desde otras apps, en la columna que toca, solo con «abrir carpeta»",
            columns[0].Cards.Any(c => c.Meta.StartsWith("Codex") && c.IsWarn) && columns[1].Cards.Any(c => c.Meta.StartsWith("Claude Code") && c.IsLive) && columns[2].Cards.Any(c => c.Meta.StartsWith("Claude Code") && c.IsGood)
            && outside.All(c => !c.ShowApprove && !c.ShowRetry && !c.ShowCancel));
        // Una respuesta ya cerrada se puede seguir: su tarjeta enseña el cuadro de seguimiento con su pista y su botón propios.
        var (answerHost, _, _, answerTeam) = Open(["team@2"], "answer", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: una respuesta ya cerrada se puede seguir con otra pregunta (cuadro y botón «Preguntar»)",
            answerTeam.EntryIsAnswer && answerTeam.ShowFollowUpBox && answerTeam.FollowUpLabel == L10n.tr("Preguntar") && answerTeam.FollowUpHint == L10n.tr("Haz otra pregunta sobre esta respuesta…")
            && !answerTeam.CanFollowUp);
        answerHost.Close();
        var markdown = MarkdownView.Build("# Título\n\nTexto con **negrita**, *cursiva* y `código`.\n\n- uno\n- dos\n\n1. a\n2. b\n\n```\nbloque\n```", 12);
        var markdownText = new System.Windows.Documents.TextRange(markdown.ContentStart, markdown.ContentEnd).Text;
        check($"{lang}: equipo: las respuestas se maquetan como Markdown (títulos, negrita, listas y código) en vez de enseñar los asteriscos",
            markdown.Blocks.OfType<System.Windows.Documents.List>().Count() == 2 && markdown.Blocks.OfType<System.Windows.Documents.Paragraph>().Count() >= 3
            && markdownText.Contains("negrita") && markdownText.Contains("bloque") && !markdownText.Contains("**") && !markdownText.Contains("```") && !markdownText.Contains('`'));
        var planCard = need.First(c => c.ShowApprove);
        bool wasOpen = planCard.IsExpanded;
        planCard.Toggle.Execute(null);
        boardView.UpdateLayout();
        check($"{lang}: equipo: pulsar una tarjeta la despliega o la pliega y se acuerda al repintar el tablero",
            boardTeam.BoardColumns[0].Cards.First(c => c.ShowApprove).IsExpanded == !wasOpen);
        planCard.Toggle.Execute(null);
        check($"{lang}: equipo: la tarjeta desplegada enseña sus pasos con sus tiempos", boardTeam.BoardColumns[2].Cards.Any(c => c.IsExpanded && c.HasLines && c.Lines.All(l => l.When.EndsWith("s"))));
        hd.Close();

        var (hl, launchView, _, launchTeam) = Open(["team@2"], "idle", TeamViewStyle.Pipeline);
        launchTeam.Launch.Execute(null);
        check($"{lang}: equipo: pulsar enviar sin poder lanzar explica por qué en vez de no hacer nada", launchTeam.Status.Length > 0 && launchTeam.HasStatus);
        check($"{lang}: equipo: la caja de la tarea es una zona donde se sueltan imágenes (y el notch no se las lleva a la bandeja)",
            Descendants(launchView).OfType<TextBox>().Any(t => TeamAttach.Inside(t)) && !TeamAttach.Inside(launchView));
        hl.Close();

        var (h2, running, _, live) = Open(["team@2"], "running", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: en marcha un nodo trabaja, otros están hechos y se puede cancelar",
            Badges(running, StepState.Running) == 1 && Badges(running, StepState.Done) >= 3 && live.Busy && Descendants(running).OfType<Button>().Any(b => b.IsVisible && b.Command == live.Cancel));
        check($"{lang}: equipo: en marcha se enseñan los tokens y su reparto entre Claude y Codex", live.HasTokens && live.HasShare && live.ClaudeShare is > 0 and <= 1);
        var (hp, _, pillModel, pillTeam) = Open(["team@2"], "running", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: en marcha la pastilla dice el avance y la actividad lleva su progreso", pillModel.PillValue("team").Contains("/") && pillTeam.ProgressFraction is > 0 and < 1
            && pillModel.LiveActivities.Any(x => x.Id == "team"));
        hp.Close();
        h2.Close();

        var (h3, pending, _, gate) = Open(["team@2"], "pending", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: un plan pendiente se enseña con formato (no el texto en bruto) y con los botones de aprobar, rechazar y editar",
            gate.HasPending && Descendants(pending).OfType<MarkdownView>().Any(m => m.IsVisible && m.Markdown == gate.PlanDraft)
            && !Descendants(pending).OfType<TextBox>().Any(t => t.IsVisible && t.Text == gate.PlanDraft)
            && Descendants(pending).OfType<Button>().Any(b => b.IsVisible && b.Command == gate.ApprovePlan) && Descendants(pending).OfType<Button>().Any(b => b.IsVisible && b.Command == gate.RejectPlan)
            && Descendants(pending).OfType<Button>().Any(b => b.IsVisible && b.Command == gate.EditPlan));
        gate.EditPlan.Execute(null);
        pending.UpdateLayout();
        bool showsEditor = gate.PlanEditing && Descendants(pending).OfType<TextBox>().Any(t => t.IsVisible && t.Text == gate.PlanDraft)
            && !Descendants(pending).OfType<MarkdownView>().Any(m => m.IsVisible && m.Markdown == gate.PlanDraft);
        gate.EditPlan.Execute(null);
        check($"{lang}: equipo: «Editar» cambia el plan por su texto editable, y volver a pulsarlo lo deja con formato", showsEditor && gate.PlanPreview);
        h3.Close();

        var (h4, done, _, result) = Open(["team@2"], "done", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: una tarea terminada enseña su resultado, sus archivos y cómo fusionarla, verla o pedir cambios",
            result.ShowResult && result.EntryOpen && result.HasChanges && result.HasVerdict
            && new[] { result.Merge, result.Discard, result.OpenFolder, result.ToggleDiff }.All(c => Descendants(done).OfType<Button>().Any(b => b.IsVisible && b.Command == c))
            && Descendants(done).OfType<TextBox>().Count(t => t.IsVisible) >= 2);
        result.Merge.Execute(null);
        check($"{lang}: equipo: fusionar pide confirmar con un segundo clic", result.MergeLabel == L10n.tr("¿Fusionar?"));
        h4.Close();

        var (h5, history, _, hist) = Open(["team@2"], "history", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: el historial lista las tareas y la pestaña lo cuenta", hist.HistoryRows.Count == 4 && hist.IsHistoryTab && hist.HistoryTabLabel.Contains('4')
            && Descendants(history).OfType<Button>().Count(b => b.IsVisible && b.DataContext is TeamHistoryRow) == 4);
        hist.ShowSchedules.Execute(null);
        history.UpdateLayout();
        check($"{lang}: equipo: se cambia de pestaña a las programadas", hist.IsScheduleTab && !hist.IsHistoryTab);
        h5.Close();

        var (h6, schedules, _, sched) = Open(["team@2"], "schedules", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: las programadas enseñan su frecuencia, la próxima y la última, y se pueden ejecutar, editar y borrar",
            sched.ScheduleRows.Count == 3 && sched.ScheduleRows[0].Frequency.Contains("08:30") && sched.ScheduleRows[0].Next.Length > 0 && sched.ScheduleRows[1].Bad
            && Descendants(schedules).OfType<Button>().Count(b => b.IsVisible && b.DataContext is TeamScheduleRow) >= 9);
        h6.Close();

        var (h7, editor, _, edit) = Open(["team@2"], "editor", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: el formulario de una programada es válido y enseña su hora", edit.HasEditor && edit.Editor!.Valid && edit.Editor.ShowTime
            && Descendants(editor).OfType<TextBox>().Any(t => t.IsVisible && t.Text == "08:30"));
        edit.Editor!.Time = "25:99";
        check($"{lang}: equipo: una hora imposible invalida el formulario", !edit.Editor.Valid);
        h7.Close();

        // Una celda y vista corta.
        var (h8, ringHost, _, _) = Open(["team", "system"], "running", TeamViewStyle.Pipeline);
        check($"{lang}: equipo: con una celda la tubería es un anillo", Descendants(ringHost).OfType<TeamRing>().Count(r => r.IsVisible) == 1 && Descendants(ringHost).OfType<TeamTrack>().All(t => !t.IsVisible));
        h8.Close();
        var (h9, barsHost, _, _) = Open(["team", "system"], "running", TeamViewStyle.Timeline);
        check($"{lang}: equipo: con una celda la línea de tiempo son barras finas", Descendants(barsHost).OfType<TeamTrack>().Count(t => t.IsVisible) == 6);
        h9.Close();
        var (h10, crewHost, _, _) = Open(["team@2"], "running", TeamViewStyle.Crew);
        check($"{lang}: equipo: la mesa enseña un retrato por paso y lo último que dijo cada uno", Badges(crewHost) >= 6 && Descendants(crewHost).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("Ajustes") || t.Text.Contains("Settings")));
        h10.Close();
        var (h11, peekHost, _, _) = Open(["team"], "running", TeamViewStyle.Pipeline, peek: true);
        check($"{lang}: equipo: la vista corta enseña solo el avance", Badges(peekHost) == 6 && Descendants(peekHost).OfType<TextBox>().All(t => !t.IsVisible));
        h11.Close();

        // El ajuste de Ajustes › Notch.
        var prefs = new Prefs();
        prefs.LoadDemo();
        var ollama = new OllamaController();
        ollama.LoadDemo();
        var supervisor = new Supervisor(ollama, prefs);
        supervisor.LoadDemo();
        var settings = new PanelViewModel(supervisor, PanelView.Settings);
        check($"{lang}: equipo: Ajustes ofrece las tres vistas y la de tubería viene elegida", settings.TeamViewSegments.Count == 3 && settings.TeamViewSegments[0].IsSelected);
        settings.TeamViewSegments[1].Pick.Execute(null);
        check($"{lang}: equipo: elegir la línea de tiempo se guarda", prefs.StoredList(PrefKeys.TeamView)?.FirstOrDefault() == nameof(TeamViewStyle.Timeline));
    }
}
