using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Persistence.Tests;

internal static class Program
{
    private static ContentCatalog _catalog = null!;
    private static string _catalogJson = "";
    private static SimulationRules _rules = null!;
    private static LocationCatalog _locations = null!;

    private static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "Data", "construction.catalog.json");
        _catalogJson = File.ReadAllText(path);
        _catalog = ContentCatalog.Load(_catalogJson);
        var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        _rules = SimulationRules.Load(File.ReadAllText(Path.Combine(dataDirectory, "simulation.rules.json")), _catalog);
        _locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(dataDirectory, "locations.json")));
        (string Name, Action Test)[] cases =
        [
            ("Roundtrip preserves state, transaction sequence, and next room IDs", Roundtrip),
            ("Operating debt survives roundtrip without structural invalidation", OperatingDebt),
            ("Operating input validation and overflow are atomic", OperatingValidation),
            ("Rank promotion validates monotonic range without topology change", Promotion),
            ("Snapshot arrays are detached from their source world", DetachedSnapshot),
            ("Corrupt, truncated, null, missing, extra, and duplicate JSON are rejected", InvalidJson),
            ("Unsupported schemas and catalogue mismatches are rejected", SchemaAndFingerprint),
            ("Full catalogue metadata contributes to the fingerprint", FingerprintCoverage),
            ("Invalid floor structures and bounds are rejected", InvalidFloors),
            ("Unknown, overlapping, out-of-bounds, unsupported rooms are rejected", InvalidRooms),
            ("Invalid IDs, rank, and counters are rejected", InvalidCounters),
            ("Ledger balances, times, sequences, and entity histories are validated", InvalidLedger),
            ("Rejected load leaves the existing world usable and unchanged", LoadIsAtomic),
            ("Save replacement retains the last good document as backup", Backup),
            ("Invalid replacement cannot overwrite save or backup", FailedReplacement),
            ("Corrupt old save cannot replace the last good backup", CorruptOldFile),
            ("Storage failure preserves the prior save and backup", StorageFailure),
            ("Three autosave slots rotate without changing other slots", AutosaveSlots),
            ("Seeded mixed operations resume with identical authoritative state", DeterministicContinuation),
            ("Standalone ordinal history survives simulation clock adoption", AdoptSimulationClock),
            ("Clock metadata rejects backwards, future, missing, and invalid times", InvalidClock),
            ("Live structural commands use exact paused and running session seconds", SessionClock),
            ("Schema one construction migration preserves historical ordinal timestamps", LegacyConstructionClock),
            ("Legacy session migration preserves old stamps and continues with real seconds", LegacySessionClock),
            ("Live save restores exact clocks and rejects desynchronized metadata", SessionClockRestore),
            ("Live person and bank inspection preserves state and shows physical movement", InspectPhysicalPerson),
            ("Inspection distinguishes arrival duration, visit deadline, and exit destination", InspectPersonSchedule),
            ("Elevator inspection shows transfer stops rather than final destinations", InspectTransferBank),
            ("Service inspection distinguishes assigned target, working deadline, and return depot", InspectServicePerson)
        ];
        var failures = 0;
        foreach (var (name, test) in cases)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
        }
        Console.WriteLine($"{cases.Length - failures}/{cases.Length} persistence tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static ConstructionWorld World()
    {
        var world = new ConstructionWorld(_catalog, 2_000_000_000, rank: 5);
        Success(world.BuildFloor(-1));
        Success(world.BuildFloor(1));
        Success(world.BuildRoom("lobby", 0, 0));
        Success(world.BuildRoom("office", 0, 1));
        return world;
    }

    private static void Roundtrip()
    {
        var world = World();
        Success(world.DemolishRoom(2));
        Success(world.ApplyOperatingTransaction(54321, 987, "Revenue.Lease", 1, "Lease payment."));
        var restored = ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(world));
        Same(world, restored);
        Success(world.BuildRoom("office", 8, 1));
        Success(restored.BuildRoom("office", 8, 1));
        Same(world, restored);
        Equal(3L, restored.Rooms[^1].Id);
    }

    private static void OperatingDebt()
    {
        var world = World();
        var topology = world.TopologyVersion;
        Success(world.ApplyOperatingTransaction(-world.CashMinor - 10_000_000, 40, "Expense.Utilities", null, "Utility invoice."));
        Equal(-10_000_000L, world.CashMinor);
        Equal(topology, world.TopologyVersion);
        Require(!world.BuildFloor(2).Success);
        Success(world.DemolishRoom(2));
        Success(world.DemolishFloor(1));
        Same(world, ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(world)));
    }

    private static void OperatingValidation()
    {
        var world = World();
        var before = ConstructionSaveCodec.Serialize(world);
        Require(!world.ApplyOperatingTransaction(10, -1, "Revenue", null, "Payment").Success);
        Require(!world.ApplyOperatingTransaction(10, 0, "Construction.Floor", null, "Payment").Success);
        Require(!world.ApplyOperatingTransaction(10, 0, "Demolition.Other", null, "Payment").Success);
        Require(!world.ApplyOperatingTransaction(10, 0, " ", null, "Payment").Success);
        Require(!world.ApplyOperatingTransaction(10, 0, "Revenue", 0, "Payment").Success);
        Require(!world.ApplyOperatingTransaction(10, 0, "Revenue", null, " ").Success);
        Require(!world.ApplyOperatingTransaction(long.MaxValue, 0, "Revenue", null, "Payment").Success);
        Equal(before, ConstructionSaveCodec.Serialize(world));
        var poor = new ConstructionWorld(_catalog, 0);
        Success(poor.ApplyOperatingTransaction(long.MinValue, 0, "Expense", null, "Extreme expense."));
        var debt = ConstructionSaveCodec.Serialize(poor);
        Require(!poor.ApplyOperatingTransaction(-1, 1, "Expense", null, "Overflow.").Success);
        Equal(debt, ConstructionSaveCodec.Serialize(poor));
        Same(poor, ConstructionSaveCodec.Deserialize(_catalog, debt));
    }

    private static void Promotion()
    {
        var world = new ConstructionWorld(_catalog);
        var topology = world.TopologyVersion;
        Success(world.PromoteRank(2));
        Success(world.PromoteRank(7));
        Require(!world.PromoteRank(8).Success && !world.PromoteRank(0).Success
            && !world.PromoteRank(6).Success && !world.PromoteRank(7).Success);
        Equal(7, world.Rank);
        Equal(topology, world.TopologyVersion);
        Same(world, ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(world)));
    }

    private static void DetachedSnapshot()
    {
        var world = World();
        var before = ConstructionSaveCodec.Serialize(world);
        var snapshot = world.CaptureSnapshot();
        var restored = ConstructionWorld.FromSnapshot(_catalog, snapshot);
        snapshot.Floors[0] = -10;
        snapshot.Rooms[0] = snapshot.Rooms[0] with { X = 31 };
        snapshot.Ledger[0] = snapshot.Ledger[0] with { AmountMinor = 1 };
        Equal(before, ConstructionSaveCodec.Serialize(world));
        Equal(before, ConstructionSaveCodec.Serialize(restored));
    }

    private static void InvalidJson()
    {
        var json = ConstructionSaveCodec.Serialize(World());
        foreach (var bad in new[] { "", "{", "null", "[]", json[..^4], json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 2, \"schemaVersion\": 2") })
            RejectJson(bad);
        var root = JsonNode.Parse(json)!.AsObject();
        foreach (var key in root.Select(pair => pair.Key).ToArray())
        {
            var copy = root.DeepClone().AsObject();
            copy.Remove(key);
            RejectJson(copy.ToJsonString());
            copy = root.DeepClone().AsObject();
            copy[key] = null;
            if (key != "clock") RejectJson(copy.ToJsonString());
        }
        var extra = root.DeepClone().AsObject();
        extra["extra"] = 3;
        RejectJson(extra.ToJsonString());
        foreach (var collection in new[] { "rooms", "ledger" })
        {
            var element = root[collection]![0]!.AsObject();
            foreach (var key in element.Select(pair => pair.Key).ToArray())
            {
                var copy = root.DeepClone().AsObject();
                copy[collection]![0]!.AsObject().Remove(key);
                RejectJson(copy.ToJsonString());
            }
        }
    }

    private static void SchemaAndFingerprint()
    {
        var snapshot = World().CaptureSnapshot();
        Reject(snapshot with { SchemaVersion = 0 });
        Reject(snapshot with { SchemaVersion = 3 });
        Reject(snapshot with { ContentFingerprint = "unknown" });
        Reject(snapshot with { ContentFingerprint = null! });
    }

    private static void FingerprintCoverage()
    {
        var original = ContentFingerprint.ForCatalog(_catalog);
        var changed = JsonNode.Parse(_catalogJson)!;
        changed["facilities"]![0]!["description"] = "Changed descriptive metadata.";
        Require(original != ContentFingerprint.ForCatalog(ContentCatalog.Load(changed.ToJsonString())));
        changed = JsonNode.Parse(_catalogJson)!;
        changed["facilities"]![0]!["operations"]!["pricingNotes"] = "Changed operating metadata.";
        Require(original != ContentFingerprint.ForCatalog(ContentCatalog.Load(changed.ToJsonString())));
        changed = JsonNode.Parse(_catalogJson)!;
        var definitions = changed["facilities"]!.AsArray();
        var reordered = new JsonArray(definitions.Reverse().Select(node => node!.DeepClone()).ToArray());
        changed["facilities"] = reordered;
        Equal(original, ContentFingerprint.ForCatalog(ContentCatalog.Load(changed.ToJsonString())));
    }

    private static void InvalidFloors()
    {
        var snapshot = World().CaptureSnapshot();
        foreach (var bad in new[] { Array.Empty<int>(), new[] { -1, 1 }, new[] { -1, 0, 0, 1 }, new[] { 0, 2 }, new[] { -11, 0 }, new[] { 0, 250 }, new[] { 1, 0, -1 } })
            Reject(snapshot with { Floors = bad });
        Reject(snapshot with { Floors = null! });
    }

    private static void InvalidRooms()
    {
        var snapshot = World().CaptureSnapshot();
        foreach (var bad in new[]
        {
            snapshot.Rooms[1] with { DefinitionId = "unknown" },
            snapshot.Rooms[1] with { DefinitionId = null! },
            snapshot.Rooms[1] with { Floor = 0 },
            snapshot.Rooms[1] with { Floor = 2 },
            snapshot.Rooms[1] with { Floor = int.MaxValue },
            snapshot.Rooms[1] with { X = int.MaxValue },
            snapshot.Rooms[1] with { X = -1 },
            snapshot.Rooms[1] with { Id = 1 }
        }) Reject(snapshot with { Rooms = [snapshot.Rooms[0], bad] });
        Reject(snapshot with { Rooms = [null!] });
        Reject(snapshot with { Rooms = null! });
    }

    private static void InvalidCounters()
    {
        var snapshot = World().CaptureSnapshot();
        Reject(snapshot with { StartingCashMinor = -1 });
        Reject(snapshot with { Rank = 8 });
        Reject(snapshot with { NextRoomId = 2 });
        Reject(snapshot with { NextRoomId = 4 });
        Reject(snapshot with { CommandSequence = 0 });
        Reject(snapshot with { TopologyVersion = 0 });
        Reject(snapshot with { CashMinor = snapshot.CashMinor + 1 });
    }

    private static void InvalidLedger()
    {
        var snapshot = World().CaptureSnapshot();
        foreach (var bad in new[]
        {
            snapshot.Ledger[0] with { Sequence = 2 },
            snapshot.Ledger[0] with { TimestampTicks = -1 },
            snapshot.Ledger[0] with { AmountMinor = 1 },
            snapshot.Ledger[0] with { BalanceAfterMinor = 1 },
            snapshot.Ledger[0] with { Floor = 249 },
            snapshot.Ledger[0] with { EntityId = -1 },
            snapshot.Ledger[0] with { Category = "Construction.Unknown" },
            snapshot.Ledger[0] with { Category = "" },
            snapshot.Ledger[0] with { Description = "" }
        }) Reject(snapshot with { Ledger = [bad, .. snapshot.Ledger.Skip(1)] });
        Reject(snapshot with { Ledger = [null!] });
        Reject(snapshot with { Ledger = null! });
        var altered = snapshot.Ledger.ToArray();
        altered[^1] = altered[^1] with { EntityId = 40 };
        Reject(snapshot with { Ledger = altered });
    }

    private static void LoadIsAtomic()
    {
        var active = World();
        var before = ConstructionSaveCodec.Serialize(active);
        Reject(active.CaptureSnapshot() with { CashMinor = 1 });
        Equal(before, ConstructionSaveCodec.Serialize(active));
        Success(active.BuildRoom("studio", 8, 1));
    }

    private static bool ValidSave(string json)
    {
        try { ConstructionSaveCodec.Deserialize(_catalog, json); return true; }
        catch (ArgumentException) { return false; }
    }

    private static void Backup() => WithStorage(directory =>
    {
        var path = Path.Combine(directory, "tower.json");
        var world = World();
        var first = ConstructionSaveCodec.Serialize(world);
        SaveFileStore.Save(path, first, ValidSave);
        Success(world.ApplyOperatingTransaction(100, 10, "Revenue", null, "Payment."));
        var second = ConstructionSaveCodec.Serialize(world);
        SaveFileStore.Save(path, second, ValidSave);
        Equal(second, SaveFileStore.Load(path));
        Equal(first, SaveFileStore.LoadBackup(path));
        Success(world.ApplyOperatingTransaction(200, 20, "Revenue", null, "Payment."));
        SaveFileStore.Save(path, ConstructionSaveCodec.Serialize(world), ValidSave);
        Equal(second, SaveFileStore.LoadBackup(path));
        Require(!Directory.EnumerateFiles(directory, "*.tmp").Any());
    });

    private static void FailedReplacement() => WithStorage(directory =>
    {
        var path = Path.Combine(directory, "tower.json");
        var valid = ConstructionSaveCodec.Serialize(World());
        SaveFileStore.Save(path, valid, ValidSave);
        SaveFileStore.Save(path, valid, ValidSave);
        Throws<SaveValidationException>(() => SaveFileStore.Save(path, "{}", ValidSave));
        Throws<SaveValidationException>(() => SaveFileStore.Save(path, "{", ValidSave));
        Throws<InvalidOperationException>(() => SaveFileStore.Save(path, valid, _ => throw new InvalidOperationException("Validation unavailable.")));
        Equal(valid, SaveFileStore.Load(path));
        Equal(valid, SaveFileStore.LoadBackup(path));
    });

    private static void CorruptOldFile() => WithStorage(directory =>
    {
        var path = Path.Combine(directory, "tower.json");
        var valid = ConstructionSaveCodec.Serialize(World());
        SaveFileStore.Save(path, valid, ValidSave);
        SaveFileStore.Save(path, valid, ValidSave);
        File.WriteAllText(path, "{}");
        Throws<SaveValidationException>(() => SaveFileStore.Save(path, valid, ValidSave));
        Equal("{}", SaveFileStore.Load(path));
        Equal(valid, SaveFileStore.LoadBackup(path));
        File.WriteAllText(path, "{");
        Throws<SaveValidationException>(() => SaveFileStore.Save(path, valid, ValidSave));
        Equal(valid, SaveFileStore.LoadBackup(path));
    });

    private static void StorageFailure() => WithStorage(directory =>
    {
        var path = Path.Combine(directory, "tower.json");
        var valid = ConstructionSaveCodec.Serialize(World());
        SaveFileStore.Save(path, valid, ValidSave);
        SaveFileStore.Save(path, valid, ValidSave);
        Throws<DirectoryNotFoundException>(() => SaveFileStore.Save(Path.Combine(directory, "absent", "tower.json"), valid, ValidSave));
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Throws<IOException>(() => SaveFileStore.Save(path, valid, ValidSave));
        }
        Equal(valid, SaveFileStore.Load(path));
        Equal(valid, SaveFileStore.LoadBackup(path));
        Require(!Directory.EnumerateFiles(directory, "*.tmp").Any());
    });

    private static void AutosaveSlots() => WithStorage(directory =>
    {
        var world = World();
        var expected = new string[3];
        var paths = new string[3];
        for (var i = 0; i < 5; i++)
        {
            Success(world.ApplyOperatingTransaction(100, i, "Revenue", null, "Payment."));
            expected[i % 3] = ConstructionSaveCodec.Serialize(world);
            paths[i % 3] = SaveFileStore.SaveAutosave(directory, "tower", i % 3, expected[i % 3], ValidSave);
        }
        for (var i = 0; i < 3; i++) Equal(expected[i], SaveFileStore.Load(paths[i]));
        Throws<ArgumentOutOfRangeException>(() => SaveFileStore.SaveAutosave(directory, "tower", 3, expected[0]));
        Throws<ArgumentException>(() => SaveFileStore.SaveAutosave(directory, "../escape", 0, expected[0]));
    });

    private static void DeterministicContinuation()
    {
        var continuous = World();
        var resumed = ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(continuous));
        var random = new Random(4912);
        for (var i = 0; i < 800; i++)
        {
            var action = random.Next(5);
            var floor = random.Next(-3, 9);
            var x = random.Next(0, 32);
            var id = random.Next(1, 40);
            var amount = random.Next(-200_000, 200_000);
            foreach (var world in new[] { continuous, resumed })
            {
                switch (action)
                {
                    case 0: world.BuildFloor(floor); break;
                    case 1: world.BuildRoom("office", x, floor); break;
                    case 2: world.DemolishRoom(id); break;
                    case 3: world.DemolishFloor(floor); break;
                    default: world.ApplyOperatingTransaction(amount, i, "Operations.Mixed", null, "Deterministic account entry."); break;
                }
            }
            if (i % 67 == 0) resumed = ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(resumed));
            Same(continuous, resumed);
        }
    }

    private static void AdoptSimulationClock()
    {
        var world = World();
        var old = world.Ledger.ToArray();
        Require(old.All(entry => !world.UsesSimulationTimestamp(entry)));
        var topology = world.TopologyVersion;
        world.SetSimulationClock(42);
        Equal(topology, world.TopologyVersion);
        Success(world.BuildFloor(2));
        Success(world.BuildFloor(3));
        Equal(42L, world.Ledger[^1].TimestampTicks);
        Equal(42L, world.Ledger[^2].TimestampTicks);
        Require(world.Ledger[^1].Sequence != world.Ledger[^2].Sequence);
        Require(old.SequenceEqual(world.Ledger.Take(old.Length)));
        Require(old.All(entry => !world.UsesSimulationTimestamp(entry)));
        Require(world.UsesSimulationTimestamp(world.Ledger[^1]));
        world.SetSimulationClock(100);
        var before = ConstructionSaveCodec.Serialize(world);
        Throws<ArgumentOutOfRangeException>(() => world.SetSimulationClock(99));
        Throws<ArgumentOutOfRangeException>(() => world.SetSimulationClock(-1));
        Require(!world.ApplyOperatingTransaction(10, 99, "Revenue", null, "Stale clock.").Success);
        Require(!world.ApplyOperatingTransaction(10, 101, "Revenue", null, "Future clock.").Success);
        Equal(before, ConstructionSaveCodec.Serialize(world));
        Success(world.ApplyOperatingTransaction(10, 100, "Revenue", null, "Current clock."));
        Same(world, ConstructionSaveCodec.Deserialize(_catalog, ConstructionSaveCodec.Serialize(world)));
    }

    private static void InvalidClock()
    {
        var world = World();
        world.SetSimulationClock(10);
        Success(world.BuildFloor(2));
        world.SetSimulationClock(20);
        Success(world.BuildFloor(3));
        var snapshot = world.CaptureSnapshot();
        Reject(snapshot with { Clock = new(-1, 1) });
        Reject(snapshot with { Clock = new(20, 0) });
        Reject(snapshot with { Clock = new(20, snapshot.CommandSequence + 2) });
        Reject(snapshot with { Clock = snapshot.Clock! with { CurrentTick = 19 } });
        Reject(snapshot with { SchemaVersion = 1 });
        Reject(snapshot with { Ledger = [.. snapshot.Ledger.Take(snapshot.Ledger.Length - 1), snapshot.Ledger[^1] with { TimestampTicks = 9 }] });
        var root = JsonNode.Parse(ConstructionSaveCodec.Serialize(world))!.AsObject();
        foreach (var key in new[] { "currentTick", "firstSimulationSequence" })
        {
            var copy = root.DeepClone().AsObject();
            copy["clock"]!.AsObject().Remove(key);
            RejectJson(copy.ToJsonString());
        }
        root["clock"]!["unexpected"] = 1;
        RejectJson(root.ToJsonString());
    }

    private static GameSession Session() => new(_catalog, _rules, _locations);
    private static GameSession ReadSession(string json) => GameSession.Deserialize(_catalog, _rules, _locations, json);

    private static void SessionClock()
    {
        var session = Session();
        Success(session.World.BuildFloor(1));
        Success(session.World.BuildFloor(2));
        Require(session.World.Ledger.All(entry => entry.TimestampTicks == 0 && session.World.UsesSimulationTimestamp(entry)));
        var topology = session.World.TopologyVersion;
        session.Advance(187);
        Equal(topology, session.World.TopologyVersion);
        Equal(187L, session.World.SimulationClockTicks!.Value);
        Success(session.World.BuildFloor(3));
        Success(session.World.DemolishFloor(3));
        Require(session.World.Ledger.TakeLast(2).All(entry => entry.TimestampTicks == 187));
        Equal(4, session.World.Ledger.Count);
    }

    private static string LegacyConstructionJson(ConstructionWorld world)
    {
        var root = JsonNode.Parse(ConstructionSaveCodec.Serialize(world))!.AsObject();
        root["schemaVersion"] = 1;
        root.Remove("clock");
        return root.ToJsonString();
    }

    private static void LegacyConstructionClock()
    {
        var old = World();
        var json = LegacyConstructionJson(old);
        var restoredWorld = ConstructionSaveCodec.Deserialize(_catalog, json);
        Require(restoredWorld.Ledger.SequenceEqual(old.Ledger));
        Equal(2, restoredWorld.CaptureSnapshot().SchemaVersion);
        Require(restoredWorld.SimulationClockTicks is null);
        Success(restoredWorld.BuildFloor(2));
        Equal(restoredWorld.Ledger[^1].Sequence, restoredWorld.Ledger[^1].TimestampTicks);

        var imported = ReadSession(json);
        Require(imported.World.Ledger.SequenceEqual(old.Ledger));
        Require(imported.World.Ledger.All(entry => !imported.World.UsesSimulationTimestamp(entry)));
        imported.Advance(77);
        Success(imported.World.BuildFloor(2));
        Equal(77L, imported.World.Ledger[^1].TimestampTicks);
        Require(imported.World.UsesSimulationTimestamp(imported.World.Ledger[^1]));
        Equal(imported.Serialize(), ReadSession(imported.Serialize()).Serialize());
    }

    private static void LegacySessionClock()
    {
        var session = Session();
        Success(session.World.BuildFloor(1));
        session.Advance(123);
        Success(session.World.BuildFloor(2));
        var root = JsonNode.Parse(session.Serialize())!.AsObject();
        var world = root["world"]!.AsObject();
        world["schemaVersion"] = 1;
        world.Remove("clock");
        foreach (var entry in world["ledger"]!.AsArray()) entry!["timestampTicks"] = entry["sequence"]!.DeepClone();
        var migrated = ReadSession(root.ToJsonString());
        Equal(123L, migrated.Tick);
        Equal(123L, migrated.World.SimulationClockTicks!.Value);
        Require(migrated.World.Ledger.All(entry => entry.TimestampTicks == entry.Sequence && !migrated.World.UsesSimulationTimestamp(entry)));
        migrated.Advance(7);
        Success(migrated.World.BuildFloor(3));
        Equal(130L, migrated.World.Ledger[^1].TimestampTicks);
        Require(migrated.World.UsesSimulationTimestamp(migrated.World.Ledger[^1]));
        var restored = ReadSession(migrated.Serialize());
        migrated.Advance(8); restored.Advance(8);
        Success(migrated.World.BuildFloor(4)); Success(restored.World.BuildFloor(4));
        Equal(138L, restored.World.Ledger[^1].TimestampTicks);
        Equal(migrated.Serialize(), restored.Serialize());
    }

    private static void SessionClockRestore()
    {
        var session = Session();
        session.Advance(313);
        Success(session.World.BuildFloor(1));
        var before = session.Serialize();
        var restored = ReadSession(before);
        Equal(before, restored.Serialize());
        session.Advance(47); restored.Advance(47);
        Success(session.World.BuildFloor(2)); Success(restored.World.BuildFloor(2));
        Equal(360L, restored.World.Ledger[^1].TimestampTicks);
        Equal(session.Serialize(), restored.Serialize());
        foreach (var alteration in new Action<JsonObject>[]
        {
            root => root["world"]!["clock"]!["currentTick"] = session.Tick + 1,
            root => root["world"]!["clock"] = null,
            root => root["world"]!.AsObject().Remove("clock"),
            root => root["world"]!["schemaVersion"] = 1
        })
        {
            var root = JsonNode.Parse(session.Serialize())!.AsObject();
            alteration(root);
            Throws<SaveValidationException>(() => ReadSession(root.ToJsonString()));
        }
    }

    private static (GameSession Session, long Office) InspectionTower(int topFloor = 4, bool transfer = false)
    {
        var game = Session();
        for (var floor = 1; floor <= topFloor; floor++) Success(game.World.BuildFloor(floor));
        Success(game.BuildRoom("lobby", 0, 0));
        var office = game.BuildRoom("office", 0, topFloor);
        Success(office);
        if (transfer)
        {
            Success(game.Transport.InstallBank(new BankDefinition(1, 28, 0, 3, [0, 3], Capacity: 1, TravelTicksPerFloor: 4)));
            Success(game.Transport.InstallBank(new BankDefinition(2, 29, 3, topFloor, [3, topFloor], Capacity: 1, TravelTicksPerFloor: 4)));
        }
        else Success(game.Transport.InstallBank(new BankDefinition(1, 29, 0, topFloor,
            Enumerable.Range(0, topFloor + 1).ToArray(), Capacity: 1, TravelTicksPerFloor: 4)));
        return (game, office.EntityId!.Value);
    }

    private static void UntilInspection(GameSession game, Func<bool> condition, int maximumTicks = 2000)
    {
        for (var i = 0; i < maximumTicks && !condition(); i++) game.Step();
        Require(condition(), $"Inspection fixture did not reach the required state by tick {game.Tick}.");
    }

    private static void InspectPhysicalPerson()
    {
        var (game, _) = InspectionTower();
        UntilInspection(game, () => game.Transport.Journeys.Any(journey => journey.State == JourneyState.Waiting));
        var personId = game.Transport.Journeys.First(journey => journey.State == JourneyState.Waiting).PersonId;
        game.Step();
        var before = game.Serialize();
        for (var read = 0; read < 100; read++)
        {
            var inspection = game.InspectPerson(personId)!;
            var journey = game.Transport.JourneyFor(personId)!;
            Equal(journey, inspection.Journey);
            Equal(game.People.Single(person => person.Id == personId), inspection.Person);
            Equal(600, inspection.PatienceTicks);
            Equal(game.Tick - journey.WaitSinceTick, inspection.CurrentWaitTicks);
            Require(inspection.LocationLabel.Contains("waiting for elevator #1"));
            Require(inspection.NextActionTick is null);
            var bank = game.InspectBank(1)!;
            Equal(bank.Bank.WaitingCount, bank.HallCalls.Sum(call => call.WaitingCount));
            Require(game.InspectPerson(long.MaxValue) is null && game.InspectPerson(-1) is null);
            Require(game.InspectBank(int.MaxValue) is null);
        }
        Equal(before, game.Serialize());
        UntilInspection(game, () => game.Transport.JourneyFor(personId)?.State == JourneyState.Riding
            && game.Transport.Cars[0].State == CarState.Traveling && game.Transport.Cars[0].DrawFloor > 0);
        var riding = game.InspectPerson(personId)!;
        Equal(game.Transport.Cars[0].DrawFloor, riding.Journey.DrawFloor);
        Equal(29d, riding.Journey.X);
        Equal(4, riding.Journey.NextStopFloor!.Value);
        Equal(0L, riding.CurrentWaitTicks);
        Equal(0L, riding.Journey.WaitSinceTick);
        Require(riding.LocationLabel.StartsWith("Elevator #1, floor "));
        Equal(riding, ReadSession(game.Serialize()).InspectPerson(personId)!);
    }

    private static void InspectPersonSchedule()
    {
        var (game, office) = InspectionTower(2);
        UntilInspection(game, () => game.People.Any());
        var personId = game.People[0].Id;
        var arriving = game.InspectPerson(personId)!;
        Equal(PersonActivity.Arriving, arriving.Person.Activity);
        Require(arriving.Person.ActionAt > 0 && arriving.NextActionTick is null);
        Require(arriving.DestinationLabel.Contains($"Office #{office}"));
        UntilInspection(game, () => game.InspectPerson(personId)?.Person.Activity == PersonActivity.Visiting);
        var visiting = game.InspectPerson(personId)!;
        Equal(visiting.Person.ActionAt, visiting.NextActionTick!.Value);
        Require(visiting.NextActionTick > game.Tick && visiting.LocationLabel.StartsWith("Inside Office #"));
        Success(game.SetOpen(office, false));
        game.Step();
        var leaving = game.InspectPerson(personId)!;
        Equal(PersonActivity.Leaving, leaving.Person.Activity);
        Require(leaving.DestinationLabel.StartsWith("Exit via Entrance Lobby #"));
        Equal(0, leaving.Journey.DestinationFloor);
        Require(leaving.NextActionTick is null);
        var originalLobby = game.World.Rooms.Single(room => room.DefinitionId == "lobby");
        Success(game.BuildRoom("lobby", 6, 0));
        Success(game.SetOpen(originalLobby.Id, false));
        Equal(leaving.DestinationLabel, game.InspectPerson(personId)!.DestinationLabel);
        UntilInspection(game, () => game.InspectPerson(personId) is null);
        Require(game.Transport.JourneyFor(personId) is null);
    }

    private static void InspectTransferBank()
    {
        var (game, _) = InspectionTower(6, transfer: true);
        UntilInspection(game, () => game.Transport.Journeys.Any(journey => journey.State == JourneyState.Riding && journey.BankId == 1));
        var rider = game.Transport.Journeys.First(journey => journey.State == JourneyState.Riding && journey.BankId == 1);
        var before = game.Serialize();
        var inspection = game.InspectBank(1)!;
        Equal(6, rider.DestinationFloor);
        Equal(3, rider.NextStopFloor!.Value);
        Require(inspection.PassengerStops.SequenceEqual(new[] { 3 }), "Passenger request must be the real transfer stop, not the final room floor.");
        Require(inspection.HallCalls.All(call => call.Floor == 0 && call.Direction == 1 && call.OldestWaitTicks >= 0));
        Equal(inspection.Bank.WaitingCount, inspection.HallCalls.Sum(call => call.WaitingCount));
        Require(((IList<int>)inspection.PassengerStops).IsReadOnly && ((IList<HallCallInspection>)inspection.HallCalls).IsReadOnly);
        Equal(before, game.Serialize());
        var restored = ReadSession(before).InspectBank(1)!;
        Require(inspection.PassengerStops.SequenceEqual(restored.PassengerStops));
        Require(inspection.HallCalls.SequenceEqual(restored.HallCalls));
    }

    private static void InspectServicePerson()
    {
        var (game, office) = InspectionTower(2);
        game.Advance(18 * 3600);
        var depot = game.BuildRoom("service-room", 6, 0);
        Success(depot);
        Success(game.RepairRoom(office));
        var personId = game.People.First(person => person.Role == "Staff" && person.ServiceTargetId == office).Id;
        var arriving = game.InspectPerson(personId)!;
        Require(arriving.DestinationLabel.StartsWith($"Service at Office #{office}"));
        Require(arriving.NextActionTick is null && arriving.PatienceTicks == 1200);
        UntilInspection(game, () => game.InspectPerson(personId)?.Person.Activity == PersonActivity.Working);
        var working = game.InspectPerson(personId)!;
        Equal(working.Person.ActionAt, working.NextActionTick!.Value);
        Require(working.LocationLabel.StartsWith($"Inside Office #{office}"));
        UntilInspection(game, () => game.InspectPerson(personId)?.Person.Activity == PersonActivity.Returning);
        var returning = game.InspectPerson(personId)!;
        var depotName = _catalog.Get("service-room").Name;
        Require(returning.DestinationLabel.StartsWith($"Return to {depotName} #{depot.EntityId}"));
        Equal(0, returning.Journey.DestinationFloor);
        Equal(office, returning.Person.ServiceTargetId!.Value);
        Require(returning.NextActionTick is null);
        var before = game.Serialize();
        for (var read = 0; read < 50; read++) _ = game.InspectPerson(personId);
        Equal(before, game.Serialize());
        Equal(returning, ReadSession(before).InspectPerson(personId)!);
    }

    private static void WithStorage(Action<string> test)
    {
        var directory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", $"persistence-test-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory, recursive: false);
        }
    }

    private static void Reject(ConstructionSnapshot snapshot) => Throws<SaveValidationException>(() => ConstructionWorld.FromSnapshot(_catalog, snapshot));
    private static void RejectJson(string json) => Throws<SaveValidationException>(() => ConstructionSaveCodec.Deserialize(_catalog, json));
    private static void Same(ConstructionWorld first, ConstructionWorld second) => Equal(ConstructionSaveCodec.Serialize(first), ConstructionSaveCodec.Serialize(second));
    private static void Success(CommandResult result) => Require(result.Success, result.Message);
    private static void Require(bool condition, string message = "Assertion failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
