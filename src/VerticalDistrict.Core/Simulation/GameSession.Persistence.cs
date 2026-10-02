using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed record SessionSnapshot(int SchemaVersion, string RulesFingerprint, string GeographyFingerprint,
    ConstructionSnapshot World, TransportSnapshot Transport, string LocationId, string SiteId, bool Sandbox,
    long Tick, long NextPersonId, uint RandomState, int Reputation, int CompletedTrips, int PeakPopulation,
    long TodayRevenue, long TodayExpenses, int TodayArrivals, int TodayDepartures, int TodayAbandoned,
    RoomOperation[] Operations, PersonState[] People, DailyReport[] Reports, GameNotice[] Notices, FinanceSnapshot Finance,
    ManagementSnapshot Management);

public sealed partial class GameSession
{
    private static readonly JsonSerializerOptions SaveOptions = CreateSaveOptions();
    private static JsonSerializerOptions CreateSaveOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type => { if (type.Kind == JsonTypeInfoKind.Object) foreach (var p in type.Properties) if (p.Set != null) p.IsRequired = true; });
        return new JsonSerializerOptions(SimulationRules.JsonOptions) { TypeInfoResolver = resolver };
    }
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SimulationRules.JsonOptions)));
    public SessionSnapshot CaptureSnapshot() => new(7, Fingerprint(Rules), Fingerprint(Locations.Locations),
        World.CaptureSnapshot(), Transport.CaptureSnapshot(), LocationId, SiteId, Sandbox,
        Tick, _nextPersonId, _randomState, Reputation, CompletedTrips, PeakPopulation,
        _todayRevenue, _todayExpenses, _todayArrivals, _todayDepartures, _todayAbandoned,
        _operations.Values.ToArray(), _people.Values.ToArray(), _reports.ToArray(), _notices.ToArray(),
        new FinanceSnapshot(_billing, _currentPeriodFirstSequence, _financialPeriods.ToArray(), _legacyFinanceThroughSequence,
            _pendingBilling.Select(b => b with { Obligations = b.Obligations.ToArray() }).ToArray()), CaptureManagementSnapshot());
    public string Serialize() => JsonSerializer.Serialize(CaptureSnapshot(), SaveOptions);
    public static GameSession Deserialize(ContentCatalog catalog, SimulationRules rules, LocationCatalog locations, string json)
    {
        try
        {
            StrictJson.Validate(json);
            using var document = JsonDocument.Parse(json);
            // Tested migration of an earlier standalone construction schema into the live-session format.
            if (document.RootElement.TryGetProperty("contentFingerprint", out _) && !document.RootElement.TryGetProperty("world", out _))
            {
                var legacyWorld = ConstructionSaveCodec.Deserialize(catalog, json);
                var migrated = new GameSession(catalog, rules, locations) { World = legacyWorld };
                Require(legacyWorld.SimulationClockTicks is null, "A standalone import cannot contain an active session clock.");
                legacyWorld.SetSimulationClock(migrated.Tick);
                migrated.Transport = new TransportSystem(legacyWorld);
                migrated.SynchronizeConstruction();
                migrated.Notice("Imported construction save. Historical construction times remain command ordinals; simulation starts at Day 1, 07:55.", "Migration");
                return migrated;
            }
            var legacyClock = false;
            var legacyFinance = false;
            var legacyManagement = false;
            var legacyOwnership = false;
            var legacyRetail = false;
            var legacyTransport = false;
            if (JsonNode.Parse(json) is JsonObject root && root["world"] is JsonObject worldJson)
            {
                legacyClock = ConstructionSaveCodec.UpgradeLegacySnapshot(worldJson);
                if (root["schemaVersion"]?.GetValue<int>() == 2)
                {
                    Require(!root.ContainsKey("finance"), "A legacy session cannot contain newer financial metadata.");
                    legacyFinance = true;
                    root["schemaVersion"] = 3;
                    root["finance"] = JsonSerializer.SerializeToNode(new FinanceSnapshot(new BillingSchedule(0, 0, FirstBillingTick), 1, [], 0, []), SaveOptions);
                }
                if (root["schemaVersion"]?.GetValue<int>() == 3)
                {
                    Require(!root.ContainsKey("management"), "A legacy session cannot contain newer management state.");
                    legacyManagement = true;
                    root["schemaVersion"] = 4;
                    root["management"] = JsonSerializer.SerializeToNode(EmptyManagementSnapshot(), SaveOptions);
                    if (root["finance"] is JsonObject finance && finance["pendingBatches"] is JsonArray batches)
                        foreach (var batch in batches.OfType<JsonObject>())
                            if (batch["obligations"] is JsonArray obligations)
                                foreach (var obligation in obligations.OfType<JsonObject>())
                                    if (!obligation.ContainsKey("tenantId")) obligation["tenantId"] = null;
                }
                if (root["schemaVersion"]?.GetValue<int>() == 4)
                {
                    Require(root["management"] is JsonObject, "Missing authoritative management state.");
                    var management = root["management"]!.AsObject();
                    Require(legacyManagement || !management.ContainsKey("ownership"), "A legacy session cannot contain newer ownership state.");
                    legacyOwnership = true;
                    root["schemaVersion"] = 5;
                    management["ownership"] = JsonSerializer.SerializeToNode(new OwnershipSnapshot(1, 0, []), SaveOptions);
                }
                if (root["schemaVersion"]?.GetValue<int>() == 5)
                {
                    Require(root["management"] is JsonObject, "Missing authoritative management state.");
                    var management = root["management"]!.AsObject();
                    Require(legacyManagement || !management.ContainsKey("retail"), "A legacy session cannot contain newer retail state.");
                    legacyRetail = true;
                    root["schemaVersion"] = 6;
                    management["retail"] = JsonSerializer.SerializeToNode(EmptyRetailSnapshot(), SaveOptions);
                    if (management["businesses"] is JsonObject businesses && businesses["foodOrders"] is JsonArray orders)
                        foreach (var order in orders.OfType<JsonObject>())
                        {
                            Require(!order.ContainsKey("productId") && !order.ContainsKey("agreedServiceSeconds"), "A legacy purchase cannot contain newer product terms.");
                            order["productId"] = ""; order["agreedServiceSeconds"] = 0;
                        }
                }
                if (root["schemaVersion"]?.GetValue<int>() == 6)
                {
                    Require(root["transport"] is JsonObject, "Missing authoritative transport state.");
                    UpgradeLegacyTransportJson(root["transport"]!.AsObject());
                    legacyTransport = true;
                    root["schemaVersion"] = 7;
                }
                if (legacyClock || legacyFinance || legacyManagement || legacyOwnership || legacyRetail || legacyTransport) json = root.ToJsonString();
            }
            var save = JsonSerializer.Deserialize<SessionSnapshot>(json, SaveOptions) ?? throw new SaveValidationException("Empty session save.");
            Require(save.SchemaVersion == 7, "Unsupported session save version.");
            Require((legacyRetail
                    ? save.RulesFingerprint == LegacyRulesFingerprint(rules, false)
                        || legacyManagement && save.RulesFingerprint == LegacyRulesFingerprint(rules, true)
                    : save.RulesFingerprint == Fingerprint(rules))
                && save.GeographyFingerprint == Fingerprint(locations.Locations), "This save uses different balance or geography definitions.");
            Require(save.World != null && save.Transport != null && save.Operations != null && save.People != null && save.Reports != null && save.Notices != null, "Missing session state.");
            Require(save.Tick is >= 0 and <= 315360000 && save.NextPersonId > 0 && save.RandomState != 0, "Invalid clock, ID, or random state.");
            Require(save.Reputation is >= 0 and <= 100 && save.CompletedTrips >= 0 && save.PeakPopulation >= 0 && save.TodayRevenue >= 0 && save.TodayExpenses >= 0
                && save.TodayArrivals >= 0 && save.TodayDepartures >= 0 && save.TodayAbandoned >= 0, "Invalid session counters.");
            var world = ConstructionWorld.FromSnapshot(catalog, save.World!);
            Require(legacyClock || world.SimulationClockTicks == save.Tick, "Construction and session clocks disagree.");
            if (legacyClock) world.SetSimulationClock(save.Tick);
            Require(save.Transport!.SchemaVersion == 2, "Current sessions require the current transport schema.");
            TransportSystem transport;
            try { transport = TransportSystem.RestoreSnapshot(world, save.Transport); }
            catch (ArgumentException ex) { throw new SaveValidationException(ex.Message); }
            Require(save.Transport!.CurrentTick == save.Tick, "Transport and world clocks disagree.");
            var session = new GameSession(catalog, rules, locations, save.LocationId, save.SiteId, save.Sandbox)
            {
                World = world, Transport = transport, Tick = save.Tick, _nextPersonId = save.NextPersonId,
                _randomState = save.RandomState, Reputation = save.Reputation, CompletedTrips = save.CompletedTrips,
                PeakPopulation = save.PeakPopulation, _todayRevenue = save.TodayRevenue, _todayExpenses = save.TodayExpenses,
                _todayArrivals = save.TodayArrivals, _todayDepartures = save.TodayDepartures, _todayAbandoned = save.TodayAbandoned
            };
            session._notices.Clear();
            foreach (var op in save.Operations!)
            {
                Require(op != null && world.Rooms.Any(r => r.Id == op.RoomId) && !session._operations.ContainsKey(op.RoomId), "Unknown or duplicate room operation.");
                Require(op!.PriceMinor is >= 0 and <= 100_000_000 && op.Staff is >= 0 and <= 20 && op.Condition is >= 0 and <= 100 && op.Cleanliness is >= 0 and <= 100
                    && op.LastRentDay >= -1 && op.LastArrivalTick <= save.Tick && op.LastArrivalTick >= -86400 && op.CondoSaleMinor >= 0 && op.GrossRevenueMinor >= 0 && op.CostsMinor >= 0
                    && op.Film is >= 0 and <= 2 && op.NextEventTick >= 0, "Invalid operating state.");
                Require(!op.CondoSold || session.Room(op.RoomId)!.DefinitionId == "condo", "Ownership on a non-condominium.");
                session._operations.Add(op.RoomId, op);
            }
            Require(session._operations.Count == world.Rooms.Count, "The save does not contain exactly one operation per room.");
            foreach (var person in save.People!)
            {
                Require(person != null && person.Id > 0 && person.Id < save.NextPersonId && !session._people.ContainsKey(person.Id), "Invalid or duplicate person ID.");
                Require(session._operations.ContainsKey(person!.RoomId) && Enum.IsDefined(person.Activity) && person.CreatedAt >= 0 && person.CreatedAt <= save.Tick && person.ActionAt >= 0
                    && person.Satisfaction is >= 0 and <= 100, "Invalid person location or schedule.");
                Require(new[] { "Worker", "Resident", "Owner", "Guest", "Customer", "Driver", "Audience", "Attendee", "Tourist", "Staff" }.Contains(person.Role), "Unknown person role.");
                Require(person.ServiceTargetId == null || person.Role == "Staff" && session._operations.ContainsKey(person.ServiceTargetId.Value), "Invalid service assignment.");
                Require(transport.Journeys.Any(j => j.PersonId == person.Id), "A person has no recoverable journey state.");
                session._people.Add(person.Id, person);
            }
            Require(transport.Journeys.All(j => session._people.ContainsKey(j.PersonId)), "Orphaned transport passenger.");
            foreach (var room in world.Rooms)
            {
                var op = session._operations[room.Id]; var rule = rules.For(room.DefinitionId);
                Require(rule != null && session.ReservedCapacity(room.Id) <= rule.Capacity, "Room reservations exceed capacity.");
                Require(op.ReservationPersonId == null || session._people.TryGetValue(op.ReservationPersonId.Value, out var guest) && guest.RoomId == room.Id && guest.Role == "Guest", "Invalid hotel reservation.");
                if (room.DefinitionId == "dock") Require(locations.ValidateDock(save.LocationId, save.SiteId, world.Rank).Success && room.Floor == 0, "Invalid dock geography.");
                if (room.DefinitionId == "subway") Require(locations.ValidateSubway(save.LocationId, save.SiteId, world.Rank).Success && room.Floor < 0, "Invalid subway geography.");
                for (var f = room.Floor; f < room.Floor + catalog.Get(room.DefinitionId).Height; f++)
                    for (var x = room.X; x < room.X + catalog.Get(room.DefinitionId).Width; x++) Require(!transport.Occupies(x, f), "A room overlaps transport geometry.");
            }
            Require(save.Reports!.Length <= 90 && save.Notices!.Length <= 60, "Report history exceeds limits.");
            foreach (var report in save.Reports!)
            {
                Require(report != null && report.Day >= 1 && report.Day < session.Day && report.RevenueMinor >= 0 && report.ExpensesMinor >= 0
                    && report.Arrivals >= 0 && report.Departures >= 0 && report.Abandoned >= 0 && report.Satisfaction is >= 0 and <= 100, "Invalid daily report.");
                session._reports.Add(report!);
            }
            Require(session._reports.Select(r => r.Day).SequenceEqual(session._reports.Select(r => r.Day).Distinct().Order()), "Daily reports must be unique and ordered.");
            foreach (var notice in save.Notices!)
            {
                Require(notice != null && notice.Tick >= 0 && notice.Tick <= save.Tick && !string.IsNullOrWhiteSpace(notice.Text) && !string.IsNullOrWhiteSpace(notice.Kind), "Invalid notification.");
                session._notices.Add(notice!);
            }
            session.RestoreFinances(save.Finance, legacyFinance);
            session.RestoreManagementSnapshot(save.Management, legacyManagement, legacyOwnership, legacyRetail);
            return session;
        }
        catch (JsonException ex) { throw new SaveValidationException("Invalid session JSON: " + ex.Message); }
        catch (OverflowException) { throw new SaveValidationException("Saved financial totals exceed the supported monetary range."); }
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string error)
    { if (!value) throw new SaveValidationException(error); }
}
