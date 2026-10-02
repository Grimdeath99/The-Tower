using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Persistence.Tests;

internal sealed class RetailPersistenceCases(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
{
    public (string Name, Action Test)[] Cases() =>
    [
        ("Genuine schema-five paid shop visits and active food retain receipts, clocks, and exact service deadlines", LegacyPaidRetail),
        ("Genuine schema-five traveling shoppers become unpaid frozen orders without prepayment", LegacyTravelingRetail),
        ("Legacy rule hashes project only added products and current saves reject historical hashes", FingerprintBoundary),
        ("Product migration retains accepted food duration even when new default-product tuning differs", LegacyFrozenDuration),
        ("Selected menus, shared demand cursors, and accepted purchases resume identically", CurrentRetailContinuation),
        ("Retail snapshot arrays and inspection cannot mutate live purchase or demand state", DetachedRetail),
        ("Missing purchase fields, invalid products, forged receipts, and corrupt demand cursors are rejected", CorruptRetail),
        ("Invalid retail candidates preserve both last-good files and missing product content is rejected", LastGoodRetailSave)
    ];

    private GameSession Read(string json, SimulationRules? tuning = null) => GameSession.Deserialize(catalog, tuning ?? rules, locations, json);
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static JsonObject Node(string json) => JsonNode.Parse(json)!.AsObject();
    private static JsonObject Retail(JsonObject root) => root["management"]!["retail"]!.AsObject();
    private static JsonArray Orders(JsonObject root) => root["management"]!["businesses"]!["foodOrders"]!.AsArray();
    private static JsonObject Order(JsonObject root, long id) => Orders(root).OfType<JsonObject>().Single(order => order["id"]!.GetValue<long>() == id);

    private void LegacyPaidRetail()
    {
        var original = Fixture("schema5-mixed-retail.json"); var old = Node(original);
        Equal(5, old["schemaVersion"]!.GetValue<int>());
        var game = Read(original); Equal(7, game.CaptureSnapshot().SchemaVersion);
        Equal(old["tick"]!.GetValue<long>(), game.Tick); Equal(old["nextPersonId"]!.GetValue<long>(), game.CaptureSnapshot().NextPersonId);
        Equal(old["randomState"]!.GetValue<uint>(), game.CaptureSnapshot().RandomState);
        Check(JsonNode.DeepEquals(old["world"], Node(game.Serialize())["world"]), "Retail migration changed historical money or construction.");
        Check(JsonNode.DeepEquals(old["transport"], TransportPersistenceCases.LegacyTransportProjection(Node(game.Serialize())["transport"]!)), "Retail migration moved passengers or altered physical routes.");
        Check(JsonNode.DeepEquals(old["management"]!["ownership"], Node(game.Serialize())["management"]!["ownership"]), "Retail migration changed an existing ownership agreement.");
        foreach (var saved in old["management"]!["businesses"]!["foodOrders"]!.AsArray().OfType<JsonObject>())
        {
            var restored = Order(Node(game.Serialize()), saved["id"]!.GetValue<long>()).DeepClone().AsObject();
            restored.Remove("productId"); restored.Remove("agreedServiceSeconds");
            Check(JsonNode.DeepEquals(saved, restored), "Migration changed an accepted food order's price, activity, or deadline.");
        }
        var serving = game.FoodOrders.Single(order => order.Status == FoodOrderStatus.Serving);
        Equal(rules.DefaultProductFor("Food").Id, serving.ProductId);
        var purchase = game.RetailOrders.Single(order => order.Status == FoodOrderStatus.Completed);
        var receipt = game.World.Ledger.Single(entry => entry.Category == "Sales.Shop"
            && entry.Description.Contains($"Customer #{purchase.PersonId}:", StringComparison.Ordinal));
        Equal(receipt.AmountMinor, purchase.AgreedPriceMinor); Equal(receipt.TimestampTicks, purchase.FinishedAt);
        Check(game.CustomerDemand.All(cursor => cursor.Attempts == 0 && cursor.LastAttemptTick == 0 && cursor.NextAttemptTick == game.Tick),
            "Migration invented old demand attempts or a catch-up backlog.");
        var restoredGame = Read(game.Serialize()); Equal(game.Serialize(), restoredGame.Serialize());
        game.Advance(6000); restoredGame.Advance(6000); Equal(game.Serialize(), restoredGame.Serialize());
        Equal(1, game.World.Ledger.Count(entry => entry.Category == "Sales.Shop"
            && entry.Description.Contains($"customer #{purchase.PersonId}:", StringComparison.OrdinalIgnoreCase)));
        Equal(1, game.World.Ledger.Count(entry => entry.Category == "Sales.Food"
            && entry.Description.Contains($"customer #{serving.PersonId}:", StringComparison.OrdinalIgnoreCase)));
    }

    private void LegacyTravelingRetail()
    {
        var original = Fixture("schema5-traveling-retail.json"); var game = Read(original);
        var incoming = game.RetailOrders.Single(order => order.Status == FoodOrderStatus.Traveling);
        Check(!game.World.Ledger.Any(entry => entry.Category == "Sales.Shop"
            && entry.Description.Contains($"customer #{incoming.PersonId}:", StringComparison.OrdinalIgnoreCase)), "Migration prepaid a traveling shopper.");
        Equal(game.OperationFor(incoming.RoomId)!.PriceMinor, incoming.AgreedPriceMinor);
        Must(game.SetProduct(incoming.RoomId, "shop-gifts"));
        var restored = Read(game.Serialize()); game.Advance(3600); restored.Advance(3600);
        Equal(game.Serialize(), restored.Serialize());
        var receipt = game.World.Ledger.Single(entry => entry.Category == "Sales.Shop"
            && entry.Description.Contains($"customer #{incoming.PersonId}:", StringComparison.OrdinalIgnoreCase));
        Equal(incoming.AgreedPriceMinor, receipt.AmountMinor);
        Check(receipt.TimestampTicks >= incoming.CreatedAt + incoming.AgreedServiceSeconds,
            "Imported traveling shopper paid before completing its new timed purchase.");
    }

    private void FingerprintBoundary()
    {
        var legacy = Fixture("schema5-mixed-retail.json"); var old = Node(legacy);
        var projected = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject(); projected.Remove("products");
        var oldRules = Node(Fixture("schema5-simulation.rules.json"));
        Check(JsonNode.DeepEquals(oldRules, projected), "Non-product rule data drifted from the genuine schema-five baseline.");
        Equal(old["rulesFingerprint"]!.GetValue<string>(), Hash(projected));
        var current = Read(legacy).Serialize();
        Reject(current, root => root["rulesFingerprint"] = old["rulesFingerprint"]!.GetValue<string>());
        Reject(current, root => root["schemaVersion"] = 5);
        var oldWithCurrentHash = Node(legacy); oldWithCurrentHash["rulesFingerprint"] = Node(current)["rulesFingerprint"]!.GetValue<string>();
        Rejected(() => Read(oldWithCurrentHash.ToJsonString()));
        var changed = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject();
        changed["repairCostMinor"] = rules.RepairCostMinor + 1;
        var changedRules = SimulationRules.Load(changed.ToJsonString(), catalog);
        Rejected(() => Read(legacy, changedRules));
    }

    private void LegacyFrozenDuration()
    {
        var legacy = Fixture("schema5-mixed-retail.json");
        var changed = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject();
        changed["products"]!.AsArray().OfType<JsonObject>().Single(product => product["id"]!.GetValue<string>() == "cafe-classic")["servicePercent"] = 200;
        var customRules = SimulationRules.Load(changed.ToJsonString(), catalog);
        var game = Read(legacy, customRules); var original = Read(legacy);
        var serving = game.FoodOrders.Single(order => order.Status == FoodOrderStatus.Serving);
        var prior = original.FoodOrders.Single(order => order.Id == serving.Id);
        Equal(prior.CompletesAt, serving.CompletesAt); Equal(prior.AgreedServiceSeconds, serving.AgreedServiceSeconds);
        Equal(2 * serving.AgreedServiceSeconds, game.ProductServiceSeconds(serving.RoomId));
        var restored = Read(game.Serialize(), customRules); game.Advance(1200); restored.Advance(1200); Equal(game.Serialize(), restored.Serialize());
        Rejected(() => Read(original.Serialize(), customRules));
    }

    private (GameSession Game, long Food, long Shop) CurrentMarket()
    {
        var game = new GameSession(catalog, rules, locations, sandbox: true);
        Must(game.BuildRoom("lobby", 0, 0));
        var food = game.BuildRoom("cafe", 6, 0); Must(food);
        var shop = game.BuildRoom("shop", 12, 0); Must(shop);
        Must(game.BuildRoom("service-room", 20, 0));
        Until(game, () => game.FoodOrders.Any(order => order.Status == FoodOrderStatus.Serving)
            && game.RetailOrders.Any(order => order.Status == FoodOrderStatus.Serving), 12 * 3600);
        return (game, food.EntityId!.Value, shop.EntityId!.Value);
    }

    private void CurrentRetailContinuation()
    {
        var (game, food, shop) = CurrentMarket();
        var accepted = game.CaptureSnapshot().Management.Businesses.FoodOrders.Where(order => order.Status == FoodOrderStatus.Serving).ToArray();
        Must(game.SetProduct(food, "cafe-lunch")); Must(game.SetProduct(shop, "shop-gifts"));
        var saved = game.Serialize(); var restored = Read(saved); Equal(saved, restored.Serialize());
        Equal("cafe-lunch", restored.ProductForRoom(food)!.Id); Equal("shop-gifts", restored.ProductForRoom(shop)!.Id);
        Check(game.CustomerDemand.SequenceEqual(restored.CustomerDemand), "Shared customer demand cursors changed on load.");
        foreach (var order in accepted) Equal(order, restored.CaptureSnapshot().Management.Businesses.FoodOrders.Single(value => value.Id == order.Id));
        game.Advance(86400); restored.Advance(86400); Equal(game.Serialize(), restored.Serialize());
        foreach (var order in accepted)
        {
            var receipt = game.World.Ledger.Single(entry => entry.Description.StartsWith($"Order #{order.Id}, customer #{order.PersonId}:", StringComparison.Ordinal));
            Equal(order.AgreedPriceMinor, receipt.AmountMinor);
        }
    }

    private void DetachedRetail()
    {
        var (game, food, shop) = CurrentMarket(); Must(game.SetProduct(food, "cafe-lunch")); Must(game.SetProduct(shop, "shop-gifts"));
        var before = game.Serialize(); var snapshot = game.CaptureSnapshot();
        snapshot.Management.Retail.Selections[0] = new RoomProductSelection(long.MaxValue, "missing");
        snapshot.Management.Retail.Demand[0] = snapshot.Management.Retail.Demand[0] with { Attempts = long.MaxValue };
        snapshot.Management.Businesses.FoodOrders[0] = snapshot.Management.Businesses.FoodOrders[0] with { ProductId = "missing" };
        for (var index = 0; index < 10; index++) { _ = game.RetailOrders; _ = game.FoodOrders; _ = game.CustomerDemand; _ = game.RetailPerformanceFor(shop); _ = game.ProductForRoom(food); }
        Equal(before, game.Serialize());
    }

    private void CorruptRetail()
    {
        var (game, food, shop) = CurrentMarket(); Must(game.SetProduct(food, "cafe-lunch")); Must(game.SetProduct(shop, "shop-gifts"));
        var before = game.Serialize(); var serving = game.RetailOrders.Single(order => order.Status == FoodOrderStatus.Serving);
        Reject(before, root => root["management"]!["retail"] = null);
        Reject(before, root => root["management"]!.AsObject().Remove("retail"));
        Reject(before, root => Order(root, serving.Id).Remove("productId"));
        Reject(before, root => Order(root, serving.Id).Remove("agreedServiceSeconds"));
        Reject(before, root => Order(root, serving.Id)["productId"] = "missing-product");
        Reject(before, root => Order(root, serving.Id)["productId"] = "cafe-classic");
        Reject(before, root => Order(root, serving.Id)["agreedServiceSeconds"] = serving.AgreedServiceSeconds + 1);
        Reject(before, root => Order(root, serving.Id)["completesAt"] = serving.ServiceStartedAt);
        Reject(before, root => Retail(root)["legacyThroughOrderId"] = long.MaxValue);
        Reject(before, root => Retail(root)["selections"]![0]!["roomId"] = 1);
        Reject(before, root => Retail(root)["selections"]![0]!["productId"] = "missing-product");
        Reject(before, root => Retail(root)["selections"]!.AsArray().Add(Retail(root)["selections"]![0]!.DeepClone()));
        Reject(before, root => Retail(root)["demand"]![0]!["nextAttemptTick"] = game.CustomerDemand[0].NextAttemptTick + 1);
        Reject(before, root => Retail(root)["demand"]![0]!["attempts"] = long.MaxValue);
        Reject(before, root => Retail(root)["demand"]![0]!["model"] = "Unsupported");
        Reject(before, root => Retail(root)["demand"]!.AsArray().RemoveAt(0));
        Until(game, () => game.RetailOrders.Any(order => order.Status == FoodOrderStatus.Completed), 1200);
        var paid = game.RetailOrders.First(order => order.Status == FoodOrderStatus.Completed); before = game.Serialize();
        Reject(before, root => Order(root, paid.Id)["status"] = "Abandoned");
        Reject(before, root => Order(root, paid.Id)["agreedPriceMinor"] = paid.AgreedPriceMinor + 1);
        Reject(before, root =>
        {
            Order(root, paid.Id)["productId"] = "shop-gifts";
            Order(root, paid.Id)["agreedServiceSeconds"] = rules.ProductFor("shop-gifts")!.ServiceSecondsFor(rules.For("shop")!);
        });
        Equal(before, game.Serialize());
    }

    private void LastGoodRetailSave()
    {
        var (game, food, _) = CurrentMarket(); Must(game.SetProduct(food, "cafe-lunch"));
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "retail-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.json"); var first = game.Serialize();
            bool Validate(string json) { try { Read(json); return true; } catch (SaveValidationException) { return false; } }
            SaveFileStore.Save(path, first, Validate); game.Advance(60); var second = game.Serialize(); SaveFileStore.Save(path, second, Validate);
            var bad = Node(second); Retail(bad)["selections"]![0]!["productId"] = "removed-product";
            Rejected(() => SaveFileStore.Save(path, bad.ToJsonString(), Validate));
            Equal(second, SaveFileStore.Load(path)); Equal(first, SaveFileStore.LoadBackup(path)); Equal(second, game.Serialize());
            var ruleJson = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject();
            var products = ruleJson["products"]!.AsArray();
            products.Remove(products.OfType<JsonObject>().Single(product => product["id"]!.GetValue<string>() == "cafe-lunch"));
            var missingContent = SimulationRules.Load(ruleJson.ToJsonString(), catalog);
            Rejected(() => Read(second, missingContent)); Equal(second, SaveFileStore.Load(path));
            Equal(first, Read(SaveFileStore.LoadBackup(path)).Serialize());
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory, false); }
    }

    private static string Hash(JsonObject value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SimulationRules.JsonOptions)));
    private void Reject(string json, Action<JsonObject> mutate) { var root = Node(json); mutate(root); Rejected(() => Read(root.ToJsonString())); }
    private static void Rejected(Action action)
    {
        try { action(); } catch (SaveValidationException) { return; }
        throw new InvalidOperationException("Corrupt or incompatible retail state was accepted.");
    }
    private static void Until(GameSession game, Func<bool> condition, int maximum)
    { for (var tick = 0; tick < maximum && !condition(); tick++) game.Step(); Check(condition(), $"Retail fixture failed to reach its state by tick {game.Tick}."); }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
