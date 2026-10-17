using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Color = System.Windows.Media.Color;

namespace SchiloArticleComposer;

public partial class MainWindow : FluentWindow
{
    private readonly ObservableCollection<DocSection> _sections = new();
    private readonly List<DocArticle> _articles = new();
    private readonly DocxParser _parser = new();
    private readonly XmlExporter _exporter = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly AutoUpdater _autoUpdater = new();
    private DocSection? _selected;
    private bool _suppressEdits;
    private string? _currentDocxPath;

    public MainWindow()
    {
        InitializeComponent();
        SectionsList.ItemsSource = _sections;

        var settings = AppSettings.Load();
        ThemeManager.Apply(settings.ThemePreference, this);
        CustomizeHtmlTagColor(ParseColor(settings.HtmlTagColor));

        SetActiveNavButton(NavArticleComposerButton, NavSchiloIaButton);

        SchiloIaPanel.ManagePresetsRequested += (_, _) => ShowPresetManager();

        _ = CheckForUpdatesOnStartupAsync();

        // Ouverture directe d'un document passe en argument (glisser-deposer d'un .docx
        // sur l'exe, "Ouvrir avec...").
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && File.Exists(args[1]))
        {
            Loaded += (_, _) => LoadDocument(args[1]);
        }
    }

    // L'onglet actif etait auparavant IsEnabled=false, ce qui le grise (visuellement
    // confus : l'onglet courant semblait desactive). Bold + soulignement accent au lieu
    // du style "disabled" par defaut ; les deux boutons restent cliquables.
    private static void SetActiveNavButton(System.Windows.Controls.Button active, System.Windows.Controls.Button inactive)
    {
        active.FontWeight = FontWeights.Bold;
        active.Opacity = 1;
        active.BorderThickness = new Thickness(0, 0, 0, 3);
        active.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCA, 0x14, 0xFC));

        inactive.FontWeight = FontWeights.Normal;
        inactive.Opacity = 0.6;
        inactive.BorderThickness = new Thickness(0);
        inactive.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
    }

    // Les trois ecrans (Article Composer / SchiloIA / Gestion des modeles) se
    // partagent la meme cellule de Grid : un seul visible a la fois.
    private void ShowScreen(UIElement toShow)
    {
        ArticleComposerPanel.Visibility = toShow == ArticleComposerPanel ? Visibility.Visible : Visibility.Collapsed;
        SchiloIaPanel.Visibility = toShow == SchiloIaPanel ? Visibility.Visible : Visibility.Collapsed;
        PresetManagerPanel.Visibility = toShow == PresetManagerPanel ? Visibility.Visible : Visibility.Collapsed;

        // Le bouton "Gerer les modeles..." n'a de sens que dans la zone SchiloIA
        // (ecran principal ou ecran de gestion des modeles).
        ManagePresetsNavButton.Visibility = toShow == SchiloIaPanel || toShow == PresetManagerPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NavSchiloIaButton_Click(object sender, RoutedEventArgs e)
    {
        SchiloIaPanel.RefreshFromStore();
        ShowScreen(SchiloIaPanel);
        SetActiveNavButton(NavSchiloIaButton, NavArticleComposerButton);
    }

    private void NavArticleComposerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(ArticleComposerPanel);
        SetActiveNavButton(NavArticleComposerButton, NavSchiloIaButton);
    }

    private void ShowPresetManager()
    {
        PresetManagerPanel.RefreshFromStore();
        ShowScreen(PresetManagerPanel);
    }

    private void ManagePresetsNavButton_Click(object sender, RoutedEventArgs e) => ShowPresetManager();

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

        var progressWindow = new UpdateProgressWindow { Owner = this };
        progressWindow.Show();

        try
        {
            var progress = new Progress<int>(progressWindow.ReportDownloadProgress);
            var msiPath = await _autoUpdater.DownloadInstallerAsync(update.MsiDownloadUrl!, progress);
            progressWindow.ShowInstalling();
            _autoUpdater.InstallAndRestart(msiPath);
        }
        catch (Exception ex)
        {
            progressWindow.Close();
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
            // Sans ca, la boite de dialogue Windows elle-meme refuse de renvoyer le
            // chemin d'un fichier verrouille (deja ouvert dans Word) avec "Le fichier
            // est en cours d'utilisation", avant meme que notre code ne s'execute —
            // empechant WordLockBridge de prendre le relais. On desactive cette
            // verification native ; c'est ParseWithWordLockFallback qui gere le cas.
            CheckFileExists = false,
        };

        if (dialog.ShowDialog() != true) return;

        LoadDocument(dialog.FileName);
    }

    private void LoadDocument(string path)
    {
        try
        {
            var (result, fromLiveWord) = ParseWithWordLockFallback(path);

            _sections.Clear();
            _articles.Clear();
            _articles.AddRange(result.Articles);

            // Un en-tete de groupe par etude (un fichier XML chacune) ; liste a plat
            // pour un document sans delimiteur d'etude.
            var view = CollectionViewSource.GetDefaultView(_sections);
            view.GroupDescriptions.Clear();
            if (_articles.Count > 0)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DocSection.Article)));
            }

            foreach (var section in result.Sections)
            {
                _sections.Add(section);
            }

            _currentDocxPath = path;
            FileNameText.Text = System.IO.Path.GetFileName(path);
            ExportButton.IsEnabled = _sections.Count > 0;

            var ignored = result.ParagraphsBeforeFirstHeading > 0
                ? $" ({result.ParagraphsBeforeFirstHeading} paragraphe(s) hors section ignore(s) — page de garde, texte des versets, etc.)"
                : string.Empty;
            var liveWordNote = fromLiveWord
                ? " (lu depuis la version actuellement ouverte dans Word, y compris modifications non enregistrees)"
                : string.Empty;
            var studies = _articles.Count > 0
                ? $"{_articles.Count} etude(s) = {_articles.Count} fichier(s) XML, "
                : string.Empty;
            StatusText.Text = $"{studies}{_sections.Count} section(s) detectee(s).{ignored}{liveWordNote}";

            if (_sections.Count == 0)
            {
                MessageBox.Show(
                    "Aucune section n'a ete trouvee dans ce document.\n" +
                    "Verifiez les styles dans Word : « Titre 1 » pour la version et le titre de l'article, " +
                    "« Titre 2 » pour les sections (ou « Titre 1 » pour les sections d'un document sans version).",
                    "Aucune section detectee", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                SectionsList.SelectedIndex = 0;
            }

            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(
                    "Points a verifier avant l'export :\n\n" + string.Join("\n", result.Warnings.Distinct()),
                    "Tableaux", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // Une etude = un fichier XML. L'ordre des sections suit celui de la liste (reordonnable).
        var studies = _articles
            .Select(a => (Article: a, Sections: _sections.Where(s => ReferenceEquals(s.Article, a)).ToList()))
            .Where(x => x.Sections.Any(s => s.Include))
            .ToList();

        var sourceDir = _currentDocxPath != null ? System.IO.Path.GetDirectoryName(_currentDocxPath) : null;
        var exports = new List<(string Path, List<DocSection> Sections)>();

        if (studies.Count > 1)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = $"Choisir le dossier des {studies.Count} fichiers XML",
                InitialDirectory = sourceDir ?? string.Empty,
            };

            if (folderDialog.ShowDialog() != true) return;

            foreach (var (article, sections) in studies)
            {
                exports.Add((System.IO.Path.Combine(folderDialog.FolderName, article.FileName), sections));
            }

            var duplicate = exports.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                MessageBox.Show(
                    $"Plusieurs etudes donnent le meme nom de fichier :\n{System.IO.Path.GetFileName(duplicate.Key)}\n\n" +
                    "Verifiez dans Word que chaque etude a une version ou un titre different.",
                    "Noms de fichier identiques", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = exports.Where(x => File.Exists(x.Path)).Select(x => System.IO.Path.GetFileName(x.Path)).ToList();
            if (existing.Count > 0)
            {
                var overwrite = MessageBox.Show(
                    "Ces fichiers existent deja et seront remplaces :\n\n" + string.Join("\n", existing) + "\n\nContinuer ?",
                    "Remplacer les fichiers", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (overwrite != MessageBoxResult.Yes) return;
            }
        }
        else
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Fichier XML (*.xml)|*.xml",
                FileName = studies.Count == 1
                    ? studies[0].Article.FileName
                    : System.IO.Path.GetFileNameWithoutExtension(FileNameText.Text) + "-sections.xml",
                InitialDirectory = sourceDir ?? string.Empty,
                Title = "Enregistrer l'export XML",
            };

            if (dialog.ShowDialog() != true) return;

            exports.Add((dialog.FileName, studies.Count == 1 ? studies[0].Sections : _sections.ToList()));
        }

        try
        {
            foreach (var (path, sections) in exports)
            {
                _exporter.Export(sections, path);

                ExportHistoryStore.Add(new ExportHistoryEntry
                {
                    Date = DateTime.Now,
                    SourceDocxPath = _currentDocxPath ?? string.Empty,
                    ExportedXmlPath = path,
                    SectionCount = sections.Count(s => s.Include),
                });
            }

            if (exports.Count == 1)
            {
                StatusText.Text = $"Export termine : {included.Count} section(s) ecrite(s) dans {exports[0].Path}";
                MessageBox.Show("Export XML termine.", "Termine", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = $"Export termine : {exports.Count} fichiers XML ({included.Count} sections) dans {System.IO.Path.GetDirectoryName(exports[0].Path)}";
                MessageBox.Show(
                    $"{exports.Count} fichiers XML exportes :\n\n" + string.Join("\n", exports.Select(x => System.IO.Path.GetFileName(x.Path))),
                    "Termine", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Echec de l'export :\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        new ExportHistoryWindow { Owner = this }.ShowDialog();
    }

    private void PreviewHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        new HtmlPreviewWindow(_selected.Title, _selected.ContentHtml) { Owner = this }.Show();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        new ThemeSettingsWindow(this) { Owner = this }.ShowDialog();
    }

    private void SectionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = SectionsList.SelectedItem as DocSection;
        _suppressEdits = true;

        TitleBox.Text = _selected?.Title ?? string.Empty;
        ContentBox.Text = _selected?.ContentHtml ?? string.Empty;
        TitleBox.IsEnabled = _selected != null;
        ContentBox.IsEnabled = _selected != null;
        PreviewHtmlButton.IsEnabled = _selected != null;

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
        MoveSelected(-1);
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(+1);
    }

    // Une section ne sort pas de son etude : chaque etude a son propre fichier XML.
    private void MoveSelected(int offset)
    {
        if (SectionsList.SelectedItem is not DocSection section) return;

        var index = _sections.IndexOf(section);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _sections.Count) return;
        if (!ReferenceEquals(_sections[target].Article, section.Article)) return;

        _sections.Move(index, target);
        SectionsList.SelectedItem = section;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "?";

        MessageBox.Show(
            $"Schilo Article Composer\nVersion {versionText}\n\n" +
            "Convertit un document Word (version, titre d'article, sections) en XML de sections\n" +
            "« paragraphe » pour Schilo Builder (schilo.org).\n\n" +
            "Auteur : Eric Philippot\n" +
            "© 2026 Eric Philippot",
            "A propos", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
