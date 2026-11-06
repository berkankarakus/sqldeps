using Microsoft.Data.Sqlite;

namespace SqlDeps.Tests.Core;

public sealed class SqliteMetadataMapperTests
{
    [Fact]
    public void ResultColumnRows_RoundTripKnownAndUnknownMetadata()
    {
        var value = new ResultColumn(
            "dbo.Report",
            1,
            "column2",
            "report.sql",
            12,
            false,
            null
        );

        var mapped = SqliteMetadataMapper.ToRow(value).ToModel();

        Assert.Equal(value, mapped);
    }

    [Fact]
    public void TableColumnRows_RoundTripNullableMetadata()
    {
        var value = new TableColumn(
            "dbo.Report",
            "CreatedAt",
            "datetime2(3)",
            "report.sql",
            8,
            null,
            true,
            null,
            false,
            3,
            null,
            "Turkish_CI_AS"
        );

        var mapped = SqliteMetadataMapper.ToRow(value).ToModel();

        Assert.Equal(value, mapped);
    }

    [Fact]
    public void TableIndexColumnRows_RoundTripFilterAndOrderingMetadata()
    {
        var value = new TableIndexColumn(
            "dbo.Report",
            "IX_Report_CreatedAt",
            "CreatedAt",
            0,
            false,
            true,
            false,
            false,
            true,
            "[IsDeleted] = 0",
            "report.sql",
            22
        );

        var mapped = SqliteMetadataMapper.ToRow(value).ToModel();

        Assert.Equal(value, mapped);
    }

    [Fact]
    public void ReadResultColumn_MapsSqliteNullAndBooleanValues()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'dbo.Report', 1, 'column2', 'report.sql', 12, 0, NULL";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        var result = SqliteMetadataMapper.ReadResultColumn(reader).ToModel();

        Assert.Equal(
            new ResultColumn("dbo.Report", 1, "column2", "report.sql", 12, false, null),
            result
        );
    }

    [Fact]
    public void ReadTableColumn_MapsNullableAndOptionalSqliteValues()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'dbo.Report', 'CreatedAt', 'datetime2', NULL, 1, NULL, 0, NULL, NULL, NULL, 'report.sql', 8";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        var result = SqliteMetadataMapper.ReadTableColumn(reader).ToModel();

        Assert.Equal(
            new TableColumn("dbo.Report", "CreatedAt", "datetime2", "report.sql", 8, null, true, null, false, null, null, null),
            result
        );
    }

    [Fact]
    public void ReadTableIndexColumn_MapsFilterAndBooleanFlags()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'dbo.Report', 'IX_Report', 'CreatedAt', 0, 0, 1, 1, 0, 1, '[IsDeleted] = 0', 'report.sql', 22";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        var result = SqliteMetadataMapper.ReadTableIndexColumn(reader).ToModel();

        Assert.Equal(
            new TableIndexColumn("dbo.Report", "IX_Report", "CreatedAt", 0, false, true, true, false, true, "[IsDeleted] = 0", "report.sql", 22),
            result
        );
    }
}
