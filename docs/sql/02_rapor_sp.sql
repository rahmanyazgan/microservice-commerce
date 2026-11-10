/* ============================================================================
   02_rapor_sp.sql — CTE ve Window Function Kullanan Günlük Satış Raporu SP
   ============================================================================ */

USE [ETicaret_OrderDb];
GO

CREATE OR ALTER PROCEDURE dbo.sp_GunlukSatisRaporu
    @GunSayisi INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    -- SARGable tarih filtresi: sütun üzerinde fonksiyon çağırmak yerine başlangıç sınırını hesapla
    DECLARE @BaslangicTarihi DATETIME2 = DATEADD(DAY, -@GunSayisi, SYSUTCDATETIME());

    ;WITH GunlukOzet AS (
        SELECT
            CONVERT(char(10), o.CreatedAt, 23) AS Day,
            COUNT(1)                           AS OrderCount,
            SUM(o.TotalAmount)                 AS TotalAmount
        FROM dbo.Orders AS o
        WHERE o.Status = N'Confirmed'
          AND o.CreatedAt >= @BaslangicTarihi
        GROUP BY CONVERT(char(10), o.CreatedAt, 23)
    )
    SELECT
        g.Day,
        g.OrderCount,
        g.TotalAmount,
        -- Window Function 1: Başlangıçtan itibaren kümülatif (running) ciro
        SUM(g.TotalAmount) OVER (ORDER BY g.Day ROWS UNBOUNDED PRECEDING) AS RunningTotal,
        -- Window Function 2: En yüksek cirolu günden başlayan sıralama
        DENSE_RANK()       OVER (ORDER BY g.TotalAmount DESC)             AS RankByAmount
    FROM GunlukOzet AS g
    ORDER BY g.Day DESC;
END
GO

