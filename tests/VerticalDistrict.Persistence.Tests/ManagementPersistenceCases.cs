using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Persistence.Tests;

internal sealed class ManagementPersistenceCases(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations)
{
    public (string Name, Action Test)[] Cases() =>
    [
        ("A real schema-three save retains overnight leases and the original rule fingerprint", LegacyOfficeNight),
        ("Management snapshots detach nested household and satisfaction arrays", DetachedManagement),
        ("Current management counters, members, references, and missing state are rejected", InvalidBusinessState),
        ("Active hotel and on-site service state survive exact continuation", ActiveManagementContinuation),
        ("Corrupt service ownership, deadlines, bookings, and complaints cannot replace a session", InvalidManagementReferences),
        ("Paid food orders and hotel stays cannot be restored as unpaid and charged again", RejectPaidStateDowngrade),
        ("Legacy service migration preserves deadlines and charges repair only when needed", LegacyServiceKinds),
        ("Legacy stranded and departing hotel guests retain the original checkout second", LegacyHotelCheckoutSeconds)
    ];

    private GameSession Read(string json) => GameSession.Deserialize(catalog, rules, locations, json);
    private GameSession Tower() => OperatingExampleScenario.Create(catalog, rules, locations);

    private void LegacyOfficeNight()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema3-office-night.json"));
        var old = JsonNode.Parse(json)!.AsObject();
        Equal(3, old["schemaVersion"]!.GetValue<int>());
        var migrated = Read(json);
        Equal(7, migrated.CaptureSnapshot().SchemaVersion);
        Equal(old["tick"]!.GetValue<long>(), migrated.Tick);
        Equal(old["randomState"]!.GetValue<uint>(), migrated.CaptureSnapshot().RandomState);
        Equal(old["world"]!["cashMinor"]!.GetValue<long>(), migrated.World.CashMinor);
        Require(JsonNode.DeepEquals(old["world"]!["ledger"], JsonSerializer.SerializeToNode(migrated.World.Ledger, SimulationRules.JsonOptions)),
            "Migration rewrote historical money or transaction IDs.");
        Equal(0, migrated.People.Count);
        Equal(3, migrated.Tenants.Count);
        Require(migrated.Tenants.All(t => t.Kind == "Office" && t.Status == TenancyStatus.Active
            && t.LastPaidDay == 1 && t.AgreedRentMinor == 85000 && t.MemberIds.Length == 8), "Empty offices lost their established lease.");
        Equal(24, migrated.Tenants.SelectMany(t => t.MemberIds).Distinct().Count());
        var resumed = Read(migrated.Serialize());
        Equal(migrated.Serialize(), resumed.Serialize());
        migrated.Advance(7200); resumed.Advance(7200);
        Equal(migrated.Serialize(), resumed.Serialize());
        Require(migrated.Billing.LastSettledDay == 1, "Old midnight billing was skipped or repeated.");
    }

    private void DetachedManagement()
    {
        var game = Tower(); game.Advance(3600);
        var before = game.Serialize();
        var snapshot = game.CaptureSnapshot();
        Require(snapshot.Management.Businesses.Tenants.Length > 0, "No real tenant was assigned.");
        snapshot.Management.Businesses.Tenants[0].MemberIds[0] = long.MaxValue;
        snapshot.Management.Businesses.Tenants[0] = snapshot.Management.Businesses.Tenants[0] with { AgreedRentMinor = 1 };
        snapshot.Management.Satisfaction.Experiences[0] = snapshot.Management.Satisfaction.Experiences[0] with { Satisfaction = 0 };
        Equal(before, game.Serialize());
    }

    private void InvalidBusinessState()
    {
        var game = Tower(); game.Advance(3600);
        var before = game.Serialize();
        Reject(before, root => root["management"] = null);
        Reject(before, root => root.Remove("management"));
        Reject(before, root => root["schemaVersion"] = 99);
        Reject(before, root => root["management"]!["businesses"]!["nextTenantId"] = 0);
        Reject(before, root => root["management"]!["businesses"]!["tenants"]![0]!["agreedRentMinor"] = -1);
        Reject(before, root => root["management"]!["businesses"]!["tenants"]![0]!["roomId"] = long.MaxValue);
        Reject(before, root => root["management"]!["businesses"]!["tenants"]![0]!["memberIds"]![0] = long.MaxValue);
        Reject(before, root => root["management"]!["businesses"]!["tenants"]![0]!["lastPaidDay"] = game.Day + 1);
        Reject(before, root =>
        {
            var members = root["management"]!["businesses"]!["tenants"]![0]!["memberIds"]!.AsArray();
            members[1] = members[0]!.GetValue<long>();
        });
        Equal(before, game.Serialize());
    }

    private GameSession ActiveManagement()
    {
        var game = Tower();
        for (var step = 0; step < 86400; step++)
        {
            game.Step();
            if (game.HotelBookings.Any(b => b.Status == HotelBookingStatus.CheckedIn)
                && game.ServiceTasks.Any(t => t.Status == ServiceTaskStatus.InProgress)) return game;
        }
        throw new InvalidOperationException("No simultaneous active hotel and on-site service state was reached.");
    }

    private void ActiveManagementContinuation()
    {
        var game = ActiveManagement();
        var checkpoint = game.Serialize();
        var loaded = Read(checkpoint);
        Equal(checkpoint, loaded.Serialize());
        game.Advance(7200); loaded.Advance(7200);
        Equal(game.Serialize(), loaded.Serialize());
    }

    private void InvalidManagementReferences()
    {
        var game = ActiveManagement();
        var before = game.Serialize();
        var workingIndex = game.ServiceTasks.ToList().FindIndex(t => t.Status == ServiceTaskStatus.InProgress);
        var bookingIndex = game.HotelBookings.ToList().FindIndex(b => b.Status == HotelBookingStatus.CheckedIn);
        Reject(before, root => root["management"]!["services"]!["nextTaskId"] = 1);
        Reject(before, root => root["management"]!["services"]!["tasks"]![workingIndex]!["workerPersonId"] = long.MaxValue);
        Reject(before, root => root["management"]!["services"]!["tasks"]![workingIndex]!["workDueTick"] = game.Tick + 3601);
        Reject(before, root => root["management"]!["services"]!["tasks"]![workingIndex]!["roomId"] = long.MaxValue);
        Reject(before, root => root["management"]!["businesses"]!["hotelBookings"]![bookingIndex]!["chargedMinor"] = 1);
        Reject(before, root => root["management"]!["businesses"]!["hotelBookings"]![bookingIndex]!["personId"] = long.MaxValue);
        Reject(before, root => root["management"]!["satisfaction"]!["experiences"]![0]!["satisfaction"] = 101);
        Reject(before, root => root["management"]!["satisfaction"]!["nextComplaintId"] = 0);
        Equal(before, game.Serialize());
    }

    private void RejectPaidStateDowngrade()
    {
        var foodGame = Tower();
        Until(foodGame, () => foodGame.FoodOrders.Any(order => order.Status == FoodOrderStatus.Completed
            && order.AgreedPriceMinor > 0 && foodGame.People.Any(person => person.Id == order.PersonId)), 14400);
        var order = foodGame.FoodOrders.First(order => order.Status == FoodOrderStatus.Completed
            && order.AgreedPriceMinor > 0 && foodGame.People.Any(person => person.Id == order.PersonId));
        foreach (var state in new[] { FoodOrderStatus.Serving, FoodOrderStatus.Abandoned })
            Reject(foodGame.Serialize(), root =>
            {
                var saved = Find(root["management"]!["businesses"]!["foodOrders"]!, "id", order.Id);
                saved["status"] = state.ToString();
                if (state == FoodOrderStatus.Serving)
                {
                    saved["finishedAt"] = 0;
                    var person = Find(root["people"]!, "id", order.PersonId);
                    person["activity"] = PersonActivity.BeingServed.ToString();
                    person["actionAt"] = order.CompletesAt;
                }
            }, "already has a ledger receipt");

        var hotelGame = ActiveManagement();
        var booking = hotelGame.HotelBookings.First(stay => stay.Status == HotelBookingStatus.CheckedIn && stay.ChargedMinor > 0);
        foreach (var state in new[] { HotelBookingStatus.Reserved, HotelBookingStatus.Cancelled })
            Reject(hotelGame.Serialize(), root =>
            {
                var saved = Find(root["management"]!["businesses"]!["hotelBookings"]!, "id", booking.Id);
                saved["status"] = state.ToString(); saved["checkedInAt"] = 0; saved["checkoutAt"] = 0; saved["chargedMinor"] = 0;
                if (state == HotelBookingStatus.Reserved)
                {
                    var person = Find(root["people"]!, "id", booking.PersonId);
                    person["activity"] = PersonActivity.Arriving.ToString(); person["actionAt"] = 0;
                }
                else
                {
                    saved["finishedAt"] = hotelGame.Tick; saved["endReason"] = "Forged cancellation.";
                    Find(root["operations"]!, "roomId", booking.RoomId)["reservationPersonId"] = null;
                }
            }, "already has a ledger receipt");
    }

    private void LegacyServiceKinds()
    {
        foreach (var hours in new[] { 9, 16 })
        {
            var game = new GameSession(catalog, rules, locations);
            Succeed(game.BuildRoom("lobby", 0, 0));
            var office = game.BuildRoom("office", 8, 0); Succeed(office);
            game.Advance(hours * 3600);
            Succeed(game.BuildRoom("service-room", 16, 0));
            Succeed(game.RepairRoom(office.EntityId!.Value));
            Until(game, () => game.ServiceTaskForRoom(office.EntityId.Value)?.Status == ServiceTaskStatus.InProgress, 1200);
            var worker = game.People.Single(person => person.Role == "Staff" && person.ServiceTargetId == office.EntityId);
            var needsRepair = game.OperationFor(office.EntityId.Value)!.Condition < 85;
            Equal(hours == 16, needsRepair);
            var old = JsonNode.Parse(game.Serialize())!.AsObject();
            old["schemaVersion"] = 3; old["rulesFingerprint"] = LegacyRulesHash(); old.Remove("management");
            old["transport"] = TransportPersistenceCases.LegacyTransportProjection(old["transport"]!);
            var migrated = Read(old.ToJsonString());
            var task = migrated.ServiceTasks.Single(job => job.WorkerPersonId == worker.Id);
            Equal(needsRepair ? ServiceTaskKind.CleaningAndMaintenance : ServiceTaskKind.Cleaning, task.Kind);
            Equal(worker.ActionAt, task.WorkDueTick!.Value);
            Equal(worker, migrated.People.Single(person => person.Id == worker.Id));
            var costBefore = migrated.OperationFor(office.EntityId.Value)!.CostsMinor;
            var restored = Read(migrated.Serialize());
            var remaining = checked((int)(worker.ActionAt - game.Tick));
            migrated.Advance(remaining); restored.Advance(remaining);
            Equal(migrated.Serialize(), restored.Serialize());
            Equal(rules.Management.CleaningCostMinor + (needsRepair ? rules.RepairCostMinor : 0),
                migrated.OperationFor(office.EntityId.Value)!.CostsMinor - costBefore);
            Equal(needsRepair ? 1 : 0, migrated.World.Ledger.Count(entry => entry.EntityId == office.EntityId
                && entry.Category == "Maintenance.Repair"));
        }
    }

    private void LegacyHotelCheckoutSeconds()
    {
        var game = new GameSession(catalog, rules, locations);
        Succeed(game.BuildRoom("lobby", 0, 0));
        var hotel = game.BuildRoom("hotel-room", 8, 0); Succeed(hotel);
        Until(game, () => game.HotelBookingFor(hotel.EntityId!.Value)?.Status == HotelBookingStatus.CheckedIn, 16 * 3600);
        var booking = game.HotelBookingFor(hotel.EntityId!.Value)!;
        var receipt = game.World.Ledger.Single(entry => entry.Category == "Sales.Hotel");
        Require(receipt.TimestampTicks % 60 != 0, "The migration fixture must check in between whole minutes.");
        var entryTick = receipt.TimestampTicks;
        // This is the pre-management checkout formula, including its retained arrival seconds.
        var expectedCheckout = entryTick + booking.Nights * 86400L
            - ((entryTick + 28500) / 3600 % 24 - 11) * 3600 - (entryTick + 28500) / 60 % 60 * 60;
        foreach (var activity in new[] { PersonActivity.Stranded, PersonActivity.Leaving })
        {
            var old = JsonNode.Parse(game.Serialize())!.AsObject();
            old["schemaVersion"] = 3; old["rulesFingerprint"] = LegacyRulesHash(); old.Remove("management");
            old["transport"] = TransportPersistenceCases.LegacyTransportProjection(old["transport"]!);
            var person = Find(old["people"]!, "id", booking.PersonId);
            person["activity"] = activity.ToString(); person["actionAt"] = game.Tick + 120;
            var ledgerReceipt = old["world"]!["ledger"]!.AsArray().OfType<JsonObject>()
                .Single(entry => entry["category"]!.GetValue<string>() == "Sales.Hotel");
            ledgerReceipt["description"] = $"Guest #{booking.PersonId}: Hotel admission/check-in.";
            if (activity == PersonActivity.Leaving)
            {
                var operation = Find(old["operations"]!, "roomId", booking.RoomId);
                operation["reservationPersonId"] = null; operation["dirty"] = true;
            }
            var migrated = Read(old.ToJsonString());
            var stay = migrated.HotelBookings.Single();
            Equal(expectedCheckout, stay.CheckoutAt);
            Equal(receipt.AmountMinor, stay.ChargedMinor);
            Equal(activity == PersonActivity.Stranded ? HotelBookingStatus.CheckedIn : HotelBookingStatus.CheckedOut, stay.Status);
            var resumed = Read(migrated.Serialize());
            migrated.Advance(600); resumed.Advance(600);
            Equal(migrated.Serialize(), resumed.Serialize());
            Equal(1, migrated.World.Ledger.Count(entry => entry.Category == "Sales.Hotel"));
        }
    }

    private static JsonObject Find(JsonNode array, string property, long id)
        => array.AsArray().OfType<JsonObject>().Single(item => item[property]!.GetValue<long>() == id);
    private string LegacyRulesHash()
    {
        var oldRules = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject();
        oldRules.Remove("products"); oldRules.Remove("management");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(oldRules, SimulationRules.JsonOptions)));
    }
    private static void Succeed(CommandResult result) => Require(result.Success, result.Message);
    private static void Until(GameSession game, Func<bool> condition, int maximumTicks)
    {
        for (var tick = 0; tick < maximumTicks && !condition(); tick++) game.Step();
        Require(condition(), $"Management fixture did not reach the required state by tick {game.Tick}.");
    }

    private void Reject(string json, Action<JsonObject> mutate, string? expectedMessage = null)
    {
        var root = JsonNode.Parse(json)!.AsObject(); mutate(root);
        try { Read(root.ToJsonString()); }
        catch (SaveValidationException exception)
        {
            Require(expectedMessage == null || exception.Message.Contains(expectedMessage, StringComparison.Ordinal),
                $"Expected validation '{expectedMessage}', got '{exception.Message}'.");
            return;
        }
        throw new InvalidOperationException("Corrupt management state was accepted.");
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
}
