# Plan 2 — Modules de données

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter au service d'affichage les quatre modules de données :
- Monitoring PC, avec l'assistant capteurs élevé et le pilote PawnIO ;
- En écoute ;
- Météo dans le module Heure ;
- Réseau, avec trois sources : Freebox, UniFi ou la carte réseau du PC.

Le plan règle aussi les points laissés ouverts par le plan 1.

**Architecture:**
- **Contrat de module.** Il passe à un `ModuleContext` (identifiant d'instance, réglages, temps, journal, coffre à secrets) et gagne un événement `StatusChanged`.
- **Capteurs.** `Trofeo.Sensors` traduit LibreHardwareMonitor en un `SensorSnapshot` neutre. Ce relevé vient de l'assistant élevé, par un tube nommé en lecture seule, ou d'une lecture locale sans droits quand l'assistant est absent.
- **Modules.** Chaque module est un projet `Trofeo.Modules.<Nom>`. Il reçoit ses services (capteurs, HTTP, source média) par le constructeur de sa fabrique, et sa logique pure est testée sans matériel ni réseau.

**Tech Stack:**
- .NET 10, C#, SkiaSharp 3.119 ;
- LibreHardwareMonitorLib 0.9.6 (MPL-2.0) ;
- System.IO.Pipes.AccessControl ;
- System.Security.Cryptography.ProtectedData (DPAPI) ;
- WinRT `Windows.Media.Control` (SMTC) ;
- HttpClient, System.Net.NetworkInformation ;
- xUnit et FakeTimeProvider.

**Spec:** `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`. Voir aussi le bilan du plan 1 : `docs/superpowers/plans/2026-10-24-plan-1-bilan.md`.

## Global Constraints

- **Identité `trofeo-HB-dashboard`** pour les dossiers, mutex, tubes, tâche planifiée et `app_id` Freebox. Elle ne change jamais.
- **Droits.** Le service d'affichage tourne **sans droits administrateur**. Seul l'assistant capteurs est élevé :
  - il est installé dans `%ProgramFiles%\trofeo-HB-dashboard\sensors\` ;
  - il est lancé par la tâche planifiée `\trofeo-HB-dashboard\Capteurs` ;
  - il n'accepte **aucune donnée entrante** : il ne fait qu'écrire.
- **Secrets.** Le jeton Freebox et la clé UniFi sont chiffrés par DPAPI (utilisateur courant) dans `%AppData%\trofeo-HB-dashboard\secrets.dat`. Ils ne vont jamais dans `layout.json`.
- **Une source qui tombe** fait passer **son** module au rouge (`ModuleHealth.Offline`), sans exception remontée au service ni effet sur les autres modules.
- **Action à faire** : un module qui attend une action de l'utilisateur la met dans `ModuleStatus.ActionMessage` et l'écrit dans sa zone.
- **Rythmes de lecture** (sobriété, spec 4.3) :

  | Source | Rythme |
  |---|---|
  | Capteurs | 1 s |
  | Freebox | 1 s (2 s pendant l'autorisation) |
  | UniFi | 5 s |
  | Carte réseau du PC | 1 s |
  | Météo | 15 min, nouvel essai 2 min après un échec |
  | Progression musicale | 1 s, pendant la lecture seulement |

- **Adresses contactées**, et aucune autre :
  - `http://mafreebox.freebox.fr` ;
  - l'hôte UniFi saisi par l'utilisateur ;
  - `https://api.open-meteo.com` ;
  - `https://geocoding-api.open-meteo.com`.
- **Textes à l'écran en français.** Copie exacte de la spec : « Appuie sur ► sur ta Freebox pour autoriser le dashboard », « Rien en lecture », « Freebox », « UniFi », « Ce PC ».
- **Tailles par défaut lisibles** : les températures du Monitoring sont **aussi grandes que les pourcentages**. Les tailles de police sont toujours multipliées par `ctx.TextScale`.
- **Référence visuelle** : la maquette Classique `.superpowers/brainstorm/4687-1792861134/content/themes-v3.html` (classes `td-mon`, `td-music`, `td-clock`, `td-net`). Les tailles sont données à l'échelle 1920 px. Le thème Classique actuel (`Themes.Classique`) reste le seul thème ; les autres arrivent au plan 4.
- **Projets.**
  - `net10.0` : ModuleKit, Core, Modules.Clock, Modules.Network.
  - `net10.0-windows10.0.19041.0` : Sensors, SensorHelper, Modules.Monitoring, Modules.NowPlaying, Display, et tous les tests.
- `TreatWarningsAsErrors` reste actif, ainsi que la licence GPL-3.0.

## Review Focus

1. **Accès Freebox révoqué pendant que le service tourne** (application supprimée dans Freebox OS) : le module redemande l'autorisation et réaffiche le message, sans planter. Test `Login_InvalidToken_RestartsAuthorization` (tâche 12).
2. **Un autre programme ouvre le tube des capteurs avant l'assistant**, pour envoyer de fausses valeurs : le service refuse ses données et repasse en lecture locale. Tests `Client_UntrustedServer_IgnoresSnapshots` (tâche 6) et `Hub_HelperSilent_FallsBackToLocal` (tâche 6).
3. **Titre de musique en japonais, avec un emoji, ou très long** : il est dessiné avec une police de secours et coupé par « … », sans déborder du module. Tests `DrawText_MissingGlyphs_UsesFallback` et `Ellipsize_LongText_FitsWidth` (tâche 2).
4. **PC d'un ami avec un CPU AMD, un GPU AMD ou pas de GPU dédié** : les bons capteurs sont choisis, le titre omet ce qui manque et les valeurs absentes s'affichent « — ». Tests `Map_AmdCpuAndGpu`, `Map_NoGpu` et `Short_NoGpu` (tâche 5).
5. **Changement de carte réseau, VPN ou sortie de veille** (compteurs remis à zéro) : pas de débit négatif ni de pic absurde. Test `NicSource_CounterReset_NoSpike` (tâche 11).

## Décisions prises en écrivant ce plan

- **Pas de rappel « réglages modifiés » dans le contrat.** Quand le logiciel de réglage (plan 5) change les réglages d'un module, le service recrée ce module. Les secrets et les données persistantes (jeton Freebox) survivent à la recréation. *Si c'est faux :* ajouter `UpdateSettings` au contrat, sans casser les modules existants.
- **Secrets rangés par type de module** (`network`), pas par instance : deux modules Réseau partagent la même Freebox.
- **L'assistant capteurs tourne en `SYSTEM`**, déclenché à l'ouverture de toute session, avec un mutex global d'instance unique. Ainsi il marche même si l'utilisateur n'est pas administrateur au quotidien.
- **Pas de journal dans `%ProgramData%`.** L'assistant élevé écrit son journal dans son propre dossier de Program Files, qui n'est modifiable que par un administrateur. Un utilisateur ne peut donc pas y glisser un lien vers un autre fichier.
- **UniFi : `txRateBps` / `rxRateBps` lus comme des bits par seconde**, selon l'ami d'Alex. Sa capture montrait l'ancienne API (`rx_bytes-r`, en octets/s), pas `statistics/latest` : la comparaison des deux reste à faire. Si elle contredit, on ne change qu'un facteur dans `UniFiSource`.
- **Titre par défaut du Monitoring** : les noms réels du CPU et du GPU lus par LibreHardwareMonitor. S'ils sont introuvables : « Monitoring » (demande d'Alex).
- **Freebox en HTTP sur le réseau local.** Le jeton d'application n'est jamais envoyé ; seul son HMAC l'est. HTTPS demanderait d'embarquer les autorités de certification de Free.
- **UniFi en HTTPS avec certificat auto-signé** : on mémorise l'empreinte du certificat au premier contact. Un changement ensuite est refusé et signalé.
- **Module Monitoring sans températures CPU** : la ligne d'explication remplace le titre, pour ne pas faire tomber une ligne de valeurs.

---

### Task 0: Branche, projets et propriétés de test partagées

**Files:**
- Create: `src/Trofeo.Sensors/Trofeo.Sensors.csproj`, `src/Trofeo.SensorHelper/Trofeo.SensorHelper.csproj`, `src/Trofeo.Modules.Monitoring/…csproj`, `src/Trofeo.Modules.NowPlaying/…csproj`, `src/Trofeo.Modules.Network/…csproj`
- Create: `tests/Directory.Build.props`, `tests/Trofeo.Sensors.Tests/…csproj`, `tests/Trofeo.Modules.Monitoring.Tests/…csproj`, `tests/Trofeo.Modules.NowPlaying.Tests/…csproj`, `tests/Trofeo.Modules.Network.Tests/…csproj`
- Modify: les quatre `.csproj` de tests existants (retirer ce qui passe dans `tests/Directory.Build.props`), `trofeo-HB-dashboard.sln`

**Interfaces:**
- Produces :
  - **Références de projets** :
    - Sensors → Core ;
    - SensorHelper (`WinExe`) → Sensors ;
    - Modules.Monitoring → ModuleKit et Sensors ;
    - Modules.NowPlaying → ModuleKit ;
    - Modules.Network → ModuleKit ;
    - Display → les trois nouveaux modules et Sensors.
  - **Paquets** :
    - `LibreHardwareMonitorLib` 0.9.6 et `System.IO.Pipes.AccessControl` dans Sensors ;
    - `System.Security.Cryptography.ProtectedData` dans Core.
  - **`tests/Directory.Build.props`** importe le `Directory.Build.props` racine. Il fixe `TargetFramework` à `net10.0-windows10.0.19041.0` et `IsPackable=false`. Il ajoute les paquets de test (coverlet.collector 6.0.4, Microsoft.Extensions.TimeProvider.Testing 10.0.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4, aux mêmes versions qu'aujourd'hui), ainsi que `<Using Include="Xunit" />`.

- [ ] **Step 1: Vérifier que LibreHardwareMonitorLib 0.9.6 passe par PawnIO**
  - Restaurer, puis chercher la chaîne `PawnIO` dans `LibreHardwareMonitorLib.dll` (`grep -c PawnIO`).
  - Expected : au moins une occurrence.
  - Sinon, prendre la préversion la plus récente qui en contient une, et noter la version retenue dans le rapport.
- [ ] **Step 2: Créer les projets et le `tests/Directory.Build.props`, puis les ajouter à la solution**
- [ ] **Step 3: Vérifier** — Run : `dotnet build` puis `dotnet test`. Expected : `0 Warning(s)`, 137 tests qui passent.
- [ ] **Step 4: Commit** — `chore: projets du plan 2 et propriétés de test partagées`

---

### Task 1: Contrat de module v2, coffre à secrets, journal de module

**Files:**
- Create: `src/Trofeo.ModuleKit/ModuleContext.cs`, `IModuleLog.cs`, `ISecretStore.cs`
- Modify: `src/Trofeo.ModuleKit/IModule.cs`, `IModuleFactory.cs`
- Create: `src/Trofeo.Core/Secrets/DpapiSecretVault.cs`, `src/Trofeo.Core/Logging/TraceModuleLog.cs`
- Modify: `src/Trofeo.Display/ModuleRegistry.cs`, `src/Trofeo.Display/DisplayLoop.cs`, `src/Trofeo.Display/Program.cs`, `src/Trofeo.Modules.Clock/ClockFactory.cs`
- Modify: `tests/Trofeo.TestDoubles/FakeModule.cs`; Create `tests/Trofeo.TestDoubles/FakeSecretStore.cs`, `FakeModuleLog.cs`
- Test: `tests/Trofeo.Core.Tests/DpapiSecretVaultTests.cs`, `TraceModuleLogTests.cs`, `tests/Trofeo.Display.Tests/DisplayLoopTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record ModuleContext(Guid InstanceId, JsonElement Settings, TimeProvider Time, IModuleLog Log, ISecretStore Secrets);
public interface IModuleFactory { ModuleDescriptor Descriptor { get; } IModule Create(ModuleContext context); }
public interface IModuleLog { void Info(string message); void Warning(string message); void Error(string message); }
/// Secrets d'un type de module. Get rend null si absent ; Set(key, null) efface.
public interface ISecretStore { string? Get(string key); void Set(string key, string? value); }
// IModule gagne :
event EventHandler? StatusChanged; // levé depuis n'importe quel thread quand Status change
```

- `DpapiSecretVault(string filePath)` dans `Trofeo.Core.Secrets`, annoté `[SupportedOSPlatform("windows")]` :
  - `string? Get(string scope, string key)` ;
  - `void Set(string scope, string key, string? value)` ;
  - `ISecretStore Scope(string scope)`.
- `TraceModuleLog(string typeId, Guid instanceId, TimeProvider time) : IModuleLog` dans `Trofeo.Core.Logging`. Il préfixe chaque message par `[{typeId} {instanceId:N8 premiers}] `. `Error` passe par `RateLimitedErrorLog`.
- `ModuleRegistry(IEnumerable<IModuleFactory> factories, Func<string, ISecretStore> secretsFor)`. `Create(ModulePlacement p, TimeProvider time)` construit le contexte avec `secretsFor(p.TypeId)`. `Program` passe `vault.Scope` ; les tests passent `_ => new FakeSecretStore()`.
- `FakeModule` gagne `RaiseStatusChanged()`.
- `FakeSecretStore : ISecretStore`, en mémoire.
- `FakeModuleLog : IModuleLog`, qui enregistre les messages dans `List<string> Entries`.

- [ ] **Step 1: Tests du coffre**
  - `Vault_SetThenGet_RoundTrips` : nouvelle instance sur le même fichier, même valeur.
  - `Vault_File_IsNotPlainText` : les octets du fichier ne contiennent pas la valeur en UTF-8.
  - `Vault_SetNull_Removes`.
  - `Vault_Scopes_AreIsolated` : `Scope("network").Get("k")` ne voit pas `Scope("clock")`.
  - `Vault_CorruptFile_ActsEmptyAndKeepsBackup` : des octets aléatoires donnent `Get` → null et un fichier `.bad` présent.
  - `Vault_MissingFile_ActsEmpty`.
- [ ] **Step 2: Tests du journal** — `Error_SameMessage_LoggedOncePerMinute` (avec FakeTimeProvider) et `Messages_ArePrefixedWithTypeAndInstance`. Vérifier par un `TraceListener` de test.
- [ ] **Step 3: Test de la boucle** — `StatusChanged_MarksDirtyAndLogs` : `FakeModule.RaiseStatusChanged()` provoque une image au tick suivant.
- [ ] **Step 4: Lancer, constater les échecs**
- [ ] **Step 5: Implémenter**
  - **Fichier du coffre** : DPAPI `CurrentUser`, entropie `"trofeo-HB-dashboard"` en UTF-8, contenu JSON `{ scope: { key: value } }`.
  - **Écriture** atomique (fichier `.tmp`, puis déplacement).
  - **Verrou** : lecture-modification-écriture sous le mutex nommé `Local\trofeo-HB-dashboard.secrets`, parce que le logiciel de réglage écrira aussi.
  - **Relecture** du fichier à chaque `Get`.
  - **Boucle** : `DisplayLoop` s'abonne à `StatusChanged`, journalise `Module « {TypeId} » : {Health}{ — ActionMessage}` et appelle `MarkDirty`. Elle se désabonne au `Dispose`.
  - **Program** : crée le coffre sur `Path.Combine(AppPaths.SettingsDir, "secrets.dat")`.
- [ ] **Step 6: Tout repasse** — Run : `dotnet test`. Expected : tout passe, y compris les tests existants adaptés à `Create(ModuleContext)`.
- [ ] **Step 7: Commit** — `feat(modulekit): contexte de module, état observable, coffre DPAPI, journal par module`

---

### Task 2: Kit de dessin des modules

**Files:**
- Create: `src/Trofeo.ModuleKit/ModuleDrawing.cs`
- Test: `tests/Trofeo.Core.Tests/ModuleDrawingTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.ModuleKit` :

```csharp
public static class ModuleDrawing
{
    public static float MeasureText(string text, SKFont font);
    public static void DrawText(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint);
    public static string Ellipsize(string text, SKFont font, float maxWidth);           // suffixe « … » (U+2026)
    public static IReadOnlyList<string> Wrap(string text, SKFont font, float maxWidth, int maxLines); // dernière ligne ellipsée
    public static void DrawBar(SKCanvas canvas, SKRect rect, float fraction, SKColor fill, SKColor track);
    public static void DrawActionMessage(SKCanvas canvas, SKRect area, Theme theme, float textScale, string message);
}
```

**Règles :**
- **Police de secours.** `MeasureText` et `DrawText` découpent le texte en suites de caractères (par `Rune`). Les caractères absents de `font.Typeface` sont dessinés avec `SKFontManager.Default.MatchCharacter(rune)`. Les polices trouvées sont gardées dans un cache statique. Sans police de secours, on garde la police principale.
- **`DrawBar`** : arrondi = hauteur / 2. La fraction est bornée à [0, 1] ; `NaN` vaut 0. La piste est toujours dessinée.
- **`DrawActionMessage`** :
  - texte en `theme.Warning`, centré dans `area`, au plus 3 lignes ;
  - taille 24 × `textScale`, réduite par pas de 2 jusqu'à 16 × `textScale` si le texte ne tient pas en 3 lignes.

- [ ] **Step 1: Écrire les tests**
  - `Ellipsize_ShortText_Unchanged`.
  - `Ellipsize_LongText_FitsWidth` : un texte de 200 caractères donne une mesure ≤ `maxWidth`, terminée par `…`.
  - `Wrap_BreaksOnSpaces_AndRespectsMaxLines`.
  - `DrawText_MissingGlyphs_UsesFallback` :
    - garde-fou d'abord : `Assert.False(barlow.ContainsGlyph('東'))` ;
    - puis `MeasureText("東京 🎵")` > 0 ;
    - et le rendu sur un bitmap noir contient des pixels non noirs.
  - `DrawBar_NaN_DrawsTrackOnly`.
  - `DrawActionMessage_LongMessage_StaysInsideArea` : aucun pixel peint hors de `area`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Core.Tests`
- [ ] **Step 5: Commit** — `feat(modulekit): dessin de texte avec police de secours, coupure, barres, message d'action`

---

### Task 3: Robustesse reportée du plan 1

Ces points viennent de la section « À traiter avant ou pendant le plan 2 » du bilan.

**Files:**
- Modify: `src/Trofeo.Device/ScreenDevice.cs`, `src/Trofeo.Display/TrayIcon.cs`, `src/Trofeo.Core/Rendering/FrameComposer.cs`
- Move: `src/Trofeo.Display/FileTraceListener.cs` → `src/Trofeo.Core/Logging/FileTraceListener.cs` (namespace `Trofeo.Core.Logging`), avec ses tests → `tests/Trofeo.Core.Tests/FileTraceListenerTests.cs`
- Test: `tests/Trofeo.Device.Tests/ScreenDeviceTests.cs`, `tests/Trofeo.Core.Tests/FileTraceListenerTests.cs`, `FrameComposerTests.cs`

**Interfaces:**
- Produces :
  - `public sealed record ScreenStatus(ScreenState State, string? Reason);`
  - `ScreenDevice.Status`, remplacé d'un bloc (`Volatile`). `State` et `Reason` deviennent des raccourcis vers `Status`.
  - `TrayIcon` ne lit plus que `Status`.
  - `FileTraceListener(string path, long maxBytes = DefaultMaxBytes, TimeProvider? time = null)`, plus un constructeur `internal` qui accepte en plus `Func<string, Stream> open` (avec `InternalsVisibleTo Trofeo.Core.Tests`).

- [ ] **Step 1: Tests**
  - `Status_ReasonMatchesState_AfterEachTransition` : Absent → Ready → Busy → NotResponding.
  - `Write_ReopenFails_RecoversOnLaterWrite` :
    - après une rotation, `open` lève une fois ;
    - les écritures suivantes sont perdues sans exception ;
    - 60 s plus tard (FakeTimeProvider), une écriture rouvre le fichier et y apparaît.
  - `Compose_InvalidLayout_Throws` : des `Placements` à `null` font lever une exception. Le bitmap est libéré dans un `catch`, puis l'exception est relancée.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
  - `FileTraceListener` : en cas d'échec de réouverture, `_writer = null`, et un nouvel essai au plus une fois par 60 s.
  - `FrameComposer` : `using var clip = new SKRoundRect(…)` ; `try { … } catch { bitmap.Dispose(); throw; }` autour du corps de `Compose`.
  - `Program` (Display) importe `Trofeo.Core.Logging`.
- [ ] **Step 4: Tout passe** — Run : `dotnet test`
- [ ] **Step 5: Commit** — `fix: état d'écran lu d'un bloc, journal qui se rouvre, ressources Skia libérées`

---

### Task 4: Boucle d'affichage réveillée par événement

Aujourd'hui, la boucle se réveille environ 64 fois par seconde, même au repos.

**Files:**
- Modify: `src/Trofeo.Core/Rendering/FrameScheduler.cs`, `src/Trofeo.Display/DisplayLoop.cs`, `src/Trofeo.Display/Program.cs`
- Test: `tests/Trofeo.Core.Tests/FrameSchedulerTests.cs`, `tests/Trofeo.Display.Tests/DisplayLoopTests.cs`

**Interfaces:**
- Produces :
  - `FrameScheduler.Signal` (`WaitHandle`, un `AutoResetEvent` levé par `MarkDirty` et `Wake`) ;
  - `FrameScheduler.Wake()` ;
  - `bool FrameScheduler.IsDirty` ;
  - `TimeSpan FrameScheduler.UntilNextSlot()` : 0 si aucune image n'a été rendue, sinon `max(0, MinInterval − écoulé)`.
  - `TimeSpan DisplayLoop.Tick()` : le délai avant la prochaine itération utile, borné à [0, 1 s].
    - Écran non prêt : le temps restant avant le prochain essai de connexion.
    - Écran prêt : le minimum entre le renvoi d'image, le sondage de présence et, si `IsDirty` ou si un module s'anime, `UntilNextSlot()`.
  - `RequestReconnect` appelle `Wake()`.
  - `Program.TickLoop` : `WaitHandle.WaitAny([ct.WaitHandle, scheduler.Signal], loop.Tick())`.

- [ ] **Step 1: Tests**
  - `MarkDirty_SetsSignal`.
  - `UntilNextSlot_AfterRender_IsRemainingInterval`.
  - `Tick_Idle_ReturnsTimeUntilKeepAlive` : juste après un envoi, environ 1 s ; 400 ms plus tard, environ 600 ms.
  - `Tick_Animating_ReturnsFrameInterval` : environ 83 ms.
  - `Tick_Disconnected_ReturnsTimeUntilReconnect` : 2 s, puis 0 à l'échéance.
  - `RequestReconnect_SetsSignal`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter** et retirer `TickInterval` de `Program`.
- [ ] **Step 4: Tout passe** — Run : `dotnet test`
- [ ] **Step 5: Commit** — `perf(display): boucle réveillée par événement au lieu de 64 fois par seconde`

---

### Task 5: Capteurs — relevé neutre, choix des capteurs, noms courts, lecteur LHM

**Files:**
- Create: `src/Trofeo.Sensors/SensorSnapshot.cs`, `SensorKeys.cs`, `RawSensor.cs`, `SensorMapper.cs`, `HardwareNames.cs`, `ISensorReader.cs`, `LhmSensorReader.cs`, `MemoryStatus.cs`
- Test: `tests/Trofeo.Sensors.Tests/SensorMapperTests.cs`, `HardwareNamesTests.cs`, `LhmSensorReaderTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record SensorSnapshot(string? CpuName, string? GpuName, IReadOnlyDictionary<string, double> Values, bool Elevated);
public static class SensorKeys
{
    public const string CpuLoad = "cpu.load", CpuTemp = "cpu.temp", CpuClock = "cpu.clock", CpuPower = "cpu.power";
    public const string GpuLoad = "gpu.load", GpuTemp = "gpu.temp", GpuClock = "gpu.clock", GpuPower = "gpu.power";
    public const string GpuVramUsed = "gpu.vram.used", GpuVramTotal = "gpu.vram.total", GpuFan = "gpu.fan";
    public const string RamUsed = "ram.used", RamTotal = "ram.total", DiskTemp = "disk.temp", DiskUsed = "disk.used";
}
public enum RawHardware { Cpu, GpuNvidia, GpuAmd, GpuIntel, Storage, Other }
public enum RawKind { Load, Temperature, Clock, Power, SmallData, Fan, Other }
public sealed record RawSensor(RawHardware Hardware, string HardwareName, RawKind Kind, string Name, float? Value);
public static class SensorMapper { public static SensorSnapshot Map(IEnumerable<RawSensor> sensors, bool elevated); }
public static class HardwareNames { public static string Short(string? cpuName, string? gpuName); }
public interface ISensorReader : IDisposable { SensorSnapshot Read(); }
public sealed class LhmSensorReader : ISensorReader { } // ajoute RAM et disque système aux valeurs mappées
```

**Unités** :

| Valeur | Unité |
|---|---|
| Charges, `disk.used` | % |
| Températures | °C |
| Fréquences | MHz |
| Puissances | W |
| VRAM | Mo |
| Ventilateur | tr/min |
| RAM | Go (2^30 octets) |

**Choix des capteurs** (`SensorMapper.Map`, code pur) :
- **Valeurs écartées** : `null`, `NaN`, infinies, négatives, et températures > 150.
- **CPU** : le premier matériel `Cpu`.
  - `cpu.load` : Load « CPU Total ».
  - `cpu.temp` : la première présente parmi « CPU Package », « Core (Tctl/Tdie) », « Core Max », « Core Average ». Sinon, le maximum des températures du CPU.
  - `cpu.clock` : le maximum des Clock dont le nom ne contient pas « Bus ».
  - `cpu.power` : la première Power dont le nom contient « Package ».
- **GPU** : le premier `GpuNvidia`, sinon `GpuAmd`, sinon `GpuIntel`.
  - Load, Temperature et Clock « GPU Core ».
  - `gpu.power` : « GPU Package », sinon la première Power.
  - `gpu.vram.used` / `gpu.vram.total` : SmallData « GPU Memory Used » / « GPU Memory Total ».
  - `gpu.fan` : le premier Fan.
- **Disque** : `disk.temp` est le maximum des températures des matériels `Storage`.
- **Ajouts du lecteur**, hors LHM : `ram.*` par `GlobalMemoryStatusEx` (`[LibraryImport]`), `disk.used` par `DriveInfo` du lecteur système.

**Noms courts** (`HardwareNames.Short`) :
- Retirer « Intel », « AMD », « NVIDIA », « GeForce », « Radeon », « (R) », « (TM) », « ® », « ™ », « Processor », « Graphics », « with Radeon Graphics » et le suffixe « N-Core ».
- Réduire les espaces.
- Joindre CPU et GPU par « · » en sautant les absents. Si les deux manquent : « Monitoring ».

- [ ] **Step 1: Tests des noms**

```csharp
[Theory]
[InlineData("Intel Core Ultra 7 265K", "NVIDIA GeForce RTX 5070", "Core Ultra 7 265K · RTX 5070")]
[InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", "AMD Radeon RX 7800 XT", "Ryzen 7 7800X3D · RX 7800 XT")]
[InlineData("Intel(R) Core(TM) i5-12400F", "Intel(R) Arc(TM) A770 Graphics", "Core i5-12400F · Arc A770")]
public void Short_StripsVendorNoise(string cpu, string gpu, string expected) => Assert.Equal(expected, HardwareNames.Short(cpu, gpu));
// Short_NoGpu: ("Intel Core i7-13700K", null) → "Core i7-13700K" ; Short_Nothing: (null, null) → "Monitoring"
```

- [ ] **Step 2: Tests du choix des capteurs**
  - `Map_IntelNvidia`, à partir d'une liste réaliste :
    - CPU « Intel Core Ultra 7 265K » : « CPU Total » 34, « CPU Package » 52, « P-Core #1 » 5400, « Bus Speed » 100, « CPU Package » (puissance) 125 ;
    - GPU NVIDIA : « GPU Core » charge 61, température 64, fréquence 2800 ; « GPU Package » 180 ; « GPU Memory Used » 5120 ; « GPU Memory Total » 12288 ; Fan 1450.
    - Valeurs exactes attendues pour chaque clé.
  - `Map_AmdCpuAndGpu` : « Core (Tctl/Tdie) » 71 donne `cpu.temp` = 71 ; le GPU AMD est choisi devant le GPU Intel intégré.
  - `Map_NoGpu` : aucune clé `gpu.*` et `GpuName == null`.
  - `Map_DiscardsInvalidValues` : `NaN`, −5 et 200 °C sont absents.
  - `Map_NoPackage_UsesMaxCoreTemperature`.
- [ ] **Step 3: Test de fumée du lecteur** — `LhmSensorReader_Read_ReturnsRamAndCpuLoad` : sur la machine de test, sans droits, `ram.total` > 0, `cpu.load` présent et `CpuName` non vide.
- [ ] **Step 4: Lancer, constater les échecs**
- [ ] **Step 5: Implémenter**
  - **LHM** : `Computer { IsCpuEnabled, IsGpuEnabled, IsStorageEnabled }`, ouvert une seule fois.
  - **`Read`** : `Accept(UpdateVisitor)`, conversion de tous les capteurs (sous-matériels compris) en `RawSensor`, puis `Map`.
  - `Elevated = Environment.IsPrivilegedProcess`.
- [ ] **Step 6: Tout passe** — Run : `dotnet test tests/Trofeo.Sensors.Tests`
- [ ] **Step 7: Commit** — `feat(sensors): relevé neutre des capteurs à partir de LibreHardwareMonitor`

---

### Task 6: Tube des capteurs et choix de la source

**Files:**
- Create: `src/Trofeo.Sensors/Pipe/SensorPipe.cs` (nom et format), `SensorPipeServer.cs`, `SensorPipeClient.cs`, `HelperIdentity.cs`, `src/Trofeo.Sensors/SensorHub.cs`
- Test: `tests/Trofeo.Sensors.Tests/SensorPipeTests.cs`, `SensorHubTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public static class SensorPipe { public const string Name = "trofeo-HB-dashboard.sensors"; public const int MaxLineBytes = 65536; }
public sealed class SensorPipeServer(string pipeName, ISensorReader reader, TimeProvider time) { public Task RunAsync(CancellationToken ct); }
public interface ISnapshotStream { event EventHandler<SensorSnapshot>? Received; }
public sealed class SensorPipeClient(string pipeName, Func<int, bool> isTrustedServer, TimeProvider time) : ISnapshotStream, IDisposable
{ public bool IsConnected { get; } public Task RunAsync(CancellationToken ct); }
public static class HelperIdentity { public static string InstallDir { get; } public static bool IsTrusted(int processId); }
public interface ISensorFeed { SensorSnapshot? Latest { get; } event EventHandler? Updated; IDisposable Acquire(); }
public sealed class SensorHub(Func<ISensorReader> localFactory, ISnapshotStream helper, TimeProvider time) : ISensorFeed, IDisposable;
```

**Règles :**
- **Format** : une ligne JSON UTF-8 par relevé : `{"cpuName":…,"gpuName":…,"elevated":true,"values":{"cpu.load":34.0,…}}`.
- **Serveur** :
  - tube `PipeDirection.Out`, une seule instance, `FirstPipeInstance` ;
  - créé par `NamedPipeServerStreamAcl.Create` avec ces droits : `SYSTEM` et `Administrators` FullControl, `Authenticated Users` Read ;
  - un relevé par seconde ;
  - un client parti (`IOException`) : il recrée le tube et attend le suivant ;
  - il ne lit jamais rien.
- **Client** :
  - connexion avec un délai de 1000 ms, nouvel essai toutes les 5 s ;
  - à la connexion, `GetNamedPipeServerProcessId` puis `isTrustedServer(pid)`. Si la réponse est fausse : déconnexion, un avertissement journalisé une seule fois, aucune donnée reçue ;
  - une ligne plus longue que `MaxLineBytes` fait couper la connexion ;
  - une ligne JSON invalide est ignorée.
- **`HelperIdentity.IsTrusted`** :
  - `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` puis `QueryFullProcessImageNameW` ;
  - le chemin complet normalisé doit commencer par `InstallDir`, sans tenir compte de la casse ;
  - `InstallDir` = `%ProgramFiles%\trofeo-HB-dashboard\sensors\`.
- **Hub** :
  - il ne tourne qu'entre le premier `Acquire` et la libération du dernier ;
  - si l'assistant a envoyé un relevé il y a moins de 3 s, on publie ses relevés à leur réception, sans lecture locale ;
  - sinon, une lecture locale par seconde : lecteur créé à la première utilisation, jamais deux lectures en même temps ;
  - `Updated` est levé à chaque relevé publié ;
  - une exception du lecteur local est journalisée par `Trace`, à raison d'une fois par minute au plus.

- [ ] **Step 1: Tests du tube**, avec un nom de tube unique par test
  - `Client_TrustedServer_ReceivesSnapshot` : les valeurs reçues sont celles du faux lecteur.
  - `Client_UntrustedServer_IgnoresSnapshots` : `isTrustedServer` rend faux, aucun `Received` en 3 s, `IsConnected` faux.
  - `Client_MalformedLine_IsSkipped` : un faux serveur écrit `"{oops\n"` puis une ligne valide ; un seul `Received`.
  - `Client_OversizedLine_Disconnects`.
  - `Server_ClientLeaves_AcceptsNextClient`.
- [ ] **Step 2: Tests du hub**, avec FakeTimeProvider, un faux `ISnapshotStream` et un faux lecteur
  - `Hub_NotAcquired_ReadsNothing`.
  - `Hub_NoHelper_ReadsLocalEverySecond`.
  - `Hub_HelperActive_SkipsLocal`.
  - `Hub_HelperSilent_FallsBackToLocal` : plus rien de l'assistant pendant 3 s, puis lecture locale.
  - `Hub_LastRelease_StopsReading`.
- [ ] **Step 3: Lancer, constater les échecs**
- [ ] **Step 4: Implémenter**
- [ ] **Step 5: Tout passe** — Run : `dotnet test tests/Trofeo.Sensors.Tests`
- [ ] **Step 6: Commit** — `feat(sensors): tube en lecture seule vers l'assistant élevé et repli sur la lecture locale`

---

### Task 7: Assistant capteurs — exécutable, installation, tâche planifiée

**Files:**
- Create: `src/Trofeo.Sensors/Setup/HelperInstaller.cs`, `ICommandRunner.cs`, `PawnIoDetector.cs`, `TaskDefinition.cs`
- Create: `src/Trofeo.SensorHelper/Program.cs`, `app.manifest`
- Test: `tests/Trofeo.Sensors.Tests/HelperInstallerTests.cs`, `TaskDefinitionTests.cs`

**Interfaces:**
- Produces :
  - `public interface ICommandRunner { int Run(string fileName, string arguments); }`, plus une version qui lance réellement un processus et attend sa fin.
  - `public interface IPawnIoDetector { bool IsInstalled(); }`. L'implémentation regarde si la clé `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` existe.
  - `public static class TaskDefinition { public const string TaskName = @"\trofeo-HB-dashboard\Capteurs"; public static string Xml(string helperExePath); }`
  - `public sealed class HelperInstaller(ICommandRunner runner, IPawnIoDetector pawnIo, string sourceDir, string installDir)` :
    - `InstallResult Install(string? pawnIoSetupPath)` ;
    - `void Uninstall()` ;
    - `public enum InstallResult { Installed, InstalledRebootRequired, PawnIoMissing, PawnIoFailed }`.
  - **Exécutable `Trofeo.SensorHelper.exe`** :
    - **sans argument** : il exige d'être élevé (sinon code de sortie 2), prend le mutex `Global\trofeo-HB-dashboard.sensors` (sinon code 0), puis lance `LhmSensorReader` et `SensorPipeServer` jusqu'à l'arrêt ;
    - **`install [--pawnio <PawnIO_setup.exe>]`** et **`uninstall`** : ils exigent d'être élevés et affichent leur résultat en français sur la console ;
    - **journal** : `FileTraceListener` sur `<dossier de l'exe>\logs\sensors.log` ;
    - **manifeste** : `asInvoker`. L'élévation vient de la tâche planifiée, ou du terminal administrateur pour `install`.

**Installation** (dans cet ordre) :
1. **PawnIO.**
   - S'il est absent et qu'un chemin est donné : `"<setup>" -install -silent`. Le code 0 donne `Installed`, le code 3010 donne `InstalledRebootRequired`, tout autre code donne `PawnIoFailed`.
   - S'il est absent sans chemin : `PawnIoMissing`, et rien d'autre n'est fait.
2. **Arrêt de l'ancienne tâche** : `schtasks /End /TN "<TaskName>"`, code de sortie ignoré.
3. **Copie** de `sourceDir` vers `installDir`, récursive, en écrasant les fichiers existants. Les fichiers créés héritent des droits de Program Files.
4. **Création de la tâche** : écrire le XML dans un fichier temporaire (UTF-16, comme l'attend schtasks), puis `schtasks /Create /TN "<TaskName>" /XML "<tmp>" /F`.
5. **Démarrage** : `schtasks /Run /TN "<TaskName>"`.

**Désinstallation** : `/End`, `/Delete /TN … /F`, puis suppression de `installDir`. PawnIO reste installé, car d'autres logiciels peuvent s'en servir.

**XML de la tâche** :
- `Principal` : `UserId` `S-1-5-18`, `RunLevel` `HighestAvailable` ;
- `LogonTrigger` sans utilisateur, c'est-à-dire pour toute session ;
- `MultipleInstancesPolicy` `IgnoreNew` ;
- `DisallowStartIfOnBatteries` et `StopIfGoingOnBatteries` à `false` ;
- `ExecutionTimeLimit` `PT0S` ;
- `RestartOnFailure` `PT1M` × 3 ;
- `Priority` 7 ;
- `Hidden` `true` ;
- `Exec/Command` = `helperExePath` entre guillemets si besoin.

- [ ] **Step 1: Tests**
  - `Xml_RunsAsSystem_AtLogon_WithoutTimeLimit` : lire le XML avec `XDocument` et vérifier chaque valeur ci-dessus.
  - `Install_PawnIoMissing_NoPath_ChangesNothing` : aucune commande lancée, aucun fichier copié.
  - `Install_RunsPawnIoSetupThenTask` : l'ordre des commandes du faux lanceur est conforme.
  - `Install_PawnIoReboot_ReturnsRebootRequired`.
  - `Install_CopiesFilesRecursively` : des dossiers temporaires pour la source et la destination.
  - `Uninstall_DeletesTaskAndFolder`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Sensors.Tests`
- [ ] **Step 5: Commit** — `feat(sensors): assistant capteurs élevé, installation et tâche planifiée`

---

### Task 8: Module Monitoring PC

**Files:**
- Create: `src/Trofeo.Modules.Monitoring/MonitoringFactory.cs`, `MonitoringModule.cs`, `MonitoringSettings.cs`, `MonitoringRows.cs`
- Test: `tests/Trofeo.Modules.Monitoring.Tests/MonitoringRowsTests.cs`, `MonitoringModuleTests.cs`, `MonitoringSettingsTests.cs`

**Interfaces:**
- Consumes : `ISensorFeed`, `SensorSnapshot`, `SensorKeys`, `HardwareNames` (tâches 5 et 6), `ModuleDrawing` (tâche 2).
- Produces :
  - `MonitoringFactory(ISensorFeed feed)`. Son descripteur :
    - `TypeId` « monitoring », `DisplayName` « Monitoring PC » ;
    - taille minimum 3×2 ;
    - tailles toutes faites : Compact 4×2, Idéal 6×2, Large 6×4.
  - `MonitoringSettings(string? Title, IReadOnlyList<string> Rows)`, lu de `{"title": "...", "rows": ["cpu","gpu","ram"]}` :
    - lignes par défaut `["cpu","gpu","ram"]` ;
    - identifiants inconnus ignorés ;
    - titre vide ou blanc : `null`.
  - `public sealed record MonitoringRow(string Label, float? Bar, string Value, string? Secondary);`
  - `public static class MonitoringRows` :
    - `MonitoringRow Build(string rowId, SensorSnapshot? s)` ;
    - `int Capacity(SKRect contentArea, float textScale)`, qui servira aussi au logiciel de réglage pour signaler un dépassement.

**Lignes disponibles** (une valeur absente s'écrit « — » et sa barre est `null`) :

| id | Label | Barre | Valeur | Secondaire |
|---|---|---|---|---|
| `cpu` | CPU | charge / 100 | « 34 % » | temp « 52 °C » |
| `gpu` | GPU | charge / 100 | « 61 % » | temp « 64 °C » |
| `ram` | RAM | utilisé / total | « 18,2 » | « / 32 Go » |
| `vram` | VRAM | utilisé / total | « 5,1 » | « / 12 Go » |
| `clock` | Fréq. | — | CPU « 5,4 GHz » | GPU « 2,8 GHz » |
| `power` | Conso | — | CPU « 125 W » | GPU « 180 W » |
| `fan` | Ventil. | — | « 1450 tr/min » | — |
| `disk` | Disque | utilisé / 100 | « 63 % » | temp « 38 °C » |

**Formats des nombres** :
- culture `fr-FR` ;
- RAM et VRAM avec une décimale, le total arrondi à l'entier ;
- GHz avec une décimale ;
- le reste en entiers arrondis (`MidpointRounding.AwayFromZero`).

**Dessin** :
- Marges 22 px horizontales et 18 px verticales.
- Ligne de titre en `TextMuted`, 18 px, à gauche. Le titre affiché est celui des réglages, sinon `HardwareNames.Short(CpuName, GpuName)`, ellipsé.
- Lignes de valeurs alignées en bas, de 40 px de haut, espacées de 10 px.
- Colonnes :
  - label 22 px en `TextMuted`, sur 64 px ;
  - barre de 8 px de haut, qui prend la place restante ;
  - valeur 30 px en `Text`, alignée à droite, sur 86 px ;
  - secondaire 30 px en `Text`, aligné à droite, sur 96 px, ou 18 px en `TextMuted` quand c'est une unité comme « / 32 Go ».
- Toutes ces tailles sont multipliées par `TextScale`. On dessine les `Capacity` premières lignes.
- **Températures CPU indisponibles** : si la ligne `cpu` est affichée et que `cpu.temp` est absent, la ligne de titre devient en `Warning` : « Temp. CPU : installez le pilote (Paramètres) ».

**État** :
- `Online` au départ ;
- `Offline` si aucun relevé n'est arrivé depuis 5 s ;
- retour à `Online` au relevé suivant ;
- `StatusChanged` à chaque changement ;
- `Invalidated` à chaque `feed.Updated` ;
- `Acquire()` au `StartAsync`, libéré au `Dispose`.

- [ ] **Step 1: Tests**
  - `Build_Cpu_FormatsLoadAndTemp` : « 34 % » et « 52 °C ».
  - `Build_Ram_FormatsUsedAndTotal` : 18,24 / 31,8 donne « 18,2 » et « / 32 Go », barre ≈ 0,574.
  - `Build_MissingValues_ShowDash`.
  - `Capacity_Compact4x2_IsThree` : rectangle de `GridGeometry.ModuleRect(8,0,4,2)`, échelle 1.
  - `Capacity_Large6x4_IsAtLeastSix`.
  - `Settings_Defaults` et `Settings_UnknownRowsIgnored`.
  - `Title_Default_IsShortHardwareName` : le titre rendu est « Core Ultra 7 265K · RTX 5070 ». Exposer `internal string CurrentTitle()`.
  - `Title_NoCpuTemp_ShowsDriverHint`.
  - `Status_NoSnapshotFor5s_GoesOffline_ThenBackOnline`, avec un faux `ISensorFeed` et FakeTimeProvider.
  - `Render_AllRowsFitInsideBounds` : aucun pixel hors du rectangle du module, pour 4×2, 6×2 et 6×4, aux trois tailles de texte.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Monitoring.Tests`
- [ ] **Step 5: Commit** — `feat(monitoring): module Monitoring PC`

---

### Task 9: Module En écoute

**Files:**
- Create: `src/Trofeo.Modules.NowPlaying/NowPlayingFactory.cs`, `NowPlayingModule.cs`, `MediaSnapshot.cs`, `IMediaSource.cs`, `SmtcMediaSource.cs`, `AppNames.cs`, `MediaTimeline.cs`
- Test: `tests/Trofeo.Modules.NowPlaying.Tests/AppNamesTests.cs`, `MediaTimelineTests.cs`, `NowPlayingModuleTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record MediaSnapshot(string Title, string Artist, string AppName, TimeSpan Position, TimeSpan Duration,
    DateTimeOffset PositionAt, bool IsPlaying, byte[]? Artwork);
public interface IMediaSource : IDisposable { MediaSnapshot? Current { get; } event EventHandler? Changed; Task StartAsync(CancellationToken ct); }
public static class AppNames { public static string Friendly(string? appUserModelId); }
public static class MediaTimeline { public static TimeSpan Current(MediaSnapshot s, DateTimeOffset now); public static string Format(TimeSpan t); }
public sealed class NowPlayingFactory(Func<IMediaSource> createSource) : IModuleFactory;
```

- **Descripteur** : `TypeId` « nowplaying », `DisplayName` « En écoute », taille minimum 3×1. Tailles toutes faites : Compact 3×1, Idéal 4×2, Large 6×2.
- **`AppNames.Friendly`**, dans cet ordre, sans tenir compte de la casse :
  1. Contient « spotify » → « Spotify ».
  2. « msedge » → « Edge ».
  3. « chrome » → « Chrome ».
  4. « firefox » → « Firefox ».
  5. « zunemusic » → « Lecteur multimédia ».
  6. « vlc » → « VLC ».
  7. « deezer » → « Deezer ».
  8. « applemusic » → « Apple Music ».
  9. Sinon : garder ce qui précède « ! », puis ce qui précède « _ », puis le dernier segment après « . » une fois « .exe » retiré. Majuscule à l'initiale.
  10. Vide ou `null` → « Média ».
- **`MediaTimeline.Current`** : `Position + (IsPlaying ? now − PositionAt : 0)`, borné à [0, Duration] quand `Duration > 0`.
- **`MediaTimeline.Format`** : « 1:24 » ; « 1:02:03 » au-delà d'une heure.
- **`SmtcMediaSource`** :
  - `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` ;
  - abonnement à `CurrentSessionChanged`, puis, sur la session courante, à `MediaPropertiesChanged`, `PlaybackInfoChanged` et `TimelinePropertiesChanged` ;
  - pochette lue par `Thumbnail.OpenReadAsync()`, limitée à 4 Mo (au-delà : `null`), relue seulement quand le titre ou l'artiste change ;
  - `Current` est remplacé d'un bloc (`Volatile`).
- **Module** :
  - état :
    - `Online` ;
    - `Offline` si `StartAsync` de la source lève, avec un journal d'erreur ;
  - `Invalidated` à chaque `Changed`, et chaque seconde tant que `IsPlaying` ;
  - pochette décodée **sur le thread de rendu**, mise en cache par référence du tableau d'octets, l'ancienne libérée à ce moment-là ;
  - **dessin en 4×2**, d'après la maquette `td-music` :
    - pochette carrée à gauche (hauteur du contenu, arrondi 10), ou un carré `TextMuted` à 25 % d'opacité si elle manque ;
    - à droite :
      - le nom de l'appli (18, `TextMuted`), précédé d'un petit symbole lecture (triangle) ou pause (deux barres) de 14 px ;
      - le titre (34, ellipsé) ;
      - l'artiste (22, `TextMuted`, ellipsé) ;
      - une barre de 8 px de haut ;
      - le temps écoulé et la durée totale (16, `TextMuted`) ;
  - **en 3×1** : pas de pochette, le titre (26) et l'artiste (18) seulement ;
  - **rien ne joue** : « Rien en lecture » centré, 24, `TextMuted`. Le module reste `Online`.

- [ ] **Step 1: Tests**
  - `Friendly_KnownApps` : `"Spotify.exe"`, `"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"`, `"MSEdge"`, `"Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"`.
  - `Friendly_Unknown_UsesExecutableName` : `"foobar2000.exe"` → « Foobar2000 ».
  - `Friendly_Empty_IsMedia`.
  - `Current_Playing_Extrapolates` et `Current_Paused_Frozen`.
  - `Current_ClampedToDuration`.
  - `Format_UnderAndOverOneHour`.
  - `Module_Playing_InvalidatesEverySecond_PausedDoesNot`, avec un faux `IMediaSource` et FakeTimeProvider.
  - `Module_SourceFails_GoesOffline`.
  - `Render_Nothing_DrawsRienEnLecture`, vérifié avec `ModuleDrawing.MeasureText` et des pixels présents au centre.
  - `Render_LongJapaneseTitle_StaysInside` : aucun pixel hors du module.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.NowPlaying.Tests`
- [ ] **Step 5: Commit** — `feat(nowplaying): module En écoute via les contrôles média de Windows`

---

### Task 10: Météo dans le module Heure

**Files:**
- Create: `src/Trofeo.Modules.Clock/Weather/OpenMeteoClient.cs`, `IWeatherProvider.cs`, `WeatherCodes.cs`, `WeatherIcons.cs`
- Modify: `src/Trofeo.Modules.Clock/ClockSettings.cs`, `ClockModule.cs`, `ClockFactory.cs`
- Test: `tests/Trofeo.Modules.Clock.Tests/OpenMeteoClientTests.cs`, `WeatherCodesTests.cs`, `ClockSettingsTests.cs`, `ClockModuleTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public enum TemperatureUnit { Celsius, Fahrenheit }
public sealed record WeatherLocation(string Name, double Latitude, double Longitude);
public sealed record ClockSettings(ClockFormat Format = ClockFormat.H24, WeatherLocation? Location = null, TemperatureUnit Unit = TemperatureUnit.Celsius);
public sealed record WeatherNow(double Temperature, int WeatherCode, bool IsDay);
public sealed record CitySuggestion(string Name, string? Region, string? Country, double Latitude, double Longitude);
public interface IWeatherProvider { Task<WeatherNow> GetCurrentAsync(WeatherLocation where, TemperatureUnit unit, CancellationToken ct); }
public sealed class OpenMeteoClient(HttpClient http) : IWeatherProvider
{ public Task<IReadOnlyList<CitySuggestion>> SearchCityAsync(string name, CancellationToken ct); }
public sealed class WeatherException(string message, Exception? inner = null) : Exception(message, inner);
public enum WeatherIcon { Sun, Moon, PartlyCloudyDay, PartlyCloudyNight, Cloud, Fog, Drizzle, Rain, Snow, Storm, Unknown }
public static class WeatherCodes { public static (WeatherIcon Icon, string Label) Describe(int code, bool isDay); }
public static class WeatherIcons { public static void Draw(SKCanvas canvas, WeatherIcon icon, SKRect rect); }
public sealed class ClockFactory(IWeatherProvider weather) : IModuleFactory;
```

**Réglages** :
- Format JSON : `{"format":"24h","location":{"name":"Lyon","latitude":45.76,"longitude":4.84},"unit":"c"|"f"}`.
- Une latitude hors de [−90, 90], une longitude hors de [−180, 180], `NaN` ou un nom vide : pas de localisation.
- `DisplayName` devient « Heure, date et météo ». `TypeId` « clock » ne change pas.

**Adresses**, avec les nombres formatés en culture invariante (`"R"`) :
- Prévision :
  ```
  https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&temperature_unit={celsius|fahrenheit}&timezone=auto
  ```
  On lit `current.temperature_2m`, `current.weather_code` et `current.is_day` (1 ou 0).
- Recherche de ville :
  ```
  https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=5&language=fr&format=json
  ```
  On lit `results[].name`, `admin1`, `country`, `latitude` et `longitude`. Sans `results` : liste vide.
- **Erreurs** : HTTP non réussi, JSON invalide ou délai de 10 s dépassé lèvent une `WeatherException`.

**Libellés** (`Describe`) :

| Code | Libellé (icône) |
|---|---|
| 0 | « Ensoleillé » (Sun) le jour, « Dégagé » (Moon) la nuit |
| 1 | « Peu nuageux » |
| 2 | « Éclaircies » |
| 3 | « Couvert » (Cloud) |
| 45, 48 | « Brouillard » (Fog) |
| 51, 53, 55 | « Bruine » |
| 56, 57 | « Bruine verglaçante » (Drizzle) |
| 61, 63 | « Pluie » |
| 65 | « Forte pluie » |
| 66, 67 | « Pluie verglaçante » (Rain) |
| 71, 73 | « Neige » |
| 75 | « Forte neige » |
| 77 | « Grains de neige » (Snow) |
| 80, 81 | « Averses » |
| 82 | « Fortes averses » (Rain) |
| 85, 86 | « Averses de neige » (Snow) |
| 95 | « Orage » |
| 96, 99 | « Orage et grêle » (Storm) |
| Autre | « — » (Unknown) |

Pour les codes 1 et 2, l'icône est PartlyCloudyDay le jour et PartlyCloudyNight la nuit.

**Icônes** : pixel art sur une grille 12 × 9, en rectangles, dans l'esprit de la mascotte. L'icône soleil + nuage reprend exactement le SVG `td-wx` de la maquette.

| Élément | Couleur |
|---|---|
| Soleil, éclair | `#FFD27A` |
| Nuage | `#E9EEF5` |
| Pluie | `#7FB8FF` |
| Neige | `#FFFFFF` |
| Lune | `#D9DCE8` |
| Brouillard | `#AEB4BE` |

**Comportement du module** :
- Avec une localisation : une lecture au démarrage, puis toutes les 15 min. Après un échec, un nouvel essai 2 min plus tard.
- Une valeur réussie est gardée 1 h au plus.
- **État** :
  - sans localisation : `Online` ;
  - dernier essai réussi : `Online` ;
  - dernier essai en échec : `Offline`, l'heure restant affichée.
- `StatusChanged` et `Invalidated` à chaque changement.

**Dessin** :
- Température : « 12° », arrondie (`AwayFromZero`), sans lettre d'unité.
- **2 rangées ou plus** : bloc en haut à droite, aligné à droite (icône de 54 px de large, température 46, libellé 16 en `TextMuted`). L'heure se réduit pour ne pas chevaucher le bloc : largeur disponible = largeur − bloc − 24.
- **1 rangée** : « 12° » (30) et une icône de 30 px au bord droit, à condition que le module fasse au moins 4 colonnes.
- Sans valeur valide, rien n'est affiché à la place de la météo.

- [ ] **Step 1: Tests** — un `HttpMessageHandler` factice enregistre l'adresse demandée et renvoie un JSON écrit à la main.
  - `GetCurrent_BuildsExactUrl_AndParses` : adresse exacte pour (45.76, 4.84, Celsius), et `WeatherNow(12.4, 2, true)`.
  - `GetCurrent_Fahrenheit_UsesUnitParameter`.
  - `GetCurrent_HttpError_ThrowsWeatherException` et `GetCurrent_BadJson_ThrowsWeatherException`.
  - `SearchCity_NoResults_ReturnsEmpty` et `SearchCity_EscapesName` (« Saint-Étienne »).
  - `Describe_Table`, un `[Theory]` sur chaque ligne du tableau, plus 0 la nuit.
  - `Settings_ParsesLocationAndUnit` et `Settings_InvalidCoordinates_NoLocation`.
  - `Weather_FetchesAtStartThenEvery15Min`.
  - `Weather_FailureRetriesAfter2Min_AndGoesOffline`.
  - `Weather_StaleAfterOneHour_Hidden` : `internal WeatherNow? CurrentWeather()`.
  - `Render_12hWithWeather_NoOverlap` : les rectangles mesurés de l'heure et du bloc météo ne se coupent pas. Exposer ce calcul de disposition en `internal`.
  - `WeatherIcons_DrawInsideRect` : pour chaque icône, des pixels peints, et aucun hors du rectangle.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter.** Mettre à jour les usages existants de `new ClockFactory()` (`Program`, `DisplayLoopTests`, `ClockModuleTests`) avec un faux `IWeatherProvider`, ou `new OpenMeteoClient(http)` dans `Program`.
- [ ] **Step 4: Tout passe** — Run : `dotnet test`
- [ ] **Step 5: Commit** — `feat(clock): météo Open-Meteo dans le module Heure`

---

### Task 11: Module Réseau — cœur et carte réseau du PC

**Files:**
- Create: `src/Trofeo.Modules.Network/NetworkFactory.cs`, `NetworkModule.cs`, `NetworkSettings.cs`, `IThroughputSource.cs`, `ThroughputHistory.cs`, `RateText.cs`, `NicSource.cs`
- Test: `tests/Trofeo.Modules.Network.Tests/RateTextTests.cs`, `ThroughputHistoryTests.cs`, `NicSourceTests.cs`, `NetworkModuleTests.cs`

**Interfaces:**
- Produces (exact) :

```csharp
public sealed record ThroughputSample(double DownBytesPerSecond, double UpBytesPerSecond);
public sealed record SourceReading(ThroughputSample? Sample, ModuleStatus Status);
public interface IThroughputSource : IDisposable { string Title { get; } TimeSpan Interval { get; } Task<SourceReading> ReadAsync(CancellationToken ct); }
public enum NetworkSourceKind { Pc, Freebox, UniFi }
public sealed record NetworkSettings(NetworkSourceKind Source = NetworkSourceKind.Pc, string? UniFiHost = null);
public sealed class ThroughputHistory(TimeSpan window) { public void Add(DateTimeOffset at, ThroughputSample s); public IReadOnlyList<(DateTimeOffset At, ThroughputSample Sample)> Snapshot(); }
public static class RateText { public static (string Value, string Unit) Format(double bytesPerSecond); }
public sealed record InterfaceCounters(string Id, long BytesReceived, long BytesSent);
public sealed class NicSource(Func<IReadOnlyList<InterfaceCounters>> readCounters, TimeProvider time) : IThroughputSource
{ public static IReadOnlyList<InterfaceCounters> ReadSystemCounters(); }
public sealed class NetworkFactory(Func<NetworkSettings, ModuleContext, IThroughputSource> createSource) : IModuleFactory;
```

**Descripteur et réglages** :
- `TypeId` « network », `DisplayName` « Réseau ».
- Taille minimum 3×1. Tailles toutes faites : Compact 3×1, Idéal 4×2, Large 6×2.
- Réglages JSON : `{"source":"pc"|"freebox"|"unifi","unifi":{"host":"192.168.1.1"}}`. Une source absente ou inconnue donne `Pc`.
- La fabrique de production qui associe `Freebox` et `UniFi` à leur source arrive aux tâches 12 et 13. Ici, seule `Pc` est branchée.

**Format des débits** (`RateText`) :
- Le calcul se fait en bits par seconde (octets × 8), en culture `fr-FR`. Une valeur négative ou `NaN` compte pour 0.
- Moins de 1e6 : kb/s, entier arrondi.
- Moins de 1e9 : Mb/s, avec une décimale sous 10, entier au-dessus.
- Au-delà : Gb/s, une décimale.

**Carte réseau du PC** (`NicSource`) :
- Titre « Ce PC », intervalle 1 s.
- `ReadSystemCounters` garde les interfaces `Up` qui ne sont ni `Loopback` ni `Tunnel` et qui ont au moins une passerelle. On lit `GetIPStatistics()`.
- **Débit** : somme des écarts sur les interfaces présentes aux deux lectures, divisée par le temps écoulé.
  - Un écart négatif (compteur remis à zéro) compte pour 0.
  - Une interface apparue entre deux lectures est ignorée pour cette lecture.
  - La première lecture rend `Sample = null`, à l'état `Online`.

**Module** :
- **Lecture** : `source.ReadAsync` à chaque `Interval`, sans lectures qui se chevauchent.
- **Historique** : fenêtre de 3 min, un ajout par échantillon.
- **Exception de la source** : `Offline`, erreur journalisée par `ctx.Log.Error`, historique conservé.
- **État** : `Status` reprend celui de la lecture. `StatusChanged` est levé quand il change, et `Invalidated` après chaque lecture.

**Dessin** (maquette `td-net`) :
- **Ligne de titre** : `source.Title` à gauche (18, `TextMuted`). À droite, « en direct » si l'état est `Online`, sinon « hors ligne » en `Warning`.
- **Valeurs** : descendant puis montant, la valeur en 44 (`Text`) et l'unité en 16 (`TextMuted`), précédée d'une flèche ↓ ou ↑. La flèche est **dessinée en triangle plein**, pas comme un caractère.
- **Courbe** en bas, sur 62 px de haut :
  - débit descendant en `Accent`, de 2 px ;
  - débit montant en `TextMuted`, de 1,5 px ;
  - échelle verticale = maximum de la fenêtre, avec un plancher de 1 Mb/s.
- **En 1 rangée** : les valeurs seulement.
- **`ActionMessage` présent** : `ModuleDrawing.DrawActionMessage` sur toute la zone sous le titre.

- [ ] **Step 1: Tests**

```csharp
[Theory]
[InlineData(0, "0", "kb/s")]
[InlineData(106_250, "850", "kb/s")]
[InlineData(525_000, "4,2", "Mb/s")]
[InlineData(51_500_000, "412", "Mb/s")]
[InlineData(155_000_000, "1,2", "Gb/s")]
[InlineData(double.NaN, "0", "kb/s")]
public void Format_Thresholds(double bytesPerSecond, string value, string unit) => Assert.Equal((value, unit), RateText.Format(bytesPerSecond));
```

  - `History_DropsOlderThanWindow`.
  - `History_Snapshot_IsCopy`.
  - `NicSource_FirstRead_NoSample`.
  - `NicSource_ComputesRate` : +1 000 000 octets reçus en 1 s donnent 1 000 000 B/s.
  - `NicSource_CounterReset_NoSpike` : un compteur qui redescend donne 0, jamais une valeur négative ni un pic.
  - `NicSource_NewInterface_IgnoredFirstTime`.
  - `Settings_DefaultIsPc`.
  - `Module_SourceThrows_GoesOfflineAndKeepsHistory`.
  - `Module_ActionMessage_IsDrawn` : pixels `Warning` présents.
  - `Render_FitsInside_AllPresets`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Network.Tests`
- [ ] **Step 5: Commit** — `feat(network): module Réseau et source carte réseau du PC`

---

### Task 12: Source Freebox

**Files:**
- Create: `src/Trofeo.Modules.Network/Freebox/FreeboxSource.cs`, `FreeboxApi.cs` (enveloppe JSON et appels), `FreeboxAuth.cs` (HMAC)
- Modify: `src/Trofeo.Modules.Network/NetworkFactory.cs` (fabrique de production)
- Test: `tests/Trofeo.Modules.Network.Tests/FreeboxSourceTests.cs`

**Interfaces:**
- Produces :
  - `FreeboxSource(HttpClient http, ISecretStore secrets, IModuleLog log, Uri? baseUri = null)`. L'adresse par défaut est `http://mafreebox.freebox.fr`.
  - `Title` « Freebox ». `Interval` vaut 1 s une fois connecté, 2 s pendant l'autorisation.
  - `public static string FreeboxAuth.Password(string appToken, string challenge)` : HMAC-SHA1 (clé = jeton, données = challenge, tous deux en UTF-8), en hexadécimal minuscule.
  - `NetworkFactory.CreateProduction(HttpClient http)` renvoie la fonction de création qui associe `Pc`, `Freebox` et `UniFi` à leur source. Pour `UniFi`, une source qui reste à l'état `NotConfigured` jusqu'à la tâche 13.

**Déroulé** (chaque `ReadAsync` fait avancer d'une étape) :
1. **Découverte.**
   - `GET /api_version` → `api_base_url` (par exemple « /api/ ») et `api_version` (par exemple « 8.2 »).
   - Préfixe = `{api_base_url}v{majeur}/`.
   - Un majeur < 4 donne `Offline` avec le message « Freebox trop ancienne ».
2. **Sans jeton** (`secrets.Get("freebox.app_token")` vaut `null`) :
   - `POST {préfixe}login/authorize/` avec `{"app_id":"trofeo-HB-dashboard","app_name":"Trofeo HB Dashboard","app_version":"<version de l'assembly>","device_name":Environment.MachineName}` ;
   - on garde en mémoire `app_token` et `track_id`.
3. **Suivi de l'autorisation** : `GET {préfixe}login/authorize/{track_id}` → `result.status`.

   | `status` | Suite |
   |---|---|
   | `pending` | `NotConfigured` avec « Appuie sur ► sur ta Freebox pour autoriser le dashboard » |
   | `granted` | jeton enregistré, on passe à 4 |
   | `timeout` ou `unknown` | retour à 2 (nouvelle demande), même message |
   | `denied` | `NotConfigured` avec « Autorisation refusée sur la Freebox », nouvelle demande dans 5 min |

4. **Session.**
   - `GET {préfixe}login/` → `result.challenge`.
   - `POST {préfixe}login/session/` avec `{"app_id":…,"password":FreeboxAuth.Password(token, challenge)}` → `result.session_token`.
   - Un `error_code` `invalid_token` ou `pending_token` efface le jeton et ramène à 2.
5. **Débit.**
   - `GET {préfixe}connection/` avec l'en-tête `X-Fbx-App-Auth: <session_token>`.
   - `rate_down` et `rate_up` sont des octets/s.
   - `state` différent de `up` donne `Offline` et un échantillon (0, 0).
   - Une réponse `auth_required` refait 4 une fois.

- **Enveloppe** : `{success, result, error_code, msg}`. `success = false` est traité selon `error_code` ; une erreur réseau lève une exception, que le module convertit en `Offline`.
- **Le jeton** n'apparaît jamais dans le journal.

- [ ] **Step 1: Tests** — un `HttpMessageHandler` factice scripté, avec `FakeSecretStore`.
  - `Password_MatchesRfc2202Vector` : `FreeboxAuth.Password("Jefe", "what do ya want for nothing?")` == `"effcdf6ae5eb2fa2d27416d5f184df9c259a7c79"`.
  - `FirstRun_RequestsAuthorization_AndShowsButtonMessage` : corps de la requête exact et message exact.
  - `Granted_StoresToken_ThenReadsRates` : le jeton est dans le coffre, l'échantillon vaut (`rate_down`, `rate_up`), et l'en-tête de session est présent.
  - `Timeout_RequestsAgain`.
  - `Denied_ShowsRefusal_AndWaits5Min`.
  - `Login_InvalidToken_RestartsAuthorization` : le jeton est effacé et le message « Appuie sur ► … » revient.
  - `AuthRequired_RelogsOnce`.
  - `ConnectionDown_IsOffline`.
  - `OldApi_IsOffline`.
  - `Log_NeverContainsToken` : aucune entrée de `FakeModuleLog` ne contient le jeton.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Network.Tests`
- [ ] **Step 5: Commit** — `feat(network): source Freebox avec autorisation sur la box`

---

### Task 13: Source UniFi

**Files:**
- Create: `src/Trofeo.Modules.Network/UniFi/UniFiSource.cs`, `CertificatePinning.cs`
- Modify: `src/Trofeo.Modules.Network/NetworkFactory.cs`
- Test: `tests/Trofeo.Modules.Network.Tests/UniFiSourceTests.cs`, `CertificatePinningTests.cs`

**Interfaces:**
- Produces :
  - `UniFiSource(string? host, ISecretStore secrets, IModuleLog log, HttpMessageHandler? handler = null)`. Sans `handler`, la source crée un `SocketsHttpHandler` qui valide le certificat par `CertificatePinning`.
  - `Title` « UniFi », `Interval` 5 s.
  - `public static (bool Accept, string? NewPin) CertificatePinning.Check(string? pinnedSha256, string presentedSha256, SslPolicyErrors errors)`.

**Règles :**
- **Configuration manquante** :
  - sans hôte : `NotConfigured` avec « Indique l'adresse de la console UniFi dans le logiciel de réglage » ;
  - sans clé (`secrets.Get("unifi.apiKey")`) : `NotConfigured` avec « Ajoute la clé API UniFi dans le logiciel de réglage ».
- **Adresse de base** : `https://{host}/proxy/network/integration/v1/`. En-tête `X-API-KEY`.
- **Recherche de la passerelle** :
  1. `GET sites` : prendre le premier élément de `data[]`, et retenir son `id`.
  2. `GET sites/{siteId}/devices?limit=200` : prendre le premier appareil dont `features` contient `"gateway"`. Aucun : `Offline` avec « Aucune passerelle UniFi trouvée ».
  3. Garder les deux identifiants en cache. Un 404 relance la recherche.
- **Débit** : `GET sites/{siteId}/devices/{deviceId}/statistics/latest` → `uplink.rxRateBps` (descendant) et `uplink.txRateBps` (montant), en **bits/s**, divisés par 8.
- **Erreurs** : 401 ou 403 donnent `NotConfigured` avec « Clé API UniFi refusée ».
- **Certificat** (`Check`) :

  | Situation | Résultat |
  |---|---|
  | `errors == None` | accepté |
  | Pas d'empreinte mémorisée | accepté, l'empreinte présentée devient la nouvelle |
  | Empreinte identique | acceptée |
  | Empreinte différente | refusée |

  - L'empreinte (SHA-256 hexadécimal du certificat) est mémorisée sous `unifi.certSha256.{host}`.
  - Un refus donne `Offline` avec « Certificat UniFi changé : vérifiez la console ».

- [ ] **Step 1: Tests**
  - `Check_Table` : les quatre lignes du tableau.
  - `NoHost_NotConfigured` et `NoKey_NotConfigured`, avec les messages exacts.
  - `FindsGatewayAndReadsUplink` : JSON factice `sites`, `devices` (un switch et une passerelle) et `statistics/latest` avec `rxRateBps` 400000000. On attend `DownBytesPerSecond` = 50 000 000 et l'en-tête `X-API-KEY` présent.
  - `NoGateway_Offline`.
  - `Unauthorized_NotConfigured`.
  - `NotFound_Rediscovers`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**, et brancher `UniFi` dans `NetworkFactory.CreateProduction`.
- [ ] **Step 4: Tout passe** — Run : `dotnet test tests/Trofeo.Modules.Network.Tests`
- [ ] **Step 5: Commit** — `feat(network): source UniFi par l'API d'intégration locale`

---

### Task 14: Câblage, disposition par défaut, outils, README et vérification sur l'écran

**Files:**
- Modify: `src/Trofeo.Display/Program.cs`, `src/Trofeo.Display/Trofeo.Display.csproj`, `src/Trofeo.Core/Layout/DashboardLayout.cs`, `tests/Trofeo.Core.Tests/LayoutStoreTests.cs`, `tools/Trofeo.HardwareCheck/Program.cs`, `tools/Trofeo.HardwareCheck/Trofeo.HardwareCheck.csproj`, `README.md`

**Interfaces:**
- Consumes : toutes les fabriques et tous les services des tâches 1 à 13.
- Produces :
  - **`Program`** crée :
    - un `HttpClient` partagé (délai 15 s, `User-Agent` `trofeo-HB-dashboard/0.2`) ;
    - le coffre ;
    - `SensorHub(() => new LhmSensorReader(), new SensorPipeClient(SensorPipe.Name, HelperIdentity.IsTrusted, TimeProvider.System), TimeProvider.System)`, avec le client lancé en tâche de fond ;
    - le registre avec `ClockFactory(new OpenMeteoClient(http))`, `MonitoringFactory(hub)`, `NowPlayingFactory(() => new SmtcMediaSource())` et `NetworkFactory(NetworkFactory.CreateProduction(http))`.

    À l'arrêt, tout est libéré dans l'ordre inverse.
  - **`DashboardLayout.Default`** : rotation 180, avec :
    - Monitoring en (8, 0, 4×2), nouvel identifiant fixe ;
    - En écoute en (8, 2, 4×2) ;
    - Heure en (12, 0, 4×2), identifiant inchangé ;
    - Réseau en (12, 2, 4×2), `{"source":"pc"}`.

    Les colonnes 0 à 7 restent libres pour Claude Code (plan 3).
  - **`HardwareCheck`** gagne deux options :
    - `--set-secret <scope> <key>` lit la valeur sur l'entrée standard et l'écrit dans le coffre ;
    - `--city <nom>` affiche les cinq propositions d'Open-Meteo avec leurs coordonnées.
  - **README** :
    - mettre à jour « Ce qui marche aujourd'hui » ;
    - ajouter aux crédits LibreHardwareMonitor (MPL-2.0), PawnIO et « Données météo : Open-Meteo.com (CC BY 4.0) » ;
    - expliquer comment activer les températures CPU (installer PawnIO, puis `Trofeo.SensorHelper.exe install` dans un terminal administrateur).

- [ ] **Step 1: Test** — `Default_HasFourDataModules_NoOverlap` remplace l'assertion « un seul module » de `LayoutStoreTests`. La validation par `LayoutValidator` passe avec le registre réel.
- [ ] **Step 2: Implémenter**, puis lancer `dotnet build -c Release` et `dotnet test`. Expected : `0 Warning(s)`, tout passe.
- [ ] **Step 3: Commit** — `feat(display): modules de données branchés et disposition par défaut à quatre modules`
- [ ] **Step 4: Vérification sur l'écran avec Alex.** Le contrôleur la mène, pas un sous-agent.
  1. Arrêter le `Trofeo.Display.exe` en cours.
  2. Sauvegarder le `layout.json` d'Alex.
  3. Demander sa ville et la chercher avec `--city`.
  4. Écrire une disposition avec Freebox et la ville. Alex confirme d'abord le contenu du fichier.
  5. Lancer le service.

  Alex vérifie :
  - les quatre modules sont lisibles d'un coup d'œil, les températures GPU s'affichent et la ligne « Temp. CPU : installez le pilote » apparaît ;
  - la Freebox affiche le message, Alex appuie sur le bouton et les débits apparaissent ;
  - le titre Spotify, la pochette et la progression s'affichent ;
  - la météo s'affiche.
- [ ] **Step 5: Températures CPU, avec l'accord d'Alex.**
  1. Alex installe PawnIO (`winget install --id namazso.PawnIO`).
  2. Il lance `Trofeo.SensorHelper.exe install` dans un terminal administrateur.
  3. Vérifier que la tâche `\trofeo-HB-dashboard\Capteurs` existe et tourne, et que le module affiche la température CPU (la ligne d'aide disparaît).
  4. Vérifier qu'après une fermeture puis une réouverture de session, l'assistant redémarre.
- [ ] **Step 6: Sobriété.** Laisser tourner 5 min sans animation, puis mesurer le CPU et la mémoire du service, et de l'assistant le cas échéant. Objectifs : moins de 100 Mo et environ 1 % de CPU en moyenne. Reporter les mesures dans le bilan du plan 2.
