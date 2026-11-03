CREATE TABLE dbo.MetadataProbe
(
    id INT IDENTITY(1, 1) NOT NULL,
    amount DECIMAL(10, 2) NULL,
    label VARCHAR(20) COLLATE Latin1_General_100_CI_AS NOT NULL DEFAULT 'new',
    computed_value AS id + 1
);
GO
ALTER TABLE dbo.MetadataProbe ALTER COLUMN amount DECIMAL(12, 4) NOT NULL;
