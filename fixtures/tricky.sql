CREATE PROCEDURE [dbo].[sp_Tricky]
AS
BEGIN
    -- temp tables and CTEs are not dependencies
    SELECT id INTO #work FROM upInfo;

    WITH recent AS (SELECT id FROM dbo.PriceLog)
    SELECT r.id FROM recent r JOIN #work w ON w.id = r.id;

    SELECT * INTO dbo.PriceCopy FROM dbo.upInfo;
    TRUNCATE TABLE dbo.PriceLog;

    MERGE dbo.upInfo AS t
    USING dbo.PriceCopy AS s ON s.id = t.id
    WHEN MATCHED THEN UPDATE SET price = s.price
    WHEN NOT MATCHED THEN INSERT (id, price) VALUES (s.id, s.price)
    WHEN NOT MATCHED BY SOURCE THEN DELETE;

    EXEC sp_Notify;
    EXEC ('SELECT 1');
    EXEC sp_executesql N'SELECT 2';
    SELECT a.id FROM Archive.dbo.upInfo a;

    UPDATE dbo.upInfo SET price = 0 WHERE id = 1;
END
GO 2
CREATE TRIGGER dbo.trg_upInfo ON dbo.upInfo AFTER UPDATE
AS
BEGIN
    INSERT INTO dbo.PriceLog (id) SELECT id FROM inserted;
END
GO
