namespace BuildingBlocks.Contracts;

/// <summary>
/// Servisler arasında RabbitMQ üzerinden taşınan tüm mesajların ortak tabanı.
/// Routing key olarak mesaj tipinin adı kullanılır (ör. "ReserveStock").
/// </summary>
public interface IIntegrationMessage;

public sealed record OrderLine(int ProductId, int Quantity);

// ---------- Komutlar (Saga orkestratörü → katılımcılar) ----------

/// <summary>Order → Catalog: Sipariş kalemleri için stok ayır.</summary>
public sealed record ReserveStock(Guid OrderId, IReadOnlyList<OrderLine> Items) : IIntegrationMessage;

/// <summary>Order → Catalog: Telafi (compensation) adımı, ayrılan stoğu geri bırak.</summary>
public sealed record ReleaseStock(Guid OrderId, string Reason) : IIntegrationMessage;

/// <summary>Order → Payment: Sipariş için ödeme niyeti (intent) oluştur.</summary>
public sealed record RequestPayment(Guid OrderId, string CustomerId, decimal Amount) : IIntegrationMessage;

/// <summary>Order → Payment: Sipariş iptal edildikten sonra gelen başarılı ödemeyi iade et (ISO 8583 0400 reversal).</summary>
public sealed record RefundPayment(Guid OrderId, Guid PaymentId, string Reason) : IIntegrationMessage;

// ---------- Olaylar (katılımcılar → Saga orkestratörü) ----------

public sealed record StockReserved(Guid OrderId) : IIntegrationMessage;

public sealed record StockReservationFailed(Guid OrderId, string Reason) : IIntegrationMessage;

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, decimal Amount, string AuthCode) : IIntegrationMessage;

public sealed record PaymentFailed(Guid OrderId, Guid PaymentId, string Reason, string? ResponseCode) : IIntegrationMessage;

// ---------- Sipariş sonuç olayları (Notification ve Payment dinler) ----------

public sealed record OrderConfirmed(Guid OrderId, string CustomerId, decimal TotalAmount) : IIntegrationMessage;

public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason) : IIntegrationMessage;

