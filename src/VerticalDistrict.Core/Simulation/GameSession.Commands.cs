using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    public CommandResult ValidateRoom(string id, int x, int floor)
    {
        var result = World.ValidateRoom(id, x, floor);
        if (!result.Success) return result;
        var d = World.Catalog.Get(id);
        for (var f = floor; f < floor + d.Height; f++)
            for (var bay = x; bay < x + d.Width; bay++)
                if (Transport.Occupies(bay, f)) return new(false, "This footprint intersects a shaft or staircase.");
        if (id == "subway")
        {
            if (floor >= 0) return new(false, "Subway concourses must be underground.");
            return Locations.ValidateSubway(LocationId, SiteId, World.Rank);
        }
        if (id == "dock")
        {
            if (floor != 0) return new(false, "Boat terminals connect to the waterfront at ground level.");
            return Locations.ValidateDock(LocationId, SiteId, World.Rank);
        }
        if (id == "parking" && floor >= 0) return new(false, "This garage definition is restricted to basement floors.");
        return result;
    }
    public CommandResult BuildRoom(string id, int x, int floor)
    {
        var check = ValidateRoom(id, x, floor);
        if (!check.Success) return check;
        var result = World.BuildRoom(id, x, floor);
        if (result.Success) SynchronizeConstruction();
        return result;
    }
    public CommandResult ValidateDemolishRoom(long id)
    {
        var result = World.ValidateDemolishRoom(id);
        if (!result.Success) return result;
        if (_pendingBilling.Any(batch => batch.Obligations.Any(bill => bill.RoomId == id)))
            return new(false, "This room has an unpaid billing obligation. Settle the pending period before demolition.");
        if (_people.Values.Any(p => p.RoomId == id || p.ServiceTargetId == id)) return new(false, "Close this facility and wait for people and service staff to leave before demolition.");
        if (_operations.TryGetValue(id, out var op) && op.CondoSold) return new(false, "Buy back the condominium before demolition.");
        var room = Room(id)!;
        if (room.DefinitionId == "lobby" && _people.Count > 0) return new(false, "The entrance cannot be removed while people remain in the tower.");
        return result;
    }
    public CommandResult DemolishRoom(long id)
    {
        var check = ValidateDemolishRoom(id);
        if (!check.Success) return check;
        var result = World.DemolishRoom(id);
        if (result.Success) _operations.Remove(id);
        return result;
    }
    public CommandResult ValidateDemolishFloor(int floor)
    {
        if (Enumerable.Range(0, ConstructionWorld.Width).Any(x => Transport.Occupies(x, floor)))
            return new(false, "Remove or reconfigure transport on this floor first.");
        if (Transport.Journeys.Any(j => j.Floor == floor && j.State is not (JourneyState.Arrived or JourneyState.Abandoned)))
            return new(false, "A journey still uses this floor.");
        return World.ValidateDemolishFloor(floor);
    }
    public CommandResult DemolishFloor(int floor) => ValidateDemolishFloor(floor).Success ? World.DemolishFloor(floor) : ValidateDemolishFloor(floor);
    public CommandResult SetPrice(long id, long price)
    {
        if (!_operations.TryGetValue(id, out var op) || price < 0 || price > 100_000_000) return new(false, "Choose a price from $0 to $1,000,000.");
        SetOp(op with { PriceMinor = price }); return new(true, "Price updated. Existing prepaid stays and ownership contracts keep their agreed price.");
    }
    public CommandResult SetStaff(long id, int staff)
    {
        if (!_operations.TryGetValue(id, out var op) || staff is < 0 or > 20) return new(false, "Staff must be between 0 and 20.");
        if (staff < _people.Values.Count(p => p.RoomId == id && p.Role == "Staff")) return new(false, "Wait for routed service workers to return before reducing this allocation.");
        SetOp(op with { Staff = staff }); return new(true, "Staff allocation updated; salaries are charged daily.");
    }
    public CommandResult SetOpen(long id, bool open)
    {
        if (!_operations.TryGetValue(id, out var op)) return new(false, "Unknown room.");
        if (!open && op.CondoSold) return new(false, "Owned homes must be bought back before closure.");
        SetOp(op with { Open = open });
        if (!open) Notice("Facility closed. Existing occupants will leave by their physical routes; prepaid guests retain their completed stay charge.");
        return new(true, open ? "Facility reopened." : "Facility closed; arrivals stopped and departures requested.");
    }
    public CommandResult SetFilm(long id, int film)
    {
        if (Room(id)?.DefinitionId != "cinema" || film is < 0 or > 2) return new(false, "Choose one of three original programs.");
        SetOp(_operations[id] with { Film = film }); return new(true, "Program updated for future screenings.");
    }
    public CommandResult ScheduleEvent(long id)
    {
        if (Room(id)?.DefinitionId != "event-hall") return new(false, "Select an event hall.");
        var op = _operations[id];
        if (op.NextEventTick > Tick || op.EventPrepared || ReservedCapacity(id) > 0) return new(false, "An event or preparation is already active.");
        var cost = Rules.EventPreparationCostMinor;
        if (World.CashMinor < cost) return new(false, $"Event preparation requires {cost / 100m:N0} currency units.");
        if (!Post(id, -cost, "Event.Preparation", "Prepared an original community event.")) return new(false, "Could not fund preparation.");
        SetOp(_operations[id] with { NextEventTick = Tick + 3600, EventPrepared = true });
        return new(true, "Event scheduled in one game hour. Staff and access are required at the scheduled time.");
    }
    public CommandResult BuyBackCondo(long id)
    {
        if (!_operations.TryGetValue(id, out var op) || !op.CondoSold) return new(false, "No condominium ownership contract here.");
        if (World.CashMinor < op.CondoSaleMinor) return new(false, "Insufficient cash to refund the recorded sale price.");
        if (!Post(id, -op.CondoSaleMinor, "Condo.Buyback", "Returned the recorded condominium purchase price.")) return new(false, "Buyback failed.");
        SetOp(_operations[id] with { CondoSold = false, CondoSaleMinor = 0, ContractActive = false, Open = false });
        return new(true, "Ownership repurchased. Residents will depart; then demolition is available.");
    }
    public CommandResult RepairRoom(long id)
    {
        if (!_operations.TryGetValue(id, out var op)) return new(false, "Select a room.");
        if (!op.Dirty && op.Condition >= 85 && op.Cleanliness >= 85) return new(false, "This room does not need cleaning or repair.");
        if (_people.Values.Any(p => p.Role == "Staff" && p.ServiceTargetId == id))
            return new(true, "A worker is already assigned to this room.");
        AssignServiceWork(id);
        if (!_people.Values.Any(p => p.Role == "Staff" && p.ServiceTargetId == id))
            return new(false, "No available worker can reach this room. Check depot staffing, current jobs and service routes.");
        Notice($"A maintenance worker has been dispatched to room {id}.");
        return new(true, "Worker dispatched. Cleaning and repairs occur after travel and on-site work.");
    }
    public CommandResult TriggerLiftDisruption(int bankId)
    {
        var result = Transport.SetBankOutOfService(bankId, true);
        if (result.Success) Notice($"Elevator bank {bankId} is out of service. Use Restore service in transport controls.", "Warning");
        return result;
    }
}
