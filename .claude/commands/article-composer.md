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

**Portée actuelle : seulement le générateur XML.** L'import côté thème
(bouton "Importer XML" dans l'éditeur Schilo Builder, remplacement des
sections `paragraphe` existantes en laissant les autres types — `liens-articles`,
`conclusion`, etc. — intacts) est une décision déjà prise mais **pas encore
construite** ; ce sera un chantier séparé, côté dépôt `schilo-theme`, quand
Eric sera prêt.

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
  installer/Product.wxs          — installeur WiX (voir section 3)
  installer/license.rtf
```

Dépendance clé : `DocumentFormat.OpenXml` (NuGet) — ne jamais tenter de
parser le XML du docx à la main, la lib gère les styles hérités/`BasedOn`.

## 2. Build & run

```bash
cd C:\Users\Eric\source\repos\SchiloArticleComposer
dotnet build -c Release
```

Publier un exécutable autonome (léger, ~9 Mo, nécessite le runtime .NET 8
Desktop déjà présent sur la machine d'Eric) :

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

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

Dépôt mono-branche `main` pour l'instant (pas encore de `develop`/`master`
séparés comme sur `schilo-theme` — outil solo, workflow plus simple assumé).
Pour une modification non triviale, une branche `feature/*` dédiée reste une
bonne pratique, mais ce n'est pas une règle imposée ici comme sur le thème
(voir `git-workflow` du dépôt `schilo-theme` si Eric veut aligner les deux).
