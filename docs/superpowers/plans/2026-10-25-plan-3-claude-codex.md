# Plan 3 — Modules Claude Code et Codex

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter les modules **Claude Code** et **Codex**, avec leurs éléments :
- onglets par session, avec un point d'état ;
- flux en direct ou vue d'ensemble ;
- barre du bas avec contexte, durée, modèle et quotas 5 h / hebdo ;
- mascotte animée, avec plusieurs variantes par état ;
- raccourcis clavier globaux pour changer d'onglet.

Le module Claude Code prend les colonnes 0 à 7 de la disposition par défaut.

**Architecture:**
- **Un projet partagé `Trofeo.Modules.Agents` en `net10.0`.** Il contient :
  - le modèle neutre (`AgentSnapshot`) ;
  - une source par outil (`ClaudeSource`, `CodexSource`), qui ne fait que **lire des fichiers locaux** ;
  - le moteur d'animation et les deux mascottes ;
  - un seul `AgentModule`, paramétré par une « saveur » (textes, mascotte).
- **Quotas Claude Code** : une petite commande de barre d'état, `Trofeo.StatusLine.exe` (`net10.0`), écrit ce que Claude Code lui transmet dans un fichier par session. Elle s'installe et se retire de `~/.claude/settings.json` par une commande qu'Alex lance lui-même.
- **Raccourcis** : enregistrés par le service d'affichage (`RegisterHotKey`), puis routés vers le module visé par un nouveau contrat `ICommandableModule` du ModuleKit.

**Tech Stack:**
- .NET 10, C#, SkiaSharp 3.119 ;
- System.Text.Json (`JsonDocument`, `JsonNode`) ;
- xUnit et FakeTimeProvider ;
- Win32 `RegisterHotKey` (service d'affichage seulement).

**Spec:** `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`, sections 6.1, 6.2, 6.3, 7.3, 9.2 (étape 4) et 10. Voir aussi les bilans `docs/superpowers/plans/2026-10-24-plan-1-bilan.md` et `2026-10-25-plan-2-bilan.md`.

## Global Constraints

- **Identité `trofeo-HB-dashboard`** : nom de dossier `~/.claude/trofeo-HB-dashboard/`, marqueur de sauvegarde `.trofeo-bak`. Elle ne change jamais.
- **Interdit** : lire le jeton de connexion de Claude Code ou de Codex, et contacter une adresse d'Anthropic ou d'OpenAI (spec 6.2). **Ce plan n'ouvre aucune connexion réseau.**
- **Fichiers lus** (rien d'autre) :
  - `<claudeHome>/sessions/*.json`, `<claudeHome>/projects/*/<sessionId>.jsonl`, `<claudeHome>/tasks/<sessionId>/*.json` ;
  - `<claudeHome>/trofeo-HB-dashboard/statusline/*.json` ;
  - `<codexHome>/sessions/AAAA/MM/JJ/rollout-*.jsonl` ;
  - le fichier `.git` des dossiers de travail.
- **`claudeHome`** vaut `%CLAUDE_CONFIG_DIR%` s'il est défini, sinon `%USERPROFILE%\.claude`. **`codexHome`** vaut `%CODEX_HOME%`, sinon `%USERPROFILE%\.codex`.
- **Fichiers écrits** :
  - les captures de `Trofeo.StatusLine.exe` dans `<claudeHome>/trofeo-HB-dashboard/statusline/` ;
  - `<claudeHome>/settings.json`, **uniquement** par `HardwareCheck --claude-statusline install|uninstall`, qu'Alex lance. C'est le consentement exigé par la spec en attendant l'assistant du plan 5. L'opération est réversible.
- **Pas dans `%AppData%`** : la commande de barre d'état est lancée par l'appli Claude empaquetée (MSIX), qui redirige `%AppData%` vers une copie privée (voir CLAUDE.md). On écrit donc sous `<claudeHome>`, qui n'est pas redirigé.
- **Textes à l'écran en français**, exactement :
  - « Aucune session Claude Code ouverte » / « Aucune session Codex ouverte » ;
  - « Claude Code n'est pas installé sur ce PC » / « Codex n'est pas installé sur ce PC » ;
  - « Contexte », « Durée », « Modèle », « 5 h », « Hebdo », « Quotas indisponibles », « Tâches », « Chargement… ».
- **Pas de texte d'état à côté de la mascotte** (demande d'Alex). L'état se lit à son animation et au point de l'onglet.
- **Couleurs des points** :

  | État | Couleur |
  |---|---|
  | Travaille | orange `#E8875B` |
  | Attend l'utilisateur | jaune `Theme.Warning` (`#F2B84B`) |
  | Inactif | gris `Theme.TextMuted` |

  Le drapeau de la mascotte Claude et le « ? / ! » de Codex utilisent le même jaune.
- **Mouvement** : chaque image d'animation dure **au moins 83 ms** (≤ 12 images/s). Les déplacements tombent sur des pixels entiers de sprite : nets, sans glissement doux (spec 7.3).
- **Sobriété** (spec 4.3) :
  - sondage des fichiers toutes les **1 s** ;
  - transcription lue **par la fin** (512 Kio au plus au premier passage), puis seulement les octets ajoutés ;
  - la mascotte joue par **séquences séparées de pauses**, pendant lesquelles `IsAnimating` est faux ;
  - elle **s'endort** (image fixe) après 10 min d'inactivité.
- **Polices** : `Theme.Typeface`, tailles toujours multipliées par `ctx.TextScale`.
- **Projets.**
  - `net10.0` : `Trofeo.Modules.Agents`, `Trofeo.StatusLine`, `tools/Trofeo.AgentPreview` et `tests/Trofeo.Modules.Agents.Tests` (cette dernière cible surcharge `tests/Directory.Build.props`). Ils se construisent et se testent sous Linux.
  - Le reste ne change pas de cible.
- `TreatWarningsAsErrors` reste actif. Licence GPL-3.0.

## Où construire

- **Tâches 0 à 9 : dans le cloud sous Linux.** Installer le SDK avec `apt-get install -y dotnet-sdk-10.0` (Ubuntu 24.04 fournit 10.0.1xx ; la tâche 0 abaisse le plancher de `global.json`). Ne construire que les projets `net10.0` cités : la solution complète contient des projets Windows.
- **Tâches 10 et 11 : sur le PC d'Alex** (projets Windows, vrai écran).

## Review Focus

1. **Transcription énorme ou en cours d'écriture.** Des centaines de Mo, ou une dernière ligne à moitié écrite : on ne lit que la fin, et la ligne partielle attend son retour à la ligne. Tests `Tail_LargeFile_ReadsOnlyLastChunk` et `Tail_PartialLine_WaitsForNewline` (tâche 2).
2. **Alex a déjà une barre d'état, ou un `settings.json` qu'on ne sait pas lire.** La sienne continue de s'afficher (chaînage) et revient telle quelle à la désinstallation. Un fichier illisible n'est jamais réécrit. Tests `Install_ChainsExistingCommand`, `Uninstall_RestoresPreviousExactly` et `Install_InvalidJson_ChangesNothing` (tâche 4).
3. **Fichier de session laissé par un Claude Code planté** (processus mort) : l'onglet n'apparaît pas. Test `Directory_DeadProcess_IsHidden` (tâche 5).
4. **Session dans un worktree, ou chemin avec accents ou espaces** (`C:\Users\Élodie\Mes projets\app`) :
   - l'onglet porte le nom du dépôt principal ;
   - la transcription est retrouvée même si l'encodage du nom de dossier diffère.

   Tests `ProjectName_Worktree_UsesMainRepo` (tâche 1) et `Locate_EncodingMismatch_FallsBackToSearch` (tâche 5).
5. **Un raccourci déjà pris par un autre logiciel** : les autres raccourcis marchent, un avertissement est journalisé, rien ne plante. Test `Router_RegisterFails_ContinuesWithOthers` (tâche 10).

## Décisions prises en écrivant ce plan

Vérifiées sur Claude Code **2.1.310**, dans la session cloud qui a écrit ce plan, par lecture des fichiers et du programme :

- **États d'une session** : `~/.claude/sessions/<pid>.json` contient `status` ∈ {`busy`, `idle`, `waiting`}, avec `waitingFor` (« input needed », « dialog open »…) quand il vaut `waiting`.
  - `busy` = travaille ; `waiting` = attend l'utilisateur ; `idle` = inactif.
  - **Le hook `Notification` est donc inutile** : pas de seconde modification des réglages de Claude Code.
- **« Tâche terminée »** (confettis) : la session affichée passe de `busy` à `idle`.
- **Tâches de la vue d'ensemble.** Claude Code les range dans `~/.claude/tasks/<sessionId>/<id>.json`, avec les champs `subject`, `activeForm` et `status`. À défaut de dossier, on prend la dernière liste `TodoWrite` de la transcription (anciennes versions).
- **Lignes ajoutées et retirées d'un Edit** : `toolUseResult.structuredPatch[].lines`, dont les lignes commencent par `+` ou `-`. Pour un Write qui crée un fichier : `structuredPatch` est vide et `type` vaut `"create"`, on compte alors les lignes de `input.content`.
- **Barre d'état.**
  - Claude Code transmet `rate_limits.five_hour` et `rate_limits.seven_day`, avec `used_percentage` et `resets_at`. `resets_at` est en secondes Unix ; on accepte aussi une date ISO.
  - Il transmet aussi `context_window.context_window_size` et `context_window.used_percentage`.
- **Taille du contexte sans barre d'état.** La taille de fenêtre dépend du modèle et du serveur. On prend, dans l'ordre :
  1. la valeur capturée par la barre d'état pour cette session ;
  2. 1 000 000 si l'identifiant du modèle contient `[1m]` ou si l'usage dépasse 200 000 ;
  3. sinon 200 000.
- **Chaînage d'une barre d'état existante** : `"<exe>" --tee | ( <ancienne commande> )`, entre parenthèses pour qu'une commande avec `&&`, `||` ou `;` reçoive bien l'entrée. Le shell de Claude Code exécute lui-même le tuyau, ce qui évite de deviner son shell (cmd ou Git Bash). Le chemin de l'exe est écrit avec des `/`, que comprennent les deux.
- **Suivi automatique** (réglage `autoFollow`, activé par défaut) : quand une session passe à « attend l'utilisateur », son onglet s'affiche, sauf si Alex a changé d'onglet au clavier dans les 2 dernières minutes. C'est le cas qui justifie un coup d'œil.
- **Codex** : format d'après la communauté (spec 6.3), **non vérifié**. Le code est tolérant : un champ inconnu est ignoré, jamais une erreur. `AgentPreview --codex-probe` affiche la structure d'une vraie transcription (les clés, sans les valeurs) pour qu'un ami d'Alex la transmette.
- **Raccourcis** rangés dans `layout.json` (`Shortcuts`, absent = valeurs par défaut). Le plan 5 les rendra modifiables.
  - Par défaut, ils visent le premier module qui accepte des commandes, dans l'ordre des colonnes.
  - Pour deux modules agents, on peut viser une instance précise par son identifiant.
- **Tailles** : minimum 6×3 ; Compact 6×3, Idéal 8×4, Large 10×4.
- **Mise en page** : la maquette `claude-module-v2.html` n'est pas dans le dépôt. Les tailles ci-dessous s'en inspirent (texte agrandi d'environ 35 %) et seront ajustées par Alex sur l'écran (tâche 11).

---

### Task 0: Projets, plancher du SDK, contrat de commandes

**Files:**
- Modify: `global.json` (`"version": "10.0.100"`, `rollForward` inchangé `latestFeature`), `trofeo-HB-dashboard.sln`
- Create: `src/Trofeo.Modules.Agents/Trofeo.Modules.Agents.csproj` (→ ModuleKit, StatusLine ; `InternalsVisibleTo Trofeo.Modules.Agents.Tests` et `Trofeo.AgentPreview`)
- Create: `src/Trofeo.StatusLine/Trofeo.StatusLine.csproj` (`Exe`, aucune dépendance de paquet ; `InternalsVisibleTo Trofeo.Modules.Agents.Tests`)
- Create: `tools/Trofeo.AgentPreview/Trofeo.AgentPreview.csproj` (`Exe`, → Agents et Core)
- Create: `tests/Trofeo.Modules.Agents.Tests/Trofeo.Modules.Agents.Tests.csproj` (→ Agents, StatusLine, Core)
- Create: `src/Trofeo.ModuleKit/ICommandableModule.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/SmokeTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.ModuleKit` :

```csharp
public enum ModuleCommandKind { PreviousTab, NextTab, SelectTab }
/// <param name="Index">Pour SelectTab : rang de l'onglet, à partir de 0.</param>
public readonly record struct ModuleCommand(ModuleCommandKind Kind, int Index = 0);
/// Un module qui réagit aux raccourcis. Appelé depuis le thread de l'interface ; rend vrai si la commande a servi.
public interface ICommandableModule { bool HandleCommand(ModuleCommand command); }
```

- **Projets de tests et d'aperçu.** Le projet de tests fixe `<TargetFramework>net10.0</TargetFramework>`. Il ne référence pas `Trofeo.TestDoubles` (Windows) : il a ses propres faux objets, `FakeModuleLog` et `FakeAgentSource`, créés aux tâches qui les utilisent. Lui et `AgentPreview` ajoutent `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.0 sous la condition `$([MSBuild]::IsOSPlatform('Linux'))`.

- [ ] **Step 1: Sur une branche dédiée partie de `main`** (dans le cloud : celle de la session, `main-zcgy1g`), **créer les projets et les ajouter à la solution**
- [ ] **Step 2: Test de fumée** — `Skia_DrawsOnLinux` : un `SKBitmap` 10×10 rempli de rouge renvoie `GetPixel(5,5) == SKColors.Red` et `Themes.Classique.Typeface` n'est pas `null`.
- [ ] **Step 3: Vérifier** — Run : `dotnet build src/Trofeo.Modules.Agents && dotnet build tools/Trofeo.AgentPreview && dotnet test tests/Trofeo.Modules.Agents.Tests`. Expected : `0 Warning(s)`, 1 test qui passe.
- [ ] **Step 4: Commit** — `chore: projets du plan 3 et contrat de commandes des modules`

---

### Task 1: Modèle neutre et textes

**Files:**
- Create: `src/Trofeo.Modules.Agents/Model/AgentModel.cs`, `IAgentSource.cs`
- Create: `src/Trofeo.Modules.Agents/Text/AgentText.cs`, `ModelNames.cs`, `ProjectNames.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/AgentTextTests.cs`, `ModelNamesTests.cs`, `ProjectNamesTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.Modules.Agents` :

```csharp
public enum AgentActivity { Idle, Working, Waiting }
public enum AgentTaskState { Pending, InProgress, Completed }
public sealed record FeedEntry(string Tool, string Target, int? Added = null, int? Removed = null);
public sealed record AgentTask(string Text, AgentTaskState State);
public sealed record QuotaWindow(double UsedPercent, DateTimeOffset? ResetsAt);
public sealed record AgentQuotas(QuotaWindow? FiveHour, QuotaWindow? Weekly, DateTimeOffset CapturedAt);
public sealed record AgentSessionSummary(string Id, string ProjectName, AgentActivity Activity,
    DateTimeOffset StartedAt, DateTimeOffset LastActivityAt);
/// Feed : du plus ancien au plus récent, 50 entrées au plus. Tasks : vide si aucune liste.
public sealed record AgentSessionDetail(string Id, string? CurrentTask, DateTimeOffset? TaskStartedAt,
    IReadOnlyList<FeedEntry> Feed, IReadOnlyList<AgentTask> Tasks, string? Model, double? ContextPercent);
/// Installed : l'outil est présent sur le PC (son dossier existe). Sessions triées par StartedAt croissant.
public sealed record AgentSnapshot(bool Installed, IReadOnlyList<AgentSessionSummary> Sessions,
    AgentSessionDetail? Selected, AgentQuotas? Quotas);
/// Lit l'état sur disque. Appelé depuis un seul thread à la fois ; peut lever (le module gère).
public interface IAgentSource : IDisposable { AgentSnapshot Poll(string? selectedSessionId, DateTimeOffset now); }

public static class AgentText
{
    public static string Duration(TimeSpan elapsed);                    // « < 1 min », « 12 min », « 1 h 05 »
    public static string Percent(double value);                         // « 42 % », arrondi AwayFromZero, borné à [0, 100]
    public static string Reset(DateTimeOffset resetsAt, DateTimeOffset now); // heures locales, culture fr-FR
}
public static class ModelNames { public static string Display(string? modelId); }
public static class ProjectNames { public static string FromWorkingDirectory(string cwd); }
```

**Règles :**
- **`Reset`** :
  - même jour : « reset 14:30 » ;
  - moins de 7 jours : « reset lun. 09:00 » (jour abrégé fr-FR) ;
  - au-delà : « reset 3 nov. » ;
  - une date déjà passée : « reset passé ».
- **`ModelNames.Display`** :
  - retirer `claude-`, un suffixe de date `-\d{8}` et `[1m]` ;
  - le premier mot devient la famille, avec une majuscule ; les nombres qui suivent sont joints par « . » ;
  - un identifiant `gpt-…` : « GPT- » suivi du numéro, puis les mots restants avec une majuscule, séparés par des espaces ;
  - `null`, vide ou `<synthetic>` : « — » ;
  - sinon l'identifiant tel quel.
- **`ProjectNames.FromWorkingDirectory`** : remonter depuis `cwd` jusqu'au premier `.git`.
  - Si c'est un **dossier** : le nom du dossier qui le contient.
  - Si c'est un **fichier** `gitdir: <chemin>` dont le chemin, normalisé en `/`, contient `/.git/worktrees/` : le nom du dossier qui précède `/.git/worktrees/`. C'est le dépôt principal.
  - Aucun `.git`, ou un fichier illisible : le nom du dossier `cwd`.
  - Une racine de lecteur : `cwd` tel quel.
  - Les deux séparateurs `\` et `/` sont acceptés.

- [ ] **Step 1: Tests**

```csharp
[Theory]
[InlineData(20, "< 1 min")]
[InlineData(12 * 60 + 30, "12 min")]
[InlineData(65 * 60, "1 h 05")]
public void Duration_Formats(int seconds, string expected) => Assert.Equal(expected, AgentText.Duration(TimeSpan.FromSeconds(seconds)));

[Theory]
[InlineData("claude-opus-5-5", "Opus 5.5")]
[InlineData("claude-sonnet-4-5-20250929", "Sonnet 4.5")]
[InlineData("claude-haiku-5-5[1m]", "Haiku 5.5")]
[InlineData("claude-fable-5-1", "Fable 5.1")]
[InlineData("gpt-5.1-codex-max", "GPT-5.1 Codex Max")]
[InlineData("<synthetic>", "—")]
[InlineData(null, "—")]
public void Display_Models(string? id, string expected) => Assert.Equal(expected, ModelNames.Display(id));
```

  - `Percent_RoundsAndClamps` : 41,5 → « 42 % » ; 130 → « 100 % » ; −3 → « 0 % ».
  - `Reset_SameDay_ShowsTime`, `Reset_ThisWeek_ShowsDay` (« reset lun. 09:00 »), `Reset_Past`.
  - `ProjectName_PlainRepo`, `ProjectName_Subfolder_UsesRepoRoot`.
  - `ProjectName_Worktree_UsesMainRepo` : dossiers temporaires `app/.git/worktrees/feat` et `app/.claude/worktrees/feat/.git`. Ce dernier est un fichier `gitdir: <tmp>/app/.git/worktrees/feat`, et le résultat attendu est « app ».
  - `ProjectName_NoGit_UsesFolder` (« Mes projets »).
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): modèle neutre des sessions et textes de la barre`

---

### Task 2: Lecture par la fin d'un fichier JSONL

**Files:**
- Create: `src/Trofeo.Modules.Agents/Common/TranscriptTail.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/TranscriptTailTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed class TranscriptTail(string path, int initialBytes = 512 * 1024, int maxLineBytes = 1024 * 1024)
{
    /// Les lignes complètes apparues depuis l'appel précédent (sans « \r\n »). Fichier absent : liste vide.
    public IReadOnlyList<string> ReadNewLines();
    /// Vrai si ce dernier appel est reparti de la fin du fichier (premier appel, ou fichier raccourci) :
    /// l'appelant remet alors son état à zéro avant d'appliquer les lignes.
    public bool Restarted { get; }
}
```

**Règles :**
- **Ouverture** : `FileShare.ReadWrite | FileShare.Delete`, fermée après chaque appel.
- **Premier appel** : départ = max(0, longueur − `initialBytes`). Si le départ est > 0, on saute jusqu'après le premier `\n`, car la première ligne est coupée.
- **Ligne partielle** : une fin sans `\n` est gardée en mémoire et complétée à l'appel suivant.
- **Ligne trop longue** : au-delà de `maxLineBytes`, elle est jetée en entier, jusqu'au `\n` suivant.
- **Fichier raccourci** : une longueur inférieure à la position lue fait repartir comme au premier appel.
- **Lecture plafonnée** : 4 Mio au plus par appel ; la suite viendra au suivant.
- **Décodage** : UTF-8. Une séquence invalide donne le caractère de remplacement, pas d'exception.

- [ ] **Step 1: Tests**, sur des fichiers temporaires
  - `Tail_FirstCall_ReturnsExistingLines` : `Restarted` est vrai.
  - `Tail_Appended_ReturnsOnlyNewLines` : `Restarted` est faux.
  - `Tail_PartialLine_WaitsForNewline` : on écrit `{"a":1}` sans retour à la ligne, aucune ligne ; on ajoute `\n`, une ligne.
  - `Tail_LargeFile_ReadsOnlyLastChunk` : 3 Mio de lignes numérotées, `initialBytes` 1024 ; aucune ligne coupée, et la dernière ligne est bien la dernière du fichier.
  - `Tail_Truncated_Restarts`.
  - `Tail_OversizedLine_Skipped` : `maxLineBytes` 100, une ligne de 500 octets entre deux lignes courtes ; les deux lignes courtes reviennent.
  - `Tail_MissingFile_Empty`.
  - `Tail_FileLockedForWriting_StillReads` : un autre `FileStream` ouvert en écriture avec `FileShare.ReadWrite`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): lecture incrémentale des transcriptions par la fin`

---

### Task 3: Transcription Claude Code et liste de tâches

**Files:**
- Create: `src/Trofeo.Modules.Agents/Claude/ClaudeTranscript.cs`, `ClaudeTaskList.cs`, `ToolTargets.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/ClaudeTranscriptTests.cs`, `ClaudeTaskListTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed class ClaudeTranscript
{
    public void Apply(string jsonLine);          // ligne illisible ou inconnue : ignorée
    public void Reset();
    public string? CurrentTask { get; }
    public DateTimeOffset? TaskStartedAt { get; }
    public IReadOnlyList<FeedEntry> Feed { get; } // 50 au plus, du plus ancien au plus récent
    public string? Model { get; }
    public long? ContextTokens { get; }
    public IReadOnlyList<AgentTask>? Todos { get; } // dernière liste TodoWrite, ou null
}
public static class ClaudeTaskList { public static IReadOnlyList<AgentTask>? Read(string sessionTasksDir); }
```

**Lignes prises en compte.** Toute ligne `"isSidechain": true` est ignorée.

| Ligne | Effet |
|---|---|
| `type` `user`, sans `isMeta`, avec un `message.content` texte (une chaîne, ou des blocs `text` sans `tool_result`) qui ne commence pas par `<` après `Trim` | `CurrentTask` = première ligne non vide, `TaskStartedAt` = `timestamp` |
| `type` `assistant`, `message.model` ≠ `<synthetic>` | `Model` ; `ContextTokens` = `usage.input_tokens + cache_creation_input_tokens + cache_read_input_tokens` ; une entrée du flux par bloc `tool_use`, gardée sous son `id` |
| `type` `user` avec des blocs `tool_result` et un objet `toolUseResult` | Le **premier** `tool_use_id` reçoit les compteurs. Pour un `structuredPatch` non vide : `Added` / `Removed` = lignes qui commencent par `+` / `-`. Pour `type` `"create"` : `Added` = nombre de lignes de `input.content`, gardé à la création de l'entrée. |
| Bloc `tool_use` `TodoWrite` | `Todos` = `input.todos[]` (`content`, `status`, `activeForm`). Pas d'entrée dans le flux. |

**Nom et cible de l'outil** (`ToolTargets.Describe(string name, JsonElement input) → (string Tool, string Target)?`, `null` = pas d'entrée) :

| Outil | Nom affiché | Cible |
|---|---|---|
| `Read`, `Write`, `Edit`, `MultiEdit` | le même (`MultiEdit` → `Edit`) | nom de fichier de `file_path` (séparateurs `\` et `/`) |
| `NotebookEdit` | `Edit` | nom de fichier de `notebook_path` |
| `Bash` | `Bash` | `description`, sinon la première ligne de `command` |
| `Grep`, `Glob` | le même | `pattern` |
| `WebFetch` | `Web` | l'hôte de `url` |
| `WebSearch` | `Recherche` | `query` |
| `Task`, `Agent` | `Agent` | `description` |
| `Skill` | `Skill` | `skill` |
| `TodoWrite`, `TaskCreate`, `TaskUpdate`, `TaskList`, `TaskGet`, `ToolSearch` | — | `null` |
| `mcp__<serveur>__<outil>` | `<outil>` | première valeur texte de `input`, sinon vide |
| autre | le nom | première valeur texte de `input`, sinon vide |

Cibles et tâches sont réduites à une seule ligne : retours à la ligne remplacés par des espaces, 200 caractères au plus.

**Liste de tâches** (`ClaudeTaskList.Read`) :
- dossier absent : `null` ;
- les fichiers `*.json` sont triés par leur nom lu comme un entier (les autres viennent après, par ordre alphabétique) ;
- champs `subject`, `activeForm` et `status` : `pending`, `in_progress` ou `completed` ;
- `deleted`, statut inconnu ou fichier illisible : ignoré ;
- texte : `activeForm` (sinon `subject`) pour une tâche en cours, `subject` sinon.

- [ ] **Step 1: Tests** — des lignes JSONL écrites à la main, au format vérifié (voir « Décisions »)
  - `Prompt_SetsTaskAndStart`.
  - `Prompt_CommandWrapper_Ignored` (`<command-name>/clear</command-name>`), `Prompt_Meta_Ignored` et `Prompt_ToolResult_NotATask`.
  - `ToolUse_AddsFeedEntries` : Read, Bash avec `description`, Grep ; noms et cibles exacts.
  - `EditResult_CountsAddedRemoved` : `structuredPatch` `[" un","-deux","+deux bis","+quatre"," trois"]` donne +2 / −1.
  - `WriteCreate_CountsContentLines`.
  - `Usage_SumsContextTokens` : 2 + 782 + 169 665 = 170 449, et `Model` « claude-opus-5-5 ».
  - `Synthetic_IgnoredForModel`.
  - `Sidechain_Ignored`.
  - `TodoWrite_SetsTodos_NotInFeed`.
  - `Feed_KeepsLast50`.
  - `BadLine_Ignored`.
  - `Describe_Table` : un `[Theory]` sur chaque ligne du tableau des outils.
  - `TaskList_ReadsInNumericOrder_SkipsDeleted` (fichiers 2, 10 et 1 → ordre 1, 2, 10).
  - `TaskList_InProgress_UsesActiveForm`.
  - `TaskList_MissingDir_Null`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): lecture des transcriptions et des tâches de Claude Code`

---

### Task 4: Commande de barre d'état et son installation

**Files:**
- Create: `src/Trofeo.StatusLine/Program.cs`, `ClaudeHome.cs`, `StatusLineFile.cs`, `StatusLineCapture.cs`, `StatusLineInstaller.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/StatusLineCaptureTests.cs`, `StatusLineInstallerTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.StatusLine` :

```csharp
public static class ClaudeHome
{
    public static string Resolve();                       // %CLAUDE_CONFIG_DIR%, sinon ~/.claude
    public static string StatusLineDir(string claudeHome); // <claudeHome>/trofeo-HB-dashboard/statusline
}
public sealed record CapturedQuota(double UsedPercent, DateTimeOffset? ResetsAt);
public sealed record StatusLineRecord(DateTimeOffset CapturedAt, string SessionId, string? ModelId,
    long? ContextWindowSize, double? ContextUsedPercent, CapturedQuota? FiveHour, CapturedQuota? SevenDay);
public static class StatusLineFile
{
    public static void Write(string dir, StatusLineRecord record);                       // <dir>/<SessionId>.json, atomique
    public static IReadOnlyList<StatusLineRecord> ReadAll(string dir, DateTimeOffset now, TimeSpan maxAge);
    public static void DeleteOlderThan(string dir, DateTimeOffset now, TimeSpan age);
}
public static class StatusLineCapture
{
    /// Lit le JSON transmis par Claude Code ; null s'il est illisible ou sans session_id valide.
    public static StatusLineRecord? Parse(ReadOnlySpan<byte> json, DateTimeOffset now);
}
public enum StatusLineInstallResult { Installed, AlreadyInstalled, Uninstalled, NotInstalled, InvalidSettings }
public sealed class StatusLineInstaller(string claudeHome)
{
    public StatusLineInstallResult Install(string exePath);
    public StatusLineInstallResult Uninstall();
    public bool IsInstalled();
}
```

**Exécutable** (`Program.Main(string[] args)`) :
- lit l'entrée standard, **1 Mio au plus** ;
- `Parse`, puis `StatusLineFile.Write` dans `StatusLineDir(Resolve())` ;
- avec `--tee`, recopie ensuite les octets reçus tels quels sur la sortie standard ;
- **code de sortie toujours 0**, sans rien écrire sur l'erreur standard : une panne ne doit jamais casser la barre d'état de Claude Code ;
- aucune autre dépendance que la bibliothèque standard, pour un démarrage rapide.

**Lecture du JSON reçu** (`Parse`) :
- `session_id`, obligatoire : `[A-Za-z0-9-]`, 1 à 64 caractères, sinon `null` ;
- `model.id` ;
- `context_window.context_window_size` et `context_window.used_percentage` ;
- `rate_limits.five_hour` / `rate_limits.seven_day` : `used_percentage` et `resets_at` (nombre en secondes Unix, ou chaîne ISO 8601) ;
- un champ absent ou d'un autre type vaut `null`, sans erreur.

**Fichier de capture** : JSON en camelCase, par exemple :

```json
{"capturedAt":"…","sessionId":"…","modelId":"claude-opus-5-5","contextWindowSize":200000,"contextUsedPercent":42.5,"fiveHour":{"usedPercent":31,"resetsAt":"…"},"sevenDay":null}
```

- Écrit dans `<id>.json.tmp`, puis déplacé.
- `ReadAll` ignore les fichiers illisibles et ceux dont `capturedAt` a plus de `maxAge`.

**Installation** (`settings.json` = `<claudeHome>/settings.json`) :
1. **Lecture.** Fichier absent : `{}`. Sinon, `JsonNode.Parse` avec commentaires ignorés et virgules finales admises. Si ce n'est pas un objet, ou si la lecture échoue : `InvalidSettings`, et **rien n'est écrit**.
2. **Déjà installé.** Si `statusLine.command` contient « Trofeo.StatusLine » (sans tenir compte de la casse) : `AlreadyInstalled`.
3. **Sauvegardes.**
   - La première fois seulement, copie de `settings.json` en `settings.json.trofeo-bak`.
   - À chaque installation, l'ancien nœud `statusLine` (ou `null`) est écrit dans `<claudeHome>/trofeo-HB-dashboard/statusline-previous.json`, sous la forme `{"previous": …}`.
4. **Nouvelle commande.** Le chemin est absolu, avec `/`, entre guillemets.
   - Si l'ancien nœud est `{"type":"command","command":X}` : `"<exe>" --tee | ( X )`, et ses autres propriétés (`padding`, `refreshInterval`…) sont gardées.
   - Sinon : `{"type":"command","command":"\"<exe>\""}`.
5. **Écriture** atomique, indentée, en UTF-8 sans BOM. Les autres clés sont conservées.

**Désinstallation** :
- si la commande n'est pas la nôtre : `NotInstalled` ;
- sinon, on remet le nœud `previous` exactement (ou on retire `statusLine` s'il valait `null`), puis on supprime `statusline-previous.json` : `Uninstalled`.

- [ ] **Step 1: Tests**
  - `Parse_FullPayload` : `session_id`, `model.id`, `context_window` (200000, 42,5), `five_hour` (31, `resets_at` 1792940400), `seven_day` (12, `"2026-10-27T08:00:00Z"`). Valeurs et dates exactes.
  - `Parse_NoRateLimits_QuotasNull`.
  - `Parse_BadSessionId_Null` (`"../x"`).
  - `Parse_Garbage_Null`.
  - `File_WriteThenReadAll_RoundTrips`.
  - `File_ReadAll_SkipsOldAndCorrupt`.
  - `File_DeleteOlderThan`.
  - `Program_Tee_EchoesInputAndWritesCapture` : `Main` est appelé en `internal` avec des flux injectés.
  - `Program_GarbageInput_ExitsZeroSilently`.
  - `Install_NoSettings_CreatesCommand`.
  - `Install_KeepsOtherKeys` (`model`, `hooks`, `permissions`).
  - `Install_ChainsExistingCommand` : `{"type":"command","command":"bash ~/sl.sh","padding":1}` donne `"\"C:/x/Trofeo.StatusLine.exe\" --tee | ( bash ~/sl.sh )"` et `padding` 1.
  - `Install_Twice_AlreadyInstalled`.
  - `Install_InvalidJson_ChangesNothing` : octets du fichier identiques et aucune sauvegarde créée.
  - `Uninstall_RestoresPreviousExactly`.
  - `Uninstall_NoPrevious_RemovesStatusLine`.
  - `Uninstall_Foreign_NotInstalled`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(statusline): capture des quotas par la barre d'état de Claude Code, installation réversible`

---

### Task 5: Source Claude Code

**Files:**
- Create: `src/Trofeo.Modules.Agents/Claude/ClaudeSessionDirectory.cs`, `ClaudeSource.cs`, `TranscriptLocator.cs`, `ContextWindow.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/ClaudeSessionDirectoryTests.cs`, `ClaudeSourceTests.cs`

**Interfaces:**
- Consumes : `TranscriptTail` (tâche 2), `ClaudeTranscript` et `ClaudeTaskList` (tâche 3), `StatusLineFile` et `ClaudeHome` (tâche 4), `ProjectNames` (tâche 1).
- Produces (exact) :

```csharp
public sealed record ClaudeSessionFile(int Pid, string SessionId, string Cwd, string Status,
    DateTimeOffset StartedAt, DateTimeOffset LastActivityAt, string? Kind);
public sealed class ClaudeSessionDirectory(string sessionsDir, Func<int, bool> isProcessAlive)
{ public IReadOnlyList<ClaudeSessionFile> Read(); }
public static class TranscriptLocator { public static string? Find(string projectsDir, string cwd, string sessionId); }
public static class ContextWindow { public static double? Percent(long? tokens, string? modelId, StatusLineRecord? capture, DateTimeOffset now); }
public sealed class ClaudeSource(string claudeHome, Func<int, bool> isProcessAlive) : IAgentSource
{ public static bool IsProcessAlive(int pid); } // Process.GetProcessById, sans exception
```

**Règles :**
- **Fichiers de session** :
  - champs `pid`, `sessionId`, `cwd`, `status`, `startedAt` et `statusUpdatedAt` (en millisecondes Unix), `kind` ; `LastActivityAt` = `statusUpdatedAt`, sinon `updatedAt`, sinon `startedAt` ;
  - gardés si `kind` est absent ou vaut `interactive`, **et** si `isProcessAlive(pid)` ;
  - un fichier illisible (en cours d'écriture) garde sa dernière lecture réussie, mise en cache par chemin, date d'écriture et taille ;
  - un même `sessionId` vu deux fois : on garde le plus récent.
- **États** : `busy` → `Working`, `waiting` → `Waiting`, tout le reste → `Idle`.
- **Transcription** (`TranscriptLocator.Find`) :
  - d'abord `projects/<cwd où chaque caractère hors [A-Za-z0-9] devient « - »>/<sessionId>.jsonl` ;
  - sinon, chercher `projects/*/<sessionId>.jsonl` ;
  - le résultat est mis en cache par `sessionId` dans `ClaudeSource`.
- **Détail de la session choisie** :
  - un `TranscriptTail` et un `ClaudeTranscript` par session, 3 au plus en cache (le moins récemment utilisé part) ;
  - quand `Restarted`, `Reset()` avant d'appliquer les lignes ;
  - `Tasks` = `ClaudeTaskList.Read(tasks/<id>)`, sinon `Todos`, sinon vide.
- **`ContextWindow.Percent`** :
  - une capture de cette session de moins de 60 s avec `ContextUsedPercent` : cette valeur ;
  - sinon `tokens` ÷ fenêtre, où la fenêtre vaut `capture.ContextWindowSize`, sinon 1 000 000 (`[1m]` dans le modèle, ou `tokens` > 200 000), sinon 200 000 ;
  - borné à [0, 100] ; `tokens` `null` : `null`.
- **Quotas** : parmi les captures de moins de 8 jours, la plus récente qui a au moins un quota. `CapturedAt` est celui de la capture.
- **Ménage** : `StatusLineFile.DeleteOlderThan(…, 7 jours)`, une fois par heure au plus.
- **Installé** : `Directory.Exists(claudeHome)`.

- [ ] **Step 1: Tests** — un faux `claudeHome` dans un dossier temporaire, et `isProcessAlive` factice
  - `Directory_ReadsLiveSessions`, avec le fichier réel vu en cloud : `{"pid":89,"sessionId":"801406b2-…","cwd":"/home/user/trofeo-HB-dashboard","startedAt":1792922150984,"kind":"interactive","entrypoint":"remote_desktop","status":"busy","statusUpdatedAt":1792922247343,…}`.
  - `Directory_DeadProcess_IsHidden`.
  - `Directory_BackgroundKind_IsHidden`.
  - `Directory_PartialFile_KeepsLastGoodRead` : on réécrit le fichier tronqué ; la session reste.
  - `Directory_DuplicateSession_KeepsNewest`.
  - `Locate_EncodedPath` (`C:\Users\alexl\trofeo-HB-dashboard` → `C--Users-alexl-trofeo-HB-dashboard`).
  - `Locate_EncodingMismatch_FallsBackToSearch` : cwd `C:\Users\Élodie\Mes projets\app`, fichier rangé sous un autre nom de dossier.
  - `Context_PrefersFreshCapture`, `Context_UsesCapturedWindowSize`, `Context_Over200k_Assumes1M` et `Context_NoTokens_Null`.
  - `Source_Poll_BuildsSnapshot` : deux sessions (une `busy`, une `waiting`), le détail de la session choisie (tâche courante, flux, modèle, contexte), et les quotas d'une capture.
  - `Source_Poll_AppendsOnlyNewLines` : une ligne ajoutée entre deux `Poll` apparaît, et le flux n'est pas doublé.
  - `Source_QuotasFromAnySession` : la capture vient d'une autre session que celle affichée.
  - `Source_NoClaudeHome_NotInstalled`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): source Claude Code (sessions, transcription, contexte, quotas)`

---

### Task 6: Source Codex et sonde de format

**Files:**
- Create: `src/Trofeo.Modules.Agents/Codex/CodexHome.cs`, `CodexRollout.cs`, `CodexSource.cs`, `CodexProbe.cs`
- Create: `tools/Trofeo.AgentPreview/Program.cs` (option `--codex-probe`)
- Test: `tests/Trofeo.Modules.Agents.Tests/CodexRolloutTests.cs`, `CodexSourceTests.cs`, `CodexProbeTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public static class CodexHome { public static string Resolve(); } // %CODEX_HOME%, sinon ~/.codex
public sealed class CodexRollout
{
    public void Apply(string jsonLine);
    public void Reset();
    public string? SessionId { get; }  public string? Cwd { get; }  public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? LastEventAt { get; }
    public AgentActivity Activity(DateTimeOffset now);
    public string? CurrentTask { get; }  public DateTimeOffset? TaskStartedAt { get; }
    public IReadOnlyList<FeedEntry> Feed { get; }  public IReadOnlyList<AgentTask> Tasks { get; }
    public string? Model { get; }  public double? ContextPercent { get; }
    public AgentQuotas? Quotas { get; }
}
public sealed class CodexSource(string codexHome, TimeProvider time) : IAgentSource;
public static class CodexProbe { public static string Describe(IEnumerable<string> jsonLines); }
```

**Format attendu** (communautaire) : une ligne `{"timestamp":…,"type":…,"payload":{…}}` par événement.

| `type` / `payload.type` | Effet |
|---|---|
| `session_meta` | `SessionId` = `payload.id`, `Cwd` = `payload.cwd`, `StartedAt` = `payload.timestamp` (sinon la ligne) |
| `turn_context` | `Model` = `payload.model`, `Cwd` si présent |
| `event_msg` / `user_message` | `CurrentTask` = première ligne de `payload.message`, `TaskStartedAt` = `timestamp` |
| `event_msg` / `task_started` | en cours |
| `event_msg` / `task_complete`, `turn_aborted` | terminé |
| `event_msg` / `exec_approval_request`, `apply_patch_approval_request`, `request_user_input` | attend l'utilisateur, jusqu'au prochain autre événement |
| `event_msg` / `token_count` | `ContextPercent` = `info.last_token_usage.input_tokens` ÷ `info.model_context_window` × 100 ; quotas `rate_limits.primary` et `secondary` |
| `response_item` / `function_call` `shell`, `exec_command`, `local_shell` | entrée « Shell » : `arguments` (texte JSON), `command` en tableau joint par des espaces ou en texte, une ligne |
| `response_item` / `custom_tool_call` ou `function_call` `apply_patch` | entrée « Patch » : premier chemin après `*** Update File: `, `*** Add File: ` ou `*** Delete File: `. `Added` / `Removed` = lignes du patch qui commencent par `+` / `-`, en-têtes `***` exclus |
| `response_item` / `function_call` `update_plan` | `Tasks` = `plan[]` (`step`, `status` : `pending`, `in_progress`, `completed`) |
| `response_item` / `web_search_call` | entrée « Recherche » : `action.query` |
| `response_item` / autre `function_call` | entrée « <name> », cible vide |

- **Quotas** :
  - `used_percent`, `window_minutes` ;
  - `resets_at` en secondes Unix, sinon `resets_in_seconds` ajouté au `timestamp` de la ligne ;
  - ≤ 300 min → `FiveHour`, ≥ 10 080 min → `Weekly` ; sans `window_minutes` : `primary` → `FiveHour`, `secondary` → `Weekly`.
- **`Activity(now)`** :
  - une demande d'approbation est le dernier événement : `Waiting` ;
  - sinon, un `task_started` sans fin depuis, et un dernier événement de moins de 10 min : `Working` ;
  - sinon `Idle`.
- **`CodexSource.Poll`** :
  - **fichiers suivis** : les `rollout-*.jsonl` des dossiers des 3 derniers jours (date locale) modifiés il y a moins de 2 h, les 12 plus récents ;
  - **lecture** : un `TranscriptTail` et un `CodexRollout` par fichier, tous mis à jour à chaque sondage (seules les nouvelles lignes coûtent) ;
  - **première ligne** : comme la fin du fichier peut l'omettre, elle est lue à part (64 Kio au plus) et appliquée en premier après chaque `Restarted` ;
  - **identifiant** : `SessionId`, sinon le nom du fichier ; nom du projet : `ProjectNames.FromWorkingDirectory(Cwd)` ;
  - **quotas** : ceux de la session au `token_count` le plus récent ;
  - **installé** : `Directory.Exists(codexHome)`.
- **`CodexProbe.Describe`** : pour chaque couple `type/payload.type`, le nombre de lignes et les chemins de clés jusqu'à la profondeur 3, avec leur genre JSON (`string`, `number`…). **Jamais une valeur.**
- **`AgentPreview --codex-probe [codexHome]`** : applique `Describe` à la transcription la plus récente et l'affiche.

- [ ] **Step 1: Tests** — des lignes écrites à la main au format ci-dessus
  - `Meta_SetsIdCwdStart`.
  - `UserMessage_SetsTask`.
  - `Shell_AddsFeedEntry` (`{"command":["bash","-lc","npm test"]}` → « Shell », « bash -lc npm test »).
  - `ApplyPatch_CountsLines`.
  - `UpdatePlan_SetsTasks`.
  - `TokenCount_SetsContextAndQuotas` : `used_percent` 31, `window_minutes` 300, `resets_in_seconds` 600 → `ResetsAt` = horodatage + 10 min.
  - `Activity_StartedThenComplete`, `Activity_ApprovalIsWaiting` et `Activity_StaleStartedIsIdle` (11 min).
  - `UnknownLines_Ignored`.
  - `Source_ScansRecentDays_IgnoresOldFiles` : dossiers `sessions/AAAA/MM/JJ` temporaires, avec une `FakeTimeProvider`.
  - `Source_TailSkipsHead_StillHasMeta` : un fichier de plus de 512 Kio dont la première ligne est `session_meta`.
  - `Source_NoCodexHome_NotInstalled`.
  - `Probe_PrintsKeysNotValues` : la sortie ne contient ni « npm test » ni « 31 », mais contient `payload.rate_limits.primary.used_percent : number`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): source Codex et sonde de format des transcriptions`

---

### Task 7: Moteur d'animation

**Files:**
- Create: `src/Trofeo.Modules.Agents/Animation/Sprite.cs`, `Easing.cs`, `Clip.cs`, `MascotAnimator.cs`
- Test: `tests/Trofeo.Modules.Agents.Tests/SpriteTests.cs`, `EasingTests.cs`, `MascotAnimatorTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
/// Image en pixels : une chaîne par rangée, un caractère par pixel ; « . » = transparent.
public sealed class Sprite(IReadOnlyList<string> rows, IReadOnlyDictionary<char, SKColor> palette)
{
    public int Width { get; }  public int Height { get; }
    public void Draw(SKCanvas canvas, float x, float y, float pixel, byte alpha = 255);
}
public enum Ease { Linear, InQuad, OutQuad, InOutQuad, OutBack }
public static class Easing { public static float Apply(Ease ease, float t); } // t borné à [0, 1]
/// Position en pixels de sprite, depuis le coin haut gauche de la scène ; From → To pendant la pose.
public sealed record Layer(Sprite Sprite, SKPointI From, SKPointI To);
public sealed record Pose(int DurationMs, IReadOnlyList<Layer> Layers, Ease Ease = Ease.Linear);
public sealed record Clip(string Name, IReadOnlyList<Pose> Poses) { public TimeSpan Duration { get; } }
public enum MascotState { Idle, Working, Waiting, Done }
public sealed record MascotDefinition(string Name, int SceneWidth, int SceneHeight, Pose Rest, Pose Sleep,
    IReadOnlyDictionary<MascotState, IReadOnlyList<Clip>> Clips);
public sealed class MascotAnimator(MascotDefinition definition, TimeProvider time, Random random)
{
    public MascotState State { get; }
    public void SetState(MascotState state);  // même état : sans effet
    public bool IsAnimating { get; }          // vrai pendant un clip, faux pendant une pause ou le sommeil
    public bool IsSleeping { get; }
    public string? CurrentClip { get; }
    public TimeSpan UntilNextChange();        // pendant une pause : sa fin ; pendant un clip : la pose suivante ; sommeil : infini
    public void Draw(SKCanvas canvas, SKRect area);
}
```

**Règles :**
- **`Sprite`** : toutes les rangées ont la même longueur, et chaque caractère autre que « . » figure dans la palette (`ArgumentException` sinon). Un pixel est un rectangle plein de côté `pixel`, sans anticrénelage.
- **Courbes** : `OutBack` avec `c1 = 1.70158` et `c3 = c1 + 1` ; ses valeurs peuvent dépasser 1.
- **Position d'un calque** : `From + (To − From) × Ease(t)`, arrondie au pixel de sprite (`MidpointRounding.AwayFromZero`).
- **Dessin** :
  - taille de pixel = max(1, plancher(min(largeur ÷ `SceneWidth`, hauteur ÷ `SceneHeight`))) ;
  - scène centrée horizontalement et posée en bas de `area` ;
  - les calques sont dessinés dans l'ordre, et `area` découpe tout.
- **Enchaînement** (temps lu à la demande dans `time`, aucun minuteur interne) :
  - **Changement d'état** : un clip du nouvel état démarre tout de suite. `Done` joue son clip une fois, puis passe à `Idle` en commençant par une pause.
  - **Fin d'un clip** : une pause à la pose `Rest`, d'une durée tirée au hasard :

    | État | Pause |
    |---|---|
    | `Idle` | 4 à 12 s |
    | `Working` | 0,5 à 2 s |
    | `Waiting` | 1 à 3 s |

  - **Clip suivant** : un autre que le précédent dès qu'il y a au moins 2 variantes.
  - **Sommeil** : 10 min de suite en `Idle`, et la mascotte s'endort (pose `Sleep`, plus aucun clip) jusqu'au prochain changement d'état.

- [ ] **Step 1: Tests** — FakeTimeProvider et `new Random(42)`
  - `Sprite_ParsesAndDraws` : un sprite 2×1 « o. » à `pixel` 4 donne un carré 4×4 coloré, puis du transparent.
  - `Sprite_UnknownChar_Throws`.
  - `Easing_Endpoints` : 0 → 0 et 1 → 1 pour chaque courbe, et `OutBack(0.5)` > 1.
  - `Layer_PositionIsWholePixels`.
  - `Animator_StateChange_StartsClipImmediately`.
  - `Animator_AfterClip_PausesAndStopsAnimating` : `IsAnimating` faux, et `UntilNextChange` entre 4 et 12 s en `Idle`.
  - `Animator_NoImmediateRepeat` : 20 clips de suite, jamais deux fois le même nom d'affilée.
  - `Animator_Done_PlaysOnceThenIdle`.
  - `Animator_Idle10Min_Sleeps` : `IsSleeping`, `UntilNextChange` infini ; puis `SetState(Working)` le réveille.
  - `Animator_Draw_StaysInsideArea` : aucun pixel peint hors de `area`, quel que soit l'instant.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Commit** — `feat(agents): moteur d'animation en pixels (images, interpolations, variantes, pauses)`

---

### Task 8: Mascottes Claude et Codex

**Files:**
- Create: `src/Trofeo.Modules.Agents/Mascots/ClaudeMascot.cs`, `CodexMascot.cs`, `Particles.cs`
- Modify: `tools/Trofeo.AgentPreview/Program.cs` (option `--mascot-sheet <dossier>`)
- Test: `tests/Trofeo.Modules.Agents.Tests/MascotDefinitionTests.cs`

**Interfaces:**
- Consumes : tout le moteur de la tâche 7.
- Produces :
  - `public static class ClaudeMascot { public static MascotDefinition Definition { get; } }` ;
  - `public static class CodexMascot { public static MascotDefinition Definition { get; } }` ;
  - `Particles.Burst(…)` : calques de particules d'un pixel, avec un tirage à graine fixe pour que les confettis et les cœurs se répètent à l'identique ;
  - `AgentPreview --mascot-sheet <dossier>` : un PNG par clip, avec les poses côte à côte (pixel 8), sur fond `Themes.Classique.Background`.

**Scène** : 24 × 16 pixels pour les deux mascottes. Le personnage, en 16 × 10, est posé en bas et centré ; les accessoires occupent le reste. Ce dessin est une **base** : l'implémenteur peut l'ajuster d'un pixel, et Alex le valide sur la feuille d'aperçu.

**Claude (« Clawd »)**. Palette : `o` `#D97757` (corps), `k` `#1A1A1A` (yeux), `y` `Theme.Warning` `#F2B84B` (drapeau), `g` `#8C929C` (haltère), `w` `#ECEEF0`. Pose de repos :

```
................
..oooooooooooo..
..oooooooooooo..
..oookooookooo..
oooookooookooooo
oooooooooooooooo
..oooooooooooo..
..oooooooooooo..
..o.o......o.o..
..o.o......o.o..
```

Yeux : colonnes 5 et 10, rangées 3 et 4 (corrigé pendant la construction : en 6 et 11, le visage était décentré). Bras : rangées 4 et 5, colonnes 0-1 et 14-15. Pattes : rangées 8 et 9.

| État | Variantes (≥ 2, sauf Terminé) |
|---|---|
| Inactif | **« balade »** : 8 poses de 110 ms, pattes alternées, avec un pas d'un pixel par pose de gauche à droite et retour. **« regarde »** : yeux à gauche (400 ms), au centre, à droite (400 ms), puis un clignement (yeux fermés 120 ms). |
| Travaille | **« haltère »** : bras droit levé et baissé, haltère `g` 5×3 dans la main, 4 poses de 140 ms × 3. **« ordinateur »** (remplace « développé » à la demande d'Alex) : tourné vers un portable de profil posé à sa gauche, le bras gauche tape, 8 poses de 140 ms dont un clignement. |
| Attend | **« drapeau »** : bras levé tenant un mât `w` et un drapeau `y` 5×3 en 2 formes alternées, 6 × 130 ms. **« saut-drapeau »** : même drapeau, avec un saut de 2 pixels en `OutQuad` à l'aller et `InQuad` au retour. |
| Terminé | **« confettis »** : 24 particules (`Accent`, `Warning`, `o`, `w`) qui partent du haut du corps en `OutQuad`, 10 poses de 100 ms. |

`Sleep` : la pose de repos avec les yeux fermés (une rangée `k` horizontale de 2 pixels) et trois pixels « z » `w` en escalier au-dessus.

**Codex** (le « pet » officiel redessiné, spec 6.3). Palette : `b` `#7AA7FF` (nuage), `l` `#B8D0FF` (reflet), `s` `#1B2233` (écran), `t` `#E9EEF5` (texte de l'écran), `y` `#F2B84B`, `h` `#FF7A9A` (cœurs). Repos : un nuage arrondi de 16 × 10, avec un reflet `l` en haut à gauche et un écran `s` de 10 × 5 au centre. Dans l'écran, `>_` en `t` : un chevron de 3 pixels de haut et un trait bas de 3 pixels.

| État | Variantes |
|---|---|
| Inactif | **« dort »** : écran sombre avec `-_`, et trois « z » qui montent en 6 poses de 300 ms. **« fredonne »** : une note de musique `t` qui monte en diagonale, le nuage balancé d'un pixel, 8 × 150 ms. |
| Travaille | **« tape »** : un portable `g` 8×3 devant, deux mains `l` alternées, et `>_` qui clignote, 6 × 110 ms. **« tape-vite »** : 10 × 85 ms, avec des lignes de texte `t` qui défilent dans l'écran. |
| Attend | **« question »** : un « ? » `y` de 3×5 au-dessus, rebond d'un pixel, 6 × 140 ms. **« exclamation »** : un « ! » `y`, qui apparaît et disparaît, 6 × 130 ms. |
| Terminé | **« cœurs »** : 6 cœurs `h` de 3×3 et 10 étincelles `t` qui montent en `OutQuad`, 10 × 100 ms. |

`Sleep` : la pose « dort » figée.

- [ ] **Step 1: Tests** — un `[Theory]` sur les deux définitions
  - `EveryPose_AtLeast83ms`.
  - `EachState_HasTwoVariants_ExceptDone`.
  - `Layers_StayInsideScene` : pour chaque calque, `From` et `To` gardent le sprite entier dans la scène.
  - `ClipNames_UniquePerState`.
  - `Rest_HasNoPropsOutsideBody`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Feuilles d'aperçu** — Run : `dotnet run --project tools/Trofeo.AgentPreview -- --mascot-sheet <scratchpad>/mascots`. Le contrôleur ouvre les PNG, les regarde et les envoie à Alex pour avis. Les retouches de dessin ne bloquent pas la suite.
- [ ] **Step 6: Commit** — `feat(agents): mascottes pixel Claude et Codex, plusieurs variantes par état`

---

### Task 9: Le module (sélection, dessin, fabriques)

**Files:**
- Create: `src/Trofeo.Modules.Agents/AgentSettings.cs`, `AgentFlavor.cs`, `AgentModule.cs`, `AgentLayout.cs`, `AgentPainter.cs`, `ClaudeCodeFactory.cs`, `CodexFactory.cs`
- Modify: `tools/Trofeo.AgentPreview/Program.cs` (option `--render <png> [--agent claude|codex] [--view feed|overview] [--size 6x3|8x4|10x4]`)
- Test: `tests/Trofeo.Modules.Agents.Tests/AgentSettingsTests.cs`, `AgentModuleTests.cs`, `AgentLayoutTests.cs`, `FakeAgentSource.cs`, `FakeModuleLog.cs`

**Interfaces:**
- Consumes : `IAgentSource` et le modèle (tâche 1), `MascotAnimator` et les mascottes (tâches 7 et 8), `ModuleDrawing` (plan 2), `ICommandableModule` (tâche 0).
- Produces (exact) :

```csharp
public enum AgentView { Feed, Overview }
public sealed record AgentSettings(AgentView View = AgentView.Feed, bool AutoFollow = true)
{ public static AgentSettings Parse(JsonElement json); } // {"view":"feed"|"overview","autoFollow":true}
public sealed record AgentFlavor(string NoSessionText, string NotInstalledText, MascotDefinition Mascot);
public sealed class AgentModule(ModuleDescriptor descriptor, AgentFlavor flavor, IAgentSource source,
    AgentSettings settings, TimeProvider time, IModuleLog log, Random random) : IModule, ICommandableModule
{
    internal string? SelectedSessionId { get; }
    internal MascotState MascotState { get; }
    internal AgentSnapshot? Snapshot { get; }
    internal void PollNow();             // un sondage synchrone, pour les tests
}
public sealed class ClaudeCodeFactory(Func<ModuleContext, IAgentSource> createSource) : IModuleFactory
{ public static ClaudeCodeFactory CreateProduction(); }   // ClaudeSource(ClaudeHome.Resolve(), ClaudeSource.IsProcessAlive)
public sealed class CodexFactory(Func<ModuleContext, IAgentSource> createSource) : IModuleFactory
{ public static CodexFactory CreateProduction(); }        // CodexSource(CodexHome.Resolve(), ctx.Time)
internal sealed record AgentLayoutResult(SKRect Tabs, SKRect Header, SKRect Body, SKRect Bar, SKRect Mascot,
    int TabCapacity, int FeedCapacity);
internal static class AgentLayout { public static AgentLayoutResult Compute(SKRect bounds, GridSize size, float textScale); }
```

- **Descripteurs** :
  - `claudecode`, « Claude Code » ; `codex`, « Codex » ;
  - minimum 6×3 ; Compact 6×3, Idéal 8×4, Large 10×4.
- **Saveurs** : les textes exacts des contraintes globales, avec `ClaudeMascot` ou `CodexMascot`.

**Comportement** :
- **Sondage** :
  - un minuteur de 1 s (`time.CreateTimer`) appelle `source.Poll(selected, now)` hors du thread de rendu, sans chevauchement ;
  - premier sondage au `StartAsync` ;
  - le résultat est remplacé d'un bloc (`Volatile`).
- **Panne de la source** : `log.Error`, dernier résultat gardé ; `Offline` après 3 échecs de suite, retour à la normale au premier succès.
- **État du module** :
  - `Installed` faux : `NotConfigured` avec le texte « n'est pas installé » en `ActionMessage` ;
  - sinon `Online`.
  - `StatusChanged` est levé à chaque changement.
- **Onglet affiché** :
  1. Aucun, ou disparu : la session au `LastActivityAt` le plus récent.
  2. `HandleCommand` : `NextTab` et `PreviousTab` bouclent ; `SelectTab(i)` n'agit que si `i` < nombre de sessions ; sinon `false`. Chaque commande traitée note l'heure (choix manuel), lance un sondage immédiat sur le pool de threads et invalide.
  3. `AutoFollow` : une session qui **passe** à `Waiting` entre deux sondages devient l'onglet affiché, sauf choix manuel de moins de 2 min.
- **Mascotte** :
  - l'activité de la session affichée donne l'état : `Working`, `Waiting` ou `Idle` ;
  - la même session qui passe de `Working` à `Idle` déclenche `Done` ;
  - aucune session : `Idle`.
- **Rafraîchissement** :
  - `Invalidated` quand la signature du résultat change. La signature est une sérialisation JSON de ce qui est affiché, avec la minute de la durée ;
  - `IsAnimating` = `animator.IsAnimating` ;
  - pendant une pause, un minuteur à un coup réveille à `UntilNextChange()` et invalide ;
  - `Dispose` arrête les minuteurs et libère la source.

**Mise en page** (`AgentLayout`, tailles × `textScale`) :
- **Marges** : 16 px.
- **Barre du bas** : 64 px de haut, filet `Text` à 10 % au-dessus.
- **Colonne des onglets** :
  - 200 px de large à partir de 7 colonnes, 132 px en dessous ;
  - un onglet fait 46 px de haut, avec 6 px entre deux.
- **À droite des onglets** :
  - une ligne d'en-tête de 36 px ;
  - le corps ;
  - des lignes de flux de 34 px.
- **Capacités** : `TabCapacity` et `FeedCapacity` sont les nombres de lignes entières qui tiennent.

**Dessin** (`AgentPainter`) :
- **Onglet** :
  - point de 12 px (couleurs des contraintes globales) ;
  - nom du projet en 22 `Text`, ellipsé ;
  - onglet affiché : fond `Text` à 8 % et liseré `Accent` de 3 px à gauche.
  - S'il y a plus de sessions que de places, la dernière place affiche « +N » en `TextMuted`.
- **En-tête** : la tâche en cours, 26 `Text`, ellipsée (rien si `null`).
- **Flux** :
  - le plus récent en haut ;
  - outil en 22 `Accent` sur 96 px, cible en 22 `Text`, ellipsée ;
  - compteurs à droite en 20 : « +12 » en `#7CE0A3`, « −3 » en `#F28B82` ;
  - opacité de 100 % pour la première ligne, puis −18 % par ligne, 25 % au moins.
- **Vue d'ensemble** :
  - l'action en cours (dernière entrée du flux si la session travaille, sinon rien) en 24 `Text` ;
  - « Tâches 3/7 » en 18 `TextMuted`, avec une barre ;
  - puis les tâches : case de 14 px (vide = à faire, pleine `Accent` = faite, point `#E8875B` = en cours) et texte en 22.
- **Barre du bas**, de gauche à droite :
  1. la mascotte, dans une zone de 96 × 64 px (pixels de sprite de 4 ; agrandie pendant la construction) ;
  2. « Contexte » / « Durée » / « Modèle » : libellé en 15 `TextMuted`, valeur en 24 `Text` :
     - `AgentText.Percent` ;
     - `AgentText.Duration(now − TaskStartedAt)` ;
     - `ModelNames.Display` ;
     - « — » si absent.
  3. à droite, deux blocs de 170 px, « 5 h » et « Hebdo » :
     - libellé en 15 `TextMuted` et pourcentage en 20 ;
     - barre de 8 px ;
     - `AgentText.Reset` en 14 `TextMuted` ;
     - sans aucun quota : « Quotas indisponibles » en 16 `TextMuted`, aligné à droite.
  - En 6 colonnes, le champ « Modèle » est omis.
- **Aucune session** : `NoSessionText` centré dans le corps, en 24 `TextMuted`.
- **Détail pas encore lu** pour l'onglet affiché : « Chargement… ».
- **`ActionMessage`** : `ModuleDrawing.DrawActionMessage` sur le corps.

- [ ] **Step 1: Tests** — `FakeAgentSource` (des résultats en file, qui enregistre `selectedSessionId`) et FakeTimeProvider
  - `Settings_Parse` (vue d'ensemble, `autoFollow` faux, valeurs inconnues → défauts).
  - `Select_DefaultsToMostRecentActivity`.
  - `Commands_NextPreviousWrap`, `Command_SelectOutOfRange_ReturnsFalse`.
  - `Command_SetsSelectionPassedToNextPoll`.
  - `AutoFollow_SwitchesToNewlyWaiting`, `AutoFollow_SuppressedAfterManualChoice` (90 s) et `AutoFollow_Disabled_NoSwitch`.
  - `Mascot_BusyToIdle_PlaysDone`.
  - `Status_NotInstalled_NotConfiguredWithMessage`.
  - `Status_ThreeFailures_Offline_ThenRecovers`.
  - `Invalidated_OnlyWhenSignatureChanges`.
  - `Layout_TabCapacity_8x4` (6 à l'échelle 1 : 434 − 2 × 16 − 64 = 338 px pour des onglets de 46 + 6 px), `Layout_6x3_NarrowTabs`.
  - `Render_AllSizesAndViews_StayInside` : 6×3, 8×4 et 10×4, × `TextSize`, × les deux vues ; aucun pixel hors du module.
  - `Render_Overflow15Sessions_ShowsPlusN` : la dernière place affiche « +10 » à l'échelle 1, en 8×4 (6 places : 5 onglets puis « +10 »). `AgentPainter` a pour cela une propriété `internal List<string>? DrawnTexts` : si elle n'est pas `null`, chaque texte dessiné y est ajouté.
  - `Render_NoSessions_DrawsMessage`.
  - `Render_NoQuotas_DrawsUnavailable`.
  - `Render_JapaneseProjectName_StaysInside`.
  - `Factories_Descriptors`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Agents.Tests`
- [ ] **Step 5: Aperçu** — Run : `dotnet run --project tools/Trofeo.AgentPreview -- --render <scratchpad>/claude-8x4.png`, puis les variantes `--view overview`, `--agent codex` et `--size 6x3`.
  - Le rendu fait 1920 × 462 : le module en (0, 0, 8×4) sur le fond Classique, avec des données d'exemple écrites dans l'outil (4 sessions dans les trois états, un flux avec un Edit +12 / −3, des quotas).
  - Le contrôleur regarde les images, corrige ce qui est illisible, puis les envoie à Alex.
- [ ] **Step 6: Commit** — `feat(agents): modules Claude Code et Codex (onglets, flux, vue d'ensemble, barre, mascotte)`

---

### Task 10: Raccourcis clavier globaux (Windows)

**Files:**
- Create: `src/Trofeo.Core/Shortcuts/ShortcutBinding.cs`, `ShortcutKeys.cs`, `ShortcutDefaults.cs`
- Modify: `src/Trofeo.Core/Layout/DashboardLayout.cs` (paramètre final `IReadOnlyList<ShortcutBinding>? Shortcuts = null`)
- Create: `src/Trofeo.Display/HotkeyRouter.cs`, `HotkeyWindow.cs`
- Modify: `src/Trofeo.Display/DisplayLoop.cs`, `src/Trofeo.Display/Program.cs`
- Test: `tests/Trofeo.Core.Tests/ShortcutKeysTests.cs`, `LayoutStoreTests.cs`, `tests/Trofeo.Display.Tests/HotkeyRouterTests.cs`, `DisplayLoopTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record ShortcutBinding(string Keys, ModuleCommandKind Command, int Index = 0, Guid? Target = null);
public static class ShortcutKeys { public static bool TryParse(string keys, out uint modifiers, out uint virtualKey); }
public static class ShortcutDefaults { public static IReadOnlyList<ShortcutBinding> All { get; } }
public interface IHotkeyRegistrar { bool Register(int id, uint modifiers, uint virtualKey); void Unregister(int id); }
public sealed class HotkeyRouter(IHotkeyRegistrar registrar, IReadOnlyList<ShortcutBinding> bindings,
    Func<Guid?, ICommandableModule?> resolveTarget) : IDisposable
{ public void RegisterAll(); public void OnHotkey(int id); }
public sealed class HotkeyWindow : NativeWindow, IHotkeyRegistrar, IDisposable { public event Action<int>? Hotkey; }
// DisplayLoop :
public ICommandableModule? FindCommandTarget(Guid? instanceId);
```

**Règles :**
- **Syntaxe des touches** : des mots séparés par « + », sans tenir compte de la casse.
  - Modificateurs : `Ctrl` (2), `Alt` (1), `Shift` (4), `Win` (8).
  - Une seule touche finale : `Left` / `Right` / `Up` / `Down` (0x25 à 0x28), `0` à `9` (0x30 à 0x39), `A` à `Z`, `F1` à `F24` (0x70 à 0x87).
  - Au moins un modificateur. Sinon : `false`.
- **Valeurs par défaut** :
  - `Ctrl+Alt+Left` → `PreviousTab` ;
  - `Ctrl+Alt+Right` → `NextTab` ;
  - `Ctrl+Alt+1` à `Ctrl+Alt+9` → `SelectTab` 0 à 8 ;
  - sans `Target`.
- **Routeur** :
  - identifiants 1 à N dans l'ordre des liaisons, enregistrés avec `MOD_NOREPEAT` (0x4000) ;
  - touches illisibles ou enregistrement refusé : `Trace.TraceWarning("Raccourci « {Keys} » indisponible…")`, puis on continue ;
  - `OnHotkey` résout la cible et appelle `HandleCommand`, en interceptant ses exceptions (journalisées) ;
  - `Dispose` retire tout ce qui a été enregistré.
- **Cible** (`FindCommandTarget`) : l'instance demandée si elle accepte les commandes ; sinon le premier module qui les accepte, dans l'ordre (colonne, rangée).
- **Câblage** : la fenêtre est créée sur le thread de l'interface avant `Application.Run`. Elle traite `WM_HOTKEY` (0x0312), puis libère tout à l'arrêt.
- **Ancienne disposition** : un `layout.json` sans `Shortcuts` se lit avec `null`, et prend donc les valeurs par défaut.

- [ ] **Step 1: Tests**
  - `TryParse_Table` : `Ctrl+Alt+Right` → (3, 0x27) ; `ctrl+shift+F5` → (6, 0x74) ; `Alt+1` → (1, 0x31) ; `Right`, `Ctrl+` et `Ctrl+Alt+Foo` → `false`.
  - `Defaults_ElevenBindings`.
  - `Layout_WithShortcuts_RoundTrips` et `Layout_OldFile_ShortcutsNull`.
  - `Router_RegistersAllWithNoRepeat`.
  - `Router_RegisterFails_ContinuesWithOthers`.
  - `Router_InvalidKeys_Skipped`.
  - `Router_OnHotkey_SendsCommandToTarget`.
  - `Router_HandlerThrows_Logged`.
  - `Router_Dispose_UnregistersAll`.
  - `FindCommandTarget_FirstByColumn`, `FindCommandTarget_ExplicitInstance`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test`
- [ ] **Step 5: Commit** — `feat(display): raccourcis clavier globaux pour changer d'onglet`

---

### Task 11: Câblage, disposition par défaut, outils, README et vérification avec Alex

**Files:**
- Modify: `src/Trofeo.Display/Program.cs`, `src/Trofeo.Display/Trofeo.Display.csproj` (→ Agents), `src/Trofeo.Core/Layout/DashboardLayout.cs`, `tests/Trofeo.Core.Tests/LayoutStoreTests.cs`, `tests/Trofeo.Display.Tests/DefaultLayoutTests.cs`
- Modify: `tools/Trofeo.HardwareCheck/Program.cs`, `Trofeo.HardwareCheck.csproj` (→ StatusLine), `README.md`
- Create: `docs/superpowers/plans/2026-10-25-plan-3-bilan.md` (à la fin)

**Interfaces:**
- Produces :
  - `Program` ajoute `ClaudeCodeFactory.CreateProduction()` et `CodexFactory.CreateProduction()` au registre.
  - `DashboardLayout.Default` ajoute Claude Code en (0, 0, 8×4), avec l'identifiant fixe `3c1d9f5e-7a2b-4e8c-9b1f-6d2a0e4c8b17` et `{"view":"feed","autoFollow":true}`.
  - `HardwareCheck --claude-statusline install <chemin de Trofeo.StatusLine.exe> | uninstall | status` : affiche en français le résultat de `StatusLineInstaller` et le chemin de `settings.json`.
  - **README** :
    - mettre à jour « Ce qui marche aujourd'hui » : Claude Code, Codex, raccourcis ;
    - expliquer l'installation de la barre d'état ;
    - ajouter la mention « Mascottes : redessins de fan de Clawd (Anthropic) et du pet Codex (OpenAI). Projet non affilié à Anthropic, OpenAI ni Thermalright. »

- [ ] **Step 1: Test** — `Default_HasFiveModules_NoOverlap` remplace celui du plan 2 ; `LayoutValidator` passe avec le registre réel.
- [ ] **Step 2: Implémenter**, puis lancer `dotnet build -c Release` et `dotnet test`. Expected : `0 Warning(s)`, tout passe, y compris les 728 tests existants.
- [ ] **Step 3: Commit** — `feat(display): modules Claude Code et Codex branchés, disposition par défaut à cinq modules`
- [ ] **Step 4: Vérification sur l'écran avec Alex.** C'est le contrôleur qui la mène, pas un sous-agent.
  1. Arrêter le `Trofeo.Display.exe` en cours et sauvegarder le `layout.json` d'Alex.
  2. Lui donner une commande PowerShell à lancer **dans son propre terminal** (piège MSIX) pour ajouter l'emplacement Claude Code à son `layout.json`. Il confirme d'abord le contenu.
  3. Il lance le service.

  Alex vérifie :
  - un onglet par session ouverte (appli desktop et terminal), au bon nom de projet ;
  - les points orange, jaune et gris qui suivent ce qu'il fait ;
  - le flux, qui défile pendant que Claude travaille ;
  - les confettis à la fin d'une réponse ;
  - le drapeau quand Claude pose une question ;
  - `Ctrl+Alt+←/→/1…9` ;
  - la lisibilité d'un coup d'œil.

  Noter ses retouches de taille et de dessin et les appliquer dans la foulée.
- [ ] **Step 5: Barre d'état, avec l'accord d'Alex.**
  1. Il lance `Trofeo.HardwareCheck.exe --claude-statusline install <chemin>` dans son terminal.
  2. Il ouvre une nouvelle session dans l'appli desktop **et** une dans un terminal.
  3. **Risque à lever** (spec 6.2) : un fichier de capture apparaît-il pour la session desktop ? Le noter dans le bilan.
  4. Les quotas s'affichent, ou « Quotas indisponibles » si aucune session ne fait tourner la barre d'état.
  5. Vérifier que sa barre d'état d'origine, s'il en avait une, s'affiche toujours.
- [ ] **Step 6: Sobriété.** Mesurer 5 min avec la mascotte en activité, puis 5 min au repos (mascotte endormie). Objectif : environ 1 % de CPU en moyenne et moins de 100 Mo de mémoire propre. Si l'animation coûte trop, allonger les pauses : c'est le seul réglage à toucher.
- [ ] **Step 7: Bilan** — écrire `2026-10-25-plan-3-bilan.md` sur le modèle des bilans précédents : état, mesures, décisions, ce qui revient aux plans 4 et 5, et ce qu'il reste à vérifier sur Codex.
