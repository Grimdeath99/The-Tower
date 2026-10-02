using System.Text.Json;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

internal static class TransferCases
{
    internal static void Register(List<(string Name, Action Test)> cases, ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
    {
        GameSession Restore(GameSession game) => GameSession.Deserialize(catalog, rules, locations, game.Serialize());
        cases.Add(("Transfer scenario: normal costed workers use both cars, physically transfer, pay once and return", () =>
        {
            var game = TransferScenario.Create(catalog, rules, locations);
            Check(!game.Sandbox && game.World.Rank == 1 && game.World.Floors.Count == 20 && game.Transport.Stairs.Count == 0,
                "Transfer factory bypassed normal rank/budget or provided a stair shortcut.");
            Check(game.Transport.Cars.Count == 3 && game.Transport.Banks.Count == 2
                && game.World.Ledger.Count(e => e.Category == "Transport.Construction") == 3, "Not all shafts were actually purchased.");
            var carsUsed = new HashSet<int>(); var transferred = new HashSet<long>(); var walked = new HashSet<long>();
            var workers = new HashSet<long>();
            for (var i = 0; i < 3600; i++)
            {
                game.Step();
                foreach (var car in game.Transport.Cars.Where(c => c.BankId == 1 && c.PassengerCount > 0)) carsUsed.Add(car.CarId);
                foreach (var journey in game.Transport.Journeys)
                {
                    workers.Add(journey.PersonId);
                    if (journey.State == JourneyState.Walking && journey.Floor == 9 && journey.RemainingRoute?.FirstOrDefault()?.Kind == RouteKind.Walk)
                        walked.Add(journey.PersonId);
                    if (journey.State == JourneyState.Waiting && journey.BankId == 2 && journey.Floor == 9) transferred.Add(journey.PersonId);
                }
                Check(game.Transport.Cars.All(c => c.PassengerCount <= c.Capacity), "Car overloaded.");
                if (game.People.Count == 24 && game.People.All(p => p.Activity == PersonActivity.Visiting)) break;
            }
            Check(carsUsed.SetEquals([1, 2]) && workers.Count == 24 && transferred.SetEquals(workers) && walked.SetEquals(workers),
                "Workers skipped a physical transfer or coordinated capacity was unused.");
            Check(game.People.Count == 24 && game.People.All(p => p.Activity == PersonActivity.Visiting)
                && game.Transport.Journeys.All(j => j.TransferCount == 1), "Not every commuter completed two rides.");
            var resumed = Restore(game); game.Advance(40000); resumed.Advance(40000);
            Check(game.Serialize() == resumed.Serialize(), "Return commute or service/billing state diverged after saving.");
            Check(game.People.All(p => p.Role != "Worker") && game.World.Ledger.Count(e => e.Category == "Lease.Office") == 3,
                "Transfer workers were lost on return or offices charged more than once.");
        }));
        cases.Add(("Transfer scenario: service-only banks deny public entry but carry real repair staff through both rides", () =>
        {
            var game = TransferScenario.Create(catalog, rules, locations);
            var upper = game.Transport.Banks.Single(b => b.Definition.Id == 2).Definition;
            Must(game.Transport.ConfigureBank(upper with { ServiceOnly = true }));
            var room = game.World.Rooms.First(r => r.DefinitionId == "office");
            Check(!game.IsAccessible(room.Id) && game.IsAccessible(room.Id, true), "A public route bypassed service-only transfer policy.");
            var beforeInspection = game.Serialize();
            Check(game.DiagnoseRoomAccess(room.Id).Status == RouteStatus.AccessDenied
                && game.DiagnoseRoomAccess(room.Id, true).Status == RouteStatus.Reachable
                && beforeInspection == game.Serialize(), "Room diagnostics lost access policy or mutated paused state.");
            var worn = game.CaptureSnapshot();
            worn = worn with { Operations = worn.Operations.Select(op => op.RoomId == room.Id
                ? op with { Condition = 50, Cleanliness = 50 } : op).ToArray() };
            game = GameSession.Deserialize(catalog, rules, locations, JsonSerializer.Serialize(worn, SimulationRules.JsonOptions));
            Must(game.RepairRoom(room.Id)); var task = game.ServiceTaskForRoom(room.Id)!;
            var worker = task.WorkerPersonId!.Value;
            Check(game.Transport.JourneyFor(worker)!.RemainingRoute!.Count(leg => leg.Kind == RouteKind.Elevator) == 2,
                "Repair used a shortcut instead of a real transfer.");
            var resumed = Restore(game);
            for (var second = 0; second < 2400 && game.People.Any(p => p.Id == worker); second++) { game.Step(); resumed.Step(); }
            Check(game.Serialize() == resumed.Serialize() && !game.People.Any(p => p.Id == worker)
                && game.ServiceTasks.Single(t => t.Id == task.Id).Status == ServiceTaskStatus.Completed,
                "Transferred worker failed to finish and return exactly after saving.");
            Check(game.World.Ledger.Count(e => e.EntityId == room.Id && e.Category == "Maintenance.Repair") == 1,
                "Repair materials were charged zero or multiple times.");
        }));
    }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
