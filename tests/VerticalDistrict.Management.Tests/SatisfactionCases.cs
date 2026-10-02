using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;

internal static class SatisfactionCases
{
    internal static void Register(Action<string, Action> test, ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
    {
        GameSession Example(int floors = 4) => OperatingExampleScenario.Create(catalog, rules, locations, floorCount: floors);
        test("Demand responds to price, access and staffing; active complaints resolve after repair", () =>
        {
            var game = Example(); var cafe = game.World.Rooms.First(r => r.DefinitionId == "cafe").Id;
            var original = game.DemandFor(cafe).Score;
            Must(game.SetPrice(cafe, rules.For("cafe")!.PriceMinor * 3));
            Check(game.DemandFor(cafe).Score < original, "Asking price did not lower demand.");
            game.Advance(60);
            Check(game.ActiveComplaints.Any(c => c.RoomId == cafe && c.Code == "Price"), "High-price complaint absent.");
            Must(game.SetPrice(cafe, rules.For("cafe")!.PriceMinor));
            Must(game.SetStaff(cafe, 0)); game.Advance(60);
            Check(game.DemandFor(cafe).Score == 0 && game.ActiveComplaints.Any(c => c.RoomId == cafe && c.Code == "Staff"), "Insufficient staff did not stop demand.");
            Check(!game.ActiveComplaints.Any(c => c.RoomId == cafe && c.Code == "Price"), "Resolved price warning remained active.");
            Must(game.SetStaff(cafe, rules.For("cafe")!.Staff)); game.Advance(60);
            Check(game.DemandFor(cafe).Score > 0 && !game.ActiveComplaints.Any(c => c.RoomId == cafe), "Staff recovery did not restore demand/resolve complaints.");
            Check(game.Complaints.Any(c => c.Code == "Staff" && c.ResolvedTick.HasValue), "Resolution history missing.");
        });
        test("Satisfaction changes at a bounded hourly cadence and recovers after corrected prices", () =>
        {
            var game = Example(); var cafe = game.World.Rooms.First(r => r.DefinitionId == "cafe").Id;
            Must(game.SetPrice(cafe, rules.For("cafe")!.PriceMinor * 3)); Must(game.SetStaff(cafe, 0));
            var initial = game.DemandFor(cafe).Satisfaction;
            game.Advance(299); Check(game.DemandFor(cafe).Satisfaction == initial, "Satisfaction changed before the hourly boundary.");
            game.Step(); Check(Math.Abs(game.DemandFor(cafe).Satisfaction - initial) <= rules.Management.SatisfactionStepPerHour, "Hourly satisfaction jump exceeded tuning.");
            game.Advance(4 * 3600); var poor = game.DemandFor(cafe).Satisfaction;
            Must(game.SetPrice(cafe, rules.For("cafe")!.PriceMinor)); Must(game.SetStaff(cafe, rules.For("cafe")!.Staff));
            game.Advance(4 * 3600);
            Check(game.DemandFor(cafe).Satisfaction > poor, "Corrected service/value did not improve satisfaction.");
            Check(game.SatisfactionHistory.Zip(game.SatisfactionHistory.Skip(1)).All(pair => pair.Second.Tick - pair.First.Tick == 3600), "Trend is not hourly.");
        });
        test("Management inspection is pure and saved outcomes agree across pause and render pacing", () =>
        {
            var game = Example(); game.Advance(900);
            var before = game.Serialize();
            foreach (var room in game.World.Rooms) { _ = game.DemandFor(room.Id); _ = game.TenantFor(room.Id); _ = game.HotelStateFor(room.Id); }
            _ = game.ActiveComplaints; _ = game.StaffSummaries; _ = game.ServiceTasks; _ = game.FoodOrders;
            Check(game.Serialize() == before, "Read models mutated the simulation.");
            var other = GameSession.Deserialize(catalog, rules, locations, before);
            var runner = new FixedStepRunner(rules); runner.SetSpeed(0); runner.Advance(100, other.Step);
            Check(other.Serialize() == before, "Paused simulation advanced.");
            game.Advance(3600); runner.SetSpeed(4);
            for (var i = 0; i < 60; i++) runner.Advance(.25, other.Step);
            Check(other.Serialize() == game.Serialize(), "Render pacing or speed changed the management result.");
        });
        test("Seven-day adverse management loses demand and tenants instead of guaranteeing profit", () =>
        {
            var game = Example(); game.Advance(14 * 3600);
            var initialContracts = game.Tenants.Count(t => t.Status == TenancyStatus.Active);
            Check(initialContracts >= 3, "Baseline did not obtain tenants.");
            foreach (var room in game.World.Rooms.Where(r => r.DefinitionId is "office" or "studio" or "cafe" or "hotel-room"))
                Must(game.SetPrice(room.Id, 100_000_000));
            var service = game.World.Rooms.Single(r => r.DefinitionId == "service-room").Id;
            Must(game.SetOpen(service, false));
            game.Advance(7 * 86400);
            Check(game.Tenants.All(t => t.Status != TenancyStatus.Active), "Failed service/renewal did not cause tenant departure.");
            Check(game.Reports.Last().ProfitMinor < 0, "Adverse management remained inevitably profitable.");
            Check(game.ActiveComplaints.Count > 0 && game.ServiceTasks.Any(t => t.Status == ServiceTaskStatus.Blocked), "Consequences were not explained by active state.");
        });
        test("Thirty-day twenty-floor endurance keeps references, histories, billing and assignments coherent", () =>
        {
            var game = Example(20);
            var construction = game.LifetimeFinances.CapitalSpendingMinor;
            for (var day = 0; day < 30; day++)
            {
                game.Advance(86400);
                var saved = game.Serialize();
                var restored = GameSession.Deserialize(catalog, rules, locations, saved);
                Check(saved == restored.Serialize(), "Day " + (day + 1) + " failed exact checkpoint validation.");
                game = restored;
                var claimed = game.ServiceTasks.Where(t => t.Status is ServiceTaskStatus.Assigned or ServiceTaskStatus.Traveling or ServiceTaskStatus.InProgress).ToArray();
                Check(claimed.Select(t => t.WorkerPersonId).Distinct().Count() == claimed.Length, "Worker double-claimed.");
                Check(claimed.Select(t => t.RoomId).Distinct().Count() == claimed.Length, "Room double-claimed.");
                Check(game.Transport.Journeys.Count <= game.People.Count && game.People.Count < 120, "Unbounded people/transport references.");
                Check(game.ServiceTasks.Count(t => t.Status is ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled) <= rules.Management.ServiceHistoryLimit, "Service history grew without bound.");
                Check(game.PendingBillingPeriods == 0 && game.Billing.LastSettledDay == day + 1, "Billing drift or stuck obligation.");
                Check(game.World.CashMinor == game.LifetimeFinances.OpeningCashMinor + game.LifetimeFinances.NetCashFlowMinor, "Cash drift.");
            }
            Check(game.Reports.Count == 30 && game.LifetimeFinances.OperatingProfitMinor > 0, "Documented operating baseline is not profitable across thirty days.");
            Check(game.LifetimeFinances.CapitalSpendingMinor == construction, "Unrequested construction appeared.");
            Check(game.World.Rank >= 2 && game.Notices.Count(n => n.Kind == "Promotion" && n.Text.StartsWith("Rank 2:")) <= 1, "Early objective did not progress or duplicated its award.");
            Console.WriteLine($"ENDURANCE 30 days / 20 floors: cash {game.World.CashMinor / 100m:N2}, operating profit {game.LifetimeFinances.OperatingProfitMinor / 100m:N2}, trips {game.CompletedTrips}, people {game.People.Count}, active tasks {game.ServiceTasks.Count(t => t.Status is not (ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled))}, abandoned {game.Transport.Metrics.AbandonedTrips}, rank {game.World.Rank}.");
        });
    }
    private static void Must(CommandResult result) { if (!result.Success) throw new Exception(result.Message); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
