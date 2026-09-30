using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private void UpdateBusinesses()
    {
        foreach (var room in World.Rooms.OrderBy(r => r.Id))
        {
            var rule = Rules.For(room.DefinitionId);
            if (rule == null || !_operations.TryGetValue(room.Id, out var op)) continue;
            if (!Ready(room, op, rule)) continue;
            var count = ReservedCapacity(room.Id);
            switch (rule.Model)
            {
                case "Office":
                    if (Hour == 8 && count == 0 && Tick - op.LastArrivalTick >= 3600)
                        for (var i = 0; i < rule.Capacity; i++) Spawn(room, "Worker", (18 - Hour) * 3600 - Minute * 60);
                    if (Hour >= 9 && Occupancy(room.Id) > 0 && op.LastRentDay != Day)
                    {
                        if (Post(room.Id, op.PriceMinor, "Lease.Office", $"Day {Day}: occupied office lease."))
                            SetOp(_operations[room.Id] with { LastRentDay = Day, ContractActive = true });
                    }
                    break;
                case "Home": case "Condo":
                    if (Hour >= 18 && count < rule.Capacity && Tick - op.LastArrivalTick >= 3600 && !op.CondoSold)
                        for (var i = count; i < rule.Capacity; i++) Spawn(room, rule.Model == "Condo" ? "Owner" : "Resident", 18 * 3600);
                    break;
                case "Hotel":
                    if (!op.Dirty && op.ReservationPersonId == null && count == 0 && Tick - op.LastArrivalTick >= rule.ArrivalIntervalSeconds)
                        Spawn(room, "Guest", rule.StayDays * 86400);
                    break;
                case "Food": case "Shop": case "Parking":
                    if (count < rule.Capacity && Tick - op.LastArrivalTick >= rule.ArrivalIntervalSeconds)
                    {
                        var priceRatio = rule.PriceMinor == 0 ? 1m : (decimal)op.PriceMinor / rule.PriceMinor;
                        if (priceRatio <= 3 && (RandomValue() % 100) < Math.Clamp(120 - (int)(priceRatio * 30), 5, 95))
                            Spawn(room, rule.Model == "Parking" ? "Driver" : "Customer", rule.VisitSeconds);
                        else SetOp(op with { LastArrivalTick = Tick });
                    }
                    break;
                case "Cinema":
                    // Original screenings every two hours. Capacity is reserved during physical arrival.
                    if (Hour % 2 == 0 && Minute == 0 && Tick - op.LastArrivalTick >= 3600 && count == 0)
                        for (var i = 0; i < rule.Capacity; i++) Spawn(room, "Audience", rule.VisitSeconds);
                    break;
                case "Event":
                    if (op.EventPrepared && Tick >= op.NextEventTick && count == 0)
                    {
                        SetOp(op with { EventPrepared = false, LastArrivalTick = Tick });
                        for (var i = 0; i < rule.Capacity; i++) Spawn(room, "Attendee", rule.VisitSeconds);
                        Notice($"The event in room {room.Id} is admitting attendees.");
                    }
                    break;
                case "Terminal":
                    if (Tick - op.LastArrivalTick >= rule.ArrivalIntervalSeconds)
                    {
                        SetOp(op with { LastArrivalTick = Tick });
                        var targets = World.Rooms.Where(r => Rules.For(r.DefinitionId)?.Model is "Food" or "Shop" or "Cinema")
                            .Where(r => ReservedCapacity(r.Id) < Rules.For(r.DefinitionId)!.Capacity && OperatingWarning(r.Id) == "Operating").ToArray();
                        // Each batch is bounded by terminal capacity; the arrivals still use normal transport.
                        for (var i = 0; i < Math.Min(4, rule.Capacity); i++)
                            if (targets.Length > 0) Spawn(targets[i % targets.Length], "Tourist", 1200, room.Floor, room.X);
                    }
                    break;
            }
        }
    }
    private void Spawn(RoomInstance room, string role, long duration, int? sourceFloor = null, int? sourceX = null)
    {
        var rule = Rules.For(room.DefinitionId)!; var op = _operations[room.Id]; var entrance = Entrance;
        if (entrance == null || ReservedCapacity(room.Id) >= rule.Capacity || !Ready(room, op, rule)) return;
        // Reservation, person creation, and route ownership happen only after a valid route is accepted.
        var id = _nextPersonId;
        var result = Transport.RequestJourney(id, sourceFloor ?? entrance.Floor, sourceX ?? entrance.X, room.Floor, room.X, false, 600);
        if (!result.Success) { Transport.CancelJourney(id); Transport.ForgetJourney(id); return; }
        _nextPersonId = checked(id + 1);
        _people.Add(id, new PersonState(id, room.Id, role, PersonActivity.Arriving, duration, Tick, Reputation));
        _todayArrivals++;
        SetOp(op with { LastArrivalTick = Tick, ReservationPersonId = role == "Guest" ? id : op.ReservationPersonId });
    }
    private void ResolvePeople()
    {
        var journeys = Transport.Journeys.ToDictionary(j => j.PersonId);
        foreach (var person in _people.Values.ToArray())
        {
            if (person.Activity is PersonActivity.Visiting or PersonActivity.Working)
            {
                if (person.Activity == PersonActivity.Working && Tick >= person.ActionAt) CompleteService(person);
                else if (person.Activity == PersonActivity.Visiting && (Tick >= person.ActionAt || !_operations.TryGetValue(person.RoomId, out var roomOp) || !roomOp.Open)) BeginLeave(person);
                continue;
            }
            if (person.Activity == PersonActivity.Stranded)
            {
                if (Tick >= person.ActionAt)
                {
                    // Retry from the last safe physical floor. No off-screen teleport or revenue is allowed.
                    BeginLeave(person);
                }
                continue;
            }
            if (!journeys.TryGetValue(person.Id, out var journey)) continue;
            if (journey.State is JourneyState.Abandoned or JourneyState.Unreachable)
            {
                if (person.Activity == PersonActivity.Arriving && journey.Floor == (Entrance?.Floor ?? 0))
                { RemovePerson(person, true); continue; }
                Reputation = Math.Max(0, Reputation - 1);
                _people[person.Id] = person with { Activity = PersonActivity.Stranded, ActionAt = Tick + 120 };
                continue;
            }
            if (journey.State != JourneyState.Arrived) continue;
            CompletedTrips++;
            if (person.Activity == PersonActivity.Leaving) { RemovePerson(person, false); continue; }
            if (person.Activity == PersonActivity.Returning) { Transport.CancelJourney(person.Id); Transport.ForgetJourney(person.Id); _people.Remove(person.Id); continue; }
            if (person.Role == "Staff")
            {
                var depot = Room(person.RoomId);
                _people[person.Id] = person with { Activity = PersonActivity.Working, ActionAt = Tick + (depot == null ? 180 : Rules.For(depot.DefinitionId)!.ServiceSeconds) };
                continue;
            }
            EnterRoom(person);
        }
    }
    private void EnterRoom(PersonState person)
    {
        var room = Room(person.RoomId);
        if (room == null || !_operations.TryGetValue(room.Id, out var op)) { BeginLeave(person); return; }
        var rule = Rules.For(room.DefinitionId)!;
        if (!Ready(room, op, rule, false) || Occupancy(room.Id) >= rule.Capacity || rule.Model == "Hotel" && op.Dirty)
        { BeginLeave(person); return; }
        // Arriving workers store their remaining shift length at creation. Preserve that save
        // format while anchoring departure to the original clock, rather than arrival time.
        long? officeDeparture = rule.Model == "Office" ? checked(person.CreatedAt + person.ActionAt) : null;
        if (officeDeparture <= Tick) { BeginLeave(person); return; }
        var duration = person.ActionAt;
        var charge = rule.Model switch { "Food" or "Shop" or "Cinema" or "Event" => op.PriceMinor,
            "Hotel" => checked(op.PriceMinor * rule.StayDays), "Condo" when !op.CondoSold => op.PriceMinor, _ => 0 };
        if (charge > 0 && !Post(room.Id, charge, "Sales." + rule.Model, $"{person.Role} #{person.Id}: {rule.Model} admission/check-in.")) { BeginLeave(person); return; }
        op = _operations[room.Id];
        if (rule.Model == "Condo")
            op = op with { CondoSold = true, CondoSaleMinor = op.CondoSold ? op.CondoSaleMinor : charge, ContractActive = true };
        if (rule.Model == "Home") op = op with { ContractActive = true };
        if (rule.Model == "Hotel") duration = rule.StayDays * 86400 - (Hour - 11) * 3600 - Minute * 60;
        if (rule.Model == "Cinema") duration = Math.Max(1800, rule.VisitSeconds - op.Film * 1800);
        if (rule.Model == "Condo") duration = 30 * 86400;
        SetOp(op with { Cleanliness = Math.Max(0, op.Cleanliness - (rule.Model is "Food" or "Shop" ? 2 : 1)) });
        _people[person.Id] = person with { Activity = PersonActivity.Visiting, ActionAt = officeDeparture ?? Tick + Math.Max(60, duration), Satisfaction = Math.Clamp(Reputation + op.Cleanliness / 10 - 8, 0, 100) };
    }
    private void BeginLeave(PersonState person)
    {
        var room = Room(person.ServiceTargetId ?? person.RoomId);
        var journey = Transport.Journeys.FirstOrDefault(j => j.PersonId == person.Id);
        var floor = journey?.Floor ?? room?.Floor ?? 0;
        var x = (int)(journey?.X ?? room?.X ?? 0);
        if (journey?.State == JourneyState.Riding) return;
        var entrance = Entrance;
        if (entrance == null || !Transport.CanReach(floor, x, entrance.Floor, entrance.X, person.Role == "Staff"))
        {
            _people[person.Id] = person with { Activity = PersonActivity.Stranded, ActionAt = Tick + 120 };
            return;
        }
        if (!Transport.CancelJourney(person.Id).Success) return;
        var result = Transport.RequestJourney(person.Id, floor, x, entrance.Floor, entrance.X, person.Role == "Staff", 1200);
        if (!result.Success) { _people[person.Id] = person with { Activity = PersonActivity.Stranded, ActionAt = Tick + 120 }; return; }
        if (_operations.TryGetValue(person.RoomId, out var op))
        {
            if (person.Role == "Guest" && person.Activity == PersonActivity.Visiting)
                SetOp(op with { Dirty = true, Cleanliness = Math.Min(op.Cleanliness, 20), ReservationPersonId = null });
            if (person.Role == "Driver" && person.Activity == PersonActivity.Visiting)
                Post(person.RoomId, op.PriceMinor, "Parking.Departure", $"Parking stay completed by vehicle #{person.Id}.");
        }
        _people[person.Id] = person with { Activity = PersonActivity.Leaving, ActionAt = Tick };
    }
    private void RemovePerson(PersonState person, bool abandoned)
    {
        Transport.CancelJourney(person.Id);
        Transport.ForgetJourney(person.Id);
        _people.Remove(person.Id);
        _todayDepartures++;
        if (abandoned) { _todayAbandoned++; Reputation = Math.Max(0, Reputation - 1); }
        if (_operations.TryGetValue(person.RoomId, out var op))
        {
            if (op.ReservationPersonId == person.Id) SetOp(op with { ReservationPersonId = null });
            if (person.Role == "Resident" && ! _people.Values.Any(p => p.RoomId == person.RoomId && p.Role == "Resident"))
                SetOp(_operations[person.RoomId] with { ContractActive = false });
        }
    }
    private void AssignServiceWork(long? requestedRoom = null)
    {
        var claimed = _people.Values.Where(p => p.Role == "Staff").Select(p => p.ServiceTargetId).ToHashSet();
        foreach (var depot in World.Rooms.Where(r => r.DefinitionId == "service-room").OrderBy(r => r.Id))
        {
            var operation = _operations[depot.Id];
            if (!operation.Open || !IsAccessible(depot.Id, true)) continue;
            var active = _people.Values.Count(p => p.Role == "Staff" && p.RoomId == depot.Id);
            for (var worker = active; worker < operation.Staff; worker++)
            {
                var target = World.Rooms.Where(r => (!requestedRoom.HasValue || r.Id == requestedRoom.Value)
                    && !claimed.Contains(r.Id) && _operations.TryGetValue(r.Id, out var o)
                    && (o.Dirty || o.Cleanliness < 85 || o.Condition < 85)
                    && Transport.CanReach(depot.Floor, depot.X, r.Floor, r.X, true))
                    .OrderBy(r => _operations[r.Id].Condition + _operations[r.Id].Cleanliness).ThenBy(r => r.Id).FirstOrDefault();
                if (target == null) break;
                var id = _nextPersonId;
                var route = Transport.RequestJourney(id, depot.Floor, depot.X, target.Floor, target.X, true, 1200);
                if (!route.Success) { Transport.CancelJourney(id); Transport.ForgetJourney(id); break; }
                _nextPersonId++;
                _people.Add(id, new PersonState(id, depot.Id, "Staff", PersonActivity.Arriving, 0, Tick, 100, target.Id));
                claimed.Add(target.Id);
            }
        }
    }
    private void CompleteService(PersonState person)
    {
        var target = person.ServiceTargetId.HasValue ? Room(person.ServiceTargetId.Value) : null;
        var depot = Room(person.RoomId);
        if (target != null && _operations.TryGetValue(target.Id, out var op))
        {
            var repaired = op.Condition;
            if (op.Condition < 85 && World.CashMinor >= Rules.RepairCostMinor && Post(target.Id, -Rules.RepairCostMinor, "Maintenance.Repair", "Routed maintenance work completed.")) repaired = 100;
            SetOp(_operations[target.Id] with { Cleanliness = 100, Dirty = false, Condition = repaired });
        }
        if (target == null || depot == null) { BeginLeave(person); return; }
        Transport.CancelJourney(person.Id);
        var result = Transport.RequestJourney(person.Id, target.Floor, target.X, depot.Floor, depot.X, true, 1200);
        _people[person.Id] = person with { Activity = result.Success ? PersonActivity.Returning : PersonActivity.Stranded, ActionAt = Tick + 120 };
    }
    private void HourlyCondition()
    {
        foreach (var op in _operations.Values.ToArray())
        {
            var condition = Math.Max(0, op.Condition - 1);
            SetOp(op with { Condition = condition, Cleanliness = Math.Max(0, op.Cleanliness - (Occupancy(op.RoomId) > 0 ? 2 : 1)) });
            if (condition == 35) Notice($"Room {op.RoomId} needs maintenance before it reaches the closure threshold (30).", "Warning");
        }
    }
    private void CloseDay()
    {
        var captureDay = _billing.LastSettledDay + _pendingBilling.Count + 1;
        var captureTick = checked(FirstBillingTick + (captureDay - 1) * 86400);
        if (Tick >= captureTick) _pendingBilling.Add(CaptureBillingBatch(captureDay, captureTick));
        while (_pendingBilling.Count > 0)
        {
            if (!SettleBillingBatch(_pendingBilling[0])) return;
            _pendingBilling.RemoveAt(0);
        }
    }
    private bool SettleBillingBatch(PendingBillingBatch batch)
    {
        var closedDay = batch.Day;
        var postings = batch.Obligations.Select(o => new FinancialPosting(o.RoomId, o.AmountMinor, o.Category, o.Description)).ToArray();
        if (!PostBatch(postings)) return false;
        foreach (var obligation in batch.Obligations.Where(o => o.Category == "Lease.Home"))
        {
            var id = obligation.RoomId!.Value;
            SetOp(_operations[id] with { LastRentDay = closedDay + 1 });
        }
        var coverage = _operations.Count == 0 ? 100 : (int)(World.Rooms.Count(r => OperatingWarning(r.Id) is "Operating" or "Outside opening hours") * 100L / _operations.Count);
        var securityCapacity = World.Rooms.Where(r => r.DefinitionId == "security-room" && IsAccessible(r.Id)
            && _operations[r.Id].Open && _operations[r.Id].Staff > 0).Sum(r => Rules.For(r.DefinitionId)!.Capacity);
        var securityEffect = PeakPopulation > 16 ? (securityCapacity >= PeakPopulation ? 1 : -2) : 0;
        Reputation = Math.Clamp(Reputation + (coverage >= 80 && Cleanliness >= 80 ? 3 : -3) + securityEffect - Math.Min(10, _todayAbandoned), 0, 100);
        if (securityEffect < 0) Notice("Security coverage is below peak demand. Build/staff a security office to reduce complaints.", "Warning");
        _reports.Add(new DailyReport(closedDay, _todayRevenue, _todayExpenses, _todayArrivals, _todayDepartures, _todayAbandoned, Reputation));
        if (_reports.Count > 90) _reports.RemoveAt(0);
        Notice($"Day {closedDay} closed. Operating result {(_todayRevenue - _todayExpenses) / 100m:N0}; satisfaction {Reputation}%.", "Report");
        RecordFinancialClose(closedDay);
        _todayRevenue = _todayExpenses = 0; _todayArrivals = _todayDepartures = _todayAbandoned = 0;
        if (Insolvent) Notice("Cash is below the recovery threshold. Pause spending, close unprofitable businesses, or salvage unused rooms. Continued play remains available.", "Warning");
        return true;
    }
    public IReadOnlyList<string> UnmetRankRequirements()
    {
        if (World.Rank == 7) return Array.Empty<string>();
        var p = Rules.Promotions.Single(r => r.Rank == World.Rank + 1);
        var reasons = new List<string>();
        var diversity = World.Rooms.Where(r => _operations.TryGetValue(r.Id, out var op) && op.Open && IsAccessible(r.Id)).Select(r => Rules.For(r.DefinitionId)?.Model).Distinct().Count();
        var profit = _reports.LastOrDefault()?.ProfitMinor ?? 0;
        if (PeakPopulation < p.Population) reasons.Add($"Peak population {PeakPopulation}/{p.Population}");
        if (profit < p.DailyProfitMinor) reasons.Add($"Last daily profit {profit / 100m:N0}/{p.DailyProfitMinor / 100m:N0}");
        if (diversity < p.Diversity) reasons.Add($"Accessible facility types {diversity}/{p.Diversity}");
        if (Satisfaction < p.Satisfaction) reasons.Add($"Satisfaction {Satisfaction}/{p.Satisfaction}");
        if (Cleanliness < p.Cleanliness) reasons.Add($"Cleanliness {Cleanliness}/{p.Cleanliness}");
        var hotels = World.Rooms.Count(r => r.DefinitionId == "hotel-room" && IsAccessible(r.Id) && _operations[r.Id].Condition >= 80);
        if (hotels < p.HotelRooms) reasons.Add($"Quality hotel rooms {hotels}/{p.HotelRooms}");
        if (CompletedTrips < p.SuccessfulTrips) reasons.Add($"Completed journeys {CompletedTrips}/{p.SuccessfulTrips}");
        return reasons.AsReadOnly();
    }
    private void EvaluatePromotion()
    {
        if (World.Rank >= 7 || UnmetRankRequirements().Count != 0) return;
        World.PromoteRank(World.Rank + 1);
        Notice($"Rank {World.Rank}: {World.Catalog.GetRank(World.Rank).Name}. New above-ground cap: {World.Catalog.GetRank(World.Rank).AboveGroundFloorCap}.", "Promotion");
        if (World.Rank == 7) Notice("Seven-Star Destination reached. Keep improving and operating your district; play continues.", "Victory");
    }
}
