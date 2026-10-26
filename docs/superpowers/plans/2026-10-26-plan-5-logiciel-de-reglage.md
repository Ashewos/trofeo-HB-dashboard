# Plan 5 — Logiciel de réglage

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Une fenêtre de réglage en Avalonia, lancée depuis l'icône du service et fermée avec sa fenêtre, qui montre l'écran en direct et permet de tout régler sans toucher à `layout.json` :
- **Disposition** : déplacer et redimensionner les modules sur le damier, en ajouter, en retirer ;
- **Personnalisation** : thème, transparence, police, fond, garde de lisibilité ;
- **Modules** : les réglages de chaque module (ville, source réseau, clé UniFi, lignes du Monitoring, GPU…) et son état ;
- **Raccourcis** et **Paramètres** (orientation, démarrage avec Windows, plafond d'images, pilote des températures, consommation, partage d'une disposition) ;
- un **assistant de première ouverture**.

Chaque changement s'applique tout de suite à l'écran, sans relancer le service.

**Architecture:**
- **Deux processus** (spec 4.2). Le service d'affichage continue de tout faire : modules, rendu, USB. Le logiciel de réglage (`Trofeo.Settings.exe`) est un processus séparé. Il ne crée aucun module et ne lit aucune source de données. Fermé, il libère toute sa mémoire, et l'écran continue de tourner.
- **Canal de contrôle** (`Trofeo.Control`) : un tube nommé local, réservé à l'utilisateur courant.
  - Les réglages y demandent l'état, l'aperçu et le rechargement de la disposition.
  - Les recherches propres à un module passent aussi par là : ville, liste des GPU, action comme « oublier le certificat ».
- **L'aperçu est l'image de l'écran.** Le service pousse aux réglages le JPEG même qu'il envoie à l'écran, avec sa rotation : c'est au pixel près ce que montre l'écran (spec 4.1). Sans écran branché, il compose quand même tant qu'un aperçu est demandé : c'est le **mode émulé**.
- **Écrire, puis recharger.**
  - Les réglages écrivent `layout.json` (écriture atomique, déjà en place), puis demandent au service de le recharger.
  - Le service le relit **sans jamais retomber sur le défaut**. Une disposition qu'il refuse laisse l'écran tel quel, et le motif revient aux réglages.
  - Sinon, il l'applique à chaud : un module dont le type et les réglages n'ont pas changé est gardé tel quel (déplacé, redimensionné), les autres sont recréés.
- **Réglages décrits par les modules** (`ModuleKit`). Chaque fabrique peut décrire ses réglages : un schéma de champs (choix, case, texte, liste ordonnée, secret, recherche, action). La fenêtre en tire un formulaire générique. Ajouter un type de module ne touche donc pas à la fenêtre (spec 6.1).
- **La logique se teste sous Linux.**
  - `DisplayLoop` et le registre des modules passent dans une bibliothèque sans WinForms (`Trofeo.Service`), dont les tests tournent sous Linux.
  - L'édition du damier est du code pur dans `Trofeo.Core`.
  - La fenêtre suit le modèle MVVM, avec des tests sans affichage (Avalonia.Headless).

**Tech Stack:**
- .NET 10, C# ;
- Avalonia 12.1.4 (Fluent, Headless.XUnit) et CommunityToolkit.Mvvm 8.4.2 ;
- SkiaSharp **3.119.4** : Avalonia 12 l'exige, tout le dépôt y passe ;
- System.IO.Pipes (`PipeOptions.CurrentUserOnly`) et System.Text.Json ;
- xUnit 2 (projets existants), xUnit v3 (`Trofeo.Settings.Tests`, imposé par Avalonia.Headless.XUnit 12), FakeTimeProvider.

**Spec:** `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`, sections 4.2, 4.3, 6.1, 8, 9.2 et 10. Maquette : `docs/superpowers/maquettes/editor-grid-v3.html`. Ses dimensions sont anciennes : le vrai écran fait 1920 × 462, et la grille réelle est celle de `GridGeometry`. Voir aussi les sections « Pour le plan 5 » des bilans des plans 2, 3 et 4.

## Global Constraints

- **Identité** : `trofeo-HB-dashboard`.
  - Tube de contrôle : `trofeo-HB-dashboard.control`.
  - Mutex des réglages : `Local\trofeo-HB-dashboard.settings`.
  - Préférences de la fenêtre : `%AppData%\trofeo-HB-dashboard\settings-app.json`.
- **Le service reste le seul à piloter l'écran et les modules.** Les réglages ne créent aucun module et n'ouvrent aucune source de données. Un plantage des réglages ne fige jamais l'écran (spec 4.2).
- **Sobriété** (spec 4.3), à mesurer sur le PC :
  - réglages fermés, le service ne paie qu'un tube en attente, sans aucun rendu ni encodage de plus ;
  - réglages ouverts, l'aperçu réutilise le JPEG de l'écran, sans second encodage ;
  - l'aperçu est limité à **4 images/s**, ou à 12 avec l'option « rendu réel 12 images/s » ;
  - un client lent perd des images de l'aperçu, il ne ralentit jamais l'écran.
- **Une disposition refusée ne change rien.** Au rechargement, le service garde la disposition en cours et ne crée pas de copie `.bad`. Il renvoie le motif en français.
- **Pas de chevauchement, taille minimum respectée** (spec 6.1). Un déplacement ou un redimensionnement refusé s'affiche en rouge et n'est pas enregistré.
- **Secrets** : jamais dans `layout.json`, jamais dans un export, jamais affichés ni journalisés. La fenêtre écrit dans le coffre DPAPI existant (`DpapiSecretVault`, qui a déjà un verrou entre processus). Un champ secret montre seulement « Enregistrée » ou « Absente ».
- **Textes** : en français, et ils **tutoient**.
  - Ils sont rangés à part (`Strings.resx`), pour l'anglais plus tard (spec 8).
  - Pas de capitales pour les titres de section, pas de « → » dans les boutons.
  - Un bouton dit ce qu'il fait, et le message qui suit reprend le même verbe.
- **Direction visuelle** : celle de l'écran (thème Classique), décrite dans « Design de la fenêtre ». Avalonia, jamais de navigateur embarqué (spec 4.1).
- **Mouvement** (spec 7.3) : courts et nets.
  - Un module glissé se cale sur la case, sans glissement doux.
  - Le refus s'affiche tout de suite.
- **Compatibilité** :
  - un `layout.json` d'avant ce plan est lu tel quel (`Shelf` et `MaxFps` absents) ;
  - les commandes `HardwareCheck` restent valables.
- `TreatWarningsAsErrors` reste actif. Licence GPL-3.0.

## Où construire

- **Tâches 0 à 14 : dans le cloud sous Linux** (`apt-get install -y dotnet-sdk-10.0`).
  - Avalonia tourne sous Linux, et ses tests aussi, en mode sans affichage.
  - Les captures de la fenêtre (`tools/Trofeo.SettingsPreview`) s'y font aussi.
- **Tâche 15 : sur le PC d'Alex.** Vrai service et vrai écran, icône de notification, démarrage avec Windows, PawnIO (élévation), raccourcis rechargés, fond vidéo changé à chaud, sobriété, puis le bilan.
- **Échecs connus sous Linux**, relevés sur `Canary` (`c2cf6f4`) avant ce plan. Tout ce qui n'y est pas doit passer.

  | Projet | Réussis | Échecs connus | Cause |
  |---|---|---|---|
  | Core.Tests | 353 | 9 | DPAPI (8) et `DrawText_MissingGlyphs_UsesFallback` (pas de polices du système) |
  | Network.Tests | 209 | 3 | tests de rendu (polices du système) |
  | Sensors.Tests | 73 | 26 | lancent `cmd.exe` |
  | Display.Tests | — | tout | demande le runtime Windows Desktop |
  | Agents, Clock, Monitoring, NowPlaying, Device | 613, 130, 75, 47, 27 | 0 | |

## Design de la fenêtre

Passé par la compétence frontend-design.

**Sujet.**
- Le panneau de réglage d'un petit écran d'ambiance monté dans un PC. C'est une bande de 1920 × 462 qu'on regarde de biais.
- Pour qui : Alex et ses amis, joueurs et développeurs qui vivent dans Claude Code.
- Sa tâche première : **arranger l'écran en le voyant**.

**Idée centrale.**
- L'écran lui-même est en haut de la fenêtre, sur toute la largeur et dans tous les onglets : une bande en direct, dans un cadre fin comme la dalle dans le boîtier.
- C'est le seul élément fort de la fenêtre :
  - dans **Disposition**, le damier s'y superpose et on y travaille directement ;
  - dans les autres onglets, on y voit l'effet de chaque réglage.
- Tout le reste est calme : listes, formulaires, alignés à gauche.

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ Trofeo   Disposition  Personnalisation  Modules  Raccourcis  Paramètres   ● Écran branché │
├──────────────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────────────────────────┐ │
│ │                    bande en direct 1920 × 462 (cadre, damier en Disposition)        │ │
│ └──────────────────────────────────────────────────────────────────────────────────┘ │
│  Glisse un module pour le déplacer, tire un bord pour l'agrandir.          Annuler ⌘Z │
├────────────────────┬─────────────────────────────────────────────────────────────────┤
│ Sur l'écran        │  Claude Code                                                    │
│  ● Claude Code 8×4 │  8 × 4 cases · 952 × 448 px · minimum 6 × 3                     │
│  ● Monitoring  4×2 │  [Compact] [Idéal] [Large]      Texte  [−] [Normal] [+]          │
│ Retirés            │  [Retirer de l'écran]                                           │
│  Codex             │                                                                  │
│ [Ajouter un module]│                                                                  │
└────────────────────┴─────────────────────────────────────────────────────────────────┘
```

**Couleurs** : celles de l'écran Classique, pour que la fenêtre et la dalle forment un seul objet.

| Jeton | Valeur | Rôle |
|---|---|---|
| `Ink` | `#0E1116` | fond de la fenêtre (le fond de l'écran) |
| `Panel` | `#151920` | colonnes et cartes |
| `Raised` | `#1C212A` | survol, élément choisi, champs |
| `Line` | `#262C37` | séparateurs, cadre de la bande |
| `Text` | `#ECEEF0` | texte |
| `Muted` | `#8C929C` | texte secondaire |
| `Accent` | `#7FD1C7` | sélection, focus, bouton principal (texte `#0E1116` dessus) |
| `Amber` | `#F2B84B` | mode émulé, avertissements |
| `Refuse` | `#E5604F` | place refusée, hors ligne |
| `Online` | `#6FD08C` | en ligne |
| `Idle` | `#5B606D` | non configuré |

**Typographie** : les polices de l'écran, déjà embarquées sous licence OFL.
- Barlow Semi Condensed SemiBold : onglets, titres, noms de modules.
- Barlow Semi Condensed Regular : chiffres (cases, pixels, Mo).
- Manrope Medium : texte courant et formulaires.
- Échelle : 12, 13, 15, 20 et 28 px.
- Les titres de section sont en casse de phrase, en `Muted`, sans capitales ni espacement de lettres.

**Formes** : rayon 10 pour le cadre de la bande, 8 pour les panneaux, 6 pour les contrôles. La hiérarchie se lit aux rayons, sans ombres empilées.

**Écarts avec la maquette `editor-grid-v3`, et pourquoi** :
- l'orange `#d97757` de la sélection devient le vert d'eau de l'écran : l'orange est la couleur de Claude, pas celle du produit ;
- les titres en capitales espacées deviennent des titres en casse de phrase ;
- l'écran passe en haut, sur toute la largeur, au lieu d'être une vignette au centre : à 1200 px de large, il fait 277 px de haut, contre environ 180.

## Review Focus

1. **Service arrêté, ou relancé pendant que la fenêtre est ouverte.**
   - La fenêtre montre « Le service d'affichage ne tourne pas » avec un bouton « Démarrer le service ».
   - Elle se reconnecte seule quand le service revient, sans planter ni perdre l'onglet en cours.

   Test `Link_ServiceRestarts_Reconnects` (tâche 9).
2. **`layout.json` modifié par quelqu'un d'autre pendant que la fenêtre est ouverte** (`HardwareCheck --appearance`, édition à la main).
   - Avant d'écrire, la fenêtre compare le fichier à celui qu'elle a lu.
   - S'il a changé, elle le relit et l'affiche, écarte sa modification et le dit : « La disposition a changé en dehors des réglages : elle a été relue. »

   Test `Document_ExternalChange_ReloadsInsteadOfOverwriting` (tâche 9).
3. **Disposition refusée par le service** (type inconnu d'un service plus ancien, fichier tronqué). L'écran reste tel quel, aucune copie `.bad` n'est faite, et la fenêtre affiche le motif. Test `Reload_InvalidFile_KeepsCurrentAndReportsReason` (tâche 8).
4. **Fond choisi qui disparaît ou ne s'ouvre pas** (vidéo déplacée, GIF corrompu). Le service retombe sur le fond du thème, et l'avertissement revient dans la réponse au rechargement, puis s'affiche sous le choix du fond. Test `Reload_MissingBackground_ReturnsWarning` (tâche 8).
5. **Glisser hors du damier, sur un voisin ou sous la taille minimum**, à l'échelle de l'aperçu. Le cadre passe au rouge, le voisin est nommé, et rien n'est enregistré. Tests `Move_OntoNeighbour_RefusedWithBlocker` (tâche 2) et `Drag_Refused_DoesNotSave` (tâche 10).

## Décisions prises en écrivant ce plan

- **Tout s'applique tout de suite, sans bouton « Enregistrer ».**
  - L'écran est l'aperçu : voir le résultat sur la vraie dalle vaut mieux qu'un brouillon.
  - Une édition qui casserait la disposition est refusée avant l'écriture.
  - Ctrl+Z annule (50 pas).

  Coût : on ne peut pas essayer en douce sans que l'écran change.
- **Aperçu = le JPEG de l'écran.**
  - Les réglages le tournent de nouveau à l'affichage, sans second encodage dans le service.
  - Inconvénient : l'aperçu montre les artefacts JPEG de l'écran. C'est voulu : c'est ce que l'écran affiche.
- **Rechargement à chaud plutôt que redémarrage du service.** Relancer le service ferait repasser l'écran au logo pendant plusieurs secondes et reconnecterait la Freebox à chaque réglage. Coût : `DisplayLoop` doit savoir changer de modules, de thème et de fond en cours de route (tâche 7).
- **Les réglages dépendent du service.** S'il ne tourne pas, la fenêtre propose de le démarrer : sans lui, pas d'aperçu, pas d'état, ni la liste des types de modules. Coût : pas de réglage « hors ligne ». C'est cohérent, puisque le service démarre avec Windows.
- **Module retiré** : il passe dans `Shelf` (« Retirés ») avec ses réglages (spec 6.1). L'ajouter de nouveau reprend ses réglages. « Oublier » le supprime pour de bon.
- **Formulaires génériques.**
  - Les réglages des modules sont décrits par un schéma dans `ModuleKit` (tâche 3) : la fenêtre ne connaît aucun module.
  - Les recherches et les actions sont faites **par le service**, qui a déjà le client HTTP, les capteurs et le coffre.
  - Coût : un réglage très particulier demandera un nouveau type de champ.
- **« Trop de contenu pour la taille »** (spec 6.1). Le module le dit lui-même par `IModuleNotices`, et la fenêtre l'affiche à côté de son état. Exemple du Monitoring : « 3 lignes sur 5 tiennent à cette taille. » Coût : seul le Monitoring le signale pour l'instant, les autres modules adaptent déjà leur contenu à la taille.
- **Choix du GPU** (bilan 2) : le relevé de capteurs donne toutes les cartes, et chaque module Monitoring choisit la sienne (`gpu`), « Automatique » par défaut. Un assistant capteurs d'avant ce plan n'envoie que la carte retenue : il faut le réinstaller pour avoir le choix, sinon on reste en « Automatique ».
- **Démarrage avec Windows** : la valeur `trofeo-HB-dashboard` sous `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, sans droits administrateur. Le plan 6 (Velopack) changera le chemin, pas le principe.
- **Pilote des températures** : le bouton lance `Trofeo.SensorHelper.exe install --pawnio <installeur>` avec élévation (`runas`). Tant que le plan 6 n'embarque pas l'installeur de PawnIO, la fenêtre demande où il se trouve.
- **Quotas de Claude Code** : une action du module (« Installer le mod des quotas »), avec une confirmation qui dit ce qui sera modifié. Elle lance `claude plugin marketplace add <dossier mods>` puis `claude plugin install trofeo-quotas@trofeo-hb-dashboard`. Le retrait de la barre d'état est laissé au plan 6.
- **Mises à jour** : le plan 6. Paramètres affiche seulement la version (`0.5.0`).
- **Textes** : `Strings.resx` est lu par une classe `Texts` écrite à la main, avec un test qui vérifie que chaque clé existe. Le générateur de ressources de Roslyn n'existe qu'en préversion.
- **xUnit v3 pour les tests de la fenêtre seulement** : Avalonia.Headless.XUnit 12 l'exige. Les autres projets restent en xUnit 2, et `tests/Directory.Build.props` reçoit l'interrupteur `UsesXunitV3`.

---

### Task 0: Socle — Avalonia possible, service testable sous Linux

**Files:**
- Modify: `src/Trofeo.ModuleKit/Trofeo.ModuleKit.csproj` : `SkiaSharp` 3.119.0 → **3.119.4**
- Modify: `tests/Directory.Build.props` :
  - `SkiaSharp.NativeAssets.Linux.NoDependencies` passe en 3.119.4 ;
  - interrupteur `UsesXunitV3`. Quand il est `true`, ni xunit 2 ni `xunit.runner.visualstudio` 3.1 ne sont ajoutés, et le paquet natif Linux non plus : Avalonia.Skia amène déjà le sien.
- Create: `src/Trofeo.Service/Trofeo.Service.csproj`, une bibliothèque `net10.0-windows10.0.19041.0` **sans** `UseWindowsForms`.
  - Elle reprend `DisplayLoop.cs` et `ModuleRegistry.cs`, déplacés depuis `src/Trofeo.Display/`, avec le même espace de noms `Trofeo.Display` pour ne rien casser.
  - Elle référence Core, Device, Sensors et les cinq projets de modules.
  - `InternalsVisibleTo` : `Trofeo.Service.Tests`, `Trofeo.Display`.
- Modify: `src/Trofeo.Display/Trofeo.Display.csproj` : référence `Trofeo.Service`.
- Create: `tests/Trofeo.Service.Tests/` : `DisplayLoopTests.cs` et `DefaultLayoutTests.cs`, déplacés depuis `tests/Trofeo.Display.Tests/`. Les tests qui restent dans Display.Tests : `HotkeyRouterTests`, `MediaFoundationBackgroundTests`.
- Modify: `trofeo-HB-dashboard.sln` (les deux nouveaux projets)
- Modify: `Directory.Build.props` : `<Version>0.5.0</Version>`

- [ ] **Step 1: Relever la base.** Lancer `dotnet test` sur chaque projet de tests de `Canary` et noter les réussites et les échecs connus. Ce tableau va dans le rapport, puis dans l'état cloud.
- [ ] **Step 2: Faire les modifications ci-dessus.** Un déplacement de fichier se fait avec `git mv`, pour garder l'historique.
- [ ] **Step 3: Vérifier.**
  - `dotnet build` de la solution : 0 avertissement.
  - `dotnet test tests/Trofeo.Service.Tests` **passe sous Linux** : ce sont les tests de `DisplayLoop`, qui ne tournaient jusqu'ici que sous Windows.
  - Les autres projets ont le même résultat qu'au step 1.
  - `dotnet run --project tools/Trofeo.ThemePreview -- --check` affiche `OK`.
- [ ] **Step 4: Commit** — `chore: SkiaSharp 3.119.4, Trofeo.Service sans WinForms (tests de la boucle sous Linux)`

---

### Task 1: Disposition — modules retirés, plafond, lecture sans repli, partage

**Files:**
- Modify: `src/Trofeo.Core/Layout/DashboardLayout.cs`, `LayoutStore.cs`
- Create: `src/Trofeo.Core/Layout/LayoutJson.cs` (lecture commune), `LayoutTransfer.cs`
- Test: `tests/Trofeo.Core.Tests/LayoutStoreTests.cs` (ajouts), `LayoutTransferTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record DashboardLayout(
    int Rotation, IReadOnlyList<ModulePlacement> Placements, IReadOnlyList<ShortcutBinding>? Shortcuts = null,
    AppearanceSettings? Appearance = null, IReadOnlyList<ModulePlacement>? Shelf = null, int? MaxFps = null);

public sealed record LayoutLoadResult(DashboardLayout? Layout, string? Error, IReadOnlyList<string> Warnings);

public sealed class LayoutStore   // ajouts
{
    /// Comme Load, sans jamais retomber sur le défaut ni copier en .bad : Layout null et Error en français.
    public LayoutLoadResult TryLoad();
    /// Empreinte du fichier (taille, date d'écriture) ; null s'il est absent.
    public LayoutStamp? Stamp();
}
public readonly record struct LayoutStamp(long Length, DateTime LastWriteUtc);

public static class LayoutTransfer
{
    public const string Format = "trofeo-HB-dashboard/disposition/1";
    public static string Export(DashboardLayout layout);       // {"Format": Format, "Layout": {...}}
    public static LayoutLoadResult Import(string json);        // même validation que TryLoad
}
```

- **Règles de lecture** :
  - `MaxFps` hors de [1, 12] vaut `null`, avec l'avertissement « Plafond d'images invalide : 12 images/s utilisées. ». Il n'y a pas de `.bad`.
  - `Shelf` : pas d'élément nul, `TypeId` non vide, `Settings` présent, `Columns` et `Rows` ≥ 1. Ses emplacements ne sont **pas** contrôlés sur le damier.
  - Les `InstanceId` sont uniques sur `Placements` et `Shelf` réunis.
  - **Raccourcis lus un par un** (bilan 3). Un raccourci illisible est écarté, avec l'avertissement « Raccourci « {Keys} » ignoré : {motif}. ». Les motifs : commande inconnue, `Keys` absent, ou `Keys` refusé par `ShortcutKeys.TryParse`. Il ne fait plus rejeter la disposition.
  - `Shortcuts` qui n'est pas un tableau vaut `null`, avec un avertissement.
- **Messages d'erreur** de `TryLoad` : « Le fichier layout.json est illisible (JSON invalide). », « Rotation {r}° refusée : 0 ou 180. », « Deux modules se chevauchent : {TypeId} et {TypeId}. », « Module hors du damier : {TypeId}. », « Identifiant de module en double. ». Fichier absent : `Layout` = `DashboardLayout.Default`, sans erreur.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Load_OldFile_HasNoShelfNoMaxFps()                 // fichier du plan 4 : Shelf null, MaxFps null
[Fact] public void SaveLoad_ShelfAndMaxFps_RoundTrip()
[Fact] public void Load_MaxFpsOutOfRange_IsNullWithWarning()         // 0 et 40 : null + avertissement exact
[Fact] public void Load_DuplicateIdAcrossShelf_IsRejected()          // Load : défaut + .bad ; TryLoad : Error, pas de .bad
[Fact] public void Load_ShelfOffGrid_IsAccepted()                    // Column 20 dans Shelf : accepté
[Fact] public void Load_OneInvalidShortcut_KeepsLayoutAndOthers()    // Command "Fly" : écarté, les autres gardés, avertissement
[Fact] public void TryLoad_Overlap_ReturnsReasonAndNoBackup()        // Error == « Deux modules se chevauchent : clock et network. », File.Exists(".bad") faux
[Fact] public void TryLoad_Missing_IsDefault()
[Fact] public void Stamp_ChangesWhenFileIsRewritten()
[Fact] public void Export_ThenImport_GivesSameLayout()
[Fact] public void Import_WrongFormat_IsRefused()                   // Error « Ce fichier n'est pas une disposition Trofeo. »
[Fact] public void Export_NeverContainsVaultContent()               // le JSON exporté n'a ni "secrets" ni "apiKey" ni "token"
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Core.Tests --filter "LayoutStore|LayoutTransfer"`. Expected : échec de compilation, puisque `Shelf`, `TryLoad` et `LayoutTransfer` n'existent pas encore.
- [ ] **Step 3: Implement.**
  - Sortir la lecture de `LayoutStore` dans `LayoutJson.Parse(string json, Action<string>? warn) → LayoutLoadResult`. `Load`, `TryLoad` et `Import` l'appellent.
  - `Load` garde exactement son comportement : défaut, puis `.bad`.
  - La lecture indulgente des raccourcis suit le modèle de celle d'`Appearance` (`JsonNode`).
- [ ] **Step 4: Run** les tests de Core. Expected : PASS, hors échecs connus.
- [ ] **Step 5: Commit** — `feat(core): modules retirés, plafond d'images, rechargement sans repli, partage de disposition`

---

### Task 2: Édition sur le damier (code pur)

**Files:**
- Modify: `src/Trofeo.Core/Layout/LayoutValidator.cs` (ajouter `NoRoom` **à la fin** de `PlacementError`), `GridGeometry.cs`
- Create: `src/Trofeo.Core/Layout/LayoutEditor.cs`, `LayoutHistory.cs`
- Test: `tests/Trofeo.Core.Tests/LayoutEditorTests.cs`, `GridGeometryTests.cs` (ajouts), `LayoutHistoryTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public enum ResizeEdge { Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// Error != None : Layout est la disposition d'entrée, inchangée ; Blocker : le module qui gêne (chevauchement).
public sealed record EditResult(DashboardLayout Layout, PlacementError Error, Guid? Blocker = null)
{
    public bool Ok => Error == PlacementError.None;
}

public sealed class LayoutEditor(IReadOnlyDictionary<string, ModuleDescriptor> types)
{
    public EditResult Move(DashboardLayout layout, Guid id, int column, int row);
    public EditResult Resize(DashboardLayout layout, Guid id, ResizeEdge edge, int deltaColumns, int deltaRows);
    public EditResult ApplyPreset(DashboardLayout layout, Guid id, SizePreset preset);
    /// Reprend le dernier module retiré de ce type (ses réglages, sa taille de texte), sinon en crée un avec newId et "{}".
    public EditResult Add(DashboardLayout layout, string typeId, Guid newId);
    public EditResult Remove(DashboardLayout layout, Guid id);           // vers la fin de Shelf
    public EditResult Forget(DashboardLayout layout, Guid shelvedId);     // retire de Shelf pour de bon
    public EditResult SetTextSize(DashboardLayout layout, Guid id, TextSize size);
    public EditResult SetSettings(DashboardLayout layout, Guid id, JsonElement settings); // placés ou retirés
}

public static class GridGeometry   // ajouts
{
    /// Case sous un point de l'écran (pixels 1920 × 462, non tourné), bornée au damier.
    public static (int Column, int Row) CellAt(float x, float y, GridSpacing spacing);
    /// Bord de colonne (0 à 16) ou de rangée (0 à 4) le plus proche d'un point : sert au redimensionnement.
    public static int NearestColumnEdge(float x, GridSpacing spacing);
    public static int NearestRowEdge(float y, GridSpacing spacing);
}

public sealed class LayoutHistory(int capacity = 50)
{
    public void Push(DashboardLayout before);
    public DashboardLayout? Undo(DashboardLayout current);   // null si rien à annuler
    public DashboardLayout? Redo(DashboardLayout current);
    public bool CanUndo { get; }
    public bool CanRedo { get; }
}
```

- **Règles** :
  - `Resize` déplace le ou les bords nommés de `delta` cases. Par exemple `Left, -1` agrandit d'une colonne vers la gauche, et `BottomRight, +1, +1` grandit vers le bas à droite.
  - `ApplyPreset` garde le coin haut-gauche. Si la taille sort du damier, le module est poussé vers la gauche ou vers le haut, juste assez pour tenir.
  - `Add` essaie la taille Idéal, puis Compact, puis la taille minimum. Pour chaque taille, il cherche la première place libre, rangée par rangée puis colonne par colonne. Sinon : `NoRoom`.
  - Type inconnu : `UnknownType`.
  - Le blocage nommé est le premier module qui chevauche, dans l'ordre de `Placements`.

- [ ] **Step 1: Write the failing tests** (sur `DashboardLayout.Default` et un registre de descripteurs de test reprenant les tailles réelles : claudecode min 6×3, monitoring min 3×2, clock min 3×1…)

```csharp
[Fact] public void Move_ToFreeSpot_Moves()
[Fact] public void Move_OntoNeighbour_RefusedWithBlocker()      // clock sur monitoring : Overlap, Blocker == id du monitoring, Layout inchangé (Same)
[Fact] public void Move_OffGrid_IsOutOfBounds()
[Fact] public void Resize_LeftEdgeGrowsLeft()
[Fact] public void Resize_BelowMin_IsRefused()
[Fact] public void ApplyPreset_AtRightEdge_ShiftsLeftToFit()
[Fact] public void Add_ShelvedType_RestoresItsSettings()        // Remove puis Add : même InstanceId, mêmes Settings, sorti de Shelf
[Fact] public void Add_FullGrid_IsNoRoom()
[Fact] public void Add_TriesIdealThenCompactThenMin()
[Fact] public void Remove_KeepsSettingsInShelf()
[Fact] public void Forget_RemovesFromShelf()
[Theory] [InlineData(0f, 0f, 0, 0)] [InlineData(1919f, 461f, 15, 3)] [InlineData(-50f, 900f, 0, 3)]
public void CellAt_MapsAndClamps(float x, float y, int col, int row)
[Fact] public void NearestColumnEdge_SnapsToClosestBoundary()   // à mi-chemin entre deux bords : le plus proche, 16 au bord droit
[Fact] public void History_UndoRedo_AndCapacity()                // 51 Push : le plus ancien est perdu
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Core.Tests --filter "LayoutEditor|GridGeometry|LayoutHistory"`. Expected : FAIL.
- [ ] **Step 3: Implement.** Chaque opération construit la disposition candidate, puis passe par `LayoutValidator.Check`. Aucune opération ne modifie l'entrée.
- [ ] **Step 4: Run.** Expected : PASS.
- [ ] **Step 5: Commit** — `feat(core): édition de la disposition sur le damier, annulation`

---

### Task 3: Les modules décrivent leurs réglages

**Files:**
- Create: `src/Trofeo.ModuleKit/Settings/SettingsSchema.cs`, `SettingsDocument.cs`, `IConfigurableModuleFactory.cs`, `IModuleNotices.cs`
- Modify: `src/Trofeo.ModuleKit/ModuleDescriptor.cs` (`AcceptsCommands`)
- Test: `tests/Trofeo.Core.Tests/SettingsSchemaTests.cs`, `SettingsDocumentTests.cs`

**Interfaces:**
- Produces (exact), espace de noms `Trofeo.ModuleKit` :

```csharp
public sealed record ModuleDescriptor(string TypeId, string DisplayName, GridSize MinSize,
    IReadOnlyDictionary<SizePreset, GridSize> Presets, bool AcceptsCommands = false);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ChoiceField), "choice")] [JsonDerivedType(typeof(ToggleField), "toggle")]
[JsonDerivedType(typeof(TextField), "text")] [JsonDerivedType(typeof(ListField), "list")]
[JsonDerivedType(typeof(SecretField), "secret")] [JsonDerivedType(typeof(LookupField), "lookup")]
[JsonDerivedType(typeof(ActionField), "action")]
public abstract record SettingField(string Key, string Label)
{
    public string? Help { get; init; }
    public FieldCondition? VisibleWhen { get; init; }
}
public sealed record FieldCondition(string Key, string Value);
public sealed record SettingOption(string Value, string Label);
public sealed record ChoiceField(string Key, string Label, IReadOnlyList<SettingOption> Options, string Default) : SettingField(Key, Label);
public sealed record ToggleField(string Key, string Label, bool Default) : SettingField(Key, Label);
public sealed record TextField(string Key, string Label, int MaxLength = 80) : SettingField(Key, Label) { public string? Placeholder { get; init; } }
/// Sous-ensemble ordonné des options, sans doublon.
public sealed record ListField(string Key, string Label, IReadOnlyList<SettingOption> Options, IReadOnlyList<string> Default) : SettingField(Key, Label);
/// Key est la clé du coffre, dans la portée du type de module ; jamais écrite dans Settings.
public sealed record SecretField(string Key, string Label) : SettingField(Key, Label);
/// Valeur : le Value d'un LookupResult. Searchable faux : la liste vient de LookupAsync(Key, "") à l'ouverture.
public sealed record LookupField(string Key, string Label, bool Searchable = true) : SettingField(Key, Label)
{
    public string? Placeholder { get; init; }
    public string LabelPath { get; init; } = "name";   // champ qui nomme une valeur objet
    public string? EmptyLabel { get; init; }           // texte quand la valeur est absente
}
public sealed record ActionField(string Key, string Label) : SettingField(Key, Label) { public string? Confirm { get; init; } }

public sealed record SettingsSchema(IReadOnlyList<SettingField> Fields) { public static SettingsSchema Empty { get; } = new([]); }
public sealed record LookupResult(string Label, JsonElement Value);
public sealed record ActionResult(bool Ok, string Message);

public interface IConfigurableModuleFactory : IModuleFactory
{
    SettingsSchema Schema { get; }
    Task<IReadOnlyList<LookupResult>> LookupAsync(string fieldKey, string query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LookupResult>>([]);
    Task<ActionResult> RunActionAsync(string actionKey, JsonElement settings, ISecretStore secrets, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "Action inconnue."));
}

/// Remarques d'un module sur sa propre configuration (« 3 lignes sur 5 tiennent à cette taille. »). Lu depuis n'importe quel thread.
public interface IModuleNotices { IReadOnlyList<string> Notices { get; } }

/// Lecture et écriture de réglages JSON par chemin pointé (« unifi.host »). Ne lève jamais pour une forme inattendue.
public static class SettingsDocument
{
    public static JsonElement? Get(JsonElement settings, string key);
    /// value null : supprime la clé. Crée les objets intermédiaires ; une racine qui n'est pas un objet devient {}.
    public static JsonElement Set(JsonElement settings, string key, JsonElement? value);
    public static string? GetString(JsonElement settings, string key);
    public static bool? GetBool(JsonElement settings, string key);
    public static IReadOnlyList<string> GetStrings(JsonElement settings, string key);
    /// Condition absente : visible. Sinon, compare à la valeur du champ visé, ou à son Default s'il est absent.
    public static bool IsVisible(SettingField field, JsonElement settings, SettingsSchema schema);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Schema_RoundTripsThroughJson_WithKinds()      // chaque type de champ, "kind" attendu, Equals après aller-retour
[Fact] public void Set_NestedKey_CreatesObjects()                // Set({}, "unifi.host", "1.2.3.4") → {"unifi":{"host":"1.2.3.4"}}
[Fact] public void Set_Null_RemovesKey_KeepsOthers()
[Fact] public void Set_OnNonObjectRoot_StartsFromEmpty()
[Fact] public void Get_MissingOrWrongShape_IsNull()
[Fact] public void IsVisible_UsesDefaultWhenConditionKeyAbsent() // source absente, Default "pc", VisibleWhen source=unifi → faux
[Fact] public void Factory_DefaultLookupAndAction_AreHarmless()  // une fabrique minimale : [] et ActionResult(false, "Action inconnue.")
[Fact] public void Descriptor_AcceptsCommands_DefaultsToFalse()
```

- [ ] **Step 2: Run** les tests de Core sur `SettingsSchema|SettingsDocument`. Expected : FAIL.
- [ ] **Step 3: Implement.** `SettingsDocument` passe par `JsonNode` et rend un `JsonElement` cloné.
- [ ] **Step 4: Run.** Expected : PASS. Les autres projets compilent sans retouche : le paramètre de `ModuleDescriptor` est facultatif.
- [ ] **Step 5: Commit** — `feat(modulekit): schéma de réglages des modules, remarques de module`

---

### Task 4: Les réglages de chaque module

**Files:**
- Modify: `src/Trofeo.Modules.Clock/ClockFactory.cs`, `Weather/IWeatherProvider.cs` (ajouter la recherche de ville)
- Modify: `src/Trofeo.Modules.Network/NetworkFactory.cs`
- Modify: `src/Trofeo.Modules.Agents/ClaudeCodeFactory.cs`, `CodexFactory.cs`
- Create: `src/Trofeo.Modules.Agents/QuotaModInstaller.cs`, `src/Trofeo.Core/AppLocator.cs`
- Modify: `src/Trofeo.Modules.Monitoring/MonitoringFactory.cs`, `MonitoringSettings.cs`, `MonitoringModule.cs`
- Test: `ClockFactoryTests.cs`, `NetworkFactoryTests.cs`, `AgentFactorySchemaTests.cs`, `QuotaModInstallerTests.cs`, `MonitoringSettingsTests.cs` (ajouts) et `MonitoringNoticesTests.cs`, chacun dans le projet de tests de son module ; `AppLocatorTests.cs` dans Core.Tests

**Interfaces:**
- Consumes : tâche 3.
- Produces :
  - `IWeatherProvider.SearchCityAsync(string name, CancellationToken ct) : Task<IReadOnlyList<CitySuggestion>>`. Elle est déjà écrite dans `OpenMeteoClient`, et les doubles de test l'implémentent.
  - `public static class AppLocator { public static string? Find(string fileName, string? startDir = null, int maxLevels = 8); public static string? FindDirectory(string relativePath, string? startDir = null, int maxLevels = 8); }`.
    - L'ordre : le dossier de départ (par défaut `AppContext.BaseDirectory`), puis, en remontant, chaque dossier `src/<projet>/bin/{Release,Debug}/net10.0-windows10.0.19041.0/` d'un dépôt (la Release d'abord), puis les dossiers parents.
    - Sert à trouver `Trofeo.Display.exe`, `Trofeo.Settings.exe`, `Trofeo.SensorHelper.exe` et `mods/.claude-plugin/marketplace.json`. Le plan 6 mettra tout dans un même dossier.
  - `public sealed class QuotaModInstaller(Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> run, string? modsDir) { Task<ActionResult> InstallAsync(CancellationToken ct); }`.
- **Schémas** (textes exacts) :
  - **Heure** :
    - `choice format` « Format de l'heure » : `24h` « 24 h », `12h` « 12 h (AM/PM) », défaut `24h` ;
    - `choice unit` « Unité » : `c` « °C », `f` « °F », défaut `c` ;
    - `lookup location` « Ville de la météo », `Placeholder` « Cherche une ville », `EmptyLabel` « Pas de météo ».
    - `LookupAsync("location", q)` rend au plus 5 résultats. Libellé : « Lyon, Auvergne-Rhône-Alpes, France », sans les morceaux absents. Valeur : `{"name","latitude","longitude"}`.
  - **Réseau** :
    - `choice source` « Source du débit » : `pc` « Ce PC », `freebox` « Freebox », `unifi` « UniFi », défaut `pc` ;
    - `text unifi.host` « Adresse de la console », `Placeholder` « 192.168.1.1 », visible si `source=unifi` ;
    - `secret unifi.apiKey` « Clé API de la console », visible si `source=unifi` ;
    - `action forget-certificate` « Oublier le certificat de la console », visible si `source=unifi`. Sa `Confirm` : « La prochaine connexion fera confiance au certificat présenté. Ne le fais qu'après un renouvellement que tu connais. ». Elle efface `UniFiSource.PinKeyPrefix + hôte en minuscules` et répond « Certificat oublié : la prochaine connexion mémorisera le nouveau. ». Sans hôte : `(false, "Indique d'abord l'adresse de la console.")`.
  - **Claude Code et Codex** :
    - `choice view` « Affichage » : `feed` « Fil d'activité », `overview` « Vue d'ensemble », défaut `feed` ;
    - `toggle autoFollow` « Passer à la session qui t'attend », défaut vrai ;
    - `toggle feedFade` « Faire pâlir les lignes anciennes », défaut faux ;
    - `choice feedNewest` « Ligne la plus récente » : `top` « En haut », `bottom` « En bas, comme un terminal », défaut `top` ;
    - `AcceptsCommands: true`.
    - Claude Code seulement : `action install-quotas` « Installer le mod des quotas ». Sa `Confirm` : « Le mod trofeo-quotas sera ajouté à Claude Code pour ton compte (claude plugin install). Tu pourras le retirer avec claude plugin uninstall trofeo-quotas@trofeo-hb-dashboard. ».
    - L'installeur lance `claude plugin marketplace add "<mods>"`, puis `claude plugin install trofeo-quotas@trofeo-hb-dashboard`.
    - Réponses :
      - succès : « Mod installé : il sera chargé par les nouvelles sessions de Claude Code. » ;
      - `claude` introuvable : « Claude Code est introuvable sur ce PC. » ;
      - dossier `mods` absent : « Le dossier du mod est introuvable. » ;
      - code de retour non nul : « L'installation a échoué : » suivi des 300 premiers caractères de la sortie.
  - **Monitoring** :
    - `text title` « Titre », `Placeholder` « Nom du matériel détecté », `MaxLength` 60 ;
    - `list rows` « Lignes affichées » : `cpu` « Processeur », `gpu` « Carte graphique », `ram` « Mémoire », `vram` « Mémoire vidéo », `clock` « Fréquences », `power` « Consommation », `fan` « Ventilateur », `disk` « Disque », défaut `cpu, gpu, ram`.
  - **En écoute** : pas de schéma (simple `IModuleFactory`). La fenêtre dit « Ce module n'a rien à régler. ».
- **Monitoring** :
  - `MonitoringSettings.FromJson` écarte les lignes en double, en gardant la première (bilan 2).
  - `MonitoringModule : IModuleNotices`. Au rendu, il note combien de lignes tiennent (`MonitoringRows.Capacity`). Quand elles ne tiennent pas toutes, sa remarque est « {n} lignes sur {total} tiennent à cette taille : agrandis le module ou retire des lignes. ». Avec une seule ligne : « 1 ligne sur … tient … ».

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Clock_Schema_HasFormatUnitLocation()                 // clés, défauts, libellés exacts
[Fact] public async Task Clock_Lookup_FormatsLabelAndValue()            // fausse météo : « Lyon, Auvergne-Rhône-Alpes, France », Value.name == "Lyon"
[Fact] public async Task Clock_Lookup_SkipsMissingParts()               // région nulle : « Lyon, France »
[Fact] public void Network_UniFiFields_VisibleOnlyForUniFi()            // IsVisible avec source pc / unifi
[Fact] public async Task Network_ForgetCertificate_ClearsPinForLowercaseHost()  // FakeSecretStore : clé unifi.certSha256.console.local effacée
[Fact] public async Task Network_ForgetCertificate_WithoutHost_Refuses()
[Fact] public void Agents_Schemas_AndAcceptsCommands()                  // claudecode a install-quotas, codex non
[Fact] public async Task QuotaMod_RunsMarketplaceThenInstall()          // ordre et arguments exacts des deux appels
[Fact] public async Task QuotaMod_ClaudeMissing_SaysSo()                // le lanceur lève Win32Exception
[Fact] public async Task QuotaMod_Failure_ReturnsTruncatedOutput()
[Fact] public void MonitoringSettings_DuplicateRows_KeepFirst()         // ["cpu","ram","cpu"] → ["cpu","ram"]
[Fact] public void Monitoring_TooManyRows_RaisesNotice()                // 5 lignes dans un 4×2 : remarque exacte ; 3 lignes : aucune
[Fact] public void AppLocator_FindsInRepoReleaseBeforeDebug()           // arborescence temporaire
```

- [ ] **Step 2: Run** les projets de tests concernés. Expected : FAIL.
- [ ] **Step 3: Implement.** Le lanceur réel de `QuotaModInstaller` (`Process`, `claude` résolu par le `PATH`, 60 s au plus) est créé dans `ClaudeCodeFactory.CreateProduction()`.
- [ ] **Step 4: Run.** Expected : PASS, hors échecs connus.
- [ ] **Step 5: Commit** — `feat(modules): réglages décrits pour Heure, Réseau, Claude Code, Codex et Monitoring`

---

### Task 5: Choisir la carte graphique surveillée

**Files:**
- Modify: `src/Trofeo.Sensors/SensorSnapshot.cs`, `SensorMapper.cs`, et la sérialisation du tube de l'assistant si elle liste les champs
- Modify: `src/Trofeo.Modules.Monitoring/MonitoringSettings.cs`, `MonitoringModule.cs`, `MonitoringFactory.cs`
- Test: `tests/Trofeo.Sensors.Tests/SensorMapperTests.cs` (ajouts), `tests/Trofeo.Modules.Monitoring.Tests/GpuChoiceTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record GpuReading(string Name, IReadOnlyDictionary<string, double> Values);   // clés SensorKeys.Gpu*
public sealed record SensorSnapshot(string? CpuName, string? GpuName, IReadOnlyDictionary<string, double> Values,
    bool Elevated, IReadOnlyList<GpuReading>? Gpus = null)
{
    /// Le relevé vu avec la carte nommée : ses valeurs Gpu* remplacent celles de la carte retenue, GpuName devient son nom.
    /// Nom null ou absent de Gpus : le relevé tel quel.
    public SensorSnapshot ForGpu(string? name);
}
public sealed record MonitoringSettings(string? Title, IReadOnlyList<string> Rows, string? Gpu = null);   // "gpu" ; null = automatique
```

- **Règles** :
  - `SensorMapper.Map` remplit `Gpus` pour chaque carte, dans l'ordre de `ChooseGpu`. La carte retenue reste aussi dans `Values`, comme aujourd'hui.
  - Un relevé sans `Gpus` (ancien assistant) se lit comme avant.
  - Dans le schéma du Monitoring : `lookup gpu` « Carte graphique surveillée », `Searchable: false`, `EmptyLabel` « Automatique (celle qui a le plus de mémoire) ».
  - `LookupAsync("gpu", _)` rend les noms de `feed.Latest?.Gpus`. Le libellé et la valeur (une chaîne JSON) sont le nom.
  - Carte choisie absente du relevé : le Monitoring prend le choix automatique, avec la remarque « Carte « {nom} » introuvable : choix automatique. ».

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Map_TwoGpus_ListsBothInRankOrder()      // NVIDIA 12 Go puis Intel : Gpus[0].Name NVIDIA, valeurs propres à chacune
[Fact] public void ForGpu_SwapsGpuValuesAndName()
[Fact] public void ForGpu_UnknownOrNull_ReturnsSame()
[Fact] public void Snapshot_WithoutGpus_RoundTripsThroughPipeFormat()   // compatibilité avec l'ancien assistant
[Fact] public void Monitoring_ChosenGpu_IsDrawn()           // valeurs de l'Intel affichées quand gpu = nom Intel
[Fact] public void Monitoring_MissingGpu_FallsBackWithNotice()
[Fact] public async Task Monitoring_LookupGpu_ListsDetectedCards()
```

- [ ] **Step 2: Run.** Expected : FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** Sensors.Tests et Monitoring.Tests. Expected : PASS, hors les 26 échecs `cmd.exe` de Sensors.
- [ ] **Step 5: Commit** — `feat(monitoring): choix de la carte graphique surveillée`

---

### Task 6: Canal de contrôle

**Files:**
- Create: `src/Trofeo.Control/Trofeo.Control.csproj` (`net10.0`, référence ModuleKit)
- Create: `src/Trofeo.Control/ControlPipe.cs`, `Messages.cs`, `Framing.cs`, `ControlServer.cs`, `ControlClient.cs`
- Test: `tests/Trofeo.Control.Tests/` : `FramingTests.cs`, `ControlChannelTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public static class ControlPipe { public const string Name = "trofeo-HB-dashboard.control"; public const int ProtocolVersion = 1; }

public sealed record ModuleTypeInfo(string TypeId, string DisplayName, GridSize MinSize,
    IReadOnlyDictionary<SizePreset, GridSize> Presets, bool AcceptsCommands, SettingsSchema Schema);
public sealed record Welcome(string ServiceVersion, int ProtocolVersion, IReadOnlyList<ModuleTypeInfo> Types);
public enum LinkScreenState { Ready, Recovering, Disconnected }
public sealed record ScreenInfo(LinkScreenState State, string? Reason);
public sealed record ModuleState(Guid InstanceId, string TypeId, ModuleHealth Health, string? ActionMessage, IReadOnlyList<string> Notices);
public sealed record ProcessInfo(long PrivateBytes, long WorkingSetBytes, double CpuPercent);
public sealed record FrameInfo(int FramesSent, double RenderMs, double SendMs);
public sealed record SensorInfo(bool PawnIoInstalled, bool HelperInstalled, bool HelperConnected);
public sealed record ServiceStatus(ScreenInfo Screen, bool Emulated, IReadOnlyList<ModuleState> Modules,
    ProcessInfo Process, FrameInfo Frames, SensorInfo Sensors);
public sealed record Reloaded(bool Applied, string? Error, IReadOnlyList<string> Warnings);
public sealed record PreviewFrame(byte[] Jpeg, int Rotation);

public interface IControlHandler
{
    Welcome Hello();
    ServiceStatus GetStatus();
    Task<Reloaded> ReloadAsync(CancellationToken ct);
    Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query, CancellationToken ct);
    Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey, CancellationToken ct);
    /// onFrame est appelé depuis le fil d'affichage : il ne doit jamais bloquer.
    IDisposable SubscribePreview(int maxFps, Action<PreviewFrame> onFrame);
}

public sealed class ControlServer(string pipeName, IControlHandler handler) : IDisposable
{
    public void Start();   // jusqu'à 4 clients à la fois
}

public sealed class ControlClient : IAsyncDisposable
{
    public static Task<ControlClient> ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken ct); // lève TimeoutException
    public Welcome Welcome { get; }
    public Task<ServiceStatus> GetStatusAsync(CancellationToken ct);
    public Task<Reloaded> ReloadAsync(CancellationToken ct);
    public Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query, CancellationToken ct);
    public Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey, CancellationToken ct);
    public Task SubscribePreviewAsync(int maxFps, CancellationToken ct);   // maxFps 0 : arrêt
    public event Action<PreviewFrame>? FrameReceived;
    public event Action? Disconnected;
}
```

- **Trame** : `int32` (petit-boutiste) de longueur, puis 1 octet de sorte (1 : JSON UTF-8, 2 : image), puis la charge. Charge d'une image : `int32` de rotation, puis le JPEG. Au-delà de 8 Mo, la connexion est fermée.
- **JSON** :
  - chaque requête porte `id` et `type` (`hello`, `status`, `reload`, `lookup`, `action`, `preview`), et chaque réponse porte `replyTo` ;
  - une erreur côté service donne `{"type":"error","message":…}`, qui devient une `ControlException(message)` chez le client ;
  - délai par requête : 10 s, et 20 s pour `reload` et `action`.
- **Tube** : `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`.
- **Images** : sur chaque connexion, une seule image en cours d'écriture. Une image qui arrive pendant ce temps remplace celle qui attend, et l'image remplacée est perdue. Le fil d'affichage ne bloque jamais.

- [ ] **Step 1: Write the failing tests** (vrais tubes nommés, sous Linux aussi : un nom unique par test)

```csharp
[Fact] public void Framing_RoundTrip_JsonAndImage()
[Fact] public void Framing_Oversize_IsRejected()
[Fact] public async Task Hello_ReturnsTypesWithSchemas()
[Fact] public async Task Status_RoundTrips()
[Fact] public async Task Reload_ReturnsHandlerResult()
[Fact] public async Task HandlerThrows_ClientGetsControlException()       // message conservé, connexion toujours utilisable
[Fact] public async Task Preview_FramesArriveWithRotation()
[Fact] public async Task Preview_SlowClient_DropsFramesNotService()       // 100 images publiées sans lire : publier ne bloque jamais (< 50 ms en tout)
[Fact] public async Task ClientDisconnect_UnsubscribesPreview()           // l'abonnement du handler est libéré
[Fact] public async Task ServerStops_ClientRaisesDisconnected()
[Fact] public async Task FiveClients_FifthWaits()                         // 4 servis ; le 5e est accepté quand un des 4 part
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Control.Tests`. Expected : FAIL.
- [ ] **Step 3: Implement.** Un fil de lecture par client. Les écritures passent par un `SemaphoreSlim` par connexion, et `ImageSlot` garde la dernière image en attente.
- [ ] **Step 4: Run.** Expected : PASS sous Linux.
- [ ] **Step 5: Commit** — `feat(control): canal de contrôle entre le service et les réglages`

---

### Task 7: La boucle d'affichage change de disposition à chaud et nourrit l'aperçu

**Files:**
- Modify: `src/Trofeo.Service/DisplayLoop.cs`, `Trofeo.Service.csproj` (référence `Trofeo.Control`, pour `PreviewFrame`)
- Modify: `src/Trofeo.Core/Rendering/FrameScheduler.cs` (`SetMaxFps`)
- Test: `tests/Trofeo.Service.Tests/DisplayLoopApplyTests.cs`, `DisplayLoopPreviewTests.cs`, `tests/Trofeo.Core.Tests/FrameSchedulerTests.cs` (ajout)

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record LoopConfig(DashboardLayout Layout, ThemeDefinition Theme, IBackground Background);
public sealed record LoopModule(Guid InstanceId, string TypeId, ModuleStatus Status, IReadOnlyList<string> Notices);
public sealed record LoopStatus(bool Emulated, IReadOnlyList<LoopModule> Modules, int FramesSent, double RenderMs, double SendMs);

public sealed class DisplayLoop   // ajouts
{
    /// Appelable depuis n'importe quel fil ; appliqué au début du Tick suivant. La boucle devient propriétaire du fond.
    /// Rend les avertissements (module refusé, type inconnu…), en français.
    public Task<IReadOnlyList<string>> ApplyAsync(LoopConfig config);
    public IDisposable SubscribePreview(int maxFps, Action<PreviewFrame> onFrame);
    public LoopStatus Status { get; }                 // lisible depuis n'importe quel fil
    public DashboardLayout Layout { get; }            // la disposition appliquée
}
public sealed class FrameScheduler { public void SetMaxFps(double maxFps); }   // borné à [1, 12]
```

- **Rapprochement** (`ApplyAsync`) :
  - Pour chaque nouvel emplacement, un module en place est **gardé** quand il a le même `InstanceId`, le même `TypeId` et des `Settings` égaux (`JsonElement.DeepEquals`). La place, la taille et la taille du texte se lisent dans la nouvelle disposition.
  - Sinon, l'ancien module est arrêté (désabonné, `Dispose`), puis le nouveau est créé et démarré.
  - Un module disparu est arrêté.
  - Le thème change. Le compositeur est recréé quand le fond ou le thème change, et l'ancien fond est libéré après lui. Le fond animé repart de zéro.
  - Le plafond d'images vient de `MaxFps` (12 par défaut).
  - Une image est demandée.
  - `_modules` devient un dictionnaire figé, remplacé d'un coup : `FindCommandTarget` se lit depuis le fil de l'interface.
- **Aperçu et mode émulé** :
  - Quand l'écran est prêt, chaque image envoyée est aussi publiée aux abonnés. Les renvois de maintien ne sont pas publiés.
  - **Quand l'écran n'est pas prêt et qu'au moins un abonné existe** :
    - la boucle avance le fond, compose et encode comme d'habitude, puis publie l'image sans l'envoyer ;
    - c'est le mode émulé, avec `Emulated` à vrai ;
    - les tentatives de reconnexion continuent toutes les 2 s.
  - Sans abonné et sans écran, rien n'est rendu, comme aujourd'hui.
  - Chaque abonné a son propre plafond, borné à [1, 12]. Une image est publiée au plus toutes les `1 / maxFps` s par abonné.

- [ ] **Step 1: Write the failing tests** (avec `FakeModule`, `FakeUsbTransport` et `FakeTimeProvider`)

```csharp
[Fact] public async Task Apply_MovedModule_IsKeptNotRecreated()          // même instance, nouveau rectangle au rendu suivant
[Fact] public async Task Apply_ChangedSettings_RecreatesModule()         // ancien Dispose une fois, nouveau Start
[Fact] public async Task Apply_RemovedModule_IsDisposed()
[Fact] public async Task Apply_NewTheme_ReplacesBackgroundAndDisposesOld()
[Fact] public async Task Apply_UnknownType_ReturnsWarningKeepsOthers()   // « Module ignoré : type inconnu « zzz ». »
[Fact] public async Task Apply_MaxFps_ChangesScheduler()
[Fact] public async Task Preview_ScreenReady_PublishesSentJpeg()         // octets identiques à ceux envoyés, rotation 180
[Fact] public async Task Preview_NoScreen_RendersEmulated()              // transport absent + abonné : images publiées, Emulated vrai
[Fact] public async Task NoScreen_NoSubscriber_RendersNothing()
[Fact] public async Task Preview_RespectsSubscriberMaxFps()              // 4 i/s : au plus 4 images par seconde simulée
[Fact] public async Task Preview_Unsubscribe_StopsEmulatedRendering()
[Fact] public void FindCommandTarget_DuringApply_IsSafe()                // appels concurrents depuis un autre fil, aucune exception
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Service.Tests`. Expected : FAIL.
- [ ] **Step 3: Implement.** La file d'application est lue au début de `Tick`, sous un verrou court, et `ApplyAsync` est réveillé par `_scheduler.Wake()`. Le `TaskCompletionSource` est achevé à la fin de l'application.
- [ ] **Step 4: Run** les tests de Service et de Core. Expected : PASS sous Linux, anciens tests de la boucle compris.
- [ ] **Step 5: Commit** — `feat(service): disposition appliquée à chaud, aperçu et mode émulé`

---

### Task 8: Le service répond aux réglages

**Files:**
- Create: `src/Trofeo.Service/ServiceControlHandler.cs`, `LoopConfigFactory.cs`, `ProcessMeter.cs`, `IHotkeyHost.cs`
- Modify: `src/Trofeo.Service/ModuleRegistry.cs` (`Factory(typeId)`, `Types`)
- Modify: `src/Trofeo.Display/Program.cs`, `TrayIcon.cs`, `HotkeyRouter.cs` (`Replace`), et la référence à `Trofeo.Control`
- Test: `tests/Trofeo.Service.Tests/ServiceControlHandlerTests.cs`, `ProcessMeterTests.cs`, `LoopConfigFactoryTests.cs`

**Interfaces:**
- Consumes : tâches 1 (`TryLoad`), 3, 6 et 7.
- Produces (exact) :

```csharp
public static class LoopConfigFactory
{
    /// AppearanceResolver + BackgroundFactory, comme Program.Run aujourd'hui ; les avertissements vont à warn.
    public static LoopConfig Build(DashboardLayout layout, ISystemFonts fonts, Func<string, IBackground?>? video, Action<string> warn);
}
public interface IHotkeyHost { Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings); }  // rend les échecs, en français
public sealed class ProcessMeter(Func<(TimeSpan Cpu, long PrivateBytes, long WorkingSet)> sample, TimeProvider time, int processorCount)
{
    public ProcessInfo Read();   // CPU : moyenne depuis la lecture précédente, en % de la machine ; première lecture 0
}
public sealed class ServiceControlHandler(
    LayoutStore store, DisplayLoop loop, ModuleRegistry registry, Func<ScreenInfo> screen, Func<SensorInfo> sensors,
    ProcessMeter meter, Func<DashboardLayout, LoopConfig> buildConfig, IHotkeyHost? hotkeys) : IControlHandler;
```

- **Rechargement** :
  - `store.TryLoad()` en erreur : `Reloaded(false, error, [])`, l'écran ne change pas.
  - Sinon : `buildConfig`, puis `loop.ApplyAsync`, puis `hotkeys.ApplyAsync(layout.Shortcuts ?? ShortcutDefaults.All)`. La réponse est `Reloaded(true, null, avertissements)` : lecture, apparence, modules et raccourcis, dans cet ordre.
- **Recherche** : la fabrique du type est un `IConfigurableModuleFactory`, sinon la liste est vide.
- **Action** :
  - l'emplacement est trouvé par `InstanceId` dans `loop.Layout`, placé ou retiré ;
  - ses `Settings` et le coffre de son type sont passés à `RunActionAsync` ;
  - emplacement inconnu : `ActionResult(false, "Module introuvable dans la disposition.")`.
- **État** : écran, `Emulated`, modules, mesure du processus, images et capteurs.
  - `SensorInfo` : `PawnIoDetector`, l'exe de l'assistant présent dans `HelperIdentity.InstallDir`, et le tube relié (`SensorPipeClient`). Les noms exacts sont à lire dans `Trofeo.Sensors`.
- **Branchement dans `Program.Run`** (Windows ; vérifié en tâche 15) :
  - la configuration de départ passe par `LoopConfigFactory.Build` ;
  - `ControlServer(ControlPipe.Name, handler).Start()` après `loop.Start`, libéré avant la boucle ;
  - la fenêtre des raccourcis est **toujours** créée, et ses raccourcis ne sont posés que si un module accepte les commandes. `IHotkeyHost` passe par le fil de l'interface (`SynchronizationContext.Post`) et attend au plus 5 s ;
  - `HotkeyRouter.Replace(bindings)` retire tout, puis pose les nouveaux raccourcis et rend les échecs.
- **Icône** : le menu gagne « Ouvrir les réglages », en premier et en gras (élément par défaut). Un double-clic fait la même chose.
  - Le lancement passe par `AppLocator.Find("Trofeo.Settings.exe")`.
  - S'il est introuvable, une bulle dit « Le logiciel de réglage est introuvable. ».

- [ ] **Step 1: Write the failing tests** (fausse boucle de test, ou vraie boucle avec un faux transport ; disposition dans un dossier temporaire)

```csharp
[Fact] public async Task Reload_ValidFile_AppliesAndReturnsWarnings()
[Fact] public async Task Reload_InvalidFile_KeepsCurrentAndReportsReason()   // chevauchement : Applied faux, Error exacte, loop.Layout inchangée, pas de .bad
[Fact] public async Task Reload_MissingBackground_ReturnsWarning()           // image: chemin absent → avertissement de BackgroundFactory dans Warnings
[Fact] public async Task Reload_PassesShortcutsToHotkeyHost()
[Fact] public async Task Lookup_UsesConfigurableFactory()
[Fact] public async Task Action_FindsShelvedInstanceToo()
[Fact] public async Task Action_UnknownInstance_Refuses()
[Fact] public void Hello_ListsRegistryTypesWithSchemas()                      // AcceptsCommands, schéma vide pour En écoute
[Fact] public void ProcessMeter_ComputesMachinePercent()                     // 1 s de CPU en 2 s sur 4 cœurs : 12,5 %
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Service.Tests`. Expected : FAIL.
- [ ] **Step 3: Implement**, branchement compris.
- [ ] **Step 4: Run.** Expected : PASS. `dotnet build src/Trofeo.Display -c Release` : 0 avertissement.
- [ ] **Step 5: Commit** — `feat(service): réponses au logiciel de réglage, rechargement à chaud, menu « Ouvrir les réglages »`

---

### Task 9: La fenêtre — socle, design et lien avec le service

**Files:**
- Create: `src/Trofeo.Settings/Trofeo.Settings.csproj`
  - `WinExe`, `net10.0-windows10.0.19041.0`, sans WinForms ;
  - paquets Avalonia, Avalonia.Desktop et Avalonia.Themes.Fluent 12.1.4, CommunityToolkit.Mvvm 8.4.2 ;
  - références à Core et à Control ;
  - polices liées depuis `../Trofeo.Core/Assets/Fonts/`, en `AvaloniaResource` : Manrope-Medium, BarlowSemiCondensed-Regular et -SemiBold, avec leurs licences.
- Create: `App.axaml(.cs)`, `Program.cs`, `Styles/Tokens.axaml`, `Styles/Controls.axaml`, `Resources/Strings.resx`, `Texts.cs`
- Create: `Services/ServiceLink.cs`, `IServiceLink.cs`, `LayoutDocument.cs`, `EditorPreferences.cs`, `ServiceStarter.cs`
- Create: `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`, `Controls/ScreenStrip.cs`
- Create: `tests/Trofeo.Settings.Tests/` (`UsesXunitV3`, Avalonia.Headless.XUnit 12.1.4) et `tools/Trofeo.SettingsPreview/` (images PNG de la fenêtre, avec un faux service)

**Interfaces:**
- Produces (exact) :

```csharp
public enum LinkState { Connecting, Connected, ServiceStopped }
public interface IServiceLink : IAsyncDisposable
{
    LinkState State { get; }
    Welcome? Welcome { get; }
    ServiceStatus? Status { get; }                       // relu chaque seconde quand Connected
    event Action? Changed;                               // State, Welcome ou Status ; sur le fil de l'interface
    event Action<PreviewFrame>? FrameReceived;           // sur le fil de l'interface
    void SetRealTimePreview(bool realTime);              // 12 i/s ou 4 i/s
    Task<Reloaded> ReloadAsync();
    Task<IReadOnlyList<LookupResult>> LookupAsync(string typeId, string fieldKey, string query);
    Task<ActionResult> RunActionAsync(Guid instanceId, string actionKey);
    Task<bool> StartServiceAsync();                       // AppLocator + Process.Start ; faux si introuvable
}
public sealed record SaveOutcome(bool Saved, string? Message, IReadOnlyList<string> Warnings);
public sealed class LayoutDocument(LayoutStore store, IServiceLink link)
{
    public DashboardLayout Current { get; }
    public event Action? Replaced;                       // relu depuis le disque
    public void Load();
    /// Compare l'empreinte au fichier lu ; s'il a changé : relit, lève Replaced, Saved faux avec le message de Review Focus 2.
    /// Sinon : Save, puis link.ReloadAsync ; un refus du service rend Saved vrai, mais Message = Error.
    public Task<SaveOutcome> SaveAsync(DashboardLayout layout);
}
public sealed record EditorPreferences(bool FirstRunDone = false, bool RealTimePreview = false)
{
    public static EditorPreferences Load(string path);   // illisible : défaut
    public void Save(string path);
}
```

- **`ServiceLink`** :
  - essaie de se connecter dès le départ, puis toutes les 2 s tant que c'est en échec ;
  - au troisième échec de suite, l'état passe à `ServiceStopped` ;
  - une coupure fait repasser à `Connecting`, sans exception vers l'interface ;
  - une fois connecté, il s'abonne à l'aperçu à 4 i/s (ou 12).
- **`ScreenStrip`** (contrôle) :
  - il montre la dernière image décodée, tournée de `Rotation` (180 : retournement horizontal et vertical), mise à l'échelle sans déformation, dans un cadre de rayon 10 en `Line` ;
  - il expose `ScreenToStrip` et `StripToScreen` pour la tâche 10 ;
  - sans image, il montre « En attente de l'écran… » en `Muted`.
- **En-tête** :
  - « Trofeo » à gauche, en Barlow SemiBold 20 ;
  - les cinq onglets ;
  - à droite, la pastille de l'écran : « Écran branché » (point `Online`), « Écran non branché » (point `Idle`) ou « Reprise de l'écran… » (`Amber`) ;
  - en mode émulé, la pastille ambre « Mode émulé ».
- **Service arrêté** : un panneau remplace le contenu des onglets.
  - Il dit « Le service d'affichage ne tourne pas. Sans lui, l'écran reste éteint et rien ne peut être réglé. » et propose le bouton « Démarrer le service ».
  - En cas d'échec : « Le service d'affichage est introuvable. Lance Trofeo.Display.exe toi-même. ».
- **Instance unique** : un second lancement ramène la première fenêtre au premier plan (mutex, plus un `EventWaitHandle` nommé, sous Windows seulement), puis quitte.
- **Fenêtre** :
  - 1440 × 900 par défaut, 1200 × 760 au minimum ;
  - titre « Réglages du Trofeo » ;
  - polices et jetons de « Design de la fenêtre » ;
  - focus visible : contour de 2 px en `Accent` ;
  - fort contraste respecté (le texte `Muted` fait au moins 4,5:1 sur `Panel`).
- **`Texts`** : une propriété par clé de `Strings.resx`. Le XAML les lit par `{x:Static}`.
- **`SettingsPreview`** : `dotnet run --project tools/Trofeo.SettingsPreview -- --out apercus-reglages`.
  - Il écrit une image PNG par onglet, plus « service arrêté » et « mode émulé », avec un faux `IServiceLink`.
  - L'aperçu de l'écran vient de `ThemePreview` (vrais modules).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Link_ServiceRestarts_Reconnects()                  // vrai ControlServer sur un tube de test : arrêt → Connecting → relance → Connected
[Fact] public async Task Link_ThreeFailures_IsServiceStopped()
[Fact] public async Task Link_Subscribes4FpsOr12()
[Fact] public async Task Document_ExternalChange_ReloadsInsteadOfOverwriting() // fichier réécrit entre Load et SaveAsync : pas d'écriture, Replaced levé, message exact
[Fact] public async Task Document_ServiceRefuses_ReportsReason()
[Fact] public void Preferences_Unreadable_IsDefault()
[AvaloniaFact] public void MainWindow_ShowsStoppedPanel_WhenServiceStopped()
[AvaloniaFact] public void MainWindow_EmulatedPill_WhenStatusEmulated()
[AvaloniaFact] public void ScreenStrip_Rotation180_FlipsImage()             // pixel haut-gauche de l'image = pixel bas-droite affiché
[Fact] public void Texts_EveryKeyExists_AndNoUnusedKey()
```

- [ ] **Step 2: Run** `dotnet test tests/Trofeo.Settings.Tests`. Expected : FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected : PASS sous Linux. Lancer aussi `SettingsPreview`, et **regarder les images** :
  - la bande est lisible ;
  - aucun texte n'est coupé ;
  - les jetons sont respectés.

  Joindre les chemins au rapport.
- [ ] **Step 5: Commit** — `feat(reglages): fenêtre Avalonia, lien avec le service, aperçu en direct`

---

### Task 10: Onglet Disposition

**Files:**
- Create: `ViewModels/LayoutTabViewModel.cs`, `Views/LayoutTab.axaml`, `Controls/GridOverlay.cs`
- Test: `tests/Trofeo.Settings.Tests/LayoutTabTests.cs`, `GridOverlayTests.cs`

**Interfaces:**
- Consumes : `LayoutEditor`, `LayoutHistory` et `GridGeometry` (tâche 2), `LayoutDocument` (tâche 9), `Welcome.Types` (tâche 6).
- **Colonne de gauche** :
  - « Sur l'écran » : chaque module placé, avec son point d'état (couleurs `Online`, `Refuse`, `Idle`), son nom et sa taille (« 8 × 4 »). Un clic le sélectionne.
  - « Retirés » : chaque module retiré, avec « Remettre » et « Oublier » (confirmation : « Ses réglages seront perdus. »).
  - « Ajouter un module » ouvre la liste des types. Un type déjà placé reste proposé : on peut en mettre deux (spec 5).
- **Damier** (`GridOverlay`, posé sur `ScreenStrip`) :
  - les cases sont en pointillés `Line`, avec les écarts du thème en cours, et le module choisi a un contour `Accent` de 2 px avec 8 poignées ;
  - glisser le corps déplace le module, glisser une poignée le redimensionne ;
  - pendant le glissement, un cadre fantôme se cale sur les cases : en `Accent` si la place est libre, en `Refuse` sinon, avec le motif sous la bande.
  - **Motifs** :
    - « Place prise par {nom du module}. » ;
    - « Hors de l'écran. » ;
    - « Plus petit que le minimum de {nom} ({c} × {r}). » ;
    - pour un ajout impossible : « Plus de place libre : retire ou réduis un module. ».
  - Au lâcher, un fantôme libre donne `SaveAsync`, un fantôme rouge ne donne rien. Échap annule le glissement.
- **Colonne de droite** (module choisi) :
  - le nom ;
  - « {c} × {r} cases », « {w} × {h} px » (par `GridGeometry.ModuleRect` avec les écarts du thème) et « minimum {c} × {r} » ;
  - les boutons Compact, Idéal et Large, le préréglage en cours étant en `Accent` ;
  - « Taille du texte » : Petit, Normal ou Grand ;
  - « Retirer de l'écran ».
- **Ctrl+Z / Ctrl+Y** : `LayoutHistory`. Chaque annulation est enregistrée et appliquée comme une édition.
- Un `SaveOutcome` qui porte un message s'affiche dans une ligne `Amber` sous la bande, jusqu'à la prochaine édition.

- [ ] **Step 1: Write the failing tests** (VM avec un faux lien et un dossier temporaire ; quelques `[AvaloniaFact]` pour le glisser)

```csharp
[Fact] public async Task Drag_ToFreeCells_SavesMovedLayout()
[Fact] public async Task Drag_Refused_DoesNotSave()                         // sur un voisin : aucune écriture, motif « Place prise par Monitoring PC. »
[Fact] public void Ghost_SnapsToCells_AtPreviewScale()                     // bande de 1152 px de large : un point → la case attendue
[Fact] public async Task Resize_BottomRightHandle_GrowsByCells()
[Fact] public async Task Preset_Large_AppliesWhenRoom()
[Fact] public async Task Remove_ThenRestore_KeepsSettings()
[Fact] public async Task Add_Codex_FirstFreeSpot()
[Fact] public async Task Undo_RestoresPreviousAndSaves()
[Fact] public void SizeInPixels_UsesThemeSpacing()                          // Pixel : écarts 18 / 20
[AvaloniaFact] public void Escape_CancelsDrag()
```

- [ ] **Step 2–4: Run, implement, run.** Puis `SettingsPreview` : regarder l'onglet, et un état « glissement refusé ».
- [ ] **Step 5: Commit** — `feat(reglages): onglet Disposition (damier, déplacer, redimensionner, ajouter, retirer)`

---

### Task 11: Onglet Personnalisation

**Files:**
- Create: `ViewModels/AppearanceTabViewModel.cs`, `Views/AppearanceTab.axaml`
- Test: `tests/Trofeo.Settings.Tests/AppearanceTabTests.cs`

- **Thème** : trois cartes, « Classique », « Liquid Glass » et « Pixel ». Chacune porte une vignette (une image fixe par thème, faite par `ThemePreview` et embarquée) et une phrase :
  - « Panneaux mats, typo de tableau de bord. » ;
  - « Verre dépoli sur un fond de halos. » ;
  - « Blocs nets et typo pixel. ».
- **Transparence** : un curseur de 0 à 100 %, avec « Celle du thème » (`PanelOpacity` null). Il est enregistré au lâcher, pas pendant le glissement.
- **Police** : une liste, avec « Celle du thème » en premier, puis les polices embarquées, un séparateur, et les polices du système (`SkiaSystemFonts.Families`). Chaque nom est écrit dans sa propre police.
- **Fond** : Celui du thème, Couleur, Photo, GIF ou Vidéo.
  - Couleur : une palette de 8 teintes et un champ `#RRGGBB`.
  - Photo, GIF et vidéo : « Choisir un fichier… », par le sélecteur d'Avalonia avec les extensions de `BackgroundFactory`. Le nom du fichier choisi s'affiche.
  - Pour une vidéo : « Une vidéo à la taille de l'écran (1920 × 462) coûte deux fois moins de processeur qu'une vidéo 1080p. ».
- **Garde de lisibilité** : un interrupteur, avec « Assombrit un panneau quand le fond derrière lui est trop clair pour lire le texte. ».
- Chaque changement donne `SaveAsync`. Les avertissements du rechargement (fond introuvable, police absente) s'affichent sous le réglage concerné, en `Amber`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Theme_Change_SavesAppearanceOnly()                // Placements et Shortcuts inchangés
[Fact] public async Task Opacity_ThemeDefault_WritesNull()
[Fact] public async Task Font_Embedded_ListedFirst()
[Fact] public async Task Background_Color_ValidatesHex()                   // "#12345" refusé : « Couleur invalide : écris-la comme #1A2B3C. »
[Fact] public async Task Background_Video_ShowsAdvice()
[Fact] public async Task ReloadWarning_ShownUnderBackground()
[Fact] public async Task Legibility_Off_WritesFalse()
```

- [ ] **Step 2–4: Run, implement, run**, puis `SettingsPreview`.
- [ ] **Step 5: Commit** — `feat(reglages): onglet Personnalisation`

---

### Task 12: Onglet Modules

**Files:**
- Create: `ViewModels/ModulesTabViewModel.cs`, `ViewModels/Fields/*.cs` (un VM par type de champ), `Views/ModulesTab.axaml`, `Views/Fields/*.axaml`
- Create: `Services/SecretAccess.cs` : `ISecretAccess { bool Has(string typeId, string key); void Set(string typeId, string key, string? value); }`, sur `DpapiSecretVault` (`AppPaths.SettingsDir/secrets.dat`)
- Test: `tests/Trofeo.Settings.Tests/ModulesTabTests.cs`, `FieldViewModelTests.cs`

- **À gauche** : les modules placés, puis les retirés, chacun avec son point d'état.
- **À droite**, pour le module choisi :
  - le nom et l'état en mots : « En ligne », « Hors ligne » ou « Non configuré » ;
  - son `ActionMessage` (par exemple « Appuie sur ► sur ta Freebox… ») et ses remarques, en `Amber` ;
  - puis le formulaire tiré du schéma. Seuls les champs visibles d'après `SettingsDocument.IsVisible` s'affichent, et la visibilité est recalculée à chaque changement.
- **Champs** :
  - choix : boutons segmentés jusqu'à 3 options, liste au-delà ;
  - case : un interrupteur ;
  - texte : enregistré à la perte du focus ou sur Entrée, borné à `MaxLength` ;
  - liste : des cases à cocher, avec des flèches haut et bas pour l'ordre ;
  - secret :
    - le champ affiche « Enregistrée » ou « Absente », avec un bouton « Modifier » ;
    - la saisie est masquée, puis « Enregistrer » et « Effacer » ;
    - le module est ensuite rechargé (`SaveAsync` de la même disposition) pour qu'il relise le coffre ;
  - recherche :
    - si le champ est cherchable, une saisie lance la recherche 400 ms après la dernière frappe, et 5 résultats au plus s'affichent ;
    - sinon, la liste s'ouvre d'elle-même ;
    - « {EmptyLabel} » remet la valeur à vide ;
  - action :
    - un bouton, avec sa confirmation si elle existe ;
    - pendant l'action, le bouton est inactif et on lit « En cours… » ;
    - le message de `ActionResult` s'affiche ensuite, en `Online` ou `Refuse`.
- Un type sans schéma affiche « Ce module n'a rien à régler. ».

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Choice_Change_WritesSettingsAndSaves()
[Fact] public async Task Visibility_FollowsSource()                        // unifi : hôte, clé, action visibles ; pc : cachés
[Fact] public async Task Secret_Set_GoesToVaultNotLayout()                 // faux coffre : Set appelé ; le JSON enregistré ne contient pas la valeur
[Fact] public async Task Secret_ShowsOnlyPresence()
[Fact] public async Task Lookup_Debounced_400ms()                          // 3 frappes rapides : 1 seule recherche
[Fact] public async Task Lookup_Pick_StoresValueAndShowsLabel()
[Fact] public async Task List_Reorder_AndNoDuplicates()
[Fact] public async Task Action_WithConfirm_RunsAfterYes()
[Fact] public void NoSchema_ShowsNothingToSet()
[Fact] public void Notices_And_ActionMessage_Shown()
```

- [ ] **Step 2–4: Run, implement, run**, puis `SettingsPreview` : Réseau en UniFi, Heure avec ville, Monitoring.
- [ ] **Step 5: Commit** — `feat(reglages): onglet Modules (formulaires tirés des schémas, secrets, recherches, actions)`

---

### Task 13: Onglets Raccourcis et Paramètres

**Files:**
- Create: `ViewModels/ShortcutsTabViewModel.cs`, `Views/ShortcutsTab.axaml`, `ViewModels/SettingsTabViewModel.cs`, `Views/SettingsTab.axaml`
- Create: `Services/StartupRegistration.cs` : `IStartupRegistration { bool IsEnabled { get; } void Enable(string exePath); void Disable(); }`. Implémentation `RunKeyStartup` : `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valeur `trofeo-HB-dashboard` = `"<chemin>"`, guillemets compris.
- Create: `Services/ElevatedLauncher.cs` : `IElevatedLauncher { bool Launch(string exe, string arguments); }`. Implémentation par `runas`, faux si l'élévation est refusée.
- Test: `ShortcutsTabTests.cs`, `SettingsTabTests.cs`, `ShortcutCaptureTests.cs`

- **Raccourcis** :
  - une ligne par raccourci, avec l'action en mots (« Onglet précédent », « Onglet suivant », « Aller à l'onglet {n} »), les touches en pastille (« Ctrl + Alt + → ») et le module visé (« Le premier qui accepte » ou un module précis parmi ceux dont le type a `AcceptsCommands`) ;
  - le bouton « Modifier » attend la combinaison : « Appuie sur la combinaison voulue (Échap pour annuler). ». La touche d'Avalonia est traduite dans la syntaxe de `ShortcutKeys`, et seules les touches que `ShortcutKeys.TryParse` accepte sont prises ;
  - **les doublons sont refusés** : « Ctrl+Alt+Right sert déjà à « Onglet suivant ». » ;
  - « Ajouter un raccourci », « Supprimer » ;
  - « Remettre les raccourcis par défaut » (`ShortcutDefaults.All`) ;
  - les raccourcis que Windows refuse au rechargement s'affichent sur leur ligne, avec le texte de `IHotkeyHost`.
- **Paramètres** :
  - **Orientation** : « À l'endroit (0°) » ou « Retourné (180°) ». C'est 180° sur le montage d'Alex.
  - **Démarrer avec Windows** : un interrupteur, via `IStartupRegistration`, avec le chemin de `Trofeo.Display.exe` trouvé par `AppLocator`.
  - **Images par seconde** : un curseur de 1 à 12, « 12 (le maximum de l'écran) ».
  - **Aperçu** : l'interrupteur « Rendu réel 12 images/s » (préférence), avec « L'aperçu suit l'écran image par image. Il coûte un peu plus de processeur pendant que les réglages sont ouverts. ».
  - **Températures du processeur** :
    - l'état en une phrase, d'après `SensorInfo` : « Actives. », « Le pilote PawnIO n'est pas installé. » ou « Le pilote est installé, mais l'assistant des capteurs ne tourne pas. » ;
    - le bouton « Installer le pilote des températures ». Si PawnIO manque, il demande d'abord l'installeur (« Choisis PawnIO_setup.exe »). Il lance ensuite `IElevatedLauncher` sur `Trofeo.SensorHelper.exe install [--pawnio <chemin>]` ;
    - un refus d'élévation affiche « Installation annulée : Windows n'a pas reçu l'autorisation. ».
  - **Consommation** :
    - « Service d'affichage : {Mo} Mo, {x,x} % du processeur » ;
    - « Réglages : {Mo} Mo, libérés à la fermeture » ;
    - en `Muted` : « Objectif : moins de 100 Mo et environ 1 % en fond. ».
  - **Partager** :
    - « Exporter la disposition… » écrit `LayoutTransfer.Export` dans un fichier `.trofeo.json` ;
    - « Importer une disposition… » valide le fichier, copie la disposition en cours en `layout.json.avant-import`, puis enregistre. Un fichier refusé affiche son motif.
  - **Version** : « Version 0.5.0 ».

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData(Key.Right, KeyModifiers.Control | KeyModifiers.Alt, "Ctrl+Alt+Right")]
         [InlineData(Key.D3, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, "Ctrl+Alt+Shift+3")]
         [InlineData(Key.F5, KeyModifiers.Control, "Ctrl+F5")]
public void Capture_TranslatesToShortcutSyntax(Key key, KeyModifiers mods, string expected)
[Fact] public void Capture_WithoutModifier_IsIgnored()
[Fact] public async Task Shortcut_Duplicate_IsRefusedWithMessage()
[Fact] public async Task Shortcuts_ResetDefaults_Saves()
[Fact] public async Task Orientation_Change_Saves180()
[Fact] public void Startup_Toggle_UsesRegistrationWithQuotedPath()
[Fact] public async Task MaxFps_Slider_Saves()
[Fact] public async Task Sensors_InstallWithoutPawnIo_AsksForInstaller()
[Fact] public void Sensors_ElevationRefused_Says()
[Fact] public async Task Import_Invalid_ShowsReason_AndKeepsCurrent()
[Fact] public async Task Import_Valid_BacksUpThenSaves()
[Fact] public void Consumption_FormatsMegabytesAndPercent()                 // 46 137 344 octets, 0,83 % → « 44 Mo, 0,8 % du processeur »
```

- [ ] **Step 2–4: Run, implement, run**, puis `SettingsPreview`.
- [ ] **Step 5: Commit** — `feat(reglages): onglets Raccourcis et Paramètres`

---

### Task 14: Assistant de première ouverture

**Files:**
- Create: `ViewModels/FirstRunViewModel.cs`, `Views/FirstRunView.axaml`
- Test: `tests/Trofeo.Settings.Tests/FirstRunTests.cs`

- Il s'ouvre à la place des onglets tant que `EditorPreferences.FirstRunDone` est faux. Chaque étape a « Continuer » et « Passer ». « Terminer » enregistre `FirstRunDone`. C'est une vraie suite d'étapes : une barre de progression les numérote (« Étape 3 sur 8 »).
- **Les étapes** (spec 9.2) :
  1. **L'écran.** Il reprend la pastille d'état.
     - S'il est occupé (motif contenant « TRCC »), on lit : « Le logiciel Thermalright utilise l'écran : ferme-le, puis attends quelques secondes. ».
     - S'il n'est pas branché : « Branche l'écran ou continue en mode émulé. ».
  2. **Le sens.** La bande en direct, avec « Sur ton écran, le texte est-il à l'endroit ? ».
     - « Oui » continue.
     - « Non, retourner » bascule la rotation et l'applique tout de suite.
  3. **Les modules.** Des cases pour les six types. On part de la disposition actuelle : décocher retire le module (`Remove`), cocher l'ajoute (`Add`). Un `NoRoom` affiche son motif.
  4. **Claude Code.** Elle n'apparaît que si un module Claude Code est placé. Elle explique les quotas et propose « Installer le mod des quotas », l'action du module.
  5. **Réseau** et 6. **Météo** : le formulaire du module, celui de la tâche 12, réduit aux champs utiles. Chacune est sautée si le module n'est pas placé.
  7. **Températures du processeur** : le bloc de Paramètres.
  8. **Démarrage avec Windows** : l'interrupteur, coché par défaut.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void FirstRun_ShownUntilDone()
[Fact] public async Task Orientation_No_FlipsAndApplies()
[Fact] public async Task Modules_Uncheck_RemovesToShelf()
[Fact] public void ClaudeStep_SkippedWithoutClaudeModule()
[Fact] public async Task Finish_SavesPreference()
[Fact] public void Progress_CountsVisibleSteps()                          // sans Claude Code : « Étape 4 sur 7 »
```

- [ ] **Step 2–4: Run, implement, run**, puis `SettingsPreview`.
- [ ] **Step 5: Commit** — `feat(reglages): assistant de première ouverture`

---

### Task 15 (PC d'Alex): Vérifier sous Windows, mesurer, bilan

- [ ] **Step 1: Construire.** `dotnet build -c Release`, puis `dotnet test` sur tous les projets : ceux qui échouaient sous Linux doivent passer, et `Display.Tests` compris.
- [ ] **Step 2: Avec Alex, sur l'écran.**
  - [ ] Le menu de l'icône « Ouvrir les réglages » et le double-clic ouvrent la fenêtre. Un second clic la ramène au premier plan.
  - [ ] L'aperçu suit l'écran, et sa rotation est juste.
  - [ ] Écran débranché : la pastille « Mode émulé » s'affiche, et l'aperçu continue.
  - [ ] Déplacer, redimensionner, ajouter et retirer un module. Un refus s'affiche en rouge. Ctrl+Z annule.
  - [ ] Thème, transparence, police et fond (photo, GIF, vidéo) se changent sans relancer le service. L'écran ne repasse pas au logo.
  - [ ] Réseau en UniFi : la clé se saisit, puis on oublie le certificat. La ville de la météo se cherche. Le choix du GPU donne Intel ou RTX 5070.
  - [ ] Les raccourcis sont modifiés puis rechargés. Un raccourci déjà pris par une autre appli est signalé.
  - [ ] « Démarrer avec Windows » marche après un redémarrage de session.
  - [ ] Le pilote des températures : bouton, élévation, état « Actives. ». Alex lance lui-même l'installeur administrateur.
  - [ ] Exporter, puis importer.
  - [ ] Assistant de première ouverture : remettre `FirstRunDone` à faux dans `settings-app.json`.
  - [ ] Fermer les réglages : l'écran continue, et la mémoire des réglages est rendue (le processus a disparu).
- [ ] **Step 3: Mesurer la sobriété** (méthode du bilan 4) :
  - le service seul, réglages fermés : même consommation qu'avant le plan, à 1 % près ;
  - réglages ouverts, aperçu à 4 et à 12 images/s ;
  - la mémoire de `Trofeo.Settings.exe`.
- [ ] **Step 4: Bilan** — `docs/superpowers/plans/2026-10-2x-plan-5-bilan.md`, puis mise à jour de `CLAUDE.md`.
