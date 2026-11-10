using BuildingBlocks.Web;

namespace Payment.Api.Cards;

/// <summary>
/// Kart doğrulama, Luhn kontrolü, BIN üzerinden şema (Visa / Mastercard / Troy) tespiti
/// ve PCI DSS uyumlu PAN maskeleme kuralları.
/// </summary>
public static class CardValidator
{
    public static string NormalizePan(string raw)
        => new((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    public static bool IsValidLuhn(string digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length is < 13 or > 19 || !digits.All(char.IsDigit))
            return false;

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public static string DetectBrand(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return "Bilinmiyor";
        if (digits.StartsWith("9792")) return "Troy";
        if (digits.StartsWith('4')) return "Visa";

        if (digits.Length >= 2 && int.TryParse(digits[..2], out var two) && two is >= 51 and <= 55)
            return "Mastercard";

        if (digits.Length >= 4 && int.TryParse(digits[..4], out var four) && four is >= 2221 and <= 2720)
            return "Mastercard";

        return "Diğer";
    }

    /// <summary>
    /// PCI DSS kuralı: Tam kart numarası (PAN) asla veritabanına veya loglara yazılmaz;
    /// yalnızca ilk 6 (BIN) ve son 4 hane görünür şekilde maskelenir (ör. <c>5528 79** **** 0008</c>).
    /// </summary>
    public static string MaskPan(string digits)
    {
        var clean = NormalizePan(digits);
        if (clean.Length < 10) return "****";

        var masked = string.Concat(clean[..6], new string('*', clean.Length - 10), clean[^4..]);
        return string.Join(' ', masked.Chunk(4).Select(c => new string(c)));
    }

    public static void ValidateOrThrow(string cardHolder, string rawPan, int expiryMonth, int expiryYear, string cvv)
    {
        var errors = new Dictionary<string, string[]>();
        var digits = NormalizePan(rawPan);

        if (string.IsNullOrWhiteSpace(cardHolder) || cardHolder.Trim().Length < 3)
            errors["cardHolder"] = ["Kart üzerindeki isim en az 3 karakter olmalıdır."];

        if (!IsValidLuhn(digits))
            errors["cardNumber"] = ["Kart numarası geçersiz (Luhn kontrolü başarısız)."];

        if (expiryMonth is < 1 or > 12)
            errors["expiryMonth"] = ["Son kullanma ayı 1 ile 12 arasında olmalıdır."];

        var normalizedYear = expiryYear < 100 ? 2000 + expiryYear : expiryYear;
        var now = DateTime.UtcNow;
        if (normalizedYear < now.Year || (normalizedYear == now.Year && expiryMonth < now.Month))
            errors["expiryYear"] = ["Kartın son kullanma tarihi geçmiş."];

        var cleanCvv = new string((cvv ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cleanCvv.Length is < 3 or > 4)
            errors["cvv"] = ["CVV güvenlik kodu 3 veya 4 haneli olmalıdır."];

        if (errors.Count > 0)
            throw new DomainException("Kart bilgileri doğrulamadan geçemedi.", 400, errors);
    }
}

