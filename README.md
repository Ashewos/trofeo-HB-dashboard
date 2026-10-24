# trofeo-HB-dashboard

Un tableau de bord modulaire pour l'écran LCD USB **Thermalright Trofeo Vision 9.16"** (1920 × 462), sous Windows.

On place des modules sur un damier de 16 × 4 cases : heure et météo, monitoring du PC, réseau, musique en cours, sessions Claude Code ou Codex… Chacun choisit les modules qu'il garde, leur taille, le thème et le fond d'écran.

> 🚧 **Projet en construction.** Les fondations sont faites : connexion à l'écran, rendu et module Heure. Les autres modules et le logiciel de réglage arrivent dans les prochaines versions.

## Ce qui marche aujourd'hui

- Connexion directe à l'écran en WinUSB, sans le logiciel TRCC et sans pilote à installer.
- Envoi plafonné à 12 images par seconde. L'écran valide chaque image avant la suivante.
- Reprise automatique :
  - débranchement et rebranchement de l'USB ;
  - veille puis réveil du PC ;
  - écran figé par un autre programme.
- Renvoi de l'image chaque seconde, sinon l'écran repasse au logo Thermalright au bout d'environ 3 s.
- Orientation 0° ou 180°, selon le sens de montage.
- Module **Heure**, au format 24 h ou 12 h.
- Icône dans la zone de notification, qui indique l'état de l'écran.

## Ce qui arrive

| Module | Contenu |
|---|---|
| Heure et météo | Date, heure, météo du lieu choisi |
| Monitoring PC | CPU, GPU, températures, RAM… avec un titre libre et le choix des valeurs affichées |
| Réseau | Débit instantané via Freebox, UniFi ou la carte réseau du PC |
| En écoute | Titre en cours via les contrôles média de Windows |
| Claude Code | Sessions en onglets, flux en direct, mascotte animée, contexte, quotas 5 h et hebdo |
| Codex | Le même module pour Codex, avec sa mascotte en pixel art |

Également prévus :

- un logiciel de réglage en `.exe`, avec un mode « émuler » pour voir l'affichage sans l'écran ;
- les thèmes Classique, Liquid Glass et Pixel ;
- un fond d'écran en image, GIF ou vidéo ;
- la transparence des modules, le choix de la police et de la taille du texte ;
- la mise à jour en un clic depuis les releases GitHub.

## Prérequis

- Windows 10 ou 11, 64 bits.
- Un écran Trofeo Vision 9.16". Windows installe seul le pilote WinUSB.
- Fermer TRCC : un seul programme peut parler à l'écran à la fois.

## Compiler et lancer

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0). La version exacte est fixée dans `global.json`.

```bash
dotnet build -c Release
```

```bash
dotnet test
```

```bash
dotnet run -c Release --project src/Trofeo.Display
```

Le service s'installe dans la zone de notification. La disposition est enregistrée dans `%AppData%\trofeo-HB-dashboard\layout.json`, et le journal dans `%AppData%\trofeo-HB-dashboard\logs\display.log`.

L'outil `tools/Trofeo.HardwareCheck` sert à vérifier un écran :

- `--orientation 0` ou `--orientation 180` : affiche une mire pour trouver le bon sens ;
- `--fps` : mesure le débit réel ;
- `--recovery` : teste la reprise d'un écran figé.

## Organisation du code

| Dossier | Rôle |
|---|---|
| `src/Trofeo.ModuleKit` | Le contrat public des modules |
| `src/Trofeo.Core` | Damier, disposition, rendu SkiaSharp, encodage JPEG |
| `src/Trofeo.Device` | Protocole USB de l'écran (WinUSB) |
| `src/Trofeo.Display` | Le service d'affichage, dans la zone de notification |
| `src/Trofeo.Modules.*` | Les modules |
| `docs/superpowers` | Conception et plans de développement |

Les modules sont intégrés au logiciel. Pour en proposer un nouveau, ouvrez une pull request avec un projet `Trofeo.Modules.<Nom>` qui implémente `IModuleFactory` et `IModule`.

## Crédits

Le protocole de l'écran a été compris grâce à ces projets :

- [Lexonight1/thermalright-trcc-linux](https://github.com/Lexonight1/thermalright-trcc-linux) (GPL-3.0)
- [sukualam/trofeo-lcd](https://github.com/sukualam/trofeo-lcd)
- [haven80/trofeo-lcd-rs](https://github.com/haven80/trofeo-lcd-rs)
- [JuliusNarvasa/TrofeoVision](https://github.com/JuliusNarvasa/TrofeoVision)
- [CraigyBabyJ/lcd_therma](https://github.com/CraigyBabyJ/lcd_therma)

La police Barlow Semi Condensed est distribuée sous licence SIL Open Font License 1.1 (voir `src/Trofeo.Core/Assets/Fonts/OFL.txt`).

## Licence

[GPL-3.0](LICENSE).

## Avertissement

Projet de fan, non affilié à Anthropic, OpenAI ni Thermalright. Les noms et marques cités appartiennent à leurs propriétaires.
