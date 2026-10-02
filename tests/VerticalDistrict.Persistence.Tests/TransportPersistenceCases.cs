using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Persistence.Tests;

internal sealed class TransportPersistenceCases(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
{
    public (string Name, Action Test)[] Cases() =>
    [
        ("Genuine schema-six first-bank riders retain physical routes, queues, money, and schedules", LegacyFirstBank),
        ("Genuine schema-six transfer queues retain completed rides and second-car ownership", LegacyTransfer),
        ("Multiple cars and physical transfer approaches resume with identical authoritative state", MultiCarContinuation),
        ("A moving disabled car resumes its retained passenger ownership before evacuation", DisabledCarContinuation),
        ("Transport snapshot shaft, passenger, and route arrays are detached", DetachedTransport),
        ("Transport rejects duplicate car ownership, invalid assignments, geometry, and timers", CorruptTransport),
        ("Current transport fields and matching session versions are mandatory", VersionBoundary),
        ("Unassigned paused queues and standalone legacy transport remain loadable", StandaloneTransport),
        ("Corrupt multi-car candidates preserve the live session and both last-good files", LastGoodTransport)
    ];

    private GameSession Read(string json) => GameSession.Deserialize(catalog, rules, locations, json);
    private static JsonObject Node(string json) => JsonNode.Parse(json)!.AsObject();
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static JsonObject Transport(JsonObject root) => root["transport"]!.AsObject();
    private static JsonArray Banks(JsonObject root) => Transport(root)["banks"]!.AsArray();
    private static JsonArray Journeys(JsonObject root) => Transport(root)["journeys"]!.AsArray();

    // Used only by synthetic historical tests and exact comparisons of fields that existed in
    // transport schema one. Genuine fixture documents themselves are never rewritten.
    internal static JsonObject LegacyTransportProjection(JsonNode transport)
    {
        var result = transport.DeepClone().AsObject(); result["schemaVersion"] = 1;
        foreach (var bank in result["banks"]!.AsArray().OfType<JsonObject>())
        {
            Check(bank["additionalCars"] is not JsonArray additional || additional.Count == 0, "Cannot project a genuine multi-car bank into old state.");
            bank.Remove("additionalCars"); bank["definition"]!.AsObject().Remove("additionalShafts");
            bank["car"]!.AsObject().Remove("carId"); bank["car"]!.AsObject().Remove("isOutOfService");
        }
        foreach (var journey in result["journeys"]!.AsArray().OfType<JsonObject>())
        {
            journey.Remove("assignedCarId"); journey.Remove("completedRides");
            foreach (var leg in journey["route"]!.AsArray().OfType<JsonObject>()) leg.Remove("carId");
        }
        return result;
    }

    private void LegacyFirstBank() => LegacyFixture("schema6-first-bank-riders.json", false);
    private void LegacyTransfer() => LegacyFixture("schema6-transfer-queue.json", true);
    private void LegacyFixture(string filename, bool transfer)
    {
        var old = Node(Fixture(filename)); Equal(6, old["schemaVersion"]!.GetValue<int>());
        var game = Read(old.ToJsonString()); var current = Node(game.Serialize());
        Equal(7, game.CaptureSnapshot().SchemaVersion); Equal(2, game.Transport.CaptureSnapshot().SchemaVersion);
        foreach (var key in new[] { "world", "people", "operations", "management", "finance", "tick", "randomState", "nextPersonId", "rulesFingerprint" })
            Check(JsonNode.DeepEquals(old[key], current[key]), "Migration changed existing session field " + key);
        Check(JsonNode.DeepEquals(old["transport"], LegacyTransportProjection(current["transport"]!)), "Migration changed a historical physical transport field.");
        var state = game.Transport.CaptureSnapshot();
        Check(state.Journeys.Where(j => j.State is JourneyState.Waiting or JourneyState.Riding).All(j => j.AssignedCarId == 1), "Original passengers lost their original car.");
        if (transfer)
        {
            Check(state.Journeys.Any(j => j.State == JourneyState.Waiting && j.Floor == 3 && j.CompletedRides == 1), "Transfer queue lost its completed first ride.");
            Check(state.Journeys.Any(j => j.State == JourneyState.Riding && j.Route[j.LegIndex].BankId == 2 && j.CompletedRides == 1), "Second-bank rider lost first-ride progress.");
        }
        var restored = Read(game.Serialize()); Equal(game.Serialize(), restored.Serialize());
        game.Advance(1600); restored.Advance(1600); Equal(game.Serialize(), restored.Serialize());
        Check(game.World.Ledger.Count(row => row.Category == "Transport.Construction") == 2, "Migration charged transport again.");
    }

    private GameSession MultiCar()
    {
        var game = new GameSession(catalog, rules, locations, sandbox: true);
        for (var floor = 1; floor <= 6; floor++) Must(game.World.BuildFloor(floor));
        Must(game.BuildRoom("lobby", 0, 0));
        for (var office = 0; office < 3; office++) Must(game.BuildRoom("office", office * 5, 6));
        Must(game.Transport.InstallBank(new BankDefinition(1, 24, 0, 3, [0, 3], Capacity: 2, TravelTicksPerFloor: 4,
            AdditionalShafts: [new ShaftDefinition(2, 26)]), rules.ElevatorCostMinor * 2));
        Must(game.Transport.InstallBank(new BankDefinition(2, 28, 3, 6, [3, 6], Capacity: 1, TravelTicksPerFloor: 8,
            AdditionalShafts: [new ShaftDefinition(2, 30)]), rules.ElevatorCostMinor * 2));
        return game;
    }

    private void MultiCarContinuation()
    {
        var game = MultiCar();
        Until(game, () => game.Transport.CaptureSnapshot().Journeys.Any(j => j.State == JourneyState.Walking && j.AssignedCarId == 2), 500);
        var restored = Read(game.Serialize()); Equal(game.Serialize(), restored.Serialize());
        for (var tick = 0; tick < 1000; tick++)
        {
            game.Step(); restored.Step();
            if (tick % 23 == 0) { Equal(game.Serialize(), restored.Serialize()); restored = Read(restored.Serialize()); }
        }
        Equal(game.Serialize(), restored.Serialize());
        Check(game.Transport.Journeys.Any(j => j.State == JourneyState.Arrived), "No transferred office passenger reached a destination.");
    }

    private void DisabledCarContinuation()
    {
        var game = MultiCar();
        Until(game, () => game.Transport.Cars.Any(c => c.State == CarState.Traveling && c.PassengerCount > 0), 800);
        var car = game.Transport.Cars.First(c => c.State == CarState.Traveling && c.PassengerCount > 0);
        Must(game.Transport.SetCarOutOfService(car.BankId, car.CarId, true));
        var resumed = Read(game.Serialize()); Equal(game.Serialize(), resumed.Serialize());
        game.Advance(700); resumed.Advance(700); Equal(game.Serialize(), resumed.Serialize());
    }

    private void DetachedTransport()
    {
        var game = MultiCar(); Until(game, () => game.Transport.Cars.Any(c => c.PassengerCount > 0), 800);
        var before = game.Serialize(); var snapshot = game.Transport.CaptureSnapshot();
        snapshot.Banks[0].Definition.AdditionalShafts![0] = new ShaftDefinition(20, 2);
        var occupied = snapshot.Banks.SelectMany(b => new[] { b.Car }.Concat(b.AdditionalCars!)).First(c => c.Passengers.Length > 0);
        occupied.Passengers[0] = long.MaxValue;
        snapshot.Journeys.First(j => j.Route.Length > 0).Route[0] = new RouteLeg(RouteKind.Walk, 0, 1, 0, 2);
        Equal(before, game.Serialize());
    }

    private string RidingSave()
    {
        var game = MultiCar(); Until(game, () => game.Transport.Journeys.Any(j => j.State == JourneyState.Riding), 800); return game.Serialize();
    }

    private void CorruptTransport()
    {
        var json = RidingSave();
        foreach (Action<JsonObject> mutate in new Action<JsonObject>[]
        {
            r => Banks(r)[0]!["additionalCars"]![0]!["carId"] = 1,
            r => Banks(r)[0]!["additionalCars"] = new JsonArray(),
            r => Banks(r)[0]!["definition"]!["additionalShafts"]![0]!["x"] = 24,
            r => Banks(r)[0]!["definition"]!["additionalShafts"]![0]!["id"] = 9,
            r => Banks(r)[0]!["additionalCars"]![0]!["timer"] = -1,
            r => Journeys(r).OfType<JsonObject>().First(j => j["state"]!.GetValue<string>() == "Riding")["assignedCarId"] = 99,
            r => { var j = Journeys(r).OfType<JsonObject>().First(j => j["state"]!.GetValue<string>() == "Riding"); j["route"]![j["legIndex"]!.GetValue<int>()]!["carId"] = 0; },
            r => Journeys(r)[0]!["completedRides"] = -1,
            r => { var id = Journeys(r).OfType<JsonObject>().First(j => j["state"]!.GetValue<string>() == "Riding")["personId"]!.GetValue<long>(); Banks(r)[1]!["additionalCars"]![0]!["passengers"] = new JsonArray(id); }
        }) Reject(json, mutate);
    }

    private void VersionBoundary()
    {
        var json = RidingSave();
        foreach (Action<JsonObject> mutate in new Action<JsonObject>[]
        {
            r => r["schemaVersion"] = 6,
            r => Transport(r)["schemaVersion"] = 1,
            r => Transport(r)["schemaVersion"] = 3,
            r => Banks(r)[0]!.AsObject().Remove("additionalCars"),
            r => Banks(r)[0]!["car"]!.AsObject().Remove("carId"),
            r => Banks(r)[0]!["car"]!.AsObject().Remove("isOutOfService"),
            r => Banks(r)[0]!["definition"]!.AsObject().Remove("additionalShafts"),
            r => Journeys(r)[0]!.AsObject().Remove("completedRides"),
            r => Journeys(r)[0]!.AsObject().Remove("assignedCarId"),
            r => Journeys(r)[0]!["route"]![0]!.AsObject().Remove("carId")
        }) Reject(json, mutate);
    }

    private void StandaloneTransport()
    {
        var world = new ConstructionWorld(catalog, 1_000_000_000, 7); Must(world.BuildFloor(1));
        var transport = new TransportSystem(world); Must(transport.InstallBank(new BankDefinition(1, 28, 0, 1, [0, 1])));
        Must(transport.RequestJourney(1, 0, 28, 1, 28));
        var snapshot = transport.CaptureSnapshot(); Equal(JourneyState.Waiting, snapshot.Journeys[0].State); Equal<int?>(null, snapshot.Journeys[0].AssignedCarId);
        var restored = TransportSystem.RestoreSnapshot(world, snapshot);
        var oldNode = LegacyTransportProjection(JsonSerializer.SerializeToNode(snapshot, SimulationRules.JsonOptions)!);
        var old = JsonSerializer.Deserialize<TransportSnapshot>(oldNode, SimulationRules.JsonOptions)!;
        var migrated = TransportSystem.RestoreSnapshot(world, old); Equal(1, migrated.CaptureSnapshot().Journeys[0].AssignedCarId!.Value);
        for (var tick = 1; tick <= 80; tick++) { transport.Step(tick); restored.Step(tick); migrated.Step(tick); }
        Equal(JourneyState.Arrived, migrated.JourneyFor(1)!.State);
        Equal(JsonSerializer.Serialize(transport.CaptureSnapshot()), JsonSerializer.Serialize(restored.CaptureSnapshot()));
    }

    private void LastGoodTransport()
    {
        var json = RidingSave(); var live = Read(json); var directory = Path.Combine(Path.GetTempPath(), "vd-transport-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "tower.json");
        bool Valid(string candidate) { try { Read(candidate); return true; } catch (SaveValidationException) { return false; } }
        try
        {
            SaveFileStore.Save(path, json, Valid); live.Step(); var second = live.Serialize(); SaveFileStore.Save(path, second, Valid);
            var corrupt = Node(second); Banks(corrupt)[0]!["additionalCars"] = null;
            try { SaveFileStore.Save(path, corrupt.ToJsonString(), Valid); throw new InvalidOperationException("Corrupt replacement accepted."); }
            catch (SaveValidationException) { }
            Equal(second, File.ReadAllText(path)); Equal(json, SaveFileStore.LoadBackup(path)); Equal(second, live.Serialize());
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private void Reject(string json, Action<JsonObject> mutate)
    {
        var node = Node(json); mutate(node);
        try { Read(node.ToJsonString()); }
        catch (SaveValidationException) { return; }
        throw new InvalidOperationException("Corrupt transport save was accepted.");
    }
    private static void Until(GameSession game, Func<bool> condition, int ticks)
    { for (var tick = 0; tick < ticks && !condition(); tick++) game.Step(); Check(condition(), "Transport fixture did not reach required state."); }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}.");
}
