using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private void UpdateBusinesses()
    {
        UpdateOwnerships();
        UpdateTenancies();
        UpdateCustomerDemand();
        foreach (var room in World.Rooms.OrderBy(r => r.Id))
        {
            var rule = Rules.For(room.DefinitionId);
            if (rule == null || !_operations.TryGetValue(room.Id, out var op)) continue;
            if (!Ready(room, op, rule)) continue;
            var count = ReservedCapacity(room.Id);
            switch (rule.Model)
            {
                case "Hotel":
                    if (HotelStateFor(room.Id) == HotelReadiness.Available && count == 0 && Tick - op.LastArrivalTick >= rule.ArrivalIntervalSeconds)
                    {
                        if (RandomValue() % 100 < DemandFor(room.Id).Score) Spawn(room, "Guest", rule.StayDays * 86400);
                        else SetOp(op with { LastArrivalTick = Tick });
                    }
                    break;
                case "Parking":
                    if (count < rule.Capacity && Tick - op.LastArrivalTick >= rule.ArrivalIntervalSeconds)
                    {
                        if (RandomValue() % 100 < DemandFor(room.Id).Score)
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
                        // Each batch is bounded by terminal capacity; the arrivals still use normal transport.
                        for (var i = 0; i < Math.Min(4, rule.Capacity); i++)
                            if (ChooseCustomerDestination(World.Rooms.Where(r => Rules.For(r.DefinitionId)?.Model is "Food" or "Shop" or "Cinema")) is { } target)
                                Spawn(target, "Tourist", 1200, room.Floor, room.X);
                    }
                    break;
            }
        }
    }
    private long? Spawn(RoomInstance room, string role, long duration, int? sourceFloor = null, int? sourceX = null, long? memberId = null)
    {
        var rule = Rules.For(room.DefinitionId)!; var op = _operations[room.Id]; var entrance = Entrance;
        if (entrance == null || ReservedCapacity(room.Id) >= rule.Capacity || !Ready(room, op, rule)) return null;
        if ((!memberId.HasValue && _nextPersonId == long.MaxValue) || (rule.Model is "Food" or "Shop" && _nextFoodOrderId == long.MaxValue)
            || (rule.Model == "Hotel" && _nextBookingId == long.MaxValue)) return null;
        // Reservation, person creation, and route ownership happen only after a valid route is accepted.
        var id = memberId ?? _nextPersonId;
        if (_people.ContainsKey(id)) return null;
        var result = Transport.RequestJourney(id, sourceFloor ?? entrance.Floor, sourceX ?? entrance.X, room.Floor, room.X, false, 600);
        if (!result.Success) { Transport.CancelJourney(id); Transport.ForgetJourney(id); return null; }
        if (!memberId.HasValue) _nextPersonId = checked(id + 1);
        _people.Add(id, new PersonState(id, room.Id, role, PersonActivity.Arriving, duration, Tick, Reputation));
        _todayArrivals++;
        SetOp(op with { LastArrivalTick = Tick, ReservationPersonId = role == "Guest" ? id : op.ReservationPersonId });
        if (rule.Model is "Food" or "Shop") CreateFoodOrder(room, id);
        if (rule.Model == "Hotel" && role == "Guest") CreateHotelBooking(room, id);
        return id;
    }
    private void ResolvePeople()
    {
        var journeys = Transport.Journeys.ToDictionary(j => j.PersonId);
        foreach (var person in _people.Values.ToArray())
        {
            if (person.Role == "Staff") continue;
            if (person.Activity != PersonActivity.Leaving && (BusinessActorMustLeave(person) || OwnershipActorMustLeave(person))) { BeginLeave(person); continue; }
            if (person.Activity is PersonActivity.WaitingForService or PersonActivity.BeingServed) continue;
            if (person.Activity is PersonActivity.Visiting or PersonActivity.Working)
            {
                if (person.Activity == PersonActivity.Visiting && (Tick >= person.ActionAt || !_operations.TryGetValue(person.RoomId, out var roomOp) || !roomOp.Open)) BeginLeave(person);
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
                _people[person.Id] = person with { Activity = PersonActivity.Stranded, ActionAt = Tick + 120 };
                continue;
            }
            if (journey.State != JourneyState.Arrived) continue;
            CompletedTrips++;
            if (person.Activity == PersonActivity.Leaving) { RemovePerson(person, false); continue; }
            if (person.Activity == PersonActivity.Returning) { Transport.CancelJourney(person.Id); Transport.ForgetJourney(person.Id); _people.Remove(person.Id); continue; }
            EnterRoom(person);
        }
    }
    private void EnterRoom(PersonState person)
    {
        var room = Room(person.RoomId);
        if (room == null || !_operations.TryGetValue(room.Id, out var op)) { BeginLeave(person); return; }
        var rule = Rules.For(room.DefinitionId)!;
        RecordTravelExperience(person);
        if (!Ready(room, op, rule, false) || Occupancy(room.Id) >= rule.Capacity || rule.Model == "Hotel" && op.Dirty)
        { BeginLeave(person); return; }
        // Arriving workers store their remaining shift length at creation. Preserve that save
        // format while anchoring departure to the original clock, rather than arrival time.
        long? officeDeparture = rule.Model == "Office" ? checked(person.CreatedAt + person.ActionAt) : null;
        if (officeDeparture <= Tick) { BeginLeave(person); return; }
        RecordTenantArrival(person);
        if (rule.Model is "Food" or "Shop") { SetOp(op with { Cleanliness = Math.Max(0, op.Cleanliness - 2) }); QueueFoodOrder(person); return; }
        if (rule.Model == "Hotel") { if (!CheckInHotel(person)) BeginLeave(person); return; }
        if (rule.Model == "Condo") { if (!EnterOwnedResidence(person)) BeginLeave(person); return; }
        var duration = person.ActionAt;
        var charge = rule.Model switch { "Cinema" or "Event" => op.PriceMinor, _ => 0 };
        if (charge > 0 && !Post(room.Id, charge, "Sales." + rule.Model, $"{person.Role} #{person.Id}: {rule.Model} admission/check-in.")) { BeginLeave(person); return; }
        op = _operations[room.Id];
        if (rule.Model == "Cinema") duration = Math.Max(1800, rule.VisitSeconds - op.Film * 1800);
        SetOp(op with { Cleanliness = Math.Max(0, op.Cleanliness - (rule.Model is "Food" or "Shop" ? 2 : 1)) });
        _people[person.Id] = person with { Activity = PersonActivity.Visiting, ActionAt = officeDeparture ?? Tick + Math.Max(60, duration), Satisfaction = DemandFor(room.Id).Satisfaction };
    }
    private void BeginLeave(PersonState person)
    {
        var room = Room(person.ServiceTargetId ?? person.RoomId);
        var journey = Transport.Journeys.FirstOrDefault(j => j.PersonId == person.Id);
        var floor = journey?.Floor ?? room?.Floor ?? 0;
        var x = (int)(journey?.X ?? room?.X ?? 0);
        if (journey?.State == JourneyState.Riding) return;
        if (FoodForPerson(person.Id) is { } order) AbandonFoodOrder(order);
        if (BookingForPerson(person.Id) is { Status: HotelBookingStatus.Reserved } reservation) CancelBooking(reservation, "The guest left before check-in. No charge was taken.");
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
            if (person.Role == "Guest") CheckOutHotel(person);
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
        if (abandoned) _todayAbandoned++;
        OnBusinessActorRemoved(person, abandoned);
        OnOwnershipActorRemoved(person);
        if (_operations.TryGetValue(person.RoomId, out var op))
        {
            if (op.ReservationPersonId == person.Id) SetOp(op with { ReservationPersonId = null });
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
            RecordHomeRentPaid(obligation, closedDay);
            SetOp(_operations[id] with { LastRentDay = Math.Max(_operations[id].LastRentDay, closedDay + 1) });
        }
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
