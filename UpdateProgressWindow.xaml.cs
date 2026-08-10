using Wpf.Ui.Controls;

namespace SchiloArticleComposer;

public partial class UpdateProgressWindow : FluentWindow
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void ReportDownloadProgress(int percent)
    {
        ProgressBarControl.IsIndeterminate = false;
        ProgressBarControl.Value = percent;
        StatusText.Text = $"Telechargement de la mise a jour... {percent}%";
    }

    public void ShowInstalling()
    {
        ProgressBarControl.IsIndeterminate = true;
        StatusText.Text = "Installation en cours. L'application va se fermer puis redemarrer automatiquement...";
    }
}
