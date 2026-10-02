using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VerticalDistrict.Core;

/// <summary>Immutable facility definitions; runtime balance is loaded separately by SimulationRules.</summary>
public sealed record FacilityDefinition(
    string Id, string Name, string Category, int Width, int Height,
    long CostMinor, int MinimumRank, string ColorHex, string Description,
    PlannedOperations Operations, ContentProvenance Provenance);

public sealed record PlannedOperations(
    string Model, string Status, long DailyUpkeepMinor, int Capacity, int StaffRequired,
    IReadOnlyList<string> Utilities, string OpeningHours, string AccessRule,
    int Noise, string PricingNotes);

public sealed record ContentProvenance(string Source, string TuningStatus, string VisualStatus);
public sealed record RankDefinition(int Rank, string Name, int AboveGroundFloorCap, string Provenance);

public sealed class ContentValidationException(string message) : ArgumentException(message);

public sealed class ContentCatalog
{
    private readonly IReadOnlyDictionary<string, FacilityDefinition> _byId;
    public int SchemaVersion { get; }
    public string CurrencyCode { get; }
    public int MinorUnitsPerMajor { get; }
    public long FloorConstructionCostMinor { get; }
    public IReadOnlyList<FacilityDefinition> Definitions { get; }
    public IReadOnlyList<RankDefinition> Ranks { get; }

    private ContentCatalog(CatalogData data, FacilityDefinition[] facilities, RankDefinition[] ranks)
    {
        SchemaVersion = data.SchemaVersion;
        CurrencyCode = data.CurrencyCode!;
        MinorUnitsPerMajor = data.MinorUnitsPerMajor;
        FloorConstructionCostMinor = data.FloorConstructionCostMinor;
        Definitions = Array.AsReadOnly(facilities);
        Ranks = Array.AsReadOnly(ranks);
        _byId = new ReadOnlyDictionary<string, FacilityDefinition>(facilities.ToDictionary(x => x.Id, StringComparer.Ordinal));
    }

    public FacilityDefinition Get(string id) => _byId.TryGetValue(id, out var definition)
        ? definition : throw new KeyNotFoundException($"Unknown facility '{id}'.");

    public bool TryGet(string id, out FacilityDefinition? definition) => _byId.TryGetValue(id, out definition);

    public RankDefinition GetRank(int rank) => rank is >= 1 and <= 7
        ? Ranks[rank - 1] : throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be between 1 and 7.");

    internal static bool IsSupportedOperatingModel(string? model) => model is
        "Office" or "Home" or "Hotel" or "Food" or "Shop" or "Cinema" or "Event" or "Condo"
        or "Advertising" or "Parking" or "Service" or "Utility" or "Security" or "Public" or "Terminal";

    internal static bool RequiresOccupantCapacity(string? model) => model is
        "Office" or "Home" or "Hotel" or "Food" or "Shop" or "Cinema" or "Event" or "Condo" or "Parking" or "Terminal";

    public static ContentCatalog Load(string json)
    {
        CatalogData data;
        try
        {
            data = JsonSerializer.Deserialize<CatalogData>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new ContentValidationException("The content catalog is empty.");
        }
        catch (JsonException error)
        {
            throw new ContentValidationException($"Invalid content JSON: {error.Message}");
        }

        Require(data.SchemaVersion == 1, "Unsupported catalog schema version; expected 1.");
        Require(data.CurrencyCode is not null && Regex.IsMatch(data.CurrencyCode, "^[A-Z]{3}$"), "Currency code must contain three uppercase letters.");
        Require(data.MinorUnitsPerMajor == 100, "This construction catalog uses 100 minor units per currency unit.");
        Require(data.FloorConstructionCostMinor > 0, "Floor construction cost must be positive.");
        Require(data.Ranks is { Length: 7 }, "Exactly seven rank definitions are required.");
        var ranks = data.Ranks!.Select(r => r ?? throw new ContentValidationException("A rank definition is null."))
            .OrderBy(r => r.Rank).ToArray();
        for (var i = 0; i < ranks.Length; i++)
        {
            var rank = ranks[i];
            Require(rank.Rank == i + 1, "Rank IDs must be unique and cover 1 through 7.");
            Require(!string.IsNullOrWhiteSpace(rank.Name) && !string.IsNullOrWhiteSpace(rank.Provenance), "Ranks require a name and provenance.");
            Require(rank.AboveGroundFloorCap is >= 1 and <= 250, "Rank floor caps must be between 1 and 250, including ground.");
            Require(i == 0 || rank.AboveGroundFloorCap >= ranks[i - 1].AboveGroundFloorCap, "Rank floor caps must not decrease.");
            Require(rank.Rank < 5 || rank.AboveGroundFloorCap == 250, "Rank 5 and later must unlock all 250 above-ground floors.");
        }

        Require(data.Facilities is { Length: > 0 }, "At least one facility definition is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var facilities = new List<FacilityDefinition>();
        foreach (var entry in data.Facilities!)
        {
            Require(entry is not null, "A facility definition is null.");
            var facility = entry!;
            Require(facility.Id is not null && Regex.IsMatch(facility.Id, "^[a-z][a-z0-9-]*$"), "Facility IDs must use lowercase letters, digits, and hyphens.");
            Require(ids.Add(facility.Id!), $"Duplicate facility ID '{facility.Id}'.");
            var label = facility.Id!;
            Require(!string.IsNullOrWhiteSpace(facility.Name) && !string.IsNullOrWhiteSpace(facility.Category)
                && !string.IsNullOrWhiteSpace(facility.Description), $"{label}: name, category, and description are required.");
            Require(facility.Width is >= 1 and <= ConstructionWorld.Width, $"{label}: width must fit the building.");
            Require(facility.Height is >= 1 and <= 260, $"{label}: height is outside the supported floor range.");
            Require(facility.CostMinor >= 0, $"{label}: construction cost cannot be negative.");
            Require(facility.MinimumRank is >= 1 and <= 7, $"{label}: minimum rank must be between 1 and 7.");
            Require(facility.ColorHex is not null && Regex.IsMatch(facility.ColorHex, "^#[0-9a-fA-F]{6}$"), $"{label}: color must be #RRGGBB.");
            Require(facility.Provenance is not null && !string.IsNullOrWhiteSpace(facility.Provenance.Source)
                && !string.IsNullOrWhiteSpace(facility.Provenance.TuningStatus)
                && !string.IsNullOrWhiteSpace(facility.Provenance.VisualStatus), $"{label}: source, tuning, and visual provenance are required.");
            Require(facility.Operations is not null, $"{label}: planned operations metadata is required.");
            var operation = facility.Operations!;
            Require(operation.Status is "Planned" or "Prototype", $"{label}: operations must declare Planned or Prototype status.");
            Require(!string.IsNullOrWhiteSpace(operation.Model) && !string.IsNullOrWhiteSpace(operation.OpeningHours)
                && !string.IsNullOrWhiteSpace(operation.AccessRule) && !string.IsNullOrWhiteSpace(operation.PricingNotes),
                $"{label}: operating model, opening hours, access rule, and pricing notes are required.");
            Require(IsSupportedOperatingModel(operation.Model), $"{label}: unsupported operating model '{operation.Model}'. Add its runtime implementation before registering content.");
            Require(operation.DailyUpkeepMinor >= 0 && operation.Capacity >= 0 && operation.StaffRequired >= 0
                && operation.Noise is >= 0 and <= 100, $"{label}: invalid planned operating ranges.");
            Require(!RequiresOccupantCapacity(operation.Model) || operation.Capacity > 0,
                $"{label}: operating model '{operation.Model}' requires positive capacity.");
            Require(operation.Utilities is not null && operation.Utilities.All(x => !string.IsNullOrWhiteSpace(x)), $"{label}: utilities must be a list of nonempty names.");
            Require(operation.Utilities!.Distinct(StringComparer.OrdinalIgnoreCase).Count() == operation.Utilities!.Length,
                $"{label}: utility references must be unique.");
            var planned = new PlannedOperations(operation.Model!, operation.Status!, operation.DailyUpkeepMinor,
                operation.Capacity, operation.StaffRequired, Array.AsReadOnly(operation.Utilities!.ToArray()),
                operation.OpeningHours!, operation.AccessRule!, operation.Noise, operation.PricingNotes!);
            facilities.Add(new FacilityDefinition(label, facility.Name!, facility.Category!, facility.Width, facility.Height,
                facility.CostMinor, facility.MinimumRank, facility.ColorHex!, facility.Description!, planned, facility.Provenance!));
        }
        return new ContentCatalog(data, facilities.ToArray(), ranks);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ContentValidationException(message);
    }

    private sealed record CatalogData(int SchemaVersion, string? CurrencyCode, int MinorUnitsPerMajor,
        long FloorConstructionCostMinor, RankDefinition?[]? Ranks, FacilityData?[]? Facilities);
    private sealed record FacilityData(string? Id, string? Name, string? Category, int Width, int Height,
        [property: JsonRequired] long CostMinor, int MinimumRank, string? ColorHex, string? Description,
        OperationsData? Operations, ContentProvenance? Provenance);
    private sealed record OperationsData(string? Model, string? Status,
        [property: JsonRequired] long DailyUpkeepMinor, [property: JsonRequired] int Capacity,
        [property: JsonRequired] int StaffRequired, string[]? Utilities, string? OpeningHours, string? AccessRule,
        [property: JsonRequired] int Noise, string? PricingNotes);
}
