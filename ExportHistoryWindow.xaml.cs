using System.Diagnostics;
using System.IO;
using System.Windows;
using SchiloArticleComposer.Models;
using SchiloArticleComposer.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace SchiloArticleComposer;

public partial class ExportHistoryWindow : Wpf.Ui.Controls.FluentWindow
{
    private class HistoryRow
    {
        public required ExportHistoryEntry Entry { get; init; }
        public string DateDisplay => Entry.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string SourceFileName => Path.GetFileName(Entry.SourceDocxPath);
        public string ExportedFileName => Path.GetFileName(Entry.ExportedXmlPath);
        public int SectionCount => Entry.SectionCount;
    }

    public ExportHistoryWindow()
    {
        InitializeComponent();
        var rows = ExportHistoryStore.Load().Select(e => new HistoryRow { Entry = e }).ToList();
        HistoryListView.ItemsSource = rows;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryListView.SelectedItem is not HistoryRow row)
        {
            MessageBox.Show("Selectionnez d'abord une ligne dans la liste.", "Aucune selection",
                MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(row.Entry.ExportedXmlPath))
        {
            MessageBox.Show("Ce fichier exporte n'existe plus a cet emplacement (deplace ou supprime).",
                "Fichier introuvable", MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Entry.ExportedXmlPath}\"") { UseShellExecute = true });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
