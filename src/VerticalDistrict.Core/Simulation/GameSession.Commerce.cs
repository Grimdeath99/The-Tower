namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, FoodOrder> _foodOrders = new();
    private readonly SortedDictionary<long, HotelBooking> _hotelBookings = new();
    private readonly Dictionary<long, long> _foodByPerson = new();
    private readonly Dictionary<long, long> _bookingByPerson = new();
    private readonly SortedSet<long> _activeFoodOrders = new();
    private readonly SortedSet<long> _activeHotelBookings = new();
    private long _nextFoodOrderId = 1, _nextBookingId = 1;
    public IReadOnlyList<FoodOrder> FoodOrders => _foodOrders.Values.Where(order => OrderModel(order) == "Food").ToArray();
    public IReadOnlyList<HotelBooking> HotelBookings => _hotelBookings.Values.ToArray();
    public int FoodQueueCount(long roomId) => _foodOrders.Values.Count(o => o.RoomId == roomId && o.Status == FoodOrderStatus.Queued);
    public HotelBooking? HotelBookingFor(long roomId) => _hotelBookings.Values.LastOrDefault(b => b.RoomId == roomId
        && b.Status is HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn);
    private FoodOrder? FoodForPerson(long personId) => _foodByPerson.TryGetValue(personId, out var id) ? _foodOrders.GetValueOrDefault(id) : null;
    private HotelBooking? BookingForPerson(long personId) => _bookingByPerson.TryGetValue(personId, out var id) ? _hotelBookings.GetValueOrDefault(id) : null;
    public HotelReadiness HotelStateFor(long roomId)
    {
        var room = Room(roomId); var op = OperationFor(roomId);
        if (room == null || Rules.For(room.DefinitionId)?.Model != "Hotel" || op == null || !op.Open
            || op.Condition < 30 || !IsAccessible(roomId) || !HasUtilities(roomId)) return HotelReadiness.Unavailable;
        if (HotelBookingFor(roomId) is { } booking)
            return booking.Status == HotelBookingStatus.Reserved ? HotelReadiness.Reserved : HotelReadiness.Occupied;
        if (_people.Values.Any(p => p.RoomId == roomId && p.Role == "Guest" && p.Activity != PersonActivity.Leaving)) return HotelReadiness.Occupied;
        if (HotelClaimedWork(roomId) is { } work) return work.Kind == ServiceTaskKind.Maintenance ? HotelReadiness.Maintenance : HotelReadiness.Cleaning;
        if (op.Dirty || op.Cleanliness < 35) return HotelReadiness.Dirty;
        return HotelReadiness.Available;
    }
    private ServiceTask? HotelClaimedWork(long roomId) => ServiceTaskForRoom(roomId) is { Status: ServiceTaskStatus.Assigned or ServiceTaskStatus.Traveling or ServiceTaskStatus.InProgress } work ? work : null;
    private void CreateFoodOrder(RoomInstance room, long personId)
    {
        var id = _nextFoodOrderId; _nextFoodOrderId = checked(id + 1);
        var order = new FoodOrder(id, room.Id, personId, Tick, _operations[room.Id].PriceMinor,
            FoodOrderStatus.Traveling, 0, 0, 0, Tick + Rules.Management.FoodPatienceSeconds, 0,
            ProductForRoom(room.Id)!.Id, ProductServiceSeconds(room.Id));
        _foodOrders.Add(order.Id, order); _foodByPerson.Add(personId, order.Id); _activeFoodOrders.Add(order.Id);
    }
    private void CreateHotelBooking(RoomInstance room, long personId)
    {
        var id = _nextBookingId; _nextBookingId = checked(id + 1);
        var booking = new HotelBooking(id, room.Id, personId, Tick, _operations[room.Id].PriceMinor,
            Rules.For(room.DefinitionId)!.StayDays, Tick + Rules.Management.HotelArrivalTimeoutSeconds,
            0, 0, HotelBookingStatus.Reserved, 0, 0, "");
        _hotelBookings.Add(booking.Id, booking); _bookingByPerson.Add(personId, booking.Id); _activeHotelBookings.Add(booking.Id);
        SetOp(_operations[room.Id] with { ReservationPersonId = personId });
    }
    private void QueueFoodOrder(PersonState person)
    {
        if (FoodForPerson(person.Id) is not { Status: FoodOrderStatus.Traveling } order) { BeginLeave(person); return; }
        _foodOrders[order.Id] = order with { Status = FoodOrderStatus.Queued, QueuedAt = Tick, PatienceUntil = Tick + Rules.Management.FoodPatienceSeconds };
        _people[person.Id] = person with { Activity = PersonActivity.WaitingForService, ActionAt = Tick + Rules.Management.FoodPatienceSeconds,
            Satisfaction = DemandFor(person.RoomId).Satisfaction };
    }
    private bool CheckInHotel(PersonState person)
    {
        if (BookingForPerson(person.Id) is not { Status: HotelBookingStatus.Reserved } booking) return false;
        if (Tick > booking.CheckInDeadline || _operations[person.RoomId].Dirty || HotelClaimedWork(person.RoomId) != null)
        { CancelBooking(booking, "The assigned room was not ready before the check-in deadline."); return false; }
        var charge = checked(booking.AgreedNightlyPriceMinor * booking.Nights);
        if (!Post(person.RoomId, charge, "Sales.Hotel", $"Booking #{booking.Id}, guest #{person.Id}: {booking.Nights} nights at the agreed rate, paid on room arrival."))
        { CancelBooking(booking, "The check-in charge could not be completed."); return false; }
        var checkout = checked(((Tick + 28500) / 86400 + booking.Nights) * 86400 + 11 * 3600 - 28500);
        _hotelBookings[booking.Id] = booking with { Status = HotelBookingStatus.CheckedIn, CheckedInAt = Tick, CheckoutAt = checkout, ChargedMinor = charge };
        _activeHotelBookings.Remove(booking.Id);
        _people[person.Id] = person with { Activity = PersonActivity.Visiting, ActionAt = checkout, Satisfaction = DemandFor(person.RoomId).Satisfaction };
        RecordManagementOutcome(person.RoomId, true);
        return true;
    }
    private void CancelBooking(HotelBooking booking, string reason)
    {
        if (booking.Status != HotelBookingStatus.Reserved) return;
        _hotelBookings[booking.Id] = booking with { Status = HotelBookingStatus.Cancelled, FinishedAt = Tick, EndReason = reason };
        _activeHotelBookings.Remove(booking.Id);
        if (_operations.TryGetValue(booking.RoomId, out var op) && op.ReservationPersonId == booking.PersonId)
            SetOp(op with { ReservationPersonId = null });
        RecordManagementOutcome(booking.RoomId, false);
    }
    private void CheckOutHotel(PersonState person)
    {
        if (BookingForPerson(person.Id) is not { Status: HotelBookingStatus.CheckedIn } booking) return;
        _hotelBookings[booking.Id] = booking with { Status = HotelBookingStatus.CheckedOut, FinishedAt = Tick,
            EndReason = Tick >= booking.CheckoutAt ? "Scheduled check-out; prepaid charge retained." : "Stay ended early; prepaid charge retained." };
        if (_operations.TryGetValue(person.RoomId, out var op))
            SetOp(op with { Dirty = true, Cleanliness = Math.Min(op.Cleanliness, 20), ReservationPersonId = null });
    }
    private void AbandonFoodOrder(FoodOrder order)
    {
        if (order.Status is FoodOrderStatus.Completed or FoodOrderStatus.Abandoned) return;
        _foodOrders[order.Id] = order with { Status = FoodOrderStatus.Abandoned, FinishedAt = Tick };
        _activeFoodOrders.Remove(order.Id);
        RecordManagementOutcome(order.RoomId, false);
    }
    private void UpdateCommerce()
    {
        foreach (var booking in _activeHotelBookings.Select(id => _hotelBookings[id]).ToArray())
            if (Tick >= booking.CheckInDeadline || !_operations.TryGetValue(booking.RoomId, out var op) || !op.Open || op.Dirty || op.Condition < 30)
                CancelBooking(booking, "Check-in cancelled because its deadline passed or the room became unavailable. No charge was taken.");
        foreach (var roomGroup in _activeFoodOrders.Select(id => _foodOrders[id])
            .GroupBy(o => o.RoomId).ToArray())
        {
            var room = Room(roomGroup.Key); var op = OperationFor(roomGroup.Key); var rule = room == null ? null : Rules.For(room.DefinitionId);
            var ready = room != null && op != null && rule != null && Ready(room, op, rule);
            var serving = 0;
            foreach (var order in roomGroup.OrderBy(o => o.Id))
            {
                if (op == null || !op.Open || rule == null || Hour >= rule.CloseHour || Hour < rule.OpenHour
                    || (Tick >= order.PatienceUntil && order.Status != FoodOrderStatus.Serving))
                { AbandonFoodOrder(order); continue; }
                if (order.Status != FoodOrderStatus.Serving) continue;
                if (op.Staff < rule.Staff || serving++ >= op.Staff)
                {
                    // A dismissed service slot cannot keep working. Its unpurchased order rejoins
                    // the queue with its existing patience and quote; a future slot starts fresh work.
                    _foodOrders[order.Id] = order with { Status = FoodOrderStatus.Queued, ServiceStartedAt = 0, CompletesAt = 0 };
                    if (_people.TryGetValue(order.PersonId, out var queued)) _people[queued.Id] = queued with { Activity = PersonActivity.WaitingForService, ActionAt = order.PatienceUntil };
                    continue;
                }
                if (!ready)
                {
                    if (Tick >= order.PatienceUntil) AbandonFoodOrder(order);
                    else
                    {
                        _foodOrders[order.Id] = order with { CompletesAt = order.CompletesAt + 1 };
                        if (_people.TryGetValue(order.PersonId, out var waiting)) _people[waiting.Id] = waiting with { ActionAt = order.CompletesAt + 1 };
                    }
                    continue;
                }
                if (Tick < order.CompletesAt) continue;
                if (!Post(order.RoomId, order.AgreedPriceMinor, "Sales." + rule.Model,
                    $"Order #{order.Id}, customer #{order.PersonId}: {order.ProductId} {rule.Model.ToLowerInvariant()} service completed at the agreed price.")) continue;
                _foodOrders[order.Id] = order with { Status = FoodOrderStatus.Completed, FinishedAt = Tick };
                _activeFoodOrders.Remove(order.Id);
                if (_people.TryGetValue(order.PersonId, out var customer))
                    _people[customer.Id] = customer with { Activity = PersonActivity.Visiting, ActionAt = rule.Model == "Shop" ? Tick : Tick + rule.VisitSeconds,
                        Satisfaction = DemandFor(order.RoomId).Satisfaction };
                RecordManagementOutcome(order.RoomId, true);
            }
            if (!ready) continue;
            var slots = op!.Staff - roomGroup.Count(o => _foodOrders[o.Id].Status == FoodOrderStatus.Serving);
            foreach (var order in roomGroup.Select(o => _foodOrders[o.Id]).Where(o => o.Status == FoodOrderStatus.Queued)
                .OrderBy(o => o.QueuedAt).ThenBy(o => o.Id).Take(Math.Max(0, slots)).ToArray())
            {
                _foodOrders[order.Id] = order with { Status = FoodOrderStatus.Serving, ServiceStartedAt = Tick, CompletesAt = checked(Tick + order.AgreedServiceSeconds) };
                var person = _people[order.PersonId];
                _people[person.Id] = person with { Activity = PersonActivity.BeingServed, ActionAt = checked(Tick + order.AgreedServiceSeconds) };
            }
        }
        if (Tick % 60 == 0) TrimBusinessHistory();
    }
    private bool BusinessActorMustLeave(PersonState person)
        => TenantForPerson(person.Id)?.Status is TenancyStatus.Departing or TenancyStatus.Ended
            || FoodForPerson(person.Id)?.Status == FoodOrderStatus.Abandoned
            || BookingForPerson(person.Id)?.Status == HotelBookingStatus.Cancelled;
    private void OnBusinessActorRemoved(PersonState person, bool abandoned)
    {
        if (FoodForPerson(person.Id) is { } order) AbandonFoodOrder(order);
        if (BookingForPerson(person.Id) is { } booking) CancelBooking(booking, abandoned ? "The guest abandoned travel before check-in." : "The guest left before check-in.");
        if (TenantForPerson(person.Id) is { } tenant) FinishTenantDeparture(tenant.Id);
        TrimBusinessHistory();
    }
    private void CloseBusiness(long roomId)
    {
        if (TenantFor(roomId) is { } tenant) RequestTenantDeparture(tenant, "The manager closed this facility.");
        foreach (var order in _foodOrders.Values.Where(o => o.RoomId == roomId).ToArray()) AbandonFoodOrder(order);
        if (HotelBookingFor(roomId) is { Status: HotelBookingStatus.Reserved } booking) CancelBooking(booking, "The manager closed the room before check-in. No charge was taken.");
    }
    private string? BusinessDemolitionBlocker(long roomId)
        => TenantFor(roomId) != null ? "End the tenancy and wait for its members to leave before demolition."
        : HotelBookingFor(roomId) != null ? "Wait for the active hotel assignment to end before demolition." : null;
    private void RemoveBusinessRoom(long roomId)
    {
        _selectedProducts.Remove(roomId);
        foreach (var tenant in _tenants.Values.Where(t => t.RoomId == roomId).ToArray())
        { _tenants.Remove(tenant.Id); foreach (var id in tenant.MemberIds) _tenantMembers.Remove(id); }
        foreach (var order in _foodOrders.Values.Where(o => o.RoomId == roomId).ToArray()) { _foodOrders.Remove(order.Id); _foodByPerson.Remove(order.PersonId); _activeFoodOrders.Remove(order.Id); }
        foreach (var booking in _hotelBookings.Values.Where(b => b.RoomId == roomId).ToArray()) { _hotelBookings.Remove(booking.Id); _bookingByPerson.Remove(booking.PersonId); _activeHotelBookings.Remove(booking.Id); }
    }
    private void TrimBusinessHistory()
    {
        foreach (var order in _foodOrders.Values.Where(o => o.Status is FoodOrderStatus.Completed or FoodOrderStatus.Abandoned)
            .Where(o => !_people.ContainsKey(o.PersonId)).OrderByDescending(o => o.Id).Skip(Rules.Management.BusinessHistoryLimit).ToArray())
        { _foodOrders.Remove(order.Id); _foodByPerson.Remove(order.PersonId); }
        foreach (var booking in _hotelBookings.Values.Where(b => b.Status is HotelBookingStatus.CheckedOut or HotelBookingStatus.Cancelled)
            .Where(b => !_people.ContainsKey(b.PersonId)).OrderByDescending(b => b.Id).Skip(Rules.Management.BusinessHistoryLimit).ToArray())
        { _hotelBookings.Remove(booking.Id); _bookingByPerson.Remove(booking.PersonId); }
        foreach (var tenant in _tenants.Values.Where(t => t.Status == TenancyStatus.Ended
            && !_pendingBilling.Any(batch => batch.Obligations.Any(o => o.TenantId == t.Id))).OrderByDescending(t => t.Id).Skip(Rules.Management.BusinessHistoryLimit).ToArray())
        { _tenants.Remove(tenant.Id); foreach (var id in tenant.MemberIds) _tenantMembers.Remove(id); }
    }
}
