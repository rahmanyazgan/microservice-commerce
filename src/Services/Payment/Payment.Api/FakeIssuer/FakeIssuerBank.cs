using System.Globalization;
using BuildingBlocks.Web;
using Payment.Api.Cards;
using Payment.Api.Iso8583;

namespace Payment.Api.FakeIssuer;

public sealed record TestCardInfo(
    string Number,
    string Brand,
    string Description,
    bool ThreeDs,
    string ExpectedResult);

/// <summary>
/// Sahte Kart Bankası (Issuer Switch): Gelen ISO 8583 <c>0100</c> otorizasyon mesajını çözer,
/// test kartına göre <c>00</c> (Onay), <c>51</c> (Yetersiz Bakiye) veya <c>05</c> (Banka Reddi)
/// yanıt koduyla <c>0110</c> mesajı üretir.
/// </summary>
public sealed class FakeIssuerBank(ChaosState chaos)
{
    public const string InsufficientFundsCard = "5406670000000009";
    public const string DoNotHonorCard = "9792000000000003";
    public const string Non3DsApprovedCard = "4111111111111111";

    public static readonly IReadOnlyList<TestCardInfo> TestCards =
    [
        new("4543600000000003", "Visa", "3D Secure + Onay (SMS Kodu: 123456)", true, "00 - Onaylandı"),
        new("5528790000000008", "Mastercard", "3D Secure + Onay (SMS Kodu: 123456)", true, "00 - Onaylandı"),
        new(Non3DsApprovedCard, "Visa", "3D Secure olmadan doğrudan onay", false, "00 - Onaylandı"),
        new(InsufficientFundsCard, "Mastercard", "Yetersiz bakiye → Saga stok iadesini tetikler", true, "51 - Yetersiz Bakiye"),
        new(DoNotHonorCard, "Troy", "Banka tarafından reddedilen kart", false, "05 - Onaylanmadı (Do Not Honor)")
    ];

    public static bool RequiresThreeDs(string cleanPan)
        => cleanPan != Non3DsApprovedCard && cleanPan != DoNotHonorCard;

    public static Iso8583Message BuildAuthorizationRequest(
        string cleanPan,
        decimal amount,
        int expiryMonth,
        int expiryYear,
        string stan,
        string rrn,
        string? eci)
    {
        var now = DateTime.UtcNow;
        var kurus = ((long)decimal.Round(amount * 100m, 0)).ToString("D12", CultureInfo.InvariantCulture);
        var yy = (expiryYear % 100).ToString("D2", CultureInfo.InvariantCulture);
        var mm = expiryMonth.ToString("D2", CultureInfo.InvariantCulture);
        var compactMaskedPan = string.Concat(cleanPan[..6], new string('*', cleanPan.Length - 10), cleanPan[^4..]);

        var req = new Iso8583Message("0100")
            .SetField(2, compactMaskedPan)
            .SetField(3, "000000") // Satış işlemi
            .SetField(4, kurus)
            .SetField(7, now.ToString("MMddHHmmss", CultureInfo.InvariantCulture))
            .SetField(11, stan)
            .SetField(12, now.ToString("HHmmss", CultureInfo.InvariantCulture))
            .SetField(13, now.ToString("MMdd", CultureInfo.InvariantCulture))
            .SetField(14, $"{yy}{mm}")
            .SetField(22, string.IsNullOrEmpty(eci) ? "012" : "812")
            .SetField(37, rrn)
            .SetField(41, "TERM0001")
            .SetField(42, "ETICARET0000001")
            .SetField(43, "MINI MAGAZA E-TICARET ISTANBUL TR")
            .SetField(49, "949"); // TRY

        if (!string.IsNullOrEmpty(eci))
            req.SetField(61, $"3DS=2.2;ECI={eci};CAVV=AAABBBCCC123");

        return req;
    }

    public async Task<(Iso8583Message Response, string ResponseCode, string? AuthCode)> AuthorizeAsync(
        Iso8583Message request,
        string cleanPan,
        CancellationToken ct)
    {
        await chaos.ApplyAsync("Banka (Issuer Switch)", ct);

        // Ham ISO 8583 mesajını tel üzerinden geçmiş gibi paketleyip tekrar açıyoruz
        var wire = request.Pack();
        var parsedReq = Iso8583Message.Unpack(wire);

        var rc = cleanPan switch
        {
            InsufficientFundsCard => "51",
            DoNotHonorCard => "05",
            _ => "00"
        };

        var authCode = rc == "00"
            ? Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture)
            : null;

        var resp = new Iso8583Message("0110")
            .SetField(2, parsedReq.GetField(2)!)
            .SetField(3, parsedReq.GetField(3)!)
            .SetField(4, parsedReq.GetField(4)!)
            .SetField(7, DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture))
            .SetField(11, parsedReq.GetField(11)!)
            .SetField(37, parsedReq.GetField(37)!)
            .SetField(39, rc)
            .SetField(41, parsedReq.GetField(41)!)
            .SetField(49, parsedReq.GetField(49)!);

        if (authCode is not null)
            resp.SetField(38, authCode);

        return (resp, rc, authCode);
    }

    public static Iso8583Message BuildReversalRequest(Iso8583Message originalRequest, string? authCode)
    {
        var rev = new Iso8583Message("0400")
            .SetField(2, originalRequest.GetField(2)!)
            .SetField(3, "000000")
            .SetField(4, originalRequest.GetField(4)!)
            .SetField(7, DateTime.UtcNow.ToString("MMddHHmmss", CultureInfo.InvariantCulture))
            .SetField(11, originalRequest.GetField(11)!)
            .SetField(37, originalRequest.GetField(37)!)
            .SetField(39, "00")
            .SetField(41, originalRequest.GetField(41)!)
            .SetField(42, originalRequest.GetField(42)!)
            .SetField(49, originalRequest.GetField(49)!);

        if (!string.IsNullOrEmpty(authCode))
            rev.SetField(38, authCode);

        return rev;
    }
}
