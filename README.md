# trofeo-HB-dashboard

Un tableau de bord modulaire pour l'écran LCD USB **Thermalright Trofeo Vision 9.16"** (1920 × 462), sous Windows.

On place des modules sur un damier de 16 × 4 cases : heure et météo, monitoring du PC, réseau, musique en cours, sessions Claude Code ou Codex… Chacun choisit les modules qu'il garde, leur taille, le thème et le fond d'écran.

> 🚧 **Projet en construction.** Les fondations et les quatre modules de données sont faits : Heure et météo, Monitoring PC, En écoute et Réseau. Les modules Claude Code et Codex, ainsi que le logiciel de réglage, arrivent dans les prochaines versions.

## Ce qui marche aujourd'hui

- Connexion directe à l'écran en WinUSB, sans le logiciel TRCC et sans pilote à installer.
- Envoi plafonné à 12 images par seconde. L'écran valide chaque image avant la suivante.
- Reprise automatique :
  - débranchement et rebranchement de l'USB ;
  - veille puis réveil du PC ;
  - écran figé par un autre programme.
- Renvoi de l'image chaque seconde, sinon l'écran repasse au logo Thermalright au bout d'environ 3 s.
- Orientation 0° ou 180°, selon le sens de montage.
- Quatre modules de données, placés par défaut dans les colonnes 8 à 15 :
  - **Heure et météo** : date, heure en 24 h ou 12 h, météo du lieu choisi ;
  - **Monitoring PC** : CPU, GPU, mémoire et températures ;
  - **En écoute** : titre, pochette et progression du média en cours dans Windows ;
  - **Réseau** : débit instantané de la carte réseau du PC, d'une Freebox ou d'une console UniFi.
- Icône dans la zone de notification, qui indique l'état de l'écran.

## Ce qui arrive

| Module | Contenu |
|---|---|
| Claude Code | Sessions en onglets, flux en direct, mascotte animée, contexte, quotas 5 h et hebdo |
| Codex | Le même module pour Codex, avec sa mascotte en pixel art |

Les colonnes 0 à 7 sont gardées pour Claude Code.

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

Le service s'installe dans la zone de notification. La disposition est enregistrée dans `%AppData%\trofeo-HB-dashboard\layout.json`, et le journal dans `%AppData%\trofeo-HB-dashboard\logs\display.log`. Un `layout.json` écrit par la première version ne garde que le module Heure ; pour obtenir les quatre modules par défaut, supprime le fichier pendant que le service est arrêté, il est recréé au prochain lancement.

L'outil `tools/Trofeo.HardwareCheck` sert à vérifier un écran :

- `--orientation 0` ou `--orientation 180` : affiche une mire pour trouver le bon sens ;
- `--fps` : mesure le débit réel ;
- `--recovery` : teste la reprise d'un écran figé ;
- `--set-secret <portée> <clé>` : enregistre un secret (mot de passe, jeton) dans le coffre du service. La valeur se lit sur l'entrée standard, saisie masquée au clavier, et n'est jamais affichée. Une valeur vide ou faite d'espaces est refusée (code de sortie 1) ;
- `--clear-secret <portée> <clé>` : efface un secret du coffre et affiche le nom de la clé effacée, jamais une valeur. Pour faire de nouveau confiance à une console UniFi dont le certificat a été renouvelé, efface son empreinte mémorisée avec `--clear-secret network unifi.certSha256.<hôte en minuscules>` (l'hôte de `layout.json` écrit en minuscules, avec son port s'il y en a un), puis relance le service : la prochaine connexion mémorise le nouveau certificat ;
- `--city <nom>` : affiche les cinq villes proposées par Open-Meteo, avec leurs coordonnées, pour régler la météo.

En attendant le logiciel de réglage, les modules se règlent à la main dans `layout.json`, service arrêté :

- **Météo** : dans les réglages du module Heure, `"location":{"name":"Lyon","latitude":45.76,"longitude":4.84}`, avec les coordonnées données par `--city`.
- **Freebox** : dans les réglages du module Réseau, `"source":"freebox"`. Au premier lancement, le module demande d'appuyer sur la flèche de la Freebox pour autoriser le dashboard.
- **UniFi** : `"source":"unifi","unifi":{"host":"192.168.1.1"}`, puis la clé API de la console avec `--set-secret network unifi.apiKey`.

## Températures du processeur

Sans réglage, le module Monitoring affiche la ligne « Temp. CPU : installe le pilote (voir le README) ». Windows ne laisse lire ces températures qu'à un programme élevé, grâce à un pilote. Pour les activer :

1. Installer PawnIO : `winget install --id namazso.PawnIO`.
2. Ouvrir un terminal en administrateur et lancer `Trofeo.SensorHelper.exe install`. Après `dotnet build -c Release`, l'exécutable se trouve dans `src/Trofeo.SensorHelper/bin/Release/`, sous le dossier de la version de Windows ciblée ; l'option `--pawnio <installateur>` installe PawnIO au passage. Cela crée la tâche planifiée `\trofeo-HB-dashboard\Capteurs`, qui relève les capteurs à chaque ouverture de session et les transmet au service. Le service lui-même reste sans droits d'administrateur.

La ligne d'aide disparaît dès que la température arrive, et ne s'affiche pas quand l'assistant tourne déjà.

## Organisation du code

| Dossier | Rôle |
|---|---|
| `src/Trofeo.ModuleKit` | Le contrat public des modules |
| `src/Trofeo.Core` | Damier, disposition, rendu SkiaSharp, encodage JPEG |
| `src/Trofeo.Device` | Protocole USB de l'écran (WinUSB) |
| `src/Trofeo.Display` | Le service d'affichage, dans la zone de notification |
| `src/Trofeo.Modules.*` | Les modules |
| `docs/superpowers` | Conception et plans de développement |

Les modules sont intégrés au logiciel. Pour en proposer un nouveau, ouvre une pull request avec un projet `Trofeo.Modules.<Nom>` qui implémente `IModuleFactory` et `IModule`.

## Crédits

Le protocole de l'écran a été compris grâce à ces projets :

- [Lexonight1/thermalright-trcc-linux](https://github.com/Lexonight1/thermalright-trcc-linux) (GPL-3.0)
- [sukualam/trofeo-lcd](https://github.com/sukualam/trofeo-lcd)
- [haven80/trofeo-lcd-rs](https://github.com/haven80/trofeo-lcd-rs)
- [JuliusNarvasa/TrofeoVision](https://github.com/JuliusNarvasa/TrofeoVision)
- [CraigyBabyJ/lcd_therma](https://github.com/CraigyBabyJ/lcd_therma)

Les relevés de capteurs viennent de [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), qui s'appuie sur le pilote [PawnIO](https://pawnio.eu/) pour les températures du processeur.

Données météo : [Open-Meteo.com](https://open-meteo.com/) (CC BY 4.0).

La police Barlow Semi Condensed est distribuée sous licence SIL Open Font License 1.1 (voir `src/Trofeo.Core/Assets/Fonts/OFL.txt`).

## Licence

[GPL-3.0](LICENSE).

## Avertissement

Projet de fan, non affilié à Anthropic, OpenAI ni Thermalright. Les noms et marques cités appartiennent à leurs propriétaires.
