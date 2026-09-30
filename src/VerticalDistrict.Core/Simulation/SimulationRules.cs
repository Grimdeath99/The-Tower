using System.Text.Json;
using System.Text.Json.Serialization;

namespace VerticalDistrict.Core.Simulation;

public sealed record BusinessRule(string Id, string Model, int Capacity, long PriceMinor,
    long UpkeepMinor, long StaffSalaryMinor, int Staff, int OpenHour, int CloseHour,
    int VisitSeconds, int ArrivalIntervalSeconds, int StayDays, int ServiceSeconds);
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
    public static SimulationRules Load(string json, ContentCatalog catalog)
    {
        var rules = JsonSerializer.Deserialize<SimulationRules>(json, JsonOptions)
            ?? throw new ContentValidationException("Missing simulation rules.");
        if (string.IsNullOrWhiteSpace(rules.Provenance) || rules.GameSecondsPerRealSecond is < 1 or > 600
            || rules.MaxStepsPerFrame is < 1 or > 3600 || rules.ElevatorCostMinor < 0 || rules.StairCostMinor < 0 || rules.EscalatorCostMinor < 0 || rules.EventPreparationCostMinor < 0 || rules.RepairCostMinor < 0
            || rules.Businesses == null || rules.Promotions == null || rules.Promotions.Length != 6)
            throw new ContentValidationException("Invalid simulation timing, costs, or progression rules.");
        var ids = new HashSet<string>();
        var models = new[] { "Office", "Home", "Hotel", "Food", "Shop", "Cinema", "Event", "Condo", "Advertising", "Parking", "Service", "Utility", "Security", "Public", "Terminal" };
        foreach (var b in rules.Businesses)
            if (b == null || !ids.Add(b.Id) || !catalog.TryGet(b.Id, out _) || !models.Contains(b.Model)
                || b.Capacity is < 0 or > 200 || b.PriceMinor < 0 || b.PriceMinor > 100_000_000
                || b.UpkeepMinor is < 0 or > 100_000_000 || b.StaffSalaryMinor is < 0 or > 100_000_000 || b.Staff is < 0 or > 20
                || b.OpenHour is < 0 or > 23 || b.CloseHour is < 1 or > 24 || b.CloseHour <= b.OpenHour
                || b.VisitSeconds is < 1 or > 604800 || b.ArrivalIntervalSeconds is < 30 or > 86400
                || b.StayDays is < 1 or > 30 || b.ServiceSeconds is < 1 or > 3600)
                throw new ContentValidationException("Invalid or duplicate business rule.");
        for (var i = 0; i < rules.Promotions.Length; i++)
        {
            var p = rules.Promotions[i];
            if (p == null || p.Rank != i + 2 || p.Population < 0 || p.DailyProfitMinor < 0 || p.Diversity is < 1 or > 20
                || p.Satisfaction is < 0 or > 100 || p.Cleanliness is < 0 or > 100 || p.HotelRooms < 0 || p.SuccessfulTrips < 0)
                throw new ContentValidationException("Invalid seven-rank promotion rule.");
        }
        return rules;
    }
    public BusinessRule? For(string id) => Businesses.FirstOrDefault(x => x.Id == id);
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
