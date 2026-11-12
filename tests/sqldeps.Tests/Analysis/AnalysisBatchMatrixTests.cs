using SqlDeps.Analysis;

namespace SqlDeps.Tests.Analysis;

public sealed class AnalysisBatchMatrixTests
{
    public static TheoryData<string, string> AnalysisBatches
    {
        get
        {
            TheoryData<string, string> data = new();
            data.Add("SELECT * FROM dbo.Users;", "select-star");
            data.Add("SELECT id FROM dbo.Users WHERE name = NULL;", "null-comparison");
            data.Add("SELECT id FROM dbo.Users WHERE name LIKE '%admin';", "leading-wildcard-like");
            data.Add("SELECT id FROM dbo.Users WHERE id NOT IN (1, NULL);", "not-in-null");
            data.Add("SELECT id FROM dbo.Users WHERE LOWER(name) = 'admin';", "non-sargable-function");
            data.Add("SELECT CASE WHEN id = 1 THEN 1 ELSE 'one' END FROM dbo.Users;", "case-mixed-constants");
            data.Add("SELECT CASE WHEN id = 1 THEN 1E2 ELSE 'one' END FROM dbo.Users;", "case-mixed-constants");
            data.Add("SELECT TOP (0) id FROM dbo.Users;", "top-zero");
            data.Add("SELECT id FROM dbo.Users ORDER BY id OFFSET -1 ROWS;", "negative-offset");
            data.Add("SELECT id FROM dbo.Users ORDER BY id OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;", "invalid-fetch");
            data.Add("UPDATE dbo.Users SET active = 1 WHERE 1 = 0;", "dml-always-false-filter");
            data.Add("DELETE FROM dbo.Users WHERE 1 = 1;", "dml-ineffective-filter");
            data.Add("EXEC(N'SELECT id FROM dbo.Users WHERE id = ' + @id);", "dynamic-sql-concatenation");
            data.Add("SELECT id FROM dbo.Users WITH (NOLOCK);", "nolock");
            data.Add("SELECT id FROM dbo.Users WITH (READUNCOMMITTED);", "read-uncommitted");
            data.Add("MERGE dbo.Users AS target USING dbo.OtherUsers AS source ON target.id = source.id WHEN MATCHED THEN UPDATE SET active = source.active;", "merge");
            data.Add("SELECT a.id FROM dbo.Users AS a JOIN dbo.OtherUsers AS a ON a.id = a.id;", "duplicate-alias");
            data.Add("SELECT missing.id FROM dbo.Users AS users;", "unbound-identifier");
            data.Add("SELECT dbo.Users.id FROM dbo.Users AS users;", "table-name-after-alias");
            data.Add("SELECT users.dbo.id FROM dbo.Users AS users;", "invalid-alias-reference");
            data.Add("SELECT id FROM dbo.Users UNION SELECT id, name FROM dbo.OtherUsers;", "set-operation-column-count");
            data.Add("SELECT status, COUNT(*) FROM dbo.Users GROUP BY id;", "ungrouped-select-column");
            data.Add("BEGIN TRANSACTION; SELECT 1;", "transaction-without-error-handling");
            data.Add("WITH Numbers AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM Numbers) SELECT n FROM Numbers;", "recursive-cte");
            data.Add("SELECT COUNT(id) FROM dbo.Users;", "count-column");
            data.Add("SELECT id FROM dbo.Users JOIN dbo.OtherUsers ON 1 = 1;", "join-ineffective-condition");
            data.Add("SELECT DISTINCT id FROM dbo.Users ORDER BY name;", "distinct-order-by");
            data.Add("SELECT id FROM dbo.Users JOIN dbo.OtherUsers ON 1 = 0;", "join-always-false-condition");

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AnalysisBatches))]
    public void Analyze_ReportsExpectedFindingForBatch(string sql, string expectedRule)
    {
        var result = DocumentAnalysis.Analyze("analysis.sql", sql);

        Assert.Contains(result.LintFindings, finding => finding.Rule == expectedRule);
    }
}
