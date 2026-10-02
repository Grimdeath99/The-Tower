namespace VerticalDistrict.Core.Simulation;

public enum FinancialFlowKind { OperatingRevenue, OperatingExpense, CapitalReceipt, CapitalSpending, Financing, Other }
public sealed record FinancialCategoryTotal(string Category, FinancialFlowKind Kind, long AmountMinor, int TransactionCount);
public sealed record FinancialSummary(long? Day, long OpeningCashMinor, long ClosingCashMinor,
    long OperatingRevenueMinor, long OperatingExpensesMinor, long CapitalSpendingMinor, long CapitalReceiptsMinor,
    long FinancingNetMinor, long OtherNetMinor, long NetCashFlowMinor,
    IReadOnlyList<FinancialCategoryTotal> Breakdown, bool ContainsLegacyTimestamps)
{
    public long OperatingProfitMinor => checked(OperatingRevenueMinor - OperatingExpensesMinor);
}
public sealed record BillingSchedule(long LastSettledDay, long LastSettledTick, long NextDueTick);
public sealed record ScheduledBill(long RoomId, string Category, long AmountMinor, long BillingDay, long DueTick);
public sealed record FinancialPeriod(long Day, long FirstSequence, long LastSequence);
public sealed record BillingObligation(long? RoomId, long AmountMinor, string Category, string Description, long? TenantId = null);
public sealed record PendingBillingBatch(long Day, long DueTick, BillingObligation[] Obligations);
public sealed record FinanceSnapshot(BillingSchedule Billing, long CurrentPeriodFirstSequence,
    FinancialPeriod[] Periods, long LegacyThroughSequence, PendingBillingBatch[] PendingBatches);

/// <summary>Known cash categories have explicit roles; arbitrary external entries never become profit.</summary>
public static class FinanceClassification
{
    public static FinancialFlowKind Classify(string category, long amountMinor) => category switch
    {
        "Construction.Floor" or "Construction.Room" or "Transport.Construction" or "Condo.Buyback"
            when amountMinor <= 0 => FinancialFlowKind.CapitalSpending,
        "Demolition.Floor" or "Demolition.Room" or "Sales.Condo"
            when amountMinor >= 0 => FinancialFlowKind.CapitalReceipt,
        "Lease.Office" or "Lease.Home" or "Sales.Food" or "Sales.Shop" or "Sales.Hotel" or "Sales.Cinema"
            or "Sales.Event" or "Parking.Departure" or "Advertising.Contract"
            when amountMinor >= 0 => FinancialFlowKind.OperatingRevenue,
        "Operations.Upkeep" or "Operations.FacilityUpkeep" or "Operations.Wages" or "Maintenance.Repair" or "Maintenance.Cleaning"
            or "Event.Preparation" when amountMinor <= 0 => FinancialFlowKind.OperatingExpense,
        _ => FinancialFlowKind.Other
    };

    internal static bool IsBusinessPosting(string category) => category is "Condo.Buyback" or "Sales.Condo"
        or "Lease.Office" or "Lease.Home" or "Sales.Food" or "Sales.Shop" or "Sales.Hotel" or "Sales.Cinema"
        or "Sales.Event" or "Parking.Departure" or "Advertising.Contract" or "Operations.Upkeep"
        or "Operations.FacilityUpkeep" or "Operations.Wages" or "Maintenance.Repair" or "Maintenance.Cleaning" or "Event.Preparation";
}

public sealed partial class GameSession
{
    private const long FirstBillingTick = 86400 - 28500;
    private BillingSchedule _billing = new(0, 0, FirstBillingTick);
    private long _currentPeriodFirstSequence = 1;
    private long _legacyFinanceThroughSequence;
    private readonly List<FinancialPeriod> _financialPeriods = new();
    private readonly List<PendingBillingBatch> _pendingBilling = new();
    public BillingSchedule Billing => _billing;
    public int PendingBillingPeriods => _pendingBilling.Count;
    public FinancialSummary CurrentFinances => Summarize(_billing.LastSettledDay + 1,
        _currentPeriodFirstSequence, World.Ledger.Count);
    public FinancialSummary LifetimeFinances => Summarize(null, 1, World.Ledger.Count);
    public IReadOnlyList<FinancialSummary> FinancialHistory => _financialPeriods
        .Select(p => Summarize(p.Day, p.FirstSequence, p.LastSequence)).ToArray();

    /// <summary>Full-day estimates become immutable obligations at midnight, without proration.</summary>
    public IReadOnlyList<ScheduledBill> UpcomingBills => _pendingBilling.Count > 0
        ? _pendingBilling[0].Obligations.Where(o => o.RoomId.HasValue && o.Category is "Operations.FacilityUpkeep" or "Operations.Wages")
            .Select(o => new ScheduledBill(o.RoomId!.Value, o.Category, o.AmountMinor, _pendingBilling[0].Day, _pendingBilling[0].DueTick)).ToArray()
        : EstimateBills(_billing.LastSettledDay + 1, _billing.NextDueTick);

    private ScheduledBill[] EstimateBills(long day, long dueTick) => World.Rooms.OrderBy(r => r.Id)
        .Where(r => _operations.ContainsKey(r.Id) && Rules.For(r.DefinitionId) != null)
        .SelectMany(r => new[]
        {
            new ScheduledBill(r.Id, "Operations.FacilityUpkeep", -Rules.For(r.DefinitionId)!.UpkeepMinor,
                day, dueTick),
            new ScheduledBill(r.Id, "Operations.Wages", checked(-Rules.For(r.DefinitionId)!.StaffSalaryMinor * _operations[r.Id].Staff),
                day, dueTick)
        }).Where(b => b.AmountMinor != 0).ToArray();

    private PendingBillingBatch CaptureBillingBatch(long day, long dueTick)
    {
        var obligations = EstimateBills(day, dueTick).Select(b => new BillingObligation(b.RoomId, b.AmountMinor, b.Category,
            $"Day {day}: {(b.Category == "Operations.Wages" ? "staff wages at the midnight allocation" : "facility utilities and upkeep")}.")).ToList();
        foreach (var room in World.Rooms.OrderBy(r => r.Id))
        {
            var rule = Rules.For(room.DefinitionId); var op = OperationFor(room.Id);
            if (rule == null || op == null) continue;
            if (rule.Model == "Home" && HomeRentObligation(room.Id, day) is { } rent)
                obligations.Add(rent);
            if (rule.Model == "Advertising" && Ready(room, op, rule, false) && PeakPopulation >= 4)
                obligations.Add(new BillingObligation(room.Id, op.PriceMinor, "Advertising.Contract", $"Day {day}: billboard contract with occupied tower audience."));
        }
        obligations.Add(new BillingObligation(null, 0, "Billing.Settlement", $"Day {day}: recurring billing settled."));
        return new PendingBillingBatch(day, dueTick, obligations.ToArray());
    }

    public IReadOnlyList<LedgerEntry> FinanceTransactions(long? day = null)
    {
        if (day == null) return World.Ledger;
        if (day == _billing.LastSettledDay + 1)
            return Entries(_currentPeriodFirstSequence, World.Ledger.Count).ToArray();
        var period = _financialPeriods.FirstOrDefault(p => p.Day == day);
        return period == null ? Array.Empty<LedgerEntry>() : Entries(period.FirstSequence, period.LastSequence).ToArray();
    }

    private IEnumerable<LedgerEntry> Entries(long first, long last) => World.Ledger
        .Skip(checked((int)(first - 1))).Take(checked((int)Math.Max(0, last - first + 1)));

    private FinancialSummary Summarize(long? day, long first, long last)
    {
        var opening = first <= 1 ? World.StartingCashMinor : World.Ledger[checked((int)first - 2)].BalanceAfterMinor;
        var closing = last < first ? opening : World.Ledger[checked((int)last - 1)].BalanceAfterMinor;
        var totals = Entries(first, last).GroupBy(e => (e.Category, Kind: FinanceClassification.Classify(e.Category, e.AmountMinor)))
            .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Category, StringComparer.Ordinal)
            .Select(g => new FinancialCategoryTotal(g.Key.Category, g.Key.Kind, g.Aggregate(0L, (sum, e) => checked(sum + e.AmountMinor)), g.Count())).ToArray();
        long Total(FinancialFlowKind kind) => totals.Where(t => t.Kind == kind).Aggregate(0L, (sum, t) => checked(sum + t.AmountMinor));
        return new FinancialSummary(day, opening, closing, Total(FinancialFlowKind.OperatingRevenue),
            checked(-Total(FinancialFlowKind.OperatingExpense)), checked(-Total(FinancialFlowKind.CapitalSpending)),
            Total(FinancialFlowKind.CapitalReceipt), Total(FinancialFlowKind.Financing), Total(FinancialFlowKind.Other),
            checked(closing - opening), totals, Entries(first, last).Any(e => !World.UsesSimulationTimestamp(e)));
    }

    private sealed record FinancialPosting(long? RoomId, long Amount, string Category, string Description);

    // Validate the entire batch and every dependent cache before the first ledger mutation.
    private bool PostBatch(IReadOnlyList<FinancialPosting> postings)
    {
        var revenue = _todayRevenue; var expenses = _todayExpenses; var cash = World.CashMinor;
        var changed = new Dictionary<long, RoomOperation>();
        try
        {
            _ = checked(World.Ledger.Count + postings.Count);
            foreach (var posting in postings)
            {
                cash = checked(cash + posting.Amount);
                var kind = FinanceClassification.Classify(posting.Category, posting.Amount);
                if (kind == FinancialFlowKind.OperatingRevenue) revenue = checked(revenue + posting.Amount);
                if (kind == FinancialFlowKind.OperatingExpense) expenses = checked(expenses - posting.Amount);
                if (posting.RoomId is not { } roomId) continue;
                if (changed.GetValueOrDefault(roomId) is not { } op && !_operations.TryGetValue(roomId, out op))
                    throw new InvalidOperationException("A financial posting requires an existing room operation.");
                changed[roomId] = posting.Amount >= 0
                    ? op with { GrossRevenueMinor = checked(op.GrossRevenueMinor + posting.Amount) }
                    : op with { CostsMinor = checked(op.CostsMinor - posting.Amount) };
            }
        }
        catch (OverflowException)
        {
            Notice("The transaction would exceed the supported financial range; no money or operating totals changed.", "Warning");
            return false;
        }
        foreach (var posting in postings)
        {
            var result = World.ApplyOperatingTransaction(posting.Amount, Tick, posting.Category, posting.RoomId, posting.Description);
            if (!result.Success) throw new InvalidOperationException("A prevalidated financial posting failed: " + result.Message);
        }
        _todayRevenue = revenue; _todayExpenses = expenses;
        foreach (var operation in changed.Values) SetOp(operation);
        return true;
    }

    private void RecordFinancialClose(long closedDay)
    {
        _financialPeriods.Add(new FinancialPeriod(closedDay, _currentPeriodFirstSequence, World.Ledger.Count));
        if (_financialPeriods.Count > 90) _financialPeriods.RemoveAt(0);
        _currentPeriodFirstSequence = checked(World.Ledger.Count + 1L);
        _billing = new BillingSchedule(closedDay, Tick, checked(_billing.NextDueTick + 86400));
    }
}
