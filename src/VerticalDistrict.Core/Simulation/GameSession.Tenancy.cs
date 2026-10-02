namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, TenantContract> _tenants = new();
    private readonly Dictionary<long, long> _tenantMembers = new();
    private long _nextTenantId = 1;
    public IReadOnlyList<TenantContract> Tenants => _tenants.Values.Select(CloneTenant).ToArray();
    private static TenantContract CloneTenant(TenantContract tenant) => tenant with { MemberIds = tenant.MemberIds.ToArray() };
    public TenantContract? TenantFor(long roomId)
        => _tenants.Values.LastOrDefault(t => t.RoomId == roomId && t.Status != TenancyStatus.Ended) is { } tenant ? CloneTenant(tenant) : null;
    public int ContractedOccupancy(long roomId) => TenantFor(roomId)?.MemberIds.Length ?? 0;
    private TenantContract? TenantForPerson(long personId)
        => _tenantMembers.TryGetValue(personId, out var id) ? _tenants.GetValueOrDefault(id) : null;

    public CommandResult EndTenancy(long roomId)
    {
        if (TenantFor(roomId) is not { } tenant) return new(false, "This room has no active tenancy.");
        RequestTenantDeparture(tenant, "The manager ended this tenancy.");
        return new(true, "Tenancy ended. Members inside will leave through their physical routes.");
    }
    private void RequestTenantDeparture(TenantContract tenant, string reason)
    {
        if (tenant.Status is TenancyStatus.Departing or TenancyStatus.Ended) return;
        _tenants[tenant.Id] = tenant with { Status = TenancyStatus.Departing, DepartureReason = reason };
        if (_operations.TryGetValue(tenant.RoomId, out var operation)) SetOp(operation with { ContractActive = false });
        Notice($"Tenant #{tenant.Id} is leaving room {tenant.RoomId}: {reason}", "Tenancy");
        FinishTenantDeparture(tenant.Id);
    }
    private void FinishTenantDeparture(long tenantId)
    {
        if (!_tenants.TryGetValue(tenantId, out var tenant) || tenant.Status != TenancyStatus.Departing
            || tenant.MemberIds.Any(_people.ContainsKey)) return;
        _tenants[tenantId] = tenant with { Status = TenancyStatus.Ended, EndedAtTick = Tick };
        TrimBusinessHistory();
    }
    private void UpdateTenancies()
    {
        foreach (var tenant in _tenants.Values.ToArray())
        {
            if (tenant.Status == TenancyStatus.Ended) continue;
            if (tenant.Status == TenancyStatus.Departing) { FinishTenantDeparture(tenant.Id); continue; }
            var room = Room(tenant.RoomId); var op = OperationFor(tenant.RoomId);
            if (room == null || op == null) continue;
            var rule = Rules.For(room.DefinitionId)!;
            var current = tenant;
            if (current.LastReviewDay < Day)
            {
                var healthy = Ready(room, op, rule, false) && DemandFor(room.Id).Satisfaction >= 35;
                var badDays = healthy ? 0 : current.BadDays + 1;
                current = current with { LastReviewDay = Day, BadDays = badDays,
                    Status = healthy ? TenancyStatus.Active : TenancyStatus.Notice,
                    DepartureReason = healthy ? "" : OperatingWarning(room.Id) == "Operating" ? "Satisfaction remains low." : OperatingWarning(room.Id) };
                _tenants[current.Id] = current;
                if (badDays >= Rules.Management.TenantGraceDays)
                { RequestTenantDeparture(current, $"Unresolved service/access complaints for {badDays} daily reviews."); continue; }
            }
            // Renew at the normal arrival hour, after midnight has captured the final old-term home rent.
            if (Day >= current.RenewalDay && Hour == (current.Kind == "Office" ? 8 : 18))
            {
                if (!Ready(room, op, rule, false) || DemandFor(room.Id).Score < Rules.Management.MinContractDemand)
                { RequestTenantDeparture(current, "The proposed renewal price or service conditions were not accepted."); continue; }
                current = current with { AgreedRentMinor = op.PriceMinor, RenewalDay = Day + Rules.Management.LeaseDays };
                _tenants[current.Id] = current;
                Notice($"Tenant #{current.Id} renewed room {room.Id} at {op.PriceMinor / 100m:N0} per day.", "Tenancy");
            }
            if (!Ready(room, op, rule, false)) continue;
            var arrivalHour = current.Kind == "Office" ? 8 : 18;
            if (Hour == arrivalHour && current.LastArrivalDay != Day)
            {
                foreach (var member in current.MemberIds)
                    if (!_people.ContainsKey(member)) Spawn(room, current.Kind == "Office" ? "Worker" : "Resident",
                        current.Kind == "Office" ? (18 - Hour) * 3600 - Minute * 60 : 18 * 3600, memberId: member);
                if (current.MemberIds.All(_people.ContainsKey))
                    _tenants[current.Id] = current = current with { LastArrivalDay = Day };
            }
            if (current.Kind == "Office" && Hour >= 9 && Hour < 18 && current.LastOccupiedDay > 0 && current.LastPaidDay < Day)
            {
                if (Post(room.Id, current.AgreedRentMinor, "Lease.Office", $"Day {Day}: tenant #{current.Id} office lease, agreed daily rent."))
                {
                    _tenants[current.Id] = current with { LastPaidDay = Day };
                    SetOp(_operations[room.Id] with { LastRentDay = Day, ContractActive = true });
                }
            }
        }
        foreach (var room in World.Rooms.OrderBy(r => r.Id))
        {
            var rule = Rules.For(room.DefinitionId); var op = OperationFor(room.Id);
            if (rule?.Model is not ("Office" or "Home") || op == null || TenantFor(room.Id) != null
                || Hour != (rule.Model == "Office" ? 8 : 18) || !Ready(room, op, rule, false)
                || DemandFor(room.Id).Score < Rules.Management.MinContractDemand) continue;
            var nextMemberId = checked(_nextPersonId + rule.Capacity); var nextTenantId = checked(_nextTenantId + 1);
            var members = Enumerable.Range(0, rule.Capacity).Select(index => _nextPersonId + index).ToArray();
            var tenant = new TenantContract(_nextTenantId, room.Id, rule.Model, Day, Day + Rules.Management.LeaseDays,
                op.PriceMinor, members, 0, 0, 0, Day, 0, TenancyStatus.Active, "", 0);
            _nextPersonId = nextMemberId; _nextTenantId = nextTenantId;
            _tenants.Add(tenant.Id, tenant);
            foreach (var member in members) _tenantMembers.Add(member, tenant.Id);
            SetOp(op with { ContractActive = true });
            foreach (var member in members) Spawn(room, rule.Model == "Office" ? "Worker" : "Resident",
                rule.Model == "Office" ? (18 - Hour) * 3600 - Minute * 60 : 18 * 3600, memberId: member);
            if (members.All(_people.ContainsKey)) _tenants[tenant.Id] = tenant with { LastArrivalDay = Day };
        }
    }
    private void RecordTenantArrival(PersonState person)
    {
        if (TenantForPerson(person.Id) is { } tenant)
            _tenants[tenant.Id] = tenant with { LastOccupiedDay = Day };
    }
    private BillingObligation? HomeRentObligation(long roomId, long day)
    {
        var tenant = TenantFor(roomId); var room = Room(roomId); var op = OperationFor(roomId);
        if (tenant == null || tenant.Kind != "Home" || tenant.Status is TenancyStatus.Departing or TenancyStatus.Ended
            || tenant.LastOccupiedDay == 0 || tenant.LastPaidDay >= day || room == null || op == null
            || !Ready(room, op, Rules.For(room.DefinitionId)!, false)) return null;
        return new BillingObligation(roomId, tenant.AgreedRentMinor, "Lease.Home", $"Day {day}: tenant #{tenant.Id} residential lease due at midnight.", tenant.Id);
    }
    private void RecordHomeRentPaid(BillingObligation obligation, long day)
    {
        var tenant = obligation.TenantId.HasValue ? _tenants.GetValueOrDefault(obligation.TenantId.Value) : TenantFor(obligation.RoomId!.Value);
        if (tenant != null) _tenants[tenant.Id] = tenant with { LastPaidDay = Math.Max(day, tenant.LastPaidDay) };
    }
}
