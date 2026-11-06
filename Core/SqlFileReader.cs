using System.Security.Cryptography;
using System.Text;

namespace SqlDeps;

/// <summary>
/// Reads a script whatever its encoding. A byte order mark decides first (UTF-8, UTF-16, UTF-32);
/// without one the bytes are read as UTF-8, and when they are not valid UTF-8 as the Turkish Windows
/// code page, which is how SQL Server Management Studio saves files on a Turkish Windows.
/// </summary>
public static class SqlFileReader
{
    private const int TurkishWindowsCodePage = 1254;
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianBom = [0xFE, 0xFF];

    public static string Decode(byte[] bytes)
    {
        if (StartsWith(bytes, Utf8Bom))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (StartsWith(bytes, Utf32LittleEndianBom))
        {
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        }

        if (StartsWith(bytes, Utf16LittleEndianBom))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (StartsWith(bytes, Utf16BigEndianBom))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        catch (DecoderFallbackException)
        {
            return TurkishWindowsEncoding().GetString(bytes);
        }
    }

    /// <summary>The SHA-256 of the file's bytes, used to tell whether it changed since the last run.</summary>
    public static string HashOf(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static Encoding TurkishWindowsEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(TurkishWindowsCodePage);
    }

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        return bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }
}
