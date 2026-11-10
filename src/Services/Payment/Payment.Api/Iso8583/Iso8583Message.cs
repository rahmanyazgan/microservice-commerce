using System.Globalization;
using System.Text;

namespace Payment.Api.Iso8583;

public enum IsoFieldFormat
{
    Fixed,
    LlVar,
    LllVar
}

public sealed record IsoFieldSpec(int Number, string Name, IsoFieldFormat Format, int MaxLength);

public sealed record IsoFieldView(int Number, string Name, string Value);

public sealed record IsoMessageView(
    string Mti,
    string MtiText,
    string Bitmap,
    string Raw,
    string Hex,
    IReadOnlyList<IsoFieldView> Fields);

/// <summary>
/// ISO 8583:1987 Finansal İşlem Mesaj Standardı paketleyici ve çözücü (Packer / Unpacker).
/// MTI + 16 karakterlik Hex Primary Bitmap (64 alan) + Veri Elemanlarını (DE) üretir ve ayrıştırır.
/// </summary>
public sealed class Iso8583Message
{
    public static readonly IReadOnlyDictionary<int, IsoFieldSpec> Specs = new Dictionary<int, IsoFieldSpec>
    {
        [2] = new(2, "PAN (Kart Numarası - Maskeli)", IsoFieldFormat.LlVar, 19),
        [3] = new(3, "İşlem Kodu (Processing Code)", IsoFieldFormat.Fixed, 6),
        [4] = new(4, "İşlem Tutarı (Kuruş)", IsoFieldFormat.Fixed, 12),
        [7] = new(7, "İletim Zamanı (MMDDhhmmss)", IsoFieldFormat.Fixed, 10),
        [11] = new(11, "STAN (Sistem İz Numarası)", IsoFieldFormat.Fixed, 6),
        [12] = new(12, "Yerel Saat (hhmmss)", IsoFieldFormat.Fixed, 6),
        [13] = new(13, "Yerel Tarih (MMDD)", IsoFieldFormat.Fixed, 4),
        [14] = new(14, "Son Kullanma Tarihi (YYMM)", IsoFieldFormat.Fixed, 4),
        [22] = new(22, "POS Giriş Modu", IsoFieldFormat.Fixed, 3),
        [37] = new(37, "RRN (Referans Numarası)", IsoFieldFormat.Fixed, 12),
        [38] = new(38, "Onay Kodu (Auth ID Response)", IsoFieldFormat.Fixed, 6),
        [39] = new(39, "Yanıt Kodu (Response Code)", IsoFieldFormat.Fixed, 2),
        [41] = new(41, "Terminal Kimliği (TID)", IsoFieldFormat.Fixed, 8),
        [42] = new(42, "Üye İşyeri Kimliği (MID)", IsoFieldFormat.Fixed, 15),
        [43] = new(43, "Üye İşyeri Adı/Konumu", IsoFieldFormat.Fixed, 40),
        [49] = new(49, "Para Birimi Kodu (949=TRY)", IsoFieldFormat.Fixed, 3),
        [61] = new(61, "3D Secure / ECI Verisi", IsoFieldFormat.LllVar, 999)
    };

    public string Mti { get; }
    private readonly SortedDictionary<int, string> _fields = new();

    public Iso8583Message(string mti)
    {
        if (string.IsNullOrWhiteSpace(mti) || mti.Length != 4 || !mti.All(char.IsDigit))
            throw new ArgumentException("MTI 4 haneli sayısal bir değer olmalıdır (ör. 0100, 0110, 0400).", nameof(mti));
        Mti = mti;
    }

    public IReadOnlyDictionary<int, string> Fields => _fields;

    public Iso8583Message SetField(int fieldNumber, string value)
    {
        if (!Specs.TryGetValue(fieldNumber, out var spec))
            throw new ArgumentOutOfRangeException(nameof(fieldNumber), $"DE-{fieldNumber} tanımlı değil.");

        var normalized = spec.Format switch
        {
            IsoFieldFormat.Fixed => NormalizeFixed(fieldNumber, value, spec.MaxLength),
            IsoFieldFormat.LlVar or IsoFieldFormat.LllVar => value.Length <= spec.MaxLength
                ? value
                : value[..spec.MaxLength],
            _ => value
        };

        _fields[fieldNumber] = normalized;
        return this;
    }

    public string? GetField(int fieldNumber)
        => _fields.TryGetValue(fieldNumber, out var v) ? v : null;

    public string BuildBitmapHex()
    {
        ulong bits = 0;
        foreach (var f in _fields.Keys)
        {
            if (f is < 2 or > 64)
                throw new InvalidOperationException($"Primary bitmap yalnızca 2..64 arası alanları destekler (Alan={f}).");
            bits |= 1UL << (64 - f);
        }

        return bits.ToString("X16", CultureInfo.InvariantCulture);
    }

    public string Pack()
    {
        var sb = new StringBuilder();
        sb.Append(Mti);
        sb.Append(BuildBitmapHex());

        foreach (var (num, val) in _fields)
        {
            var spec = Specs[num];
            switch (spec.Format)
            {
                case IsoFieldFormat.Fixed:
                    sb.Append(val);
                    break;
                case IsoFieldFormat.LlVar:
                    sb.Append(val.Length.ToString("D2", CultureInfo.InvariantCulture));
                    sb.Append(val);
                    break;
                case IsoFieldFormat.LllVar:
                    sb.Append(val.Length.ToString("D3", CultureInfo.InvariantCulture));
                    sb.Append(val);
                    break;
            }
        }

        return sb.ToString();
    }

    public static Iso8583Message Unpack(string packed)
    {
        if (string.IsNullOrEmpty(packed) || packed.Length < 20)
            throw new ArgumentException("Geçersiz ISO 8583 mesaj uzunluğu.", nameof(packed));

        var mti = packed[..4];
        var bitmapHex = packed.Substring(4, 16);
        var bits = ulong.Parse(bitmapHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var msg = new Iso8583Message(mti);
        var cursor = 20;

        for (var field = 2; field <= 64; field++)
        {
            var isPresent = (bits & (1UL << (64 - field))) != 0;
            if (!isPresent) continue;

            var spec = Specs[field];
            int len;
            switch (spec.Format)
            {
                case IsoFieldFormat.Fixed:
                    len = spec.MaxLength;
                    break;
                case IsoFieldFormat.LlVar:
                    len = int.Parse(packed.AsSpan(cursor, 2), CultureInfo.InvariantCulture);
                    cursor += 2;
                    break;
                case IsoFieldFormat.LllVar:
                    len = int.Parse(packed.AsSpan(cursor, 3), CultureInfo.InvariantCulture);
                    cursor += 3;
                    break;
                default:
                    throw new InvalidOperationException();
            }

            var val = packed.Substring(cursor, len);
            cursor += len;
            msg._fields[field] = val;
        }

        return msg;
    }

    public IsoMessageView ToView()
    {
        var raw = Pack();
        var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(raw));
        var fieldViews = _fields
            .Select(kv => new IsoFieldView(
                kv.Key,
                Specs.TryGetValue(kv.Key, out var s) ? s.Name : $"DE-{kv.Key}",
                kv.Value))
            .ToList();

        return new IsoMessageView(
            Mti,
            DescribeMti(Mti),
            BuildBitmapHex(),
            raw,
            hex,
            fieldViews);
    }

    public static string DescribeMti(string mti) => mti switch
    {
        "0100" => "Otorizasyon İsteği (Authorization Request)",
        "0110" => "Otorizasyon Yanıtı (Authorization Response)",
        "0400" => "İptal / Ters İşlem İsteği (Reversal Request)",
        "0410" => "İptal / Ters İşlem Yanıtı (Reversal Response)",
        _ => "ISO 8583 Mesajı"
    };

    public static string DescribeResponseCode(string? rc) => rc switch
    {
        "00" => "Onaylandı (Approved)",
        "05" => "Onaylanmadı — Banka Reddi (Do Not Honor)",
        "14" => "Geçersiz Kart Numarası",
        "51" => "Yetersiz Bakiye (Insufficient Funds)",
        "54" => "Vadesi Dolmuş Kart (Expired Card)",
        "91" => "Banka / Switch Hizmet Dışı (Issuer Inoperative)",
        _ => "İşlem Reddedildi"
    };

    private static string NormalizeFixed(int field, string value, int len)
    {
        if (value.Length > len)
            return value[..len];

        // Sayısal alanlar sola sıfır dolgulu, alfa-sayısal alanlar sağa boşluk dolgulu
        var isNumeric = field is 3 or 4 or 7 or 11 or 12 or 13 or 14 or 22 or 49;
        return isNumeric ? value.PadLeft(len, '0') : value.PadRight(len, ' ');
    }
}

