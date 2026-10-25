using System.Security.Cryptography;
using System.Text;

namespace Trofeo.Modules.Network;

/// <summary>Calcul du mot de passe de session de l'API Freebox.</summary>
public static class FreeboxAuth
{
    /// <summary>
    /// Mot de passe d'ouverture de session : HMAC-SHA1 du <paramref name="challenge"/> avec le jeton d'application
    /// pour clé (tous deux en UTF-8), en hexadécimal minuscule.
    /// </summary>
    public static string Password(string appToken, string challenge) =>
        Convert.ToHexStringLower(HMACSHA1.HashData(Encoding.UTF8.GetBytes(appToken), Encoding.UTF8.GetBytes(challenge)));
}
