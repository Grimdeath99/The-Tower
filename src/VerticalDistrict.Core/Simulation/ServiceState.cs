namespace VerticalDistrict.Core.Simulation;

public enum ServiceTaskKind { Cleaning, Maintenance, CleaningAndMaintenance }
public enum ServiceTaskStatus { Pending, Assigned, Traveling, InProgress, Completed, Blocked, Cancelled }

/// <summary>One authoritative job. Terminal entries retain historical references, not live ownership.</summary>
public sealed record ServiceTask(long Id, long RoomId, ServiceTaskKind Kind, ServiceTaskStatus Status,
    long? WorkerPersonId, long? DepotRoomId, long CreatedTick, long UpdatedTick, long? WorkDueTick,
    long? FinishedTick, string Reason);

public sealed record ServiceStaffSummary(long DepotRoomId, int Hired, int Available, int Dispatched,
    int Traveling, int Working, int ReturningOrBlocked, long DailyWagesMinor, bool Open, bool Accessible);
