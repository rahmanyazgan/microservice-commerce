/* ============================================================================
   03_optimizasyon_ornekleri.sql — Execution Plan ve Sorgu Optimizasyonu Laboratuvarı
   SSMS'te "Include Actual Execution Plan" (Ctrl+M) açıkken çalıştırın.
   ============================================================================ */

USE [ETicaret_OrderDb];
GO

-- ----------------------------------------------------------------------------
-- BÖLÜM 0: Performans farkını net ölçmek için 10.000 satırlık sentetik veri üretimi
-- ----------------------------------------------------------------------------
SET NOCOUNT ON;

IF (SELECT COUNT(1) FROM dbo.Orders) < 10000
BEGIN
    PRINT N'10.000 örnek sipariş satırı üretiliyor (yaklaşık 2 saniye)...';

    ;WITH N1(n) AS (SELECT 1 FROM (VALUES (1),(1),(1),(1),(1),(1),(1),(1),(1),(1)) v(n)),
          N2(n) AS (SELECT 1 FROM N1 a CROSS JOIN N1 b),
          N4(n) AS (SELECT 1 FROM N2 a CROSS JOIN N2 b),
          Nums(r) AS (SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM N4)
    INSERT INTO dbo.Orders (Id, CustomerId, Status, TotalAmount, CancelReason, PaymentId, CreatedAt, UpdatedAt)
    SELECT
        NEWID(),
        CASE WHEN r % 50 = 0 THEN N'demo-musteri' ELSE CONCAT(N'musteri-', r % 500) END,
        CASE WHEN r % 5 = 0 THEN N'Cancelled' ELSE N'Confirmed' END,
        CAST(100 + (r % 2500) AS decimal(18,2)),
        NULL,
        NULL,
        DATEADD(MINUTE, -r, SYSUTCDATETIME()),
        SYSUTCDATETIME()
    FROM Nums;

    PRINT N'10.000 sipariş eklendi.';
END
GO

SET STATISTICS IO, TIME ON;
GO

-- ----------------------------------------------------------------------------
-- ÖRNEK 1: SARGable Olmayan vs. SARGable Tarih Sorgusu
-- ----------------------------------------------------------------------------

-- [KÖTÜ - Non-SARGable]: Sütun üzerinde YEAR()/MONTH() fonksiyonu kullanıldığı için
-- indeks kullanılamaz, tüm indeks/tablo taranır (Index Scan).
SELECT COUNT(1), SUM(TotalAmount)
FROM dbo.Orders
WHERE Status = N'Confirmed'
  AND YEAR(CreatedAt) = YEAR(SYSUTCDATETIME())
  AND MONTH(CreatedAt) = MONTH(SYSUTCDATETIME());

-- [İYİ - SARGable]: Sütun yalnız bırakılır, aralık sınırları sağ tarafta hesaplanır.
-- IX_Orders_Status_CreatedAt üzerinde doğrudan Index Seek yapar.
DECLARE @AyBasi DATETIME2 = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
DECLARE @SonrakiAyBasi DATETIME2 = DATEADD(MONTH, 1, @AyBasi);

SELECT COUNT(1), SUM(TotalAmount)
FROM dbo.Orders
WHERE Status = N'Confirmed'
  AND CreatedAt >= @AyBasi
  AND CreatedAt <  @SonrakiAyBasi;
GO

-- ----------------------------------------------------------------------------
-- ÖRNEK 2: Key Lookup (RID/Clustered Lookup) Maliyeti vs. Covering Index
-- ----------------------------------------------------------------------------

-- IX_Orders_CustomerId_CreatedAt_Covering indeksimiz INCLUDE (Status, TotalAmount, CancelReason, PaymentId)
-- içerdiği için aşağıdaki sorgu Clustered Index'e hiç uğramadan tek bir Index Seek ile tamamlanır:
SELECT TOP (20)
    Id, CustomerId, CreatedAt, Status, TotalAmount, CancelReason, PaymentId
FROM dbo.Orders
WHERE CustomerId = N'demo-musteri'
ORDER BY CreatedAt DESC;
GO

-- ----------------------------------------------------------------------------
-- ÖRNEK 3: Parameter Sniffing ve OPTION (RECOMPILE) / OPTIMIZE FOR UNKNOWN
-- ----------------------------------------------------------------------------
-- Bazı müşterilerin 2 siparişi, bazılarının (kampanya/bayi hesabı) 50.000 siparişi olduğunda
-- ilk derlenen parametreye göre önbelleğe alınan plan diğer uçtaki parametre için kötü çalışabilir.
DECLARE @Musteri NVARCHAR(80) = N'demo-musteri';

SELECT Id, TotalAmount, CreatedAt
FROM dbo.Orders
WHERE CustomerId = @Musteri
ORDER BY CreatedAt DESC
OPTION (RECOMPILE); -- Dağılımı aşırı çarpık (skewed) sorgularda her çalıştırmada güncel istatistikle plan üretir
GO

SET STATISTICS IO, TIME OFF;
GO

