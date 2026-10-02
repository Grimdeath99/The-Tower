namespace VerticalDistrict.Core.Simulation;

public enum TenancyStatus { Active, Notice, Departing, Ended }
public sealed record TenantContract(long Id, long RoomId, string Kind, long StartDay, long RenewalDay,
    long AgreedRentMinor, long[] MemberIds, long LastArrivalDay, long LastPaidDay, long LastOccupiedDay,
    long LastReviewDay, int BadDays, TenancyStatus Status, string DepartureReason, long EndedAtTick);

public enum FoodOrderStatus { Traveling, Queued, Serving, Completed, Abandoned }
public sealed record FoodOrder(long Id, long RoomId, long PersonId, long CreatedAt, long AgreedPriceMinor,
    FoodOrderStatus Status, long QueuedAt, long ServiceStartedAt, long CompletesAt, long PatienceUntil, long FinishedAt,
    string ProductId = "", int AgreedServiceSeconds = 0);

public enum HotelBookingStatus { Reserved, CheckedIn, CheckedOut, Cancelled }
public enum HotelReadiness { Available, Reserved, Occupied, Dirty, Cleaning, Maintenance, Unavailable }
public sealed record HotelBooking(long Id, long RoomId, long PersonId, long ReservedAt, long AgreedNightlyPriceMinor,
    int Nights, long CheckInDeadline, long CheckedInAt, long CheckoutAt, HotelBookingStatus Status,
    long ChargedMinor, long FinishedAt, string EndReason);
