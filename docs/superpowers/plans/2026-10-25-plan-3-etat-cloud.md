# Plan 3 — état après la construction dans le cloud (tâches 0 à 9)

*Écrit le 25/10/2026 à la fin de la session cloud, pour reprendre les tâches 10 et 11 sur le PC d'Alex. Le bilan complet du plan 3 sera écrit à la fin de la tâche 11.*

## État

- **Tâches 0 à 9 construites** sur `main-zcgy1g`, de `38cbf12` (plan) à la vague de correction finale.
  - Chaque tâche a eu sa relecture, et ses corrections ont été relues à leur tour.
  - La relecture de toute la branche a donné « avec corrections », sans défaut critique. La vague de correction qui a suivi a été relue : aucun constat ne reste ouvert.
- **481 tests passent** sous Linux : `dotnet test tests/Trofeo.Modules.Agents.Tests`, avec le SDK 10.0.113 d'Ubuntu.
  - `global.json` descend à 10.0.100, avec `rollForward: latestFeature`.
  - Sur le PC d'Alex, le SDK 10.0.4xx reste choisi.
- **Pas encore fait** : tâche 10 (raccourcis clavier globaux), tâche 11 (branchement dans le service, disposition par défaut, `HardwareCheck --claude-statusline`, README, vérification sur l'écran, sobriété), puis le bilan.
- **Aperçus** : `dotnet run --project tools/Trofeo.AgentPreview -- --render <png>` (options `--view overview`, `--agent codex`, `--size 6x3|8x4|10x4`, `--text`) et `--mascot-sheet <dossier>`. Ils ont été regardés dans le cloud : texte lisible, mascotte en pixels de 4.

## Décisions prises pendant la construction

Chaque décision est suivie de son coût si elle est fausse.

- **Préalables.**
  - `Program.cs` minimaux pour les deux exécutables dès la tâche 0. Coût : nul.
  - `g` `#8C929C` ajouté à la palette Codex (le portable). Coût : une couleur.
  - Tâches 10 et 11 laissées au PC d'Alex. Coût : aucun.
- **Lecture par la fin.** La première ligne est gardée si l'octet qui précède le point de départ est un `\n`. Coût : nul.
- **Invites.** « [Request interrupted… » et les résumés de compaction ne sont pas des invites. Coût : une vraie invite commençant ainsi serait ignorée.
- **Barre d'état.**
  - Chaînage toujours entre parenthèses : `"<exe>" --tee | ( X )`. Sans elles, une commande contenant `&&`, `||` ou `;` perdait son entrée. Coût : aucune commande gênée connue.
  - Désinstallation refusée (`InvalidSettings`) si la sauvegarde de l'ancienne commande manque alors que notre commande est chaînée. Coût : désinstallation à la main depuis `settings.json.trofeo-bak`.
- **Codex.**
  - Sans début de tour visible, un événement d'activité de moins de 10 min vaut « travaille ». Coût : une session inactive vue orange jusqu'à 10 min.
  - Un `token_count` ne met pas fin à « attend ». Coût : un état jaune qui dure un événement de trop.
  - Quotas fusionnés fenêtre par fenêtre. Coût : un quota périmé affiché.
  - La méta, le `turn_context` et le `token_count` ne comptent pas comme activité. Coût : aucun connu.
- **Clawd.** Yeux en colonnes 5 et 10 ; en 6 et 11, comme le disait le plan, le visage était décentré. Coût : nul.
- **Mascotte dans la barre** : zone de 96 × 64 px au lieu de 64 × 64, soit des pixels de 4 au lieu de 2. Coût : 32 px pris aux champs.
- **Fichiers de session** relus à chaque sondage. Le cache (date, taille) risquait de manquer `busy` → `idle` sous NTFS. Coût : quelques lectures de moins de 1 Ko par seconde.
- **Fenêtre de contexte sans capture** de la session : celle de la capture la plus récente du même modèle, puis les règles du plan. Coût : un pourcentage faux tant qu'aucune session terminal n'a tourné avec ce modèle.
- **Sommeil de la mascotte** aussi après 30 min en « attend ». Coût : une mascotte immobile sur une très longue attente.

## À reprendre au début de la tâche 11

1. **Pid réutilisé.** `ClaudeSessionDirectory` garde le verdict « processus mort » tant que le pid relu est le même. Ajouter la condition « même `sessionId` ». Sinon, un nouveau Claude qui reçoit le pid d'un fichier orphelin reste caché.
2. **Comparaison de modèle.** Elle est stricte. Vérifier sur une vraie capture si la barre d'état rapporte `…[1m]` alors que la transcription n'a pas le suffixe. Si oui, normaliser.
3. **`HardwareCheck --claude-statusline`.**
   - Attraper les `IOException` d'`Install` / `Uninstall`.
   - Avertir que les commentaires de `settings.json` disparaissent : la copie `.trofeo-bak` les garde.
   - Avertir qu'une ancienne commande qui finit par `# commentaire` casse le chaînage.
4. **Exécutable déplacé.** `AlreadyInstalled` ne vérifie pas le chemin de l'exe. Si `Trofeo.StatusLine.exe` change de place, réécrire la commande en gardant le chaînage.
5. **À vérifier sous Windows** :
   - le point d'état qui suit `busy` / `idle` / `waiting` ;
   - les polices de secours ;
   - `|` et `( )` dans le shell qui lance la barre d'état (Git Bash ou cmd) ;
   - la commande de barre d'état lancée par l'appli desktop (le risque nommé par la spec) ;
   - les tests sensibles à l'horodatage du système de fichiers.

## Petits points repoussés (sans urgence)

- **Code partagé.**
  - Les petites fonctions de lecture JSON (`Str`…) et le cache du nom de projet sont recopiés entre Claude et Codex.
  - `AgentModule.cs` (≈ 530 lignes) et `AgentPainter.cs` (≈ 455) sont gros. Le choix de l'onglet pourrait devenir une classe pure.
- **Module.**
  - Aucun délai sur `source.Poll`.
  - Le texte de reset ne change pas à minuit si rien d'autre ne change.
  - Le minuteur de réveil sonne aussi pendant un clip (sans effet).
- **Codex.**
  - Un `event_msg` inconnu compte comme activité.
  - La date d'écriture d'un fichier ouvert peut être en retard sous NTFS (seuil de 2 h).
  - Deux transcriptions de même identifiant donneraient deux onglets.
  - « Aucune session » n'apparaît que 2 h après la dernière écriture.
- **Barre d'état.**
  - Lien symbolique `settings.json` remplacé par un fichier.
  - Fichier temporaire fixe de l'installeur.
- **Tests.** Quelques assertions faibles : animateur à deux threads, pixel minimal, cible de particule. Les tests ont été écrits après le code en tâche 7.
- **Dessin** (à juger par Alex sur l'écran) : l'haltère se lit comme des « cornes », et le nuage Codex ressemble plus à un écran.
