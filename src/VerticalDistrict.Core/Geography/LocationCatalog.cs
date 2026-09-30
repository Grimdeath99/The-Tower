using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VerticalDistrict.Core.Geography;

public enum SubwayEligibility { Unknown, Eligible, Ineligible }

/// <summary>A fictional building parcel, not a promise of a real property or station connection.</summary>
public sealed record SiteProfile(string Id, string Name, bool SubwayConnection, bool Waterfront);
public sealed record NetworkSource(string Publisher, string Title, string Url, string CheckedOn, string Evidence);
public sealed record DemandModifiers(decimal Residential, decimal Office, decimal Retail, decimal Hotel, string Status);
public sealed record LocationDefinition(string Id, string Name, string GeographicScope,
    SubwayEligibility SubwayNetwork, string NetworkNotes, IReadOnlyList<SiteProfile> Sites,
    DemandModifiers Demand, IReadOnlyList<NetworkSource> Sources)
{
    public bool HasOperatingSubway => SubwayNetwork == SubwayEligibility.Eligible;
    public SiteProfile GetSite(string id) => Sites.FirstOrDefault(s => s.Id == id)
        ?? throw new KeyNotFoundException($"Unknown site '{id}' in {Name}.");
}

/// <summary>
/// Immutable, release-pinned geography. These checks authorize only geography and rank;
/// they do not create facilities, arrivals, passengers, revenue, or transport connections.
/// </summary>
public sealed class LocationCatalog
{
    private static readonly string[] RequiredIds = ["tokyo", "osaka", "seoul", "dubai", "abu-dhabi",
        "new-york", "shanghai", "hong-kong", "beijing", "las-vegas", "san-francisco", "chicago", "hawaii"];
    private readonly Dictionary<string, LocationDefinition> _byId;
    public int SchemaVersion { get; }
    public string SnapshotDate { get; }
    public int SubwayMinimumRank { get; }
    public int DockMinimumRank { get; }
    public string RankGateProvenance { get; }
    public IReadOnlyList<LocationDefinition> Locations { get; }

    private LocationCatalog(CatalogData data, LocationDefinition[] locations)
    {
        SchemaVersion = data.SchemaVersion;
        SnapshotDate = data.SnapshotDate!;
        SubwayMinimumRank = data.SubwayMinimumRank;
        DockMinimumRank = data.DockMinimumRank;
        RankGateProvenance = data.RankGateProvenance!;
        Locations = Array.AsReadOnly(locations);
        _byId = locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
    }

    public LocationDefinition Get(string id) => id is not null && _byId.TryGetValue(id, out var location)
        ? location : throw new KeyNotFoundException($"Unknown location '{id}'.");

    public CommandResult ValidateSubway(string locationId, string siteId, int rank, bool relaxRankGate = false)
    {
        var validation = FindSite(locationId, siteId, rank, out var location, out var site);
        if (!validation.Success) return validation;
        if (location!.SubwayNetwork == SubwayEligibility.Unknown)
            return new(false, $"Subway eligibility for {location.Name} is unverified in the {SnapshotDate} snapshot.");
        if (!location.HasOperatingSubway)
            return new(false, $"{location.Name} has no qualifying subway network in this release snapshot.");
        if (!site!.SubwayConnection)
            return new(false, "This fictional site has no subway connection.");
        if (!relaxRankGate && rank < SubwayMinimumRank)
            return new(false, $"Subway connection requires rank {SubwayMinimumRank}.");
        return new(true, "Location, fictional site, and rank permit a subway connection.");
    }

    public CommandResult ValidateDock(string locationId, string siteId, int rank, bool relaxRankGate = false)
    {
        var validation = FindSite(locationId, siteId, rank, out var location, out var site);
        if (!validation.Success) return validation;
        if (location!.Id != "hawaii") return new(false, "Docks and boat terminals are available only in Hawaii.");
        if (!site!.Waterfront) return new(false, "Docks require a compatible fictional waterfront site.");
        if (!relaxRankGate && rank < DockMinimumRank)
            return new(false, $"Optional docks require rank {DockMinimumRank}.");
        return new(true, "Location, fictional waterfront site, and rank permit an optional dock.");
    }

    private CommandResult FindSite(string locationId, string siteId, int rank,
        out LocationDefinition? location, out SiteProfile? site)
    {
        location = null;
        site = null;
        if (rank is < 1 or > 7) return new(false, "Rank must be between 1 and 7.");
        if (locationId is null || !_byId.TryGetValue(locationId, out location)) return new(false, "Unknown location.");
        site = location.Sites.FirstOrDefault(s => s.Id == siteId);
        return site is null ? new(false, "Unknown fictional site for this location.") : new(true, "Valid location and site.");
    }

    public static LocationCatalog Load(string json)
    {
        Require(!string.IsNullOrWhiteSpace(json) && json.Length <= 1_000_000, "Location JSON is empty or too large.");
        CatalogData data;
        try
        {
            data = JsonSerializer.Deserialize<CatalogData>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter<SubwayEligibility>(allowIntegerValues: false) }
            }) ?? throw new ContentValidationException("The location catalog is null.");
        }
        catch (JsonException error) { throw new ContentValidationException($"Invalid location JSON: {error.Message}"); }
        Require(data.SchemaVersion == 1, "Unsupported location schema version; expected 1.");
        Require(IsDate(data.SnapshotDate), "Location snapshot date must use yyyy-MM-dd.");
        Require(data.SubwayMinimumRank is >= 1 and <= 7 && data.DockMinimumRank is >= 1 and <= 7,
            "Transport rank gates must be between 1 and 7.");
        Require(!string.IsNullOrWhiteSpace(data.RankGateProvenance), "Rank gates need provenance.");
        Require(data.Locations is { Length: 13 }, "Exactly thirteen location definitions are required.");
        var locations = new List<LocationDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.Locations!)
        {
            Require(entry is not null, "A location definition is null.");
            var item = entry!;
            Require(item.Id is not null && RequiredIds.Contains(item.Id, StringComparer.Ordinal) && ids.Add(item.Id),
                $"Missing, duplicate, or unsupported location ID '{item.Id}'.");
            Require(HasText(item.Name) && HasText(item.GeographicScope) && HasText(item.NetworkNotes),
                $"{item.Id}: name, geographic scope, and network notes are required.");
            Require(item.Sites is { Length: > 0 }, $"{item.Id}: at least one fictional site is required.");
            var sites = new List<SiteProfile>();
            var siteIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var site in item.Sites!)
            {
                Require(site is not null && site.Id is not null && Regex.IsMatch(site.Id, "^[a-z][a-z0-9-]*$")
                    && siteIds.Add(site.Id) && HasText(site.Name), $"{item.Id}: invalid or duplicate fictional site.");
                // Site suitability is an independent fictional fact; network permission is checked separately.
                sites.Add(new SiteProfile(site!.Id!, site.Name!, site.SubwayConnection, site.Waterfront));
            }
            Require(item.Demand is not null && item.Demand.Residential == 1 && item.Demand.Office == 1
                && item.Demand.Retail == 1 && item.Demand.Hotel == 1 && item.Demand.Status == "ProvisionalNeutral",
                $"{item.Id}: only provisional neutral demand metadata is supported; operating demand is not implemented.");
            Require(item.Sources is { Length: > 0 }, $"{item.Id}: authoritative source provenance is required.");
            var sources = new List<NetworkSource>();
            foreach (var source in item.Sources!)
            {
                Require(source is not null && HasText(source.Publisher) && HasText(source.Title) && HasText(source.Evidence)
                    && source.CheckedOn == data.SnapshotDate && Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme == "https", $"{item.Id}: source needs a publisher, title, HTTPS URL, evidence, and snapshot check date.");
                sources.Add(source!);
            }
            locations.Add(new LocationDefinition(item.Id!, item.Name!, item.GeographicScope!, item.SubwayNetwork,
                item.NetworkNotes!, sites.AsReadOnly(), item.Demand!, sources.AsReadOnly()));
        }
        return new LocationCatalog(data, locations.ToArray());
    }

    private static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text);
    private static bool IsDate(string? date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out _);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ContentValidationException(message);
    }

    private sealed record CatalogData(int SchemaVersion, string? SnapshotDate,
        [property: JsonRequired] int SubwayMinimumRank, [property: JsonRequired] int DockMinimumRank,
        string? RankGateProvenance, LocationData?[]? Locations);
    private sealed record LocationData(string? Id, string? Name, string? GeographicScope,
        [property: JsonRequired] SubwayEligibility SubwayNetwork, string? NetworkNotes,
        SiteData?[]? Sites, DemandModifiers? Demand, NetworkSource?[]? Sources);
    private sealed record SiteData(string? Id, string? Name, [property: JsonRequired] bool SubwayConnection,
        [property: JsonRequired] bool Waterfront);
}
