using System.IO;
using System.Net;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SchiloArticleComposer;

// Apercu du rendu HTML d'une section, avec le vrai CSS de schilo.org (charge en
// direct depuis le site, via les feuilles de style de schilo-theme) plutot qu'une
// approximation locale : reste a jour automatiquement si le theme evolue, au prix
// d'avoir besoin d'une connexion internet pour l'apercu.
//
// Structure DOM copiee du rendu reel d'un article (verifiee sur schilo.org le
// 2026-09-18, section type "paragraphe") :
//   <div class="schilo-container schilo-single-layout"><div class="schilo-single-main">
//     <div class="schilo-post-sections schilo-post-per">
//       <section class="schilo-section schilo-section-paragraphe schilo-per schilo-per-paragraphe schilo-migrated">
//         <h2 class="schilo-section-title">{Titre}</h2>
//         <div class="schilo-section-content">{ContentHtml}</div>
//       </section>
//     </div>
//   </div></div>
//
// Necessite le WebView2 Runtime (present par defaut sur Windows 11 et installe
// automatiquement avec Edge sur Windows 10) : PAS bundle par cette app.
public partial class HtmlPreviewWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly string[] StylesheetUrls =
    {
        "https://schilo.org/wp-content/themes/schilo-theme/inc/builder/assets/front/builder-front.css",
        "https://fonts.googleapis.com/css2?family=Inter:wght@400;500&family=Lora:ital,wght@0,400;0,500;1,400&display=swap",
        "https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@2.44.0/tabler-icons.min.css",
        "https://schilo.org/wp-content/themes/schilo-theme/assets/css/compat.css",
        "https://schilo.org/wp-content/themes/schilo-theme/style.css",
        "https://schilo.org/wp-content/themes/schilo-theme/assets/css/responsive.css",
        "https://schilo.org/wp-content/themes/schilo-theme/assets/css/single.css",
        "https://schilo.org/wp-content/themes/schilo-theme/assets/css/usx-integration.css",
    };

    public HtmlPreviewWindow(string sectionTitle, string contentHtml)
    {
        InitializeComponent();
        Title = $"Aperçu HTML — {sectionTitle}";
        Loaded += async (_, _) => await LoadPreviewAsync(sectionTitle, contentHtml);
    }

    private async Task LoadPreviewAsync(string sectionTitle, string contentHtml)
    {
        try
        {
            // Le dossier de profil WebView2 par defaut (a cote de l'exe) n'est pas
            // forcement inscriptible (installation dans Program Files/AppData\Programs) ->
            // E_ACCESSDENIED. On force un dossier ecrivable sous %LocalAppData%, coherent
            // avec AppSettings/PresetStore/ExportHistoryStore.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Schilo Article Composer", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            await PreviewBrowser.EnsureCoreWebView2Async(environment);
            PreviewBrowser.CoreWebView2.NavigateToString(BuildHtmlDocument(sectionTitle, contentHtml));
            StatusText.Text = "Apercu charge avec le CSS en direct de schilo.org (necessite une connexion internet).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Impossible de charger l'apercu.";
            MessageBox.Show(
                "L'apercu HTML necessite le WebView2 Runtime (present par defaut sur Windows 11) " +
                "et une connexion internet pour charger le style de schilo.org.\n\n" +
                $"Detail : {ex.Message}",
                "Apercu indisponible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string BuildHtmlDocument(string sectionTitle, string contentHtml)
    {
        var links = string.Join("\n    ", StylesheetUrls.Select(url => $"<link rel=\"stylesheet\" href=\"{url}\" />"));
        var titleHtml = WebUtility.HtmlEncode(sectionTitle);

        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8" />
              {{links}}
            </head>
            <body class="wp-singular post-template-default single single-post single-format-standard wp-theme-schilo-theme">
              <main>
                <div class="schilo-container schilo-single-layout">
                  <div class="schilo-single-main">
                    <div class="schilo-post-sections schilo-post-per">
                      <section class="schilo-section schilo-section-paragraphe schilo-per schilo-per-paragraphe schilo-migrated">
                        <h2 class="schilo-section-title">{{titleHtml}}</h2>
                        <div class="schilo-section-content">
                          {{contentHtml}}
                        </div>
                      </section>
                    </div>
                  </div>
                </div>
              </main>
            </body>
            </html>
            """;
    }
}
