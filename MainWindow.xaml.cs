using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace SchiloArticleComposer;

public partial class MainWindow : FluentWindow
{
    private readonly ObservableCollection<DocSection> _sections = new();
    private readonly DocxParser _parser = new();
    private readonly XmlExporter _exporter = new();
    private readonly UpdateChecker _updateChecker = new();
    private DocSection? _selected;
    private bool _suppressEdits;

    public MainWindow()
    {
        InitializeComponent();
        SectionsList.ItemsSource = _sections;

        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);

        _ = CheckForUpdatesOnStartupAsync();
    }

    private static Version GetCurrentVersion()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(GetCurrentVersion());
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update != null)
            {
                OfferUpdate(result.Update);
            }
        }
        catch
        {
            // Verification silencieuse au demarrage : pas de connexion, API indisponible, etc. -> on ignore.
        }
    }

    private void OfferUpdate(UpdateInfo update)
    {
        var result = MessageBox.Show(
            $"Une nouvelle version ({update.Version}) de Schilo Article Composer est disponible.\n\n" +
            "Ouvrir la page de telechargement ?",
            "Mise a jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(GetCurrentVersion());
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update != null:
                    OfferUpdate(result.Update);
                    break;
                case UpdateCheckStatus.UpToDate:
                    MessageBox.Show("Vous utilisez deja la derniere version.", "Mises a jour",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case UpdateCheckStatus.NoReleasePublished:
                    MessageBox.Show("Aucune release n'a encore ete publiee sur GitHub.", "Mises a jour",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de verifier les mises a jour :\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Documents Word (*.docx)|*.docx",
            Title = "Choisir le document Word source",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = _parser.Parse(dialog.FileName);

            _sections.Clear();
            foreach (var section in result.Sections)
            {
                _sections.Add(section);
            }

            FileNameText.Text = System.IO.Path.GetFileName(dialog.FileName);
            ExportButton.IsEnabled = _sections.Count > 0;

            var ignored = result.ParagraphsBeforeFirstHeading > 0
                ? $" ({result.ParagraphsBeforeFirstHeading} paragraphe(s) avant le premier titre H2 ignore(s) — page de garde, texte des versets, etc.)"
                : string.Empty;
            StatusText.Text = $"{_sections.Count} section(s) detectee(s).{ignored}";

            if (_sections.Count == 0)
            {
                MessageBox.Show(
                    "Aucun titre de style « Titre 1 » / « Heading 1 » (niveau de plan 1) n'a ete trouve dans ce document.\n" +
                    "Verifiez que les titres de section utilisent bien ce style dans Word.",
                    "Aucune section detectee", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                SectionsList.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de lire ce document :\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var included = _sections.Where(s => s.Include).ToList();
        if (included.Count == 0)
        {
            MessageBox.Show("Aucune section cochee a exporter.", "Rien a exporter",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Fichier XML (*.xml)|*.xml",
            FileName = System.IO.Path.GetFileNameWithoutExtension(FileNameText.Text) + "-sections.xml",
            Title = "Enregistrer l'export XML",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _exporter.Export(_sections, dialog.FileName);
            StatusText.Text = $"Export termine : {included.Count} section(s) ecrite(s) dans {dialog.FileName}";
            MessageBox.Show("Export XML termine.", "Termine", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Echec de l'export :\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SectionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = SectionsList.SelectedItem as DocSection;
        _suppressEdits = true;

        TitleBox.Text = _selected?.Title ?? string.Empty;
        ContentBox.Text = _selected?.ContentHtml ?? string.Empty;
        TitleBox.IsEnabled = _selected != null;
        ContentBox.IsEnabled = _selected != null;

        _suppressEdits = false;
    }

    private void TitleBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressEdits || _selected == null) return;
        _selected.Title = TitleBox.Text;
    }

    private void ContentBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressEdits || _selected == null) return;
        _selected.ContentHtml = ContentBox.Text;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        var index = SectionsList.SelectedIndex;
        if (index <= 0) return;
        _sections.Move(index, index - 1);
        SectionsList.SelectedIndex = index - 1;
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        var index = SectionsList.SelectedIndex;
        if (index < 0 || index >= _sections.Count - 1) return;
        _sections.Move(index, index + 1);
        SectionsList.SelectedIndex = index + 1;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "?";

        MessageBox.Show(
            $"Schilo Article Composer\nVersion {versionText}\n\n" +
            "Convertit un document Word (titres « Titre 1 » = sections) en XML de sections\n" +
            "« paragraphe » pour Schilo Builder (schilo.org).\n\n" +
            "Auteur : Eric Philippot\n" +
            "© 2026 Eric Philippot",
            "A propos", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
