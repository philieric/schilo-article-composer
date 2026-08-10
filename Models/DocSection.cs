using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SchiloArticleComposer.Models;

public class DocSection : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _contentHtml = string.Empty;
    private bool _include = true;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string ContentHtml
    {
        get => _contentHtml;
        set { _contentHtml = value; OnPropertyChanged(); OnPropertyChanged(nameof(WordCount)); }
    }

    public bool Include
    {
        get => _include;
        set { _include = value; OnPropertyChanged(); }
    }

    public int WordCount
    {
        get
        {
            var text = System.Text.RegularExpressions.Regex.Replace(ContentHtml, "<[^>]+>", " ");
            return text.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }

    public string Summary => $"{Title}  ({WordCount} mots)";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
