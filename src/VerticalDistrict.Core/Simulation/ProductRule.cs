using System.Text.Json.Serialization;

namespace VerticalDistrict.Core.Simulation;

/// <summary>Original provisional offerings, layered on the owning facility's balance rule.</summary>
public sealed record ProductRule(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Model,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Category,
    [property: JsonRequired] int PricePercent,
    [property: JsonRequired] int ServicePercent,
    [property: JsonRequired] int DemandPercent,
    [property: JsonRequired] int LunchDemandPercent,
    [property: JsonRequired] string Provenance)
{
    public const string DefaultFoodId = "cafe-classic";
    public const string DefaultShopId = "shop-essentials";
    public const int MaximumAgreedServiceSeconds = 18000;

    /// <summary>Round down to whole minor units, retaining the existing asking-price ceiling.</summary>
    public long PriceFor(BusinessRule business) => Math.Min(100_000_000,
        checked(business.PriceMinor * PricePercent) / 100);

    /// <summary>Round service time up to a whole logical second; service is never instantaneous.</summary>
    public int ServiceSecondsFor(BusinessRule business) => Math.Max(1,
        checked((int)((checked((long)business.ServiceSeconds * ServicePercent) + 99) / 100)));

    // Only C# fixture construction uses these defaults. Current JSON must explicitly supply Products.
    internal static ProductRule[] OriginalDefaults() =>
    [
        new(DefaultFoodId, "Food", "Classic cafe menu", "Everyday", 100, 100, 100, 100, OriginalProvenance),
        new("cafe-lunch", "Food", "Lunch menu", "Lunch", 150, 150, 80, 160, OriginalProvenance),
        new(DefaultShopId, "Shop", "Everyday essentials", "Essentials", 100, 100, 100, 100, OriginalProvenance),
        new("shop-gifts", "Shop", "Gift assortment", "Gifts", 160, 180, 70, 100, OriginalProvenance)
    ];

    private const string OriginalProvenance = "Original Vertical District provisional offering, 2026-10-01. Not approved GDD balance or reference-game facts.";
}
