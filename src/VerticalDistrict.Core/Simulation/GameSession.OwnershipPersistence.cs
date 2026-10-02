using System.Text.RegularExpressions;

namespace VerticalDistrict.Core.Simulation;

public sealed record OwnershipSnapshot(long NextOwnershipId, long LegacyThroughOwnershipId, CondoOwnership[] Contracts);

public sealed partial class GameSession
{
    private sealed record OwnershipCashCycle(LedgerEntry? Sale, LedgerEntry? Refund);

    private OwnershipSnapshot CaptureOwnershipSnapshot() => new(_nextOwnershipId, _legacyOwnershipThroughId,
        _ownerships.Values.Select(CloneOwnership).ToArray());

    private BusinessRule? OwnershipRoomRule(long roomId)
    {
        if (Room(roomId) is { } room) return Rules.For(room.DefinitionId) is { Model: "Condo" } rule ? rule : null;
        var construction = World.Ledger.SingleOrDefault(entry => entry.EntityId == roomId && entry.Category == "Construction.Room");
        return Rules.Businesses.FirstOrDefault(rule => rule.Model == "Condo" && construction?.Description
            == $"Built {World.Catalog.Get(rule.Id).Name} #{roomId}.");
    }

    // Retained ledger cycles remain authoritative even after a terminal agreement is pruned.
    private List<OwnershipCashCycle> OwnershipCashCycles(long roomId, bool permitLegacyFreeRefund)
    {
        var cycles = new List<OwnershipCashCycle>();
        LedgerEntry? sale = null;
        foreach (var entry in World.Ledger.Where(entry => entry.EntityId == roomId
                     && entry.Category is "Sales.Condo" or "Condo.Buyback"))
        {
            if (entry.Category == "Sales.Condo")
            {
                Require(sale == null && entry.AmountMinor is >= 0 and <= 100_000_000,
                    "A condominium has overlapping purchases or an invalid sale amount.");
                sale = entry;
            }
            else
            {
                Require(sale != null ? entry.AmountMinor == -sale.AmountMinor
                        : permitLegacyFreeRefund && entry.AmountMinor == 0,
                    "A condominium refund lacks its purchase or does not return the original price exactly once.");
                cycles.Add(new OwnershipCashCycle(sale, entry)); sale = null;
            }
        }
        if (sale != null) cycles.Add(new OwnershipCashCycle(sale, null));
        return cycles;
    }

    private void RestoreOwnershipSnapshot(OwnershipSnapshot? saved)
    {
        Require(saved != null && saved.Contracts != null && saved.NextOwnershipId > 0
            && saved.LegacyThroughOwnershipId >= 0 && saved.LegacyThroughOwnershipId < saved.NextOwnershipId,
            "Missing or invalid ownership state and counters.");
        var activeRooms = new HashSet<long>(); var sales = new HashSet<long>(); var refunds = new HashSet<long>();
        var cyclesByRoom = World.Ledger.Where(entry => entry.Category is "Sales.Condo" or "Condo.Buyback")
            .Select(entry => entry.EntityId).Where(id => id.HasValue).Select(id => id!.Value).Distinct()
            .ToDictionary(id => id, id => OwnershipCashCycles(id, saved.LegacyThroughOwnershipId > 0));
        foreach (var ownership in saved.Contracts)
        {
            Require(ownership != null && ownership.Id > 0 && ownership.Id < saved.NextOwnershipId
                && !_ownerships.ContainsKey(ownership.Id) && Enum.IsDefined(ownership.Status)
                && OwnershipRoomRule(ownership.RoomId) != null, "Invalid ownership identity, room, or status.");
            var legacy = ownership.Id <= saved.LegacyThroughOwnershipId;
            var rule = OwnershipRoomRule(ownership.RoomId)!;
            Require(ownership.ResidentIds != null && ownership.ResidentIds.Length == rule.Capacity
                && ownership.ResidentIds.Length > 0 && ownership.OwnerId == ownership.ResidentIds[0]
                && ownership.AgreedPriceMinor is >= 0 and <= 100_000_000
                && ownership.OfferedAtTick >= 0 && ownership.OfferedAtTick <= Tick
                && ownership.OfferExpiresAtTick > ownership.OfferedAtTick
                && ownership.OfferExpiresAtTick <= checked(ownership.OfferedAtTick + 86400)
                && ownership.LastArrivalDay >= 0 && ownership.LastArrivalDay <= Day
                && ownership.EndReason != null && ownership.EndReason.Length <= 500,
                "Invalid ownership household, frozen price, or purchase schedule.");
            foreach (var resident in ownership.ResidentIds)
            {
                Require(resident > 0 && resident < _nextPersonId && !_tenantMembers.ContainsKey(resident)
                    && !_foodByPerson.ContainsKey(resident) && !_bookingByPerson.ContainsKey(resident)
                    && _ownershipResidents.TryAdd(resident, ownership.Id), "An ownership household reuses a person ID or exceeds its counter.");
                if (_people.TryGetValue(resident, out var person))
                    Require(person.Role == "Owner" && person.RoomId == ownership.RoomId
                        && person.Activity is PersonActivity.Arriving or PersonActivity.Visiting or PersonActivity.Leaving or PersonActivity.Stranded,
                        "An ownership resident has a different room, role, or activity.");
            }
            var live = ownership.ResidentIds.Any(_people.ContainsKey);
            var terminal = ownership.Status is CondoOwnershipStatus.Reacquired or CondoOwnershipStatus.Cancelled;
            Require(terminal || activeRooms.Add(ownership.RoomId), "A condominium has more than one active ownership agreement.");
            Require(ownership.Status is CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Owned
                    ? ownership.EndReason.Length == 0 : ownership.EndReason.Length > 0,
                "Ownership status and its terminal explanation disagree.");
            if (Room(ownership.RoomId) == null)
            {
                var demolition = World.Ledger.SingleOrDefault(entry => entry.Category == "Demolition.Room" && entry.EntityId == ownership.RoomId);
                Require(terminal && !live && demolition != null && demolition.TimestampTicks >= (ownership.ReacquiredAtTick ?? ownership.OfferedAtTick),
                    "Only a completed ownership record can refer to an actually demolished condominium.");
            }
            if (ownership.Status is CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Cancelled)
            {
                Require(ownership.PurchasedAtTick == null && ownership.SaleLedgerSequence == null
                    && ownership.ReacquiredAtTick == null && ownership.BuybackLedgerSequence == null,
                    "An unpaid condominium offer retains a purchase or refund.");
                Require(!ownership.ResidentIds.Any(id => _people.GetValueOrDefault(id)?.Activity == PersonActivity.Visiting),
                    "An unpaid condominium offer already has a resident inside.");
            }
            else
            {
                Require(ownership.PurchasedAtTick >= ownership.OfferedAtTick && ownership.PurchasedAtTick <= Tick,
                    "A paid condominium has no valid purchase time.");
                RequireOwnershipReceipt(ownership, false, sales, legacy);
                if (ownership.Status == CondoOwnershipStatus.Owned)
                    Require(ownership.ReacquiredAtTick == null && ownership.BuybackLedgerSequence == null,
                        "A currently owned condominium has already been refunded.");
                else
                {
                    Require(ownership.ReacquiredAtTick >= ownership.PurchasedAtTick && ownership.ReacquiredAtTick <= Tick,
                        "A repurchased condominium has no valid refund time.");
                    RequireOwnershipReceipt(ownership, true, refunds, legacy);
                    Require(ownership.Status != CondoOwnershipStatus.Reacquired || !live,
                        "Reacquired ownership still has residents physically inside the tower.");
                    Require(ownership.Status != CondoOwnershipStatus.Evacuating || live,
                        "An evacuating ownership has no residents left to evacuate.");
                    if (ownership.SaleLedgerSequence is { } saleSequence)
                        Require(cyclesByRoom.GetValueOrDefault(ownership.RoomId)?.Any(cycle => cycle.Sale?.Sequence == saleSequence
                            && cycle.Refund?.Sequence == ownership.BuybackLedgerSequence) == true,
                            "The ownership refund does not close its recorded purchase cycle.");
                }
            }
            _ownerships.Add(ownership.Id, CloneOwnership(ownership));
        }
        foreach (var pair in cyclesByRoom)
        {
            var unpaidSale = pair.Value.LastOrDefault(cycle => cycle.Sale != null && cycle.Refund == null)?.Sale;
            if (unpaidSale != null)
                Require(_ownerships.Values.Any(ownership => ownership.RoomId == pair.Key && ownership.Status == CondoOwnershipStatus.Owned
                    && ownership.SaleLedgerSequence == unpaidSale.Sequence), "A condominium purchase has no matching current ownership.");
        }
        foreach (var room in World.Rooms.Where(room => Rules.For(room.DefinitionId)?.Model == "Condo"))
        {
            var op = _operations[room.Id];
            var ownership = _ownerships.Values.SingleOrDefault(value => value.RoomId == room.Id && value.Status == CondoOwnershipStatus.Owned);
            Require(op.CondoSold == (ownership != null) && op.ContractActive == (ownership != null)
                && op.CondoSaleMinor == (ownership?.AgreedPriceMinor ?? 0), "Condominium flags and recorded price disagree with ownership.");
            var current = _ownerships.Values.SingleOrDefault(value => value.RoomId == room.Id && value.Status is CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Owned or CondoOwnershipStatus.Evacuating);
            Require(current?.Status != CondoOwnershipStatus.Evacuating || !op.Open, "An evacuating condominium cannot be reopened.");
            if (ownership?.SaleLedgerSequence is { } saleSequence)
                Require(cyclesByRoom.GetValueOrDefault(room.Id)?.Any(cycle => cycle.Sale?.Sequence == saleSequence && cycle.Refund == null) == true,
                    "An owned condominium points to an already refunded purchase.");
            if (ownership != null && ownership.SaleLedgerSequence == null)
                Require(!cyclesByRoom.GetValueOrDefault(room.Id, []).Any(cycle => cycle.Refund?.TimestampTicks >= ownership.PurchasedAtTick),
                    "An imported free condominium was already repurchased.");
        }
        Require(_people.Values.Where(person => person.Role == "Owner").All(person => _ownershipResidents.ContainsKey(person.Id)),
            "A physical condominium resident has no ownership agreement.");
        Require(_ownerships.Values.Count(ownership => ownership.Status is CondoOwnershipStatus.Reacquired or CondoOwnershipStatus.Cancelled
            && ownership.ResidentIds.All(id => !_people.ContainsKey(id))) <= Rules.Management.BusinessHistoryLimit,
            "Completed ownership history exceeds its configured limit.");
        _nextOwnershipId = saved.NextOwnershipId; _legacyOwnershipThroughId = saved.LegacyThroughOwnershipId;
    }

    private void RequireOwnershipReceipt(CondoOwnership ownership, bool refund, HashSet<long> claimed, bool legacy)
    {
        var sequence = refund ? ownership.BuybackLedgerSequence : ownership.SaleLedgerSequence;
        if (sequence == null)
        {
            Require(legacy && ownership.AgreedPriceMinor == 0, "A paid condominium transaction has no authoritative ledger receipt.");
            return;
        }
        Require(sequence > 0 && sequence <= World.Ledger.Count && claimed.Add(sequence.Value), "An ownership receipt is invalid or reused.");
        var entry = World.Ledger[checked((int)sequence.Value - 1)];
        Require(entry.Category == (refund ? "Condo.Buyback" : "Sales.Condo") && entry.EntityId == ownership.RoomId
            && entry.AmountMinor == (refund ? -ownership.AgreedPriceMinor : ownership.AgreedPriceMinor)
            && entry.TimestampTicks == (refund ? ownership.ReacquiredAtTick : ownership.PurchasedAtTick)
            && (legacy || entry.Description.StartsWith($"Ownership #{ownership.Id}, owner #{ownership.OwnerId}:", StringComparison.Ordinal)),
            "The ownership price, identity, timestamp, and ledger receipt do not reconcile.");
    }

    private void MigrateLegacyOwnerships()
    {
        var normalizedSchedule = false;
        foreach (var room in World.Rooms.Where(room => Rules.For(room.DefinitionId)?.Model == "Condo").OrderBy(room => room.Id))
        {
            var op = _operations[room.Id]; var rule = Rules.For(room.DefinitionId)!;
            var actors = _people.Values.Where(person => person.RoomId == room.Id && person.Role == "Owner").OrderBy(person => person.Id).ToArray();
            var cycles = OwnershipCashCycles(room.Id, true);
            var last = cycles.LastOrDefault();
            var outstanding = last is { Sale: not null, Refund: null } ? last.Sale : null;
            Require(op.CondoSold ? outstanding?.AmountMinor == op.CondoSaleMinor || op.CondoSaleMinor == 0 && outstanding == null
                    : outstanding == null, "Legacy condominium flags and outstanding sale receipt disagree.");
            if (!op.CondoSold && actors.Length == 0 && last?.Refund?.AmountMinor != 0) continue;
            var refund = !op.CondoSold && last?.Refund != null && (actors.Length == 0 || actors.All(person => person.CreatedAt <= last.Refund.TimestampTicks))
                ? last.Refund : null;
            var sale = op.CondoSold ? outstanding : refund != null ? last!.Sale : null;
            var paid = op.CondoSold || refund != null;
            var price = paid ? sale?.AmountMinor ?? 0 : op.PriceMinor;
            var oldFreeArrival = actors.Where(person => person.Activity == PersonActivity.Visiting)
                .Select(person => person.ActionAt - 30 * 86400L).Where(arrival => arrival >= 0 && arrival <= Tick).DefaultIfEmpty(Tick).Min();
            var purchased = paid ? sale?.TimestampTicks ?? Math.Min(oldFreeArrival, refund?.TimestampTicks ?? Tick) : (long?)null;
            var offered = Math.Min(purchased ?? Tick, actors.Length == 0 ? purchased ?? Tick : actors.Min(person => person.CreatedAt));
            // A slow legacy arrival had no accepted offer deadline. Retain its receipt and give the imported offer a valid historical window.
            if (purchased - offered >= 86400) offered = purchased!.Value;
            var members = actors.Select(person => person.Id).ToList();
            var ownerMatch = sale == null ? Match.Empty : Regex.Match(sale.Description, @"(?:^|, )owner #(\d+):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (ownerMatch.Success && long.TryParse(ownerMatch.Groups[1].Value, out var originalOwner)
                && originalOwner > 0 && originalOwner < _nextPersonId && !_tenantMembers.ContainsKey(originalOwner)
                && !_foodByPerson.ContainsKey(originalOwner) && !_bookingByPerson.ContainsKey(originalOwner)
                && (! _people.TryGetValue(originalOwner, out var existing) || existing.RoomId == room.Id && existing.Role == "Owner"))
            { members.Remove(originalOwner); members.Insert(0, originalOwner); }
            Require(members.Count <= rule.Capacity, "Legacy condominium residents exceed the household capacity.");
            while (members.Count < rule.Capacity) { members.Add(_nextPersonId); _nextPersonId = checked(_nextPersonId + 1); }
            var status = op.CondoSold ? CondoOwnershipStatus.Owned : refund != null
                ? actors.Length > 0 ? CondoOwnershipStatus.Evacuating : CondoOwnershipStatus.Reacquired
                : op.Open && actors.Any(person => person.Activity == PersonActivity.Arriving) ? CondoOwnershipStatus.PendingSale : CondoOwnershipStatus.Cancelled;
            var expires = NextOwnershipDeparture(offered);
            if (status == CondoOwnershipStatus.PendingSale && expires <= Tick) expires = NextOwnershipDeparture(Tick);
            if (expires > offered + 86400) offered = Tick;
            var ownership = new CondoOwnership(_nextOwnershipId++, room.Id, members[0], members.ToArray(), price, offered, expires,
                purchased, sale?.Sequence, refund?.TimestampTicks, refund?.Sequence,
                actors.Length > 0 ? Day : 0, status, status is CondoOwnershipStatus.PendingSale or CondoOwnershipStatus.Owned ? ""
                    : refund != null ? "Imported the original condominium refund and remaining physical departures." : "Imported an unfinished condominium purchase that did not complete.");
            _ownerships.Add(ownership.Id, ownership);
            SetOp(op with { CondoSold = status == CondoOwnershipStatus.Owned, CondoSaleMinor = status == CondoOwnershipStatus.Owned ? price : 0,
                ContractActive = status == CondoOwnershipStatus.Owned, Open = status == CondoOwnershipStatus.Evacuating ? false : op.Open });
            foreach (var actor in actors.Where(actor => actor.Activity is PersonActivity.Arriving or PersonActivity.Visiting))
                if (status is CondoOwnershipStatus.Owned or CondoOwnershipStatus.PendingSale)
                {
                    var deadline = NextOwnershipDeparture(Tick);
                    _people[actor.Id] = actor with { ActionAt = actor.Activity == PersonActivity.Visiting ? deadline : deadline - actor.CreatedAt };
                    normalizedSchedule = true;
                }
        }
        _legacyOwnershipThroughId = _nextOwnershipId - 1;
        var captured = CaptureOwnershipSnapshot();
        _ownerships.Clear(); _ownershipResidents.Clear();
        RestoreOwnershipSnapshot(captured);
        if (normalizedSchedule) Notice("Imported condominium ownership and original sale prices. Existing residents now use the daily 18:00 arrival / 08:00 departure schedule; their physical journeys are preserved.", "Migration");
    }
}
