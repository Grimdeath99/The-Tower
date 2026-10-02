namespace VerticalDistrict.Core.Simulation;

public enum CondoOwnershipStatus { PendingSale, Owned, Evacuating, Reacquired, Cancelled }
public enum CondoAvailability { Available, PendingSale, Owned, Evacuating, Unavailable }

/// <summary>One accepted purchase and its durable household. Financial terms never follow later asking-price edits.</summary>
public sealed record CondoOwnership(long Id, long RoomId, long OwnerId, long[] ResidentIds,
    long AgreedPriceMinor, long OfferedAtTick, long OfferExpiresAtTick, long? PurchasedAtTick,
    long? SaleLedgerSequence, long? ReacquiredAtTick, long? BuybackLedgerSequence,
    long LastArrivalDay, CondoOwnershipStatus Status, string EndReason);
