# Plan 4 — Apparence

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner au tableau de bord son apparence réglable :
- les trois thèmes de la maquette, **Classique**, **Liquid Glass** et **Pixel** ;
- la **transparence** des modules ;
- la **police** : polices embarquées (licence OFL) ou police du système ;
- la **taille du texte** par module (déjà là depuis le plan 1, vérifiée dans chaque thème) ;
- le **fond d'écran** : couleur, image, GIF ou vidéo, à 12 images/s au plus, sans rien à installer.

**Architecture:**
- **ModuleKit.** `Theme` reçoit ce dont les modules ont besoin pour suivre le thème : couleurs de surface, de piste et de mise en avant, forme (arrondie ou pixel), police du fil. `ModuleDrawing.DrawBar` dessine les barres en segments dans le thème Pixel.
- **Core.**
  - `ThemeDefinition` ajoute ce qui ne regarde que la composition : écarts du damier, dessin du panneau (mat, verre, pixel), fond par défaut.
  - `AppearanceSettings` est rangé dans `layout.json` (`Appearance`, absent = Classique). Il est lu avec indulgence : une valeur fautive retombe sur le défaut sans faire rejeter la disposition.
  - `IBackground` : couleur, dégradé « Aurore », image, image animée (GIF, WebP), et vidéo fournie par l'hôte.
  - `FrameComposer` peint le fond, puis chaque panneau dans le style du thème. Le verre floute une copie réduite du fond, recalculée seulement quand le fond change.
- **Windows (PC d'Alex).** La vidéo est lue par Media Foundation, puis le tout est branché dans `Trofeo.Display`.

**Tech Stack:**
- .NET 10, C#, SkiaSharp 3.119 (`SKCodec` pour les GIF et WebP animés, `SKImageFilter` pour le flou) ;
- System.Text.Json ;
- xUnit et FakeTimeProvider ;
- Media Foundation (Windows, tâche 9 seulement).

**Spec:** `docs/superpowers/specs/2026-10-24-trofeo-dashboard-design.md`, sections 4.3, 6.1 (taille du texte), 7 et 10. Maquette de référence : `docs/superpowers/maquettes/themes-v3.html` (classes `.t-classic`, `.t-glass`, `.t-pixel`). Voir aussi les bilans des plans 1 à 3.

## Global Constraints

- **Les valeurs de la maquette font foi** pour les couleurs, les flous et les arrondis (tableau de la tâche 3). La maquette est à l'échelle 1920 px : `calc(N * var(--u))` vaut N pixels de l'écran.
- **Identifiants des thèmes** : `classique`, `liquid-glass`, `pixel`, ceux de la maquette (`data-choice`). Ils ne changent plus une fois publiés.
- **Polices embarquées** :
  - Barlow Semi Condensed, déjà là ;
  - Manrope, Pixelify Sans et VT323, à ajouter.

  Toutes sous licence OFL, avec le texte de licence de chacune dans `src/Trofeo.Core/Assets/Fonts/`. Aucune police du système n'est jamais copiée ni embarquée.
- **≤ 12 images/s** (spec 7.2) : un fond animé plus rapide garde sa vitesse en sautant des images. Pas de fondu ni de glissement entre deux images (spec 7.3).
- **Sobriété** (spec 4.3) :
  - un fond fixe ne force aucun redessin ;
  - un fond animé ne garde **qu'une image décodée** en mémoire (plus le tampon de décodage), jamais toutes les images du GIF ;
  - le flou du verre est recalculé seulement quand l'image du fond change.
- **Rien à installer** (spec 7.2) : image et GIF par SkiaSharp, vidéo par Media Foundation (fourni par Windows).
- **Un fond ou une police qui manque ne fait jamais planter le service.** On retombe sur le défaut du thème, avec un avertissement journalisé (spec 10).
- **Textes du journal en français.** Les modules ne dessinent aucun nouveau texte.
- **Compatibilité** :
  - un `layout.json` sans `Appearance` donne exactement l'écran d'aujourd'hui (Classique, fond `#0E1116`) ;
  - l'ancienne surcharge `FrameComposer.Compose(layout, modules, theme, now)` reste, pour que `Trofeo.Display` compile tant que la tâche 10 n'est pas faite.
- `TreatWarningsAsErrors` reste actif. Licence GPL-3.0.

## Où construire

- **Tâches 0 à 8 : dans le cloud sous Linux.**
  - Installer le SDK : `apt-get install -y dotnet-sdk-10.0`.
  - Depuis la tâche 0, la plupart des projets de tests Windows se construisent et tournent aussi sous Linux.
- **Tâches 9 et 10 : sur le PC d'Alex** (Media Foundation, polices du système réelles, branchement, vrai écran).
- **Échecs connus sous Linux**, à ne pas confondre avec une régression (relevés avant ce plan sur `b365cb9`) :

  | Projet | Échecs | Cause |
  |---|---|---|
  | Core.Tests | 9 | les 8 tests de `DpapiSecretVaultTests` (DPAPI) et `DrawText_MissingGlyphs_UsesFallback` (pas de polices du système) |
  | Network.Tests | 3 | `Render_OneRow_NotOnline…`, `Render_OfflineTitle_IsWarning…` et `Render_Title_NotConfigured…` (même cause probable : polices) |
  | Sensors.Tests | 26 | lancent `cmd.exe` |
  | Display.Tests | tout | demande le runtime Windows Desktop |

  Tout le reste passe : Agents 543, Clock 127, Monitoring 72, NowPlaying 46 et Device 27.

## Review Focus

1. **Fichier de fond absent, illisible ou énorme** (fichier déplacé, photo de 12 000 × 8 000, GIF de 500 images).
   - On affiche le fond du thème et on journalise un avertissement.
   - Rien ne plante, et la mémoire reste sous l'objectif.

   Tests `Factory_MissingFile_FallsBackToTheme`, `Image_Huge_IsDecodedScaledDown` et `Animated_KeepsOneDecodedFrame` (tâche 5).
2. **GIF plus rapide que 12 images/s, ou durées nulles** (fréquent dans les GIF du web). La vitesse réelle est conservée en sautant des images, et une durée de 0 vaut 100 ms, comme dans les navigateurs. Tests `Animated_FastGif_DropsFramesKeepsSpeed` et `Animated_ZeroDelay_Uses100Ms` (tâche 5).
3. **Texte illisible sur un fond clair.**
   - Cas : verre ou transparence au plus bas, posés sur une image blanche.
   - Le panneau s'assombrit juste assez pour que sa luminance moyenne reste sous 0,35.
   - Le texte blanc reste lisible, conformément à l'anti-objectif « texte illisible ».

   Test `Panel_BrightBackdrop_IsDarkenedForLegibility` (tâche 6).
4. **Disposition partagée par un ami**, avec une police qu'Alex n'a pas ou un thème mal écrit.
   - La police du thème est utilisée à la place.
   - Les modules et leur place sont gardés.
   - Aucune copie `.bad` n'est faite.

   Tests `Resolve_UnknownFamily_FallsBackToTheme` (tâche 2) et `Load_UnknownTheme_KeepsPlacements` (tâche 4).
5. **Module qui déborde avec une police plus large** : Manrope est plus large que Barlow condensée. Le texte est raccourci par « … », jamais coupé au milieu d'un caractère ni dessiné hors du panneau. Contrôle `ThemePreview --check` (tâche 8).

## Décisions prises en écrivant ce plan

- **La plupart des tests Windows tournent sous Linux.** Le plan 3 croyait que non. En réalité, avec `EnableWindowsTargeting` et la bibliothèque native Linux de SkiaSharp, ils se construisent et passent (voir « Où construire »). Le plan 4 en profite pour modifier Monitoring et NowPlaying dans le cloud. Coût : aucun sous Windows, les deux réglages sont conditionnés à Linux.
- **Aperçu en `net10.0-windows`.** `tools/Trofeo.ThemePreview` vise `net10.0-windows10.0.19041.0`, ce qui lui permet de référencer les cinq vrais modules. Il tourne sous Linux comme les tests. L'aperçu montre donc les vrais modules, pas des imitations.
- **Fond par défaut de chaque thème.**
  - Classique garde `#0E1116`, pour que l'écran d'Alex ne change pas tant qu'il ne choisit rien.
  - Liquid Glass et Pixel prennent le fond « Aurore » de la maquette, celui qu'Alex a validé : le verre n'a pas de sens sur un fond uni.

  Coût : un réglage à changer s'il préfère autre chose.
- **Une seule graisse par police**, comme aujourd'hui avec Barlow Regular : Manrope Medium (500), Pixelify Sans Regular (400) et VT323 Regular. Manrope et Pixelify sont des polices variables : on en tire une instance fixe avec `fonttools`. SkiaSharp 3.119 n'expose pas les axes de variation. Coût : pas de gras pour les chiffres (la maquette met 600 aux `strong`).
- **Police choisie par l'utilisateur** : elle remplace aussi la police du fil (VT323 en Pixel), et l'agrandissement du fil revient à 1. Coût : un Pixel sans VT323 si on choisit une autre police.
- **Transparence** : un nombre entre 0 et 1 (`PanelOpacity`) qui remplace l'opacité du fond du panneau. Absent : celle du thème.
  - Classique 0,86, verre 0,22 (la teinte), Pixel 1.
  - À 0, il ne reste que le contour et les reflets : « le verre presque invisible » de la spec.
  - La garde de lisibilité (Review Focus 3) s'applique quand même.
- **Écarts du damier propres au thème** : Pixel a une marge de 18 px et des écarts de 20 px (maquette `.t-pixel .td-grid`), pour laisser la place aux bordures et aux ombres décalées. Les modules Pixel sont donc un peu plus petits. Coût : quelques pixels de contenu.
- **Ce qui est laissé de côté dans la maquette** : l'ombre du texte du verre (`text-shadow`, 18 % d'opacité, invisible à 12 images/s en JPEG) et le fond des onglets Pixel `#1f1b38`. Les onglets utilisent `Surface`.
- **Fond vidéo** : interface `IBackground` commune. Dans le cloud, une vidéo demandée retombe sur le fond du thème : la fabrique de vidéo vient de l'hôte (tâche 9).
- **Réglages lus au démarrage du service.** Changer de thème demande de le relancer, jusqu'au logiciel de réglage (plan 5). En attendant, `HardwareCheck --appearance` les écrit (tâche 10).

---

### Task 0: Construire et tester sous Linux, maquette dans le dépôt

**Files:**
- Modify: `Directory.Build.props` : `<EnableWindowsTargeting Condition="$([MSBuild]::IsOSPlatform('Linux'))">true</EnableWindowsTargeting>`
- Modify: `tests/Directory.Build.props` : `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.0, sous la condition `$([MSBuild]::IsOSPlatform('Linux'))`, pour tous les projets de tests sauf `IsTestSupportLibrary`
- Modify: `tests/Trofeo.Modules.Agents.Tests/Trofeo.Modules.Agents.Tests.csproj` : retirer sa propre référence au même paquet, en doublon (NU1504 devient une erreur)
- Add: `docs/superpowers/maquettes/themes-v3.html` (fourni par Alex)
- Modify: `CLAUDE.md`, section « Pièges connus » : les tests Windows tournent sous Linux, avec les échecs connus

- [ ] **Step 1: Sur la branche de la session (`main-zcgy1g`, partie de `main` en `b365cb9`), faire les modifications**
- [ ] **Step 2: Vérifier** — Run : `dotnet test` sur chacun des projets de tests. Expected : exactement le tableau « Où construire ».
- [ ] **Step 3: Vérifier que Windows n'est pas touché** — Relire le diff : chaque ajout est conditionné à Linux.
- [ ] **Step 4: Commit** — `chore: tests Windows lancés sous Linux, maquette des thèmes dans le dépôt`

---

### Task 1: Ce que les modules savent du thème

**Files:**
- Modify: `src/Trofeo.ModuleKit/Theme.cs`
- Modify: `src/Trofeo.ModuleKit/ModuleDrawing.cs`
- Test: `tests/Trofeo.Core.Tests/ThemeTests.cs`, `ModuleDrawingTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.ModuleKit`. Le constructeur positionnel ne change pas, et les nouvelles propriétés ont une valeur par défaut tirée des anciennes. Le code et les tests existants compilent donc sans retouche.

```csharp
/// Rounded : coins arrondis, barres pleines, points ronds. Pixel : angles vifs, barres en segments, points carrés,
/// onglet actif marqué d'un trait de 4 px à gauche.
public enum ThemeShape { Rounded, Pixel }

public sealed record Theme(string Id, SKColor Background, SKColor Panel, float PanelOpacity, float CornerRadius,
    SKColor Text, SKColor TextMuted, SKColor Accent, SKColor Warning, SKTypeface Typeface)
{
    public ThemeShape Shape { get; init; }                  // défaut Rounded
    public SKColor Highlight { get; init; }                 // titres mis en avant ; défaut : Text
    public SKColor Surface { get; init; }                   // onglet actif, zones en relief ; défaut : Text à 7 % (alpha 18)
    public SKColor Track { get; init; }                     // piste des barres ; défaut : Text à 8 % (alpha 20)
    public SKTypeface FeedTypeface { get; init; }           // police du fil d'activité ; défaut : Typeface
    public float FeedScale { get; init; }                   // agrandissement du fil ; défaut 1
}

public static class ModuleDrawing
{
    // existant : DrawBar(canvas, rect, fraction, fill, track) inchangé
    /// Barre dans le style du thème : Track pour la piste ; Rounded = DrawBar existant ;
    /// Pixel = angles vifs, segments pleins.
    public static void DrawBar(SKCanvas canvas, SKRect rect, float fraction, SKColor fill, Theme theme);
}
```

Les défauts calculés (`Highlight` = `Text`…) passent par des champs nullables privés, puisque `init` ne peut pas lire `Text` dans un initialiseur.

**Règles de la barre Pixel** :
- segment plein de `0,75 × hauteur`, espace de `0,25 × hauteur` (la maquette fait 9 + 3 pour 12 px de haut) ;
- autant de segments entiers que la largeur en contient, le reste à droite étant laissé vide ;
- segments remplis : `round(fraction × nombre)`, avec `AwayFromZero`, fraction bornée à [0, 1] et NaN = 0 ;
- les autres segments prennent `Track` ;
- dessin sans anticrénelage, coordonnées arrondies au pixel.

- [ ] **Step 1: Tests**
  - `Theme_Defaults_FollowTextColors` : un `Theme` construit comme `Themes.Classique` a `Highlight == Text`, `Surface == Text.WithAlpha(18)`, `Track == Text.WithAlpha(20)`, `FeedTypeface` = `Typeface`, `FeedScale == 1`, `Shape == Rounded`.
  - `DrawBar_Pixel_DrawsWholeSegments` : rect 0,0 → 120 × 12, fraction 0,5, thème `Shape = Pixel`, `Track` rouge, remplissage vert.
    - 10 segments ; les 5 premiers sont verts (pixel x = 4, y = 6) ;
    - le 6ᵉ est rouge (x = 64) ;
    - l'espace entre le 1ᵉʳ et le 2ᵉ est transparent (x = 10).
  - `DrawBar_Pixel_Rounding` : fraction 0,04 sur 10 segments → 0 segment vert ; 0,05 → 1 segment.
  - `DrawBar_Rounded_UsesThemeTrack` : thème Rounded, fraction 0 → le centre du rect a la couleur `Track` (alpha compris).
- [ ] **Step 2: Lancer, constater les échecs** — `dotnet test tests/Trofeo.Core.Tests --filter "ThemeTests|ModuleDrawingTests"`
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — Core.Tests : seuls les 9 échecs connus restent.
- [ ] **Step 5: Commit** — `feat(modulekit): thème étendu (forme, surface, piste, police du fil) et barres en segments`

---

### Task 2: Polices embarquées et polices du système

**Files:**
- Add: `src/Trofeo.Core/Assets/Fonts/Manrope-Medium.ttf`, `PixelifySans-Regular.ttf`, `VT323-Regular.ttf`
- Add: `src/Trofeo.Core/Assets/Fonts/OFL-Manrope.txt`, `OFL-PixelifySans.txt`, `OFL-VT323.txt`
- Rename: l'actuel `OFL.txt` (Barlow) devient `OFL-BarlowSemiCondensed.txt`
- Modify: `src/Trofeo.Core/Trofeo.Core.csproj` : `OFL-*.txt` copiés à la sortie
- Create: `src/Trofeo.Core/Rendering/Fonts.cs`
- Modify: `README.md`, crédits : les quatre polices et leur licence
- Test: `tests/Trofeo.Core.Tests/FontsTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.Core.Rendering` :

```csharp
public static class EmbeddedFonts
{
    public const string Barlow = "Barlow Semi Condensed", Manrope = "Manrope", Pixelify = "Pixelify Sans", Vt323 = "VT323";
    /// Les quatre familles, dans cet ordre.
    public static IReadOnlyList<string> Families { get; }
    /// Police embarquée par nom de famille (casse ignorée) ; null si ce n'en est pas une. Chargée une fois, gardée.
    public static SKTypeface? Find(string family);
}

/// Polices installées sur le PC. Remplaçable en test.
public interface ISystemFonts
{
    IReadOnlyList<string> Families { get; }     // triées, sans doublon (casse ignorée)
    SKTypeface? Match(string family);           // null si absente
}
/// SKFontManager.Default. Match rend null quand Skia renvoie une police d'une autre famille (repli silencieux de Skia).
public sealed class SkiaSystemFonts : ISystemFonts;

public static class FontResolver
{
    /// Ordre : null ou blanc → fallback ; police embarquée ; police du système ; sinon fallback, et warn
    /// reçoit « Police « {family} » introuvable : police du thème utilisée. »
    public static SKTypeface Resolve(string? family, SKTypeface fallback, ISystemFonts system, Action<string>? warn = null);
}
```

- **Fichiers de police.**
  - Source : le dépôt `google/fonts` (`ofl/manrope`, `ofl/pixelifysans`, `ofl/vt323`), par `raw.githubusercontent.com`, joignable depuis le cloud.
  - Instances fixes : `pip install fonttools`, puis `fonttools varLib.instancer "Manrope[wght].ttf" wght=500 -o Manrope-Medium.ttf` (même chose pour Pixelify, `wght=400`).
  - Vérifier ensuite que `SKTypeface.FromStream` donne la bonne famille et la bonne graisse (`FontWeight`).
- `FontLoader.Load` reste tel quel. `EmbeddedFonts` s'appuie dessus.

- [ ] **Step 1: Tests**
  - `Embedded_AllFamiliesLoad` : chaque nom de `Families` donne une police non nulle, et son `FamilyName` est ce nom. Manrope a `FontWeight == 500`.
  - `Embedded_Find_IgnoresCase` : `Find("pixelify sans")` n'est pas null ; `Find("Arial")` est null.
  - `Resolve_Embedded_BeatsSystem` : un faux `ISystemFonts` qui connaît « Manrope » n'est jamais appelé pour Manrope.
  - `Resolve_SystemFont_IsUsed` : le faux rend une police pour « Segoe UI » → c'est elle.
  - `Resolve_UnknownFamily_FallsBackToTheme` : le faux rend null → `fallback`, et `warn` a reçu le message exact.
  - `Resolve_Blank_IsFallbackWithoutWarning`.
  - `Licenses_ShipWithEachFont` : pour chaque `.ttf` du dossier, un `OFL-<Famille>.txt` existe à côté, dans la sortie de construction.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Télécharger les polices, créer les instances fixes, implémenter**
- [ ] **Step 4: Lancer** — Core.Tests : seuls les échecs connus restent.
- [ ] **Step 5: Commit** — `feat(core): polices Manrope, Pixelify Sans et VT323 embarquées, polices du système`

---

### Task 3: Les trois thèmes

**Files:**
- Modify: `src/Trofeo.Core/Rendering/Themes.cs`
- Create: `src/Trofeo.Core/Rendering/ThemeDefinition.cs`
- Modify: `src/Trofeo.Core/Layout/GridGeometry.cs` (surcharge avec écarts)
- Test: `tests/Trofeo.Core.Tests/ThemesTests.cs`, `GridGeometryTests.cs`

**Interfaces:**
- Produces (exact), dans `Trofeo.Core` :

```csharp
namespace Trofeo.Core.Layout;
public readonly record struct GridSpacing(int Inset, int Gap) { public static GridSpacing Default { get; } = new(14, 12); }
public static class GridGeometry
{
    // existant : ModuleRect(column, row, columns, rows) == ModuleRect(..., GridSpacing.Default)
    public static SKRectI ModuleRect(int column, int row, int columns, int rows, GridSpacing spacing);
}

namespace Trofeo.Core.Rendering;
public enum PanelKind { Matte, Glass, Pixel }
/// Dessin du panneau. Champs inutiles pour un style : laissés à leur défaut.
public sealed record PanelLook(PanelKind Kind)
{
    public SKColor Outline { get; init; }          // contour intérieur de 1 px (Matte, Glass)
    public float BlurSigma { get; init; }          // Glass
    public float Saturation { get; init; } = 1f;   // Glass
    public SKColor SheenTop { get; init; }         // Glass : dégradé à 155°, de SheenTop (0 %) à SheenMid (55 %) puis SheenBottom (100 %)
    public SKColor SheenMid { get; init; }
    public SKColor SheenBottom { get; init; }
    public SKColor TopLight { get; init; }         // Glass : trait de 1,5 px le long du bord haut
    public SKColor DropShadow { get; init; }       // Glass : décalage (0, 10), flou 30 ; Pixel : voir PixelShadow
    public SKColor Border { get; init; }           // Pixel : bordure extérieure
    public int BorderWidth { get; init; }          // Pixel : 4
    public SKColor PixelShadow { get; init; }      // Pixel : ombre nette décalée de (8, 8), étendue de 4
}
public enum BackgroundKind { Theme, Color, Image, Animated, Video }
/// Color : « #RRGGBB » ; Path : fichier image, GIF/WebP animé ou vidéo.
public sealed record BackgroundSettings(BackgroundKind Kind, string? Color = null, string? Path = null);
/// Le fond par défaut d'un thème : une couleur, ou « Aurore » (Aurora = true).
public sealed record ThemeBackground(SKColor Color, bool Aurora);
public sealed record ThemeDefinition(Theme Theme, GridSpacing Grid, PanelLook Panel, ThemeBackground DefaultBackground);

public static class Themes
{
    public static Theme Classique { get; }                 // inchangé : Themes.Classique == Definition("classique").Theme
    public static ThemeDefinition ClassiqueDefinition { get; }
    public static ThemeDefinition LiquidGlass { get; }
    public static ThemeDefinition Pixel { get; }
    public static IReadOnlyList<ThemeDefinition> All { get; }   // dans cet ordre
    /// Par identifiant (casse ignorée) ; null si inconnu.
    public static ThemeDefinition? Find(string? id);
}
```

**Valeurs (maquette `themes-v3.html`)** — `a` est l'opacité :

| | Classique | Liquid Glass | Pixel |
|---|---|---|---|
| Id | `classique` | `liquid-glass` | `pixel` |
| Panel, PanelOpacity | `#111317`, 0,86 | `#101220`, 0,22 | `#17142A`, 1 |
| CornerRadius | 14 | 30 | 0 |
| Text | `#ECEEF0` | `#FFFFFF` | `#F4EEDE` |
| TextMuted | `#8C929C` | `#FFFFFF` a 0,72 | `#8F86B8` |
| Accent | `#7FD1C7` | `#FFFFFF` | `#7CE0A3` |
| Warning | `#F2B84B` | `#FFD56B` | `#FFD27A` |
| Highlight | Text | Text | `#FFD27A` |
| Surface | `#FFFFFF` a 0,07 | `#FFFFFF` a 0,22 | `#2E2850` |
| Track | `#FFFFFF` a 0,08 | `#FFFFFF` a 0,22 | `#2E2850` |
| Shape | Rounded | Rounded | Pixel |
| Typeface / FeedTypeface | Barlow / Barlow | Manrope / Manrope | Pixelify Sans / VT323 |
| FeedScale | 1 | 1 | 1,24 (31 / 25) |
| Grid | 14 / 12 | 14 / 12 | 18 / 20 |
| Panel | Matte, Outline `#FFFFFF` a 0,06 | Glass : Outline `#FFFFFF` a 0,22 ; BlurSigma 26 ; Saturation 1,7 ; Sheen `#FFFFFF` a 0,18 / 0,04 / 0,08 ; TopLight `#FFFFFF` a 0,55 ; DropShadow `#000000` a 0,18 | Pixel : Border `#2E2850` sur 4 ; PixelShadow `#0A0814` a 0,6 |
| Background (couleur de secours) | `#0E1116` | `#241A3D` | `#17142A` |
| DefaultBackground | couleur `#0E1116` | Aurore | Aurore |

Les alphas s'arrondissent par `Math.Round(a × 255, AwayFromZero)`.

- [ ] **Step 1: Tests**
  - `Classique_IsUnchanged` : `Themes.Classique` a toujours ses valeurs du plan 1, et `ClassiqueDefinition.Theme` est la même instance.
  - `Find_ById_IgnoresCase`, `Find_Unknown_IsNull`.
  - `Themes_MatchMockup` : une `[Theory]` sur les trois thèmes, avec les valeurs du tableau (Accent, Warning, CornerRadius, Grid, Shape, `FeedTypeface.FamilyName`).
  - `ModuleRect_Pixel_Spacing` : `ModuleRect(0, 0, 16, 4, new(18, 20))` vaut `(18, 18, 1902, 444)`, et deux modules voisins sont séparés de 20 px.
  - `ModuleRect_DefaultOverload_Unchanged` : les valeurs actuelles de `GridGeometryTests` passent toujours.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — Core.Tests : seuls les échecs connus restent.
- [ ] **Step 5: Commit** — `feat(core): thèmes Liquid Glass et Pixel d'après la maquette`

---

### Task 4: Réglages d'apparence dans la disposition

**Files:**
- Create: `src/Trofeo.Core/Rendering/AppearanceSettings.cs`, `AppearanceResolver.cs`
- Modify: `src/Trofeo.Core/Layout/DashboardLayout.cs`, `LayoutStore.cs`
- Test: `tests/Trofeo.Core.Tests/AppearanceTests.cs`, `LayoutStoreTests.cs`

**Interfaces:**
- Consumes : `Themes.Find`, `FontResolver.Resolve`, `ISystemFonts` (tâches 2 et 3).
- Produces (exact) :

```csharp
namespace Trofeo.Core.Rendering;
/// Theme : identifiant (défaut « classique ») ; PanelOpacity : null = celle du thème ; Font : null = celle du thème.
public sealed record AppearanceSettings(string Theme = "classique", float? PanelOpacity = null, string? Font = null,
    BackgroundSettings? Background = null)
{
    public static AppearanceSettings Default { get; } = new();
}
public static class AppearanceResolver
{
    /// Thème inconnu → Classique (warn). Opacité bornée à [0, 1], NaN → celle du thème (warn).
    /// Police → FontResolver (Typeface et FeedTypeface remplacées, FeedScale = 1).
    public static ThemeDefinition Resolve(AppearanceSettings settings, ISystemFonts fonts, Action<string>? warn = null);
}

namespace Trofeo.Core.Layout;
public sealed record DashboardLayout(int Rotation, IReadOnlyList<ModulePlacement> Placements,
    IReadOnlyList<ShortcutBinding>? Shortcuts = null, AppearanceSettings? Appearance = null);
```

- **Lecture indulgente** : `LayoutStore.IsValid` ne regarde pas `Appearance`. Un `Appearance` illisible fait seulement retomber `Appearance` à `null`, et la disposition est gardée, sans `.bad`.
  - Cas : un mauvais type JSON, ou un `Kind` inconnu.
  - Moyen : lire `Appearance` à part avec `JsonNode`, avant la désérialisation du reste.
- **Format écrit**, pour `HardwareCheck` (tâche 10) et le plan 5 :

```json
"Appearance": { "Theme": "liquid-glass", "PanelOpacity": 0.5, "Font": "Manrope",
                "Background": { "Kind": "Animated", "Color": null, "Path": "C:\\Users\\alex\\Images\\pluie.gif" } }
```

- [ ] **Step 1: Tests**
  - `Resolve_Default_IsClassique` : `Resolve(AppearanceSettings.Default, …)` donne `ClassiqueDefinition`, aucun avertissement.
  - `Resolve_UnknownTheme_IsClassiqueWithWarning` : « Thème « neon » inconnu : thème Classique utilisé. »
  - `Resolve_Opacity_ReplacesThemeOpacity` (0,4 → `PanelOpacity == 0.4f`) et `Resolve_Opacity_IsClamped` (3 → 1 ; −1 → 0).
  - `Resolve_Font_ReplacesBothTypefaces` : Pixel avec « Manrope » → `Typeface` et `FeedTypeface` Manrope, `FeedScale == 1`.
  - `Load_WithoutAppearance_IsNull` : un `layout.json` du plan 3 se relit à l'identique, avec `Appearance == null`.
  - `Load_UnknownTheme_KeepsPlacements` : `"Theme": "neon"` → les placements sont lus, `Appearance.Theme == "neon"` (c'est `Resolve` qui retombe sur Classique), pas de `.bad`.
  - `Load_BrokenAppearance_KeepsLayout` : `"Appearance": 42` et `"Background": {"Kind":"Plasma"}` → placements gardés, `Appearance == null`, pas de `.bad`.
  - `SaveLoad_RoundTrips` : une disposition avec un `Appearance` complet se relit à l'identique.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — Core.Tests : seuls les échecs connus restent.
- [ ] **Step 5: Commit** — `feat(core): réglages d'apparence dans layout.json, lus avec indulgence`

---

### Task 5: Fonds d'écran

**Files:**
- Create: `src/Trofeo.Core/Rendering/Backgrounds/IBackground.cs`, `ColorBackground.cs`, `AuroraBackground.cs`, `ImageBackground.cs`, `AnimatedBackground.cs`, `BackgroundFactory.cs`
- Test: `tests/Trofeo.Core.Tests/BackgroundTests.cs`. Les images de test sont fabriquées par les tests (`SKBitmap` encodé en PNG ; GIF animé écrit octet par octet, en 2 couleurs, par une petite aide de test `TinyGif.Write(path, frames, delaysCs)`).

**Interfaces:**
- Produces (exact), dans `Trofeo.Core.Rendering` :

```csharp
/// Fond de l'écran, toujours 1920 × 462. Utilisé par un seul thread (celui de l'affichage).
public interface IBackground : IDisposable
{
    bool IsAnimated { get; }
    /// Image courante, recadrée pour remplir l'écran. Même instance tant que Version ne change pas.
    SKImage Current { get; }
    /// Change à chaque nouvelle image (le flou du verre s'appuie dessus).
    int Version { get; }
    /// Avance au temps écoulé depuis le début de la lecture ; vrai si l'image a changé.
    bool Advance(TimeSpan elapsed);
    /// Délai avant la prochaine image depuis le dernier Advance ; null pour un fond fixe.
    TimeSpan? UntilNextFrame { get; }
}
public sealed class ColorBackground(SKColor color) : IBackground;
/// Le fond de la maquette : dégradé linéaire à 120° (#142A3A, #241A3D à 55 %, #0F2D33) et quatre halos elliptiques.
public sealed class AuroraBackground : IBackground;
public sealed class ImageBackground : IBackground { public static ImageBackground? TryLoad(string path, Action<string>? warn); }
public sealed class AnimatedBackground : IBackground { public static AnimatedBackground? TryLoad(string path, Action<string>? warn); }
public static class BackgroundFactory
{
    /// settings null ou Kind Theme → fond par défaut du thème. Video → video(path), ou le fond du thème si video est null
    /// ou rend null. Fichier absent ou illisible, couleur mal écrite → fond du thème, et warn.
    public static IBackground Create(BackgroundSettings? settings, ThemeDefinition theme,
        Func<string, IBackground?>? video = null, Action<string>? warn = null);
}
```

**Règles :**
- **Halos d'Aurore**, en CSS : ellipse de rayons (largeur × %, hauteur × %), centrée en %, couleur pleine au centre et transparente au rayon × arrêt. Ils sont dessinés du dernier au premier, sur le linéaire :

  | Couleur | Rayons | Centre | Arrêt |
  |---|---|---|---|
  | `#FF7A59` | 40 % × 90 % | 12 %, 85 % | 60 % |
  | `#1FB8A6` | 35 % × 80 % | 48 %, 10 % | 65 % |
  | `#6D5DFC` | 30 % × 70 % | 82 %, 90 % | 60 % |
  | `#FFCF5A` | 25 % × 60 % | 95 %, 15 % | 60 % |

  L'image est calculée une fois.
- **Recadrage** : échelle = `max(1920 / l, 462 / h)`, image centrée, échantillonnage `SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)`.
- **Grande image** : décodée avec `SKCodec.GetScaledDimensions` à la plus petite échelle qui couvre encore 1920 × 462. Une image de plus de 16 384 px de côté est refusée avec un avertissement.
- **Image animée** (`SKCodec`, GIF et WebP) :
  - une durée de moins de 20 ms (0 compris) vaut 100 ms ;
  - l'image affichée est celle du temps écoulé, modulo la durée totale ; les images intermédiaires sont décodées dans le tampon sans être recadrées ;
  - le décodage est séquentiel dans **un seul tampon** à la taille du GIF, avec `SKCodecOptions(i, priorFrame)` quand `FrameInfo[i].RequiredFrame != -1` ;
  - un retour en arrière (bouclage) repart de l'image 0 ;
  - un fichier d'une seule image est traité comme une image fixe (`IsAnimated` faux).
- **Messages d'avertissement** (exacts) :
  - « Fond d'écran introuvable : {path}. Fond du thème utilisé. »
  - « Fond d'écran illisible : {path}. Fond du thème utilisé. »
  - « Couleur de fond invalide : {color}. Fond du thème utilisé. »
  - « Vidéo non prise en charge ici : {path}. Fond du thème utilisé. »

- [ ] **Step 1: Tests**
  - `Color_FillsScreen` : `Current` fait 1920 × 462 et est uni ; `IsAnimated` faux ; `UntilNextFrame` null.
  - `Aurora_HasMockupColors` : le pixel (230, 393), au centre du halo orange, est plus proche de `#FF7A59` que de `#241A3D`.
  - `Image_CoverCrop` : une image de 100 × 100, moitié haute rouge et moitié basse bleue, est agrandie à 1920 de large. Le pixel (960, 5) est rouge et (960, 456) est bleu.
  - `Image_Huge_IsDecodedScaledDown` : un JPEG de 8000 × 2000 est décodé à au plus 2 × la taille couverte (largeur décodée < 4000, d'après une propriété `internal DecodedSize`).
  - `Factory_MissingFile_FallsBackToTheme` : `Kind Image` et chemin absent → `ColorBackground` du thème Classique, avec le message exact.
  - `Factory_BadColor_FallsBack`, `Factory_Video_WithoutHost_FallsBack`.
  - `Animated_FollowsElapsedTime` : GIF de 3 images de 200 ms (rouge, vert, bleu). `Advance(250 ms)` → vrai et vert ; `Advance(300 ms)` → faux ; `Advance(650 ms)` → rouge (bouclage).
  - `Animated_FastGif_DropsFramesKeepsSpeed` : 10 images de 20 ms. `Advance(100 ms)` montre l'image 5, et `UntilNextFrame` ≤ 20 ms. Le plafond à 12 images/s est celui de l'ordonnanceur, que ce test ne simule pas.
  - `Animated_ZeroDelay_Uses100Ms` : durées 0 → `Advance(150 ms)` montre l'image 1.
  - `Animated_KeepsOneDecodedFrame` : `internal int BitmapCount == 2` (le tampon et l'image recadrée), quel que soit le nombre d'images.
  - `Animated_Version_ChangesOnlyWithFrame`.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — Core.Tests : seuls les échecs connus restent.
- [ ] **Step 5: Commit** — `feat(core): fonds d'écran couleur, Aurore, image et image animée`

---

### Task 6: Composer l'écran selon le thème

**Files:**
- Create: `src/Trofeo.Core/Rendering/PanelPainter.cs`, `Backdrop.cs`
- Modify: `src/Trofeo.Core/Rendering/FrameComposer.cs`
- Test: `tests/Trofeo.Core.Tests/FrameComposerTests.cs`, `PanelPainterTests.cs`

**Interfaces:**
- Consumes : `ThemeDefinition`, `GridGeometry.ModuleRect(…, GridSpacing)`, `IBackground` (tâches 3 et 5).
- Produces (exact) :

```csharp
public sealed class FrameComposer
{
    // existant, gardé : Compose(layout, modules, theme, now) = Compose(layout, modules, définition de Classique
    // avec ce thème, ColorBackground(theme.Background) détenu par le compositeur, now)
    public SKBitmap Compose(DashboardLayout layout, IReadOnlyDictionary<Guid, IModule> modules,
        ThemeDefinition theme, IBackground background, DateTimeOffset now);
}
/// Copie floutée et saturée du fond, au quart de la taille, recalculée seulement quand Version ou le thème changent.
internal sealed class Backdrop : IDisposable
{
    public SKImage Get(IBackground background, PanelLook look);
    internal int Computations { get; }
}
internal static class PanelPainter
{
    /// Peint le panneau sous le module (avant son rendu). backdrop : null hors Glass.
    public static void Paint(SKCanvas canvas, SKRect rect, ThemeDefinition theme, SKImage? backdrop, SKImage background);
}
```

**Dessin des panneaux** (ordre de peinture) :
- **Matte** :
  1. `Panel` à `PanelOpacity`, arrondi `CornerRadius` ;
  2. contour intérieur de 1 px en `Outline`.
- **Glass** :
  1. ombre portée `DropShadow` (décalage 0, 10 ; flou `SKImageFilter.CreateBlur` de sigma 15, la moitié des 30 px CSS) ;
  2. dans l'arrondi, le fond flouté ;
  3. la teinte `Panel` à `PanelOpacity` ;
  4. le reflet en dégradé à 155° ;
  5. le contour intérieur de 1 px en `Outline` ;
  6. le trait `TopLight` de 1,5 px, qui suit l'arrondi sur le bord haut (arc de 180° à 360°, rogné à la moitié haute du rect).
- **Pixel** :
  1. l'ombre `PixelShadow` : rectangle décalé de (8, 8), agrandi de 4 de chaque côté, sans flou ;
  2. la bordure `Border` : rectangle agrandi de `BorderWidth` ;
  3. le panneau `Panel` à `PanelOpacity`, angles vifs, sans anticrénelage.
- **Flou du verre.**
  - Réduire le fond au quart (480 × 116), flouter avec un sigma de `BlurSigma / 4`, puis appliquer la saturation par `SKColorFilter.CreateColorMatrix`. Matrice de saturation standard, coefficients de luminance 0,2126 / 0,7152 / 0,0722.
  - Au dessin : agrandir au quart d'échelle inverse, avec un échantillonnage linéaire.
  - Le flou déborde sur les bords : l'image est prolongée (`SKShaderTileMode.Clamp`).
- **Garde de lisibilité**, pour tous les styles. Avant la teinte ou le fond du panneau :
  1. mesurer la luminance moyenne du fond sous le rect, sur la copie réduite (calculée une fois par `Version` et par rect, gardée dans `Backdrop`) ;
  2. si `L_fond × (1 − a) + L_panel × a > 0,35`, monter `a` à la plus petite valeur qui donne 0,35.
  - `a` est l'opacité du panneau et `L` la luminance relative.
  - Le reflet du verre compte pour + 0,05 dans le calcul.
- **Rognage des modules** : même arrondi que le panneau, et angles vifs en Pixel.
- **Panneau d'erreur** : peint par `PanelPainter` dans le style du thème. Sa couleur reste `Warning`.

- [ ] **Step 1: Tests**
  - `Compose_OldOverload_IsPixelIdentical` : la même image qu'avant ce plan, comparée au rendu de la surcharge neuve avec `ClassiqueDefinition` et `ColorBackground(#0E1116)`, et à une empreinte : somme des pixels d'une disposition fixe, calculée avant de toucher au code.
  - `Compose_UsesThemeGrid` : avec Pixel, un module 0,0 → 1×1 reçoit `Bounds.Left == 18`.
  - `Compose_Glass_ReusesBackdropForSameFrame` : deux compositions avec le même fond → `Computations == 1`. Après un `Advance` qui change l'image → 2.
  - `Glass_ShowsBlurredBackground` : fond moitié gauche rouge, moitié droite bleue, et un module Glass à cheval sur la limite. Au centre du module, le pixel est un mélange (R et B tous deux > 40), là où le fond net serait pur.
  - `Pixel_DrawsBorderAndOffsetShadow` : un pixel à 2 px hors du bord gauche est `#2E2850`. Un pixel à (droite + 10, bas + 10) est plus sombre que le fond.
  - `Panel_BrightBackdrop_IsDarkenedForLegibility` : fond blanc, Classique avec `PanelOpacity` 0. La luminance moyenne du panneau est ≤ 0,36.
  - `Panel_DarkBackdrop_KeepsUserOpacity` : fond `#0E1116` et opacité 0,3. Le panneau garde 0,3 : sa couleur au centre est celle attendue à ± 2.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — Core.Tests, puis Agents, Clock, Network, Monitoring et NowPlaying : seuls les échecs connus restent.
- [ ] **Step 5: Commit** — `feat(core): composition par thème (verre flouté, pixel, garde de lisibilité)`

---

### Task 7: Les modules suivent le thème

**Files:**
- Modify: `src/Trofeo.Modules.Agents/AgentPainter.cs`, `src/Trofeo.Modules.Monitoring/MonitoringModule.cs`, `src/Trofeo.Modules.NowPlaying/NowPlayingModule.cs`, `src/Trofeo.Modules.Clock/ClockModule.cs`
- Test: les projets de tests de ces quatre modules

**Interfaces:**
- Consumes : `Theme.Shape`, `Highlight`, `Surface`, `Track`, `FeedTypeface`, `FeedScale` et `ModuleDrawing.DrawBar(…, Theme)` (tâche 1).

**Ce qui change (maquette, et rien d'autre)** :
- **Toutes les barres** passent par `DrawBar(…, theme)` : quotas, tâches, CPU/GPU/RAM, progression du morceau.
  - La hauteur d'une barre Pixel vaut 1,5 × sa hauteur arrondie (8 → 12), centrée sur la même ligne.
  - Les pistes codées en dur (`Opacity(Text, 0.16f)`, `TrackAlpha`) deviennent `Theme.Track`.
- **Agents.**
  - L'onglet actif est rempli en `Surface` (au lieu de `Text` à 8 %).
  - En Pixel : angles vifs, trait `Highlight` de 4 px à gauche (au lieu du trait `Accent` de 3 px), texte en `Highlight`, points d'état carrés.
  - Le titre de la tâche est en `Highlight`.
  - Le fil (outil et cible) est en `FeedTypeface`, avec une taille × `FeedScale`.
  - La mascotte ne change pas.
- **Clock** : l'heure est en `Highlight`.
- **Le reste** (Network, et les couleurs `Accent` / `Warning` existantes) ne change pas.

- [ ] **Step 1: Tests**, un par règle, sur un thème de test au `Highlight` et au `Surface` reconnaissables :
  - `Agents_PixelTheme_ActiveTabHasHighlightMarker` ;
  - `Agents_FeedUsesFeedTypeface` : un enregistreur de dessin, ou `DrawnRuns`, vérifie la police ;
  - `Clock_TimeUsesHighlight` ;
  - `Monitoring_PixelTheme_BarsAreSegmented` ;
  - `NowPlaying_ProgressUsesThemeTrack`.

  Avec le thème Classique, les rendus existants ne changent pas : les tests actuels passent sans retouche.
- [ ] **Step 2: Lancer, constater les échecs**
- [ ] **Step 3: Implémenter**
- [ ] **Step 4: Lancer** — les cinq projets de modules : tout passe, sauf les 3 échecs connus de Network.
- [ ] **Step 5: Commit** — `feat(modules): barres, onglets, heure et fil suivent le thème`

---

### Task 8: Aperçus PNG des thèmes

**Files:**
- Create: `tools/Trofeo.ThemePreview/Trofeo.ThemePreview.csproj`. Cible `net10.0-windows10.0.19041.0`, `Exe`, `EnableWindowsTargeting`. Références : Core et les cinq projets de modules, plus `SkiaSharp.NativeAssets.Linux.NoDependencies` sous Linux. Le projet est ajouté à la solution.
- Create: `tools/Trofeo.ThemePreview/Program.cs`, `SampleData.cs` (fausses sources)
- Test: `tests/Trofeo.Core.Tests/ThemeRenderTests.cs` (composeur et vrais modules Clock et Network). Le contrôle de débordement des cinq modules est la commande `--check` de l'outil : seul l'outil référence les cinq modules.

**Commandes :**
- `--out <dossier>` (par défaut `apercus/`). Écrit :
  - `classique.png`, `liquid-glass.png` et `pixel.png` : chaque thème avec son fond par défaut ;
  - `<thème>-opacite-0.png`, `<thème>-opacite-1.png` ;
  - `<thème>-texte-petit.png`, `<thème>-texte-grand.png` ;
  - `<thème>-fond-clair.png` : sur une image blanche, ce qui montre la garde de lisibilité ;
  - `liquid-glass-gif-<n>.png` : les 4 premières images d'un GIF fabriqué, à 83 ms d'intervalle.
- `--theme <id> [--opacity x] [--font <famille>] [--background color:#hex|image:<chemin>|gif:<chemin>] [--text-size small|normal|large] --png <fichier>` : une image à la demande.
- `--check` : rend chaque thème, à chaque taille de texte, avec des textes longs. Il vérifie que rien n'est peint dans les écarts du damier, hors du fond. Il affiche `OK` ou la liste des fautes, et rend 1 en cas de faute.

**Contenu** : les données de la maquette, avec les cinq modules à leur place par défaut.
- Claude Code : onglets « thermalright-dash » (travaille), « api-server » (attend), « blog » (inactif) ; tâche « Ajoute le module météo » ; les cinq lignes du fil ; contexte 42 %, durée 6 min, Opus 5.5, quotas 38 % et 71 %.
- Monitoring : « Core Ultra 7 265K · RTX 5070 », CPU 34 % 52 °C, GPU 61 % 64 °C, RAM 18,2 / 32 Go.
- En écoute : Spotify, « VALHALLA », Yo Joshy, 1:24 / 3:12. La pochette est un dégradé fabriqué.
- Heure : 14:32, samedi 24 octobre, 12° « Éclaircies ».
- Réseau : Freebox, 412 / 38 Mb/s.
- Les vrais modules sont démarrés avec ces fausses sources. L'aperçu attend leur première donnée (au plus 3 s), puis compose avec `FrameComposer`, comme le service.

- [ ] **Step 1: Test** `Compose_AllThemes_Deterministic` (Core.Tests) : deux compositions du même thème, avec les mêmes modules Clock et Network sur de fausses sources, donnent les mêmes octets PNG.
- [ ] **Step 2: Écrire l'outil**
- [ ] **Step 3: Vérifier**
  - Run : `dotnet run --project tools/Trofeo.ThemePreview -- --check`. Expected : `OK`.
  - Run : `dotnet run --project tools/Trofeo.ThemePreview -- --out <scratchpad>/apercus`. Puis **regarder chaque PNG** (la session principale le fait elle-même).
  - Comparer à la maquette : couleurs, arrondis, flou, segments, et texte lisible au premier coup d'œil.
  - Noter les écarts dans l'état cloud.
- [ ] **Step 4: Commit** — `feat(tools): aperçus PNG des trois thèmes et contrôle de débordement`
- [ ] **Step 5: Envoyer les aperçus à Alex** pour qu'il juge le rendu avant l'écran.

---

### Task 9: Vidéo par Media Foundation (sur le PC d'Alex)

**Files:**
- Create: `src/Trofeo.Display/Video/MediaFoundationBackground.cs` (Windows)
- Test: `tests/Trofeo.Display.Tests/MediaFoundationBackgroundTests.cs`

**Interfaces:**
- Produces : `public sealed class MediaFoundationBackground : IBackground { public static MediaFoundationBackground? TryOpen(string path, Action<string>? warn); }`, passée à `BackgroundFactory.Create` comme `video`.

**Règles :**
- **Lecture.** `IMFSourceReader` avec `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = 1`, sortie `MFVideoFormat_RGB32`.
  - Interop : le paquet `Vortice.MediaFoundation` (licence MIT, dernière version stable), ou des déclarations COM écrites à la main si le paquet pèse plus de 2 Mo à la publication. On mesure avant de choisir.
- **Cadence.** On lit les images au fil du temps et on garde la dernière dont l'horodatage ≤ temps écoulé. On ne recadre qu'au plus 12 fois par seconde.
  - Le décodeur doit lire toutes les images de la vidéo : une vidéo à 60 images/s coûte plus qu'une à 24.
  - Le conseil « 1080p, 30 images/s au plus » va dans le README.
- **Fin de la vidéo** : retour au début (`SetCurrentPosition(0)`), sans image noire.
- **Fichier non lisible** (codec absent, fichier abîmé) : `null` et l'avertissement « Fond d'écran illisible… ».
- **Pas de son, jamais.** Seul le flux vidéo est sélectionné.

- [ ] **Step 1: Tests**, sur une petite vidéo MP4 fabriquée au début du test par l'encodeur Media Foundation (`IMFSinkWriter`, 64 × 36, 10 images de 100 ms, couleurs connues), ou à défaut un fichier de 10 Ko ajouté aux tests :
  - `Video_FollowsElapsedTime` ;
  - `Video_LoopsWithoutBlackFrame` ;
  - `Video_BadFile_IsNull`.
- [ ] **Step 2: Implémenter**
- [ ] **Step 3: Lancer** — `dotnet test` complet sous Windows : tout passe.
- [ ] **Step 4: Commit** — `feat(display): fond vidéo par Media Foundation`

---

### Task 10: Branchement, réglage en ligne de commande et vérification sur l'écran (sur le PC d'Alex)

**Files:**
- Modify: `src/Trofeo.Display/DisplayLoop.cs`, `Program.cs`
- Modify: `tools/Trofeo.HardwareCheck/Program.cs` (`--appearance`)
- Modify: `README.md`
- Test: `tests/Trofeo.Display.Tests/DisplayLoopTests.cs`

**Ce qui est branché :**
- **Au démarrage**, `Program` lit `layout.Appearance`, appelle `AppearanceResolver.Resolve(…, new SkiaSystemFonts(), Trace.TraceWarning)`, puis `BackgroundFactory.Create(…, MediaFoundationBackground.TryOpen, Trace.TraceWarning)`. Il donne les deux à `DisplayLoop`.
- **`DisplayLoop`** :
  - compose avec `ThemeDefinition` et le fond ;
  - appelle `background.Advance(temps depuis le démarrage)` à chaque `Tick`, et `MarkDirty()` si l'image a changé ;
  - le délai renvoyé tient compte de `UntilNextFrame` ;
  - un fond fixe ne change rien au rythme actuel (1 image/s de maintien).
- **`HardwareCheck --appearance`** :
  - options `theme=<id>`, `opacity=<0..1|theme>`, `font=<famille|theme>` et `background=theme|color:#hex|image:<chemin>|gif:<chemin>|video:<chemin>` ;
  - il écrit dans `%AppData%\trofeo-HB-dashboard\layout.json` et garde le reste du fichier ;
  - `--appearance` seul affiche les réglages actuels, puis la liste des polices du système (`SkiaSystemFonts.Families`) ;
  - Alex le lance dans **son propre terminal** (piège MSIX, voir CLAUDE.md).

- [ ] **Step 1: Tests**
  - `Loop_StaticBackground_KeepsOneFramePerSecond` : avec `FakeTimeProvider`, sur 10 s sans module animé, 10 envois au plus.
  - `Loop_AnimatedBackground_FollowsFrames` : un faux fond animé à 100 ms donne une image par changement, et jamais plus de 12 par seconde.
  - `HardwareCheck_Appearance_KeepsPlacements` : les placements et les raccourcis sont relus à l'identique après l'écriture.
- [ ] **Step 2: Implémenter**
- [ ] **Step 3: `dotnet test` complet sous Windows**, puis construire en Release sans avertissement.
- [ ] **Step 4: Sur l'écran, avec Alex** :
  - **Les trois thèmes** sur leur fond par défaut. Pour chacun : lisible d'un coup d'œil ? Fidèle à la maquette ?
  - **Transparence** à 0, à 0,5 et à 1.
  - **Une police du système** (par exemple Segoe UI), puis une police absente (avertissement dans le journal).
  - **Taille du texte** petite et grande sur un module.
  - **Fonds** : une couleur, une photo, un GIF et une vidéo.
- [ ] **Step 5: Sobriété** (comme au plan 3, 5 min par mesure). Pour chaque cas, relever le processeur et la mémoire propre :
  - Classique sur fond fixe ;
  - Liquid Glass sur fond fixe ;
  - Liquid Glass sur GIF ;
  - Liquid Glass sur vidéo 1080p.

  Objectif : environ 1 % de processeur et moins de 100 Mo. Un fond animé peut dépasser l'objectif de processeur : chiffrer l'écart et le dire à Alex.
- [ ] **Step 6: README** : choisir son thème et son fond avec `HardwareCheck --appearance`, et les crédits des polices.
- [ ] **Step 7: Commit** — `feat(display): thèmes, transparence, polices et fonds d'écran branchés`

---

### Task 11: Bilan

- [ ] Écrire `docs/superpowers/plans/2026-10-2x-plan-4-bilan.md` (état, mesures, décisions, ce qui revient au plan 5 : l'onglet Personnalisation), puis mettre à jour CLAUDE.md.
