using VerticalDistrict.Core.Persistence;

namespace VerticalDistrict.Core;

public sealed partial class ConstructionWorld
{
    public ConstructionSnapshot CaptureSnapshot() => new(2, ContentFingerprint.ForCatalog(Catalog),
        StartingCashMinor, CashMinor, Rank, _nextRoomId, _commandSequence, TopologyVersion,
        _floors.ToArray(), _rooms.ToArray(), _ledger.ToArray(), _clock);

    /// <summary>Validates a detached candidate completely before returning a replacement world.</summary>
    public static ConstructionWorld FromSnapshot(ContentCatalog catalog, ConstructionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireSave(snapshot.SchemaVersion is 1 or 2, "Unsupported construction save schema; supported versions are 1 and 2.");
        RequireSave(snapshot.SchemaVersion != 1 || snapshot.Clock is null, "Construction schema 1 cannot contain clock metadata.");
        RequireSave(snapshot.ContentFingerprint == ContentFingerprint.ForCatalog(catalog), "Save content does not match the installed catalogue.");
        RequireSave(snapshot.StartingCashMinor >= 0, "Starting cash cannot be negative.");
        RequireSave(snapshot.Rank is >= 1 and <= 7, "Save rank is outside 1 through 7.");
        RequireSave(snapshot.NextRoomId > 0 && snapshot.CommandSequence >= 0 && snapshot.TopologyVersion >= 0,
            "Save IDs, command sequence, or topology version are invalid.");
        RequireSave(snapshot.Clock is null || (snapshot.Clock.CurrentTick >= 0 && snapshot.Clock.FirstSimulationSequence > 0
            && snapshot.Clock.FirstSimulationSequence - 1 <= snapshot.CommandSequence), "Save simulation clock or adoption sequence is invalid.");
        RequireSave(snapshot.Floors is not null && snapshot.Rooms is not null && snapshot.Ledger is not null,
            "Save floors, rooms, and ledger are required.");
        var floors = snapshot.Floors!.ToArray();
        var rooms = snapshot.Rooms!.ToArray();
        var ledger = snapshot.Ledger!.ToArray();
        RequireSave(floors.Length is >= 1 and <= MaxFloor - MinFloor + 1, "Save has an invalid number of floors.");
        var cap = catalog.GetRank(snapshot.Rank).AboveGroundFloorCap;
        RequireSave(floors.Contains(0) && floors.SequenceEqual(floors.Distinct().OrderBy(x => x)),
            "Save floors must be unique, sorted, and include ground.");
        foreach (var floor in floors)
        {
            RequireSave(floor >= MinFloor && floor <= MaxFloor && floor < cap, "Save floor exceeds world or rank limits.");
            RequireSave(floor == 0 || floors.Contains(floor > 0 ? floor - 1 : floor + 1), "Save contains an unsupported floor.");
        }
        var occupied = new HashSet<(int X, int Floor)>();
        var liveRooms = new Dictionary<long, RoomInstance>();
        long previousRoomId = 0;
        foreach (var room in rooms)
        {
            RequireSave(room is not null, "Save contains a null room.");
            RequireSave(room!.Id > previousRoomId && room.Id < snapshot.NextRoomId,
                "Save room IDs must be positive, unique, ordered, and below the next ID.");
            previousRoomId = room.Id;
            RequireSave(!string.IsNullOrWhiteSpace(room.DefinitionId) && catalog.TryGet(room.DefinitionId, out _),
                "Save contains an unknown room definition.");
            var definition = catalog.Get(room.DefinitionId);
            RequireSave(definition.MinimumRank <= snapshot.Rank, "Save contains a rank-locked room.");
            RequireSave(room.X >= 0 && (long)room.X + definition.Width <= Width, "Save room exceeds building width.");
            RequireSave(room.Floor >= MinFloor && (long)room.Floor + definition.Height - 1 <= MaxFloor
                && (long)room.Floor + definition.Height - 1 < cap, "Save room exceeds world or rank floor limits.");
            for (var floor = room.Floor; floor < room.Floor + definition.Height; floor++)
            {
                RequireSave(floors.Contains(floor), "Save room lacks support for its full footprint.");
                for (var x = room.X; x < room.X + definition.Width; x++)
                    RequireSave(occupied.Add((x, floor)), "Save room footprints overlap.");
            }
            liveRooms.Add(room.Id, room);
        }
        ValidateSavedLedger(catalog, snapshot, ledger, floors, liveRooms);

        var candidate = new ConstructionWorld(catalog, snapshot.StartingCashMinor, snapshot.Rank);
        candidate._floors.Clear();
        candidate._floors.AddRange(floors);
        candidate._rooms.AddRange(rooms);
        candidate._ledger.AddRange(ledger);
        candidate.CashMinor = snapshot.CashMinor;
        candidate._nextRoomId = snapshot.NextRoomId;
        candidate._commandSequence = snapshot.CommandSequence;
        candidate.TopologyVersion = snapshot.TopologyVersion;
        candidate._clock = snapshot.Clock;
        return candidate;
    }

    private static void ValidateSavedLedger(ContentCatalog catalog, ConstructionSnapshot snapshot,
        LedgerEntry[] ledger, int[] floors, Dictionary<long, RoomInstance> liveRooms)
    {
        RequireSave(snapshot.CommandSequence == ledger.LongLength, "Save command sequence does not match its ledger.");
        var balance = snapshot.StartingCashMinor;
        long sequence = 0;
        long nextRoomId = 1;
        var topology = 0;
        long previousSimulationStamp = 0;
        var historicFloors = new HashSet<int> { 0 };
        var historicRooms = new Dictionary<long, LedgerEntry>();
        foreach (var transaction in ledger)
        {
            RequireSave(transaction is not null, "Save contains a null ledger entry.");
            var entry = transaction!;
            RequireSave(entry.Sequence == ++sequence && entry.TimestampTicks >= 0,
                "Save ledger sequences must be consecutive and times cannot be negative.");
            RequireSave(!string.IsNullOrWhiteSpace(entry.Category) && !string.IsNullOrWhiteSpace(entry.Description)
                && entry.EntityId is not <= 0, "Save ledger metadata is invalid.");
            try { balance = checked(balance + entry.AmountMinor); }
            catch (OverflowException) { throw new SaveValidationException("Save ledger balance overflows."); }
            RequireSave(entry.BalanceAfterMinor == balance, "Save ledger balance does not reconcile.");
            var structural = IsStructuralCategory(entry.Category);
            var adoptedClock = snapshot.Clock is not null && entry.Sequence >= snapshot.Clock.FirstSimulationSequence;
            if (adoptedClock)
            {
                RequireSave(entry.TimestampTicks >= previousSimulationStamp && entry.TimestampTicks <= snapshot.Clock!.CurrentTick,
                    "Save ledger timestamps are outside the adopted simulation clock or move backwards.");
                previousSimulationStamp = entry.TimestampTicks;
            }
            if (!structural)
            {
                RequireSave(entry.Floor is null, "Operating entries cannot carry structural floor changes.");
                continue;
            }
            topology++;
            RequireSave(adoptedClock || entry.TimestampTicks == entry.Sequence,
                "Legacy construction timestamps must preserve command sequence time.");
            RequireSave(entry.Floor is >= MinFloor and <= MaxFloor && entry.Floor < catalog.GetRank(snapshot.Rank).AboveGroundFloorCap,
                "Save structural ledger has an invalid floor.");
            var floor = entry.Floor!.Value;
            switch (entry.Category)
            {
                case "Construction.Floor":
                    RequireSave(entry.EntityId is null && floor != 0 && !historicFloors.Contains(floor)
                        && historicFloors.Contains(floor > 0 ? floor - 1 : floor + 1)
                        && entry.AmountMinor == -catalog.FloorConstructionCostMinor && balance >= 0,
                        "Save floor construction ledger is inconsistent.");
                    historicFloors.Add(floor);
                    break;
                case "Demolition.Floor":
                    RequireSave(entry.EntityId is null && floor != 0 && historicFloors.Contains(floor)
                        && !historicFloors.Any(other => floor > 0 ? other > floor : other < floor)
                        && !historicRooms.Values.Any(room => room.Floor == floor) && entry.AmountMinor == 0,
                        "Save floor demolition ledger is inconsistent.");
                    historicFloors.Remove(floor);
                    break;
                case "Construction.Room":
                    RequireSave(entry.EntityId == nextRoomId && historicFloors.Contains(floor) && entry.AmountMinor <= 0
                        && catalog.Definitions.Any(definition => definition.CostMinor == -entry.AmountMinor)
                        && (entry.AmountMinor == 0 || balance >= 0), "Save room construction ledger is inconsistent.");
                    RequireSave(nextRoomId < long.MaxValue, "Save room IDs exceed their supported range.");
                    historicRooms.Add(nextRoomId++, entry);
                    break;
                case "Demolition.Room":
                    RequireSave(entry.EntityId is not null && historicRooms.TryGetValue(entry.EntityId.Value, out _),
                        "Save demolition references an absent room.");
                    var original = historicRooms[entry.EntityId!.Value];
                    RequireSave(original.Floor == entry.Floor && entry.AmountMinor == -(original.AmountMinor / 2),
                        "Save room salvage does not match its construction price.");
                    historicRooms.Remove(entry.EntityId.Value);
                    break;
                default:
                    throw new SaveValidationException("Save ledger uses an unknown construction category.");
            }
        }
        RequireSave(balance == snapshot.CashMinor, "Save cash does not match its ledger.");
        RequireSave(topology == snapshot.TopologyVersion, "Save topology does not match its structural changes.");
        RequireSave(nextRoomId == snapshot.NextRoomId, "Save next room ID does not match its construction history.");
        RequireSave(historicFloors.SetEquals(floors), "Save floors do not match their construction history.");
        RequireSave(historicRooms.Keys.ToHashSet().SetEquals(liveRooms.Keys), "Save rooms do not match their construction history.");
        foreach (var room in liveRooms.Values)
        {
            var original = historicRooms[room.Id];
            RequireSave(original.Floor == room.Floor && original.AmountMinor == -catalog.Get(room.DefinitionId).CostMinor,
                "Save room differs from its construction ledger.");
        }
    }

    private static void RequireSave(bool condition, string message)
    {
        if (!condition) throw new SaveValidationException(message);
    }
}
