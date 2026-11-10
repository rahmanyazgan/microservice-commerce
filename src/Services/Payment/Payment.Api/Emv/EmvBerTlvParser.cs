using BuildingBlocks.Web;

namespace Payment.Api.Emv;

public sealed record EmvTlvTagDto(string Tag, string Name, int Length, string Value);

/// <summary>
/// EMV çipli kart işlemlerinde (DE-55) kullanılan BER-TLV (Tag-Length-Value) veri yapısını ayrıştırır.
/// </summary>
public static class EmvBerTlvParser
{
    private static readonly Dictionary<string, string> KnownTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4F"] = "AID (Application Identifier)",
        ["50"] = "Uygulama Etiketi (Application Label)",
        ["5A"] = "Uygulama PAN (Kart Numarası)",
        ["5F2A"] = "İşlem Para Birimi Kodu (Transaction Currency Code)",
        ["5F34"] = "PAN Sıra Numarası",
        ["82"] = "AIP (Application Interchange Profile)",
        ["84"] = "Dedicated File (DF) Adı",
        ["95"] = "TVR (Terminal Verification Results)",
        ["9A"] = "İşlem Tarihi (YYMMDD)",
        ["9C"] = "İşlem Tipi",
        ["9F02"] = "İşlem Tutarı (Amount, Authorized)",
        ["9F03"] = "Diğer Tutar (Cashback)",
        ["9F10"] = "Issuer Uygulama Verisi (IAD)",
        ["9F1A"] = "Terminal Ülke Kodu (0792 = Türkiye)",
        ["9F26"] = "Uygulama Kriptogramı (AC / ARQC)",
        ["9F27"] = "Kriptogram Bilgi Verisi (CID)",
        ["9F36"] = "Uygulama İşlem Sayacı (ATC)",
        ["9F37"] = "Öngörülemeyen Sayı (Unpredictable Number)"
    };

    public static IReadOnlyList<EmvTlvTagDto> Parse(string hex)
    {
        var clean = new string((hex ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (clean.Length == 0 || clean.Length % 2 != 0)
            throw new DomainException("EMV TLV verisi çift uzunlukta geçerli bir hex dizgesi olmalıdır.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(clean);
        }
        catch (FormatException)
        {
            throw new DomainException("Hex dizgesi geçersiz karakterler içeriyor.");
        }

        var result = new List<EmvTlvTagDto>();
        var i = 0;

        while (i < bytes.Length)
        {
            var tagStart = i;
            var firstTagByte = bytes[i++];

            // Alt 5 bit 11111 (0x1F) ise çok baytlı EMV tag (ör. 9F02, 5F2A)
            if ((firstTagByte & 0x1F) == 0x1F)
            {
                while (i < bytes.Length && (bytes[i++] & 0x80) != 0) { }
            }

            var tagHex = Convert.ToHexString(bytes.AsSpan(tagStart, i - tagStart));
            if (i >= bytes.Length)
                throw new DomainException($"'{tagHex}' etiketi için uzunluk baytı eksik.");

            int length = bytes[i++];
            if ((length & 0x80) != 0)
            {
                var numLenBytes = length & 0x7F;
                if (numLenBytes is < 1 or > 2 || i + numLenBytes > bytes.Length)
                    throw new DomainException($"'{tagHex}' etiketi için geçersiz uzunluk kodlaması.");

                length = 0;
                for (var b = 0; b < numLenBytes; b++)
                    length = (length << 8) | bytes[i++];
            }

            if (i + length > bytes.Length)
                throw new DomainException($"'{tagHex}' etiketinin uzunluğu ({length} bayt) veri boyutunu aşıyor.");

            var valueHex = Convert.ToHexString(bytes.AsSpan(i, length));
            i += length;

            var name = KnownTags.GetValueOrDefault(tagHex, "EMV Veri Alanı");
            result.Add(new EmvTlvTagDto(tagHex, name, length, valueHex));
        }

        return result;
    }
}

