using Payment.Api.Cards;
using Payment.Api.Emv;
using Payment.Api.Iso8583;
using Xunit;

namespace Payment.Tests;

public sealed class PaymentStandardsTests
{
    [Theory]
    [InlineData("4543600000000003", true, "Visa")]
    [InlineData("5528790000000008", true, "Mastercard")]
    [InlineData("5406670000000009", true, "Mastercard")]
    [InlineData("9792000000000003", true, "Troy")]
    [InlineData("4543600000000001", false, "Visa")]
    public void Luhn_Ve_Marka_Tespiti_Dogru_Calismali(string pan, bool expectedValid, string expectedBrand)
    {
        Assert.Equal(expectedValid, CardValidator.IsValidLuhn(pan));
        Assert.Equal(expectedBrand, CardValidator.DetectBrand(pan));
    }

    [Fact]
    public void MaskPan_Yalnizca_Ilk6_Ve_Son4_Haneyi_Gostermeli()
    {
        var masked = CardValidator.MaskPan("5528 7900 0000 0008");
        Assert.Equal("5528 79** **** 0008", masked);
    }

    [Fact]
    public void Iso8583_Pack_Ve_Unpack_Tum_Alanlari_Korumali()
    {
        var original = new Iso8583Message("0100")
            .SetField(2, "454360******0006")
            .SetField(3, "000000")
            .SetField(4, "000000245000")
            .SetField(7, "1110063000")
            .SetField(11, "123456")
            .SetField(37, "261110123456")
            .SetField(41, "TERM0001")
            .SetField(49, "949")
            .SetField(61, "3DS=2.2;ECI=05;CAVV=AAABBBCCC123");

        var wire = original.Pack();
        var unpacked = Iso8583Message.Unpack(wire);

        Assert.Equal("0100", unpacked.Mti);
        Assert.Equal(original.BuildBitmapHex(), unpacked.BuildBitmapHex());
        Assert.Equal("454360******0006", unpacked.GetField(2));
        Assert.Equal("000000245000", unpacked.GetField(4));
        Assert.Equal("123456", unpacked.GetField(11));
        Assert.Equal("949", unpacked.GetField(49));
        Assert.Equal("3DS=2.2;ECI=05;CAVV=AAABBBCCC123", unpacked.GetField(61));
    }

    [Fact]
    public void EmvBerTlvParser_CokBaytli_Tag_Ve_Degerleri_Ayristirmali()
    {
        const string sampleHex = "9F02060000000150005A0841111111111111119F1A020792";
        var tags = EmvBerTlvParser.Parse(sampleHex);

        Assert.Equal(3, tags.Count);
        Assert.Equal("9F02", tags[0].Tag);
        Assert.Equal("000000015000", tags[0].Value);
        Assert.Equal("5A", tags[1].Tag);
        Assert.Equal("4111111111111111", tags[1].Value);
        Assert.Equal("9F1A", tags[2].Tag);
        Assert.Equal("0792", tags[2].Value);
    }
}
