namespace Kirinico.Core.Models;

public sealed class OutlineOptions
{
    public bool Enabled { get; set; }

    public RgbColor Color { get; set; } = new(0, 0, 0);

    public double Thickness { get; set; } = 1d;
}
