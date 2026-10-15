using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deleguard.Desktop.Core;

public sealed record VaultEnvelope(int Version, string Protection, string Identity, string Salt, int Iterations, string Nonce, string Tag, string Ciphertext);

public static class ProjectVault
{
    public const int Iterations = 600_000;
    public static string Seal(string secret, string identity, string? password, bool? useWindows = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(secret, "^dgk_[A-Za-z0-9_-]{1,200}$")) throw new SetupException("invalid_key");
        var plain = Encoding.UTF8.GetBytes(secret); var aad = Encoding.UTF8.GetBytes(identity);
        try
        {
            if (useWindows ?? OperatingSystem.IsWindows())
                return JsonSerializer.Serialize(new VaultEnvelope(1, "dpapi-current-user", identity, "", 0, "", "", Convert.ToBase64String(Dpapi.Transform(plain, aad, true))));
            if (password is null || password.Length < 12 || password.Length > 1024) throw new SetupException("password_length");
            var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var ciphertext = new byte[plain.Length];
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, ciphertext, tag, aad); }
            finally { CryptographicOperations.ZeroMemory(key); }
            return JsonSerializer.Serialize(new VaultEnvelope(1, "aes256gcm-pbkdf2-sha256", identity, Convert.ToBase64String(salt), Iterations,
                Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext)));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static byte[] Open(string envelope, string identity, string? password)
    {
        try
        {
            if (envelope.Length > 8192) throw new CryptographicException();
            var vault = JsonSerializer.Deserialize<VaultEnvelope>(envelope) ?? throw new CryptographicException();
            if (vault.Version != 1 || vault.Identity != identity) throw new CryptographicException();
            var data = Convert.FromBase64String(vault.Ciphertext); var aad = Encoding.UTF8.GetBytes(identity);
            if (vault.Protection == "dpapi-current-user" && OperatingSystem.IsWindows()) return Dpapi.Transform(data, aad, false);
            if (vault.Protection != "aes256gcm-pbkdf2-sha256" || password is null || vault.Iterations != Iterations) throw new CryptographicException();
            var salt = Convert.FromBase64String(vault.Salt); var nonce = Convert.FromBase64String(vault.Nonce); var tag = Convert.FromBase64String(vault.Tag);
            if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16 || data.Length > 256) throw new CryptographicException();
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32); var plain = new byte[data.Length];
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, data, tag, plain, aad); return plain; }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        { throw new SetupException("vault_locked"); }
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new SetupException("vault_locked");
        var a = new Blob { Size=input.Length, Data=Marshal.AllocHGlobal(input.Length) }; var b=new Blob { Size=entropy.Length, Data=Marshal.AllocHGlobal(entropy.Length) }; Blob output=default;
        try
        {
            Marshal.Copy(input,0,a.Data,input.Length); Marshal.Copy(entropy,0,b.Data,entropy.Length);
            var ok = protect ? CryptProtectData(ref a,null,ref b,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref a,IntPtr.Zero,ref b,IntPtr.Zero,IntPtr.Zero,1,out output);
            if (!ok) throw new CryptographicException();
            var bytes=new byte[output.Size]; Marshal.Copy(output.Data,bytes,0,bytes.Length); return bytes;
        }
        finally
        {
            Marshal.Copy(new byte[a.Size],0,a.Data,a.Size); Marshal.FreeHGlobal(a.Data); Marshal.FreeHGlobal(b.Data);
            if(output.Data!=IntPtr.Zero){Marshal.Copy(new byte[output.Size],0,output.Data,output.Size);LocalFree(output.Data);}
        }
    }
}
