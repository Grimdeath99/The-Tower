namespace VerticalDistrict.Core.Simulation;

public sealed record RetailDemandState(string Model, long LastAttemptTick, long NextAttemptTick, long Attempts);

/// <summary>Counts cover retained order history; money covers the room's complete posted operating ledger.</summary>
public sealed record RetailPerformance(long RoomId, int RetainedOrders, int Visits, int Completed, int Abandoned,
    int Traveling, int Queued, int Serving, int ServiceSlots, double StaffUtilization, long RevenueMinor, long ExpensesMinor)
{
    public long OperatingProfitMinor => checked(RevenueMinor - ExpensesMinor);
}
