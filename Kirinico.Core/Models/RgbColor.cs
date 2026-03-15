using OpenCvSharp;

namespace Kirinico.Core.Models;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public Scalar ToBgrScalar() => new(B, G, R);

    public string ToHex() => $"{R:X2}{G:X2}{B:X2}";

    public static bool TryParseHex(string? value, out RgbColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            normalized = normalized[1..];
        }

        if (normalized.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(normalized[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    public static RgbColor Blend(RgbColor left, RgbColor right, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return new RgbColor(
            (byte)Math.Round((left.R * (1d - amount)) + (right.R * amount)),
            (byte)Math.Round((left.G * (1d - amount)) + (right.G * amount)),
            (byte)Math.Round((left.B * (1d - amount)) + (right.B * amount)));
    }
}
