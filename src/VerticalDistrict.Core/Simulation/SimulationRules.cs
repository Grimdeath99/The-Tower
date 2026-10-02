using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VerticalDistrict.Core.Simulation;

public sealed record BusinessRule(
    [property: JsonRequired] string Id, [property: JsonRequired] string Model,
    [property: JsonRequired] int Capacity, [property: JsonRequired] long PriceMinor,
    [property: JsonRequired] long UpkeepMinor, [property: JsonRequired] long StaffSalaryMinor,
    [property: JsonRequired] int Staff, [property: JsonRequired] int OpenHour,
    [property: JsonRequired] int CloseHour, [property: JsonRequired] int VisitSeconds,
    [property: JsonRequired] int ArrivalIntervalSeconds, [property: JsonRequired] int StayDays,
    [property: JsonRequired] int ServiceSeconds);
public sealed record PromotionRule(int Rank, int Population, long DailyProfitMinor, int Diversity,
    int Satisfaction, int Cleanliness, int HotelRooms, int SuccessfulTrips);

/// <summary>Original provisional tuning, never claims reference-game or real-world values.</summary>
public sealed class SimulationRules
{
    public required string Provenance { get; init; }
    public required int GameSecondsPerRealSecond { get; init; }
    public required int MaxStepsPerFrame { get; init; }
    public required long ElevatorCostMinor { get; init; }
    public required long StairCostMinor { get; init; }
    public required long EscalatorCostMinor { get; init; }
    public required long EventPreparationCostMinor { get; init; }
    public required long RepairCostMinor { get; init; }
    public required BusinessRule[] Businesses { get; init; }
    public required PromotionRule[] Promotions { get; init; }
    public ManagementRules Management { get; init; } = new();
    [JsonRequired] public ProductRule[] Products { get; init; } = ProductRule.OriginalDefaults();
    public static SimulationRules Load(string json, ContentCatalog catalog)
    {
        SimulationRules rules;
        try
        {
            rules = JsonSerializer.Deserialize<SimulationRules>(json, JsonOptions)
                ?? throw new ContentValidationException("Missing simulation rules.");
        }
        catch (JsonException error)
        {
            throw new ContentValidationException($"Invalid simulation rules JSON: {error.Message}");
        }
        if (string.IsNullOrWhiteSpace(rules.Provenance) || rules.GameSecondsPerRealSecond is < 1 or > 600
            || rules.MaxStepsPerFrame is < 1 or > 3600 || rules.ElevatorCostMinor < 0 || rules.StairCostMinor < 0 || rules.EscalatorCostMinor < 0 || rules.EventPreparationCostMinor < 0 || rules.RepairCostMinor < 0
            || rules.Businesses == null || rules.Promotions == null || rules.Promotions.Length != 6)
            throw new ContentValidationException("Invalid simulation timing, costs, or progression rules.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in rules.Businesses)
        {
            if (b == null || string.IsNullOrWhiteSpace(b.Id))
                throw new ContentValidationException("A business rule is null or has no facility ID.");
            if (!ids.Add(b.Id)) throw new ContentValidationException($"{b.Id}: duplicate business rule.");
            if (!catalog.TryGet(b.Id, out var facility))
                throw new ContentValidationException($"{b.Id}: business rule references an unknown facility.");
            if (!ContentCatalog.IsSupportedOperatingModel(b.Model))
                throw new ContentValidationException($"{b.Id}: unsupported operating model '{b.Model}'.");
            if (b.Model != facility!.Operations.Model)
                throw new ContentValidationException($"{b.Id}: runtime model '{b.Model}' contradicts catalogue model '{facility.Operations.Model}'.");
            if (b.Capacity is < 0 or > 200)
                throw new ContentValidationException($"{b.Id}: capacity must be between 0 and 200.");
            if (ContentCatalog.RequiresOccupantCapacity(b.Model) && b.Capacity == 0)
                throw new ContentValidationException($"{b.Id}: operating model '{b.Model}' requires positive capacity.");
            if (b.PriceMinor is < 0 or > 100_000_000 || b.UpkeepMinor is < 0 or > 100_000_000
                || b.StaffSalaryMinor is < 0 or > 100_000_000)
                throw new ContentValidationException($"{b.Id}: price, daily upkeep and per-staff daily salary must be between 0 and 100000000 minor units.");
            if (b.Staff is < 0 or > 20)
                throw new ContentValidationException($"{b.Id}: staff allocation must be between 0 and 20.");
            if (b.OpenHour is < 0 or > 23 || b.CloseHour is < 1 or > 24 || b.CloseHour <= b.OpenHour)
                throw new ContentValidationException($"{b.Id}: opening hours must define a positive same-day interval within 00:00-24:00.");
            if (b.VisitSeconds is < 1 or > 604800 || b.ArrivalIntervalSeconds is < 30 or > 86400
                || b.StayDays is < 1 or > 30 || b.ServiceSeconds is < 1 or > 3600)
                throw new ContentValidationException($"{b.Id}: visit (1-604800s), arrivals (30-86400s), stay (1-30 days) or service (1-3600s) duration is out of range.");
        }
        foreach (var facility in catalog.Definitions)
            if (!ids.Contains(facility.Id))
                throw new ContentValidationException($"{facility.Id}: missing runtime business rule for catalogue facility.");
        for (var i = 0; i < rules.Promotions.Length; i++)
        {
            var p = rules.Promotions[i];
            if (p == null || p.Rank != i + 2 || p.Population < 0 || p.DailyProfitMinor < 0 || p.Diversity is < 1 or > 20
                || p.Satisfaction is < 0 or > 100 || p.Cleanliness is < 0 or > 100 || p.HotelRooms < 0 || p.SuccessfulTrips < 0)
                throw new ContentValidationException("Invalid seven-rank promotion rule.");
        }
        if (rules.Management == null) throw new ContentValidationException("Missing management rules.");
        rules.Management.Validate();
        rules.ValidateProducts();
        return rules;
    }
    public BusinessRule? For(string id) => Businesses.FirstOrDefault(x => x.Id == id);
    public ProductRule? ProductFor(string id) => Products.FirstOrDefault(product => product.Id == id);
    public IReadOnlyList<ProductRule> ProductsFor(string model)
        => Array.AsReadOnly(Products.Where(product => product.Model == model).ToArray());
    public ProductRule DefaultProductFor(string model)
    {
        var id = model switch
        {
            "Food" => ProductRule.DefaultFoodId,
            "Shop" => ProductRule.DefaultShopId,
            _ => throw new KeyNotFoundException($"Operating model '{model}' has no product catalogue.")
        };
        return ProductFor(id) is { } product && product.Model == model ? product
            : throw new ContentValidationException($"{id}: missing or mismatched default product for model '{model}'.");
    }

    private void ValidateProducts()
    {
        if (Products is not { Length: > 0 }) throw new ContentValidationException("Products must be an explicit nonempty list.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in Products)
        {
            if (product == null) throw new ContentValidationException("A product definition is null.");
            if (product.Id == null || !Regex.IsMatch(product.Id, "^[a-z][a-z0-9-]*$"))
                throw new ContentValidationException($"Product '{product.Id}': IDs must use lowercase letters, digits and hyphens.");
            if (!ids.Add(product.Id)) throw new ContentValidationException($"{product.Id}: duplicate product ID.");
            if (product.Model is not ("Food" or "Shop"))
                throw new ContentValidationException($"{product.Id}: unsupported product model '{product.Model}'; expected Food or Shop.");
            if (!Businesses.Any(business => business.Model == product.Model))
                throw new ContentValidationException($"{product.Id}: product model '{product.Model}' has no matching facility business rule.");
            if (string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Category)
                || string.IsNullOrWhiteSpace(product.Provenance))
                throw new ContentValidationException($"{product.Id}: product name, category and provenance are required.");
            if (product.PricePercent is < 1 or > 500)
                throw new ContentValidationException($"{product.Id}: pricePercent must be between 1 and 500.");
            if (product.ServicePercent is < 1 or > 500)
                throw new ContentValidationException($"{product.Id}: servicePercent must be between 1 and 500.");
            if (product.DemandPercent is < 1 or > 200)
                throw new ContentValidationException($"{product.Id}: demandPercent must be between 1 and 200.");
            if (product.LunchDemandPercent is < 1 or > 200)
                throw new ContentValidationException($"{product.Id}: lunchDemandPercent must be between 1 and 200.");
        }
        _ = DefaultProductFor("Food");
        _ = DefaultProductFor("Shop");
    }
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>Frame time only budgets authoritative ticks. Excess work remains queued, never skipped.</summary>
public sealed class FixedStepRunner(SimulationRules rules)
{
    private double _pending;
    public int Speed { get; private set; } = 1;
    public double BacklogSeconds => _pending;
    public double EffectiveSpeed { get; private set; }
    public void SetSpeed(int speed)
    {
        if (speed is not (0 or 1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
    }
    public int Advance(double realSeconds, Action tick)
    {
        if (!double.IsFinite(realSeconds) || realSeconds < 0) throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (Speed == 0) { EffectiveSpeed = 0; return 0; }
        _pending += realSeconds * rules.GameSecondsPerRealSecond * Speed;
        var count = (int)Math.Min(Math.Floor(_pending + 1e-9), rules.MaxStepsPerFrame);
        for (var i = 0; i < count; i++) { tick(); _pending -= 1; }
        _pending = Math.Max(0, _pending);
        EffectiveSpeed = realSeconds > 0 ? count / (realSeconds * rules.GameSecondsPerRealSecond) : 0;
        return count;
    }
    public void Reset() { _pending = 0; EffectiveSpeed = 0; }
}
