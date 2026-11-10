using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.FakeIssuer;
using Payment.Api.Iso8583;

namespace Payment.Api.Application;

public sealed class PaymentIntegrationHandlers(PaymentDbContext db, ILogger<PaymentIntegrationHandlers> logger)
    : IMessageHandler<RequestPayment>,
      IMessageHandler<OrderCancelled>,
      IMessageHandler<RefundPayment>
{
    public async Task HandleAsync(RequestPayment message, CancellationToken ct)
    {
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (existing is not null)
            return;

        db.Payments.Add(new PaymentRecord
        {
            Id = Guid.NewGuid(),
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            Amount = message.Amount,
            Status = PaymentStatus.AwaitingCard,
            CreatedAt = DateTime.UtcNow
        });

        logger.LogInformation("Ödeme niyeti (intent) oluşturuldu: Sipariş={OrderId}, Tutar={Amount}", message.OrderId, message.Amount);
    }

    public async Task HandleAsync(OrderCancelled message, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (payment is null) return;

        if (payment.Status is PaymentStatus.AwaitingCard or PaymentStatus.ThreeDsPending)
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.ResponseText = message.Reason;
        }
    }

    public async Task HandleAsync(RefundPayment message, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (payment is null || payment.Status != PaymentStatus.Approved)
            return;

        if (!string.IsNullOrEmpty(payment.IsoRequestRaw))
        {
            var orig = Iso8583Message.Unpack(payment.IsoRequestRaw);
            var reversal = FakeIssuerBank.BuildReversalRequest(orig, payment.AuthCode);
            payment.IsoReversalRaw = reversal.Pack();
        }

        payment.Status = PaymentStatus.Reversed;
        payment.ResponseText = $"İade / Ters İşlem (0400 Reversal): {message.Reason}";
        logger.LogWarning("Ödeme için ISO 8583 0400 Reversal üretildi: Ödeme={PaymentId}", payment.Id);
    }
}

