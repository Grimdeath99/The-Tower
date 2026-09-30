using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VerticalDistrict.Core.Persistence;

/// <summary>Only authoritative values belong here. Routes and UI state are rebuilt after loading.</summary>
public sealed record ConstructionSnapshot(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string ContentFingerprint,
    [property: JsonRequired] long StartingCashMinor,
    [property: JsonRequired] long CashMinor,
    [property: JsonRequired] int Rank,
    [property: JsonRequired] long NextRoomId,
    [property: JsonRequired] long CommandSequence,
    [property: JsonRequired] int TopologyVersion,
    [property: JsonRequired] int[] Floors,
    [property: JsonRequired] RoomInstance[] Rooms,
    [property: JsonRequired] LedgerEntry[] Ledger,
    [property: JsonRequired] ConstructionClockSnapshot? Clock = null);

public sealed record ConstructionClockSnapshot(
    [property: JsonRequired] long CurrentTick,
    [property: JsonRequired] long FirstSimulationSequence);

public sealed class SaveValidationException(string message) : ArgumentException(message);

public static class ContentFingerprint
{
    /// <summary>Includes all immutable catalogue fields, with stable ordering by content ID.</summary>
    public static string ForCatalog(ContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            catalog.SchemaVersion,
            catalog.CurrencyCode,
            catalog.MinorUnitsPerMajor,
            catalog.FloorConstructionCostMinor,
            Ranks = catalog.Ranks.OrderBy(rank => rank.Rank),
            Facilities = catalog.Definitions.OrderBy(definition => definition.Id, StringComparer.Ordinal)
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public static class ConstructionSaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static string Serialize(ConstructionWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return JsonSerializer.Serialize(world.CaptureSnapshot(), Options);
    }

    public static ConstructionWorld Deserialize(ContentCatalog catalog, string json)
    {
        try
        {
            StrictJson.Validate(json);
            if (JsonNode.Parse(json) is JsonObject root && UpgradeLegacySnapshot(root)) json = root.ToJsonString();
            var snapshot = JsonSerializer.Deserialize<ConstructionSnapshot>(json, Options)
                ?? throw new SaveValidationException("The save is empty.");
            return ConstructionWorld.FromSnapshot(catalog, snapshot);
        }
        catch (JsonException error)
        {
            throw new SaveValidationException($"Invalid save JSON: {error.Message}");
        }
    }

    /// <summary>Version 1 stored structural command ordinals. Preserve them exactly and record no adopted clock.</summary>
    internal static bool UpgradeLegacySnapshot(JsonObject snapshot)
    {
        if (snapshot["schemaVersion"] is not JsonValue value || !value.TryGetValue<int>(out var version) || version != 1)
            return false;
        if (snapshot.ContainsKey("clock")) throw new SaveValidationException("Construction schema 1 cannot contain clock metadata.");
        snapshot["schemaVersion"] = 2;
        snapshot["clock"] = null;
        return true;
    }
}

internal static class StrictJson
{
    internal static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        RequireUniqueNames(document.RootElement);
    }

    private static void RequireUniqueNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
                RequireUniqueNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) RequireUniqueNames(child);
        }
    }
}
