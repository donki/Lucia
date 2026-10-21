using System.Security.Cryptography;
using System.Text;

namespace SocLucia.Services;

/// <summary>
/// Secretos (contraseña y token de MLflow) cifrados con DPAPI, ligados al usuario de Windows: en
/// settings.json solo queda <c>dpapi1:</c> y el cifrado, nunca el texto. Lo mismo que RC Manager.
/// </summary>
/// <remarks>
/// Hasta donde llega: protege el fichero en reposo (copiado a otro equipo o leido por otra cuenta no
/// revela nada). Un programa que corra con la sesion del usuario puede descifrarlo igual que esta
/// aplicacion; eso es cosa del sistema.
/// </remarks>
public static class Secrets
{
    private const string Prefix = "dpapi1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("sOCLucia");

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain))
            return string.Empty;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>El texto; vacio si no hay, si no es de este usuario y equipo o si esta roto.</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
