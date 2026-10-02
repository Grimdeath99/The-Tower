using System.Text.Json;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Transport;

var catalog = ContentCatalog.Load(File.ReadAllText("Data/construction.catalog.json"));
var cases = new (string, Action)[]
{
    ("Travel consumes time, doors cycle and snapshots expose actual position", ActualTravel),
    ("Capacity one leaves a real queue and oldest waiting person boards first", FullCar),
    ("Opposite direction waits while occupied car continues its direction", DirectionalBoarding),
    ("Transfers use two independent shafts without duplicate passenger ownership", Transfer),
    ("Stair journeys walk each floor and work independently of elevators", Stairs),
    ("Service-only access is enforced by pathfinding", ServiceAccess),
    ("Unreachable routes retry after a topology edit", Disconnected),
    ("Stop changes invalidate queues without moving their physical position", StopsChanged),
    ("Breakdown lands safely then repairs recover stranded journeys", Breakdown),
    ("Cancelled queue entries cannot board later", Cancel),
    ("Patience abandonment and overload metrics are observable", Abandonment),
    ("Room and shaft collision plus costs are validated atomically", Geometry),
    ("Active movement and transfer queues continue exactly after JSON save/load", SaveContinue),
    ("Malformed snapshots reject duplicate car ownership and invalid timers", MalformedSave),
    ("Deterministic rush demand preserves capacity and completes", RushHour),
    ("Each clock tick must execute exactly once", Clock),
    ("Removed destinations remain explicitly unreachable and survive saving", DemolishedDestination),
    ("Unrelated construction keeps queue fairness and patience intact", QueueAge),
    ("Cancellation preserves walked position and never teleports from stairs", SafeCancellation),
    ("Escalators enforce direction and charge only valid geometry", EscalatorDirection),
    ("Escalator to elevator transfers retain direction after active save/load", EscalatorTransferSave),
    ("Continuous terminal demand cannot starve an older intermediate hall call", StarvationPrevention),
    ("Paused construction access queries are pure and simulation observes edits exactly once", PausedAccessQueries),
    ("Coordinated cars occupy distinct costed shafts and expose detached definitions", CoordinatedGeometry),
    ("Multi-car transfer demand walks the connecting floor and accounts for every passenger", CoordinatedTransfer),
    ("Assigned passengers walk to their actual shaft before boarding", AssignedApproach),
    ("One cancelled hall passenger cannot cancel another passenger's assigned service", CoordinatedCancellation),
    ("Individual car outage lands riders safely while its bank continues serving", IndividualOutage),
    ("Unavailable car reassignment preserves an older hall call's age", ReassignmentAge),
    ("Busy car removal and bank configuration reject atomically", CoordinatedEdits),
    ("Route diagnostics separate disabled service permissions and missing stop geometry", RouteDiagnostics),
    ("Active multi-car travel and transfer queues resume exactly after saving", CoordinatedSave),
    ("Unrelated idle bank seats cannot hide pressure at a congested bank", BankPressure),
    ("Added and restored cars serve the existing backlog without resetting queue age", CapacityEntry),
    ("Reinstalled shaft identities require a physical walk from the old hall position", ReinstalledShaft),
    ("Capacity changes preserve an approaching passenger's commitment and age through boarding", ApproachHandoff)
};
var failed = 0;
foreach (var (name, run) in cases)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} transport tests passed.");
return failed == 0 ? 0 : 1;

ConstructionWorld World(int floors = 5)
{
    var world = new ConstructionWorld(catalog, 2_000_000_000, 5);
    for (var floor = 1; floor <= floors; floor++) Assert(world.BuildFloor(floor).Success);
    return world;
}

TransportSystem System(int capacity = 4, int top = 5, int travel = 2)
{
    var system = new TransportSystem(World(top));
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, top, Enumerable.Range(0, top + 1).ToArray(), capacity, travel, 2)).Success);
    return system;
}

void Advance(TransportSystem system, int ticks)
{
    for (var i = 0; i < ticks; i++)
    {
        system.Step(system.CurrentTick + 1);
        foreach (var car in system.Cars)
        {
            Assert(car.PassengerCount <= car.Capacity);
            foreach (var id in car.PassengerIds)
                Assert(system.Journeys.Single(j => j.PersonId == id).State == JourneyState.Riding);
        }
        Assert(system.Cars.SelectMany(c => c.PassengerIds).Distinct().Count() == system.Cars.Sum(c => c.PassengerCount));
    }
}

void Until(TransportSystem system, Func<bool> predicate, int max = 2000)
{
    for (var i = 0; i < max && !predicate(); i++) Advance(system, 1);
    Assert(predicate(), "Condition did not become true within allotted ticks.");
}

void ActualTravel()
{
    var system = System();
    Assert(system.RequestJourney(1, 0, 1, 5, 3).Success);
    Assert(system.Journeys.Single().State == JourneyState.Waiting);
    Until(system, () => system.Cars[0].State == CarState.Traveling);
    Assert(system.Cars[0].DrawFloor == 0);
    Advance(system, 1);
    Assert(system.Cars[0].DrawFloor > 0 && system.Cars[0].DrawFloor < 5);
    Assert(system.Journeys[0].DrawFloor == system.Cars[0].DrawFloor);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.Journeys[0].X == 3 && system.Journeys[0].Floor == 5);
    Assert(system.Metrics.WaitSampleCount == 1);
}

void FullCar()
{
    var system = System(1);
    for (var id = 1; id <= 3; id++) Assert(system.RequestJourney(id, 0, 1, 5, 1, patienceTicks: 1000).Success);
    Until(system, () => system.Cars[0].PassengerCount == 1);
    Assert(system.Cars[0].PassengerIds.Single() == 1);
    Assert(system.Journeys.Count(j => j.State == JourneyState.Waiting) == 2);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
    Assert(system.Journeys[1].TotalWaitTicks > system.Journeys[0].TotalWaitTicks);
    Assert(system.Journeys[2].TotalWaitTicks > system.Journeys[1].TotalWaitTicks);
}

void DirectionalBoarding()
{
    var system = System();
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Assert(system.RequestJourney(2, 2, 1, 4, 1).Success);
    Assert(system.RequestJourney(3, 2, 1, 0, 1).Success);
    Until(system, () => system.Journeys.Single(j => j.PersonId == 2).State == JourneyState.Riding);
    Assert(system.Journeys.Single(j => j.PersonId == 3).State == JourneyState.Waiting);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
}

void Transfer()
{
    var system = new TransportSystem(World(4));
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 2, [0, 2])).Success);
    Assert(system.InstallBank(new BankDefinition(2, 3, 2, 4, [2, 4])).Success);
    Assert(system.RequestJourney(1, 0, 1, 4, 3).Success);
    var seen = new HashSet<int>();
    for (var i = 0; i < 150; i++)
    {
        Advance(system, 1);
        foreach (var car in system.Cars.Where(c => c.PassengerCount > 0)) seen.Add(car.BankId);
    }
    Assert(seen.SetEquals([1, 2]));
    Assert(system.Journeys[0].State == JourneyState.Arrived);
}

void Stairs()
{
    var system = new TransportSystem(World(3));
    for (var floor = 0; floor < 3; floor++) Assert(system.BuildStair(floor, 5).Success);
    Assert(system.RequestJourney(1, 0, 3, 3, 7).Success);
    Advance(system, 6);
    Assert(system.Journeys[0].DrawFloor > 0 && system.Journeys[0].DrawFloor < 1);
    Assert(!system.RemoveStair(0, 5).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.CurrentTick == 28);
    Assert(system.RemoveStair(0, 5).Success);
    Assert(!system.CanReach(0, 1, 3, 1));
}

void ServiceAccess()
{
    var system = new TransportSystem(World());
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 5, [0, 5], ServiceOnly: true)).Success);
    Assert(!system.CanReach(0, 1, 5, 1));
    Assert(system.CanReach(0, 1, 5, 1, true));
    Assert(system.RequestJourney(1, 0, 1, 5, 1, true).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
}

void Disconnected()
{
    var system = new TransportSystem(World());
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Assert(system.Journeys[0].State == JourneyState.Unreachable);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 5, [0, 5])).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
}

void StopsChanged()
{
    var system = System();
    Assert(system.RequestJourney(1, 2, 1, 5, 1).Success);
    Assert(system.ConfigureBank(new BankDefinition(1, 1, 0, 5, [0, 5])).Success);
    Advance(system, 1);
    Assert(system.Journeys[0].State == JourneyState.Unreachable && system.Journeys[0].Floor == 2);
    Assert(system.ConfigureBank(new BankDefinition(1, 1, 0, 5, [0, 2, 5])).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
}

void Breakdown()
{
    var system = System();
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Until(system, () => system.Cars[0].State == CarState.Traveling);
    Advance(system, 2);
    var position = system.Cars[0].DrawFloor;
    Assert(system.SetBankOutOfService(1, true).Success);
    Assert(system.Cars[0].DrawFloor == position && system.Journeys[0].State == JourneyState.Riding);
    Until(system, () => system.Cars[0].State == CarState.OutOfService);
    Assert(system.Cars[0].Floor == 5 && system.Cars[0].PassengerCount == 0);
    Advance(system, 1);
    Assert(system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.RequestJourney(2, 0, 1, 5, 1).Success);
    Assert(system.Journeys.Single(j => j.PersonId == 2).State == JourneyState.Unreachable);
    Assert(system.SetBankOutOfService(1, false).Success);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
}

void Cancel()
{
    var system = System();
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Assert(system.CancelJourney(1).Success);
    Advance(system, 20);
    Assert(system.Journeys[0].State == JourneyState.Abandoned && system.Cars[0].PassengerCount == 0);
}

void Abandonment()
{
    var system = System(1);
    for (var id = 1; id <= 5; id++) Assert(system.RequestJourney(id, 0, 1, 5, 1, patienceTicks: 3).Success);
    Assert(system.Metrics.OverloadedFloors.SequenceEqual([0]));
    Advance(system, 3);
    Assert(system.Metrics.AbandonedTrips == 5 && system.Metrics.Waiting == 0);
}

void Geometry()
{
    var world = World();
    var system = new TransportSystem(world);
    var before = world.CashMinor;
    Assert(!system.InstallBank(new BankDefinition(1, 1, 0, 6, [0, 6]), 100).Success);
    Assert(world.CashMinor == before);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 5, [0, 5]), 100).Success);
    Assert(world.CashMinor == before - 100);
    Assert(!system.InstallBank(new BankDefinition(2, 1, 0, 5, [0, 5])).Success);
    Assert(!system.BuildStair(0, 1).Success);
    Assert(system.Occupies(1, 3));
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Assert(!system.RemoveBank(1).Success);
    Assert(system.RemoveBank(1, true).Success);
    Advance(system, 1);
    Assert(system.Journeys[0].State == JourneyState.Unreachable);
}

void SaveContinue()
{
    var world = World();
    var system = new TransportSystem(world);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 3, [0, 2, 3], 2)).Success);
    Assert(system.InstallBank(new BankDefinition(2, 3, 3, 5, [3, 5], 1)).Success);
    for (var id = 1; id <= 12; id++) Assert(system.RequestJourney(id, 0, 1, 5, 4, patienceTicks: 1000).Success);
    Until(system, () => system.Cars.Any(c => c.State == CarState.Traveling));
    Advance(system, 2);
    Assert(system.Cars.Any(c => c.State == CarState.Traveling));
    var json = JsonSerializer.Serialize(system.CaptureSnapshot());
    var restored = TransportSystem.RestoreSnapshot(world, JsonSerializer.Deserialize<TransportSnapshot>(json)!);
    Assert(JsonSerializer.Serialize(restored.CaptureSnapshot()) == json);
    for (var i = 0; i < 350; i++)
    {
        Advance(system, 1); Advance(restored, 1);
        Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(restored.CaptureSnapshot()), $"Continuation diverged at {system.CurrentTick}");
    }
    Assert(system.Journeys.All(j => j.State == JourneyState.Arrived));
}

void MalformedSave()
{
    var world = World();
    var system = new TransportSystem(world);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 5, [0, 5])).Success);
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Until(system, () => system.Cars[0].State == CarState.Traveling);
    var state = system.CaptureSnapshot();
    Throws(() => TransportSystem.RestoreSnapshot(world, state with { CurrentTick = -1 }));
    Throws(() => TransportSystem.RestoreSnapshot(world, state with { Banks = [state.Banks[0] with { Car = state.Banks[0].Car with { Timer = 0 } }] }));
    Throws(() => TransportSystem.RestoreSnapshot(world, state with { Banks = [state.Banks[0] with { Car = state.Banks[0].Car with { Passengers = [1, 1] } }] }));
    Throws(() => TransportSystem.RestoreSnapshot(world, state with { Journeys = [] }));
    Throws(() => TransportSystem.RestoreSnapshot(world, state with { Banks = [state.Banks[0] with { Definition = state.Banks[0].Definition with { Capacity = 0 } }] }));
}

void RushHour()
{
    var system = System(4, 10);
    for (var id = 1; id <= 60; id++) Assert(system.RequestJourney(id, 0, 1, 1 + id % 10, 1, patienceTicks: 5000).Success);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived), 5000);
    Assert(system.Metrics.WaitSampleCount == 60 && system.Metrics.P95WaitTicks >= system.Metrics.AverageWaitTicks);
    Assert(system.Metrics.Utilization is > 0 and <= 1);
}

void Clock()
{
    var system = System();
    Throws(() => system.Step(0)); Throws(() => system.Step(2));
    system.Step(1); Throws(() => system.Step(1));
}

void DemolishedDestination()
{
    var world = World(1);
    var system = new TransportSystem(world);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 1, [0, 1])).Success);
    Assert(system.RequestJourney(1, 0, 1, 1, 1).Success);
    Assert(system.RemoveBank(1, true).Success);
    Assert(world.DemolishFloor(1).Success);
    Advance(system, 1);
    Assert(system.Journeys[0].State == JourneyState.Unreachable && system.Journeys[0].Floor == 0);
    var restored = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
    Assert(restored.Journeys[0].State == JourneyState.Unreachable);
    Assert(world.BuildFloor(1).Success);
    Assert(restored.InstallBank(new BankDefinition(1, 1, 0, 1, [0, 1])).Success);
    Until(restored, () => restored.Journeys[0].State == JourneyState.Arrived);
}

void QueueAge()
{
    var world = World(5);
    var system = new TransportSystem(world);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 5, [0, 5], 1, 10)).Success);
    Assert(system.RequestJourney(1, 0, 1, 5, 1).Success);
    Assert(system.RequestJourney(2, 0, 1, 5, 1, patienceTicks: 20).Success);
    Advance(system, 15);
    var age = system.Journeys.Single(j => j.PersonId == 2).WaitSinceTick;
    Assert(world.BuildFloor(6).Success);
    Advance(system, 1);
    Assert(system.Journeys.Single(j => j.PersonId == 2).WaitSinceTick == age);
    Advance(system, 4);
    Assert(system.Journeys.Single(j => j.PersonId == 2).State == JourneyState.Abandoned);
}

void SafeCancellation()
{
    var system = new TransportSystem(World(1));
    Assert(system.RequestJourney(1, 0, 2, 0, 12).Success);
    Advance(system, 4);
    Assert(system.Journeys[0].X == 6);
    Assert(system.CancelJourney(1).Success);
    Assert(system.Journeys[0].X == 6);
    Assert(system.ForgetJourney(1).Success);
    Assert(system.BuildStair(0, 2).Success);
    Assert(system.RequestJourney(2, 0, 2, 1, 2).Success);
    Advance(system, 4);
    Assert(system.Journeys[0].DrawFloor == .5);
    Assert(!system.CancelJourney(2).Success);
    Assert(system.Journeys[0].DrawFloor == .5);
    Assert(!system.ForgetJourney(2).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.ForgetJourney(2).Success && system.Journeys.Count == 0);
}

void EscalatorDirection()
{
    var world = World(1);
    var system = new TransportSystem(world);
    var cash = world.CashMinor;
    Assert(!system.BuildEscalator(0, 2, 0, 100).Success);
    Assert(!system.BuildEscalator(0, 2, 2, 100).Success);
    Assert(world.CashMinor == cash);
    Assert(system.BuildEscalator(0, 2, 1, 100).Success);
    Assert(world.CashMinor == cash - 100);
    Assert(system.CanReach(0, 2, 1, 2) && !system.CanReach(1, 2, 0, 2));
    Assert(!system.BuildEscalator(0, 2, -1).Success);
    Assert(!system.BuildStair(0, 2).Success);
    Assert(system.RequestJourney(1, 0, 2, 1, 2).Success);
    Advance(system, 4);
    Assert(system.Journeys[0].State == JourneyState.Walking && system.Journeys[0].DrawFloor == .5);
    Assert(!system.RemoveStair(0, 2).Success);
    Advance(system, 4);
    Assert(system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.BuildEscalator(0, 4, -1).Success);
    Assert(system.CanReach(1, 2, 0, 2));
    Assert(system.RequestJourney(1, 1, 2, 0, 2).Success);
    Until(system, () => system.Journeys[0].State == JourneyState.Arrived);
    Assert(system.Journeys[0].Floor == 0);
    Assert(system.RemoveStair(0, 2).Success);
    Assert(!system.CanReach(0, 2, 1, 2) && system.CanReach(1, 2, 0, 2));
}

void EscalatorTransferSave()
{
    var world = World(3);
    var system = new TransportSystem(world);
    Assert(system.BuildEscalator(0, 2, 1).Success);
    Assert(system.BuildEscalator(0, 3, -1).Success);
    Assert(system.InstallBank(new BankDefinition(1, 4, 1, 3, [1, 3])).Success);
    Assert(system.RequestJourney(1, 0, 2, 3, 5).Success);
    Advance(system, 4);
    Assert(system.Journeys[0].DrawFloor == .5);
    var json = JsonSerializer.Serialize(system.CaptureSnapshot());
    var restored = TransportSystem.RestoreSnapshot(world, JsonSerializer.Deserialize<TransportSnapshot>(json)!);
    Assert(restored.Stairs.Single(s => s.X == 2).Direction == 1);
    Assert(restored.Stairs.Single(s => s.X == 3).Direction == -1);
    for (var i = 0; i < 50; i++)
    {
        Advance(system, 1); Advance(restored, 1);
        Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(restored.CaptureSnapshot()));
    }
    Assert(restored.Journeys[0].State == JourneyState.Arrived && restored.Metrics.WaitSampleCount == 1);
    var snapshot = system.CaptureSnapshot();
    Throws(() => TransportSystem.RestoreSnapshot(world, snapshot with { Stairs = [snapshot.Stairs[0] with { Direction = 2 }] }));
    Throws(() => TransportSystem.RestoreSnapshot(world, snapshot with { Stairs = [snapshot.Stairs[0], snapshot.Stairs[0] with { Direction = -1 }] }));
}

void StarvationPrevention()
{
    var system = System(1);
    Assert(system.RequestJourney(1, 0, 1, 5, 1, patienceTicks: 5000).Success);
    Until(system, () => system.Cars[0].PassengerCount == 1);
    // This older call points up and cannot join the full car. Opposing terminal queues
    // arrive later and remain saturated throughout the experiment.
    Assert(system.RequestJourney(2, 2, 1, 5, 1, patienceTicks: 5000).Success);
    Advance(system, 1);
    for (var id = 3; id < 25; id++)
        Assert(system.RequestJourney(id, id % 2 == 0 ? 0 : 5, 1, id % 2 == 0 ? 5 : 0, 1, patienceTicks: 5000).Success);
    Until(system, () => system.Journeys.Single(j => j.PersonId == 2).State == JourneyState.Arrived, 100);
    Assert(system.Journeys.Where(j => j.PersonId > 2).All(j => j.State == JourneyState.Waiting));
}

void PausedAccessQueries()
{
    var world = World(1);
    var system = new TransportSystem(world);
    Assert(system.InstallBank(new BankDefinition(1, 1, 0, 1, [0, 1])).Success);
    Assert(system.RequestJourney(1, 0, 1, 1, 1).Success);
    Assert(system.CanReach(0, 1, 1, 1));
    var version = system.TopologyVersion;
    Assert(world.BuildRoom("cafe", 5, 1).Success);
    var paused = JsonSerializer.Serialize(system.CaptureSnapshot());
    var building = JsonSerializer.Serialize(world.CaptureSnapshot());
    for (var i = 0; i < 2; i++)
    {
        Assert(system.CanReach(0, 1, 1, 6));
        Assert(!system.CanReach(2, 0, 2, 4));
        Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == paused);
        Assert(JsonSerializer.Serialize(world.CaptureSnapshot()) == building);
    }
    var restored = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
    Advance(system, 1); Advance(restored, 1);
    Assert(system.TopologyVersion == version + 1);
    Assert(system.CaptureSnapshot().ObservedWorldTopologyVersion == world.TopologyVersion);
    Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(restored.CaptureSnapshot()));
    Assert(world.BuildFloor(2).Success);
    paused = JsonSerializer.Serialize(system.CaptureSnapshot());
    Assert(system.CanReach(2, 0, 2, 4) && system.CanReach(2, 0, 2, 4));
    Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == paused);
    Assert(system.RequestJourney(2, 2, 0, 2, 4).Success);
    Assert(system.TopologyVersion == version + 2);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
    Assert(system.TopologyVersion == version + 2);
}

void CoordinatedGeometry()
{
    var world = World(5); var system = new TransportSystem(world);
    Assert(system.InstallBank(new(1, 1, 0, 5, [0, 5])).Success);
    var cash = world.CashMinor;
    Assert(!system.AddCar(1, 2, 1, 100).Success && !system.AddCar(1, 1, 2, 100).Success);
    Assert(!system.AddCar(1, 2, ConstructionWorld.Width, 100).Success && world.CashMinor == cash);
    Assert(system.AddCar(1, 2, 2, 100).Success && world.CashMinor == cash - 100);
    Assert(system.Banks[0].CarCount == 2 && system.Banks[0].AvailableCarCount == 2);
    Assert(system.Cars.Select(car => car.X).SequenceEqual([1, 2]) && system.Cars.All(car => car.CarId == car.ShaftId));
    Assert(system.Occupies(2, 3) && !system.BuildStair(1, 2).Success);
    Assert(!system.InstallBank(new(2, 2, 0, 5, [0, 5])).Success);
    system.Banks[0].Definition.AdditionalShafts![0] = new(2, 7);
    Assert(system.Cars[1].X == 2 && system.Banks[0].Definition.AdditionalShafts![0].X == 2);
    Assert(!system.RemoveCar(1, 1).Success && system.RemoveCar(1, 2).Success && !system.Occupies(2, 3));
}

void CoordinatedTransfer()
{
    var system = new TransportSystem(World(6));
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 2, 2, 2)).Success);
    Assert(system.AddCar(1, 2, 2).Success);
    Assert(system.InstallBank(new(2, 5, 3, 6, [3, 6], 3, 2, 2)).Success);
    var used = Enumerable.Range(1, 24).ToDictionary(id => (long)id, _ => new HashSet<int>());
    var cars = new HashSet<(int, int)>(); var walked = new HashSet<long>();
    for (var id = 1; id <= 24; id++) Assert(system.RequestJourney(id, 0, 0, 6, 7, patienceTicks: 5000).Success);
    for (var tick = 0; tick < 2500 && system.Journeys.Any(j => j.State != JourneyState.Arrived); tick++)
    {
        Advance(system, 1); Assert(system.Journeys.Count == 24);
        foreach (var car in system.Cars.Where(car => car.PassengerCount > 0))
        {
            cars.Add((car.BankId, car.CarId)); foreach (var id in car.PassengerIds) used[id].Add(car.BankId);
        }
        foreach (var person in system.Journeys.Where(j => j.State == JourneyState.Walking && j.Floor == 3 && j.X > 2 && j.X < 5)) walked.Add(person.PersonId);
    }
    Assert(system.Journeys.All(j => j.State == JourneyState.Arrived && j.TransferCount == 1 && j.RemainingRoute!.Count == 0));
    Assert(used.Values.All(banks => banks.SetEquals([1, 2])) && walked.Count == 24);
    Assert(cars.SetEquals([(1, 1), (1, 2), (2, 1)]) && system.Metrics.WaitSampleCount == 48 && system.Metrics.AbandonedTrips == 0);
}

void AssignedApproach()
{
    var system = new TransportSystem(World(2));
    Assert(system.InstallBank(new(1, 1, 0, 2, [0, 2], 1)).Success);
    Assert(system.AddCar(1, 2, 5).Success);
    Assert(system.RequestJourney(1, 0, 1, 2, 1).Success && system.RequestJourney(2, 0, 1, 2, 1).Success);
    Advance(system, 1);
    var approaching = system.JourneyFor(2)!;
    Assert(approaching.State == JourneyState.Walking && approaching.CarId == 2 && approaching.X == 1);
    Assert(approaching.RemainingRoute![0] == new RouteLeg(RouteKind.Walk, 0, 1, 0, 5));
    Advance(system, 2);
    Assert(system.JourneyFor(2)!.X == 3 && system.JourneyFor(2)!.State == JourneyState.Walking);
    Until(system, () => system.JourneyFor(2)!.State == JourneyState.Riding);
    Assert(system.JourneyFor(2)!.X == 5 && system.Cars.Single(car => car.CarId == 2).PassengerIds.Contains(2));
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
    Assert(system.Journeys.All(j => j.X == 1 && j.CarId == null));
}

void CoordinatedCancellation()
{
    var system = new TransportSystem(World(3));
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 1)).Success);
    Assert(system.AddCar(1, 2, 5).Success);
    for (var id = 1; id <= 8; id++) Assert(system.RequestJourney(id, 0, 1, 3, 1, patienceTicks: 1000).Success);
    Advance(system, 1);
    Assert(system.JourneyFor(2)!.State == JourneyState.Walking && system.CancelJourney(2).Success);
    var cancelledX = system.JourneyFor(2)!.X;
    Until(system, () => system.Journeys.Where(j => j.PersonId != 2).All(j => j.State == JourneyState.Arrived));
    Assert(system.JourneyFor(2)!.State == JourneyState.Abandoned && system.JourneyFor(2)!.X == cancelledX);
    Assert(system.Metrics.WaitSampleCount == 7 && system.Cars.All(car => !car.PassengerIds.Contains(2)));
}

void IndividualOutage()
{
    var system = new TransportSystem(World(5));
    Assert(system.InstallBank(new(1, 1, 0, 5, [0, 5], 2, 10, 2)).Success && system.AddCar(1, 2, 2).Success);
    for (var id = 1; id <= 12; id++) Assert(system.RequestJourney(id, 0, 1, 5, 1, patienceTicks: 5000).Success);
    Until(system, () => system.Cars.All(car => car.State == CarState.Traveling && car.PassengerCount > 0));
    var car = system.Cars.Single(car => car.CarId == 2); var riders = car.PassengerIds.ToArray(); var position = car.DrawFloor;
    Assert(system.SetCarOutOfService(1, 2, true).Success);
    Assert(system.Cars.Single(car => car.CarId == 2).DrawFloor == position && riders.All(id => system.JourneyFor(id)!.State == JourneyState.Riding));
    Assert(system.Banks[0].AvailableCarCount == 1 && !system.Banks[0].IsOutOfService);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived), 5000);
    Assert(system.Cars.Single(car => car.CarId == 2).State == CarState.OutOfService && system.Metrics.AbandonedTrips == 0);
    Assert(system.SetBankOutOfService(1, true).Success && system.SetCarOutOfService(1, 2, false).Success);
    Assert(system.Banks[0].AvailableCarCount == 0);
    Assert(system.SetBankOutOfService(1, false).Success && system.Banks[0].AvailableCarCount == 2);
}

void ReassignmentAge()
{
    var system = new TransportSystem(World(5));
    Assert(system.InstallBank(new(1, 1, 0, 5, [0, 5], 1, 10, 2)).Success && system.AddCar(1, 2, 2).Success);
    for (var id = 1; id <= 6; id++) Assert(system.RequestJourney(id, 0, 1, 5, 1, patienceTicks: 5000).Success);
    Until(system, () => system.Cars.All(car => car.State == CarState.Traveling));
    var waiting = system.Journeys.First(j => j.State == JourneyState.Waiting && j.CarId == 2);
    var age = waiting.WaitSinceTick;
    Assert(system.SetCarOutOfService(1, 2, true).Success);
    Until(system, () => system.JourneyFor(waiting.PersonId) is { State: JourneyState.Waiting, CarId: 1 });
    Assert(system.JourneyFor(waiting.PersonId)!.WaitSinceTick == age);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived), 5000);
}

void CoordinatedEdits()
{
    var system = new TransportSystem(World(3));
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 1)).Success && system.AddCar(1, 2, 4).Success);
    Assert(system.RequestJourney(1, 0, 1, 3, 1).Success && system.RequestJourney(2, 0, 1, 3, 1).Success);
    Advance(system, 1); var before = JsonSerializer.Serialize(system.CaptureSnapshot());
    Assert(!system.RemoveCar(1, 2).Success && !system.RemoveBank(1).Success);
    Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == before);
    Until(system, () => system.Cars.Any(car => car.State == CarState.Traveling)); before = JsonSerializer.Serialize(system.CaptureSnapshot());
    Assert(!system.ConfigureBank(system.Banks[0].Definition with { Capacity = 2 }).Success);
    Assert(!system.RemoveBank(1, true).Success && JsonSerializer.Serialize(system.CaptureSnapshot()) == before);
    Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
    Assert(system.RemoveCar(1, 2).Success && system.Cars.Count == 1);
}

void RouteDiagnostics()
{
    var system = new TransportSystem(World(6));
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], ServiceOnly: true)).Success);
    Assert(system.AddCar(1, 2, 2).Success && system.InstallBank(new(2, 5, 3, 6, [3, 6], ServiceOnly: true)).Success);
    var before = JsonSerializer.Serialize(system.CaptureSnapshot());
    Assert(system.DiagnoseRoute(0, 0, 6, 7).Status == RouteStatus.AccessDenied);
    var service = system.DiagnoseRoute(0, 0, 6, 7, true);
    Assert(service.Status == RouteStatus.Reachable && service.Route.Count(leg => leg.Kind == RouteKind.Elevator) == 2);
    Assert(system.DiagnoseRoute(0, 0, 2, 7, true).Status == RouteStatus.Disconnected);
    Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == before);
    Assert(system.SetBankOutOfService(2, true).Success);
    Assert(system.DiagnoseRoute(0, 0, 6, 7, true).Status == RouteStatus.TemporarilyUnavailable);
    Assert(system.DiagnoseRoute(0, 0, 6, 7).Status == RouteStatus.AccessDenied);
}

void CoordinatedSave()
{
    var world = World(6); var system = new TransportSystem(world);
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 2)).Success && system.AddCar(1, 2, 4).Success);
    Assert(system.InstallBank(new(2, 7, 3, 6, [3, 6], 1)).Success);
    for (var id = 1; id <= 16; id++) Assert(system.RequestJourney(id, 0, 1, 6, 8, patienceTicks: 5000).Success);
    Advance(system, 1);
    Assert(system.Journeys.Any(j => j.State == JourneyState.Walking && j.CarId == 2));
    var restored = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
    for (var tick = 0; tick < 1000; tick++)
    {
        Advance(system, 1); Advance(restored, 1);
        Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(restored.CaptureSnapshot()), $"Coordinated save diverged at tick {system.CurrentTick}.");
        if (tick is 10 or 80 or 180) restored = TransportSystem.RestoreSnapshot(world, restored.CaptureSnapshot());
    }
    Assert(system.Journeys.All(j => j.State == JourneyState.Arrived && j.TransferCount == 1));
}

void BankPressure()
{
    var system = new TransportSystem(World(3));
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 1)).Success);
    Assert(system.InstallBank(new(2, 8, 0, 3, [0, 3], 200, ServiceOnly: true)).Success);
    Assert(system.RequestJourney(1, 0, 1, 3, 1).Success && system.RequestJourney(2, 0, 1, 3, 1).Success);
    Assert(system.Metrics.OverloadedFloors.SequenceEqual([0]));
    Assert(system.AddCar(1, 2, 2).Success && system.Metrics.OverloadedFloors.Count == 0);
    Assert(system.SetCarOutOfService(1, 2, true).Success && system.Metrics.OverloadedFloors.SequenceEqual([0]));
}

void CapacityEntry()
{
    foreach (var restore in new[] { false, true })
    {
        var world = World(5); var system = new TransportSystem(world);
        Assert(system.InstallBank(new(1, 1, 0, 5, [0, 5], 1, 10, 2)).Success);
        if (restore) Assert(system.AddCar(1, 2, 5).Success && system.SetCarOutOfService(1, 2, true).Success);
        for (var id = 1; id <= 6; id++) Assert(system.RequestJourney(id, 0, 1, 5, 1, patienceTicks: 5000).Success);
        Until(system, () => system.Cars.Single(car => car.CarId == 1).State == CarState.Traveling);
        var queued = system.Journeys.Where(j => j.State == JourneyState.Waiting).ToArray();
        Assert(queued.Length == 5 && queued.All(j => j.CarId == 1));
        Assert((restore ? system.SetCarOutOfService(1, 2, false) : system.AddCar(1, 2, 5)).Success);
        foreach (var previous in queued)
        {
            var current = system.JourneyFor(previous.PersonId)!;
            Assert(current.State == JourneyState.Waiting && current.CarId == null && current.X == previous.X
                && current.WaitSinceTick == previous.WaitSinceTick);
        }
        var resumed = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
        var newCarUsed = false; var approached = false;
        for (var tick = 0; tick < 1500 && system.Journeys.Any(j => j.State != JourneyState.Arrived); tick++)
        {
            Advance(system, 1); Advance(resumed, 1);
            Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(resumed.CaptureSnapshot()));
            foreach (var previous in queued)
            {
                var current = system.JourneyFor(previous.PersonId)!;
                if (current.State == JourneyState.Waiting) Assert(current.WaitSinceTick == previous.WaitSinceTick);
                if (current.State == JourneyState.Walking && current.CarId == 2 && current.Floor == 0 && current.X > 1 && current.X < 5)
                    approached = true;
                if (current.State == JourneyState.Riding && current.CarId == 2) { Assert(current.X == 5); newCarUsed = true; }
            }
        }
        Assert(newCarUsed && approached && system.Journeys.All(j => j.State == JourneyState.Arrived));
        Assert(system.Metrics.WaitSampleCount == 6 && system.Metrics.AbandonedTrips == 0);
    }
    var approachingSystem = new TransportSystem(World(2));
    Assert(approachingSystem.InstallBank(new(1, 1, 0, 2, [0, 2], 1)).Success && approachingSystem.AddCar(1, 2, 5).Success);
    Assert(approachingSystem.RequestJourney(1, 0, 1, 2, 1).Success && approachingSystem.RequestJourney(2, 0, 1, 2, 1).Success);
    Advance(approachingSystem, 2);
    var approachBefore = approachingSystem.JourneyFor(2)!;
    Assert(approachBefore.State == JourneyState.Walking && approachBefore.CarId == 2);
    Assert(approachingSystem.AddCar(1, 3, 8).Success);
    var approachAfter = approachingSystem.JourneyFor(2)!;
    Assert(approachAfter.State == JourneyState.Walking && approachAfter.CarId == 2 && approachAfter.X == approachBefore.X
        && approachAfter.RemainingRoute!.SequenceEqual(approachBefore.RemainingRoute!));
}

void ReinstalledShaft()
{
    var world = World(3); var system = new TransportSystem(world);
    Assert(system.InstallBank(new(1, 1, 0, 3, [0, 3], 1, 10, 2)).Success && system.AddCar(1, 2, 5).Success);
    Assert(system.SetCarOutOfService(1, 1, true).Success && system.RequestJourney(1, 0, 1, 3, 1).Success);
    Until(system, () => system.JourneyFor(1) is { State: JourneyState.Waiting, CarId: 2 });
    var before = system.JourneyFor(1)!; Assert(before.X == 5);
    Assert(system.RemoveBank(1, true).Success);
    Assert(system.InstallBank(new(1, 2, 0, 3, [0, 3], 1, 10, 2, AdditionalShafts: [new(2, 7)])).Success);
    Assert(system.SetCarOutOfService(1, 1, true).Success);
    var resumed = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
    Advance(system, 1); Advance(resumed, 1);
    Assert(system.JourneyFor(1) is { State: JourneyState.Walking, X: 5, CarId: 2 });
    Advance(system, 1); Advance(resumed, 1);
    Assert(system.JourneyFor(1) is { State: JourneyState.Walking, X: 6 });
    Until(system, () => system.JourneyFor(1)!.State == JourneyState.Riding);
    Advance(resumed, (int)(system.CurrentTick - resumed.CurrentTick));
    Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(resumed.CaptureSnapshot()));
    Assert(system.JourneyFor(1)!.X == 7 && system.Metrics.WaitSampleCount == 1);
    Assert(system.Metrics.AverageWaitTicks == system.CurrentTick - before.WaitSinceTick);
    Until(system, () => system.JourneyFor(1)!.State == JourneyState.Arrived);
    Assert(system.JourneyFor(1)!.X == 1);
}

void ApproachHandoff()
{
    foreach (var restore in new[] { false, true })
    {
        var world = World(5); var system = new TransportSystem(world);
        Assert(system.InstallBank(new(1, 1, 0, 5, [0, 5], 1, 10, 2)).Success && system.AddCar(1, 2, 8).Success);
        if (restore) Assert(system.AddCar(1, 3, 12).Success && system.SetCarOutOfService(1, 3, true).Success);
        Advance(system, 10);
        Assert(system.RequestJourney(1, 0, 1, 5, 1).Success && system.RequestJourney(2, 0, 1, 5, 1).Success);
        Advance(system, 2);
        var before = system.JourneyFor(2)!;
        var originalWaitSince = system.CaptureSnapshot().Journeys.Single(j => j.PersonId == 2).WaitSinceTick;
        Assert(before.State == JourneyState.Walking && before.CarId == 2 && before.X > 1 && before.X < 3 && originalWaitSince == 10,
            "Fixture must start with an assigned in-progress approach and an established hall request age.");
        Assert((restore ? system.SetCarOutOfService(1, 3, false) : system.AddCar(1, 3, 12)).Success);
        var resumed = TransportSystem.RestoreSnapshot(world, system.CaptureSnapshot());
        var previousX = before.X; var reachedShaft = false;
        for (var tick = 0; tick < 100 && system.JourneyFor(2)!.State != JourneyState.Riding; tick++)
        {
            Advance(system, 1); Advance(resumed, 1);
            Assert(JsonSerializer.Serialize(system.CaptureSnapshot()) == JsonSerializer.Serialize(resumed.CaptureSnapshot()));
            var current = system.JourneyFor(2)!;
            Assert(current.CarId == 2, "Capacity change dropped the existing approach commitment at its handoff.");
            Assert(current.Floor == 0 && current.X >= previousX && current.X <= 8,
                "A committed approach must reach its actual shaft without walking back or teleporting.");
            Assert(system.CaptureSnapshot().Journeys.Single(j => j.PersonId == 2).WaitSinceTick == originalWaitSince,
                "The original hall request age must survive the completed approach.");
            if (current.State == JourneyState.Waiting)
            {
                Assert(current.X == 8 && current.WaitSinceTick == originalWaitSince);
                if (!reachedShaft) resumed = TransportSystem.RestoreSnapshot(world, resumed.CaptureSnapshot());
                reachedShaft = true;
            }
            previousX = current.X;
        }
        Assert(reachedShaft && system.JourneyFor(2) is { State: JourneyState.Riding, CarId: 2, X: 8 });
        Assert(system.JourneyFor(2)!.TotalWaitTicks == system.CurrentTick - originalWaitSince);
        Until(system, () => system.Journeys.All(j => j.State == JourneyState.Arrived));
        Assert(system.Metrics.WaitSampleCount == 2 && system.Metrics.AbandonedTrips == 0);
    }
}

static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected argument rejection."); }
