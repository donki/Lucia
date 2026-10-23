using System.Security.Cryptography;
using System.Text;

namespace SocLucia.Services;

/// <summary>
/// Cifrado del perfil cuando sale del equipo a la nube del usuario (Google Drive u OneDrive), con una
/// <b>frase del usuario</b> (no DPAPI: DPAPI ata al usuario de este Windows y el perfil tiene que poder
/// leerse en otro PC). AES-256-GCM (autenticado) con clave derivada por PBKDF2-SHA256 (600 000
/// iteraciones) y sal aleatoria. Formato <c>enc1:</c> + base64 de <c>sal(16)|nonce(12)|etiqueta(16)|cifrado</c>.
/// Mismo esquema que RC Manager. Hasta donde llega: protege lo que hay en la nube de quien tenga acceso
/// a esa cuenta; quien tenga la frase, lo lee.
/// </summary>
public static class CloudVault
{
    private const string Prefix = "enc1:";
    private const int Iterations = 600_000;

    public static string Encrypt(string plain, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Derive(passphrase, salt);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, data, cipher, tag);

        var all = new byte[16 + 12 + 16 + cipher.Length];
        salt.CopyTo(all, 0);
        nonce.CopyTo(all, 16);
        tag.CopyTo(all, 28);
        cipher.CopyTo(all, 44);
        return Prefix + Convert.ToBase64String(all);
    }

    /// <summary>Descifra. Lanza <see cref="CryptographicException"/> si la frase no es la que era.</summary>
    public static string Decrypt(string stored, string passphrase)
    {
        if (!IsEncrypted(stored))
            throw new CryptographicException("El perfil no está cifrado con un esquema conocido.");
        var all = Convert.FromBase64String(stored[Prefix.Length..]);
        var salt = all[..16];
        var nonce = all[16..28];
        var tag = all[28..44];
        var cipher = all[44..];
        var key = Derive(passphrase, salt);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public static bool IsEncrypted(string text) => text.StartsWith(Prefix, StringComparison.Ordinal);

    private static byte[] Derive(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
}
