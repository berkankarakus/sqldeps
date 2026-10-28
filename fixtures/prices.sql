CREATE TABLE dbo.upInfo (id INT PRIMARY KEY, price DECIMAL(10,2));
GO
CREATE TABLE dbo.PriceLog (id INT, old_price DECIMAL(10,2));
GO
CREATE VIEW dbo.v_ActivePrices AS
SELECT id, price FROM dbo.upInfo WHERE price > 0;
GO
CREATE FUNCTION dbo.fn_Round(@p DECIMAL(10,2)) RETURNS DECIMAL(10,2)
AS
BEGIN
    RETURN ROUND(@p, 0);
END
GO
CREATE PROCEDURE dbo.sp_UpdatePrices
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.PriceLog (id, old_price)
    SELECT id, price FROM dbo.upInfo;

    UPDATE u
    SET price = dbo.fn_Round(u.price)
    FROM dbo.upInfo u
    JOIN dbo.v_ActivePrices v ON v.id = u.id;

    DELETE FROM dbo.PriceLog WHERE id < 0;

    EXEC dbo.sp_Notify;
END
GO
