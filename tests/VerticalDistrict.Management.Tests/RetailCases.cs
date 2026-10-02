using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

internal static class RetailCases
{
    internal static void Register(Action<string, Action> test, ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
    {
        GameSession Restore(GameSession game) => GameSession.Deserialize(catalog, game.Rules, locations, game.Serialize());
        SimulationRules TuneShop(int arrival = 900, int service = 120, int open = 10, int close = 22)
        {
            var json = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!;
            var shop = json["businesses"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "shop")!;
            shop["arrivalIntervalSeconds"] = arrival; shop["serviceSeconds"] = service;
            shop["openHour"] = open; shop["closeHour"] = close;
            return SimulationRules.Load(json.ToJsonString(), catalog);
        }
        (GameSession Game, long Room) Fixture(string kind = "shop", int floor = 0, SimulationRules? tuning = null)
        {
            var game = new GameSession(catalog, tuning ?? rules, locations);
            for (var f = 1; f <= floor; f++) Must(game.World.BuildFloor(f));
            Build(game, "lobby", 0, 0); Build(game, "service-room", 16, 0);
            var room = Build(game, kind, floor == 0 ? 6 : 0, floor);
            if (floor > 0) Must(game.Transport.InstallBank(new BankDefinition(1, 27, 0, floor, [0, floor], 12, 8, 2), rules.ElevatorCostMinor));
            return (game, room);
        }
        FoodOrder StartService(GameSession game, long room)
        {
            Until(game, () => Orders(game, room).Any(order => order.Status == FoodOrderStatus.Serving), 5 * 3600, "No customer started physical service");
            return Orders(game, room).First(order => order.Status == FoodOrderStatus.Serving);
        }
        test("Retail: a completed purchase pays once with its frozen product price and duration", () =>
        {
            var (game, room) = Fixture(); var order = StartService(game, room);
            Check(order.ProductId == "shop-essentials" && order.AgreedServiceSeconds == rules.For("shop")!.ServiceSeconds,
                "Default shop offering changed existing quoted tuning.");
            Check(!Sales(game, room).Any(), "Shop charged at physical arrival before completed service.");
            Must(game.SetProduct(room, "shop-gifts")); Must(game.SetPrice(room, 1));
            var restored = Restore(game);
            var remaining = checked((int)(order.CompletesAt - game.Tick));
            game.Advance(remaining - 1); restored.Advance(remaining - 1);
            Check(!Sales(game, room).Any(), "Purchase charged before its agreed service ended.");
            game.Step(); restored.Step();
            Check(game.Serialize() == restored.Serialize(), "Accepted product or service schedule rerolled across a save.");
            var receipt = Sales(game, room).Single(); var completed = game.RetailOrders.Single(item => item.Id == order.Id);
            Check(receipt.AmountMinor == order.AgreedPriceMinor && receipt.Description.Contains(order.ProductId)
                && completed.ProductId == order.ProductId && completed.AgreedServiceSeconds == order.AgreedServiceSeconds
                && completed.Status == FoodOrderStatus.Completed, "Completed purchase changed its accepted terms.");
            game.Advance(60); Check(Sales(game, room).Count() == 1, "The same customer paid twice.");
        });
        test("Retail: menu selection sets suggested price and leaves accepted food service unchanged", () =>
        {
            var (game, room) = Fixture("cafe"); Must(game.SetProduct(room, "cafe-lunch"));
            var product = game.ProductForRoom(room)!;
            Check(game.OperationFor(room)!.PriceMinor == product.PriceFor(rules.For("cafe")!), "Applying the menu did not set its suggested price.");
            Check(game.DemandFor(room).Factors.Single(factor => factor.Name == "Value at offering price").Score == 100,
                "The menu's suggested price was incorrectly treated as overpriced.");
            var order = StartService(game, room);
            Check(order.ProductId == product.Id && order.AgreedServiceSeconds == product.ServiceSecondsFor(rules.For("cafe")!), "Menu service time was not quoted.");
            Must(game.SetProduct(room, "cafe-classic"));
            Check(game.OperationFor(room)!.PriceMinor == rules.For("cafe")!.PriceMinor, "Default menu did not restore its suggested price.");
            game.Advance(checked((int)(order.CompletesAt - game.Tick)));
            Check(Sales(game, room).Single().AmountMinor == order.AgreedPriceMinor, "A menu edit repriced an accepted meal.");
            Must(game.SetProduct(room, "cafe-lunch"));
            Until(game, () => game.Hour == 11, 4 * 3600, "Lunch window was not reached");
            Check(game.ProductDemandMultiplier(room) == product.DemandPercent * product.LunchDemandPercent / 100, "Lunch demand modifier was not applied.");
            Until(game, () => game.Hour == 14, 4 * 3600, "Lunch window did not end");
            Check(game.ProductDemandMultiplier(room) == product.DemandPercent, "Lunch modifier leaked into the afternoon.");
        });
        test("Retail: staff dismissal stops service and unpaid shoppers leave after patience expires", () =>
        {
            var (game, room) = Fixture(); var order = StartService(game, room);
            Must(game.SetStaff(room, 0)); game.Step();
            var queued = game.RetailOrders.Single(item => item.Id == order.Id);
            Check(queued.Status == FoodOrderStatus.Queued && queued.CompletesAt == 0 && queued.AgreedServiceSeconds == order.AgreedServiceSeconds,
                "An unstaffed service slot kept working or lost its accepted duration.");
            Until(game, () => game.RetailOrders.Single(item => item.Id == order.Id).Status == FoodOrderStatus.Abandoned,
                rules.Management.FoodPatienceSeconds + 1, "Unserved shopper did not abandon");
            Until(game, () => game.People.All(person => person.Id != order.PersonId), 300, "Abandoned shopper did not physically exit");
            Check(!Sales(game, room).Any(), "Abandoned service produced a sale.");
            Must(game.SetStaff(room, 1)); Until(game, () => Sales(game, room).Any(), 3600, "Staffing recovery did not restore real purchases");
        });
        test("Retail: closure cancels queued and serving purchases without erasing physical actors", () =>
        {
            var (game, room) = Fixture(tuning: TuneShop(30, 300));
            Until(game, () => game.RetailOrders.Any(order => order.Status == FoodOrderStatus.Serving)
                && game.RetailOrders.Any(order => order.Status == FoodOrderStatus.Queued), 4 * 3600, "No real busy queue formed");
            var people = game.People.Where(person => person.RoomId == room).Select(person => person.Id).ToArray();
            Must(game.SetOpen(room, false));
            Check(people.All(id => game.People.Any(person => person.Id == id)) && !game.DemolishRoom(room).Success,
                "Closing erased queued people or allowed occupied demolition.");
            var resumed = Restore(game); game.Advance(1200); resumed.Advance(1200);
            Check(game.Serialize() == resumed.Serialize() && !Sales(game, room).Any(), "Closed unpaid orders charged or resumed differently.");
            Check(people.All(id => game.People.All(person => person.Id != id)), "Closed shoppers never exited physically.");
            Must(game.DemolishRoom(room)); Check(Restore(game).Serialize() == game.Serialize(), "Removed shop retained product selection or order references.");
        });
        test("Retail: a cancelled shopper riding the lift remains accounted until safe exit", () =>
        {
            var (game, room) = Fixture(floor: 3);
            Until(game, () => game.RetailOrders.Any(order => game.Transport.JourneyFor(order.PersonId)?.State == JourneyState.Riding), 4 * 3600, "No shopper boarded the real lift");
            var order = game.RetailOrders.First(item => game.Transport.JourneyFor(item.PersonId)?.State == JourneyState.Riding);
            Must(game.SetOpen(room, false)); var floor = game.Transport.JourneyFor(order.PersonId)!.DrawFloor;
            Check(game.People.Any(person => person.Id == order.PersonId) && game.Transport.JourneyFor(order.PersonId)!.DrawFloor == floor,
                "Closing teleported a riding shopper.");
            var resumed = Restore(game); game.Advance(1200); resumed.Advance(1200);
            Check(game.Serialize() == resumed.Serialize() && game.People.All(person => person.Id != order.PersonId) && !Sales(game, room).Any(),
                "Riding cancellation did not safely resume and exit unpaid.");
        });
        test("Retail: lost access pauses real service and repair resumes the accepted purchase", () =>
        {
            var (game, room) = Fixture(floor: 1); var order = StartService(game, room);
            Must(game.Transport.SetBankOutOfService(1, true)); game.Advance(30);
            var paused = game.RetailOrders.Single(item => item.Id == order.Id);
            Check(paused.Status == FoodOrderStatus.Serving && paused.CompletesAt == order.CompletesAt + 30 && !Sales(game, room).Any(),
                "Disconnected service completed or lost its remaining work.");
            Must(game.Transport.SetBankOutOfService(1, false));
            Until(game, () => Sales(game, room).Any(), 600, "Repaired access did not restore the suspended purchase");
            Check(Sales(game, room).Single().AmountMinor == order.AgreedPriceMinor, "Access repair changed the quoted price.");
        });
        test("Retail: closing hour abandons unfinished service and a 24-hour shop crosses midnight once", () =>
        {
            var (game, room) = Fixture(tuning: TuneShop(30, 3600)); Must(game.SetOpen(room, false));
            Until(game, () => game.Hour == 21 && game.Minute >= 50, 14 * 3600, "Closing window was not reached");
            Must(game.SetOpen(room, true)); var order = StartService(game, room);
            Until(game, () => game.Hour == 22, 1200, "Shop did not reach closing time");
            Check(game.RetailOrders.Single(item => item.Id == order.Id).Status == FoodOrderStatus.Abandoned && !Sales(game, room).Any(),
                "Unfinished service charged after closing hours.");
            var allDay = Fixture(tuning: TuneShop(30, 180, 0, 24)); Must(allDay.Game.SetOpen(allDay.Room, false));
            Until(allDay.Game, () => allDay.Game.Hour == 23 && allDay.Game.Minute >= 58, 17 * 3600, "Midnight window was not reached");
            Must(allDay.Game.SetOpen(allDay.Room, true)); var midnightOrder = StartService(allDay.Game, allDay.Room);
            var restored = Restore(allDay.Game); allDay.Game.Advance(600); restored.Advance(600);
            Check(allDay.Game.Serialize() == restored.Serialize() && allDay.Game.Day == 2
                && allDay.Game.RetailOrders.Single(item => item.Id == midnightOrder.Id).Status == FoodOrderStatus.Completed,
                "Midnight billing duplicated or cancelled valid all-day service.");
            Check(Sales(allDay.Game, allDay.Room).Count(entry => entry.Description.Contains($"Order #{midnightOrder.Id},")) == 1,
                "The purchase crossing midnight was not charged exactly once.");
        });
        test("Retail: comparable shops share a finite customer budget and both win purchases", () =>
        {
            var (game, first) = Fixture(); var second = Build(game, "shop", 10, 0);
            game.Advance(14 * 3600);
            var cursor = game.CustomerDemand.Single(item => item.Model == "Shop");
            Check(game.RetailOrders.Count <= cursor.Attempts && cursor.Attempts <= (game.Tick + game.CustomerArrivalInterval("Shop") - 1) / game.CustomerArrivalInterval("Shop"),
                "Adding stores multiplied the shared arrival budget.");
            Check(Sales(game, first).Count() >= 2 && Sales(game, second).Count() >= 2, "Weighted selection sent all shoppers to one comparable store.");
            Check(game.RetailOrders.Select(order => order.PersonId).Distinct().Count() == game.RetailOrders.Count,
                "One physical customer owns multiple store purchases.");
        });
        test("Retail: zero-value receipts and overflow retries remain exact once", () =>
        {
            var (game, room) = Fixture(); Must(game.SetPrice(room, 0)); var free = StartService(game, room);
            game.Advance(checked((int)(free.CompletesAt - game.Tick)));
            Check(Sales(game, room).Single().AmountMinor == 0, "A free completed purchase has no authoritative receipt.");
            Must(game.SetPrice(room, rules.For("shop")!.PriceMinor));
            var order = StartService(game, room);
            Must(game.World.ApplyOperatingTransaction(long.MaxValue - game.World.CashMinor, game.Tick, "Fixture.Cash", null, "Adversarial cash-range fixture."));
            game.Advance(checked((int)(order.CompletesAt - game.Tick)) + 2);
            Check(Sales(game, room).Count() == 1 && game.RetailOrders.Single(item => item.Id == order.Id).Status == FoodOrderStatus.Serving,
                "Overflowing payment partly completed a purchase.");
            var resumed = Restore(game);
            Must(game.World.ApplyOperatingTransaction(-order.AgreedPriceMinor, game.Tick, "Fixture.Cash", null, "Make room for the accepted charge."));
            Must(resumed.World.ApplyOperatingTransaction(-order.AgreedPriceMinor, resumed.Tick, "Fixture.Cash", null, "Make room for the accepted charge."));
            game.Step(); resumed.Step();
            Check(game.Serialize() == resumed.Serialize() && Sales(game, room).Count() == 2, "Frozen payment did not retry exactly once after capacity recovered.");
        });
        test("Retail: invalid product changes are atomic and paused projections are pure", () =>
        {
            var (game, room) = Fixture(); _ = StartService(game, room);
            var ordinaryDemand = game.DemandFor(room).Score;
            Must(game.SetProduct(room, "shop-gifts"));
            var productDemand = game.DemandFor(room);
            Check(productDemand.Factors.Single(factor => factor.Name == "Value at offering price").Score == 100
                && productDemand.Score < ordinaryDemand, "Selected gift demand did not change independently of price value.");
            var before = game.Serialize();
            Check(!game.SetProduct(room, "cafe-lunch").Success && !game.SetProduct(room, "missing").Success
                && !game.SetProduct(room, null!).Success && !game.SetProduct(room, " ").Success, "An invalid retail product was accepted.");
            for (var read = 0; read < 5; read++)
            {
                _ = game.ProductForRoom(room); _ = game.ProductSuggestedPrice(room); _ = game.ProductServiceSeconds(room); _ = game.ProductDemandMultiplier(room); _ = game.DemandFor(room);
                var metrics = game.RetailPerformanceFor(room)!;
                Check(metrics.Serving == 1 && metrics.Visits > 0 && metrics.StaffUtilization is >= 0 and <= 1, "Retail metrics do not reflect actual service.");
                ((FoodOrder[])game.RetailOrders)[0] = game.RetailOrders[0] with { AgreedPriceMinor = 1 };
                ((RetailDemandState[])game.CustomerDemand)[0] = new("Food", 1, 1, 1);
            }
            Check(game.Serialize() == before, "Inspection or an invalid product command changed authoritative state.");
        });
        test("Retail: terminal tourists own real orders and pay only after completed service", () =>
        {
            var site = locations.Get("hawaii").Sites.First(item => item.Waterfront).Id;
            var game = new GameSession(catalog, rules, locations, "hawaii", site, sandbox: true);
            Build(game, "lobby", 0, 0); Build(game, "dock", 6, 0); var shop = Build(game, "shop", 15, 0); Build(game, "service-room", 21, 0);
            Until(game, () => game.People.Any(person => person.Role == "Tourist" && person.RoomId == shop), 5 * 3600, "Terminal prototype produced no shop-bound tourists");
            var touristIds = game.People.Where(person => person.Role == "Tourist" && person.RoomId == shop).Select(person => person.Id).ToArray();
            Check(touristIds.All(id => game.RetailOrders.Count(order => order.PersonId == id) == 1), "A tourist bypassed the accepted purchase lifecycle.");
            Check(touristIds.All(id => !Sales(game, shop).Any(entry => entry.Description.Contains($"customer #{id}:"))), "Tourist paid before physical service.");
            game.Advance(1800);
            Check(game.RetailOrders.Any(order => touristIds.Contains(order.PersonId) && order.Status == FoodOrderStatus.Completed), "Tourists never completed actual retail service.");
        });
        test("Retail: three-day mixed twenty-floor operation resumes frozen purchases and demand cursors", () =>
        {
            var game = OperatingExampleScenario.Create(catalog, rules, locations, floorCount: 20);
            var shop = Build(game, "shop", 16, 4); var cafe = game.World.Rooms.Single(room => room.DefinitionId == "cafe").Id;
            Must(game.SetProduct(shop, "shop-gifts")); Must(game.SetProduct(cafe, "cafe-lunch")); _ = StartService(game, shop);
            var resumed = Restore(game);
            for (var day = 0; day < 3; day++)
            {
                game.Advance(86400); resumed.Advance(86400);
                Check(game.Serialize() == resumed.Serialize(), "Mixed-operation saved continuation diverged.");
                resumed = Restore(resumed);
            }
            Check(Sales(game, shop).Any() && Sales(game, cafe).Any() && game.World.Ledger.Any(entry => entry.Category == "Sales.Hotel"),
                "Mixed fixture did not exercise shop, food and hotel receipts.");
            var metrics = game.RetailPerformanceFor(shop)!;
            Check(metrics.RevenueMinor == Sales(game, shop).Sum(entry => entry.AmountMinor) && metrics.ExpensesMinor > 0
                && metrics.OperatingProfitMinor == metrics.RevenueMinor - metrics.ExpensesMinor,
                "Retail operating result does not reconcile with the actual ledger.");
        });
        test("Retail: rebuilding the last shop preserves one demand cursor without replaying missed arrivals", () =>
        {
            var (game, room) = Fixture(); _ = StartService(game, room);
            Must(game.SetProduct(room, "shop-gifts")); Must(game.SetOpen(room, false));
            Until(game, () => game.ValidateDemolishRoom(room).Success, 1800, "Accepted shopper capacity did not clear for demolition");
            Must(game.DemolishRoom(room)); var cursor = game.CustomerDemand.Single(item => item.Model == "Shop");
            game.Advance(game.CustomerArrivalInterval("Shop") * 3);
            Check(game.CustomerDemand.Single(item => item.Model == "Shop") == cursor && game.ProductForRoom(room) == null,
                "An absent shop consumed demand attempts or retained its product selection.");
            var resumed = Restore(game); var rebuilt = Build(game, "shop", 6, 0); var rebuiltCopy = Build(resumed, "shop", 6, 0);
            Check(rebuilt == rebuiltCopy && game.ProductForRoom(rebuilt)!.Id == ProductRule.DefaultShopId,
                "Rebuilt shop inherited a removed room's offering or identity.");
            var toNextMinute = (int)(60 - game.Tick % 60); game.Advance(toNextMinute); resumed.Advance(toNextMinute);
            var next = game.CustomerDemand.Single(item => item.Model == "Shop");
            Check(next.Attempts == cursor.Attempts + 1 && next.NextAttemptTick == game.Tick + game.CustomerArrivalInterval("Shop")
                && game.RetailOrders.Count <= 1, "Rebuilding replayed missed demand or reset the shared attempt counter.");
            game.Advance(3600); resumed.Advance(3600);
            Check(game.Serialize() == resumed.Serialize(), "Rebuilt-shop demand diverged across an absent-room save.");
        });
        test("Retail: exhausted customer or purchase identities create no partial actor route or charge", () =>
        {
            var (game, room) = Fixture(); Until(game, () => game.Hour == 10, 3 * 3600, "Shop opening was not reached");
            Check(game.RetailOrders.Count == 0, "Counter fixture unexpectedly already has a shopper.");
            foreach (var personCounter in new[] { true, false })
            {
                var save = JsonNode.Parse(game.Serialize())!.AsObject();
                if (personCounter) save["nextPersonId"] = long.MaxValue;
                else save["management"]!["businesses"]!["nextFoodOrderId"] = long.MaxValue;
                var exhausted = GameSession.Deserialize(catalog, rules, locations, save.ToJsonString());
                var cash = exhausted.World.CashMinor; exhausted.Advance(1800);
                Check(exhausted.RetailOrders.Count == 0 && exhausted.People.Count == 0 && exhausted.Transport.Journeys.Count == 0
                    && exhausted.World.CashMinor == cash && !Sales(exhausted, room).Any(),
                    "Exhausted identity space partly created or charged an accepted shopper.");
                Check(Restore(exhausted).Serialize() == exhausted.Serialize(), "Exhausted but untouched retail identities could not round-trip.");
            }
        });
        test("Retail: exhausted worker identities block repair before allocating an orphan journey", () =>
        {
            var (game, room) = Fixture(); var save = JsonNode.Parse(game.Serialize())!.AsObject();
            save["nextPersonId"] = long.MaxValue;
            save["operations"]!.AsArray().Single(operation => operation!["roomId"]!.GetValue<long>() == room)!["cleanliness"] = 20;
            var exhausted = GameSession.Deserialize(catalog, rules, locations, save.ToJsonString());
            var cash = exhausted.World.CashMinor;
            Check(!exhausted.RepairRoom(room).Success, "An exhausted worker identity was assigned to a repair.");
            var task = exhausted.ServiceTaskForRoom(room);
            Check(task is { Status: ServiceTaskStatus.Blocked, WorkerPersonId: null } && task.Reason.Contains("identities")
                && exhausted.People.Count == 0 && exhausted.Transport.Journeys.Count == 0 && exhausted.World.CashMinor == cash,
                "Rejected worker admission left a partial person, journey, or payment.");
            exhausted.Advance(180);
            Check(exhausted.Transport.Journeys.Count == 0 && exhausted.ServiceTasks.Count == 1,
                "Automatic retry created orphan routes or duplicate repair tasks.");
            Check(Restore(exhausted).Serialize() == exhausted.Serialize(), "Blocked repair with exhausted identities did not round-trip.");
        });
    }

    private static IEnumerable<FoodOrder> Orders(GameSession game, long room) => game.FoodOrders.Concat(game.RetailOrders).Where(order => order.RoomId == room);
    private static IEnumerable<LedgerEntry> Sales(GameSession game, long room) => game.World.Ledger.Where(entry => entry.EntityId == room && entry.Category is "Sales.Food" or "Sales.Shop");
    private static long Build(GameSession game, string kind, int x, int floor) { var result = game.BuildRoom(kind, x, floor); Must(result); return result.EntityId!.Value; }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Until(GameSession game, Func<bool> done, int limit, string message)
    { for (var i = 0; i < limit && !done(); i++) game.Step(); Check(done(), message); }
}
