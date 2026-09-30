namespace VerticalDistrict.Core.Simulation;

public enum PersonActivity { Arriving, Visiting, Leaving, Working, Returning, Stranded }
public sealed record PersonState(long Id, long RoomId, string Role, PersonActivity Activity,
    long ActionAt, long CreatedAt, int Satisfaction, long? ServiceTargetId = null);
public sealed record RoomOperation(long RoomId, long PriceMinor, bool Open, int Staff,
    int Cleanliness, int Condition, bool Dirty, long? ReservationPersonId, bool ContractActive,
    long LastRentDay, long LastArrivalTick, bool CondoSold, long CondoSaleMinor, int Film,
    long NextEventTick, bool EventPrepared, long GrossRevenueMinor, long CostsMinor);
public sealed record DailyReport(long Day, long RevenueMinor, long ExpensesMinor, int Arrivals, int Departures,
    int Abandoned, int Satisfaction)
{
    public long ProfitMinor => RevenueMinor - ExpensesMinor;
}
public sealed record GameNotice(long Tick, string Text, string Kind);
