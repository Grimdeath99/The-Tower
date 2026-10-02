namespace VerticalDistrict.Core.Simulation;

/// <summary>Provisional, validated management tuning. Existing definitions migrate to these defaults.</summary>
public sealed record ManagementRules
{
    public int LeaseDays { get; init; } = 7;
    public int TenantGraceDays { get; init; } = 2;
    public int MinContractDemand { get; init; } = 40;
    public int FoodPatienceSeconds { get; init; } = 600;
    public int HotelArrivalTimeoutSeconds { get; init; } = 1200;
    public int BusinessHistoryLimit { get; init; } = 256;
    public int ServiceCleanThreshold { get; init; } = 85;
    public int ServiceRepairThreshold { get; init; } = 85;
    public int HourlyConditionLoss { get; init; } = 1;
    public int HourlyCleanlinessLoss { get; init; } = 1;
    public int OccupiedHourlyCleanlinessLoss { get; init; } = 2;
    public long CleaningCostMinor { get; init; } = 100;
    public int ServiceRetrySeconds { get; init; } = 120;
    public int ServiceHistoryLimit { get; init; } = 128;
    public int SatisfactionStepPerHour { get; init; } = 5;
    public int TravelComfortSeconds { get; init; } = 180;
    public int ComplaintHistoryLimit { get; init; } = 128;
    public int SatisfactionHistoryHours { get; init; } = 168;

    internal void Validate()
    {
        if (LeaseDays is < 1 or > 365 || TenantGraceDays is < 1 or > 30
            || MinContractDemand is < 1 or > 100 || FoodPatienceSeconds is < 30 or > 7200
            || HotelArrivalTimeoutSeconds is < 60 or > 7200 || BusinessHistoryLimit is < 16 or > 2048
            || ServiceCleanThreshold is < 35 or > 100 || ServiceRepairThreshold is < 30 or > 100
            || HourlyConditionLoss is < 0 or > 20 || HourlyCleanlinessLoss is < 0 or > 20
            || OccupiedHourlyCleanlinessLoss is < 0 or > 20 || CleaningCostMinor is < 0 or > 100_000_000
            || ServiceRetrySeconds is < 30 or > 3600 || ServiceHistoryLimit is < 16 or > 1024
            || SatisfactionStepPerHour is < 1 or > 20 || TravelComfortSeconds is < 30 or > 3600
            || ComplaintHistoryLimit is < 16 or > 1024 || SatisfactionHistoryHours is < 24 or > 720)
            throw new ContentValidationException("Invalid provisional management tuning.");
    }
}
