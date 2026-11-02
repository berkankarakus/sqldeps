using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;
using SqlDeps.Storage;

namespace SqlDeps.Tests;

public sealed class IndexDatabaseTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sqldeps-tests-" + Guid.NewGuid());

    public IndexDatabaseTests()
    {
        Directory.CreateDirectory(_folder);
    }

    [Fact]
    public void GetStatus_ChangedFile_ReportsExactlyOneChangedFile()
    {
        const string relativePath = "procedure.sql";
        var sourcePath = Path.Combine(_folder, relativePath);
        File.WriteAllText(sourcePath, "SELECT 1;");
        var originalHash = SqlFileReader.HashOf(File.ReadAllBytes(sourcePath));
        var databasePath = Path.Combine(_folder, "index.sqlite");
        var settings = IndexCache.SettingsFor(ObjectNames.StandardDefaultSchema);
        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [new IndexedFile(relativePath, originalHash)],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: settings,
            defaultSchema: ObjectNames.StandardDefaultSchema,
            sourceRoot: _folder);

        File.WriteAllText(sourcePath, "SELECT 2;");

        var status = new IndexDatabase(databasePath).GetStatus(_folder);

        Assert.Equal(1, status.SqlFiles);
        Assert.Equal(1, status.IndexedFiles);
        Assert.Equal(0, status.Added);
        Assert.Equal(1, status.Changed);
        Assert.Equal(0, status.Removed);
        Assert.False(status.IsCurrent);
    }

    [Fact]
    public void QualifyObjectName_OnePartName_UsesIndexedDefaultSchema()
    {
        var databasePath = Path.Combine(_folder, "index.sqlite");
        SqliteStore.Write(
            databasePath,
            objects: [],
            dependencies: [],
            parameters: [],
            resultColumns: [],
            tableKeys: [],
            foreignKeys: [],
            indexColumns: [],
            columnUsage: [],
            tableColumns: [],
            problems: [],
            warnings: [],
            lintFindings: [],
            files: [],
            reachability: ReachabilityBuilder.Build([], 12),
            settings: "rules=5;default_schema=app",
            defaultSchema: "app",
            sourceRoot: _folder);

        var database = new IndexDatabase(databasePath);

        Assert.Equal("app.Orders", database.QualifyObjectName("Orders"));
        Assert.Equal("audit.Orders", database.QualifyObjectName("audit.Orders"));
    }

    [Fact]
    public void Index_ComputedTableColumn_IsRecordedWithoutStoppingTheFile()
    {
        File.WriteAllText(Path.Combine(_folder, "computed.sql"), """
            CREATE TABLE dbo.OrderLine
            (
                Quantity int NOT NULL,
                Total AS Quantity * 2
            );
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Collection(
            indexer.TableColumns.OrderBy(column => column.ColumnName),
            quantity =>
            {
                Assert.Equal("Quantity", quantity.ColumnName);
                Assert.Equal("int", quantity.DataType, ignoreCase: true);
            },
            total =>
            {
                Assert.Equal("Total", total.ColumnName);
                Assert.Equal("computed", total.DataType);
            }
        );
    }

    [Fact]
    public void Index_AlterTableAdd_RecordsTheAddedColumn()
    {
        File.WriteAllText(Path.Combine(_folder, "alter.sql"), "ALTER TABLE dbo.OrderLine ADD Discount decimal(10, 2) NULL;");
        File.WriteAllText(Path.Combine(_folder, "repeat-alter.sql"), "ALTER TABLE dbo.OrderLine ADD Discount decimal(10, 2) NULL;");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.Equal("dbo.OrderLine", column.TableName);
        Assert.Equal("Discount", column.ColumnName);
        Assert.Equal("decimal (10, 2)", column.DataType, ignoreCase: true);
    }

    [Fact]
    public void Index_AlterTableAlterColumn_RecordsTheColumnType()
    {
        File.WriteAllText(Path.Combine(_folder, "alter-column.sql"), "ALTER TABLE dbo.OrderLine ALTER COLUMN Discount decimal(12, 4) NOT NULL;");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var column = Assert.Single(indexer.TableColumns);
        Assert.Equal("dbo.OrderLine", column.TableName);
        Assert.Equal("Discount", column.ColumnName);
        Assert.Equal("decimal (12, 4)", column.DataType, ignoreCase: true);
    }

    [Fact]
    public void Index_ProcedureTopLevelSelect_RecordsResultColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "results.sql"), "CREATE PROCEDURE dbo.GetOrders AS SELECT id, name AS display_name FROM dbo.Orders;");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Collection(
            indexer.ResultColumns.OrderBy(column => column.Ordinal),
            id =>
            {
                Assert.Equal("dbo.GetOrders", id.ObjectName);
                Assert.Equal("id", id.ColumnName);
                Assert.Equal(0, id.Ordinal);
            },
            displayName => Assert.Equal("display_name", displayName.ColumnName));
    }

    [Fact]
    public void Index_ClrProcedureWithoutStatementList_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_folder, "clr-procedure.sql"), "CREATE PROCEDURE dbo.ManagedProc AS EXTERNAL NAME [MyAssembly].[MyType].[Run];");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Contains(indexer.Objects, item => item.Name == "dbo.ManagedProc" && item.Kind == "procedure");
        Assert.Empty(indexer.ResultColumns);
    }

    [Fact]
    public void Index_ProcedureUnion_UsesFirstBranchResultNames()
    {
        File.WriteAllText(Path.Combine(_folder, "union-results.sql"), "CREATE PROCEDURE dbo.GetIds AS SELECT id AS item_id FROM dbo.Orders UNION SELECT id FROM dbo.ArchivedOrders;");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var column = Assert.Single(indexer.ResultColumns);
        Assert.Equal("item_id", column.ColumnName);
        Assert.Equal(0, column.Ordinal);
    }

    [Fact]
    public void Index_TableConstraints_RecordsPrimaryAndUniqueColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "keys.sql"), "CREATE TABLE dbo.Orders (id INT NOT NULL, code INT NOT NULL, CONSTRAINT PK_Orders PRIMARY KEY (id), CONSTRAINT UQ_Orders_Code UNIQUE (code));");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Contains(indexer.TableKeys, key => key.Kind == "primary-key" && key.ColumnName == "id");
        Assert.Contains(indexer.TableKeys, key => key.Kind == "unique" && key.ColumnName == "code");
    }

    [Fact]
    public void Index_TableConstraints_RecordsForeignKeyTargets()
    {
        File.WriteAllText(Path.Combine(_folder, "foreign-key.sql"), "CREATE TABLE dbo.OrderLine (order_id INT, CONSTRAINT FK_OrderLine_Order FOREIGN KEY (order_id) REFERENCES dbo.Orders (id));");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var foreignKey = Assert.Single(indexer.ForeignKeys);
        Assert.Equal("dbo.OrderLine", foreignKey.TableName);
        Assert.Equal("order_id", foreignKey.ColumnName);
        Assert.Equal("dbo.Orders", foreignKey.ReferencedTableName);
        Assert.Equal("id", foreignKey.ReferencedColumnName);
    }

    [Fact]
    public void Index_CreateIndex_RecordsKeyAndIncludedColumns()
    {
        File.WriteAllText(Path.Combine(_folder, "index.sql"), "CREATE NONCLUSTERED INDEX IX_Orders_Code ON dbo.Orders (code DESC) INCLUDE (id);");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Contains(indexer.IndexColumns, column => column.IndexName == "IX_Orders_Code" && column.ColumnName == "code" && column.IsDescending);
        Assert.Contains(indexer.IndexColumns, column => column.IndexName == "IX_Orders_Code" && column.ColumnName == "id" && column.IsIncluded);
    }

    [Fact]
    public void Index_AlterTableConstraint_RecordsForeignKey()
    {
        File.WriteAllText(Path.Combine(_folder, "alter-constraint.sql"), "ALTER TABLE dbo.OrderLine ADD CONSTRAINT FK_OrderLine_Order FOREIGN KEY (order_id) REFERENCES dbo.Orders (id);");
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Contains(indexer.ForeignKeys, key => key.TableName == "dbo.OrderLine" && key.ReferencedTableName == "dbo.Orders");
    }

    [Fact]
    public void Index_RecordsTsqlLintFindings()
    {
        File.WriteAllText(Path.Combine(_folder, "lint.sql"), """
            SELECT * FROM dbo.Orders WITH (NOLOCK);
            SELECT Id FROM dbo.Orders WITH (READUNCOMMITTED);
            UPDATE dbo.Orders SET Status = 'Archived';
            DELETE FROM dbo.Orders;
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET Status = 'Archived';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "select-star" && finding.Severity == "info");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "unfiltered-select" && finding.Severity == "info");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "nolock" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "read-uncommitted" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "update-without-where" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "delete-without-where" && finding.Severity == "warning");
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "merge" && finding.Severity == "warning");
        Assert.All(indexer.LintFindings, finding =>
        {
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }

    [Fact]
    public void Index_GroupedAggregate_DoesNotReportAggregateArgumentAsUngrouped()
    {
        File.WriteAllText(Path.Combine(_folder, "grouped-aggregate.sql"), """
            CREATE FUNCTION dbo.GetInvalidClicks()
            RETURNS TABLE
            AS
            RETURN
            (
                SELECT clicks.AtDate, SUM(clicks.InvalidClick) AS InvalidClicks
                FROM dbo.Clicks clicks
                GROUP BY clicks.AtDate
            );
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "ungrouped-select-column");
    }

    [Fact]
    public void Index_NestedQueryScopesAllowTheSameTableAlias()
    {
        File.WriteAllText(Path.Combine(_folder, "nested-alias.sql"), """
            CREATE PROCEDURE dbo.GetItems
            AS
            SELECT outerInfo.id
            FROM (SELECT innerInfo.id FROM dbo.Info innerInfo) outerInfo
            JOIN dbo.OtherInfo innerInfo ON innerInfo.id = outerInfo.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "duplicate-alias");
    }

    [Fact]
    public void Index_CrossApplyFunctionAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "cross-apply.sql"), """
            CREATE PROCEDURE dbo.GetSplitItems
            AS
            SELECT splitItem.value
            FROM dbo.Info info
            CROSS APPLY dbo.SplitValues(info.Tags) splitItem;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_InlineValuesAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "inline-values.sql"), """
            CREATE PROCEDURE dbo.GetValues
            AS
            SELECT valuesRow.itemID
            FROM (VALUES (1), (2)) valuesRow(itemID);
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_ContainsTableAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "contains-table.sql"), """
            CREATE PROCEDURE dbo.GetFullTextItems
            AS
            SELECT fullText.[KEY]
            FROM CONTAINSTABLE(dbo.Info, *, 'value') fullText;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_SelectStarFromTemporaryTable_UsesItsKnownColumnCountInUnion()
    {
        File.WriteAllText(Path.Combine(_folder, "temporary-union.sql"), """
            CREATE PROCEDURE dbo.GetTemporaryItems
            AS
            SELECT id, title, status
            INTO #items
            FROM dbo.Info;

            SELECT * FROM #items
            UNION ALL
            SELECT id, title, status FROM dbo.Info;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "set-operation-column-count");
    }

    [Fact]
    public void Index_DmlIneffectiveFilter_SuppressesUnstableConditions()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-filters.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND @enabled = 1;
            DELETE FROM dbo.Orders WHERE 1 = 1 AND GETDATE() = GETDATE();
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND id = 1;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var findings = indexer.LintFindings.Where(finding => finding.Rule == "dml-ineffective-filter").ToArray();
        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void Index_DmlConditionFindings_CoverUpdateDeleteAndMerge()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-condition-families.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND id = 1;
            DELETE FROM dbo.Orders WHERE 1 = 1 AND id = 1;
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.id = source.id
            WHEN MATCHED THEN UPDATE SET status = 1;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var ineffective = indexer.LintFindings
            .Where(finding => finding.Rule == "dml-ineffective-filter")
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 2], ineffective.Select(finding => finding.Line).ToArray());
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "merge" && finding.Line == 3);
        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule.StartsWith("dml-", StringComparison.Ordinal) && finding.Line >= 3);
    }

    [Fact]
    public void Index_LocalDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "local-dml.sql"), """
            CREATE PROCEDURE dbo.ClearLocalRows
            AS
            BEGIN
                DECLARE @rows TABLE (id INT);
                CREATE TABLE #rows (id INT);
                UPDATE @rows SET id = 1;
                DELETE FROM @rows;
                UPDATE rowsAlias
                SET id = 1
                FROM @rows AS rowsAlias;
                DELETE rowsAlias
                FROM @rows AS rowsAlias;
                UPDATE #rows SET id = 1;
                DELETE FROM #rows;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_AliasedLocalDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "aliased-local-dml.sql"), """
            CREATE PROCEDURE dbo.ClearAliasedLocalRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                UPDATE rowsAlias
                SET id = 1
                FROM #rows AS rowsAlias;
                DELETE rowsAlias
                FROM #rows AS rowsAlias;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_TransactionInsideTryCatchDoesNotReportMissingErrorHandling()
    {
        File.WriteAllText(Path.Combine(_folder, "transaction-try-catch.sql"), """
            CREATE PROCEDURE dbo.SafeTransaction
            AS
            BEGIN TRY
                BEGIN TRANSACTION;
                UPDATE dbo.Orders SET status = 1 WHERE id = 1;
                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "transaction-without-error-handling");
    }

    [Fact]
    public void Index_TransactionImmediatelyBeforeTryCatchWithRollbackDoesNotReportMissingErrorHandling()
    {
        File.WriteAllText(Path.Combine(_folder, "transaction-before-try-catch.sql"), """
            CREATE PROCEDURE dbo.SafeTransactionWithOuterBegin
            AS
            BEGIN
                BEGIN TRANSACTION;
                BEGIN TRY
                    UPDATE dbo.Orders SET status = 1 WHERE id = 1;
                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "transaction-without-error-handling");
    }

    [Fact]
    public void Index_DmlJoinPredicateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-join-filter.sql"), """
            UPDATE target
            SET status = 1
            FROM dbo.Orders target
            JOIN dbo.OrderKeys keys ON keys.id = target.id;

            DELETE target
            FROM dbo.Orders target
            JOIN dbo.OrderKeys keys ON keys.id = target.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_LimitedDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "limited-dml.sql"), """
            UPDATE TOP (10) dbo.Orders SET status = 1;
            DELETE TOP (10) FROM dbo.Orders;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_TopLimitedCteDeleteDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "top-limited-cte-delete.sql"), """
            CREATE PROCEDURE dbo.TrimLocalRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                WITH limited AS
                (
                    SELECT TOP (1) id
                    FROM #rows
                    ORDER BY id
                )
                DELETE FROM limited;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "delete-without-where");
    }

    [Fact]
    public void Index_FilteredCteUpdateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "filtered-cte-update.sql"), """
            CREATE PROCEDURE dbo.UpdateFilteredRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                WITH filtered AS
                (
                    SELECT id
                    FROM #rows
                    WHERE id > 0
                )
                UPDATE filtered SET id = id + 1;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "update-without-where");
    }

    [Fact]
    public void Index_RestrictedDerivedUpdateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "restricted-derived-update.sql"), """
            CREATE PROCEDURE dbo.UpdateOneRow
            AS
            BEGIN
                UPDATE selected
                SET status = 1
                FROM
                (
                    SELECT TOP (1) id, status
                    FROM dbo.Orders
                    WHERE status = 0
                    ORDER BY id
                ) AS selected;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "update-without-where");
    }

    [Fact]
    public void Index_NestedDerivedAliasDoesNotShadowDmlTarget()
    {
        File.WriteAllText(Path.Combine(_folder, "nested-derived-alias.sql"), """
            UPDATE target
            SET status = 1
            FROM dbo.Orders AS target
            JOIN
            (
                SELECT id
                FROM
                (
                    SELECT id
                    FROM dbo.OtherOrders AS target
                ) AS target
            ) AS source ON source.id = target.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "dml-derived-target");
    }

    [Fact]
    public void Index_MergeTopSourceRequiresAnOrderBy()
    {
        File.WriteAllText(Path.Combine(_folder, "merge-top-order.sql"), """
            MERGE dbo.Orders AS target
            USING (SELECT TOP (1) id FROM dbo.NewOrders) AS source
            ON target.id = source.id
            WHEN MATCHED THEN UPDATE SET status = 1;

            MERGE dbo.Orders AS orderedTarget
            USING (SELECT TOP (1) id FROM dbo.NewOrders ORDER BY id) AS orderedSource
            ON orderedTarget.id = orderedSource.id
            WHEN MATCHED THEN UPDATE SET status = 1;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var findings = indexer.LintFindings.Where(finding => finding.Rule == "merge-top-without-order").ToArray();
        Assert.Single(findings);
        Assert.Equal(2, findings[0].Line);
    }

    [Fact]
    public void Index_InsertOnlyMergeWithConstantFalseMatchDoesNotReportNondeterministicMatch()
    {
        File.WriteAllText(Path.Combine(_folder, "merge-insert-only.sql"), """
            MERGE dbo.Orders AS target
            USING dbo.NewOrders AS source
            ON 1 = 0
            WHEN NOT MATCHED THEN INSERT (id) VALUES (source.id);
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge-nondeterministic-match");
    }

    [Fact]
    public void Index_DmlConstantCorpus_KeepsOnlyCollationIndependentStringComparisons()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-constant-corpus.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE '' = '';
            DELETE FROM dbo.Orders WHERE 'a' = 'A';
            UPDATE dbo.Orders SET status = 1 WHERE 'same' <> 'same';
            DELETE FROM dbo.Orders WHERE 1.25 < 1.25;
            UPDATE dbo.Orders SET status = 1 WHERE 1.25 >= 1.25;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule.StartsWith("dml-", StringComparison.Ordinal))
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 3, 4, 5], findings.Select(finding => finding.Line).ToArray());
        Assert.Equal(
            ["dml-ineffective-filter", "dml-always-false-filter", "dml-always-false-filter", "dml-ineffective-filter"],
            findings.Select(finding => finding.Rule).ToArray());
        Assert.All(findings, finding =>
        {
            Assert.Equal("info", finding.Severity);
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }

    [Fact]
    public void Index_FilteredIndex_PreservesPredicateAndColumnCollationMetadata()
    {
        File.WriteAllText(Path.Combine(_folder, "filtered-index.sql"), """
            CREATE TABLE dbo.FilteredText (id INT, status NVARCHAR(20) COLLATE Latin1_General_100_CI_AS);
            CREATE INDEX IX_FilteredText_Id ON dbo.FilteredText (id) WHERE status = N'active';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var index = Assert.Single(indexer.IndexColumns);
        Assert.Equal("status = N'active'", index.FilterPredicate, ignoreCase: true);
        var column = Assert.Single(indexer.TableColumns, item => item.ColumnName == "status");
        Assert.Equal("nvarchar (20)", column.DataType, ignoreCase: true);
        Assert.Equal("Latin1_General_100_CI_AS", column.Collation);
    }

    [Fact]
    public void Index_SuppressesOnlyTheRuleOnTheNextLine()
    {
        File.WriteAllText(Path.Combine(_folder, "suppressed.sql"), """
            -- sqldeps: disable-next-line=merge
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET Status = 'Archived';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge");
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

}
