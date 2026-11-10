/* ============================================================================
   01_indeksler.sql — T-SQL İndeks Tasarımı (Covering & Filtered Index)
   Veritabanları: ETicaret_OrderDb, ETicaret_CatalogDb, ETicaret_PaymentDb
   ============================================================================ */

USE [ETicaret_OrderDb];
GO

-- 1. COVERING INDEX: Müşteri siparişlerini tarihe göre ters sıralarken Clustered Index'e
--    geri dönüp (Key Lookup) Status, TotalAmount, CancelReason, PaymentId okumayı engeller.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Orders_CustomerId_CreatedAt_Covering'
      AND object_id = OBJECT_ID(N'dbo.Orders')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_CreatedAt_Covering]
    ON [dbo].[Orders] ([CustomerId] ASC, [CreatedAt] DESC)
    INCLUDE ([Status], [TotalAmount], [CancelReason], [PaymentId]);
END
GO

-- 2. FILTERED INDEX (Transactional Outbox):
--    OutboxMessages tablosunda milyonlarca işlenmiş (ProcessedAt IS NOT NULL) satır birikebilir.
--    OutboxPublisher yalnızca ProcessedAt IS NULL olan az sayıdaki satırı taradığı için
--    filtered index boyutu birkaç KB seviyesinde kalır ve Index Seek ile çalışır.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_OutboxMessages_Unprocessed'
      AND object_id = OBJECT_ID(N'dbo.OutboxMessages')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_OutboxMessages_Unprocessed]
    ON [dbo].[OutboxMessages] ([OccurredAt] ASC)
    WHERE [ProcessedAt] IS NULL;
END
GO

USE [ETicaret_CatalogDb];
GO

-- 3. FILTERED + COVERING INDEX (Ürün Kataloğu):
--    Pasife alınmış (IsActive = 0) ürünleri indeks dışında bırakır;
--    kategori ve isim sıralamasını doğrudan yaprak (leaf) seviyesinden karşılar.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Products_Active_Category_Name'
      AND object_id = OBJECT_ID(N'dbo.Products')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Products_Active_Category_Name]
    ON [dbo].[Products] ([Category] ASC, [Name] ASC)
    INCLUDE ([Price], [Stock], [ImageUrl], [Description])
    WHERE [IsActive] = 1;
END
GO

