using System.Globalization;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Application;
using Payment.Api.Cards;
using Payment.Api.Domain;
using Payment.Api.Emv;
using Payment.Api.FakeIssuer;
using Payment.Api.Iso8583;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Payment.Api");

var connStr = builder.Configuration.GetConnectionString("PaymentDb")
              ?? "Server=(localdb)\\MSSQLLocalDB;Database=ETicaret_PaymentDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<PaymentDbContext>(o => o.UseSqlServer(connStr));
builder.Services.AddSingleton<FakeIssuerBank>();

builder.Services.AddRabbitMqMessaging<PaymentDbContext>(builder.Configuration, queueName: "payment")
    .AddHandler<RequestPayment, PaymentIntegrationHandlers>()
    .AddHandler<OrderCancelled, PaymentIntegrationHandlers>()
    .AddHandler<RefundPayment, PaymentIntegrationHandlers>();

var app = builder.Build();
app.UseStandardServicePipeline();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

var group = app.MapGroup("/api/payments");

group.MapGet("/test-cards", () => Results.Ok(FakeIssuerBank.TestCards));

group.MapGet("/chaos", (ChaosState chaos) => Results.Ok(chaos.Get()));
group.MapPut("/chaos", (ChaosSettings settings, ChaosState chaos) => Results.Ok(chaos.Update(settings)))
    .RequireAuthorization(JwtAuthConstants.AdminPolicy);

group.MapPost("/tools/emv-tlv", (EmvParseRequest req) =>
    Results.Ok(EmvBerTlvParser.Parse(req.Hex)));

group.MapGet("/{paymentId:guid}", async (Guid paymentId, PaymentDbContext db, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new DomainException("Ödeme kaydı bulunamadı.", 404);
    return Results.Ok(MapDetail(p));
});

group.MapGet("/by-order/{orderId:guid}", async (Guid orderId, PaymentDbContext db, CancellationToken ct) =>
{
    var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, ct)
            ?? throw new DomainException("Bu siparişe ait ödeme kaydı bulunamadı.", 404);
    return Results.Ok(MapDetail(p));
});

group.MapPost("/", async (
    HttpContext http,
    SubmitPaymentRequest req,
    PaymentDbContext db,
    FakeIssuerBank issuer,
    CancellationToken ct) =>
{
    var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var hdr) && !string.IsNullOrWhiteSpace(hdr)
        ? hdr.ToString().Trim()
        : null;

    if (!string.IsNullOrEmpty(idempotencyKey))
    {
        var existingByKey = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (existingByKey is not null)
            return Results.Ok(ToResultDto(existingByKey));
    }

    CardValidator.ValidateOrThrow(req.CardHolder, req.CardNumber, req.ExpiryMonth, req.ExpiryYear, req.Cvv);

    var payment = await db.Payments.FirstOrDefaultAsync(x => x.OrderId == req.OrderId, ct)
                  ?? throw new DomainException("Sipariş için stok ayrılması henüz tamamlanmadı; lütfen 1 saniye sonra tekrar deneyin.", 409);

    if (payment.Status is PaymentStatus.Approved or PaymentStatus.Declined or PaymentStatus.Failed)
        return Results.Ok(ToResultDto(payment));

    if (payment.Status == PaymentStatus.Cancelled)
        throw new DomainException("Bu sipariş iptal edildiği için ödeme alınamaz.", 409);

    var cleanPan = CardValidator.NormalizePan(req.CardNumber);
    payment.MaskedPan = CardValidator.MaskPan(cleanPan);
    payment.Brand = CardValidator.DetectBrand(cleanPan);
    payment.ExpiryMonth = req.ExpiryMonth;
    payment.ExpiryYear = req.ExpiryYear < 100 ? 2000 + req.ExpiryYear : req.ExpiryYear;
    payment.IssuerDecisionKey = cleanPan;
    payment.IdempotencyKey = idempotencyKey;

    if (FakeIssuerBank.RequiresThreeDs(cleanPan))
    {
        payment.Status = PaymentStatus.ThreeDsPending;
        payment.ThreeDsChallengeId = Guid.NewGuid().ToString("N")[..12];
        payment.ThreeDsRemainingAttempts = 3;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResultDto(payment));
    }

    await ExecuteIsoAuthorizationAsync(payment, cleanPan, eci: null, db, issuer, ct);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToResultDto(payment));
});

group.MapPost("/{paymentId:guid}/3ds", async (
    Guid paymentId,
    VerifyThreeDsRequest req,
    PaymentDbContext db,
    FakeIssuerBank issuer,
    CancellationToken ct) =>
{
    var payment = await db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId, ct)
                  ?? throw new DomainException("Ödeme kaydı bulunamadı.", 404);

    if (payment.Status != PaymentStatus.ThreeDsPending)
        return Results.Ok(ToResultDto(payment));

    var otp = (req.Otp ?? string.Empty).Trim();
    if (otp != "123456")
    {
        payment.ThreeDsRemainingAttempts = Math.Max(0, payment.ThreeDsRemainingAttempts - 1);
        if (payment.ThreeDsRemainingAttempts == 0)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ResponseCode = "3D";
            payment.ResponseText = "3D Secure SMS doğrulaması 3 kez hatalı girildi.";
            payment.CompletedAt = DateTime.UtcNow;
            db.AddOutboxMessage(new PaymentFailed(payment.OrderId, payment.Id, payment.ResponseText, "3D"));
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResultDto(payment,
            payment.Status == PaymentStatus.ThreeDsPending
                ? $"Hatalı doğrulama kodu. Kalan deneme hakkı: {payment.ThreeDsRemainingAttempts}"
                : null));
    }

    var eci = payment.Brand == "Mastercard" ? "02" : "05";
    await ExecuteIsoAuthorizationAsync(payment, payment.IssuerDecisionKey ?? "", eci, db, issuer, ct);
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToResultDto(payment));
});

app.Run();

static async Task ExecuteIsoAuthorizationAsync(
    PaymentRecord payment,
    string cleanPan,
    string? eci,
    PaymentDbContext db,
    FakeIssuerBank issuer,
    CancellationToken ct)
{
    payment.Stan = Random.Shared.Next(1, 999999).ToString("D6", CultureInfo.InvariantCulture);
    payment.Rrn = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture) + payment.Stan;
    payment.Eci = eci;

    var isoReq = FakeIssuerBank.BuildAuthorizationRequest(
        cleanPan.Length >= 13 ? cleanPan : "4543600000000006",
        payment.Amount,
        payment.ExpiryMonth,
        payment.ExpiryYear,
        payment.Stan,
        payment.Rrn,
        eci);

    payment.IsoRequestRaw = isoReq.Pack();

    try
    {
        var (isoResp, rc, authCode) = await issuer.AuthorizeAsync(isoReq, cleanPan, ct);
        payment.IsoResponseRaw = isoResp.Pack();
        payment.ResponseCode = rc;
        payment.ResponseText = Iso8583Message.DescribeResponseCode(rc);
        payment.AuthCode = authCode;
        payment.CompletedAt = DateTime.UtcNow;

        if (rc == "00" && authCode is not null)
        {
            payment.Status = PaymentStatus.Approved;
            db.AddOutboxMessage(new PaymentSucceeded(payment.OrderId, payment.Id, payment.Amount, authCode));
        }
        else
        {
            payment.Status = PaymentStatus.Declined;
            db.AddOutboxMessage(new PaymentFailed(payment.OrderId, payment.Id, payment.ResponseText, rc));
        }
    }
    catch (SimulatedFaultException ex)
    {
        payment.Status = PaymentStatus.Failed;
        payment.ResponseCode = "91";
        payment.ResponseText = $"Banka iletişim hatası (Switch Timeout/Fault): {ex.Message}";
        payment.CompletedAt = DateTime.UtcNow;
        db.AddOutboxMessage(new PaymentFailed(payment.OrderId, payment.Id, payment.ResponseText, "91"));
    }
}

static object ToResultDto(PaymentRecord p, string? overrideMsg = null) => new
{
    PaymentId = p.Id,
    p.OrderId,
    Status = p.Status.ToString(),
    Message = overrideMsg ?? p.ResponseText ?? p.Status switch
    {
        PaymentStatus.ThreeDsPending => "3D Secure SMS doğrulaması bekleniyor.",
        PaymentStatus.Approved => "Ödeme başarıyla onaylandı.",
        _ => "Ödeme işlemi güncellendi."
    },
    p.ResponseCode,
    ThreeDs = p.Status == PaymentStatus.ThreeDsPending
        ? new
        {
            ChallengeId = p.ThreeDsChallengeId,
            p.MaskedPan,
            p.Amount,
            MerchantName = "Mini Mağaza E-Ticaret A.Ş.",
            RemainingAttempts = p.ThreeDsRemainingAttempts
        }
        : null
};

static object MapDetail(PaymentRecord p) => new
{
    p.Id,
    p.OrderId,
    p.Amount,
    p.Currency,
    p.MaskedPan,
    p.Brand,
    Status = p.Status.ToString(),
    p.ResponseCode,
    p.ResponseText,
    p.AuthCode,
    p.Rrn,
    p.Stan,
    p.Eci,
    p.CreatedAt,
    IsoRequest = string.IsNullOrEmpty(p.IsoRequestRaw) ? null : Iso8583Message.Unpack(p.IsoRequestRaw).ToView(),
    IsoResponse = string.IsNullOrEmpty(p.IsoResponseRaw) ? null : Iso8583Message.Unpack(p.IsoResponseRaw).ToView(),
    ReversalRequest = string.IsNullOrEmpty(p.IsoReversalRaw) ? null : Iso8583Message.Unpack(p.IsoReversalRaw).ToView()
};

public sealed record SubmitPaymentRequest(
    Guid OrderId,
    string CardHolder,
    string CardNumber,
    int ExpiryMonth,
    int ExpiryYear,
    string Cvv);

public sealed record VerifyThreeDsRequest(string Otp);
public sealed record EmvParseRequest(string Hex);

