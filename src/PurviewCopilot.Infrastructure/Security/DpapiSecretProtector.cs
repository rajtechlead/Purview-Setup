using System.Runtime.InteropServices;
using System.Text;

namespace PurviewCopilot.Infrastructure.Security;

/// <summary>
/// Protects the lab client secret in the settings document with Windows DPAPI scoped to the
/// current user. A value protected by one user or machine cannot be decrypted by another, and
/// no key material is stored alongside the data. Implemented with crypt32 directly so the
/// Infrastructure project needs no additional package.
/// </summary>
public static class DpapiSecretProtector
{
    private const string Prefix = "dpapi:v1:";
    private const int CryptProtectUiForbidden = 0x1;

    public static bool IsProtected(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var data = Encoding.UTF8.GetBytes(plaintext);
        var input = default(DataBlob);
        var output = default(DataBlob);

        try
        {
            input.Data = Marshal.AllocHGlobal(data.Length);
            input.Size = data.Length;
            Marshal.Copy(data, 0, input.Data, data.Length);

            if (!CryptProtectData(ref input, "Purview Governance Copilot", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref output))
            {
                throw new InvalidOperationException(
                    $"DPAPI could not protect the secret (Win32 error {Marshal.GetLastWin32Error()}).");
            }

            var cipher = new byte[output.Size];
            Marshal.Copy(output.Data, cipher, 0, output.Size);
            return Prefix + Convert.ToBase64String(cipher);
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    /// <summary>
    /// Returns the plaintext for a protected value, the value unchanged when it was never
    /// protected, or null when it cannot be decrypted by this user (for example after a profile
    /// or machine change). Callers treat null as "secret not available" and re-prompt.
    /// </summary>
    public static string? UnprotectIfNeeded(string? value)
    {
        if (!IsProtected(value))
        {
            return value;
        }

        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(value![Prefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }

        var input = default(DataBlob);
        var output = default(DataBlob);

        try
        {
            input.Data = Marshal.AllocHGlobal(cipher.Length);
            input.Size = cipher.Length;
            Marshal.Copy(cipher, 0, input.Data, cipher.Length);

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref output))
            {
                return null;
            }

            var plain = new byte[output.Size];
            Marshal.Copy(output.Data, plain, 0, output.Size);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LocalFree(IntPtr memory);
}
