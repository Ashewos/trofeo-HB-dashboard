using Trofeo.ModuleKit;

namespace Trofeo.Modules.Agents;

/// <summary>Les réglages que partagent les modules Claude Code et Codex (les clés lues par <see cref="AgentSettings"/>).</summary>
internal static class AgentSchema
{
    public static IReadOnlyList<SettingField> CommonFields() =>
    [
        new ChoiceField("view", "Affichage",
            [new SettingOption("feed", "Fil d'activité"), new SettingOption("overview", "Vue d'ensemble")], "feed"),
        new ToggleField("autoFollow", "Passer à la session qui t'attend", true),
        new ToggleField("feedFade", "Faire pâlir les lignes anciennes", false),
        new ChoiceField("feedNewest", "Ligne la plus récente",
            [new SettingOption("top", "En haut"), new SettingOption("bottom", "En bas, comme un terminal")], "top"),
    ];
}
