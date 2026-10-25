# trofeo-HB-dashboard — notes pour Claude

Tableau de bord modulaire pour l'écran USB Thermalright Trofeo Vision 9,16" (1920×462, rotation 180°) : .NET 10, SkiaSharp, xUnit. Alex, le propriétaire, parle français : on lui répond en français, et les textes affichés à l'écran le tutoient.

## Où on en est

- Spécification : `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`.
- Feuille de route en 6 plans : `docs/superpowers/plans/2026-10-24-plan-1-fondations.md` (3 Claude Code / Codex, 4 Apparence, 5 Logiciel de réglage, 6 Distribution).
- Plans 1 et 2 terminés, fusionnés dans `main` et vérifiés sur le vrai écran. Bilan du plan 2 : `docs/superpowers/plans/2026-10-25-plan-2-bilan.md` (dont ce qui revient au plan 5).
- Plan 3 (Claude Code / Codex) : `docs/superpowers/plans/2026-10-25-plan-3-claude-codex.md`. Tâches 0 à 9 construites dans le cloud et fusionnées dans `main`. Tâches 10 à 13 (raccourcis, branchement, options du fil, mod `trofeo-quotas`) sur la branche locale `plan-3-raccourcis-branchement`, vérifiées sur l'écran, pas encore fusionnées ni poussées.
- **Bilan du plan 3 : `docs/superpowers/plans/2026-10-25-plan-3-bilan.md`** (état, mesures, décisions, reste à faire, ce qui revient aux plans 5 et 6). Prochaine étape : fusion de la branche avec l'accord d'Alex, puis le plan 4 (Apparence).
- Les quotas de Claude Code viennent du mod `mods/trofeo-quotas` (appli desktop et terminal) ou de `Trofeo.StatusLine.exe` (terminal seulement : l'appli desktop ne lance pas la barre d'état).
- Les projets `net10.0` du plan 3 se construisent aussi sous Linux : `apt-get install dotnet-sdk-10.0`, puis `dotnet test tests/Trofeo.Modules.Agents.Tests`.

## Règles de travail

- **Commits** : adresse `1345997+Ashewos@users.noreply.github.com` (configurée dans le dépôt), jamais l'adresse personnelle d'Alex.
- **Publication** : rien n'est poussé, fusionné ni publié sans l'accord explicite d'Alex, donné pour cette action-là.
- **Sous-agents** (subagent-driven-development) : implémenteurs et relecteurs sur Sonnet, effort élevé. La session principale reprend la main quand un sous-agent coince.
- **Méthode** : spec → plan (writing-plans) → construction en sous-agents avec relecture par tâche, relecture de toute la branche, puis finishing-a-development-branch. Un bilan est écrit à la fin de chaque plan.
- **Installations élevées** (PawnIO, assistant capteurs) : c'est Alex qui les lance en administrateur.

## Pièges connus

- **Windows uniquement** : les projets ciblent `net10.0-windows` (WinForms, WinRT, LibreHardwareMonitor, WinUSB). Dans une session cloud sous Linux, la compilation et les tests des projets Windows ne marcheront probablement pas : écrire les plans et la logique indépendante de Windows là-bas, et vérifier sur le PC d'Alex (suite complète + vrai écran).
- **Application Claude empaquetée (MSIX)** sur le PC d'Alex : ce qui est lancé depuis les terminaux de Claude écrit `%AppData%` dans une copie privée (`%LocalAppData%\Packages\Claude_…\LocalCache\Roaming`). Pour changer ses vrais réglages (`%AppData%\trofeo-HB-dashboard`), lui donner une commande à lancer dans son propre terminal. Pour lire ses vrais fichiers (journal, `layout.json`), passer par `\\localhost\C$\Users\alexl\AppData\Roaming\…`. Pour lancer le service comme lui, `explorer.exe <chemin de Trofeo.Display.exe>`, avec son accord.
- Le service ne démarre pas encore tout seul avec Windows (plan 6) : Alex lance `src\Trofeo.Display\bin\Release\net10.0-windows10.0.19041.0\Trofeo.Display.exe` lui-même.
