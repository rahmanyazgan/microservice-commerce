using BuildingBlocks.Contracts;
using Order.Api.Domain;
using Xunit;

namespace Order.Tests;

public sealed class OrderSagaTests
{
    [Fact]
    public void Basarili_Akis_Pending_StockReserved_Confirmed_Olmali()
    {
        var (order, createMsgs) = OrderEntity.Create("musteri-1", [(1, "Klavye", 2450m, 2)]);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(4900m, order.TotalAmount);
        Assert.Single(createMsgs.OfType<ReserveStock>());

        var stockMsgs = order.OnStockReserved();
        Assert.Equal(OrderStatus.StockReserved, order.Status);
        var reqPay = Assert.Single(stockMsgs.OfType<RequestPayment>());
        Assert.Equal(4900m, reqPay.Amount);

        var payMsgs = order.OnPaymentSucceeded(Guid.NewGuid(), "654321");
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Single(payMsgs.OfType<OrderConfirmed>());
    }

    [Fact]
    public void Odeme_Reddedildiginde_Saga_Telafi_Adimi_ReleaseStock_Uretmeli()
    {
        var (order, _) = OrderEntity.Create("musteri-1", [(2, "Mouse", 1150m, 1)]);
        order.OnStockReserved();

        var failMsgs = order.OnPaymentFailed(Guid.NewGuid(), "Yetersiz Bakiye", "51");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(failMsgs.OfType<ReleaseStock>());
        Assert.Single(failMsgs.OfType<OrderCancelled>());
    }

    [Fact]
    public void Iptal_Edilmis_Siparise_Sonradan_Odeme_Gelirse_RefundPayment_Uretilmeli()
    {
        var (order, _) = OrderEntity.Create("musteri-1", [(3, "Monitör", 7890m, 1)]);
        order.OnStockReserved();
        order.OnTimedOut();

        Assert.Equal(OrderStatus.Cancelled, order.Status);

        var latePaymentMsgs = order.OnPaymentSucceeded(Guid.NewGuid(), "999888");
        Assert.Single(latePaymentMsgs.OfType<RefundPayment>());
    }
}

