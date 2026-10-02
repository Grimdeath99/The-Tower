using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

public static partial class Program
{
    private static (GameSession Game, long Office, long Depot) ServiceFixture(bool elevator = true)
    {
        var game = NewGame();
        Succeed(game.World.BuildFloor(1));
        Build(game, "lobby", 0, 0);
        var office = Build(game, "office", 0, 1);
        Succeed(game.SetOpen(office, false));
        // Natural logical-time degradation creates the need; no runtime state is injected.
        game.Advance(16 * 3600);
        var depot = Build(game, "service-room", 8, 0);
        Succeed(game.SetStaff(depot, 1));
        if (elevator) Succeed(game.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1]), Rules.ElevatorCostMinor));
        return (game, office, depot);
    }

    private static void AssertServiceOwnership(GameSession game)
    {
        var tasks = game.ServiceTasks.Where(task => task.Status is ServiceTaskStatus.Assigned or ServiceTaskStatus.Traveling or ServiceTaskStatus.InProgress).ToArray();
        Check(tasks.Select(task => task.RoomId).Distinct().Count() == tasks.Length, "Two tasks claimed one room.");
        Check(tasks.Select(task => task.WorkerPersonId).Distinct().Count() == tasks.Length, "Worker claimed more than one task.");
        foreach (var task in tasks)
        {
            var worker = game.People.SingleOrDefault(person => person.Id == task.WorkerPersonId);
            Check(worker is { Role: "Staff" } && worker.RoomId == task.DepotRoomId && worker.ServiceTargetId == task.RoomId,
                "Task and worker ownership disagree.");
            Check(game.Transport.JourneyFor(worker!.Id)?.Service == true, "Worker has no actual service journey.");
        }
        Check(game.People.Where(person => person.Role == "Staff").All(person => game.Transport.JourneyFor(person.Id) != null), "An active staff person lost its journey.");
    }

    static partial void RegisterServiceCases(List<(string Name, Action Run)> cases)
    {
        cases.Add(("Services: blocked route retains one task and restored access permits physical paid completion", () =>
        {
            var (game, office, _) = ServiceFixture(elevator: false);
            Until(game, () => game.ServiceTaskForRoom(office) is { Status: ServiceTaskStatus.Blocked } job && job.Reason.Contains("route"),
                1200, "Inaccessible room never reported its service-route blocker");
            var blocked = game.ServiceTaskForRoom(office)!;
            Check(blocked.Status == ServiceTaskStatus.Blocked && blocked.Reason.Contains("route"), "Inaccessible room has no explanatory blocked task.");
            var condition = game.OperationFor(office)!.Condition;
            game.Advance(180);
            Check(game.ServiceTaskForRoom(office)!.Id == blocked.Id && game.OperationFor(office)!.Condition == condition, "Blocked task was duplicated or serviced remotely.");
            Check(game.People.All(person => person.ServiceTargetId != office), "Worker reached a disconnected room.");
            Succeed(game.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1]), Rules.ElevatorCostMinor));
            Until(game, () => game.ServiceTaskForRoom(office)?.Status == ServiceTaskStatus.InProgress, 3600, "Restored route never delivered a worker");
            var working = game.ServiceTaskForRoom(office)!;
            Check(working.Id == blocked.Id && working.WorkDueTick > game.Tick, "Task identity or work duration was lost.");
            AssertServiceOwnership(game);
            var costBefore = game.OperationFor(office)!.CostsMinor;
            game.Advance(checked((int)(working.WorkDueTick!.Value - game.Tick - 1)));
            Check(game.OperationFor(office)!.Condition < 85 && game.OperationFor(office)!.CostsMinor == costBefore, "Room changed or paid before on-site work finished.");
            game.Step();
            Check(game.OperationFor(office) is { Condition: 100, Cleanliness: 100, Dirty: false }, "Completed service failed to restore room state.");
            Check(game.ServiceTasks.Single(task => task.Id == working.Id).Status == ServiceTaskStatus.Completed, "Task did not reach Completed.");
            Check(game.OperationFor(office)!.CostsMinor - costBefore == Rules.RepairCostMinor + Rules.Management.CleaningCostMinor,
                "Combined service did not charge its exact once-only materials.");
            Check(game.World.Ledger.Count(entry => entry.EntityId == office && entry.Category == "Maintenance.Cleaning") == 1, "Cleaning cost was missing or duplicated.");
            Check(game.World.Ledger.Count(entry => entry.EntityId == office && entry.Category == "Maintenance.Repair") == 1, "Repair cost was missing or duplicated.");
        }));

        cases.Add(("Services: repeated dispatch cannot double-claim and dismissal is safe until return", () =>
        {
            var (game, office, depot) = ServiceFixture();
            Succeed(game.RepairRoom(office));
            var job = game.ServiceTaskForRoom(office)!;
            Succeed(game.RepairRoom(office)); Succeed(game.RepairRoom(office));
            Check(game.ServiceTasks.Count(task => task.RoomId == office && task.Status is not (ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled)) == 1,
                "Repeated repair requests created duplicate work.");
            Check(game.People.Count(person => person.ServiceTargetId == office) == 1, "Two workers were assigned to one task.");
            var before = game.Serialize();
            Check(!game.SetStaff(depot, 0).Success, "An active dispatched worker was unsafely dismissed.");
            Check(before == game.Serialize(), "Rejected dismissal changed authoritative state.");
            Succeed(game.SetOpen(depot, false));
            Check(game.ServiceTasks.Single(task => task.Id == job.Id).Status == ServiceTaskStatus.Cancelled, "Depot closure did not cancel its task.");
            Until(game, () => game.People.All(person => person.Role != "Staff"), 1200, "Cancelled worker did not physically return");
            Succeed(game.SetStaff(depot, 0));
            Check(game.StaffSummaries.Single(summary => summary.DepotRoomId == depot) is { Hired: 0, Dispatched: 0, Available: 0 }, "Dismissed staff summary retained active workers.");
            AssertServiceOwnership(game);
        }));

        cases.Add(("Services: closing a depot while its worker rides preserves the passenger until safe return", () =>
        {
            var (game, office, depot) = ServiceFixture();
            Succeed(game.RepairRoom(office));
            var task = game.ServiceTaskForRoom(office)!;
            var workerId = task.WorkerPersonId!.Value;
            Until(game, () => game.Transport.JourneyFor(workerId)?.State == JourneyState.Riding, 600, "Worker never entered elevator");
            Succeed(game.SetOpen(depot, false));
            Check(game.People.Any(person => person.Id == workerId) && game.Transport.Cars.Any(car => car.PassengerIds.Contains(workerId)),
                "Cancellation removed a rider inside a car.");
            Check(game.ServiceTasks.Single(job => job.Id == task.Id).Status == ServiceTaskStatus.Cancelled, "Cancelled travel still owned an active task.");
            Until(game, () => game.People.All(person => person.Id != workerId), 1800, "Worker failed to return after a safe landing");
            Check(game.Transport.Cars.All(car => !car.PassengerIds.Contains(workerId)) && game.Transport.JourneyFor(workerId) == null, "Cancelled worker left transport ownership behind.");
            Check(!game.World.Ledger.Any(entry => entry.EntityId == office && entry.Category.StartsWith("Maintenance.", StringComparison.Ordinal)), "Cancelled work charged or serviced its target.");
        }));

        cases.Add(("Services: disruption before arrival blocks work and recovery reuses the task", () =>
        {
            var (game, office, _) = ServiceFixture();
            Succeed(game.RepairRoom(office));
            var id = game.ServiceTaskForRoom(office)!.Id;
            Succeed(game.Transport.SetBankOutOfService(1, true));
            Until(game, () => game.ServiceTaskForRoom(office)?.Status == ServiceTaskStatus.Blocked, 600, "Disrupted task never reported Blocked");
            Check(game.OperationFor(office)!.Condition < 85, "Disconnected work repaired the room.");
            Succeed(game.Transport.SetBankOutOfService(1, false));
            Until(game, () => game.ServiceTasks.Any(task => task.Id == id && task.Status == ServiceTaskStatus.Completed), 3600, "Restored route never completed the original task");
            AssertServiceOwnership(game);
        }));

        cases.Add(("Services: demolition cancels an unclaimed job without stale live room references", () =>
        {
            var (game, office, _) = ServiceFixture(elevator: false);
            game.Advance(60);
            var id = game.ServiceTaskForRoom(office)!.Id;
            Succeed(game.DemolishRoom(office));
            Check(game.ServiceTaskForRoom(office) == null && game.ServiceTasks.Single(task => task.Id == id).Status == ServiceTaskStatus.Cancelled,
                "Demolished room retained active work.");
            var restored = GameSession.Deserialize(Catalog, Rules, Locations, game.Serialize());
            Check(restored.Serialize() == game.Serialize(), "Historical cancelled job could not round-trip after demolition.");
        }));

        cases.Add(("Services: in-progress work saves its identity deadline ownership and exact future charges", () =>
        {
            var (game, office, _) = ServiceFixture();
            Succeed(game.RepairRoom(office));
            Until(game, () => game.ServiceTaskForRoom(office)?.Status == ServiceTaskStatus.InProgress, 1200, "Worker did not begin on-site work");
            game.Advance(30);
            var before = game.Serialize();
            var restored = GameSession.Deserialize(Catalog, Rules, Locations, before);
            Check(restored.Serialize() == before && restored.ServiceTaskForRoom(office) == game.ServiceTaskForRoom(office), "Task identity/deadline changed on load.");
            for (var second = 0; second < 1200; second++)
            {
                game.Step(); restored.Step();
                AssertServiceOwnership(game); AssertServiceOwnership(restored);
            }
            Check(game.Serialize() == restored.Serialize(), "Resumed service diverged from uninterrupted physical work.");
            Check(game.ServiceTasks.Any(task => task.RoomId == office && task.Status == ServiceTaskStatus.Completed), "Saved work never completed.");
        }));

        cases.Add(("Services: failed material payment is atomic and queued work can recover", () =>
        {
            var (game, office, _) = ServiceFixture();
            Succeed(game.RepairRoom(office));
            Until(game, () => game.ServiceTaskForRoom(office)?.Status == ServiceTaskStatus.InProgress, 1200, "Worker did not reach task");
            var task = game.ServiceTaskForRoom(office)!;
            Succeed(game.World.ApplyOperatingTransaction(-game.World.CashMinor, game.Tick, "Fixture.Cash", null, "Fixture removes available cash."));
            var op = game.OperationFor(office)!;
            game.Advance(checked((int)(task.WorkDueTick!.Value - game.Tick)));
            Check(game.ServiceTaskForRoom(office)?.Status == ServiceTaskStatus.Blocked, "Unfunded material cost did not block work.");
            Check(game.OperationFor(office)!.Condition == op.Condition && game.OperationFor(office)!.Cleanliness == op.Cleanliness
                && game.OperationFor(office)!.CostsMinor == op.CostsMinor, "Failed material batch partly repaired, cleaned, or charged the room.");
            Succeed(game.World.ApplyOperatingTransaction(100_000, game.Tick, "Fixture.Cash", null, "Fixture restores funds for recovery."));
            Until(game, () => game.ServiceTasks.Any(job => job.Id == task.Id && job.Status == ServiceTaskStatus.Completed), 3600, "Funded queued task failed to recover");
            Check(game.OperationFor(office)!.Condition == 100, "Recovered service did not restore condition.");
        }));

        cases.Add(("Services: task history is bounded and inspection cannot advance authoritative state", () =>
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(Rules, SimulationRules.JsonOptions))!;
            json["management"]!["serviceHistoryLimit"] = 16;
            json["management"]!["serviceCleanThreshold"] = 100;
            json["management"]!["serviceRepairThreshold"] = 100;
            var tuning = SimulationRules.Load(json.ToJsonString(), Catalog);
            var game = new GameSession(Catalog, tuning, Locations);
            Build(game, "lobby", 0, 0);
            Build(game, "service-room", 8, 0);
            var office = Build(game, "office", 16, 0);
            Succeed(game.SetOpen(office, false));
            game.Advance(36 * 3600);
            var terminal = game.ServiceTasks.Where(task => task.Status is ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled).ToArray();
            Check(terminal.Length <= 16 && terminal.Length > 0 && game.ServiceTasks.Max(task => task.Id) > 16,
                "Service history was unbounded or stable IDs were reused.");
            var before = game.Serialize();
            for (var i = 0; i < 10; i++)
            {
                _ = game.ServiceTasks; _ = game.StaffSummaries; _ = game.ServiceTaskForRoom(office); _ = game.IsRoomBeingServiced(office);
            }
            Check(game.Serialize() == before, "Service panel getters changed state.");
            var restored = GameSession.Deserialize(Catalog, tuning, Locations, before);
            game.Advance(600); restored.Advance(600);
            Check(game.Serialize() == restored.Serialize(), "Bounded task history changed future work after load.");
        }));

        cases.Add(("Services: an arriving hotel reservation excludes routine work until checkout", () =>
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(Rules, SimulationRules.JsonOptions))!;
            json["management"]!["serviceCleanThreshold"] = 100;
            var tuning = SimulationRules.Load(json.ToJsonString(), Catalog);
            var game = new GameSession(Catalog, tuning, Locations);
            Succeed(game.World.BuildFloor(1));
            Build(game, "lobby", 0, 0);
            var hotel = Build(game, "hotel-room", 0, 1);
            Succeed(game.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1], TravelTicksPerFloor: 60), tuning.ElevatorCostMinor));
            Until(game, () => game.HotelBookingFor(hotel)?.Status == HotelBookingStatus.Reserved, 12 * 3600, "Hotel demand did not create a reservation");
            var depot = Build(game, "service-room", 8, 0);
            Succeed(game.SetStaff(depot, 1));
            Check(!game.RepairRoom(hotel).Success, "A traveling guest's reserved room was assigned to a worker.");
            var task = game.ServiceTaskForRoom(hotel)!;
            Check(task.Status == ServiceTaskStatus.Blocked && task.WorkerPersonId == null, "Reserved inventory did not block routine cleaning.");
            Until(game, () => game.HotelBookingFor(hotel)?.Status == HotelBookingStatus.CheckedIn, 1200, "Reservation failed to become a stay");
            Check(game.ServiceTaskForRoom(hotel)?.Status == ServiceTaskStatus.Blocked && !game.IsRoomBeingServiced(hotel), "Room service overlapped the active stay.");
            Succeed(game.SetOpen(hotel, false));
            Until(game, () => game.ServiceTasks.Any(job => job.Id == task.Id && job.Status == ServiceTaskStatus.Completed), 3600,
                "Checkout failed to release the previously blocked cleaning task");
            Check(!game.OperationFor(hotel)!.Dirty, "Completed post-checkout work left the hotel dirty.");
        }));
    }
}
