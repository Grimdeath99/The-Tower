using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Simulation;

public static partial class Program
{
    static partial void RegisterCatalogueCases(List<(string Name, Action Run)> cases)
    {
        cases.Add(("Catalogue: unsupported operating models and duplicate utility references identify the facility", () =>
        {
            CatalogueReject(() => ContentCatalog.Load(CatalogueEdit(root => CatalogueFacility(root, "shop")["operations"]!["model"] = "InventoryMagic")), "shop", "unsupported");
            CatalogueReject(() => ContentCatalog.Load(CatalogueEdit(root => CatalogueFacility(root, "office")["operations"]!["utilities"] = new JsonArray("Power", "power"))), "office", "unique");
        }));
        cases.Add(("Catalogue: every registered facility including public space requires an operating rule", () =>
        {
            foreach (var id in new[] { "office", "atrium", "dock" })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["businesses"]!.AsArray().Remove(CatalogueRule(root, id))), Catalog), id, "missing runtime");
        }));
        cases.Add(("Catalogue: duplicate and orphan operating rules identify the offending ID", () =>
        {
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["businesses"]!.AsArray().Add(CatalogueRule(root, "cafe").DeepClone())), Catalog), "cafe", "duplicate");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "cafe")["id"] = "unknown-cafe"), Catalog), "unknown-cafe", "unknown facility");
        }));
        cases.Add(("Catalogue: a supported model cannot silently replace the definition's operating behavior", () =>
        {
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "office")["model"] = "Shop"), Catalog), "office", "contradicts");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "office")["model"] = "Unknown"), Catalog), "office", "unsupported");
        }));
        cases.Add(("Catalogue: invalid business ranges report both content ID and actionable field", () =>
        {
            foreach (var (field, value, diagnostic) in new (string, int, string)[]
            {
                ("capacity", 201, "capacity"), ("priceMinor", -1, "price"), ("upkeepMinor", -1, "upkeep"),
                ("staffSalaryMinor", -1, "salary"), ("staff", 21, "staff"), ("closeHour", 8, "opening hours"),
                ("visitSeconds", 0, "visit"), ("arrivalIntervalSeconds", 0, "arrivals"),
                ("stayDays", 0, "stay"), ("serviceSeconds", 0, "service")
            })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "office")[field] = value), Catalog), "office", diagnostic);
        }));
        cases.Add(("Catalogue: omitted monetary and allocation fields cannot silently become zero", () =>
        {
            foreach (var field in new[] { "priceMinor", "upkeepMinor", "staffSalaryMinor", "capacity", "staff", "openHour" })
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "office").Remove(field)), Catalog), field);
        }));
        cases.Add(("Catalogue: occupant models require real capacity while public and advertising models allow zero", () =>
        {
            foreach (var id in new[] { "office", "studio", "condo", "cafe", "hotel-room", "shop", "cinema", "event-hall", "parking", "subway", "dock" })
            {
                CatalogueReject(() => ContentCatalog.Load(CatalogueEdit(root => CatalogueFacility(root, id)["operations"]!["capacity"] = 0)), id, "positive capacity");
                CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, id)["capacity"] = 0), Catalog), id, "positive capacity");
            }
            foreach (var id in new[] { "lobby", "atrium", "billboard" })
                Check(Catalog.Get(id).Operations.Capacity == 0 && Rules.For(id)!.Capacity == 0,
                    id + ": a non-occupant model should retain its valid zero capacity.");
        }));
        cases.Add(("Catalogue: malformed and null rules produce content errors instead of runtime failures", () =>
        {
            foreach (var json in new[] { "{", "null", "{}" })
                CatalogueReject(() => SimulationRules.Load(json, Catalog));
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => root["businesses"]![0] = null), Catalog), "business rule");
            CatalogueReject(() => SimulationRules.Load(RulesEdit(root => CatalogueRule(root, "office")["id"] = null), Catalog), "facility ID");
        }));
        cases.Add(("Catalogue: an explicitly defined supported variant loads without hardcoded content counts or metadata pricing", () =>
        {
            var catalog = ContentCatalog.Load(CatalogueEdit(root =>
            {
                var extra = CatalogueFacility(root, "cafe").DeepClone();
                extra["id"] = "test-cafe-variant";
                root["facilities"]!.AsArray().Add(extra);
            }));
            var rules = SimulationRules.Load(RulesEdit(root =>
            {
                var extra = CatalogueRule(root, "cafe").DeepClone();
                extra["id"] = "test-cafe-variant";
                extra["priceMinor"] = 0;
                extra["upkeepMinor"] = 12345;
                root["businesses"]!.AsArray().Add(extra);
            }), catalog);
            Check(rules.For("test-cafe-variant") is { Model: "Food", PriceMinor: 0, UpkeepMinor: 12345 }
                && catalog.Get("test-cafe-variant").Operations.DailyUpkeepMinor != 12345,
                "An explicit free price or runtime tuning was overwritten by duplicate display metadata.");
        }));
        cases.Add(("Catalogue: all 17 current entries place atomically and obey closed operation without rendering", () =>
        {
            Check(Catalog.Definitions.Count == 17, "Update the reviewed catalogue inventory when adding approved content.");
            foreach (var facility in Catalog.Definitions)
            {
                // Controlled geometry/rank fixtures verify entry coverage, not earned progression or completed business models.
                var game = new GameSession(Catalog, Rules, Locations,
                    facility.Id == "dock" ? "hawaii" : "tokyo", facility.Id == "dock" ? "waterfront" : "transit");
                if (facility.Id == "dock") Succeed(game.World.PromoteRank(Locations.DockMinimumRank));
                var floor = facility.Id is "subway" or "parking" ? -1 : 0;
                if (floor < 0)
                {
                    Succeed(game.World.BuildFloor(-1));
                    Succeed(game.Transport.BuildStair(-1, 30, Rules.StairCostMinor));
                }
                if (facility.Height > 1) Succeed(game.World.BuildFloor(1));
                Build(game, "lobby", 0, 0);
                var beforePreview = game.Serialize();
                Succeed(game.ValidateRoom(facility.Id, 10, floor));
                Check(game.Serialize() == beforePreview, facility.Id + ": placement preview mutated the session.");
                var cash = game.World.CashMinor;
                var room = Build(game, facility.Id, 10, floor);
                Check(game.World.CashMinor == cash - facility.CostMinor && game.IsAccessible(room),
                    facility.Id + ": valid placement was not charged exactly or physically accessible.");
                var beforeFailure = game.Serialize();
                Check(!game.BuildRoom(facility.Id, 10, floor).Success && game.Serialize() == beforeFailure,
                    facility.Id + ": overlap rejection changed cash, IDs, rooms or operations.");
                var second = Build(game, facility.Id, 20, floor);
                Check(second == room + 1, facility.Id + ": rejected placement consumed a room ID.");
                Succeed(game.SetOpen(room, false)); Succeed(game.SetOpen(second, false));
                var paused = game.Serialize();
                _ = game.OperationFor(room); _ = game.DemandFor(room); _ = game.OperatingWarning(room);
                _ = game.Rules.For(facility.Id); _ = game.World.Catalog.Get(facility.Id);
                Check(game.Serialize() == paused, facility.Id + ": inspection advanced the simulation.");
                var resumed = Restore(game);
                game.Advance(3600); resumed.Advance(3600);
                Check(game.Serialize() == resumed.Serialize(), facility.Id + ": closed headless continuation diverged after loading.");
                Check(!game.World.Ledger.Any(entry => (entry.EntityId == room || entry.EntityId == second) && entry.AmountMinor > 0)
                    && !game.People.Any(person => person.RoomId == room || person.RoomId == second),
                    facility.Id + ": a closed facility created customer activity or income.");
            }
        }));
        cases.Add(("Catalogue: placement keeps subway site/network and Hawaii waterfront gates even in sandbox", () =>
        {
            foreach (var (id, location, site, floor, sandbox) in new (string, string, string, int, bool)[]
            {
                ("dock", "hawaii", "waterfront", 0, false), // Rank 1 is below the optional dock gate.
                ("dock", "tokyo", "waterfront", 0, true),
                ("dock", "hawaii", "central", 0, true),
                ("dock", "hawaii", "waterfront", -1, true),
                ("subway", "tokyo", "central", -1, true),
                ("subway", "hawaii", "transit", -1, true),
                ("subway", "abu-dhabi", "transit", -1, true),
                ("subway", "tokyo", "transit", 0, true),
                ("parking", "tokyo", "central", 0, true)
            })
            {
                var game = new GameSession(Catalog, Rules, Locations, location, site, sandbox);
                if (floor < 0) Succeed(game.World.BuildFloor(-1));
                var before = game.Serialize();
                var result = game.BuildRoom(id, 10, floor);
                Check(!result.Success && !string.IsNullOrWhiteSpace(result.Message) && game.Serialize() == before,
                    $"{id}: {location}/{site}, floor {floor}, sandbox {sandbox} bypassed placement restrictions or mutated state.");
            }
        }));
    }

    private static JsonObject CatalogueFacility(JsonObject root, string id) => root["facilities"]!.AsArray()
        .Single(node => node!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject CatalogueRule(JsonObject root, string id) => root["businesses"]!.AsArray()
        .Single(node => node!["id"]!.GetValue<string>() == id)!.AsObject();
    private static string CatalogueEdit(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "construction.catalog.json")))!.AsObject();
        edit(root); return root.ToJsonString();
    }
    private static string RulesEdit(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "simulation.rules.json")))!.AsObject();
        edit(root); return root.ToJsonString();
    }
    private static void CatalogueReject(Action action, params string[] diagnostic)
    {
        try { action(); }
        catch (ContentValidationException error)
        {
            foreach (var expected in diagnostic)
                Check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    $"Expected diagnostic '{expected}', got '{error.Message}'.");
            return;
        }
        throw new InvalidOperationException("Invalid catalogue/rules were accepted.");
    }
}
