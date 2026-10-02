using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Persistence.Tests;

internal sealed class OwnershipPersistenceCases(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
{
    public (string Name, Action Test)[] Cases() =>
    [
        ("A genuine schema-four condo sale migrates its paid price, residents, and physical journeys", MigrateSoldCondo),
        ("A genuine legacy owned condo without physical residents recovers a durable household", MigrateOutsideHousehold),
        ("A genuine legacy free condominium preserves its receipt-free purchase and refunds only once", MigrateFreeCondo),
        ("Schema-five ownership arrays and public household inspections are detached", DetachedHousehold),
        ("Corrupt ownership identity, price, flags, receipts, and missing state are rejected", RejectCorruptOwnership),
        ("Pending condo offers retain quotes and deadlines through save and physical purchase", PendingContinuation),
        ("Refunded ownership cannot be restored as a refundable sale or change its original refund", RejectRefundCorruption),
        ("Completed ownership survives real demolition and rejects fabricated historical room references", HistoricalOwnership),
        ("Invalid ownership saves preserve the live session and both last-good files", LastGoodOwnershipSave)
    ];

    private GameSession Read(string json) => GameSession.Deserialize(catalog, rules, locations, json);
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private GameSession ImportedSale() => Read(Fixture("schema4-sold-condo.json"));

    private (GameSession Game, long Room) NewCondo()
    {
        var game = new GameSession(catalog, rules, locations, sandbox: true);
        Must(game.BuildRoom("lobby", 0, 0));
        var condo = game.BuildRoom("condo", 8, 0); Must(condo);
        return (game, condo.EntityId!.Value);
    }

    private void MigrateSoldCondo()
    {
        var original = Fixture("schema4-sold-condo.json");
        var old = JsonNode.Parse(original)!;
        Equal(4, old["schemaVersion"]!.GetValue<int>());
        var game = Read(original); var agreement = game.Ownerships.Single();
        Equal(7, game.CaptureSnapshot().SchemaVersion);
        Equal(CondoOwnershipStatus.Owned, agreement.Status);
        Equal(2_200_000L, agreement.AgreedPriceMinor);
        Equal(old["tick"]!.GetValue<long>(), game.Tick);
        Equal(old["randomState"]!.GetValue<uint>(), game.CaptureSnapshot().RandomState);
        Equal(old["world"]!["cashMinor"]!.GetValue<long>(), game.World.CashMinor);
        Check(JsonNode.DeepEquals(old["world"]!["ledger"], JsonSerializer.SerializeToNode(game.World.Ledger, SimulationRules.JsonOptions)),
            "Migration rewrote historical cash, receipts, or transaction identities.");
        Check(JsonNode.DeepEquals(old["transport"], TransportPersistenceCases.LegacyTransportProjection(JsonSerializer.SerializeToNode(game.Transport.CaptureSnapshot(), SimulationRules.JsonOptions)!)),
            "Migration moved physical people or changed their transport journeys.");
        var oldIds = old["people"]!.AsArray().Select(person => person!["id"]!.GetValue<long>()).Order().ToArray();
        Check(agreement.ResidentIds.Order().SequenceEqual(oldIds), "Existing condo residents lost their stable identities.");
        var sale = game.World.Ledger.Single(entry => entry.Category == "Sales.Condo");
        Equal(sale.Sequence, agreement.SaleLedgerSequence!.Value);
        Equal(sale.TimestampTicks, agreement.PurchasedAtTick!.Value);
        Check(game.People.All(person => person.ActionAt > game.Tick && (person.ActionAt + 28500) % 86400 == 8 * 3600),
            "Legacy visits did not adopt the explicit next-morning schedule.");
        Check(game.Notices.Any(notice => notice.Kind == "Migration" && notice.Text.Contains("08:00", StringComparison.Ordinal)),
            "The changed legacy residence schedule was not disclosed.");
        var resumed = Read(game.Serialize()); Equal(game.Serialize(), resumed.Serialize());
        game.Advance(86400); resumed.Advance(86400);
        Equal(game.Serialize(), resumed.Serialize());
        Equal(1, game.World.Ledger.Count(entry => entry.Category == "Sales.Condo"));
    }

    private void MigrateOutsideHousehold()
    {
        var json = Fixture("schema4-owned-condo-outside.json"); var old = JsonNode.Parse(json)!;
        Equal(0, old["people"]!.AsArray().Count);
        var game = Read(json); var agreement = game.Ownerships.Single();
        Equal(CondoOwnershipStatus.Owned, agreement.Status); Equal(0, game.People.Count);
        Equal(rules.For("condo")!.Capacity, agreement.ResidentIds.Length);
        Equal(agreement.ResidentIds.Length, agreement.ResidentIds.Distinct().Count());
        Check(game.CaptureSnapshot().NextPersonId > old["nextPersonId"]!.GetValue<long>(), "Missing household IDs were not reserved.");
        Check(agreement.ResidentIds.All(id => id > 0 && id < game.CaptureSnapshot().NextPersonId), "Recovered residents exceed the person counter.");
        Equal(old["world"]!["cashMinor"]!.GetValue<long>(), game.World.CashMinor);
        var restored = Read(game.Serialize()); Equal(game.Serialize(), restored.Serialize());
        Must(restored.BuyBackCondo(agreement.RoomId));
        Equal(CondoOwnershipStatus.Reacquired, restored.Ownerships.Single().Status);
        Equal(1, restored.World.Ledger.Count(entry => entry.Category == "Condo.Buyback"));
        var after = restored.Serialize(); Check(!restored.BuyBackCondo(agreement.RoomId).Success, "An absent household enabled a second refund.");
        Equal(after, restored.Serialize());
    }

    private void DetachedHousehold()
    {
        var game = ImportedSale(); var before = game.Serialize();
        var snapshot = game.CaptureSnapshot();
        snapshot.Management.Ownership.Contracts[0].ResidentIds[0] = long.MaxValue;
        snapshot.Management.Ownership.Contracts[0] = snapshot.Management.Ownership.Contracts[0] with { AgreedPriceMinor = 1 };
        game.Ownerships[0].ResidentIds[0] = long.MaxValue;
        game.OwnershipFor(game.Ownerships[0].RoomId)!.ResidentIds[0] = long.MaxValue;
        Equal(before, game.Serialize());
    }

    private void MigrateFreeCondo()
    {
        var original = Fixture("schema4-free-condo.json"); var old = JsonNode.Parse(original)!;
        var game = Read(original); var ownership = game.Ownerships.Single();
        Equal(0L, ownership.AgreedPriceMinor); Check(ownership.SaleLedgerSequence == null, "Migration invented a historical free-sale receipt.");
        var originalArrival = old["people"]!.AsArray().Select(person => person!["actionAt"]!.GetValue<long>() - 30 * 86400L).Min();
        Equal(originalArrival, ownership.PurchasedAtTick!.Value);
        Check(JsonNode.DeepEquals(old["world"]!["ledger"], JsonSerializer.SerializeToNode(game.World.Ledger, SimulationRules.JsonOptions)),
            "Free-sale migration rewrote the old ledger.");
        Equal(game.Serialize(), Read(game.Serialize()).Serialize());
        var cash = game.World.CashMinor; Must(game.BuyBackCondo(ownership.RoomId)); Equal(cash, game.World.CashMinor);
        Equal(game.Serialize(), Read(game.Serialize()).Serialize());
        Equal(1, game.World.Ledger.Count(entry => entry.Category == "Condo.Buyback"));
        var after = game.Serialize(); Check(!game.BuyBackCondo(ownership.RoomId).Success, "Free ownership was repurchased twice.");
        Equal(after, game.Serialize());
        Reject(Read(original).Serialize(), root => Ownership(root)["legacyThroughOwnershipId"] = 0);
    }

    private void RejectCorruptOwnership()
    {
        var game = ImportedSale(); var before = game.Serialize();
        Reject(before, root => root["management"]!["ownership"] = null);
        Reject(before, root => root["management"]!.AsObject().Remove("ownership"));
        Reject(before, root => Ownership(root)["nextOwnershipId"] = 1);
        Reject(before, root => Ownership(root)["legacyThroughOwnershipId"] = long.MaxValue);
        Reject(before, root => Contract(root)["id"] = 0);
        Reject(before, root => Contract(root)["ownerId"] = long.MaxValue);
        Reject(before, root => Contract(root)["roomId"] = long.MaxValue);
        Reject(before, root => Contract(root)["residentIds"]![1] = Contract(root)["residentIds"]![0]!.GetValue<long>());
        Reject(before, root => Contract(root)["residentIds"]!.AsArray().RemoveAt(1));
        Reject(before, root => Contract(root)["agreedPriceMinor"] = -1);
        Reject(before, root => Contract(root)["saleLedgerSequence"] = 1);
        Reject(before, root => Contract(root)["saleLedgerSequence"] = null);
        Reject(before, root => Contract(root)["purchasedAtTick"] = game.Tick + 1);
        Reject(before, root =>
        {
            Contract(root)["agreedPriceMinor"] = 3_000_000;
            Operation(root, game.Ownerships[0].RoomId)["condoSaleMinor"] = 3_000_000;
        });
        Reject(before, root =>
        {
            Ownership(root)["contracts"] = new JsonArray();
            var op = Operation(root, game.Ownerships[0].RoomId);
            op["condoSold"] = false; op["condoSaleMinor"] = 0; op["contractActive"] = false;
        });
        Reject(before, root => root["world"]!["contentFingerprint"] = "Unavailable-content-definition");
        Equal(before, game.Serialize());
    }

    private void PendingContinuation()
    {
        var (game, room) = NewCondo();
        Until(game, () => game.OwnershipFor(room)?.Status == CondoOwnershipStatus.PendingSale, 14 * 3600);
        var accepted = game.OwnershipFor(room)!; Must(game.SetPrice(room, accepted.AgreedPriceMinor + 100));
        var saved = game.Serialize(); var resumed = Read(saved); Equal(saved, resumed.Serialize());
        game.Advance(1200); resumed.Advance(1200); Equal(game.Serialize(), resumed.Serialize());
        var purchased = game.OwnershipFor(room)!;
        Equal(CondoOwnershipStatus.Owned, purchased.Status);
        Equal(accepted.AgreedPriceMinor, purchased.AgreedPriceMinor);
        Equal(accepted.OfferedAtTick, purchased.OfferedAtTick); Equal(accepted.OfferExpiresAtTick, purchased.OfferExpiresAtTick);
        Equal(accepted.AgreedPriceMinor, game.World.Ledger.Single(entry => entry.Category == "Sales.Condo").AmountMinor);
        Reject(saved, root => Contract(root)["offerExpiresAtTick"] = accepted.OfferedAtTick);
    }

    private void RejectRefundCorruption()
    {
        var game = ImportedSale(); var room = game.Ownerships.Single().RoomId;
        Must(game.BuyBackCondo(room));
        var before = game.Serialize(); Equal(before, Read(before).Serialize());
        Reject(before, root => Contract(root)["buybackLedgerSequence"] = Contract(root)["saleLedgerSequence"]!.GetValue<long>());
        Reject(before, root => Contract(root)["buybackLedgerSequence"] = null);
        Reject(before, root => Contract(root)["reacquiredAtTick"] = game.Tick + 1);
        Reject(before, root =>
        {
            var ownership = Contract(root); ownership["status"] = "Owned"; ownership["reacquiredAtTick"] = null;
            ownership["buybackLedgerSequence"] = null; ownership["endReason"] = "";
            var op = Operation(root, room); op["condoSold"] = true; op["condoSaleMinor"] = ownership["agreedPriceMinor"]!.GetValue<long>();
            op["contractActive"] = true; op["open"] = true;
        });
        var resumed = Read(before); game.Advance(600); resumed.Advance(600); Equal(game.Serialize(), resumed.Serialize());
        Equal(CondoOwnershipStatus.Reacquired, game.Ownerships.Single().Status);
        Equal(1, game.World.Ledger.Count(entry => entry.Category == "Condo.Buyback"));
    }

    private void HistoricalOwnership()
    {
        var game = ImportedSale(); var room = game.Ownerships.Single().RoomId;
        Must(game.BuyBackCondo(room)); game.Advance(600); Must(game.DemolishRoom(room));
        Check(game.World.Rooms.All(current => current.Id != room), "Condominium was not actually demolished.");
        var before = game.Serialize(); Equal(before, Read(before).Serialize());
        Equal(CondoOwnershipStatus.Reacquired, game.Ownerships.Single().Status);
        Reject(before, root => Contract(root)["roomId"] = 1); // The surviving lobby is not a condo.
        Reject(before, root => Contract(root)["roomId"] = long.MaxValue);
        Reject(before, root => Contract(root)["status"] = "Owned");
    }

    private void LastGoodOwnershipSave()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "ownership-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.json"); var game = ImportedSale();
            var first = game.Serialize();
            bool Validate(string json) { try { Read(json); return true; } catch (SaveValidationException) { return false; } }
            SaveFileStore.Save(path, first, Validate); game.Advance(60); var second = game.Serialize();
            SaveFileStore.Save(path, second, Validate); Equal(first, SaveFileStore.LoadBackup(path));
            var corrupt = JsonNode.Parse(second)!.AsObject(); Contract(corrupt)["agreedPriceMinor"] = 1;
            try { SaveFileStore.Save(path, corrupt.ToJsonString(), Validate); throw new InvalidOperationException("Corrupt ownership replaced the last good file."); }
            catch (SaveValidationException) { }
            Equal(second, SaveFileStore.Load(path)); Equal(first, SaveFileStore.LoadBackup(path)); Equal(second, game.Serialize());
            var recovered = Read(SaveFileStore.LoadBackup(path)); Must(recovered.BuyBackCondo(recovered.Ownerships.Single().RoomId));
            Equal(1, recovered.World.Ledger.Count(entry => entry.Category == "Condo.Buyback"));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory, false);
        }
    }

    private static JsonObject Ownership(JsonObject root) => root["management"]!["ownership"]!.AsObject();
    private static JsonObject Contract(JsonObject root) => Ownership(root)["contracts"]![0]!.AsObject();
    private static JsonObject Operation(JsonObject root, long room) => root["operations"]!.AsArray().OfType<JsonObject>().Single(op => op["roomId"]!.GetValue<long>() == room);
    private void Reject(string json, Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(json)!.AsObject(); mutate(root);
        try { Read(root.ToJsonString()); }
        catch (SaveValidationException) { return; }
        throw new InvalidOperationException("Corrupt ownership state was accepted.");
    }
    private static void Until(GameSession game, Func<bool> condition, int maximum)
    {
        for (var tick = 0; tick < maximum && !condition(); tick++) game.Step();
        Check(condition(), $"Ownership fixture did not reach its required state by {game.Tick}.");
    }
    private static void Must(CommandResult result) => Check(result.Success, result.Message);
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
