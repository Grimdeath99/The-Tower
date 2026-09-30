using System.Text.Json.Serialization;
using VerticalDistrict.Core.Persistence;

namespace VerticalDistrict.Core;

public sealed record CommandResult(bool Success, string Message, long CostMinor = 0, long? EntityId = null);
public sealed record RoomInstance(
    [property: JsonRequired] long Id,
    [property: JsonRequired] string DefinitionId,
    [property: JsonRequired] int X,
    [property: JsonRequired] int Floor);

/// <summary>
/// AmountMinor is a signed cash change. Construction entries use the adopted simulation clock,
/// or command sequence time in standalone worlds. UsesSimulationTimestamp identifies old ordinal
/// entries after migration; their original timestamps are never fabricated or rewritten.
/// </summary>
public sealed record LedgerEntry(
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] long TimestampTicks,
    [property: JsonRequired] string Category,
    [property: JsonRequired] long? EntityId,
    [property: JsonRequired] int? Floor,
    [property: JsonRequired] long AmountMinor,
    [property: JsonRequired] long BalanceAfterMinor,
    [property: JsonRequired] string Description);

/// <summary>
/// Authoritative construction and cash ledger. Slabs, room footprints, and a reserved corridor layer
/// are separate: slabs support rooms; room footprints cannot overlap; corridor space is implicit and
/// may overlay a footprint. Other simulation systems post operating transactions through the ledger
/// without changing structural topology. The world is owned and commanded by one simulation thread.
/// </summary>
public sealed partial class ConstructionWorld
{
    public const int Width = 32;
    public const int MinFloor = -10;
    public const int MaxFloor = 249;
    private readonly List<int> _floors = [0];
    private readonly List<RoomInstance> _rooms = [];
    private readonly List<LedgerEntry> _ledger = [];
    private long _nextRoomId = 1;
    private long _commandSequence;
    private ConstructionClockSnapshot? _clock;

    public ContentCatalog Catalog { get; }
    public long StartingCashMinor { get; }
    public long CashMinor { get; private set; }
    public int Rank { get; private set; }
    public int TopologyVersion { get; private set; }
    public long? SimulationClockTicks => _clock?.CurrentTick;
    public long FloorCostMinor => Catalog.FloorConstructionCostMinor;
    public IReadOnlyCollection<int> Floors { get; }
    public IReadOnlyList<RoomInstance> Rooms { get; }
    public IReadOnlyList<LedgerEntry> Ledger { get; }

    public ConstructionWorld(ContentCatalog catalog, long startingCash = 250_000_000, int rank = 1)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (startingCash < 0) throw new ArgumentOutOfRangeException(nameof(startingCash), "Starting cash cannot be negative.");
        catalog.GetRank(rank);
        Catalog = catalog;
        StartingCashMinor = startingCash;
        CashMinor = startingCash;
        Rank = rank;
        Floors = _floors.AsReadOnly();
        Rooms = _rooms.AsReadOnly();
        Ledger = _ledger.AsReadOnly();
    }

    public CommandResult ValidateFloor(int floor)
    {
        var bounds = ValidateFloorBounds(floor);
        if (!bounds.Success) return bounds;
        if (_floors.Contains(floor)) return Fail("A floor slab already exists here.");
        var supportingFloor = floor > 0 ? floor - 1 : floor + 1;
        if (!_floors.Contains(supportingFloor)) return Fail($"Build supporting floor {supportingFloor} first.");
        return ValidateTransaction(FloorCostMinor, "Build floor slab.");
    }

    public CommandResult BuildFloor(int floor)
    {
        var result = ValidateFloor(floor);
        if (!result.Success) return result;
        var transaction = PrepareTransaction("Construction.Floor", null, floor, -result.CostMinor, $"Built floor {floor}.");
        _floors.Insert(~_floors.BinarySearch(floor), floor);
        Commit(transaction);
        return result with { Message = $"Built floor {floor}." };
    }

    public CommandResult ValidateRoom(string definitionId, int x, int floor)
    {
        if (string.IsNullOrWhiteSpace(definitionId) || !Catalog.TryGet(definitionId, out var definition)) return Fail("Unknown facility definition.");
        var room = definition!;
        if (Rank < room.MinimumRank) return Fail($"{room.Name} requires rank {room.MinimumRank}.");
        // Use long before adding to reject hostile/extreme input without integer wrapping.
        if (x < 0 || (long)x + room.Width > Width) return Fail($"Room must fit within columns 0 to {Width - 1}.");
        var bounds = ValidateFloorBounds(floor);
        if (!bounds.Success) return bounds;
        var topFloor = (long)floor + room.Height - 1;
        if (topFloor > MaxFloor || topFloor >= Catalog.GetRank(Rank).AboveGroundFloorCap)
            return Fail($"The full room footprint exceeds rank {Rank}'s floor limit.");
        for (var level = floor; level <= topFloor; level++)
            if (!_floors.Contains(level)) return Fail($"Build floor {level} beneath the full room footprint first.");
        foreach (var placed in _rooms)
        {
            var existing = Catalog.Get(placed.DefinitionId);
            if (x < placed.X + existing.Width && x + room.Width > placed.X
                && floor < placed.Floor + existing.Height && floor + room.Height > placed.Floor)
                return Fail($"Room footprint overlaps {existing.Name} #{placed.Id}.");
        }
        if (_nextRoomId == long.MaxValue) return Fail("No additional room IDs are available.");
        return ValidateTransaction(room.CostMinor, $"Build {room.Name} structural shell.");
    }

    public CommandResult BuildRoom(string definitionId, int x, int floor)
    {
        var result = ValidateRoom(definitionId, x, floor);
        if (!result.Success) return result;
        var room = new RoomInstance(_nextRoomId, definitionId, x, floor);
        var nextRoomId = checked(_nextRoomId + 1);
        var transaction = PrepareTransaction("Construction.Room", room.Id, floor, -result.CostMinor, $"Built {Catalog.Get(definitionId).Name} #{room.Id}.");
        _rooms.Add(room);
        _nextRoomId = nextRoomId;
        Commit(transaction);
        return result with { Message = transaction.Description, EntityId = room.Id };
    }

    public CommandResult ValidateDemolishRoom(long id)
    {
        var room = _rooms.Find(r => r.Id == id);
        if (room is null) return Fail("This room no longer exists.");
        // Salvage is rounded down in minor units, once, on removing the actual instance.
        var refund = Catalog.Get(room.DefinitionId).CostMinor / 2;
        return ValidateTransaction(-refund, "Demolish room and recover 50% of construction cost.") with { EntityId = id };
    }

    public CommandResult DemolishRoom(long id)
    {
        var result = ValidateDemolishRoom(id);
        if (!result.Success) return result;
        var index = _rooms.FindIndex(r => r.Id == id);
        var room = _rooms[index];
        var transaction = PrepareTransaction("Demolition.Room", id, room.Floor, -result.CostMinor, $"Demolished {Catalog.Get(room.DefinitionId).Name} #{id}.");
        _rooms.RemoveAt(index);
        Commit(transaction);
        return result with { Message = transaction.Description };
    }

    public CommandResult ValidateDemolishFloor(int floor)
    {
        if (floor == 0) return Fail("The ground floor is the permanent structural foundation.");
        if (!_floors.Contains(floor)) return Fail("There is no floor slab here.");
        if (_rooms.Any(room => floor >= room.Floor && floor < room.Floor + Catalog.Get(room.DefinitionId).Height))
            return Fail("Demolish every room touching this floor first.");
        if (_floors.Any(other => floor > 0 ? other > floor : other < floor))
            return Fail("This slab supports floors farther from ground. Remove those floors first.");
        return ValidateTransaction(0, "Demolish slab. Slabs have no salvage refund.");
    }

    public CommandResult DemolishFloor(int floor)
    {
        var result = ValidateDemolishFloor(floor);
        if (!result.Success) return result;
        var transaction = PrepareTransaction("Demolition.Floor", null, floor, 0, $"Demolished floor {floor}.");
        _floors.Remove(floor);
        Commit(transaction);
        return result with { Message = transaction.Description };
    }

    /// <summary>Posts an operating receipt or expense without invalidating structural routes.</summary>
    public CommandResult ApplyOperatingTransaction(long amountMinor, long timestampTicks, string category,
        long? entityId, string description)
    {
        if (timestampTicks < 0) return Fail("Transaction time cannot be negative.");
        if (_clock is not null && timestampTicks != _clock.CurrentTick)
            return Fail("Transaction time must match the adopted simulation clock.");
        if (string.IsNullOrWhiteSpace(category) || category.StartsWith("Construction.", StringComparison.Ordinal)
            || category.StartsWith("Demolition.", StringComparison.Ordinal))
            return Fail("Operating transactions require a non-construction category.");
        if (entityId is <= 0) return Fail("Entity references must be positive IDs.");
        if (string.IsNullOrWhiteSpace(description)) return Fail("Transaction description is required.");
        LedgerEntry transaction;
        try
        {
            transaction = new LedgerEntry(checked(_commandSequence + 1), timestampTicks, category, entityId,
                null, amountMinor, checked(CashMinor + amountMinor), description);
        }
        catch (OverflowException)
        {
            return Fail("This transaction would exceed the supported accounting range.");
        }
        Commit(transaction, structuralChange: false);
        return new CommandResult(true, description, EntityId: entityId);
    }

    /// <summary>Only the progression evaluator should call this after satisfying its requirements.</summary>
    public CommandResult PromoteRank(int newRank)
    {
        if (newRank is < 1 or > 7) return Fail("Rank must be between 1 and 7.");
        if (newRank <= Rank) return Fail("Promotion requires a higher rank.");
        Catalog.GetRank(newRank);
        Rank = newRank;
        return new CommandResult(true, $"Promoted to rank {newRank}.");
    }

    /// <summary>
    /// Adopts a deterministic session clock, then advances it monotonically. Earlier structural
    /// timestamps remain command ordinals, identified by the saved first-simulation sequence.
    /// </summary>
    public void SetSimulationClock(long timestampTicks)
    {
        if (timestampTicks < 0 || (_clock is not null && timestampTicks < _clock.CurrentTick))
            throw new ArgumentOutOfRangeException(nameof(timestampTicks), "Simulation time cannot be negative or move backwards.");
        _clock = _clock is null
            ? new ConstructionClockSnapshot(timestampTicks, checked(_commandSequence + 1))
            : _clock with { CurrentTick = timestampTicks };
    }

    public bool UsesSimulationTimestamp(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !IsStructuralCategory(entry.Category)
            || (_clock is not null && entry.Sequence >= _clock.FirstSimulationSequence);
    }

    private static bool IsStructuralCategory(string category) => category.StartsWith("Construction.", StringComparison.Ordinal)
        || category.StartsWith("Demolition.", StringComparison.Ordinal);

    private CommandResult ValidateFloorBounds(int floor)
    {
        if (floor < MinFloor || floor > MaxFloor) return Fail($"Floor must be between {MinFloor} and {MaxFloor} (ground is 0).");
        var cap = Catalog.GetRank(Rank).AboveGroundFloorCap;
        if (floor >= cap) return Fail($"Rank {Rank} supports {cap} above-ground floors, including ground (0 to {cap - 1}).");
        return new CommandResult(true, "Within structural bounds.");
    }

    private CommandResult ValidateTransaction(long costMinor, string message)
    {
        if (costMinor > 0 && costMinor > CashMinor) return new CommandResult(false, "Insufficient construction funds.", costMinor);
        try
        {
            _ = checked(CashMinor - costMinor);
            _ = checked(_commandSequence + 1);
            _ = checked(TopologyVersion + 1);
        }
        catch (OverflowException)
        {
            return Fail("This command would exceed the supported accounting or topology range.");
        }
        return new CommandResult(true, message, costMinor);
    }

    private LedgerEntry PrepareTransaction(string category, long? entityId, int floor, long amountMinor, string description)
    {
        var sequence = checked(_commandSequence + 1);
        var balance = checked(CashMinor + amountMinor);
        return new LedgerEntry(sequence, _clock?.CurrentTick ?? sequence, category, entityId, floor, amountMinor, balance, description);
    }

    private void Commit(LedgerEntry transaction, bool structuralChange = true)
    {
        _ledger.Add(transaction);
        CashMinor = transaction.BalanceAfterMinor;
        _commandSequence = transaction.Sequence;
        if (structuralChange) TopologyVersion = checked(TopologyVersion + 1);
    }

    private static CommandResult Fail(string message) => new(false, message);
}
