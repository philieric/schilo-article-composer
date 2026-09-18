using System.Windows;
using System.Windows.Controls;
using SchiloArticleComposer.Services;

namespace SchiloArticleComposer;

public partial class ThemeSettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Window _mainWindow;
    private bool _initializing = true;

    public ThemeSettingsWindow(Window mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        var current = AppSettings.Load().ThemePreference;
        (current switch
        {
            ThemeManager.Light => LightRadio,
            ThemeManager.System => SystemRadio,
            _ => DarkRadio,
        }).IsChecked = true;

        _initializing = false;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        var preference = sender switch
        {
            _ when ReferenceEquals(sender, LightRadio) => ThemeManager.Light,
            _ when ReferenceEquals(sender, SystemRadio) => ThemeManager.System,
            _ => ThemeManager.Dark,
        };

        ThemeManager.Apply(preference, _mainWindow);

        var settings = AppSettings.Load();
        settings.ThemePreference = preference;
        AppSettings.Save(settings);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
