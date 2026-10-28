CREATE PROCEDURE dbo.sp_Broken
AS
BEGIN
    SELEC nothing FROM dbo.upInfo
END
GO
CREATE PROCEDURE dbo.sp_AfterBroken
AS
BEGIN
    SELECT id FROM dbo.upInfo;
END
GO
