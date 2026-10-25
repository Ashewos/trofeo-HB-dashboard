namespace Trofeo.StatusLine;

/// <summary>
/// La commande <c>HardwareCheck --claude-statusline install &lt;exe&gt; | uninstall | status</c> : branche, débranche ou
/// décrit la barre d'état dans le <c>settings.json</c> de Claude Code, avec des messages en français. Les flux sont
/// passés en paramètres pour que les tests lisent ce qui est affiché.
/// </summary>
public static class StatusLineCommand
{
    private const string Usage =
        "Usage : Trofeo.HardwareCheck --claude-statusline install <chemin de Trofeo.StatusLine.exe> | uninstall | status";

    /// <summary>Rend le code de sortie : 0 réussi, 1 échec (fichier illisible ou impossible à écrire), 2 mauvais arguments.</summary>
    /// <param name="args">Ce qui suit <c>--claude-statusline</c>.</param>
    public static int Run(string[] args, string claudeHome, TextWriter output, TextWriter error)
    {
        var installer = new StatusLineInstaller(claudeHome);
        try
        {
            return args switch
            {
                ["install", var exe] => Install(installer, exe, output, error),
                ["uninstall"] => Uninstall(installer, output, error),
                ["status"] => Status(installer, output),
                _ => Fail(error, Usage, 2),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pas de trace de pile : le message de Windows dit déjà ce qui bloque (fichier ouvert, lecture seule…).
            return Fail(error, $"Impossible d'écrire {installer.SettingsPath} : {ex.Message}", 1);
        }
    }

    private static int Install(StatusLineInstaller installer, string exe, TextWriter output, TextWriter error)
    {
        if (!File.Exists(exe)) return Fail(error, $"Trofeo.StatusLine.exe introuvable : {exe}", 2);

        // Lue avant l'installation : après, elle est enveloppée dans la nôtre.
        var previous = installer.CurrentCommand();
        var result = installer.Install(exe);
        switch (result)
        {
            case StatusLineInstallResult.Installed:
                output.WriteLine($"Barre d'état installée dans {installer.SettingsPath}.");
                output.WriteLine(previous is null
                    ? "Tu n'avais pas de barre d'état : seule celle de Trofeo.StatusLine s'affichera."
                    : "Ta barre d'état d'origine est gardée : elle s'affichera toujours, derrière la nôtre.");
                WarnAboutComments(installer, output);
                if (previous is not null && EndsWithShellComment(previous))
                {
                    output.WriteLine(
                        "Attention : ta barre d'état d'origine se termine par un commentaire (# …). Le chaînage ajoute une "
                        + "parenthèse fermante après elle, qui finirait dans ce commentaire et casserait la commande. "
                        + $"Retire ce commentaire de la commande dans {installer.SettingsPath}.");
                }

                return 0;
            case StatusLineInstallResult.Updated:
                output.WriteLine($"Barre d'état déjà installée, mais l'exécutable avait changé de place : commande mise à jour dans {installer.SettingsPath}.");
                output.WriteLine("Ta barre d'état d'origine, s'il y en avait une, reste chaînée derrière la nôtre.");
                WarnAboutComments(installer, output);
                return 0;
            case StatusLineInstallResult.AlreadyInstalled:
                output.WriteLine($"Barre d'état déjà installée dans {installer.SettingsPath} : rien à faire.");
                return 0;
            default:
                return Fail(error, UnreadableMessage(installer), 1);
        }
    }

    private static int Uninstall(StatusLineInstaller installer, TextWriter output, TextWriter error)
    {
        switch (installer.Uninstall())
        {
            case StatusLineInstallResult.Uninstalled:
                output.WriteLine($"Barre d'état retirée de {installer.SettingsPath} (ta barre d'état d'origine, s'il y en avait une, est restaurée).");
                return 0;
            case StatusLineInstallResult.NotInstalled:
                output.WriteLine($"La barre d'état n'est pas installée dans {installer.SettingsPath} : rien à faire.");
                return 0;
            default:
                return Fail(error, UnreadableMessage(installer), 1);
        }
    }

    private static int Status(StatusLineInstaller installer, TextWriter output)
    {
        output.WriteLine(installer.IsInstalled() ? "Barre d'état installée." : "Barre d'état pas installée.");
        output.WriteLine($"Fichier de réglages : {installer.SettingsPath}");
        return 0;
    }

    /// <summary>
    /// Le fichier est réécrit sans ses commentaires ; la copie d'origine <c>.trofeo-bak</c> les garde. Sans copie, il
    /// n'y avait pas de fichier avant : rien à signaler.
    /// </summary>
    private static void WarnAboutComments(StatusLineInstaller installer, TextWriter output)
    {
        if (!File.Exists(installer.BackupPath)) return;
        output.WriteLine(
            "Attention : si settings.json contenait des commentaires, ils ne sont pas gardés dans le fichier réécrit ; "
            + $"ils restent dans la copie {installer.BackupPath}.");
    }

    private static string UnreadableMessage(StatusLineInstaller installer) =>
        $"{installer.SettingsPath} (ou la sauvegarde de ta barre d'état d'origine) n'est pas un JSON lisible : rien n'a été modifié.";

    private static int Fail(TextWriter error, string message, int code)
    {
        error.WriteLine(message);
        return code;
    }

    /// <summary>
    /// Vrai si la dernière ligne de la commande se termine par un commentaire shell (<c>#</c> en début de mot, hors
    /// guillemets). Enveloppée dans <c>( … )</c>, la parenthèse fermante finirait dedans.
    /// </summary>
    public static bool EndsWithShellComment(string command)
    {
        var quote = '\0';
        var inComment = false;
        var previous = ' '; // le début de la commande compte comme une séparation
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (inComment)
            {
                if (c == '\n')
                {
                    inComment = false;
                    previous = c;
                }

                continue;
            }

            if (c == '\\' && quote != '\'')
            {
                i++; // le caractère protégé n'a aucun rôle (guillemet échappé, « \# »)
                previous = 'x';
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c == '#' && char.IsWhiteSpace(previous))
            {
                inComment = true;
            }

            previous = c;
        }

        return inComment;
    }
}
