using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace SchiloArticleComposer;

public partial class InputDialog : FluentWindow
{
    public string InputText => InputBox.Text;

    public InputDialog(string prompt, string initialValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        InputBox.Loaded += (_, _) => InputBox.Focus();
    }

    public static string? Show(Window owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new InputDialog(prompt, initialValue) { Owner = owner, Title = title };
        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
