using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SchiloArticleComposer.Services;

// Gere les 3 modes de theme de l'app (voir AppSettingsData.ThemePreference).
//
// "Light"/"System" laissent WPF-UI gerer entierement l'apparence (Mica + palette
// native), inchange depuis le debut du projet.
//
// "Dark" est un theme sombre PERSONNALISE (pas juste ApplicationTheme.Dark de
// WPF-UI) : Eric a signale que le sombre natif + Mica rend les limites entre la
// barre de titre, le contenu et les boutons trop peu contrastees (difficile de
// reperer le haut de la fenetre pour la deplacer, boutons qui se fondent dans le
// fond). Fix : backdrop=None (plus de flou/transparence lie au fond d'ecran, donc
// contraste garanti quel que soit le fond d'ecran de l'utilisateur) + une palette
// de nuances de gris fonce distinctes appliquee via des DynamicResource (voir
// App.xaml pour les cles par defaut, neutres/transparentes en Light/System) :
// barre de titre plus sombre que le contenu, boutons avec fond+bordure visibles,
// bordure exterieure de la fenetre, ligne de separation sous la barre de nav.
public static class ThemeManager
{
    public const string Light = "Light";
    public const string System = "System";
    public const string Dark = "Dark";

    public static void Apply(string preference, Window window)
    {
        switch (preference)
        {
            case Light:
                UnwatchIfLoaded(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                SetWindowBackdrop(window, WindowBackdropType.Mica);
                ClearCustomDarkPalette();
                break;

            case Dark:
                UnwatchIfLoaded(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, true);
                SetWindowBackdrop(window, WindowBackdropType.None);
                ApplyCustomDarkPalette();
                break;

            default: // System
                ApplicationThemeManager.ApplySystemTheme();
                SetWindowBackdrop(window, WindowBackdropType.Mica);
                SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
                ClearCustomDarkPalette();
                break;
        }
    }

    // SystemThemeWatcher.UnWatch() leve une exception si la fenetre n'a jamais ete
    // "watchee" au prealable (ex: au tout premier demarrage en mode Light/Dark, ou
    // avant que la fenetre soit chargee) -> ne desinscrire que si deja charge.
    private static void UnwatchIfLoaded(Window window)
    {
        if (window.IsLoaded)
        {
            try { SystemThemeWatcher.UnWatch(window); } catch (InvalidOperationException) { /* pas surveillee */ }
        }
    }

    private static void SetWindowBackdrop(Window window, WindowBackdropType backdrop)
    {
        if (window is FluentWindow fluentWindow)
        {
            fluentWindow.WindowBackdropType = backdrop;
        }
    }

    private static void ApplyCustomDarkPalette()
    {
        var resources = Application.Current.Resources;
        resources["SchiloWindowBackgroundBrush"] = Brush(0x1B, 0x1B, 0x1D);
        resources["SchiloTitleBarBackgroundBrush"] = Brush(0x12, 0x12, 0x14);
        resources["SchiloWindowBorderBrush"] = Brush(0x3A, 0x3A, 0x3F);
        resources["SchiloWindowBorderThickness"] = new Thickness(1);
        resources["SchiloButtonBackgroundBrush"] = Brush(0x2A, 0x2A, 0x2E);
        resources["SchiloButtonBorderBrush"] = Brush(0x3F, 0x3F, 0x45);
        resources["SchiloButtonBorderThickness"] = new Thickness(1);
        resources["SchiloDividerBrush"] = Brush(0x33, 0x33, 0x3A);
    }

    private static void ClearCustomDarkPalette()
    {
        var resources = Application.Current.Resources;
        resources["SchiloWindowBackgroundBrush"] = Brushes.Transparent;
        resources["SchiloTitleBarBackgroundBrush"] = Brushes.Transparent;
        resources["SchiloWindowBorderBrush"] = Brushes.Transparent;
        resources["SchiloWindowBorderThickness"] = new Thickness(0);
        resources["SchiloButtonBackgroundBrush"] = Brushes.Transparent;
        resources["SchiloButtonBorderBrush"] = Brushes.Transparent;
        resources["SchiloButtonBorderThickness"] = new Thickness(0);
        resources["SchiloDividerBrush"] = Brushes.Transparent;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
