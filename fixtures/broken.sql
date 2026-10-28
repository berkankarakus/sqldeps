CREATE PROCEDURE dbo.sp_Broken
AS
BEGIN
    SELEC nothing FROM dbo.tbl_a
END
GO
CREATE PROCEDURE dbo.sp_AfterBroken
AS
BEGIN
    SELECT id FROM dbo.tbl_a;
END
GO
