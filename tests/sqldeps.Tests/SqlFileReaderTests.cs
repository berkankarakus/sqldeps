using System.Text;
using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlFileReaderTests
{
    [Fact]
    public void Decode_Utf16LittleEndianBom_DecodesTheTextWithoutBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("SELECT N'ç';")).ToArray();

        var text = SqlFileReader.Decode(bytes);

        Assert.Equal("SELECT N'ç';", text);
    }

    [Fact]
    public void HashOf_EqualContentIsStableAndDifferentContentChangesHash()
    {
        var first = SqlFileReader.HashOf(Encoding.UTF8.GetBytes("SELECT 1;"));
        var repeated = SqlFileReader.HashOf(Encoding.UTF8.GetBytes("SELECT 1;"));
        var changed = SqlFileReader.HashOf(Encoding.UTF8.GetBytes("SELECT 2;"));

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, changed);
        Assert.Equal(64, first.Length);
    }
}
