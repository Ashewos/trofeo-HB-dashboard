# Plan 5 — état après la construction dans le cloud (tâches 0 à 14)

*Écrit le 27/10/2026 à la fin de la session cloud, pour reprendre la tâche 15 sur le PC d'Alex. Le bilan complet du plan 5 sera écrit à la fin de la tâche 15.*

## État

- **Tâches 0 à 14 construites** sur `main-zcgy1g`, partie de `Canary` en `c2cf6f4`.
  - Le plan est en `a04b517`, la dernière correction en `24f0eb1`.
  - La branche est poussée sur GitHub. Elle n'est fusionnée ni dans `Canary` ni dans `main` : cela attend l'accord d'Alex.
- **Relectures.** Chaque tâche a eu sa relecture. Les corrections demandées ont été relues à leur tour, et 7 tâches sur 15 ont eu un tour de correction.
- **Relecture de toute la branche (sur Opus).**
  - Verdict : « à fusionner après corrections ». Aucun défaut critique, 3 importants, 12 mineurs.
  - Les 3 importants et 7 mineurs sont corrigés dans une vague relue à son tour.
  - Un petit défaut d'affichage reste ouvert (voir « À reprendre sur le PC d'Alex », point 1).
- **Tests sous Linux** (SDK 10.0.113) : solution construite sans avertissement.

  | Projet de tests | Réussis | Échecs connus (Linux seulement) |
  |---|---|---|
  | Settings (nouveau, xUnit v3, Avalonia sans affichage) | 454 | — (1 test ignoré, propre à Windows) |
  | Service (nouveau : la boucle d'affichage tourne enfin sous Linux) | 137 | — |
  | Control (nouveau, vrais tubes nommés) | 23 | — |
  | Core | 454 | 9 : DPAPI (8) et `DrawText_MissingGlyphs_UsesFallback` |
  | Agents | 623 | — |
  | Clock | 136 | — |
  | Monitoring | 98 | — |
  | Network | 214 | 3 tests de rendu (polices du système) |
  | NowPlaying | 47 | — |
  | Device | 27 | — |
  | Sensors | 79 | 26 (`cmd.exe`) |
  | Display | ne démarre pas | demande le runtime Windows Desktop |

- **Aperçus de la fenêtre** : `dotnet run --project tools/Trofeo.SettingsPreview -- --out apercus-reglages`. Une centaine d'images : les cinq onglets, les états (service arrêté, mode émulé, glissement refusé…), et chaque étape de l'assistant.
- **Vignettes des thèmes** de l'onglet Personnalisation : `dotnet run --project tools/Trofeo.ThemePreview -- --thumbnails src/Trofeo.Settings/Assets/Themes`.

## Ce qui a été construit

- **`Trofeo.Service`**, bibliothèque sans WinForms : `DisplayLoop` et `ModuleRegistry` y sont déplacés. La boucle sait maintenant :
  - appliquer une nouvelle disposition à chaud. Un module dont le type et les réglages n'ont pas changé est gardé. Le thème et le fond sont gardés tant que l'apparence ne change pas ;
  - recréer un module (après une action, ou quand une clé change dans le coffre) ;
  - publier l'aperçu, c'est-à-dire le JPEG même envoyé à l'écran. Sans écran, elle compose quand même tant qu'un aperçu est demandé : c'est le mode émulé.
- **`Trofeo.Control`** : le tube `trofeo-HB-dashboard.control`, réservé à l'utilisateur courant. Requêtes : bonjour (types de modules et schémas), état, rechargement, recherche, action, redémarrage de module, aperçu.
- **Service** :
  - `ServiceControlHandler` : rechargement sans repli, et une disposition refusée laisse l'écran tel quel. Il gère aussi la mesure du processus et les raccourcis reposés à chaud ;
  - menu de l'icône « Ouvrir les réglages » (et double-clic) ;
  - User-Agent tiré de la version (0.5.0).
- **Core** :
  - modules retirés (`Shelf`) et plafond d'images (`MaxFps`) ;
  - `TryLoad` (sans repli, motif en français) ;
  - raccourcis lus un par un : un raccourci illisible n'invalide plus toute la disposition ;
  - `LayoutTransfer` (export et import) ;
  - `LayoutEditor` (déplacer, redimensionner, préréglages, ajouter, retirer, oublier) et `LayoutHistory` ;
  - `AppLocator` (trouve `Trofeo.Display.exe`, `Trofeo.Settings.exe`, `Trofeo.SensorHelper.exe` et `mods/`).
- **ModuleKit** : schéma de réglages, `IConfigurableModuleFactory` (recherche, action), `IModuleNotices`, `ModuleDescriptor.AcceptsCommands`.
- **Modules** :
  - schémas pour Heure (ville par Open-Meteo), Réseau (source, console UniFi, clé, « oublier le certificat »), Claude Code et Codex (« installer le mod des quotas »), et Monitoring (titre, lignes, carte graphique) ;
  - le Monitoring signale les lignes qui ne tiennent pas, et le relevé des capteurs liste toutes les cartes.
- **`Trofeo.Settings`** (Avalonia 12.1.4), avec l'écran en direct en haut de tous les onglets :
  - **Disposition** : damier, glisser, redimensionner, refus en rouge avec son motif, ajouter, retirer, oublier, Ctrl+Z et Ctrl+Y dans toute la fenêtre ;
  - **Personnalisation** : thèmes avec vignettes, transparence, police, fond, garde de lisibilité ;
  - **Modules** : formulaires tirés des schémas, secrets dans le coffre DPAPI, recherches, actions ;
  - **Raccourcis** ;
  - **Paramètres** : orientation, démarrage avec Windows, images par seconde, rendu réel 12 i/s, pilote des températures, consommation, export et import, « Relancer l'assistant » ;
  - **assistant de première ouverture** en 8 étapes ;
  - instance unique, journal `logs\settings.log` et filet pour les exceptions non gérées.

## Décisions prises pendant la construction

Chaque décision est suivie de son coût si elle est fausse.

- **Annulation partagée.** Ctrl+Z et Ctrl+Y valent dans toute la fenêtre et suivent un seul historique (`EditSession`). Sinon, une annulation dans Disposition aurait défait en silence un réglage d'un autre onglet. Coût : une annulation peut défaire un réglage fait dans un autre onglet, comme dans un éditeur.
- **Module gardé, coffre relu.** Un module gardé au rechargement ne relit pas son coffre. D'où la requête « redémarrer le module », envoyée après une action réussie ou après une clé changée. Coût : une requête de plus dans le protocole.
- **Fond gardé.** Le fond est gardé tant que le fond choisi et le fond par défaut du thème ne changent pas. Une vidéo ne repart donc pas de zéro à chaque déplacement de module ni à chaque changement d'opacité. Coût : une image remplacée sous le même chemin n'est reprise qu'au prochain changement de fond.
- **Raccourcis.**
  - Une lettre ou un chiffre exige Alt ou Win : Ctrl+C ou Maj+A en raccourci global casseraient la saisie partout. Coût : Ctrl+Maj+lettre refusé.
  - Une disposition dont tous les raccourcis sont illisibles n'a plus aucun raccourci, et non ceux par défaut. Coût : un ami sans raccourcis jusqu'à « Remettre les raccourcis par défaut ».
- **Import.**
  - L'import garde l'orientation de ce PC, qui dépend du montage.
  - Il garde aussi les réglages locaux des modules qui touchent une clé du coffre (Réseau). Sinon, un fichier reçu pourrait envoyer la clé UniFi vers une console choisie par son auteur. Coût : la console UniFi d'un ami n'est pas reprise.
- **Disposition illisible.** Si `layout.json` est illisible à l'ouverture, la fenêtre affiche le motif et refuse toute modification. Elle propose « Repartir de la disposition par défaut », qui copie d'abord le fichier en `layout.json.avant-reglages`. Coût : il faut corriger le fichier ou repartir du défaut.
- **Type de module inconnu.** Un type inconnu du service est écarté avec un avertissement, et le reste de la disposition s'applique. Le « Review Focus » 3 du plan parlait d'un refus complet : c'est le plan qui allait trop loin. Coût : aucun.
- **Aperçu.**
  - Une seule image peut attendre d'être affichée.
  - L'aperçu est suspendu quand la fenêtre est réduite.
  - En mode émulé, le rendu se cale sur le plus rapide des aperçus demandés (4 ou 12 i/s).
  - Coût : aucun.
- **Interface.**
  - L'étape 2 de l'assistant n'a pas de « Continuer » : « Oui » en tient lieu.
  - Les tailles du module choisi s'affichent en trois valeurs séparées, sans points médians.
  - La police est enregistrée au clic ou à Entrée : les flèches ne font que déplacer la sélection.
  - La transparence et les images par seconde sont enregistrées au lâcher du curseur.
- **Tests de la fenêtre en série.** Le parallélisme des tests de Settings est coupé. La cause est l'hygiène des tests : des `[Fact]` simples construisent la fenêtre hors du fil de l'interface. Le code de production n'est pas en cause. Coût : la suite prend 20 s. Suivi : passer ces tests en `[AvaloniaFact]`.
- **Fonds proposés au choix.** Les extensions viennent des lecteurs : png, jpg, jpeg, webp, bmp, gif, mp4, m4v, mov, wmv, avi. Coût : mkv et webm ne sont pas proposés.

## À reprendre sur le PC d'Alex (tâche 15)

1. **D'abord, une correction d'une ligne** (laissée ouverte par la dernière relecture).
   - Dans `ServiceControlHandler`, branche « même apparence », ajouter `{ BackgroundWarnings = applied.BackgroundWarnings }`, avec un test.
   - Sans elle, l'avertissement « fond de repli » peut disparaître de la fenêtre après un rechargement suivi d'un changement d'opacité, alors que le fond de repli reste à l'écran.
2. **Construire toute la solution en Release** (`dotnet build -c Release`) avant les essais. `AppLocator` préfère une sortie Release, même ancienne, à une sortie Debug récente : sinon l'icône ouvrirait une fenêtre périmée.
3. **La suite complète sous Windows**, `Display.Tests` compris : `HotkeyRouter.Replace`, `TrayIcon` et les tests Media Foundation.
4. **Sur l'écran, avec Alex** : la liste de la tâche 15 du plan. À regarder en particulier :
   - la fenêtre qui s'ouvre depuis l'icône (menu et double-clic), puis revient au premier plan au second lancement ;
   - les polices Barlow Semi Condensed SemiBold et Manrope : DirectWrite peut nommer la famille autrement ;
   - l'aperçu, avec sa rotation ;
   - le mode émulé, écran débranché ;
   - un glissement refusé, puis Ctrl+Z ;
   - le thème et la vidéo changés à chaud, sans retour au logo. Media Foundation est maintenant ouvert depuis un fil du pool au rechargement ;
   - le choix de la carte graphique. Il faut d'abord réinstaller l'assistant des capteurs : l'ancien n'envoie qu'une carte, et la liste reste vide sans explication ;
   - les raccourcis changés à chaud :
     - capturer une combinaison déjà prise par le service peut ne pas atteindre la fenêtre ;
     - un raccourci pris par une autre application n'est signalé qu'au rechargement qui le change ;
   - « Installer le mod des quotas ». Le lanceur cherche `claude` sans passer par un shell, donc un `claude.cmd` installé par npm n'est pas trouvé ;
   - « Démarrer avec Windows » (valeur `Run` de HKCU) : l'interrupteur, puis une ouverture de session ;
   - le bouton du pilote des températures (élévation par `runas`). Alex lance l'installeur PawnIO lui-même. Le code de sortie n'est pas lu : vérifier l'état « Actives. » après coup ;
   - l'export, puis l'import ;
   - « Relancer l'assistant », et les 8 étapes ;
   - la liste « Retirés » à 1200 × 760 : elle passe sous le pli ;
   - le délai après « Démarrer le service » : le panneau « ne tourne pas » peut revenir si le service met plus de 6 s à ouvrir son tube.
5. **Sobriété** (méthode du bilan 4) :
   - le service seul, fenêtre fermée : même consommation qu'avant le plan ;
   - la fenêtre ouverte, aperçu à 4 puis à 12 i/s ;
   - la mémoire de `Trofeo.Settings.exe` (environ 96 Mo dans l'aperçu Linux), et la durée d'un rechargement ;
   - fenêtre fermée, le processus disparaît.
6. **Bilan du plan 5**, puis mise à jour de `CLAUDE.md`.

## Petits points repoussés (sans urgence)

- **Protocole et service.**
  - L'écran occupé par TRCC est reconnu au mot « TRCC » dans le motif. Mieux : un état `Busy` dans le protocole.
  - Les modules sont créés et libérés sur le fil d'affichage pendant un rechargement : un module lent à se créer retarde l'image.
  - Un module retiré est libéré juste avant la publication du nouvel état : un raccourci pressé pendant ce court instant peut viser un module libéré.
  - Le premier abonné à l'aperçu coûte un rendu de plus.
  - Côté client, quelques robustesses : une réponse de plus de 8 Mo, une annulation pendant une écriture, un délai doublé pour la poignée de main.
  - Après une application qui échoue en cours de route, `_applied` peut garder un fond libéré.
- **Fenêtre.**
  - Quand le service a été lancé depuis la fenêtre, il a 6 s pour ouvrir son tube, avant que le panneau « ne tourne pas » revienne.
  - Le résultat de l'installation des capteurs n'est pas lu (code de sortie de `runas`).
  - Le champ GPU n'a pas de message quand la liste est vide.
  - L'export contient des chemins locaux : fond, ville. Il faudrait des champs « propres à ce PC ».
  - Le fichier importé est lu sans plafond de taille.
  - La copie `.avant-import` est faite même si l'import est refusé, et elle est écrasée à chaque import.
  - Les avertissements sont aiguillés d'après leur texte en français (`WarningRouter`, `IsHotkeyFailure`).
  - Les onglets cachés se rafraîchissent chaque seconde.
  - Les polices du système sont lues au démarrage, sur le fil de l'interface.
  - La ligne ambre décale le contenu quand elle apparaît.
  - Les recherches abandonnées ne sont pas annulées.
  - Une saisie non validée est perdue quand le formulaire est reconstruit (reconnexion, annulation).
- **Tests.**
  - Il manque des tests :
    - capture de la souris perdue pendant un glissement ;
    - annulation après un refus d'écriture ;
    - double-clic sur une action ou à l'étape 2 ;
    - étape de l'assistant qui disparaît ;
    - installeur PawnIO dont le chemin contient des espaces ;
    - `--thumbnails`.
  - Le test `Log_EveryHundredFrames_ShowsAverageRenderAndSendTimes` dépend du `Trace` global et du temps réel : il a échoué une fois dans une exécution groupée.
- **Petits détails** : quelques commentaires et assertions en double, une comparaison de police sensible à la casse, le style des boutons inactifs, et `EnsureFor` rangé dans Services.
