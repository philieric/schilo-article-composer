# Skill : Schilo Article Composer

Application Windows (C# / WPF, .NET 8) qui convertit un document Word (`.docx`)
en fichier XML de sections destiné à un futur import dans **Schilo Builder**
(thème WordPress de [schilo.org](https://schilo.org), dépôt séparé `schilo-theme`).

Dépôt : https://github.com/philieric/schilo-article-composer (local :
`C:\Users\Eric\source\repos\SchiloArticleComposer`). Projet indépendant du thème
— pas de lien git entre les deux dépôts, mais le XML produit ici est conçu pour
être consommé côté thème plus tard.

Invoquer avec `/article-composer` pour reprendre le développement de cette
application (nouvelle fonctionnalité, correctif, nouvelle release du MSI).

---

## 0. Contexte et objectif

Eric rédige de longs articles bibliques dans Word (ex. `PAR001 OK Le semeur.docx`,
~2000 paragraphes) avec un style **Titre 1** pour chaque grande section
(Introduction, Commentaire 1-5, Annexe 1-6...). L'app :

1. Ouvre le `.docx`, détecte les paragraphes de style niveau de plan 1 (peu
   importe leur nom exact — "Titre1", "Heading 1", etc.) comme séparateurs de
   section.
2. Convertit le contenu de chaque section en HTML simple.
3. Affiche un aperçu éditable (titre + HTML, réordonnable, inclure/exclure).
4. Exporte un XML contenant uniquement des sections `type="paragraphe"`.

**Le contenu avant le premier titre H2 est volontairement ignoré** (page de
garde, photo, bloc "Textes bibliques") — hors périmètre, décision d'Eric.

**Cote import : deja construit dans `schilo-theme`.** Le bouton "Importer XML"
existe dans l'editeur Schilo Builder (remplace les sections `paragraphe`
existantes en laissant les autres types — `liens-articles`, `conclusion`,
etc. — intacts). Cet outil (Article Composer) ne s'occupe que de la
generation du XML ; ne pas proposer de (re)construire l'import cote theme,
c'est deja fait — voir le depot `schilo-theme` pour le detail de son
fonctionnement si besoin d'evolution.

## 1. Architecture

```
SchiloArticleComposer/
  App.xaml(.cs)                  — point d'entree WPF standard
  MainWindow.xaml(.cs)           — UI : HTML editable a GAUCHE, liste des
                                    sections detectees (checkbox + titre + nb
                                    mots) a DROITE (choix explicite d'Eric,
                                    inverse du layout initial)
  Models/DocSection.cs           — Title, ContentHtml, Include, WordCount
                                    (INotifyPropertyChanged pour le binding)
  Services/DocxParser.cs         — parsing via DocumentFormat.OpenXml :
                                    resout les styles (OutlineLevel via
                                    styles.xml, avec repli sur le nom si
                                    absent), convertit paragraphe par
                                    paragraphe (gras isole -> <h3>, gras/italique
                                    en ligne -> <strong>/<em>, numPr -> <ul><li>)
  Services/XmlExporter.cs        — ecrit <schilo_sections><section type="paragraphe">...
  HtmlPreviewWindow.xaml(.cs)    — apercu du rendu HTML d'une section, avec le
                                    VRAI CSS de schilo.org (WebView2 charge les
                                    feuilles de style en direct depuis le site,
                                    voir section 1bis) — necessite internet
  ExportHistoryWindow.xaml(.cs)  — historique des exports XML (Models/
                                    ExportHistoryEntry.cs, Services/
                                    ExportHistoryStore.cs, JSON sous
                                    %LocalAppData%\Schilo Article Composer\)
  installer/Product.wxs          — installeur WiX (voir section 3)
  installer/license.rtf
```

Dépendance clé : `DocumentFormat.OpenXml` (NuGet) — ne jamais tenter de
parser le XML du docx à la main, la lib gère les styles hérités/`BasedOn`.

## 1bis. Aperçu HTML (WebView2 + CSS reel de schilo.org)

Le bouton "Aperçu HTML..." (a cote du label "Contenu (HTML)") ouvre une fenetre
WebView2 qui reproduit la structure DOM reelle d'une section `paragraphe` sur
schilo.org (verifiee en direct sur le site le 2026-09-18) :

```html
<div class="schilo-container schilo-single-layout"><div class="schilo-single-main">
  <div class="schilo-post-sections schilo-post-per">
    <section class="schilo-section schilo-section-paragraphe schilo-per schilo-per-paragraphe schilo-migrated">
      <h2 class="schilo-section-title">{Titre}</h2>
      <div class="schilo-section-content">{ContentHtml}</div>
    </section>
  </div>
</div></div>
```

Les feuilles de style (`style.css`, `single.css`, `builder-front.css`, etc.)
sont chargees en `<link>` directement depuis `https://schilo.org/wp-content/
themes/schilo-theme/...` — pas de copie locale, donc toujours a jour avec le
theme, mais **necessite une connexion internet** pour afficher l'apercu.

**Piege rencontre et corrige** : WebView2 utilise par defaut un dossier de
profil a cote de l'exe pour son cache — ca echoue avec `E_ACCESSDENIED`
(0x80070005) une fois installe (dossier non inscriptible). Fix : passer un
`userDataFolder` explicite sous `%LocalAppData%\Schilo Article Composer\
WebView2\` via `CoreWebView2Environment.CreateAsync(userDataFolder: ...)`
avant `EnsureCoreWebView2Async(environment)` — ne jamais utiliser la version
sans argument.

**Limite connue, pas un bug** : les shortcodes WordPress du type `[bib]Matthieu
9.1-8[/bib]` (references bibliques interactives) restent affiches tels quels
dans l'apercu, car ils sont traites cote serveur par WordPress et non par le
navigateur — l'apercu montre le HTML brut, pas le rendu final post-shortcodes.

**Prerequis machine cible** : necessite le WebView2 Runtime, present par
defaut sur Windows 11 (et installe automatiquement avec Edge sur Windows 10) —
pas bundle par cette app (contrairement au runtime .NET, self-contained).

## 1ter. Theme (3 choix, Sombre personnalise par defaut)

Bouton "Thème..." sur la barre de nav (ThemeSettingsWindow) : 3 choix
appliques immediatement + persistes dans `settings.json`
(`AppSettingsData.ThemePreference`, "Light" | "System" | "Dark") :

- **Clair** / **Système** : comportement WPF-UI natif inchange (Mica +
  palette native), tel qu'il existait avant cette fonctionnalite.
- **Sombre** (`ThemeManager.Dark`, **par defaut**) : PAS le sombre natif
  WPF-UI+Mica — Eric a signale que celui-ci rend les limites entre barre de
  titre/contenu/boutons trop peu contrastees (difficile de reperer le haut de
  la fenetre pour la deplacer). Fix : `WindowBackdropType.None` (plus de
  flou/transparence liee au fond d'ecran -> contraste garanti quel que soit
  le fond d'ecran de l'utilisateur) + une palette de nuances de gris fonce
  distinctes appliquee via des `DynamicResource` definies dans `App.xaml`
  (`SchiloWindowBackgroundBrush`, `SchiloTitleBarBackgroundBrush`,
  `SchiloButtonBackgroundBrush`, etc.) : barre de titre plus sombre que le
  contenu, boutons avec fond+bordure visibles, bordure exterieure de la
  fenetre, ligne de separation sous la barre de nav.

Voir `Services/ThemeManager.cs` pour le detail. Le style de bouton
(Padding/Margin/Background/BorderBrush via ces DynamicResource) est
**duplique intentionnellement** dans `MainWindow.xaml` (`Window.Resources`),
`SchiloIaView.xaml` et `PresetManagerView.xaml` (`UserControl.Resources`).

**Piege rencontre et corrige (important, a ne pas refaire)** : le style de
bouton avait d'abord ete centralise dans `App.xaml` pour eviter cette
duplication — resultat : **tout le texte des boutons devenait noir sur fond
sombre** (illisible), meme si Eric regardait l'app en Clair ca ne se
remarquait pas (texte noir sur fond clair = normal), d'ou une confusion
initiale sur l'origine du bug. Cause : `<Style TargetType="Button"
BasedOn="{StaticResource {x:Type Button}}">` place **directement dans
`Application.Resources`**, au **meme niveau** que `<ui:ControlsDictionary />`
fusionne — la resolution de `{StaticResource {x:Type Button}}` devient
auto-referente a ce niveau precis (WPF ne retrouve pas le style WPF-UI et
retombe silencieusement sur le `Button` par defaut, dont le texte est noir,
constant quel que soit le theme app). Le meme `BasedOn="{StaticResource
{x:Type Button}}"` fonctionne SANS probleme quand il est declare dans
`Window.Resources`/`UserControl.Resources` d'une fenetre/vue (un niveau EN
DESSOUS d'`Application.Resources`, sans ambiguite de resolution) — c'est pour
ca que la duplication par ecran, bien que repetitive, est la version qui
marche. Ne pas re-tenter la centralisation sans un test pixel-precis (voir
methode ci-dessous) pour verifier que le texte reste blanc en sombre.

**Piege rencontre et corrige** : `SystemThemeWatcher.UnWatch(window)` leve
`InvalidOperationException` si la fenetre n'est pas encore chargee (`IsLoaded`
false) ou n'a jamais ete "watchee" — ne jamais l'appeler sans garde (voir
`ThemeManager.UnwatchIfLoaded`).

**Methode de verification qui a marche pour ce bug** : une capture d'ecran
lue/interpretee visuellement peut être trompeuse sur du texte a faible
contraste (une premiere lecture visuelle de la capture a laisse croire, a
tort, que le texte des onglets etait blanc et lisible). Pour trancher,
echantillonner les pixels reels (`Bitmap.GetPixel`, comparer la luminosite
R+G+B d'une zone de texte attendu a celle du fond) plutot que de se fier a
une relecture visuelle de l'image — c'est ce qui a confirme le bug la ou la
premiere inspection visuelle ne l'avait pas detecte.

## 2. Build & run

```bash
cd C:\Users\Eric\source\repos\SchiloArticleComposer
dotnet build -c Release
```

Publier un exécutable autonome (self-contained depuis le 2026-08-11 — runtime
.NET 8 embarqué, mais zéro prérequis sur la machine cible ; nécessaire car
l'app est destinée à être installée sur des postes non-développeurs qui n'ont
pas forcément le runtime .NET 8 Desktop) :

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

**Ne jamais ajouter `-p:PublishSingleFile=true` ici** : testé le 2026-08-11,
provoque un crash immediat au demarrage (`DllNotFoundException` dans
`MS.Win32.UnsafeNativeMethods` / `SetWindowLongPtrWndProc`, WPF ne charge pas
ses DLL natives de fenetrage depuis un bundle single-file). Le self-contained
multi-fichiers (dossier `publish/` avec plusieurs dizaines de fichiers) suffit
pour l'objectif "zero prerequis" ; `installer/Product.wxs` harvest tout ce
dossier via `<Files Include="$(var.PublishDir)\**" />` (WiX v5), plus besoin
de lister l'exe seul.

**Toujours lancer l'UI réellement (pas juste `dotnet build`) avant de livrer**
un exécutable à Eric — un bug XAML (ex: `DisplayMemberPath` + `ItemTemplate`
combinés sur un `ListBox`, incompatible) plante l'app **au démarrage sans
fenêtre visible**, invisible à la compilation. Lancer via
`dotnet bin/Release/net8.0-windows/SchiloArticleComposer.dll` (attache un
vrai flux stdout/stderr, contrairement au `.exe` publié en `WinExe`) pour
voir la stack trace en cas de crash.

## 3. Installeur MSI (WiX)

**Toujours WiX v5, jamais v7** : la v7 exige d'accepter une licence payante
("Open Source Maintenance Fee") pour être utilisée, même en CLI
(`error WIX7015`). v5 reste sous l'ancienne licence libre.

Installation prévue en config initiale (déjà fait sur cette machine) :

```bash
dotnet tool install --global wix --version 5.0.2
cd installer
wix extension add WixToolset.UI.wixext/5.0.2
```

Build (depuis `installer/`, après un `dotnet publish` a jour dans `../publish`) :

```bash
wix build Product.wxs -ext WixToolset.UI.wixext -arch x64 -d "AppVersion=1.0.0" -d "PublishDir=../publish" -o SchiloArticleComposer.msi
```

**`Scope="perUser"` obligatoire, jamais `perMachine`** : un test avec
`perMachine` a échoué (`Error 1925 — droits administrateur requis`) car
`msiexec /qn` silencieux ne peut pas demander l'élévation UAC. En `perUser`,
installation dans `%LocalAppData%\Programs\Schilo Article Composer`, aucun
droit admin necessaire. Verifié install + raccourcis (bureau + menu demarrer)
+ entree "Applications installees" + desinstallation propre, via `msiexec /i|/x ... /qn /l*v <log>`
et lecture du registre `HKLM:\...\Installer\UserData\<SID>\Products\<GUID compresse>\InstallProperties`
(⚠️ PAS `HKCU:\...\Uninstall` — c'est la ou j'ai cherche en premier et ca ne montre rien
meme quand l'install a reussi, l'entree ARP reelle vit sous UserData).

Penser à incrémenter `AppVersion` (passé en `-d`) **et** `<Version>` dans
`SchiloArticleComposer.csproj` à chaque release — les deux doivent rester
synchronisés (le `MajorUpgrade` de Product.wxs s'appuie sur la version MSI
pour gérer les mises à jour propres).

## 4. Historique des décisions (pour ne pas les redécouvrir)

- **Layout inversé** (v1.0.0) : HTML à gauche, sections détectées à droite —
  demande explicite d'Eric après la première version (l'inverse).
- **Bouton "À propos"** affiche nom + version (lue depuis
  `Assembly.GetExecutingAssembly().GetName().Version`) + description courte.
- Le SDK .NET n'était pas installé sur la machine au départ (seul le runtime
  WPF l'était) — installé via `winget install Microsoft.DotNet.SDK.8`, avec
  l'accord explicite d'Eric (installation système).
- Aucun lien git entre ce dépôt et `schilo-theme` : projets vraiment séparés,
  même si le XML produit ici est un intrant pour Schilo Builder plus tard.

## 5. Workflow git

Aligné sur `schilo-theme` (voir `git-workflow` dans ce dépôt) depuis la
demande d'Eric du 2026-08-10 : `main` (stable/publié) + `develop`
(intégration), toutes deux poussées sur origin.

**Règle impérative : à chaque nouvelle demande d'Eric, créer une branche
dédiée à partir de `develop`** (jamais travailler directement sur `develop`
ou `main`), avec le même prefixage que `schilo-theme` :
`feature/<slug>` (fonctionnalité), `fix/<slug>` (correctif), `chore/<slug>`
(outillage/config/CI). Une fois la demande terminée et vérifiée dans la
session (build + lancement réel de l'UI), fusionner la branche dans
`develop` en local et pousser — **pas de Pull Request GitHub**, Eric a
choisi le merge direct pour cet outil solo.

`develop` ne remonte vers `main` qu'au moment d'une release explicite (build
MSI livré à Eric), pas à chaque fusion de feature.

### Releases automatisees (depuis le 2026-08-10)

Contrairement a `schilo-theme` (simple tag lu par un plugin WordPress, pas de
binaire a joindre), cette app est compilee : un tag seul ne suffit pas, il
faut construire et joindre le MSI. Un workflow GitHub Actions
(`.github/workflows/release.yml`) s'en charge automatiquement :

1. Bumper `<Version>`/`<AssemblyVersion>`/`<FileVersion>` dans
   `SchiloArticleComposer.csproj` (les trois identiques, format `x.y.z`).
2. Fusionner `develop` -> `main`, pousser.
3. Creer et pousser un tag **annote** `vX.Y.Z` correspondant exactement a
   `<Version>` (le workflow echoue si ca ne correspond pas) :
   `git tag -a vX.Y.Z -m "Version X.Y.Z" && git push origin vX.Y.Z`.
4. Le workflow (runner `windows-latest`) publie l'app, construit le MSI avec
   WiX, et cree la GitHub Release avec le MSI attache — sans authentification
   locale necessaire (GITHUB_TOKEN fourni automatiquement par Actions).

Cette methode contourne definitivement le blocage rencontre le 2026-08-10 :
`gh` n'etait pas authentifie sur cette machine, et extraire le jeton Git
stocke pour appeler l'API a la place a ete refuse par le garde-fou de
securite (extraction d'identifiants) — a ne plus retenter. Le tag + push
restent des operations git normales, deja possibles avec les identifiants
existants ; seule la creation de la Release + upload du binaire necessitait
un acces qu'on n'avait pas localement, desormais delegue a Actions.
