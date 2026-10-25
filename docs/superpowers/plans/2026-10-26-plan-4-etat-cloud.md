# Plan 4 — état après la construction dans le cloud (tâches 0 à 8)

*Écrit le 26/10/2026 à la fin de la session cloud, pour reprendre les tâches 9 et 10 sur le PC d'Alex. Le bilan complet du plan 4 sera écrit à la fin de la tâche 10 (tâche 11).*

## État

- **Tâches 0 à 8 construites** sur `main-zcgy1g`, partie de `main` en `b365cb9`. Le plan est en `df14846` et le dernier correctif en `d49a3e3`.
  - Chaque tâche a eu sa relecture, et ses corrections ont été relues à leur tour.
  - La relecture de toute la branche (sur Opus) a donné « prête après corrections » : aucun défaut critique et 2 importants (orientation EXIF des photos, garde de lisibilité). Ils sont corrigés avec 7 petits points dans une vague relue à son tour. Aucun constat important ne reste ouvert.
  - **Fusionné dans `main` le 26/10/2026** à la demande d'Alex, pour les essais sur son PC.
- **Tests sous Linux**, avec le SDK 10.0.113 d'Ubuntu. Depuis la tâche 0, la plupart des projets de tests Windows tournent aussi sous Linux.

  | Projet de tests | Réussis | Échecs connus (Linux seulement) |
  |---|---|---|
  | Core | 277 | 9 : `DpapiSecretVaultTests` (8) et `DrawText_MissingGlyphs_UsesFallback` |
  | Agents | 557 | — |
  | Clock | 129 | — |
  | Monitoring | 74 | — |
  | NowPlaying | 47 | — |
  | Network | 209 | 3 tests de rendu (polices du système absentes) |
  | Device | 27 | — |
  | Sensors | — | 26 (`cmd.exe`) |
  | Display | ne démarre pas | demande le runtime Windows Desktop |

  `Trofeo.Display` et `Trofeo.HardwareCheck` se construisent en Release sans avertissement. Display utilise encore l'ancienne surcharge de `Compose` : l'écran reste en Classique tant que la tâche 10 n'est pas faite.
- **Aperçus.**
  - `dotnet run --project tools/Trofeo.ThemePreview -- --out apercus` écrit 22 PNG : les trois thèmes, l'opacité à 0 et à 1, le texte petit et grand, un fond clair, et 4 images d'un GIF.
  - `-- --check` contrôle qu'aucun module ne déborde, dans chaque thème et à chaque taille de texte.
  - `-- --theme pixel --background gif:<chemin> --png x.png` produit une image à la demande.
  - L'outil vise `net10.0-windows` et tourne aussi sous Windows.

## Décisions prises pendant la construction

Chaque décision est suivie de son coût si elle est fausse.

- **Classique.**
  - `Surface` et `Track` gardent la couleur du texte à 7 % et 8 %, au lieu du blanc de la maquette. Monitoring et En écoute restent identiques au pixel près. Coût : une nuance invisible.
  - Le panneau gagne le contour intérieur de 1 px de la maquette (blanc à 6 %). Coût : un liseré à peine visible sur ton écran actuel, une valeur à retirer.
  - Dans le module Claude Code, la piste des barres passe de 16 % à 8 % (valeur de la maquette) et le fond de l'onglet actif de 8 % à 7 %. Coût : des barres un peu plus discrètes qu'au plan 3.
- **Apparence illisible.** Une erreur de type dans un champ d'`Appearance` (texte à la place d'un nombre, par exemple) fait ignorer tout `Appearance`, avec un avertissement si l'appelant en donne un. Le reste de la disposition est gardé. Un thème inconnu, une opacité hors bornes ou une police absente retombent chacun sur leur défaut. Coût : une faute de frappe fait perdre le thème choisi jusqu'à correction.
- **Fond animé.**
  - Le fichier est lu au fil de la lecture, par un flux qui permet de le remplacer ou de le supprimer pendant qu'il joue. Une seule image décodée est gardée en mémoire. Coût : à vérifier sous Windows (voir plus bas).
  - `Current` réutilise les mêmes pixels d'une image à l'autre : le compositeur ne le garde jamais d'une image à la suivante.
- **Garde de lisibilité** : elle prend la **zone la plus claire** sous le panneau, et non la moyenne que disait le plan.
  - Grille de cases d'environ 96 px ; on garde la plus claire, avec une cible de 0,35.
  - La moyenne laissait illisible un texte posé sur un halo clair.
  - Coût : des panneaux plus sombres que la maquette sur un fond très contrasté.
- **Police du fil** : elle ne concerne que le fil d'activité. La ligne « action en cours » de la vue d'ensemble garde la police du thème, comme `.td-log` dans la maquette. Coût : un appel à changer.
- **Heure** : en `Highlight` (jaune en Pixel), la température reste en `Text`, comme dans la maquette.
- **Aperçu** : le test de déterminisme du compositeur utilise `FakeModule`, et les cinq vrais modules sont contrôlés par `ThemePreview --check`. Coût : aucun.
- **Polices.**
  - Manrope Medium a été corrigée pour s'appeler « Manrope » dans le nom de famille de base (nameID 1) aussi, pour DirectWrite.
  - `EmbeddedFonts.Find` ne lève jamais d'exception.

## À reprendre sur le PC d'Alex

1. **Tâche 9 : vidéo par Media Foundation** (voir le plan). `BackgroundFactory.Create(…, video: MediaFoundationBackground.TryOpen, …)`. Quand l'hôte fournit `video`, c'est lui qui journalise l'échec : la fabrique ne dit rien de plus.
2. **Tâche 10 : branchement dans `Trofeo.Display`.**
   - `new LayoutStore(path, Trace.TraceWarning)`, pour journaliser un `Appearance` ignoré.
   - `AppearanceResolver.Resolve(…, new SkiaSystemFonts(), Trace.TraceWarning)`.
   - `BackgroundFactory.Create`, puis la surcharge `Compose(layout, modules, theme, background, now)`.
   - `DisplayLoop` doit **libérer le `FrameComposer`** (il est devenu `IDisposable` : il garde la copie floutée du fond).
   - Il doit aussi libérer le fond, appeler `Advance` à chaque `Tick`, et tenir compte de `UntilNextFrame` dans le délai.
   - `HardwareCheck --appearance` et le README, comme dans le plan.
3. **À vérifier sous Windows**, avant ou pendant la séance sur l'écran :
   - `FontsTests` : vérifié par Alex le 26/10. DirectWrite nomme « Barlow » la famille « Barlow Semi Condensed » (il retire les mots de largeur). `SkiaSystemFonts.IsSameFamily` accepte désormais ce nom raccourci, et les tests aussi. Manrope, Pixelify Sans et VT323 passent.
   - Une police listée par `SkiaSystemFonts.Families` doit être acceptée par `Match` (les noms traduits par DirectWrite peuvent différer).
   - Les fichiers `OFL-*.txt` sont à côté de `Trofeo.Display.exe` en Release.
   - Remplacer ou supprimer le GIF pendant qu'il joue (partage en suppression de Windows).
   - La suite complète, `Display.Tests` comprise.
4. **Sur l'écran, avec Alex** (la tâche 10 détaille la liste) :
   - **Pixelify Sans en petite taille** : certains chiffres ne se lisent pas bien. « 5 » se lit comme S, « 0 » comme O, « 7 » comme 1, par exemple dans « Core Ultra 7 265K », « 52 °C » et « Opus 5.5 ». C'est la police de la maquette, mais à 15–20 px sur l'écran, il faut qu'Alex juge.
   - **Pixel à opacité 0** : les libellés gris du bas de Claude Code (Contexte, Durée, Modèle) restent faibles sur le halo orange de l'Aurore. Le halo est plus petit qu'une case de la garde.
   - **Nom du premier onglet** coupé en verre et en Pixel (« thermalright-… ») : les polices sont plus larges que Barlow.
   - **Liseré du Classique** et pistes du module Claude Code plus discrètes : à confirmer.
5. **Sobriété** (tâche 10, étape 5). Mesures faites dans le conteneur Linux, à refaire sur le PC :

   | Cas | Temps par image |
   |---|---|
   | Classique | 0,9 ms |
   | Pixel | 0,8 ms |
   | Liquid Glass, Aurore fixe | 15 ms, dont 7 ms d'ombre portée |
   | Liquid Glass, GIF 1280 × 720 | 31 ms |

   L'ombre portée du verre est recalculée à chaque image alors qu'elle ne dépend que de la disposition. Si la mesure sur le PC dépasse l'objectif, la mettre en cache.

## Petits points repoussés (sans urgence)

- **Mémoire.**
  - Une très grande image PNG est décodée en entier (environ 384 Mo pour 12 000 × 8 000, brièvement) : JPEG et WebP sont réduits au décodage, pas PNG.
  - Un GIF peut garder un tampon de 64 Mo (limite à 4096 × 4096). Baisser la limite (2560 × 1440) ou décoder par lignes.
- **Barres.** `DrawBar(BarRect(…))` est répété à quatre endroits. Une aide `DrawThemedBar` éviterait qu'un futur module oublie la règle Pixel.
- **Tests.**
  - Aucun test permanent ne vérifie que Monitoring, En écoute et Heure restent identiques au pixel près en Classique : ça a été vérifié une fois, par comparaison de PNG.
  - Les tests de descripteurs de fichiers ne tournent que sous Linux.
  - Un test porte un nom trompeur : `Agents_ClassiqueQuotaBar_UsesThemeTrackRounded`.
- **Robustesse.**
  - Un rappel d'avertissement qui lève une exception sortirait de `LayoutStore.Load`.
  - Avec des placements invalides et un `Appearance` illisible, deux messages pour une même chute.
  - Le `SKManagedStream` n'est pas libéré explicitement si `SKCodec.Create` échoue (rare).
  - `Themes` lève une `NullReferenceException` si une police embarquée manque. C'est un défaut de construction, pas une donnée de l'utilisateur.
- **Divers.**
  - `GridGeometry.Inset` et `Gap` répètent `GridSpacing.Default`.
  - Barres Pixel : `MathF.Round` arrondit au pair au lieu d'arrondir en s'éloignant de zéro.
  - Un fichier d'aide (`BackgroundSupport`) est dans `IBackground.cs`.
  - Dans `ThemePreview`, `--help` écrit sur stderr et l'ordre des options est imposé.
- **Laissé de côté de la maquette** : onglets inactifs gris, trait de 4 px au-dessus du bas, cadre de la pochette en Pixel, ombre du texte sur le verre.
