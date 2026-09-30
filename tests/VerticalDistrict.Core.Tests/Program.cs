using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;

namespace VerticalDistrict.Core.Tests;

internal static class Program
{
    private const long GenerousCash = 2_000_000_000;
    private static ContentCatalog _catalog = null!;
    private static string _catalogJson = "";
    private static readonly List<(string Name, Action Body)> Cases =
    [
        ("Catalogue loads unique, usable construction definitions", CatalogDefinitions),
        ("Constructor rejects invalid cash and ranks", ConstructorLimits),
        ("Ground and inclusive -10/249 floor boundaries", FloorBoundaries),
        ("All seven ranks enforce their data-driven floor caps", RankCaps),
        ("Floors require contiguous structural support", StructuralSupport),
        ("Room footprint requires every occupied slab", RoomNeedsAllFloors),
        ("Room width boundaries and extreme coordinates are safe", RoomWidthBoundaries),
        ("Variable room widths allow adjacency and reject overlap", VariableWidths),
        ("Multi-floor rooms reserve every footprint cell", MultiFloorCollisions),
        ("Rooms can occupy valid basement and top floors", RoomVerticalBoundaries),
        ("Validation never changes authoritative state", ValidationIsReadOnly),
        ("Failed commands preserve state and do not consume entity IDs", FailedCommandsAreAtomic),
        ("Floor and room purchases enforce exact affordability", Affordability),
        ("Construction and demolition reconcile every ledger balance", ExactLedger),
        ("Room demolition refunds once and cannot duplicate cash", DemolitionRefund),
        ("Occupied and supporting floors cannot be demolished", FloorDemolitionDependencies),
        ("Refund of an odd minor-unit price rounds down", RefundRounding),
        ("Facility rank requirements prevent locked construction", FacilityRankGate),
        ("Malformed and incomplete catalogues are rejected", MalformedCatalogues),
        ("Duplicate IDs and invalid facility ranges are rejected", InvalidFacilities),
        ("Invalid rank and money definitions are rejected", InvalidProgressionAndCosts),
        ("Seeded command fuzz preserves deterministic construction invariants", SeededFuzz)
    ];

    private static int Main(string[] args)
    {
        try
        {
            var path = FindCatalog(args);
            _catalogJson = File.ReadAllText(path);
            _catalog = ContentCatalog.Load(_catalogJson);
            Console.WriteLine($"Vertical District construction tests — {path}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SETUP FAILED: {exception.Message}");
            return 2;
        }

        var failed = 0;
        foreach (var (name, body) in Cases)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}\n  {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{Cases.Count - failed}/{Cases.Count} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static string FindCatalog(string[] args)
    {
        if (args.Length > 1)
            throw new ArgumentException("Usage: dotnet run --project tests/VerticalDistrict.Core.Tests -- [catalogue-path]");
        if (args.Length == 1)
            return Path.GetFullPath(args[0]);

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                foreach (var relative in new[] { "Data/construction.catalog.json", "Data/facilities.json", "game/Data/construction.catalog.json", "game/Data/facilities.json" })
                {
                    var path = Path.Combine(directory.FullName, relative);
                    if (File.Exists(path))
                        return path;
                }
            }
        }

        throw new FileNotFoundException("Cannot find the construction catalogue. Supply its path as the sole argument.");
    }

    private static ConstructionWorld World(long cash = GenerousCash, int rank = 5, ContentCatalog? catalog = null)
        => new(catalog ?? _catalog, cash, rank);

    private static void CatalogDefinitions()
    {
        True(_catalog.Definitions.Count >= 2, "At least two room definitions are needed for construction.");
        Equal(_catalog.Definitions.Count, _catalog.Definitions.Select(definition => definition.Id).Distinct().Count(), "Unique facility IDs");
        Equal(7, _catalog.Ranks.Count, "Exactly seven ranks");
        True(_catalog.Definitions.Select(definition => definition.Width).Distinct().Count() > 1, "Variable-width construction data");
        True(_catalog.Definitions.Any(definition => definition.Height > 1), "Multi-floor construction data");
        foreach (var definition in _catalog.Definitions)
        {
            Equal(definition, _catalog.Get(definition.Id), $"Lookup of {definition.Id}");
            True(definition.CostMinor >= 0, "Nonnegative construction price");
            True(definition.Width > 0 && definition.Width <= 32, "Usable room width");
            True(definition.MinimumRank is >= 1 and <= 7, "Usable unlock rank");
        }
    }

    private static void ConstructorLimits()
    {
        Throws<ArgumentException>(() => World(-1), "Negative starting cash");
        Throws<ArgumentException>(() => World(rank: 0), "Rank zero");
        Throws<ArgumentException>(() => World(rank: 8), "Rank eight");
        Equal(0L, World(0).CashMinor, "Zero starting cash is valid");
    }

    private static void FloorBoundaries()
    {
        var world = World();
        True(world.Floors.Contains(0), "Ground exists initially");
        Equal(1, world.Floors.Count, "Only ground exists initially");
        BuildFloors(world, -10, 249);
        Equal(260, world.Floors.Count, "250 above-ground slabs plus 10 basements");
        foreach (var floor in new[] { -11, 250, int.MinValue, int.MaxValue })
            RejectedUnchanged(world, () => world.BuildFloor(floor), $"Out-of-world floor {floor}");
        RejectedUnchanged(world, () => world.BuildFloor(0), "Duplicate ground floor");
    }

    private static void RankCaps()
    {
        for (var rank = 1; rank <= 7; rank++)
        {
            var world = World(rank: rank);
            var cap = _catalog.GetRank(rank).AboveGroundFloorCap;
            True(cap is >= 1 and <= 250, $"Rank {rank} cap is within architecture");
            if (rank >= 5)
                Equal(250, cap, $"Rank {rank} unlocks full above-ground cap");
            BuildFloors(world, -10, cap - 1);
            Equal(cap + 10, world.Floors.Count, $"Rank {rank} exact slab count");
            RejectedUnchanged(world, () => world.BuildFloor(cap), $"Rank {rank} upper boundary");
        }
    }

    private static void StructuralSupport()
    {
        var world = World();
        RejectedUnchanged(world, () => world.BuildFloor(2), "Skipping first above-ground slab");
        RejectedUnchanged(world, () => world.BuildFloor(-2), "Skipping first basement slab");
        Succeeds(world.BuildFloor(1));
        Succeeds(world.BuildFloor(2));
        Succeeds(world.BuildFloor(-1));
        Succeeds(world.BuildFloor(-2));
        Equal(new[] { -2, -1, 0, 1, 2 }, world.Floors.OrderBy(floor => floor).ToArray(), "Contiguous structure");
    }

    private static void RoomNeedsAllFloors()
    {
        var world = World();
        var tall = _catalog.Definitions.First(definition => definition.Height > 1);
        RejectedUnchanged(world, () => world.BuildRoom(tall.Id, 0, 0), "Missing upper footprint slab");
        BuildFloors(world, 0, tall.Height - 1);
        Succeeds(world.BuildRoom(tall.Id, 0, 0));
        var single = SingleRoom();
        RejectedUnchanged(world, () => world.BuildRoom(single.Id, 0, -1), "Missing basement slab");
    }

    private static void RoomWidthBoundaries()
    {
        foreach (var definition in _catalog.Definitions)
        {
            var world = World(rank: 7);
            BuildFloors(world, 0, definition.Height - 1);
            foreach (var x in new[] { -1, 33 - definition.Width, 32, int.MinValue, int.MaxValue })
                RejectedUnchanged(world, () => world.BuildRoom(definition.Id, x, 0), $"{definition.Id} at x={x}");
            Succeeds(world.BuildRoom(definition.Id, 32 - definition.Width, 0));
        }
    }

    private static void VariableWidths()
    {
        var first = _catalog.Definitions.OrderBy(definition => definition.Width).First();
        var second = _catalog.Definitions.First(definition => definition.Width != first.Width && definition.Width + first.Width <= 32);
        var world = World(rank: 7);
        BuildFloors(world, 0, Math.Max(first.Height, second.Height) - 1);
        Succeeds(world.BuildRoom(first.Id, 0, 0));
        RejectedUnchanged(world, () => world.BuildRoom(second.Id, first.Width - 1, 0), "One-cell overlap");
        Succeeds(world.BuildRoom(second.Id, first.Width, 0));
        Equal(2, world.Rooms.Count, "Adjacent variable-width rooms");
        AssertWorldInvariants(world, GenerousCash);
    }

    private static void MultiFloorCollisions()
    {
        var tall = _catalog.Definitions.First(definition => definition.Height > 1);
        var single = SingleRoom();
        var world = World(rank: 7);
        BuildFloors(world, 0, tall.Height);
        Succeeds(world.BuildRoom(tall.Id, 0, 0));
        for (var floor = 0; floor < tall.Height; floor++)
            RejectedUnchanged(world, () => world.BuildRoom(single.Id, tall.Width - 1, floor), $"Tall footprint blocks floor {floor}");
        Succeeds(world.BuildRoom(single.Id, 0, tall.Height));

        var reverse = World(rank: 7);
        BuildFloors(reverse, 0, tall.Height - 1);
        Succeeds(reverse.BuildRoom(single.Id, 0, tall.Height - 1));
        RejectedUnchanged(reverse, () => reverse.BuildRoom(tall.Id, 0, 0), "Upper room blocks incoming tall footprint");
    }

    private static void RoomVerticalBoundaries()
    {
        var world = World(rank: 7);
        var single = SingleRoom();
        var tall = _catalog.Definitions.First(definition => definition.Height > 1);
        BuildFloors(world, -10, 249);
        Succeeds(world.BuildRoom(single.Id, 0, -10));
        Succeeds(world.BuildRoom(single.Id, 0, 0));
        Succeeds(world.BuildRoom(single.Id, 0, 249));
        foreach (var floor in new[] { -11, 250, int.MinValue, int.MaxValue })
            RejectedUnchanged(world, () => world.BuildRoom(single.Id, 8, floor), $"Invalid room floor {floor}");
        RejectedUnchanged(world, () => world.BuildRoom(tall.Id, 8, 249), "Multi-floor roof exceeds 249");
    }

    private static void ValidationIsReadOnly()
    {
        var world = World();
        var single = SingleRoom();
        var initial = Snapshot(world);
        Succeeds(world.ValidateFloor(1));
        Fails(world.ValidateFloor(2));
        Succeeds(world.ValidateRoom(single.Id, 0, 0));
        Fails(world.ValidateRoom("missing-definition", 0, 0));
        Fails(world.ValidateDemolishRoom(42));
        Fails(world.ValidateDemolishFloor(0));
        Equal(initial, Snapshot(world), "Validation preserves initial state");
        var room = Succeeds(world.BuildRoom(single.Id, 0, 0));
        Succeeds(world.BuildFloor(1));
        var built = Snapshot(world);
        Succeeds(world.ValidateDemolishRoom(room.EntityId!.Value));
        Succeeds(world.ValidateDemolishFloor(1));
        Equal(built, Snapshot(world), "Successful demolition previews preserve state");
    }

    private static void FailedCommandsAreAtomic()
    {
        var world = World();
        var control = World();
        var single = SingleRoom();
        foreach (var current in new[] { world, control })
            Succeeds(current.BuildRoom(single.Id, 0, 0));
        RejectedUnchanged(world, () => world.BuildRoom("unknown", 8, 0), "Unknown definition");
        RejectedUnchanged(world, () => world.BuildRoom(single.Id, 0, 0), "Occupied footprint");
        RejectedUnchanged(world, () => world.BuildRoom(single.Id, 8, 3), "Missing slab");
        RejectedUnchanged(world, () => world.BuildFloor(5), "Missing structural support");
        RejectedUnchanged(world, () => world.DemolishRoom(long.MaxValue), "Missing entity");
        RejectedUnchanged(world, () => world.DemolishFloor(0), "Protected ground");
        var afterFailures = Succeeds(world.BuildRoom(single.Id, single.Width, 0));
        var uninterrupted = Succeeds(control.BuildRoom(single.Id, single.Width, 0));
        Equal(uninterrupted.EntityId, afterFailures.EntityId, "Failed placements do not consume stable IDs");
        Equal(Snapshot(control), Snapshot(world), "Failed commands do not alter any exposed authoritative state");
    }

    private static void Affordability()
    {
        var floorPrice = World().FloorCostMinor;
        var poorFloor = World(floorPrice - 1);
        RejectedUnchanged(poorFloor, () => poorFloor.BuildFloor(1), "Floor costs one minor unit too much");
        var exactFloor = World(floorPrice);
        Succeeds(exactFloor.BuildFloor(1));
        Equal(0L, exactFloor.CashMinor, "Exact floor affordability");

        var single = SingleRoom();
        True(single.CostMinor > 0, "Fixture has a positive construction price");
        var poorRoom = World(single.CostMinor - 1);
        RejectedUnchanged(poorRoom, () => poorRoom.BuildRoom(single.Id, 0, 0), "Room costs one minor unit too much");
        var exactRoom = World(single.CostMinor);
        Succeeds(exactRoom.BuildRoom(single.Id, 0, 0));
        Equal(0L, exactRoom.CashMinor, "Exact room affordability");
        RejectedUnchanged(exactRoom, () => exactRoom.BuildRoom(single.Id, single.Width, 0), "No credit after spending last unit");
    }

    private static void ExactLedger()
    {
        var world = World();
        var single = SingleRoom();
        var floor = Succeeds(world.BuildFloor(1));
        Equal(world.FloorCostMinor, floor.CostMinor, "Floor command debit");
        var room = Succeeds(world.BuildRoom(single.Id, 0, 1));
        Equal(single.CostMinor, room.CostMinor, "Room command debit");
        var demolition = Succeeds(world.DemolishRoom(room.EntityId!.Value));
        Equal(-(single.CostMinor / 2), demolition.CostMinor, "Refund command is a negative debit");
        Succeeds(world.DemolishFloor(1));
        Equal(GenerousCash - world.FloorCostMinor - single.CostMinor + single.CostMinor / 2, world.CashMinor, "Exact final cash");
        True(world.Ledger.Any(entry => entry.EntityId == room.EntityId && entry.AmountMinor == -single.CostMinor), "Room purchase ledger links entity and debit");
        True(world.Ledger.Any(entry => entry.EntityId == room.EntityId && entry.AmountMinor == single.CostMinor / 2), "Room refund ledger links entity and credit");
        AssertWorldInvariants(world, GenerousCash);
    }

    private static void DemolitionRefund()
    {
        var world = World();
        var single = SingleRoom();
        var room = Succeeds(world.BuildRoom(single.Id, 0, 0));
        Succeeds(world.DemolishRoom(room.EntityId!.Value));
        Equal(0, world.Rooms.Count, "Demolition removes room");
        Equal(GenerousCash - single.CostMinor + single.CostMinor / 2, world.CashMinor, "Half-price refund");
        RejectedUnchanged(world, () => world.DemolishRoom(room.EntityId.Value), "Repeated demolition cannot refund twice");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var before = world.CashMinor;
            var next = Succeeds(world.BuildRoom(single.Id, 0, 0));
            True(next.EntityId > room.EntityId, "Entity IDs remain unique after demolition");
            Succeeds(world.DemolishRoom(next.EntityId!.Value));
            True(world.CashMinor <= before, "Build/demolish cycle cannot create money");
        }
    }

    private static void FloorDemolitionDependencies()
    {
        var world = World();
        BuildFloors(world, -2, 2);
        RejectedUnchanged(world, () => world.DemolishFloor(1), "Upper floor depends on lower support");
        RejectedUnchanged(world, () => world.DemolishFloor(-1), "Deeper basement depends on inward support");
        var tall = _catalog.Definitions.First(definition => definition.Height > 1);
        BuildFloors(world, -2, tall.Height);
        var room = Succeeds(world.BuildRoom(tall.Id, 0, 1));
        RejectedUnchanged(world, () => world.DemolishFloor(tall.Height), "Upper footprint occupies otherwise outermost slab");
        Succeeds(world.DemolishRoom(room.EntityId!.Value));
        for (var floor = Math.Max(2, tall.Height); floor >= 1; floor--)
            Succeeds(world.DemolishFloor(floor));
        Succeeds(world.DemolishFloor(-2));
        Succeeds(world.DemolishFloor(-1));
        Equal(1, world.Floors.Count, "Only protected ground remains");
        RejectedUnchanged(world, () => world.DemolishFloor(0), "Ground cannot be removed");
        RejectedUnchanged(world, () => world.DemolishFloor(1), "Missing slab cannot be removed twice");
    }

    private static void RefundRounding()
    {
        var single = SingleRoom();
        var catalog = AlterCatalog(root => Facility(root, single.Id)["costMinor"] = 5);
        var world = World(5, catalog: catalog);
        var room = Succeeds(world.BuildRoom(single.Id, 0, 0));
        Succeeds(world.DemolishRoom(room.EntityId!.Value));
        Equal(2L, world.CashMinor, "Half of five rounds down to two minor units");
    }

    private static void FacilityRankGate()
    {
        var single = SingleRoom();
        var catalog = AlterCatalog(root => Facility(root, single.Id)["minimumRank"] = 7);
        var locked = World(rank: 6, catalog: catalog);
        RejectedUnchanged(locked, () => locked.BuildRoom(single.Id, 0, 0), "Rank-seven facility at rank six");
        Succeeds(World(rank: 7, catalog: catalog).BuildRoom(single.Id, 0, 0));
    }

    private static void MalformedCatalogues()
    {
        foreach (var json in new[] { "", "{", "null", "[]", "{}", "{\"schemaVersion\":99}" })
            Throws<ArgumentException>(() => ContentCatalog.Load(json), $"Invalid catalogue '{json}'");
        RejectCatalog(root => root["facilities"] = new JsonArray(), "Empty catalogue");
        RejectCatalog(root => root.Remove("ranks"), "Missing progression data");
        RejectCatalog(root => root["schemaVersion"] = 999, "Unsupported schema version");
    }

    private static void InvalidFacilities()
    {
        var id = SingleRoom().Id;
        RejectCatalog(root => root["facilities"]!.AsArray().Add(Facility(root, id).DeepClone()), "Duplicate facility ID");
        foreach (var (property, value) in new (string, int)[]
        {
            ("width", 0), ("width", 33), ("height", 0), ("height", 261),
            ("costMinor", -1), ("minimumRank", 0), ("minimumRank", 8)
        })
            RejectCatalog(root => Facility(root, id)[property] = value, $"Invalid {property}={value}");
        RejectCatalog(root => Facility(root, id)["id"] = "", "Empty facility ID");
        RejectCatalog(root => Facility(root, id).Remove("costMinor"), "Omitted construction price cannot imply a free room");
        foreach (var property in new[] { "dailyUpkeepMinor", "capacity", "staffRequired", "noise" })
            RejectCatalog(root => Facility(root, id)["operations"]!.AsObject().Remove(property), $"Missing planned operation field {property}");
    }

    private static void InvalidProgressionAndCosts()
    {
        RejectCatalog(root => root["ranks"]!.AsArray().RemoveAt(0), "Missing rank");
        RejectCatalog(root => root["ranks"]![1]!["rank"] = 1, "Duplicate rank");
        RejectCatalog(root => root["ranks"]![4]!["aboveGroundFloorCap"] = 249, "Rank five must unlock full cap");
        RejectCatalog(root => root["ranks"]![0]!["aboveGroundFloorCap"] = 251, "Rank cap above world height");
        RejectCatalog(root => root["floorConstructionCostMinor"] = -1, "Negative slab price");
        RejectCatalog(root => root["minorUnitsPerMajor"] = 0, "Zero minor-unit scale");
    }

    private static void SeededFuzz()
    {
        const int seed = 20260930;
        const int commandCount = 1_500;
        var random = new Random(seed);
        var world = World(rank: 7);
        var mirror = World(rank: 7);
        BuildFloors(world, -3, 12);
        BuildFloors(mirror, -3, 12);
        var successes = 0;
        var failures = 0;
        for (var index = 0; index < commandCount; index++)
        {
            var operation = random.Next(8);
            var floor = random.Next(-12, 28);
            var x = random.Next(-3, 35);
            var contentIndex = random.Next(_catalog.Definitions.Count + 1);
            var definitionId = contentIndex == _catalog.Definitions.Count ? "missing-content" : _catalog.Definitions[contentIndex].Id;
            var entityId = world.Rooms.Count > 0 && random.Next(4) != 0 ? world.Rooms[random.Next(world.Rooms.Count)].Id : long.MaxValue;
            CommandResult Apply(ConstructionWorld candidate) => operation switch
            {
                0 => candidate.BuildFloor(floor),
                1 => candidate.BuildRoom(definitionId, x, floor),
                2 => candidate.DemolishRoom(entityId),
                3 => candidate.DemolishFloor(floor),
                4 => candidate.ValidateFloor(floor),
                5 => candidate.ValidateRoom(definitionId, x, floor),
                6 => candidate.ValidateDemolishRoom(entityId),
                _ => candidate.ValidateDemolishFloor(floor)
            };

            var before = Snapshot(world);
            var version = world.TopologyVersion;
            var actual = Apply(world);
            var repeated = Apply(mirror);
            Equal(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(repeated), $"Seed {seed}, command {index} result");
            if (actual.Success)
                successes++;
            else
                failures++;
            if (!actual.Success || operation >= 4)
                Equal(before, Snapshot(world), $"Seed {seed}, command {index} must not mutate");
            else
                Equal(version + 1, world.TopologyVersion, $"Seed {seed}, command {index} invalidates topology once");
            Equal(Snapshot(world), Snapshot(mirror), $"Seed {seed}, command {index} deterministic state");
            AssertWorldInvariants(world, GenerousCash);
        }
        True(successes > 50 && failures > 50, $"Seed {seed} exercises successes ({successes}) and failures ({failures})");
    }

    private static void AssertWorldInvariants(ConstructionWorld world, long initialCash)
    {
        True(world.CashMinor >= 0, "Cash never falls below zero");
        True(world.Floors.Contains(0), "Ground remains present");
        foreach (var floor in world.Floors)
        {
            True(floor is >= -10 and <= 249, "Slab within architecture");
            if (floor != 0)
                True(world.Floors.Contains(floor - Math.Sign(floor)), "Every slab has inward support");
            if (floor >= 0)
                True(floor < _catalog.GetRank(world.Rank).AboveGroundFloorCap, "Slab respects rank");
        }
        var ids = new HashSet<long>();
        var cells = new HashSet<(int X, int Floor)>();
        foreach (var room in world.Rooms)
        {
            True(ids.Add(room.Id), "Room IDs are unique");
            var definition = _catalog.Get(room.DefinitionId);
            for (var floor = room.Floor; floor < room.Floor + definition.Height; floor++)
            {
                True(world.Floors.Contains(floor), "Every room footprint slab exists");
                for (var x = room.X; x < room.X + definition.Width; x++)
                {
                    True(x is >= 0 and < 32, "Room cell within horizontal bounds");
                    True(cells.Add((x, floor)), "Room footprints never overlap");
                }
            }
        }
        var balance = initialCash;
        long lastSequence = 0;
        long lastTimestamp = -1;
        foreach (var entry in world.Ledger)
        {
            True(entry.Sequence > lastSequence, "Ledger sequences strictly increase");
            True(entry.TimestampTicks >= lastTimestamp, "Ledger logical timestamps never reverse");
            True(!string.IsNullOrWhiteSpace(entry.Category), "Every transaction has a category");
            True(!string.IsNullOrWhiteSpace(entry.Description), "Every transaction has a description");
            balance = checked(balance + entry.AmountMinor);
            Equal(balance, entry.BalanceAfterMinor, "Auditable intermediate ledger balance");
            lastSequence = entry.Sequence;
            lastTimestamp = entry.TimestampTicks;
        }
        Equal(balance, world.CashMinor, "Ledger exactly reconciles current cash");
    }

    private static FacilityDefinition SingleRoom()
        => _catalog.Definitions.First(definition => definition.Height == 1 && definition.MinimumRank == 1 && definition.Width <= 16);

    private static void BuildFloors(ConstructionWorld world, int minimum, int maximum)
    {
        for (var floor = 1; floor <= maximum; floor++)
            if (!world.Floors.Contains(floor))
                Succeeds(world.BuildFloor(floor));
        for (var floor = -1; floor >= minimum; floor--)
            if (!world.Floors.Contains(floor))
                Succeeds(world.BuildFloor(floor));
    }

    private static JsonObject Facility(JsonObject root, string id)
        => root["facilities"]!.AsArray().Select(node => node!.AsObject()).Single(node => node["id"]!.GetValue<string>() == id);

    private static ContentCatalog AlterCatalog(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(_catalogJson)!.AsObject();
        mutate(root);
        return ContentCatalog.Load(root.ToJsonString());
    }

    private static void RejectCatalog(Action<JsonObject> mutate, string context)
        => Throws<ArgumentException>(() => AlterCatalog(mutate), context);

    private static string Snapshot(ConstructionWorld world)
        => JsonSerializer.Serialize(new
        {
            world.CashMinor, world.Rank, world.TopologyVersion,
            Floors = world.Floors.OrderBy(floor => floor), world.Rooms, world.Ledger
        });

    private static void RejectedUnchanged(ConstructionWorld world, Func<CommandResult> command, string context)
    {
        var before = Snapshot(world);
        Fails(command(), context);
        Equal(before, Snapshot(world), $"{context}: atomic rejection");
    }

    private static CommandResult Succeeds(CommandResult result)
    {
        True(result.Success, $"Expected successful command: {result.Message}");
        return result;
    }

    private static void Fails(CommandResult result, string context = "Command rejection")
    {
        True(!result.Success, $"{context}: unexpectedly succeeded");
        True(!string.IsNullOrWhiteSpace(result.Message), $"{context}: failure must explain why");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}; expected {expected}, got {actual}.");
    }

    private static void Equal(int[] expected, int[] actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message}; expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
    }

    private static void Throws<TException>(Action body, string context) where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{context}: expected {typeof(TException).Name}.");
    }
}
