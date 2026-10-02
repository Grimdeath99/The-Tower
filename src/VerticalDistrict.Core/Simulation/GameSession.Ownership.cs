namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, CondoOwnership> _ownerships = new();
    private readonly Dictionary<long, long> _ownershipResidents = new();
    private long _nextOwnershipId = 1;
    private long _legacyOwnershipThroughId;

    public IReadOnlyList<CondoOwnership> Ownerships => _ownerships.Values.Select(CloneOwnership).ToArray();
    private static CondoOwnership CloneOwnership(CondoOwnership ownership) => ownership with { ResidentIds = ownership.ResidentIds.ToArray() };
    public CondoOwnership? OwnershipFor(long roomId)
        => _ownerships.Values.LastOrDefault(o => o.RoomId == roomId && o.Status is CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Owned or CondoOwnershipStatus.Evacuating)
            is { } ownership ? CloneOwnership(ownership) : null;
    public int CondoResidentCapacity(long roomId) => OwnershipFor(roomId)?.ResidentIds.Length ?? 0;
    private CondoOwnership? OwnershipForResident(long personId)
        => _ownershipResidents.TryGetValue(personId, out var id) ? _ownerships.GetValueOrDefault(id) : null;

    public CondoAvailability CondoStateFor(long roomId)
    {
        var room = Room(roomId); var op = OperationFor(roomId);
        if (room == null || Rules.For(room.DefinitionId)?.Model != "Condo" || op == null) return CondoAvailability.Unavailable;
        if (OwnershipFor(roomId) is { } ownership) return ownership.Status switch
        {
            CondoOwnershipStatus.PendingSale => CondoAvailability.PendingSale,
            CondoOwnershipStatus.Owned => CondoAvailability.Owned,
            _ => CondoAvailability.Evacuating
        };
        if (OwnershipAwaitingExit(roomId)) return CondoAvailability.Evacuating;
        return Ready(room, op, Rules.For(room.DefinitionId)!, false) ? CondoAvailability.Available : CondoAvailability.Unavailable;
    }
    private bool OwnershipAwaitingExit(long roomId) => _ownerships.Values.Any(o => o.RoomId == roomId
        && o.Status is CondoOwnershipStatus.Evacuating or CondoOwnershipStatus.Cancelled && o.ResidentIds.Any(_people.ContainsKey));
    private bool OwnershipActorMustLeave(PersonState person) => OwnershipForResident(person.Id)?.Status
        is CondoOwnershipStatus.Evacuating or CondoOwnershipStatus.Reacquired or CondoOwnershipStatus.Cancelled;
    private static long NextOwnershipDeparture(long tick)
    {
        var dayStart = (tick + 28500) / 86400 * 86400 - 28500;
        var departure = dayStart + 8 * 3600;
        return departure > tick ? departure : checked(departure + 86400);
    }
    private void UpdateOwnerships()
    {
        foreach (var original in _ownerships.Values.ToArray())
        {
            if (original.Status == CondoOwnershipStatus.Evacuating) { FinishOwnershipDeparture(original.Id); continue; }
            if (original.Status is CondoOwnershipStatus.Reacquired or CondoOwnershipStatus.Cancelled) continue;
            var room = Room(original.RoomId); var op = OperationFor(original.RoomId);
            if (room == null || op == null) continue;
            if (original.Status == CondoOwnershipStatus.PendingSale && (Tick >= original.OfferExpiresAtTick || !op.Open))
            { CancelOwnershipOffer(original, "The purchase was not completed before the agreed morning deadline or the room closed."); continue; }
            if (Hour != 18 || original.LastArrivalDay == Day || !Ready(room, op, Rules.For(room.DefinitionId)!, false)) continue;
            foreach (var member in original.ResidentIds)
                if (!_people.ContainsKey(member)) Spawn(room, "Owner", NextOwnershipDeparture(Tick) - Tick, memberId: member);
            if (original.ResidentIds.All(_people.ContainsKey)) _ownerships[original.Id] = original with { LastArrivalDay = Day };
        }
        foreach (var room in World.Rooms.OrderBy(r => r.Id))
        {
            var rule = Rules.For(room.DefinitionId); var op = OperationFor(room.Id);
            if (rule?.Model != "Condo" || rule.Capacity < 1 || op == null || Hour != 18 || OwnershipFor(room.Id) != null
                || _people.Values.Any(p => p.RoomId == room.Id && p.Role == "Owner") || !Ready(room, op, rule, false)
                || DemandFor(room.Id).Score < Rules.Management.MinContractDemand) continue;
            var nextPerson = checked(_nextPersonId + rule.Capacity); var nextOwnership = checked(_nextOwnershipId + 1);
            var residents = Enumerable.Range(0, rule.Capacity).Select(index => _nextPersonId + index).ToArray();
            var ownership = new CondoOwnership(_nextOwnershipId, room.Id, residents[0], residents, op.PriceMinor,
                Tick, NextOwnershipDeparture(Tick), null, null, null, null, 0, CondoOwnershipStatus.PendingSale, "");
            _nextPersonId = nextPerson; _nextOwnershipId = nextOwnership;
            _ownerships.Add(ownership.Id, ownership);
            foreach (var member in residents)
            {
                _ownershipResidents.Add(member, ownership.Id);
                Spawn(room, "Owner", ownership.OfferExpiresAtTick - Tick, memberId: member);
            }
            if (residents.All(_people.ContainsKey)) _ownerships[ownership.Id] = ownership with { LastArrivalDay = Day };
        }
        TrimOwnershipHistory();
    }
    private bool EnterOwnedResidence(PersonState person)
    {
        if (OwnershipForResident(person.Id) is not { } ownership
            || ownership.Status is not (CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Owned)) return false;
        var departure = checked(person.CreatedAt + person.ActionAt);
        if (departure <= Tick) return false;
        if (ownership.Status == CondoOwnershipStatus.PendingSale)
        {
            if (Tick >= ownership.OfferExpiresAtTick) { CancelOwnershipOffer(ownership, "The accepted purchase expired before physical arrival."); return false; }
            if (!Post(ownership.RoomId, ownership.AgreedPriceMinor, "Sales.Condo",
                $"Ownership #{ownership.Id}, owner #{ownership.OwnerId}: agreed condominium purchase completed on household arrival."))
            { CancelOwnershipOffer(ownership, "The purchase could not be posted; no ownership or payment was committed."); return false; }
            ownership = ownership with { Status = CondoOwnershipStatus.Owned, PurchasedAtTick = Tick, SaleLedgerSequence = World.Ledger[^1].Sequence };
            _ownerships[ownership.Id] = ownership;
            SetOp(_operations[ownership.RoomId] with { CondoSold = true, CondoSaleMinor = ownership.AgreedPriceMinor, ContractActive = true });
            RecordManagementOutcome(ownership.RoomId, true);
        }
        var op = _operations[ownership.RoomId];
        SetOp(op with { Cleanliness = Math.Max(0, op.Cleanliness - 1) });
        _people[person.Id] = person with { Activity = PersonActivity.Visiting, ActionAt = departure, Satisfaction = DemandFor(person.RoomId).Satisfaction };
        return true;
    }
    private void CancelOwnershipOffer(CondoOwnership ownership, string reason)
    {
        if (ownership.Status != CondoOwnershipStatus.PendingSale) return;
        _ownerships[ownership.Id] = ownership with { Status = CondoOwnershipStatus.Cancelled, EndReason = reason };
        if (_operations.TryGetValue(ownership.RoomId, out var op)) SetOp(op with { CondoSold = false, CondoSaleMinor = 0, ContractActive = false });
        RecordManagementOutcome(ownership.RoomId, false);
        TrimOwnershipHistory();
    }
    private void CloseOwnershipOffer(long roomId)
    {
        if (OwnershipFor(roomId) is { Status: CondoOwnershipStatus.PendingSale } ownership)
            CancelOwnershipOffer(ownership, "The manager closed this unit before its purchase completed; no sale or refund occurred.");
    }
    private void OnOwnershipActorRemoved(PersonState person)
    {
        if (OwnershipForResident(person.Id) is not { } ownership) return;
        if (ownership.Status == CondoOwnershipStatus.PendingSale && ownership.ResidentIds.All(id => !_people.ContainsKey(id)))
            CancelOwnershipOffer(ownership, "The prospective household left before completing its purchase.");
        if (ownership.Status == CondoOwnershipStatus.Evacuating) FinishOwnershipDeparture(ownership.Id);
        TrimOwnershipHistory();
    }
    private void FinishOwnershipDeparture(long id)
    {
        if (!_ownerships.TryGetValue(id, out var ownership) || ownership.Status != CondoOwnershipStatus.Evacuating
            || ownership.ResidentIds.Any(_people.ContainsKey)) return;
        _ownerships[id] = ownership with { Status = CondoOwnershipStatus.Reacquired, EndReason = "The original purchase price was returned and every assigned resident has left." };
        TrimOwnershipHistory();
    }
    private string? OwnershipDemolitionBlocker(long roomId) => OwnershipFor(roomId) is { } ownership
        ? ownership.Status == CondoOwnershipStatus.Owned ? "Buy back the condominium ownership before demolition."
            : ownership.Status == CondoOwnershipStatus.PendingSale ? "Cancel the pending purchase by closing the unit and wait for its prospective residents to leave."
            : "Wait for the reacquired unit's residents to physically leave before demolition."
        : OwnershipAwaitingExit(roomId) ? "Wait for the cancelled purchase's residents to leave before demolition." : null;
    private void RemoveOwnershipRoom(long roomId)
    {
        // Demolition is guarded while any active assignment exists. Keep bounded terminal records
        // so a demolished unit's purchase and reacquisition remain traceable in the ledger.
        TrimOwnershipHistory();
    }
    private void TrimOwnershipHistory()
    {
        foreach (var ownership in _ownerships.Values.Where(o => o.Status is CondoOwnershipStatus.Reacquired or CondoOwnershipStatus.Cancelled)
            .Where(o => o.ResidentIds.All(id => !_people.ContainsKey(id))).OrderByDescending(o => o.Id).Skip(Rules.Management.BusinessHistoryLimit).ToArray())
        { _ownerships.Remove(ownership.Id); foreach (var member in ownership.ResidentIds) _ownershipResidents.Remove(member); }
    }
}
