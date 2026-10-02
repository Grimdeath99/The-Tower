namespace VerticalDistrict.Core.Transport;

public enum CarState { Idle, Traveling, Opening, Unloading, Boarding, Closing, OutOfService }
public enum JourneyState { Walking, Waiting, Riding, Arrived, Unreachable, Abandoned }
public enum RouteKind { Walk, Stair, Elevator }

/// <summary>One car per reserved shaft. Shaft/car IDs are stable within their bank.</summary>
public sealed record ShaftDefinition(int Id, int X);
/// <summary>The primary shaft/car is ID 1 at X. All coordinated cars share stops and operating policy.</summary>
public sealed record BankDefinition(int Id, int X, int MinFloor, int MaxFloor, int[] Stops,
    int Capacity = 8, int TravelTicksPerFloor = 2, int DoorTicks = 2, bool ServiceOnly = false,
    ShaftDefinition[]? AdditionalShafts = null);
/// <summary>Direction 0 is bidirectional stairs; +1/-1 is an upward/downward escalator.</summary>
public sealed record StairDefinition(int LowerFloor, int X, int Direction = 0);
public sealed record RouteLeg(RouteKind Kind, int FromFloor, int FromX, int ToFloor, int ToX, int BankId = 0, int CarId = 0);
public sealed record BankView(BankDefinition Definition, bool IsOutOfService, int WaitingCount,
    int CarCount = 1, int AvailableCarCount = 1);
public sealed record CarView(int BankId, CarState State, int Floor, double DrawFloor, int TargetFloor,
    int Direction, int PassengerCount, int Capacity, IReadOnlyList<long> PassengerIds,
    int CarId = 1, int ShaftId = 1, int X = 0, bool IsOutOfService = false);
/// <summary>
/// Read-only presentation values. DrawFloor follows the actual car or walking leg. WaitSinceTick
/// is meaningful only while Waiting and is zero in other states; TotalWaitTicks retains completed
/// waits. NextStopFloor is the current elevator leg's stop, which can be a transfer floor.
/// </summary>
public sealed record JourneyView(long PersonId, JourneyState State, int Floor, double DrawFloor, double X,
    int DestinationFloor, int DestinationX, int? BankId, long WaitSinceTick, int TotalWaitTicks, bool Service,
    int? NextStopFloor = null, int PatienceTicks = 0, int? CarId = null, int TransferCount = 0,
    IReadOnlyList<RouteLeg>? RemainingRoute = null);
public enum RouteStatus { Reachable, TemporarilyUnavailable, AccessDenied, Disconnected }
public sealed record RouteDiagnostic(RouteStatus Status, string Reason, IReadOnlyList<RouteLeg> Route);
public sealed record TransportMetrics(double AverageWaitTicks, int P95WaitTicks, int WaitSampleCount,
    long AbandonedTrips, double Utilization, IReadOnlyList<int> OverloadedFloors, int Waiting, int Riding);

public sealed record CarSnapshot(int BankId, CarState State, int Floor, int TargetFloor, int TravelStartFloor,
    int Direction, int Timer, int Duration, long[] Passengers, int CarId = 1, bool IsOutOfService = false);
public sealed record BankSnapshot(BankDefinition Definition, bool IsOutOfService, CarSnapshot Car,
    CarSnapshot[]? AdditionalCars = null);
public sealed record JourneySnapshot(long PersonId, JourneyState State, int Floor, int X,
    int DestinationFloor, int DestinationX, bool Service, int PatienceTicks, RouteLeg[] Route,
    int LegIndex, int RemainingTicks, int LegDuration, long WaitSinceTick, int TotalWaitTicks, int RouteVersion,
    int? AssignedCarId = null, int CompletedRides = 0);
public sealed record TransportSnapshot(int SchemaVersion, long CurrentTick, int TopologyVersion,
    int ObservedWorldTopologyVersion, BankSnapshot[] Banks, StairDefinition[] Stairs,
    JourneySnapshot[] Journeys, int[] WaitSamples, long AbandonedTrips, long OccupiedCarTicks,
    long AvailableSeatTicks);
