using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Color = System.Windows.Media.Color;

namespace SchiloArticleComposer;

public partial class MainWindow : FluentWindow
{
    private readonly ObservableCollection<DocSection> _sections = new();
    private readonly DocxParser _parser = new();
    private readonly XmlExporter _exporter = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly AutoUpdater _autoUpdater = new();
    private DocSection? _selected;
    private bool _suppressEdits;

    public MainWindow()
    {
        InitializeComponent();
        SectionsList.ItemsSource = _sections;

        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(this);

        CustomizeHtmlTagColor(ParseColor(AppSettings.Load().HtmlTagColor));

        _ = CheckForUpdatesOnStartupAsync();
    }

    private void NavSchiloIaButton_Click(object sender, RoutedEventArgs e)
    {
        ArticleComposerPanel.Visibility = Visibility.Collapsed;
        SchiloIaPanel.Visibility = Visibility.Visible;
        NavSchiloIaButton.IsEnabled = false;
        NavArticleComposerButton.IsEnabled = true;
    }

    private void NavArticleComposerButton_Click(object sender, RoutedEventArgs e)
    {
        SchiloIaPanel.Visibility = Visibility.Collapsed;
        ArticleComposerPanel.Visibility = Visibility.Visible;
        NavArticleComposerButton.IsEnabled = false;
        NavSchiloIaButton.IsEnabled = true;
    }

    // Remplace la couleur par defaut des balises HTML d'AvalonEdit (theme HTML integre)
    // par la couleur choisie par Eric (parametrable via le bouton "Parametrage"). Le
    // theme integre n'a PAS de couleur nommee "Tag" (verifie en inspectant
    // HTML-Mode.xshd, embarque dans ICSharpCode.AvalonEdit.dll) : les chevrons/nom de
    // balise utilisent "HtmlTag"/"Tags", et le "/" de fermeture utilise "Slash".
    // Passe par HighlightingManager.Instance (definition partagee), toujours
    // disponible immediatement contrairement a ContentBox.SyntaxHighlighting.
    private static void CustomizeHtmlTagColor(Color color)
    {
        var brush = new SimpleHighlightingBrush(color);
        var definition = HighlightingManager.Instance.GetDefinition("HTML");
        foreach (var name in new[] { "HtmlTag", "Tags", "Slash" })
        {
            var namedColor = definition?.GetNamedColor(name);
            if (namedColor != null)
            {
                namedColor.Foreground = brush;
            }
        }
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return Color.FromRgb(0xCA, 0x14, 0xFC);
        }
    }

    private void HtmlTagColorButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Load();
        var currentColor = ParseColor(settings.HtmlTagColor);

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B),
            FullOpen = true,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        var newColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        settings.HtmlTagColor = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
        AppSettings.Save(settings);

        CustomizeHtmlTagColor(newColor);
        ContentBox.TextArea.TextView.Redraw();
    }

    private static Version GetCurrentVersion()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static readonly TimeSpan UpdateCheckThrottle = TimeSpan.FromHours(24);

    private async Task CheckForUpdatesOnStartupAsync()
    {
        var lastCheck = UpdateCheckState.GetLastCheckUtc();
        if (lastCheck.HasValue && DateTime.UtcNow - lastCheck.Value < UpdateCheckThrottle)
        {
            return; // Deja verifie il y a moins de 24h : on evite de solliciter l'API GitHub inutilement.
        }

        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(GetCurrentVersion());
            UpdateCheckState.SetLastCheckNowUtc();
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update != null)
            {
                await OfferUpdateAsync(result.Update);
            }
        }
        catch
        {
            // Verification silencieuse au demarrage : pas de connexion, API indisponible, etc. -> on ignore,
            // et on NE marque PAS la verification comme faite pour pouvoir reessayer au prochain lancement.
        }
    }

    private async Task OfferUpdateAsync(UpdateInfo update)
    {
        var canAutoInstall = update.MsiDownloadUrl != null;
        var question = canAutoInstall
            ? $"Une nouvelle version ({update.Version}) de Schilo Article Composer est disponible.\n\n" +
              "La telecharger et l'installer maintenant ? L'application va se fermer puis redemarrer automatiquement."
            : $"Une nouvelle version ({update.Version}) de Schilo Article Composer est disponible.\n\n" +
              "Ouvrir la page de telechargement ?";

        var result = MessageBox.Show(question, "Mise a jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (!canAutoInstall)
        {
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
            return;
        }

        try
        {
            StatusText.Text = $"Telechargement de la mise a jour {update.Version}...";
            var msiPath = await _autoUpdater.DownloadInstallerAsync(update.MsiDownloadUrl!);
            _autoUpdater.InstallAndRestart(msiPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Echec du telechargement/installation automatique de la mise a jour :\n{ex.Message}\n\n" +
                "Tu peux telecharger et installer manuellement depuis la page de la release.",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _updateChecker.CheckForUpdateAsync(GetCurrentVersion());
            UpdateCheckState.SetLastCheckNowUtc();
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update != null:
                    await OfferUpdateAsync(result.Update);
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
        catch (Exception)
        {
            // Echec technique (reseau, limite API GitHub, etc.) : sans issue actionnable
            // pour l'utilisateur, on affiche un message neutre plutot qu'une erreur brute.
            MessageBox.Show("Pas de mise a jour disponible.", "Mises a jour",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            var (result, fromLiveWord) = ParseWithWordLockFallback(dialog.FileName);

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
            var liveWordNote = fromLiveWord
                ? " (lu depuis la version actuellement ouverte dans Word, y compris modifications non enregistrees)"
                : string.Empty;
            StatusText.Text = $"{_sections.Count} section(s) detectee(s).{ignored}{liveWordNote}";

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
        catch (IOException)
        {
            MessageBox.Show(
                "Ce fichier est ouvert dans un autre programme et n'a pas pu etre lu (y compris via Word).\n" +
                "Ferme-le (ou enregistre-le) puis reessaie.",
                "Fichier verrouille", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de lire ce document :\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Le fichier peut etre verrouille en exclusif (FileShare.None) par Word ou par une
    // synchronisation OneDrive en cours, auquel cas aucun mode de partage cote lecteur
    // ne peut l'ouvrir. Si c'est Word qui le detient, on lui demande une copie
    // temporaire de son contenu actuel plutot que d'echouer.
    private (DocxParseResult Result, bool FromLiveWord) ParseWithWordLockFallback(string path)
    {
        try
        {
            return (_parser.Parse(path), false);
        }
        catch (IOException)
        {
            var tempCopyPath = WordLockBridge.TryGetUnlockedCopy(path);
            if (tempCopyPath == null)
            {
                throw;
            }

            try
            {
                return (_parser.Parse(tempCopyPath), true);
            }
            finally
            {
                try { File.Delete(tempCopyPath); } catch { /* best effort */ }
            }
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

    private void ContentBox_TextChanged(object? sender, EventArgs e)
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
