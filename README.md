# Schilo Article Composer

Application Windows (C# / WPF, .NET 8) qui convertit un document Word (`.docx`)
en fichier XML de sections, destine a l'import dans **Schilo Builder**
(theme WordPress de [schilo.org](https://schilo.org)).

## Fonctionnement

- Les paragraphes stylés en **Titre 1** (niveau de plan 1, quel que soit le nom
  du style Word) sont detectes comme separateurs de section.
- Le contenu de chaque section est converti en HTML simple :
  - paragraphe entierement en gras -> `<h3>`
  - gras / italique en ligne -> `<strong>` / `<em>`
  - listes a puces -> `<ul><li>`
  - les shortcodes `[bib]...[/bib]` deja presents dans le texte sont conserves tels quels
- Le contenu **avant** le premier titre H2 (page de garde, image, refs...) est ignore.
- Chaque section peut etre relue/editee (titre + HTML) et reordonnee avant export.
- Export XML (`<schilo_sections><section type="paragraphe">...`), uniquement
  les sections cochees.

## Compiler

Necessite le SDK .NET 8.

```bash
dotnet build -c Release
```

## Publier un exécutable autonome

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Construire l'installeur MSI

Necessite l'outil [WiX Toolset](https://wixtoolset.org/) v5 (`dotnet tool install --global wix --version 5.0.2`
puis `wix extension add WixToolset.UI.wixext/5.0.2`).

```bash
cd installer
wix build Product.wxs -ext WixToolset.UI.wixext -arch x64 -d "AppVersion=1.0.0" -d "PublishDir=../publish" -o SchiloArticleComposer.msi
```

Installation par utilisateur (pas de droits administrateur necessaires), dans
`%LocalAppData%\Programs\Schilo Article Composer`.

## Statut

Genere uniquement le fichier XML pour l'instant. L'import cote thème
(remplacement des sections de type `paragraphe` dans Schilo Builder) reste a
construire separement, dans le depot `schilo-theme`.
