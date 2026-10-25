# trofeo-HB-dashboard

Un tableau de bord modulaire pour l'écran LCD USB **Thermalright Trofeo Vision 9.16"** (1920 × 462), sous Windows.

On place des modules sur un damier de 16 × 4 cases : heure et météo, monitoring du PC, réseau, musique en cours, sessions Claude Code ou Codex… Chacun choisit les modules qu'il garde, leur taille, le thème et le fond d'écran.

> 🚧 **Projet en construction.** Les fondations et six modules sont faits : Heure et météo, Monitoring PC, En écoute, Réseau, Claude Code et Codex. Le logiciel de réglage, les thèmes et le fond d'écran arrivent dans les prochaines versions.

## Ce qui marche aujourd'hui

- Connexion directe à l'écran en WinUSB, sans le logiciel TRCC et sans pilote à installer.
- Envoi plafonné à 12 images par seconde. L'écran valide chaque image avant la suivante.
- Reprise automatique :
  - débranchement et rebranchement de l'USB ;
  - veille puis réveil du PC ;
  - écran figé par un autre programme.
- Renvoi de l'image chaque seconde, sinon l'écran repasse au logo Thermalright au bout d'environ 3 s.
- Orientation 0° ou 180°, selon le sens de montage.
- Six modules. Par défaut, Claude Code occupe les colonnes 0 à 7 et les quatre modules de données les colonnes 8 à 15 :
  - **Heure et météo** : date, heure en 24 h ou 12 h, météo du lieu choisi ;
  - **Monitoring PC** : CPU, GPU, mémoire et températures ;
  - **En écoute** : titre, pochette et progression du média en cours dans Windows ;
  - **Réseau** : débit instantané de la carte réseau du PC, d'une Freebox ou d'une console UniFi ;
  - **Claude Code** : un onglet par session ouverte (appli desktop ou terminal), au nom du projet, avec un point orange, jaune ou gris selon ce que fait la session, le flux de son activité, une mascotte animée, des confettis à la fin d'une réponse et un drapeau quand Claude te pose une question ;
  - **Codex** : le même module pour Codex, avec sa mascotte en pixel art. Il n'est pas dans la disposition par défaut, qui remplit déjà les 16 colonnes : pour l'avoir, remplace Claude Code (colonnes 0 à 7) par Codex dans `layout.json`, service arrêté (voir plus bas).
- Raccourcis clavier globaux pour changer d'onglet : `Ctrl+Alt+←` et `Ctrl+Alt+→` pour l'onglet précédent ou suivant, `Ctrl+Alt+Maj+1` à `Ctrl+Alt+Maj+9` pour aller directement à un onglet (avec Maj : `Ctrl+Alt+chiffre` est `AltGr+chiffre` sur un clavier AZERTY et empêcherait de taper `~`, `#`, `{`, `[`, `|`, `` ` ``, `\` et `^`). Une combinaison déjà prise par une autre application est simplement laissée de côté (elle est notée dans le journal).
- Icône dans la zone de notification, qui indique l'état de l'écran.

## Ce qui arrive

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

Le service s'installe dans la zone de notification. La disposition est enregistrée dans `%AppData%\trofeo-HB-dashboard\layout.json`, et le journal dans `%AppData%\trofeo-HB-dashboard\logs\display.log`. Un `layout.json` écrit par une version précédente ne contient pas Claude Code ; pour obtenir les cinq modules par défaut, supprime le fichier pendant que le service est arrêté, il est recréé au prochain lancement (tes réglages de météo, de réseau ou de raccourcis sont alors à refaire).

L'outil `tools/Trofeo.HardwareCheck` sert à vérifier un écran :

- `--orientation 0` ou `--orientation 180` : affiche une mire pour trouver le bon sens ;
- `--fps` : mesure le débit réel ;
- `--recovery` : teste la reprise d'un écran figé ;
- `--set-secret <portée> <clé>` : enregistre un secret (mot de passe, jeton) dans le coffre du service. La valeur se lit sur l'entrée standard, saisie masquée au clavier, et n'est jamais affichée. Une valeur vide ou faite d'espaces est refusée (code de sortie 1) ;
- `--clear-secret <portée> <clé>` : efface un secret du coffre et affiche le nom de la clé effacée, jamais une valeur. Pour faire de nouveau confiance à une console UniFi dont le certificat a été renouvelé, efface son empreinte mémorisée avec `--clear-secret network unifi.certSha256.<hôte en minuscules>` (l'hôte de `layout.json` écrit en minuscules, avec son port s'il y en a un), puis relance le service : la prochaine connexion mémorise le nouveau certificat ;
- `--city <nom>` : affiche les cinq villes proposées par Open-Meteo, avec leurs coordonnées, pour régler la météo ;
- `--claude-statusline install <chemin de Trofeo.StatusLine.exe>`, `uninstall` ou `status` : branche, débranche ou décrit la barre d'état de Claude Code (voir plus bas).

En attendant le logiciel de réglage, les modules se règlent à la main dans `layout.json`, service arrêté :

- **Météo** : dans les réglages du module Heure, `"location":{"name":"Lyon","latitude":45.76,"longitude":4.84}`, avec les coordonnées données par `--city`.
- **Codex à la place de Claude Code** : dans `Placements`, supprime l'objet dont `TypeId` vaut `"claudecode"` et ajoute celui-ci (sépare-le des autres objets par une virgule ; `InstanceId` est un GUID quelconque, par exemple celui que donne `[guid]::NewGuid()` dans PowerShell). Sans cette suppression, les deux modules se chevauchent et le service retombe sur la disposition par défaut, en gardant une copie `layout.json.bad`.

  ```json
  {
    "InstanceId": "7e0b1c52-3d4a-4f6e-8a19-5c2d9b3e6f40",
    "TypeId": "codex",
    "Column": 0,
    "Row": 0,
    "Columns": 8,
    "Rows": 4,
    "TextSize": "Normal",
    "Settings": { "view": "feed", "autoFollow": true }
  }
  ```
  Réglages du module Claude Code / Codex (tous facultatifs) :

  - `view` : `feed` (le fil d'activité, par défaut) ou `overview` (l'action en cours et les tâches) ;
  - `autoFollow` : `true` (par défaut) pour que l'onglet affiché passe à la session qui se met à t'attendre ;
  - `feedFade` : `true` pour que les lignes du fil pâlissent en vieillissant, `false` (par défaut) pour les garder toutes à pleine intensité ;
  - `feedNewest` : `top` (par défaut) pour que la ligne la plus récente soit en haut et que le fil descende, `bottom` pour qu'elle soit en bas et que le fil monte, comme un terminal.

  Le module Codex exclut les sessions techniques de sous-agents (dont `guardian`). Les onglets utilisent les titres de
  `~/.codex/session_index.jsonl` quand cet index existe ; sinon, la première demande lisible ou le dossier de travail.
  Les messages Desktop utilisateur et assistant apparaissent dans le fil, sous forme d'extraits d'une ligne, en plus des
  appels d'outils. Les instructions injectées et le raisonnement interne n'y sont pas affichés. La durée mesure le
  traitement de la demande et reste figée à sa fin ou à son interruption (ce n'est pas la durée d'une commande shell).
  Le module reste une vue des sessions locales récentes : au plus 12 conversations modifiées depuis moins de 2 h,
  recherchées dans les dossiers des 3 derniers jours ; ce n'est pas l'historique complet de l'application.
  Les commandes littérales encapsulées dans l'outil Desktop `exec` sont également lues (PowerShell, Bash, CMD et
  modifications de fichiers avec compteurs de lignes). Cette lecture ne réexécute jamais le JavaScript ni les commandes.
  Au démarrage, si le modèle est absent de la portion relue, le module recherche le dernier `turn_context` du même
  fichier (16 Mio au plus), sans rejouer son fil ni emprunter le modèle d'une autre conversation.
  Pour Codex, le contexte et les quotas « 5 h » / « Hebdo » affichent la part **restante**, barres comprises
  (30 % consommés donnent 70 % restants). Les heures de remise à zéro ne changent pas ; une valeur inconnue ou
  un quota périmé reste « — ». Le module Claude Code conserve son affichage de la part consommée.

- **Freebox** : dans les réglages du module Réseau, `"source":"freebox"`. Au premier lancement, le module demande d'appuyer sur la flèche de la Freebox pour autoriser le dashboard.
- **UniFi** : `"source":"unifi","unifi":{"host":"192.168.1.1"}`, puis la clé API de la console avec `--set-secret network unifi.apiKey`.

## Quotas de Claude Code

Le module lit les sessions Claude Code sans rien installer. Pour afficher en plus les quotas (fenêtre de 5 h et hebdomadaire) et le remplissage exact du contexte, il faut que Claude Code écrive ses chiffres dans `~/.claude/trofeo-HB-dashboard/statusline`, un fichier par session, que le module relit. Tant que rien n'y est écrit, le module affiche « Quotas indisponibles ». Deux façons de le faire, qui écrivent le même fichier :

- **le mod `trofeo-quotas`** (conseillé) : il marche dans l'appli desktop **et** dans le terminal ;
- **la barre d'état `Trofeo.StatusLine.exe`** : elle ne marche que dans le terminal, car l'appli desktop ne lance pas la commande de barre d'état.

Lance ces commandes **toi-même, dans ton propre terminal** : elles modifient les réglages de Claude Code, ce n'est pas à un assistant de le faire à ta place.

### Le mod (appli desktop et terminal)

Le mod est dans `mods/trofeo-quotas`. À chaque mesure de Claude Code (après chaque réponse, ou quand un quota bouge d'un point), il écrit les chiffres de la session : rien d'autre n'est lu, aucune connexion n'est ouverte. Depuis la racine du dépôt :

```powershell
claude plugin marketplace add "$PWD\mods"
claude plugin install trofeo-quotas@trofeo-hb-dashboard
```

La portée par défaut est « user » : le mod se charge alors dans toutes les nouvelles sessions, celles de l'appli desktop comprises. Il est lu directement dans `mods/trofeo-quotas` : après un `git pull`, `/reload-plugins` suffit. Pour le retirer : `claude plugin uninstall trofeo-quotas@trofeo-hb-dashboard`.

### La barre d'état (terminal seulement)

Après `dotnet build -c Release`, depuis la racine du dépôt :

```powershell
tools\Trofeo.HardwareCheck\bin\Release\net10.0-windows10.0.19041.0\Trofeo.HardwareCheck.exe --claude-statusline install "$PWD\src\Trofeo.StatusLine\bin\Release\net10.0\Trofeo.StatusLine.exe"
```

`$PWD` donne le chemin complet de `Trofeo.StatusLine.exe`, qui doit rester à sa place tant que la barre d'état est branchée.

- La commande écrit dans `settings.json` (dans `%CLAUDE_CONFIG_DIR%` s'il est défini, sinon `~/.claude`) et en garde une copie d'origine, `settings.json.trofeo-bak`. Le fichier est réécrit sans ses éventuels commentaires, qui restent dans la copie.
- Si tu avais déjà une barre d'état, elle est conservée : Trofeo.StatusLine la précède dans un tube et lui passe les mêmes données, donc elle s'affiche toujours. Si cette barre se termine par un commentaire shell (`# …`), la commande te prévient : retire ce commentaire.
- Si tu déplaces `Trofeo.StatusLine.exe`, relance la même commande avec le nouveau chemin : elle met à jour la commande sans rien casser.
- `--claude-statusline status` dit si elle est branchée et où se trouve `settings.json`.
- Pour la retirer : `Trofeo.HardwareCheck.exe --claude-statusline uninstall`. Ta barre d'état d'origine est alors restaurée.

## Températures du processeur

Sans réglage, le module Monitoring affiche la ligne « Temp. CPU : installe le pilote ». Windows ne laisse lire ces températures qu'à un programme élevé, grâce à un pilote. Pour les activer :

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
| `src/Trofeo.StatusLine` | La barre d'état de Claude Code (quotas) et son installation |
| `mods/trofeo-quotas` | Le mod Claude Code qui écrit les quotas, appli desktop comprise |
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

Mascottes : redessins de fan de Clawd (Anthropic) et du pet Codex (OpenAI). Projet non affilié à Anthropic, OpenAI ni Thermalright.

La police Barlow Semi Condensed est distribuée sous licence SIL Open Font License 1.1 (voir `src/Trofeo.Core/Assets/Fonts/OFL.txt`).

## Licence

[GPL-3.0](LICENSE).

## Avertissement

Projet de fan, non affilié à Anthropic, OpenAI ni Thermalright. Les noms et marques cités appartiennent à leurs propriétaires.
