# MS SQL Server İndeksleme, Execution Plan Analizi ve Sorgu Optimizasyonu Notları

Bu doküman, projedeki `OrderDb` ve `CatalogDb` veritabanlarında uygulanan T-SQL optimizasyon tekniklerini ve `docs/sql/03_optimizasyon_ornekleri.sql` çıktısını özetler.

## 1. Covering Index ile Key Lookup Eliminasyonu

`Order.Api` içinde müşteri siparişleri şu sorguyla listelenir:

```sql
SELECT TOP (50) Id, CustomerId, CreatedAt, Status, TotalAmount, CancelReason, PaymentId
FROM dbo.Orders
WHERE CustomerId = @cid
ORDER BY CreatedAt DESC;
```

* **Yalnızca `(CustomerId, CreatedAt DESC)` indeksi olsaydı:** SQL Server her eşleşen satır için `Status`, `TotalAmount`, `CancelReason` ve `PaymentId` sütunlarını okumak üzere Clustered Index'e geri dönecekti (**Key Lookup** + Nested Loops). 200 siparişli bir müşteride bu ~600 ek sayfa okuması (logical reads) demektir.
* **Çözüm (`IX_Orders_CustomerId_CreatedAt_Covering`):**
  ```sql
  CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_CreatedAt_Covering]
  ON [dbo].[Orders] ([CustomerId] ASC, [CreatedAt] DESC)
  INCLUDE ([Status], [TotalAmount], [CancelReason], [PaymentId]);
  ```
  Yaprak (leaf) seviyesine `INCLUDE` edilen sütunlar sayesinde Execution Plan'da **yalnızca tek bir `Index Seek`** operatörü yer alır ve `CreatedAt DESC` sıralaması indeks sırasıyla birebir örtüştüğü için ek bir **`Sort` operatörü oluşmaz**.

## 2. Transactional Outbox için Filtered Index

`OutboxMessages` tablosunda zamanla yüz binlerce işlenmiş (`ProcessedAt IS NOT NULL`) kayıt birikirken, `OutboxPublisher` her 500 ms'de yalnızca işlenmemiş (`ProcessedAt IS NULL`) kayıtları sorgular:

```sql
CREATE NONCLUSTERED INDEX [IX_OutboxMessages_Unprocessed]
ON [dbo].[OutboxMessages] ([OccurredAt] ASC)
WHERE [ProcessedAt] IS NULL;
```

* İşlenen satırlar indeksten otomatik olarak düşer.
* Tablo 1 milyon satıra ulaşsa bile indeks yalnızca o an kuyrukta bekleyen birkaç satırı tutar (**logical reads = 2–3 sayfa**).

## 3. SARGable (Search Argument Able) Sorgu Yazımı

| Yaklaşım | Sorgu Filtresi | Execution Plan Operatörü | 10.000 Satırda Logical Reads |
|---|---|---|---|
| **Kötü (Non-SARGable)** | `YEAR(CreatedAt) = 2026 AND MONTH(CreatedAt) = 11` | `Index Scan` (tüm satırlar taranır) | ~95 sayfa |
| **İyi (SARGable)** | `CreatedAt >= '2026-11-01' AND CreatedAt < '2026-12-01'` | `Index Seek` (yalnızca aralık okunur) | ~4 sayfa |

## 4. CTE ve Window Functions (`sp_GunlukSatisRaporu`)

`OrderReportQueryService` Dapper üzerinden `dbo.sp_GunlukSatisRaporu` saklı yordamını çağırır:

* `SUM(g.TotalAmount) OVER (ORDER BY g.Day ROWS UNBOUNDED PRECEDING)` ile kendini tekrar eden alt sorgular (self-join / correlated subquery) olmadan **tek geçişte (single scan)** kümülatif ciro hesaplanır.
* `DENSE_RANK() OVER (ORDER BY g.TotalAmount DESC)` ile en yüksek cirolu günler sıralanır.

