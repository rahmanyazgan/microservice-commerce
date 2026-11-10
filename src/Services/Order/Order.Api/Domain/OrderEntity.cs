using BuildingBlocks.Contracts;
using BuildingBlocks.Web;

namespace Order.Api.Domain;

public enum OrderStatus
{
    Pending,
    StockReserved,
    Confirmed,
    Cancelled
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public sealed class OrderHistoryEntry
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Sipariş Kök Varlığı (Aggregate Root) ve Saga Durum Makinesi.
/// Durum geçişleri (Pending → StockReserved → Confirmed / Cancelled) ve üretilen entegrasyon mesajları
/// burada kapsüllenir (State / Saga Orchestrator deseni).
/// </summary>
public sealed class OrderEntity
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public string? CancelReason { get; set; }
    public Guid? PaymentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderHistoryEntry> History { get; set; } = [];

    public string StatusText => Status switch
    {
        OrderStatus.Pending => "Stok kontrolü bekleniyor",
        OrderStatus.StockReserved => "Stok ayrıldı — Ödeme bekleniyor",
        OrderStatus.Confirmed => "Sipariş onaylandı",
        OrderStatus.Cancelled => "İptal edildi",
        _ => Status.ToString()
    };

    public static (OrderEntity Order, IReadOnlyList<IIntegrationMessage> OutgoingMessages) Create(
        string customerId,
        IReadOnlyList<(int ProductId, string ProductName, decimal UnitPrice, int Quantity)> items)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new DomainException("Müşteri kimliği zorunludur.");
        if (items.Count == 0)
            throw new DomainException("Sipariş en az bir ürün içermelidir.");

        var order = new OrderEntity
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId.Trim(),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var i in items)
        {
            if (i.Quantity <= 0)
                throw new DomainException("Ürün adedi 1 veya daha büyük olmalıdır.");

            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = decimal.Round(i.UnitPrice, 2),
                Quantity = i.Quantity
            });
        }

        order.TotalAmount = decimal.Round(order.Items.Sum(x => x.UnitPrice * x.Quantity), 2);
        order.AddHistory(OrderStatus.Pending, "Sipariş oluşturuldu; katalog servisinden stok rezervasyonu istendi.");

        var reserveCmd = new ReserveStock(
            order.Id,
            order.Items.Select(x => new OrderLine(x.ProductId, x.Quantity)).ToList());

        return (order, [reserveCmd]);
    }

    public IReadOnlyList<IIntegrationMessage> OnStockReserved()
    {
        if (Status != OrderStatus.Pending)
            return [];

        Status = OrderStatus.StockReserved;
        UpdatedAt = DateTime.UtcNow;
        AddHistory(OrderStatus.StockReserved, "Stok başarıyla ayrıldı; ödeme bekleniyor.");

        return [new RequestPayment(Id, CustomerId, TotalAmount)];
    }

    public IReadOnlyList<IIntegrationMessage> OnStockReservationFailed(string reason)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
            return [];

        Status = OrderStatus.Cancelled;
        CancelReason = reason;
        UpdatedAt = DateTime.UtcNow;
        AddHistory(OrderStatus.Cancelled, $"Stok ayrılamadığı için iptal edildi: {reason}");

        return [new OrderCancelled(Id, CustomerId, reason)];
    }

    public IReadOnlyList<IIntegrationMessage> OnPaymentSucceeded(Guid paymentId, string authCode)
    {
        if (Status == OrderStatus.Confirmed)
            return [];

        // Sipariş zaman aşımından dolayı iptal edilmişken ödeme sonradan gelirse telafi (0400 Reversal/İade) başlat
        if (Status == OrderStatus.Cancelled)
        {
            AddHistory(OrderStatus.Cancelled,
                $"İptal edilmiş siparişe ödeme geldi (OnayKodu={authCode}); otomatik iade (Reversal 0400) tetiklendi.");
            return [new RefundPayment(Id, paymentId, "Sipariş daha önce iptal edildiği için ödeme iade edildi.")];
        }

        PaymentId = paymentId;
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
        AddHistory(OrderStatus.Confirmed, $"Ödeme onaylandı (Onay Kodu: {authCode}). Sipariş kesinleşti.");

        return [new OrderConfirmed(Id, CustomerId, TotalAmount)];
    }

    public IReadOnlyList<IIntegrationMessage> OnPaymentFailed(Guid paymentId, string reason, string? responseCode)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled)
            return [];

        PaymentId = paymentId;
        var hadReservedStock = Status == OrderStatus.StockReserved || Status == OrderStatus.Pending;
        Status = OrderStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(responseCode) ? reason : $"{reason} (Kod: {responseCode})";
        UpdatedAt = DateTime.UtcNow;
        AddHistory(OrderStatus.Cancelled, $"Ödeme başarısız: {CancelReason}. Ayrılan stok geri bırakılıyor (Telafi).");

        var outgoing = new List<IIntegrationMessage>();
        if (hadReservedStock)
            outgoing.Add(new ReleaseStock(Id, CancelReason));

        outgoing.Add(new OrderCancelled(Id, CustomerId, CancelReason));
        return outgoing;
    }

    public IReadOnlyList<IIntegrationMessage> OnTimedOut()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.StockReserved))
            return [];

        const string reason = "Ödeme süresi dolduğu için sipariş zaman aşımına uğradı.";
        Status = OrderStatus.Cancelled;
        CancelReason = reason;
        UpdatedAt = DateTime.UtcNow;
        AddHistory(OrderStatus.Cancelled, $"{reason} Ayrılan stok geri bırakılıyor.");

        return
        [
            new ReleaseStock(Id, reason),
            new OrderCancelled(Id, CustomerId, reason)
        ];
    }

    private void AddHistory(OrderStatus status, string note)
    {
        History.Add(new OrderHistoryEntry
        {
            OrderId = Id,
            Status = status,
            Note = note,
            OccurredAt = DateTime.UtcNow
        });
    }
}

