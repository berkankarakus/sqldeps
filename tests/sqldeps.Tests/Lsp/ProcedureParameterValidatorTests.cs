using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class ProcedureParameterValidatorTests
{
    [Fact]
    public void Validate_ReportsMissingRequiredParameter()
    {
        var findings = Validate(
            "EXEC dbo.RunReport @id = 1;",
            [
                new ObjectParameter("dbo.RunReport", 0, "@id", "int", null, false, "procedure.sql"),
                new ObjectParameter("dbo.RunReport", 1, "@name", "nvarchar(50)", null, false, "procedure.sql"),
            ]
        );

        var finding = Assert.Single(findings);
        Assert.Equal("missing-procedure-parameter", finding.Rule);
        Assert.Contains("@name", finding.Message);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.True(finding.Length > 0);
    }

    [Fact]
    public void Validate_DoesNotReportMissingParametersForCompletePositionalCall()
    {
        var findings = Validate(
            "EXEC dbo.RunReport 1, 'Ada';",
            [
                new ObjectParameter("dbo.RunReport", 0, "@id", "int", null, false, "procedure.sql"),
                new ObjectParameter("dbo.RunReport", 1, "@name", "nvarchar(50)", null, false, "procedure.sql"),
            ]
        );

        Assert.DoesNotContain(findings, finding => finding.Rule == "missing-procedure-parameter");
    }

    [Fact]
    public void Validate_ReportsIgnoredOutputParameter()
    {
        var findings = Validate(
            "EXEC dbo.RunReport @result = @value;",
            [
                new ObjectParameter("dbo.RunReport", 0, "@result", "int", null, true, "procedure.sql"),
            ]
        );

        var finding = Assert.Single(findings);
        Assert.Equal("procedure-output-ignored", finding.Rule);
        Assert.Contains("@result", finding.Message);
    }

    [Fact]
    public void Validate_ReportsLiteralTypeMismatch()
    {
        var findings = Validate(
            "EXEC dbo.RunReport @id = N'abc';",
            [
                new ObjectParameter("dbo.RunReport", 0, "@id", "int", null, false, "procedure.sql"),
            ]
        );

        var finding = Assert.Single(findings);
        Assert.Equal("procedure-parameter-type", finding.Rule);
        Assert.Contains("cannot be converted", finding.Message);
    }

    [Fact]
    public void Validate_DoesNotReportConvertibleDateAndGuidLiterals()
    {
        var findings = Validate(
            "EXEC dbo.RunReport @id = 1, @key = '6F9619FF-8B86-D011-B42D-00C04FC964FF', @when = '2026-11-06';",
            [
                new ObjectParameter("dbo.RunReport", 0, "@id", "int", null, false, "procedure.sql"),
                new ObjectParameter("dbo.RunReport", 1, "@key", "uniqueidentifier", null, false, "procedure.sql"),
                new ObjectParameter("dbo.RunReport", 2, "@when", "date", null, false, "procedure.sql"),
            ]
        );

        Assert.DoesNotContain(findings, finding => finding.Rule == "procedure-parameter-type");
    }

    [Fact]
    public void Validate_DoesNotReportSystemProceduresAsUnknown()
    {
        var findings = Validate("EXEC sys.sp_who;", []);

        Assert.Empty(findings);
    }

    private static IReadOnlyList<LintFinding> Validate(
        string sql,
        IReadOnlyList<ObjectParameter> parameters
    )
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        Assert.Empty(errors);

        var script = Assert.IsType<TSqlScript>(fragment);
        var batches = new[] { new ParsedSqlBatch(script, sql, 1) };
        var objects = new[] { new DatabaseObject("dbo.RunReport", "procedure", "procedure.sql", 1) };

        return ProcedureParameterValidator.Validate(
            "call.sql",
            batches,
            objects,
            parameters
        ).ToArray();
    }
}
