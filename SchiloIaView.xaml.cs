using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace SchiloArticleComposer;

// Port en C#/WPF de SchiloIA (app.py, Tkinter) : assistant qui prepare un prompt
// (instructions + texte colle) a copier dans une IA externe (Claude/ChatGPT/Copilot).
// Memes presets, meme comportement, integre comme deuxieme ecran d'Article Composer
// plutot qu'une application Python separee.
public partial class SchiloIaView : UserControl
{
    private PresetData _presets = new();
    private bool _suppressPresetEvents;

    public SchiloIaView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadPresetsAndSelectDefault();
    }

    private void LoadPresetsAndSelectDefault()
    {
        _presets = PresetStore.Load();
        RefreshPresetList();

        var names = _presets.Presets.Keys.ToList();
        var selected = names.Contains(_presets.Default) ? _presets.Default : names.FirstOrDefault();

        _suppressPresetEvents = true;
        PresetCombo.SelectedItem = selected;
        _suppressPresetEvents = false;

        LoadPresetText(selected);
    }

    private void RefreshPresetList()
    {
        var names = _presets.Presets.Keys.ToList();
        PresetCombo.ItemsSource = names;

        var count = names.Count;
        var plural = count > 1 ? "s" : "";
        var hint = $"{count} modele{plural} disponible{plural} : cliquez sur la liste ci-dessus pour en choisir un autre.";
        if (!string.IsNullOrEmpty(_presets.Default))
        {
            hint += $" Par defaut au demarrage : {_presets.Default}";
        }
        HintText.Text = hint;
    }

    private void LoadPresetText(string? name)
    {
        InstructionsBox.Text = name != null && _presets.Presets.TryGetValue(name, out var text) ? text : string.Empty;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetEvents) return;
        var name = PresetCombo.SelectedItem as string;
        LoadPresetText(name);
        if (name != null)
        {
            StatusText.Text = $"Modele « {name} » charge.";
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string name) return;
        _presets.Presets[name] = InstructionsBox.Text.Trim() + "\n";
        PresetStore.Save(_presets);
        StatusText.Text = $"Modele « {name} » sauvegarde.";
    }

    private void NewPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = System.Windows.Window.GetWindow(this);
        var name = InputDialog.Show(owner!, "Nouveau modele", "Nom du nouveau modele d'instructions :");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        if (_presets.Presets.ContainsKey(name))
        {
            MessageBox.Show("Un modele porte deja ce nom.", "Nom existant", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _presets.Presets[name] = InstructionsBox.Text.Trim() + "\n";
        PresetStore.Save(_presets);
        RefreshPresetList();

        _suppressPresetEvents = true;
        PresetCombo.SelectedItem = name;
        _suppressPresetEvents = false;

        StatusText.Text = $"Modele « {name} » cree.";
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string name) return;

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
        RefreshPresetList();

        var newSelection = _presets.Presets.Keys.First();
        _suppressPresetEvents = true;
        PresetCombo.SelectedItem = newSelection;
        _suppressPresetEvents = false;
        LoadPresetText(newSelection);

        StatusText.Text = $"Modele « {name} » supprime.";
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string name) return;
        _presets.Default = name;
        PresetStore.Save(_presets);
        RefreshPresetList();
        StatusText.Text = $"« {name} » est maintenant le modele par defaut au demarrage.";
    }

    private string GetFullPrompt()
        => $"{InstructionsBox.Text.Trim()}\n\n{TextToCorrectBox.Text.Trim()}";

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextToCorrectBox.Text))
        {
            MessageBox.Show("Collez d'abord votre texte d'etude biblique.", "Texte vide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        System.Windows.Clipboard.SetText(GetFullPrompt());
        StatusText.Text = "Prompt copie dans le presse-papier ! Collez-le (Ctrl+V) dans votre IA.";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Effacer le texte colle ?", "Effacer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        TextToCorrectBox.Clear();
        StatusText.Text = "Texte efface.";
    }

    private string? GetClipboardTextOrWarn()
    {
        if (!System.Windows.Clipboard.ContainsText())
        {
            MessageBox.Show("Le presse-papier ne contient pas de texte.", "Presse-papier vide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return System.Windows.Clipboard.GetText();
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        var clip = GetClipboardTextOrWarn();
        if (clip == null) return;
        TextToCorrectBox.Text += clip;
        StatusText.Text = "Texte colle depuis le presse-papier.";
    }

    private void ClearAndPasteButton_Click(object sender, RoutedEventArgs e)
    {
        var clip = GetClipboardTextOrWarn();
        if (clip == null) return;
        TextToCorrectBox.Text = clip;
        StatusText.Text = "Texte efface puis remplace par le contenu du presse-papier.";
    }

    private void OpenAi(string url)
    {
        if (!string.IsNullOrWhiteSpace(TextToCorrectBox.Text))
        {
            System.Windows.Clipboard.SetText(GetFullPrompt());
            StatusText.Text = "Prompt copie dans le presse-papier ! Collez-le (Ctrl+V) dans votre IA.";
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void ClaudeButton_Click(object sender, RoutedEventArgs e) => OpenAi("https://claude.ai/new");
    private void ChatGptButton_Click(object sender, RoutedEventArgs e) => OpenAi("https://chatgpt.com");
    private void CopilotButton_Click(object sender, RoutedEventArgs e) => OpenAi("https://copilot.microsoft.com");
}
