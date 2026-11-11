using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Core;

namespace SqlDeps.Tests.Core;

public sealed class SqlFormatterTests
{
    [Fact]
    public void Format_PreservesCommentsStringsAndBatchBoundaries()
    {
        const string text = "-- keep this comment\nSELECT 'a  b' AS value FROM dbo.Info;\nGO\nSELECT 1;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("keep this comment", result.Text, StringComparison.Ordinal);
        Assert.Contains("'a  b'", result.Text, StringComparison.Ordinal);
        Assert.Contains("\nGO\n", result.Text, StringComparison.Ordinal);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Format_PreservesGoSeparatorComments()
    {
        const string text = "SELECT 1;\nGO -- keep this separator comment\nSELECT 2;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "\nGO -- keep this separator comment\n",
            result.Text,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Format_RejectsInvalidSqlWithoutChangingPreviewText()
    {
        const string text = "SELECT FROM;";

        var result = SqlFormatter.Format(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(text, result.Text);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Format_RejectsGoRepeatCountsUntilTheyCanRoundTripSafely()
    {
        const string text = "SELECT 1;\nGO 2\n";

        var result = SqlFormatter.Format(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(text, result.Text);
        Assert.Contains("repeat counts", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReturnsParseableOutput()
    {
        const string text = "select * from dbo.Info where id=1;";

        var result = SqlFormatter.Format(text);
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(result.Text);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        Assert.True(result.IsSuccess);
        Assert.NotNull(fragment);
        Assert.Empty(errors);
    }

    [Fact]
    public void Format_LeavesOneBlankLineAtTheEnd()
    {
        var result = SqlFormatter.Format("SELECT 1;");

        Assert.True(result.IsSuccess);
        Assert.EndsWith("\n\n", result.Text, StringComparison.Ordinal);
        Assert.Equal(result.Text, SqlFormatter.Format(result.Text).Text);
    }

    [Fact]
    public void Format_AddsAStatementTerminatorWhenTheCandidateStillParses()
    {
        var result = SqlFormatter.Format("SELECT 1");

        Assert.True(result.IsSuccess);
        Assert.Contains("SELECT 1;", result.Text, StringComparison.Ordinal);
    }

    private const string JoinQuery =
        "select o.id, c.name from Orders o with (nolock) "
        + "join Customers c with (nolock) on o.customerId = c.id "
        + "left join Regions r with (nolock) on r.id = o.regionId "
        + "where c.active = 1 and o.active = 1;";

    [Fact]
    public void Format_PutsSimpleJoinsOnOneLineWithShortKeywordsAndNoClauseAlignment()
    {
        var result = SqlFormatter.Format(JoinQuery);

        Assert.True(result.IsSuccess);
        Assert.Contains("FROM Orders AS o WITH (NOLOCK)\n", result.Text, StringComparison.Ordinal);
        Assert.Contains(
            "     JOIN Customers AS c WITH (NOLOCK) ON o.customerId = c.id\n",
            result.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "     LEFT JOIN Regions AS r WITH (NOLOCK) ON r.id = o.regionId\n",
            result.Text,
            StringComparison.Ordinal);
        Assert.Contains("\nWHERE\n    c.active = 1 AND\n    o.active = 1;", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("OUTER", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PutsSubqueryParenthesesOnOwnLinesAndIndentsTheQueryByFourSpaces()
    {
        const string text = "select 1 from a join (select x from b union select x from c) t on t.x = a.x;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "     JOIN\n     (\n         SELECT x\n         FROM b\n         UNION\n         SELECT x\n         FROM c\n     ) AS t\n     ON t.x = a.x",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IndentsSearchedCaseBranchesAndEnd()
    {
        const string text = "select case when a=1 then 'one' when a=2 then 'two' else 'other' end as label from dbo.t;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "SELECT CASE\n           WHEN a = 1 THEN 'one'\n           WHEN a = 2 THEN 'two'\n           ELSE 'other'\n       END AS label\nFROM dbo.t;",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IndentsSimpleCaseBranches()
    {
        const string text = "select case status when 1 then 'active' else 'inactive' end as state from dbo.t;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "SELECT CASE status\n           WHEN 1 THEN 'active'\n           ELSE 'inactive'\n       END AS state\nFROM dbo.t;",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IndentsNestedCaseBranchesRelativeToTheirParent()
    {
        const string text = "select case when a=1 then case when b=1 then 'x' else 'y' end else 'z' end as label from dbo.t;";

        var result = SqlFormatter.Format(text);
        Assert.True(result.IsSuccess);

        var lines = result.Text.Split('\n');
        var outerWhen = Assert.Single(lines, line => line.Contains("WHEN a = 1", StringComparison.Ordinal));
        var innerWhen = Assert.Single(lines, line => line.Contains("WHEN b = 1", StringComparison.Ordinal));

        Assert.True(IndentationOf(innerWhen) > IndentationOf(outerWhen));
        Assert.Equal(result.Text, SqlFormatter.Format(result.Text).Text);
    }

    [Fact]
    public void Format_CaseLayoutIsIdempotentAndPreservesLiteralText()
    {
        const string text = "select case when note = 'WHEN THEN' then 'ELSE' else 'END' end as label from dbo.t;";

        var first = SqlFormatter.Format(text);
        var second = SqlFormatter.Format(first.Text);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
        Assert.Contains("'WHEN THEN'", first.Text, StringComparison.Ordinal);
        Assert.Contains("'ELSE'", first.Text, StringComparison.Ordinal);
        Assert.Contains("'END'", first.Text, StringComparison.Ordinal);
    }

    private static int IndentationOf(string line)
    {
        return line.Length - line.TrimStart(' ').Length;
    }

    [Fact]
    public void Format_IndentsNestedSubqueriesByLevel()
    {
        const string text = "select 1 from a where a.x in (select b.x from b where b.y in (select c.y from c));";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("WHERE a.x IN\n      (\n          SELECT b.x\n          FROM b\n", result.Text, StringComparison.Ordinal);
        Assert.Contains(
            "          WHERE b.y IN\n                (\n                    SELECT c.y\n                    FROM c\n                )\n      );",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotTouchSubqueryTextInsideStringsOrComments()
    {
        const string text = "select '(select 1)' as v, '[' as w from a; -- (select 2)\nselect 1;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("'(select 1)'", result.Text, StringComparison.Ordinal);
        Assert.Contains("-- (select 2)", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_SubqueryLayoutIsIdempotent()
    {
        const string text = "select 1 from a where a.x in (select b.x from b where b.y in (select c.y from c));";

        var first = SqlFormatter.Format(text);
        var second = SqlFormatter.Format(first.Text);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Format_KeepsMultiLineJoinConditionsUnderTheJoin()
    {
        const string text = "select 1 from a join b on a.x = b.x and a.y = b.y;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("JOIN b\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("ON a.x = b.x\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("AND a.y = b.y", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotTouchJoinKeywordsInsideStringsOrComments()
    {
        const string text = "select 'x\nINNER JOIN\ny' as v from a;\n/*\nINNER JOIN\n*/\nselect 1;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("'x\nINNER JOIN\ny'", result.Text, StringComparison.Ordinal);
        Assert.Contains("/*\nINNER JOIN\n*/", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_IsIdempotent()
    {
        var first = SqlFormatter.Format(JoinQuery);
        var second = SqlFormatter.Format(first.Text);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Format_AppliesTheSameLayoutToUpdateAndDeleteStatements()
    {
        const string update = "update u set u.a = 1 from dbo.u u join dbo.v v on u.id = v.id where u.c in (select c from dbo.z) and u.d = 2;";
        const string delete = "delete u from dbo.u u where exists (select 1 from dbo.w w where w.id = u.id) and u.d = 2;";

        var updated = SqlFormatter.Format(update);
        var deleted = SqlFormatter.Format(delete);

        Assert.True(updated.IsSuccess);
        Assert.Contains("     JOIN dbo.v AS v ON u.id = v.id\n", updated.Text, StringComparison.Ordinal);
        Assert.Contains("WHERE\n    u.c IN\n    (\n        SELECT c\n        FROM dbo.z\n    ) AND\n    u.d = 2;", updated.Text, StringComparison.Ordinal);
        Assert.True(deleted.IsSuccess);
        Assert.Contains("WHERE\n    EXISTS\n    (\n        SELECT 1\n", deleted.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_AlignsInsertValuesRowsAndTidiesMergeAndTopStatements()
    {
        const string text = "insert into dbo.t (a, b) values (1, 'x'), (2, 'y'); "
            + "merge dbo.t as t using dbo.s as s on t.id = s.id when matched then update set t.v = s.v; "
            + "delete top (10) from dbo.q where k = 1; update top (5) dbo.q set k = 2;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("VALUES (1, 'x'),\n       (2, 'y');", result.Text, StringComparison.Ordinal);
        Assert.Contains("MERGE INTO dbo.t AS t\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("DELETE TOP (10) dbo.q\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("UPDATE TOP (5) dbo.q\n", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" \n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_KeepsTrailingSpacesInsideMultiLineStrings()
    {
        const string text = "select 'a  \nb' as v from dbo.t;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("'a  \nb'", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("update dbo.t set a = 1, b = (select max(x) from dbo.x where x.id = t.id) where c in (select c from dbo.z);")]
    [InlineData("delete from dbo.t where id in (select id from dbo.x where k in (select k from dbo.y));")]
    [InlineData("insert into dbo.t (a) select a from dbo.x x join dbo.y y on y.id = x.id where exists (select 1 from dbo.z);")]
    [InlineData("insert into dbo.t (a, b) values (1, 2), (3, 4);")]
    [InlineData("with c as (select id from dbo.t where k in (select k from dbo.z)) select * from c join dbo.u u on u.id = c.id;")]
    [InlineData("merge dbo.t as t using (select id, v from dbo.s) as s on t.id = s.id when matched then update set t.v = s.v when not matched then insert (id, v) values (s.id, s.v);")]
    [InlineData("create procedure dbo.p @id int as begin if exists (select 1 from dbo.t where id = @id) begin update dbo.t set v = 1 where id = @id; end else begin insert into dbo.t (id) values (@id); end end")]
    [InlineData("select case when a in (select a from dbo.z) then 1 else 0 end as f from dbo.t; exec dbo.p @id = 1;")]
    [InlineData("delete top (10) from dbo.q where k = 1; update top (5) dbo.q set k = 2;")]
    public void Format_ProducesParseableIdempotentOutputForEveryStatementKind(string text)
    {
        var first = SqlFormatter.Format(text);
        var second = SqlFormatter.Format(first.Text);
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(first.Text);
        parser.Parse(reader, out IList<ParseError> errors);

        Assert.True(first.IsSuccess);
        Assert.Empty(errors);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Format_IndentsProcedureParametersOnePerLine()
    {
        const string text = "create procedure dbo.p @a int, @b varchar(10)='x', @c decimal(18, 2) output as select 1 as one;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "CREATE PROCEDURE dbo.p\n    @a INT,\n    @b VARCHAR(10) = 'x',\n    @c DECIMAL(18, 2) OUTPUT\nAS\n",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_StartsTheSelectListOnTheNextLineWhenThereAreSeveralElements()
    {
        const string text = "select top (10) a, b from dbo.t; select distinct a, b from dbo.t; select a from dbo.t; select @x = 1, @y = 2;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("SELECT TOP (10)\n    a,\n    b\nFROM dbo.t;", result.Text, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT\n    a,\n    b\nFROM dbo.t;", result.Text, StringComparison.Ordinal);
        Assert.Contains("SELECT a\nFROM dbo.t;", result.Text, StringComparison.Ordinal);
        Assert.Contains("SELECT\n    @x = 1,\n    @y = 2;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_KeepsASingleWherePredicateOnTheWhereLine()
    {
        const string text = "select a from dbo.t where a = 1; delete from dbo.t where id in (select id from dbo.x);";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("FROM dbo.t\nWHERE a = 1;", result.Text, StringComparison.Ordinal);
        Assert.Contains("WHERE id IN\n      (\n          SELECT id\n          FROM dbo.x\n      );", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_KeepsASingleSelectElementBesideSelect()
    {
        const string text = "select top (1)\n    a\nfrom dbo.t;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("SELECT TOP (1) a", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_MovesNestedClausesTogetherWithTheirParentQuery()
    {
        const string text = "select 1 from dbo.t where a in (select b, c from dbo.x where d = 1 and e = 2) and f = 3;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "WHERE\n    a IN\n    (\n        SELECT\n            b,\n            c\n        FROM dbo.x\n        WHERE\n            d = 1 AND\n            e = 2\n    ) AND\n    f = 3;",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotShiftTextInsideMultiLineStringsWhenClausesMove()
    {
        const string text = "select a, 'x\n    WHERE y\n      AND z' as v from dbo.t where a = 1 and b = 2;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("'x\n    WHERE y\n      AND z'", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PutsSpacesAroundTheEqualsSignOfParameterDefaults()
    {
        const string text = "CREATE PROCEDURE [dbo].[GetOrders]\n@customerId dt_id=NULL\nAS\nSELECT 1;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains(
            "CREATE PROCEDURE [dbo].[GetOrders]\n    @customerId dt_id = NULL\nAS\n",
            result.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_LinesUpListElementsAndCommentsWhereverTheGeneratorPutsThem()
    {
        const string text = "select a, -- first\n b from dbo.t where x = 1 and -- second\n y = 2;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);

        // The generator keeps a trailing comment on its line or moves it to its own line, depending on the
        // ScriptDom build; either way the elements and predicates share one indentation.
        Assert.Matches(@"SELECT\n    a,( -- first\n|\n    -- first\n)    b\nFROM dbo\.t", result.Text);
        Assert.Matches(@"WHERE\n    x = 1 AND( -- second\n    |\n    -- second\n)\s*y = 2;", result.Text);
    }

    [Fact]
    public void Format_StartsWherePredicatesBelowWhereWhenTheFirstOneIsAnExistsTest()
    {
        const string text = "select 1 from dbo.t where exists (select 1 from dbo.u) and a = 1;";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("WHERE\n    EXISTS\n    (\n        SELECT 1\n        FROM dbo.u\n    ) AND\n    a = 1;", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("select a, -- first\n b from dbo.t where x = 1 and -- second\n y = 2;")]
    [InlineData("select /* c */ a, b from dbo.t;")]
    [InlineData("select a from dbo.t where /* c */ x = 1 and y = 2;")]
    [InlineData("select * from dbo.t join dbo.u /* c */ on t.id = u.id;")]
    [InlineData("insert into dbo.t (a) values (1), /* c */ (2);")]
    [InlineData("select 'a' as x, N'it''s (select 1)' as y from dbo.t where z = '--' and w = ']';")]
    public void Format_KeepsCommentsAndLiteralsValidAndIdempotent(string text)
    {
        var first = SqlFormatter.Format(text);
        var second = SqlFormatter.Format(first.Text);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
    }

    private const string ProcedureWithBlankLines =
        "CREATE PROCEDURE dbo.GetOrders AS\nSET NOCOUNT ON;\n\n\nDECLARE @x INT = 0;\nDECLARE @y INT;\n\n"
        + "-- load the orders\nSELECT a, b FROM dbo.t WHERE a = 1;\nIF @x > 0\nBEGIN\n"
        + "    UPDATE dbo.t SET a = 0 WHERE b = 1;\n    DELETE FROM dbo.t WHERE a < 0;\nEND\nSELECT 1;\n";

    [Fact]
    public void Format_KeepsSourceBlankLinesAndCollapsesRepeatedOnes()
    {
        var result = SqlFormatter.Format(ProcedureWithBlankLines);

        Assert.True(result.IsSuccess);
        Assert.Contains("AS\n\nSET NOCOUNT ON;\n\nDECLARE @x AS INT = 0;\nDECLARE @y AS INT;\n\n-- load the orders\nSELECT", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_SeparatesMultiLineStatementsFromTheirNeighboursWithABlankLine()
    {
        var result = SqlFormatter.Format(ProcedureWithBlankLines);

        Assert.True(result.IsSuccess);
        Assert.Contains("WHERE a = 1;\n\nIF @x > 0\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("WHERE b = 1;\n\n        DELETE", result.Text, StringComparison.Ordinal);
        Assert.Contains("    END\n\nSELECT 1;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_KeepsAShortRunOfSingleLineStatementsTogether()
    {
        const string text = "CREATE PROCEDURE dbo.P AS\nSET NOCOUNT ON;\nDECLARE @a INT;\nDECLARE @b INT;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("AS\n\nSET NOCOUNT ON;\nDECLARE @a AS INT;\nDECLARE @b AS INT;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PutsABlankLineBeforeProcedureNoCountStatement()
    {
        const string text = "CREATE PROCEDURE [dbo].[ExampleProcedure]\n"
            + "    @prCode dt_prCode = NULL\n"
            + "AS\n"
            + "SET NOCOUNT ON;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("AS\n\nSET NOCOUNT ON;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PreservesWindowsLineEndings()
    {
        const string text = "SELECT a\r\nFROM dbo.ExampleTable\r\nWHERE a = 1;\r\n";

        var result = SqlFormatter.Format(text);
        var normalized = result.Text.Replace("\r\n", "", StringComparison.Ordinal);

        Assert.True(result.IsSuccess);
        Assert.Contains("\r\n", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_KeepsABlankLineAfterAsWhenTheSourceHasOne()
    {
        const string text = "CREATE PROCEDURE dbo.P AS\n\nSET NOCOUNT ON;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("AS\n\nSET NOCOUNT ON;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_PutsTheSemicolonBackWhenTheGeneratorMovedItIntoAFollowingComment()
    {
        const string text = "DECLARE @y INT;\n\n-- load the orders\nSELECT 1;\n";

        var result = SqlFormatter.Format(text);

        Assert.True(result.IsSuccess);
        Assert.Contains("DECLARE @y AS INT;\n\n-- load the orders\nSELECT 1;", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("orders;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_BlankLineLayoutIsIdempotent()
    {
        var first = SqlFormatter.Format(ProcedureWithBlankLines);
        var second = SqlFormatter.Format(first.Text);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Text, second.Text);
    }
}
