using System.Windows;
using System.Windows.Controls;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace SchiloArticleComposer;

// Ecran dedie a la gestion des modeles d'instructions SchiloIA : liste complete
// (au lieu du menu deroulant "un a la fois" de SchiloIaView), pour visualiser,
// modifier, creer et supprimer les modeles. Meme source de donnees (PresetStore)
// que SchiloIaView -> RefreshFromStore() doit etre appele a chaque fois que cet
// ecran redevient visible, au cas ou l'autre ecran ait modifie les presets.
public partial class PresetManagerView : UserControl
{
    private PresetData _presets = new();
    private bool _suppressSelectionEvents;

    public PresetManagerView()
    {
        InitializeComponent();
    }

    public void RefreshFromStore()
    {
        _presets = PresetStore.Load();
        var previouslySelected = PresetListBox.SelectedItem as string;
        RefreshList();

        var names = _presets.Presets.Keys.ToList();
        var toSelect = previouslySelected != null && names.Contains(previouslySelected)
            ? previouslySelected
            : (names.Contains(_presets.Default) ? _presets.Default : names.FirstOrDefault());

        _suppressSelectionEvents = true;
        PresetListBox.SelectedItem = toSelect;
        _suppressSelectionEvents = false;
        LoadSelected(toSelect);
    }

    private void RefreshList()
    {
        PresetListBox.ItemsSource = _presets.Presets.Keys.ToList();
    }

    private void LoadSelected(string? name)
    {
        SelectedNameText.Text = name ?? string.Empty;
        InstructionsBox.Text = name != null && _presets.Presets.TryGetValue(name, out var text) ? text : string.Empty;
        DefaultHintText.Text = name != null && name == _presets.Default
            ? $"« {name} » est le modele par defaut au demarrage."
            : string.Empty;

        var hasSelection = name != null;
        SaveButton.IsEnabled = hasSelection;
        SetDefaultButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        InstructionsBox.IsEnabled = hasSelection;
    }

    private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents) return;
        LoadSelected(PresetListBox.SelectedItem as string);
    }

    private void NewPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var name = InputDialog.Show(owner!, "Nouveau modele", "Nom du nouveau modele d'instructions :");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        if (_presets.Presets.ContainsKey(name))
        {
            MessageBox.Show("Un modele porte deja ce nom.", "Nom existant", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _presets.Presets[name] = string.Empty;
        PresetStore.Save(_presets);
        RefreshList();

        _suppressSelectionEvents = true;
        PresetListBox.SelectedItem = name;
        _suppressSelectionEvents = false;
        LoadSelected(name);

        StatusText.Text = $"Modele « {name} » cree. Renseignez ses instructions puis enregistrez.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetListBox.SelectedItem is not string name) return;
        _presets.Presets[name] = InstructionsBox.Text.Trim() + "\n";
        PresetStore.Save(_presets);
        StatusText.Text = $"Modele « {name} » sauvegarde.";
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetListBox.SelectedItem is not string name) return;
        _presets.Default = name;
        PresetStore.Save(_presets);
        LoadSelected(name);
        StatusText.Text = $"« {name} » est maintenant le modele par defaut au demarrage.";
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetListBox.SelectedItem is not string name) return;

        if (_presets.Presets.Count <= 1)
        {
            MessageBox.Show("Il doit rester au moins un modele.", "Impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Supprimer le modele « {name} » ?", "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _presets.Presets.Remove(name);
        if (_presets.Default == name)
        {
            _presets.Default = _presets.Presets.Keys.First();
        }
        PresetStore.Save(_presets);
        RefreshList();

        var newSelection = _presets.Presets.Keys.First();
        _suppressSelectionEvents = true;
        PresetListBox.SelectedItem = newSelection;
        _suppressSelectionEvents = false;
        LoadSelected(newSelection);

        StatusText.Text = $"Modele « {name} » supprime.";
    }
}
