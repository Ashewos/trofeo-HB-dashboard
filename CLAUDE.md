# trofeo-HB-dashboard — notes pour Claude

Tableau de bord modulaire pour l'écran USB Thermalright Trofeo Vision 9,16" (1920×462, rotation 180°) : .NET 10, SkiaSharp, xUnit. Alex, le propriétaire, parle français : on lui répond en français, et les textes affichés à l'écran le tutoient.

## Où on en est

- Spécification : `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`.
- Feuille de route en 6 plans : `docs/superpowers/plans/2026-10-24-plan-1-fondations.md` (3 Claude Code / Codex, 4 Apparence, 5 Logiciel de réglage, 6 Distribution).
- Plans 1 et 2 terminés, fusionnés dans `main` et vérifiés sur le vrai écran. Bilan du plan 2 : `docs/superpowers/plans/2026-10-25-plan-2-bilan.md` (dont ce qui revient au plan 5).
- Plan 3 (Claude Code / Codex) : `docs/superpowers/plans/2026-10-25-plan-3-claude-codex.md`. Tâches 0 à 9 construites dans le cloud et fusionnées dans `main`. Tâches 10 à 13 (raccourcis, branchement, options du fil, mod `trofeo-quotas`) faites sur le PC d'Alex, vérifiées sur l'écran, fusionnées dans `main` et poussées le 25/10/2026.
- **Bilan du plan 3 : `docs/superpowers/plans/2026-10-25-plan-3-bilan.md`** (état, mesures, décisions, reste à faire, ce qui revient aux plans 5 et 6).
- Plan 4 (Apparence) : `docs/superpowers/plans/2026-10-26-plan-4-apparence.md`, maquette `docs/superpowers/maquettes/themes-v3.html`. Tâches 0 à 8 construites dans le cloud (`2026-10-26-plan-4-etat-cloud.md`), tâches 9 à 12 (vidéo Media Foundation, branchement, VT323 pour Pixel, corrections, décodeur sobre) sur le PC d'Alex, vérifiées sur l'écran et mesurées. Aperçus : `dotnet run --project tools/Trofeo.ThemePreview -- --out apercus` ; banc vidéo : `tools/Trofeo.VideoBench`.
- **Bilan du plan 4 : `docs/superpowers/plans/2026-10-26-plan-4-bilan.md`** (sobriété par fond, décisions, ce qui revient aux plans 5 et 6, et pour après : accélération matérielle complète et refonte du verre — le projet liquidglass cité n'a pas de licence, ne pas copier son code).
- Les quotas de Claude Code viennent du mod `mods/trofeo-quotas` (appli desktop et terminal) ou de `Trofeo.StatusLine.exe` (terminal seulement : l'appli desktop ne lance pas la barre d'état).
- Sous Linux (`apt-get install dotnet-sdk-10.0`), la plupart des projets de tests se construisent et tournent, y compris les projets Windows : `dotnet test tests/Trofeo.Modules.Agents.Tests`. Voir « Pièges connus ».

## Règles de travail

- **Commits** : adresse `1345997+Ashewos@users.noreply.github.com` (configurée dans le dépôt), jamais l'adresse personnelle d'Alex.
- **Branches** : `Canary` est la branche de travail. On en part, et les contributions (PR comprises) la visent. `main` ne reçoit que les versions stables, sur demande d'Alex, après ses essais sous Windows. Ne pas supprimer `Canary`.
- **Publication** : rien n'est poussé, fusionné ni publié sans l'accord explicite d'Alex, donné pour cette action-là.
- **Sous-agents** (subagent-driven-development) : implémenteurs et relecteurs sur Sonnet, effort élevé. La session principale reprend la main quand un sous-agent coince.
- **Méthode** : spec → plan (writing-plans) → construction en sous-agents avec relecture par tâche, relecture de toute la branche, puis finishing-a-development-branch. Un bilan est écrit à la fin de chaque plan.
- **Installations élevées** (PawnIO, assistant capteurs) : c'est Alex qui les lance en administrateur.

## Pièges connus

- **Windows, mais testable sous Linux** : les projets ciblent `net10.0-windows` (WinForms, WinRT, LibreHardwareMonitor, WinUSB). Sous Linux, la plupart des projets de tests se construisent et tournent quand même (`dotnet test tests/<projet>`), grâce à `EnableWindowsTargeting` et au paquet natif Skia pour Linux, tous deux conditionnés à Linux. Échecs connus : DPAPI et police de secours (pas de polices système) dans Core et Network, `cmd.exe` dans Sensors, et Display.Tests qui demande le runtime Windows Desktop. L'écran réel, Media Foundation, WinRT et les raccourcis clavier restent à vérifier sur le PC d'Alex.
- **Application Claude empaquetée (MSIX)** sur le PC d'Alex : ce qui est lancé depuis les terminaux de Claude écrit `%AppData%` dans une copie privée (`%LocalAppData%\Packages\Claude_…\LocalCache\Roaming`). Pour changer ses vrais réglages (`%AppData%\trofeo-HB-dashboard`), lui donner une commande à lancer dans son propre terminal. Pour lire ses vrais fichiers (journal, `layout.json`), passer par `\\localhost\C$\Users\%USERNAME%\AppData\Roaming\…`. Pour lancer le service comme lui, `explorer.exe <chemin de Trofeo.Display.exe>`, avec son accord.
- **Essais d'apparence avec Alex** : lui donner la fonction de séance du bilan du plan 4 (elle arrête le service, applique `--appearance`, relance toujours), et écrire les nombres avec un point (`opacity=0.5`) : PowerShell coupe `0,5` quand une fonction relaie `@args`. Les réglages ne sont lus qu'au démarrage du service.
- Le service ne démarre pas encore tout seul avec Windows (plan 6) : Alex lance `src\Trofeo.Display\bin\Release\net10.0-windows10.0.19041.0\Trofeo.Display.exe` lui-même.
