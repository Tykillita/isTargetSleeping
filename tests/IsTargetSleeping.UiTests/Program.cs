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
        }
        Check("la demo no modifica ajustes guardados", originalSettings == (File.Exists(Paths.SettingsFile) ? File.ReadAllText(Paths.SettingsFile) : null));
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
}
