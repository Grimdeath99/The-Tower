using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

internal static class OwnershipCases
{
    internal static void Register(Action<string, Action> test, ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
    {
        (GameSession Game, long Room) Fixture(int floor = 1, bool sandbox = false)
        {
            var game = new GameSession(catalog, rules, locations, sandbox: sandbox);
            for (var f = 1; f <= floor; f++) Must(game.World.BuildFloor(f));
            Build(game, "lobby", 0, 0); Build(game, "service-room", 8, 0);
            var room = Build(game, "condo", 0, floor);
            Must(game.Transport.InstallBank(new BankDefinition(1, 27, 0, floor, [0, floor], 12), rules.ElevatorCostMinor));
            return (game, room);
        }
        GameSession Restore(GameSession game) => GameSession.Deserialize(catalog, rules, locations, game.Serialize());
        CondoOwnership Purchase(GameSession game, long room)
        {
            Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.Owned && game.Occupancy(room) == rules.For("condo")!.Capacity,
                14 * 3600, "No household completed a physical condominium purchase");
            return game.OwnershipFor(room)!;
        }
        test("Ownership: accepted price is frozen and the first physical arrival records one capital sale", () =>
        {
            var (game, room) = Fixture();
            Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.PendingSale, 12 * 3600, "No purchase offer was accepted");
            var offer = game.OwnershipFor(room)!;
            Check(game.World.Ledger.All(e => e.Category != "Sales.Condo") && !game.OperationFor(room)!.CondoSold, "A prospective arrival was charged before reaching the unit.");
            Must(game.SetPrice(room, offer.AgreedPriceMinor * 2));
            var ownership = Purchase(game, room);
            var receipt = game.World.Ledger.Single(e => e.Category == "Sales.Condo");
            Check(ownership.Id == offer.Id && receipt.AmountMinor == offer.AgreedPriceMinor && ownership.SaleLedgerSequence == receipt.Sequence
                && ownership.PurchasedAtTick == receipt.TimestampTicks && receipt.Description.Contains($"Ownership #{ownership.Id}, owner #{ownership.OwnerId}:"),
                "Completed ownership lost its accepted quote, identity, timestamp, or ledger correlation.");
            Check(ownership.ResidentIds.Length == 2 && ownership.OwnerId == ownership.ResidentIds[0] && game.CondoResidentCapacity(room) == 2,
                "Owner household does not match physical condo capacity.");
            Check(game.TenantFor(room) == null && game.World.Ledger.All(e => e.Category is not ("Lease.Home" or "Lease.Office")), "Condo silently created a rental tenancy.");
            Check(game.LifetimeFinances.CapitalReceiptsMinor == offer.AgreedPriceMinor && game.LifetimeFinances.OperatingRevenueMinor == 0,
                "Ownership proceeds were misreported as daily operating revenue.");
        });
        test("Ownership: daytime vacancy preserves ownership and nightly residents retain their identities", () =>
        {
            var (game, room) = Fixture(); var ownership = Purchase(game, room);
            Until(game, () => game.Day == 2 && game.Hour >= 9 && game.People.All(p => p.RoomId != room), 16 * 3600, "Condo residents failed to leave at08:00");
            Check(game.OwnershipFor(room)?.Id == ownership.Id && game.CondoStateFor(room) == CondoAvailability.Owned
                && game.CondoResidentCapacity(room) == 2 && game.OperationFor(room)!.CondoSold, "Physical vacancy erased or resold an owned unit.");
            var restored = Restore(game);
            game.Advance(11 * 3600); restored.Advance(11 * 3600);
            Check(game.Serialize() == restored.Serialize(), "Outside owner identities or future schedules changed on load.");
            Check(game.People.Where(p => p.RoomId == room).Select(p => p.Id).Order().SequenceEqual(ownership.ResidentIds.Order()), "Returning residents received new ownership identities.");
            Check(game.World.Ledger.Count(e => e.Category == "Sales.Condo") == 1 && game.World.Ledger.All(e => e.Category is not ("Lease.Home" or "Lease.Office")),
                "A return commute generated another sale or rent.");
        });
        test("Ownership: buyback uses the original price once and evacuation blocks reopening or demolition", () =>
        {
            var (game, room) = Fixture(); var ownership = Purchase(game, room);
            var beforeInvalid = game.Serialize();
            Check(!game.SetOpen(room, false).Success && !game.DemolishRoom(room).Success && beforeInvalid == game.Serialize(), "Owned closure or demolition changed money or ownership.");
            Must(game.SetPrice(room, 0)); var beforeCash = game.World.CashMinor;
            Must(game.BuyBackCondo(room));
            var bought = game.OwnershipFor(room)!;
            Check(bought.Status == CondoOwnershipStatus.Evacuating && game.World.CashMinor == beforeCash - ownership.AgreedPriceMinor
                && !game.OperationFor(room)!.Open && !game.OperationFor(room)!.CondoSold, "Buyback changed the wrong amount or skipped physical evacuation.");
            var refund = game.World.Ledger.Single(e => e.Category == "Condo.Buyback");
            Check(bought.BuybackLedgerSequence == refund.Sequence && bought.ReacquiredAtTick == refund.TimestampTicks, "Buyback lost its ledger identity.");
            var after = game.Serialize();
            Check(!game.BuyBackCondo(room).Success && !game.SetOpen(room, true).Success && !game.DemolishRoom(room).Success && after == game.Serialize(),
                "A repeated buyback, early reopening, or occupied demolition changed state.");
            var resumed = Restore(game); game.Advance(1800); resumed.Advance(1800);
            Check(game.Serialize() == resumed.Serialize() && game.Ownerships.Single(o => o.Id == ownership.Id).Status == CondoOwnershipStatus.Reacquired,
                "Saved evacuation did not finish exactly once.");
            Check(ownership.ResidentIds.All(id => game.Transport.JourneyFor(id) == null), "Evacuated ownership left a journey behind.");
            Must(game.DemolishRoom(room));
            Check(game.Ownerships.Single(o => o.Id == ownership.Id).SaleLedgerSequence == ownership.SaleLedgerSequence,
                "Demolition discarded the historical ownership audit record.");
            Check(Restore(game).Serialize() == game.Serialize(), "Historical ownership could not reload after valid demolition.");
        });
        test("Ownership: insufficient buyback funds reject the complete command atomically", () =>
        {
            var (game, room) = Fixture(); var ownership = Purchase(game, room);
            Must(game.World.ApplyOperatingTransaction(-game.World.CashMinor, game.Tick, "Fixture.Cash", null, "Isolated failed-buyback boundary."));
            var before = game.Serialize();
            Check(!game.BuyBackCondo(room).Success && before == game.Serialize(), "Insufficient funds partly changed cash, residents, or ownership.");
            Must(game.World.ApplyOperatingTransaction(ownership.AgreedPriceMinor, game.Tick, "Fixture.Cash", null, "Restore only the agreed buyback amount."));
            Must(game.BuyBackCondo(room));
            Check(game.World.CashMinor == 0 && game.World.Ledger.Count(e => e.Category == "Condo.Buyback") == 1, "Funded retry did not pay the original amount once.");
        });
        test("Ownership: zero-price purchase and reacquisition still have exact transaction identities", () =>
        {
            var (game, room) = Fixture(); Must(game.SetPrice(room, 0));
            var ownership = Purchase(game, room); var cash = game.World.CashMinor;
            Check(ownership.AgreedPriceMinor == 0 && game.World.Ledger.Single(e => e.Category == "Sales.Condo") is { AmountMinor: 0 }
                && ownership.SaleLedgerSequence.HasValue, "Zero-price purchase lacked an explicit completed transaction.");
            Must(game.SetPrice(room, rules.For("condo")!.PriceMinor)); Must(game.BuyBackCondo(room));
            Check(game.World.CashMinor == cash && game.World.Ledger.Single(e => e.Category == "Condo.Buyback") is { AmountMinor: 0 }
                && game.OwnershipFor(room)!.BuybackLedgerSequence.HasValue, "Zero-price buyback charged the edited asking price or omitted its transaction.");
            Check(Restore(game).Serialize() == game.Serialize(), "Zero-valued ownership transactions did not persist.");
        });
        test("Ownership: cancelled pending riders return safely without a sale or a refund", () =>
        {
            var (game, room) = Fixture();
            Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.PendingSale, 12 * 3600, "No prospective household formed");
            var ownership = game.OwnershipFor(room)!;
            Until(game, () => ownership.ResidentIds.Any(id => game.Transport.JourneyFor(id)?.State == JourneyState.Riding), 600, "Prospective owner never entered the car");
            var rider = ownership.ResidentIds.First(id => game.Transport.JourneyFor(id)?.State == JourneyState.Riding);
            Must(game.SetOpen(room, false));
            Check(game.Ownerships.Single(o => o.Id == ownership.Id).Status == CondoOwnershipStatus.Cancelled
                && game.Transport.Cars.Any(c => c.PassengerIds.Contains(rider)) && game.People.Any(p => p.Id == rider), "Cancelling an offer removed a passenger inside a car.");
            Check(!game.SetOpen(room, true).Success, "A cancelled household still traveling allowed immediate reopening.");
            Until(game, () => ownership.ResidentIds.All(id => game.People.All(p => p.Id != id)), 1800, "Cancelled prospective residents failed to return");
            Check(game.World.Ledger.All(e => e.Category is not ("Sales.Condo" or "Condo.Buyback")), "An unpaid cancelled offer generated sale or refund cash.");
            Must(game.SetOpen(room, true)); Check(game.CondoStateFor(room) == CondoAvailability.Available, "Safely vacated unit did not return to sale inventory.");
        });
        test("Ownership: lost access preserves the deed and stranded residents leave when routes recover", () =>
        {
            var (game, room) = Fixture(); var ownership = Purchase(game, room);
            Until(game, () => game.Day == 2 && game.Hour == 7 && game.Minute == 59, 16 * 3600, "Morning departure boundary was not reached");
            Must(game.Transport.SetBankOutOfService(1, true)); game.Advance(180);
            Check(ownership.ResidentIds.All(id => game.People.Any(p => p.Id == id && p.Activity == PersonActivity.Stranded))
                && game.OwnershipFor(room)?.Status == CondoOwnershipStatus.Owned, "Unreachable departure discarded residents or their ownership.");
            var resumed = Restore(game);
            Must(game.Transport.SetBankOutOfService(1, false)); Must(resumed.Transport.SetBankOutOfService(1, false));
            game.Advance(1800); resumed.Advance(1800);
            Check(game.Serialize() == resumed.Serialize() && ownership.ResidentIds.All(id => game.People.All(p => p.Id != id)), "Saved stranded residents did not use the recovered route identically.");
            Check(game.OwnershipFor(room)?.Id == ownership.Id && game.World.Ledger.Count(e => e.Category == "Sales.Condo") == 1, "Access recovery created another deed or sale.");
        });
        test("Ownership: an overflowing sale cancels without partly assigning paid ownership", () =>
        {
            var (game, room) = Fixture();
            Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.PendingSale, 12 * 3600, "No pending offer formed");
            var offer = game.OwnershipFor(room)!;
            Must(game.World.ApplyOperatingTransaction(long.MaxValue - game.World.CashMinor, game.Tick, "Fixture.Cash", null, "Exercise checked sale overflow."));
            Until(game, () => game.Ownerships.Single(o => o.Id == offer.Id).Status == CondoOwnershipStatus.Cancelled, 600, "Overflowing sale never rejected the pending ownership");
            Check(game.World.CashMinor == long.MaxValue && game.World.Ledger.All(e => e.Category != "Sales.Condo")
                && !game.OperationFor(room)!.CondoSold && game.Ownerships.Single(o => o.Id == offer.Id).SaleLedgerSequence == null,
                "Rejected purchase left a partial sale, owner mirror, or cash movement.");
        });
        test("Ownership: pending travel snapshots and detached projections preserve exact future purchase", () =>
        {
            var (game, room) = Fixture();
            Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.PendingSale, 12 * 3600, "No pending ownership formed");
            var offer = game.OwnershipFor(room)!; var before = game.Serialize();
            offer.ResidentIds[0] = long.MaxValue;
            _ = game.Ownerships; _ = game.OwnershipFor(room); _ = game.CondoStateFor(room); _ = game.CondoResidentCapacity(room);
            Check(game.Serialize() == before, "Inspecting or editing a returned household array mutated authoritative state.");
            var resumed = Restore(game); game.Advance(1200); resumed.Advance(1200);
            Check(game.Serialize() == resumed.Serialize() && game.OwnershipFor(room)?.Status == CondoOwnershipStatus.Owned, "Pending purchase rerolled or duplicated on load.");
        });
        test("Ownership: controlled high-rank fixture uses real transport above the20-floor milestone", () =>
        {
            var (game, room) = Fixture(100, sandbox: true); var ownership = Purchase(game, room);
            Check(game.World.Rank == 7 && game.World.Rooms.Single(r => r.Id == room).Floor == 100
                && ownership.ResidentIds.All(id => game.Transport.JourneyFor(id)?.Floor == 100), "High-rank ownership bypassed its actual upper-floor journey.");
            Check(game.World.Ledger.Count(e => e.Category == "Sales.Condo") == 1 && Restore(game).Serialize() == game.Serialize(), "High-floor ownership lost its transaction or persistence.");
        });
        test("Ownership: three-day mixed twenty-floor tower resumes a condo purchase and midnight billing exactly", () =>
        {
            var game = OperatingExampleScenario.Create(catalog, rules, locations, floorCount: 20);
            var room = Build(game, "condo", 16, 4);
            var ownership = Purchase(game, room);
            game.Advance((int)(game.Billing.NextDueTick - game.Tick - 1)); var resumed = Restore(game);
            game.Step(); resumed.Step();
            Check(game.Serialize() == resumed.Serialize(), "Ownership changed the midnight billing boundary on load.");
            var target = 3 * 86400;
            game.Advance(target - (int)game.Tick); resumed.Advance(target - (int)resumed.Tick);
            Check(game.Serialize() == resumed.Serialize() && game.OwnershipFor(room)?.Id == ownership.Id
                && game.OwnershipFor(room)!.ResidentIds.SequenceEqual(ownership.ResidentIds), "Mixed operation changed stable ownership across days.");
            Check(game.World.Ledger.Count(e => e.EntityId == room && e.Category == "Sales.Condo") == 1
                && game.World.Ledger.All(e => e.EntityId != room || e.Category is not ("Lease.Home" or "Lease.Office")), "Mixed billing resold or rented the owned condo.");
            Check(game.FinancialHistory.Count == 3 && game.CompletedTrips > 100 && game.Transport.Metrics.AbandonedTrips == 0,
                "Mixed fixture bypassed real recurring operations or overloaded its transport.");
        });
    }
    private static long Build(GameSession game, string id, int x, int floor)
    { var result = game.BuildRoom(id, x, floor); Must(result); return result.EntityId!.Value; }
    private static void Until(GameSession game, Func<bool> condition, int limit, string message)
    { for (var i = 0; i < limit && !condition(); i++) game.Step(); Check(condition(), message); }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
