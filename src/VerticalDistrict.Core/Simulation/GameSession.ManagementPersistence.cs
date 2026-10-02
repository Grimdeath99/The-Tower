using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed record BusinessSnapshot(long NextTenantId, long NextFoodOrderId, long NextBookingId,
    TenantContract[] Tenants, FoodOrder[] FoodOrders, HotelBooking[] HotelBookings);
public sealed record ServiceSnapshot(long NextTaskId, ServiceTask[] Tasks);
public sealed record ManagementSnapshot(BusinessSnapshot Businesses, ServiceSnapshot Services, SatisfactionSnapshot Satisfaction,
    OwnershipSnapshot Ownership, RetailSnapshot Retail);

public sealed partial class GameSession
{
    private static ManagementSnapshot EmptyManagementSnapshot() => new(new(1, 1, 1, [], [], []),
        new(1, []), new(1, [], [], []), new(1, 0, []), EmptyRetailSnapshot());

    private static string LegacyRulesFingerprint(SimulationRules rules, bool removeManagement)
    {
        var legacy = JsonSerializer.SerializeToNode(rules, SimulationRules.JsonOptions)!.AsObject();
        legacy.Remove("products");
        if (removeManagement) legacy.Remove("management");
        return Fingerprint(legacy);
    }

    private ManagementSnapshot CaptureManagementSnapshot() => new(
        new BusinessSnapshot(_nextTenantId, _nextFoodOrderId, _nextBookingId,
            _tenants.Values.Select(tenant => tenant with { MemberIds = tenant.MemberIds.ToArray() }).ToArray(),
            _foodOrders.Values.ToArray(), _hotelBookings.Values.ToArray()),
        new ServiceSnapshot(_nextServiceTaskId, _serviceTasks.Values.ToArray()), CaptureSatisfaction(), CaptureOwnershipSnapshot(), CaptureRetailSnapshot());

    private void RestoreManagementSnapshot(ManagementSnapshot? state, bool legacy, bool legacyOwnership, bool legacyRetail)
    {
        if (!legacyRetail)
        {
            Require(state?.Retail != null, "Missing authoritative retail state.");
            _legacyRetailThroughOrderId = state.Retail.LegacyThroughOrderId;
        }
        if (legacy)
        {
            MigrateLegacyBusinesses();
            MigrateLegacyServices();
            SynchronizeSatisfaction();
        }
        else
        {
            Require(state != null && state.Businesses != null && state.Services != null && state.Satisfaction != null,
                "Missing authoritative management state.");
            RestoreBusinessSnapshot(state.Businesses, legacyRetail);
            RestoreServiceSnapshot(state.Services);
            RestoreSatisfaction(state.Satisfaction);
        }
        if (legacyOwnership) MigrateLegacyOwnerships();
        else RestoreOwnershipSnapshot(state!.Ownership);
        if (legacyRetail) MigrateLegacyRetail();
        else RestoreRetailSnapshot(state!.Retail);
        ValidateManagementRelationships();
    }

    private void RestoreServiceSnapshot(ServiceSnapshot saved)
    {
        Require(saved.NextTaskId > 0 && saved.Tasks != null, "Missing service task state.");
        var unresolvedRooms = new HashSet<long>();
        var workers = new HashSet<long>();
        var historicRooms = World.Ledger.Where(entry => entry.Category == "Construction.Room" && entry.EntityId.HasValue)
            .Select(entry => entry.EntityId!.Value).ToHashSet();
        foreach (var task in saved.Tasks)
        {
            Require(task != null && task.Id > 0 && task.Id < saved.NextTaskId && !_serviceTasks.ContainsKey(task.Id)
                && Enum.IsDefined(task.Kind) && Enum.IsDefined(task.Status) && historicRooms.Contains(task.RoomId)
                && task.CreatedTick >= 0 && task.CreatedTick <= task.UpdatedTick && task.UpdatedTick <= Tick
                && !string.IsNullOrWhiteSpace(task.Reason) && task.Reason.Length <= 500, "Invalid service task identity, timing, or reason.");
            Require(task.WorkerPersonId == null || task.WorkerPersonId > 0 && task.WorkerPersonId < _nextPersonId,
                "Invalid service worker reference.");
            Require(task.DepotRoomId == null || historicRooms.Contains(task.DepotRoomId.Value), "Invalid service depot reference.");
            Require(task.WorkDueTick == null || task.WorkDueTick >= task.CreatedTick && task.WorkDueTick <= checked(Tick + 3600),
                "Invalid service work deadline.");
            if (ServiceTaskIsTerminal(task))
                Require(task.FinishedTick != null && task.FinishedTick >= task.CreatedTick
                    && task.FinishedTick == task.UpdatedTick, "A completed or cancelled service task has no valid finish time.");
            else
            {
                Require(_operations.ContainsKey(task.RoomId) && unresolvedRooms.Add(task.RoomId) && task.FinishedTick == null,
                    "Missing serviced room or duplicate unresolved service task.");
                if (ServiceTaskIsClaimed(task))
                {
                    Require(task.WorkerPersonId != null && workers.Add(task.WorkerPersonId.Value)
                        && _people.TryGetValue(task.WorkerPersonId.Value, out var person) && person.Role == "Staff"
                        && person.RoomId == task.DepotRoomId && person.ServiceTargetId == task.RoomId,
                        "A claimed service task has no unique matching worker.");
                    var worker = _people[task.WorkerPersonId.Value];
                    Require(task.DepotRoomId.HasValue && Room(task.DepotRoomId.Value) is { } depot
                        && Rules.For(depot.DefinitionId)?.Model == "Service", "A claimed task has no live service depot.");
                    var journey = Transport.JourneyFor(worker.Id);
                    var target = Room(task.RoomId)!;
                    Require(journey != null && journey.Service && journey.DestinationFloor == target.Floor
                        && journey.DestinationX == target.X, "A service worker's journey does not lead to the assigned room.");
                    Require(task.Status == ServiceTaskStatus.InProgress
                            ? worker.Activity == PersonActivity.Working && task.WorkDueTick == worker.ActionAt
                                && task.WorkDueTick >= Tick && journey.State == JourneyState.Arrived
                            : worker.Activity == PersonActivity.Arriving && task.WorkDueTick == null,
                        "Service work state, worker activity, and deadline disagree.");
                }
                else Require(task.WorkerPersonId == null && task.DepotRoomId == null && task.WorkDueTick == null,
                    "An unclaimed task retains worker ownership or a work deadline.");
            }
            _serviceTasks.Add(task.Id, task);
        }
        Require(_serviceTasks.Values.Count(ServiceTaskIsTerminal) <= Rules.Management.ServiceHistoryLimit,
            "Service history exceeds its configured limit.");
        _nextServiceTaskId = saved.NextTaskId;
    }

    private void RestoreBusinessSnapshot(BusinessSnapshot saved, bool legacyRetail = false)
    {
        Require(saved.NextTenantId > 0 && saved.NextFoodOrderId > 0 && saved.NextBookingId > 0
            && saved.Tenants != null && saved.FoodOrders != null && saved.HotelBookings != null, "Missing business management state.");
        var activeRooms = new HashSet<long>();
        foreach (var tenant in saved.Tenants)
        {
            Require(tenant != null && tenant.Id > 0 && tenant.Id < saved.NextTenantId && !_tenants.ContainsKey(tenant.Id)
                && Room(tenant.RoomId) is { } room && Rules.For(room.DefinitionId)?.Model == tenant.Kind
                && tenant.Kind is "Office" or "Home" && Enum.IsDefined(tenant.Status), "Invalid tenant identity, room, or status.");
            var rule = Rules.For(Room(tenant.RoomId)!.DefinitionId)!;
            Require(tenant.StartDay >= 1 && tenant.StartDay <= Day && tenant.RenewalDay > tenant.StartDay
                && tenant.RenewalDay <= Day + Rules.Management.LeaseDays && tenant.AgreedRentMinor is >= 0 and <= 100_000_000
                && tenant.MemberIds != null && tenant.MemberIds.Length is > 0 && tenant.MemberIds.Length <= rule.Capacity
                && tenant.LastArrivalDay >= 0 && tenant.LastArrivalDay <= Day && tenant.LastPaidDay >= 0 && tenant.LastPaidDay <= Day
                && tenant.LastOccupiedDay >= 0 && tenant.LastOccupiedDay <= Day && tenant.LastReviewDay >= tenant.StartDay
                && tenant.LastReviewDay <= Day && tenant.BadDays is >= 0 and <= 30
                && tenant.DepartureReason != null && tenant.DepartureReason.Length <= 500,
                "Invalid tenant capacity, rent, or contract schedule.");
            Require(tenant.Status == TenancyStatus.Ended
                    ? tenant.EndedAtTick >= 0 && tenant.EndedAtTick <= Tick && tenant.MemberIds.All(id => !_people.ContainsKey(id))
                    : tenant.EndedAtTick == 0 && activeRooms.Add(tenant.RoomId), "Duplicate live tenancy or invalid departure state.");
            foreach (var member in tenant.MemberIds)
            {
                Require(member > 0 && member < _nextPersonId && _tenantMembers.TryAdd(member, tenant.Id), "Tenant members reuse an ID or exceed the person counter.");
                if (_people.TryGetValue(member, out var person))
                    Require(person.RoomId == tenant.RoomId && person.Role == (tenant.Kind == "Office" ? "Worker" : "Resident"),
                        "A tenant member is assigned to a different room or population category.");
            }
            _tenants.Add(tenant.Id, tenant with { MemberIds = tenant.MemberIds.ToArray() });
        }
        foreach (var original in saved.FoodOrders)
        {
            Require(original != null, "Missing purchase order.");
            var order = legacyRetail ? UpgradeLegacyOrder(original) : original;
            Require(order.Id > 0 && order.Id < saved.NextFoodOrderId && !_foodOrders.ContainsKey(order.Id)
                && Room(order.RoomId) is { } room && Rules.For(room.DefinitionId)?.Model is "Food" or "Shop"
                && order.PersonId > 0 && order.PersonId < _nextPersonId && !_foodByPerson.ContainsKey(order.PersonId)
                && !_tenantMembers.ContainsKey(order.PersonId) && Enum.IsDefined(order.Status)
                && order.AgreedPriceMinor is >= 0 and <= 100_000_000 && order.CreatedAt >= 0 && order.CreatedAt <= Tick
                && order.QueuedAt >= 0 && order.QueuedAt <= Tick && order.ServiceStartedAt >= 0 && order.ServiceStartedAt <= Tick
                && order.CompletesAt >= 0 && order.CompletesAt <= Tick + 18000
                && order.PatienceUntil >= order.CreatedAt && order.PatienceUntil <= Tick + Rules.Management.FoodPatienceSeconds,
                "Invalid food order identity, price, or schedule.");
            ValidateOrderProduct(order, legacyRetail || order.Id <= _legacyRetailThroughOrderId);
            var terminal = order.Status is FoodOrderStatus.Completed or FoodOrderStatus.Abandoned;
            Require(terminal ? order.FinishedAt >= order.CreatedAt && order.FinishedAt <= Tick : order.FinishedAt == 0,
                "Food order completion time disagrees with its status.");
            _people.TryGetValue(order.PersonId, out var person);
            Require(person == null ? terminal : person.RoomId == order.RoomId && person.Role is "Customer" or "Tourist",
                "Food order references the wrong customer or has lost an active customer.");
            var saleCategory = "Sales." + Rules.For(Room(order.RoomId)!.DefinitionId)!.Model;
            if (order.Status != FoodOrderStatus.Completed)
                Require(BusinessSale(order.RoomId, order.PersonId, saleCategory) == null,
                    "An unpaid or abandoned food order already has a ledger receipt.");
            switch (order.Status)
            {
                case FoodOrderStatus.Traveling:
                    Require(person?.Activity == PersonActivity.Arriving && order.QueuedAt == 0 && order.ServiceStartedAt == 0 && order.CompletesAt == 0,
                        "Traveling food order has already started service."); break;
                case FoodOrderStatus.Queued:
                    Require(person?.Activity == PersonActivity.WaitingForService && person.ActionAt == order.PatienceUntil
                        && order.QueuedAt >= order.CreatedAt && order.ServiceStartedAt == 0 && order.CompletesAt == 0,
                        "Food queue state disagrees with its customer."); break;
                case FoodOrderStatus.Serving:
                    Require(person?.Activity == PersonActivity.BeingServed && person.ActionAt == order.CompletesAt
                        && order.ServiceStartedAt >= order.QueuedAt && order.QueuedAt >= order.CreatedAt
                        && order.CompletesAt >= order.ServiceStartedAt, "Food service state disagrees with its customer or deadline."); break;
                case FoodOrderStatus.Completed:
                    Require(order.FinishedAt >= order.ServiceStartedAt && order.FinishedAt >= order.CompletesAt,
                        "Food purchase finished before service.");
                    RequireSale(order.RoomId, order.PersonId, saleCategory, order.AgreedPriceMinor, order.FinishedAt);
                    if (!legacyRetail && order.Id > _legacyRetailThroughOrderId)
                    {
                        var receipt = BusinessSale(order.RoomId, order.PersonId, saleCategory);
                        Require(receipt != null && receipt.Description.StartsWith($"Order #{order.Id}, customer #{order.PersonId}: {order.ProductId} ", StringComparison.Ordinal),
                            "A completed purchase has lost its order and product receipt identity.");
                    }
                    break;
            }
            _foodOrders.Add(order.Id, order); _foodByPerson.Add(order.PersonId, order.Id);
            if (!terminal) _activeFoodOrders.Add(order.Id);
        }
        var assignedHotels = new HashSet<long>();
        foreach (var booking in saved.HotelBookings)
        {
            Require(booking != null && booking.Id > 0 && booking.Id < saved.NextBookingId && !_hotelBookings.ContainsKey(booking.Id)
                && Room(booking.RoomId) is { } room && Rules.For(room.DefinitionId)?.Model == "Hotel"
                && booking.PersonId > 0 && booking.PersonId < _nextPersonId && !_bookingByPerson.ContainsKey(booking.PersonId)
                && !_tenantMembers.ContainsKey(booking.PersonId) && !_foodByPerson.ContainsKey(booking.PersonId)
                && Enum.IsDefined(booking.Status) && booking.AgreedNightlyPriceMinor is >= 0 and <= 100_000_000
                && booking.Nights is >= 1 and <= 30 && booking.ReservedAt >= 0 && booking.ReservedAt <= Tick
                && booking.CheckInDeadline >= booking.ReservedAt && booking.CheckInDeadline <= Tick + Rules.Management.HotelArrivalTimeoutSeconds
                && booking.EndReason != null && booking.EndReason.Length <= 500, "Invalid hotel booking identity, price, or reservation.");
            var active = booking.Status is HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn;
            _people.TryGetValue(booking.PersonId, out var guest);
            Require(guest == null ? !active : guest.Role == "Guest" && guest.RoomId == booking.RoomId,
                "Hotel booking has lost its active guest or references another room.");
            Require(active ? assignedHotels.Add(booking.RoomId) && booking.FinishedAt == 0 && booking.EndReason.Length == 0
                    && _operations[booking.RoomId].ReservationPersonId == booking.PersonId
                : booking.FinishedAt >= booking.ReservedAt && booking.FinishedAt <= Tick && booking.EndReason.Length > 0,
                "Overlapping hotel assignments or invalid completed stay.");
            if (booking.Status is HotelBookingStatus.Reserved or HotelBookingStatus.Cancelled)
            {
                Require(booking.CheckedInAt == 0 && booking.CheckoutAt == 0 && booking.ChargedMinor == 0
                    && (booking.Status != HotelBookingStatus.Reserved || guest!.Activity is PersonActivity.Arriving or PersonActivity.Stranded),
                    "An unchecked-in guest has a paid stay or an invalid activity.");
                Require(BusinessSale(booking.RoomId, booking.PersonId, "Sales.Hotel") == null,
                    "An unpaid hotel booking already has a ledger receipt.");
            }
            else
            {
                Require(booking.CheckedInAt >= booking.ReservedAt && booking.CheckedInAt <= Tick
                    && booking.CheckoutAt > booking.CheckedInAt && booking.CheckoutAt <= Tick + 31 * 86400L
                    && booking.ChargedMinor == checked(booking.AgreedNightlyPriceMinor * booking.Nights),
                    "Hotel stay dates or frozen charge do not reconcile.");
                if (booking.Status == HotelBookingStatus.CheckedIn)
                    Require(guest!.Activity is PersonActivity.Visiting or PersonActivity.Stranded
                        && (guest.Activity != PersonActivity.Visiting || guest.ActionAt == booking.CheckoutAt),
                        "Checked-in guest and checkout schedule disagree.");
                RequireSale(booking.RoomId, booking.PersonId, "Sales.Hotel", booking.ChargedMinor, booking.CheckedInAt);
            }
            _hotelBookings.Add(booking.Id, booking); _bookingByPerson.Add(booking.PersonId, booking.Id);
            if (booking.Status == HotelBookingStatus.Reserved) _activeHotelBookings.Add(booking.Id);
        }
        var pendingTenants = _pendingBilling.SelectMany(batch => batch.Obligations).Where(o => o.TenantId.HasValue).Select(o => o.TenantId!.Value).ToHashSet();
        Require(_tenants.Values.Count(t => t.Status == TenancyStatus.Ended && !pendingTenants.Contains(t.Id)) <= Rules.Management.BusinessHistoryLimit
            && _foodOrders.Values.Count(o => o.Status is FoodOrderStatus.Completed or FoodOrderStatus.Abandoned && !_people.ContainsKey(o.PersonId)) <= Rules.Management.BusinessHistoryLimit
            && _hotelBookings.Values.Count(b => b.Status is HotelBookingStatus.CheckedOut or HotelBookingStatus.Cancelled && !_people.ContainsKey(b.PersonId)) <= Rules.Management.BusinessHistoryLimit,
            "Completed business history exceeds its configured limit.");
        _nextTenantId = saved.NextTenantId; _nextFoodOrderId = saved.NextFoodOrderId; _nextBookingId = saved.NextBookingId;
    }

    private LedgerEntry? BusinessSale(long roomId, long personId, string category)
    {
        var matches = World.Ledger.Where(entry => entry.EntityId == roomId && entry.Category == category
            && (entry.Description.Contains($"customer #{personId}:", StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains($"tourist #{personId}:", StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains($"guest #{personId}:", StringComparison.OrdinalIgnoreCase))).ToArray();
        Require(matches.Length <= 1, "A customer or guest was charged more than once.");
        return matches.SingleOrDefault();
    }

    private void RequireSale(long roomId, long personId, string category, long amount, long timestamp)
    {
        var sale = BusinessSale(roomId, personId, category);
        Require(sale == null ? amount == 0 : sale.AmountMinor == amount && sale.TimestampTicks == timestamp,
            "The frozen completed purchase or stay does not match its ledger receipt.");
    }

    private void MigrateLegacyBusinesses()
    {
        var pendingHomes = _pendingBilling.SelectMany(batch => batch.Obligations).Where(o => o.Category == "Lease.Home")
            .Select(o => o.RoomId!.Value).ToHashSet();
        foreach (var room in World.Rooms.OrderBy(room => room.Id))
        {
            var rule = Rules.For(room.DefinitionId)!; var op = _operations[room.Id];
            if (rule.Model is not ("Office" or "Home")) continue;
            var people = _people.Values.Where(person => person.RoomId == room.Id && person.Role == (rule.Model == "Office" ? "Worker" : "Resident"))
                .OrderBy(person => person.Activity is PersonActivity.Leaving or PersonActivity.Stranded ? 1 : 0).ThenBy(person => person.Id).ToArray();
            if (!op.ContractActive && people.Length == 0 && !pendingHomes.Contains(room.Id)) continue;
            var memberIds = people.Take(rule.Capacity).Select(person => person.Id).ToList();
            while (memberIds.Count < rule.Capacity) { memberIds.Add(_nextPersonId); _nextPersonId = checked(_nextPersonId + 1); }
            var paidDay = Math.Max(0, op.LastRentDay - (rule.Model == "Home" ? 1 : 0));
            var occupiedDay = op.ContractActive || people.Any(person => person.Activity == PersonActivity.Visiting) ? Math.Max(1, paidDay == 0 ? Day : paidDay) : 0;
            var ended = !op.ContractActive && people.Length == 0;
            var tenant = new TenantContract(_nextTenantId++, room.Id, rule.Model, Day, checked(Day + Rules.Management.LeaseDays),
                op.PriceMinor, memberIds.ToArray(), people.Length > 0 || rule.Model == "Office" && paidDay == Day ? Day : 0,
                paidDay, occupiedDay, Day, 0, ended ? TenancyStatus.Ended : TenancyStatus.Active,
                ended ? "Imported a former contract with an outstanding bill." : "", ended ? Tick : 0);
            _tenants.Add(tenant.Id, tenant);
            SetOp(op with { ContractActive = !ended });
        }
        for (var index = 0; index < _pendingBilling.Count; index++)
            _pendingBilling[index] = _pendingBilling[index] with { Obligations = _pendingBilling[index].Obligations.Select(obligation =>
                obligation.Category == "Lease.Home" ? obligation with { TenantId = _tenants.Values.Last(t => t.RoomId == obligation.RoomId).Id } : obligation).ToArray() };
        foreach (var person in _people.Values.OrderBy(person => person.Id))
        {
            var rule = Rules.For(Room(person.RoomId)!.DefinitionId)!;
            if (rule.Model == "Food" && person.Role is "Customer" or "Tourist")
            {
                var sale = BusinessSale(person.RoomId, person.Id, "Sales.Food");
                var completed = sale != null || person.Activity == PersonActivity.Visiting;
                var entered = sale?.TimestampTicks ?? Math.Clamp(person.ActionAt - rule.VisitSeconds, person.CreatedAt, Tick);
                var status = completed ? FoodOrderStatus.Completed : person.Activity == PersonActivity.Arriving ? FoodOrderStatus.Traveling : FoodOrderStatus.Abandoned;
                var order = new FoodOrder(_nextFoodOrderId++, person.RoomId, person.Id, person.CreatedAt,
                    completed ? sale?.AmountMinor ?? 0 : _operations[person.RoomId].PriceMinor, status,
                    completed ? entered : 0, completed ? entered : 0, completed ? entered : 0,
                    checked((completed ? entered : person.CreatedAt) + Rules.Management.FoodPatienceSeconds), completed ? entered : status == FoodOrderStatus.Abandoned ? Tick : 0);
                _foodOrders.Add(order.Id, order);
            }
            if (person.Role == "Guest")
            {
                var sale = BusinessSale(person.RoomId, person.Id, "Sales.Hotel");
                var op = _operations[person.RoomId];
                var paid = sale != null || person.Activity == PersonActivity.Visiting;
                var reserved = !paid && person.Activity is PersonActivity.Arriving or PersonActivity.Stranded && op.ReservationPersonId == person.Id;
                var checkedIn = paid && op.ReservationPersonId == person.Id && person.Activity != PersonActivity.Leaving;
                var status = checkedIn ? HotelBookingStatus.CheckedIn : paid ? HotelBookingStatus.CheckedOut
                    : reserved ? HotelBookingStatus.Reserved : HotelBookingStatus.Cancelled;
                var entryTick = sale?.TimestampTicks ?? Math.Min(Tick, person.CreatedAt);
                var amount = sale?.AmountMinor ?? 0;
                Require(amount % rule.StayDays == 0, "The imported hotel receipt cannot be divided into its original nightly rate.");
                var checkout = !paid ? 0 : person.Activity == PersonActivity.Visiting ? person.ActionAt
                    // Legacy checkout subtracted whole hours/minutes, so retained the arrival second.
                    : checked(((entryTick + 28500) / 86400 + rule.StayDays) * 86400 + 39600 - 28500 + entryTick % 60);
                var finished = status is HotelBookingStatus.CheckedOut or HotelBookingStatus.Cancelled ? Math.Min(Tick, Math.Max(person.CreatedAt, person.ActionAt)) : 0;
                var booking = new HotelBooking(_nextBookingId++, person.RoomId, person.Id, person.CreatedAt,
                    paid ? amount / rule.StayDays : op.PriceMinor, rule.StayDays,
                    checked(person.CreatedAt + Rules.Management.HotelArrivalTimeoutSeconds), paid ? entryTick : 0, checkout,
                    status, amount, finished, status is HotelBookingStatus.CheckedOut or HotelBookingStatus.Cancelled ? "Imported an already ended hotel assignment." : "");
                _hotelBookings.Add(booking.Id, booking);
            }
        }
        var snapshot = new BusinessSnapshot(_nextTenantId, _nextFoodOrderId, _nextBookingId,
            _tenants.Values.ToArray(), _foodOrders.Values.ToArray(), _hotelBookings.Values.ToArray());
        _tenants.Clear(); _foodOrders.Clear(); _hotelBookings.Clear();
        RestoreBusinessSnapshot(snapshot, true);
    }

    private void MigrateLegacyServices()
    {
        foreach (var worker in _people.Values.Where(person => person.Role == "Staff").ToArray())
        {
            if (worker.Activity is PersonActivity.Arriving or PersonActivity.Working)
            {
                Require(worker.ServiceTargetId.HasValue && _operations.ContainsKey(worker.ServiceTargetId.Value),
                    "An old service worker has no recoverable assignment.");
                var taskId = _nextServiceTaskId++;
                var working = worker.Activity == PersonActivity.Working;
                _serviceTasks.Add(taskId, new ServiceTask(taskId, worker.ServiceTargetId.Value,
                    LegacyServiceKind(_operations[worker.ServiceTargetId.Value]), working ? ServiceTaskStatus.InProgress : ServiceTaskStatus.Traveling,
                    worker.Id, worker.RoomId, worker.CreatedAt, working ? Tick : worker.CreatedAt,
                    working ? worker.ActionAt : null, null, "Imported an existing routed service assignment."));
            }
            else
            {
                if (worker.Activity == PersonActivity.Stranded && worker.ServiceTargetId is { } target
                    && _operations.ContainsKey(target) && !_serviceTasks.Values.Any(task => task.RoomId == target))
                {
                    var taskId = _nextServiceTaskId++;
                    _serviceTasks.Add(taskId, new ServiceTask(taskId, target, LegacyServiceKind(_operations[target]),
                        ServiceTaskStatus.Blocked, null, null, worker.CreatedAt, Tick, null, null,
                        "Imported work awaits a reachable service route."));
                }
                _people[worker.Id] = worker with { ServiceTargetId = null };
            }
        }
        var snapshot = new ServiceSnapshot(_nextServiceTaskId, _serviceTasks.Values.ToArray());
        _serviceTasks.Clear();
        RestoreServiceSnapshot(snapshot);
    }

    // Old jobs always cleaned; repair materials were charged only below the old fixed threshold.
    private static ServiceTaskKind LegacyServiceKind(RoomOperation operation)
        => operation.Condition < 85 ? ServiceTaskKind.CleaningAndMaintenance : ServiceTaskKind.Cleaning;

    private void ValidateManagementRelationships()
    {
        foreach (var room in World.Rooms)
        {
            var operation = _operations[room.Id]; var model = Rules.For(room.DefinitionId)!.Model;
            if (model is "Office" or "Home")
                Require(operation.ContractActive == _tenants.Values.Any(t => t.RoomId == room.Id && t.Status is TenancyStatus.Active or TenancyStatus.Notice),
                    "A room's occupancy flag disagrees with its tenant contract.");
            if (operation.ReservationPersonId.HasValue)
                Require(model == "Hotel" && _hotelBookings.Values.Any(b => b.RoomId == room.Id && b.PersonId == operation.ReservationPersonId
                    && b.Status is HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn), "A hotel assignment has no matching booking.");
            if (_hotelBookings.Values.Any(b => b.RoomId == room.Id && b.Status is HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn))
                Require(!operation.Dirty, "A dirty hotel room has been assigned as ready inventory.");
        }
        foreach (var person in _people.Values)
        {
            var journey = Transport.JourneyFor(person.Id)!;
            Require(journey.Service == (person.Role == "Staff"), "A person's transport access policy disagrees with their role.");
            if (person.Role is "Worker" or "Resident" && person.Activity is not (PersonActivity.Leaving or PersonActivity.Stranded))
                Require(_tenantMembers.TryGetValue(person.Id, out var tenantId) && _tenants[tenantId].Status != TenancyStatus.Ended,
                    "An active resident or office worker has no tenant contract.");
            if (Rules.For(Room(person.RoomId)!.DefinitionId)!.Model is "Food" or "Shop" && person.Role is "Customer" or "Tourist")
                Require(_foodByPerson.ContainsKey(person.Id), "A food customer has no saved order.");
            if (person.Role == "Guest") Require(_bookingByPerson.ContainsKey(person.Id), "A guest has no saved hotel assignment.");
            if (person.Activity is PersonActivity.WaitingForService or PersonActivity.BeingServed)
                Require(_foodByPerson.TryGetValue(person.Id, out var orderId)
                    && _foodOrders[orderId].Status is FoodOrderStatus.Queued or FoodOrderStatus.Serving or FoodOrderStatus.Abandoned,
                    "A service-waiting customer has no matching unfinished order.");
            if (_foodByPerson.TryGetValue(person.Id, out var foodId) && _foodOrders[foodId].Status == FoodOrderStatus.Completed)
                Require(person.Activity is PersonActivity.Visiting or PersonActivity.Leaving or PersonActivity.Stranded,
                    "A completed food purchase retains an unfinished service activity.");
            if (person.Role == "Staff")
            {
                Require(Room(person.RoomId) is { } depot && Rules.For(depot.DefinitionId)?.Model == "Service",
                    "A service worker has no valid depot.");
                var claimed = _serviceTasks.Values.Count(task => task.WorkerPersonId == person.Id && ServiceTaskIsClaimed(task));
                Require(person.Activity is PersonActivity.Arriving or PersonActivity.Working
                        ? claimed == 1 && person.ServiceTargetId.HasValue
                        : claimed == 0 && person.ServiceTargetId == null
                            && person.Activity is PersonActivity.Returning or PersonActivity.Stranded or PersonActivity.Leaving,
                    "A service worker has conflicting task ownership.");
            }
        }
        foreach (var batch in _pendingBilling)
            foreach (var obligation in batch.Obligations)
            {
                if (obligation.Category != "Lease.Home")
                    Require(obligation.TenantId == null, "A non-rent obligation cannot reference a tenant.");
                else Require(obligation.TenantId.HasValue && _tenants.TryGetValue(obligation.TenantId.Value, out var tenant)
                    && tenant.RoomId == obligation.RoomId && tenant.Kind == "Home", "A frozen rent obligation has no matching tenant contract.");
            }
    }
}
