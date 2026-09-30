namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private void RestoreFinances(FinanceSnapshot? saved, bool legacy)
    {
        if (legacy)
        {
            MigrateLegacyFinances();
            return;
        }
        Require(saved != null && saved.Billing != null && saved.Periods != null && saved.PendingBatches != null,
            "Missing billing schedule, pending obligations, or financial periods.");
        _billing = saved!.Billing;
        _currentPeriodFirstSequence = saved.CurrentPeriodFirstSequence;
        _legacyFinanceThroughSequence = saved.LegacyThroughSequence;
        Require(_legacyFinanceThroughSequence >= 0 && _legacyFinanceThroughSequence <= World.Ledger.Count,
            "Invalid legacy financial history boundary.");
        Require(_billing.LastSettledDay >= 0 && _billing.LastSettledDay < Day
            && (_billing.LastSettledDay == 0 ? _billing.LastSettledTick == 0 : _billing.LastSettledTick >= checked(FirstBillingTick + (_billing.LastSettledDay - 1) * 86400))
            && _billing.LastSettledTick <= Tick
            && _billing.NextDueTick == checked(FirstBillingTick + _billing.LastSettledDay * 86400), "Invalid recurring billing schedule.");
        Require(saved.Periods.Length == Math.Min(90, _billing.LastSettledDay)
            && saved.Periods.Length == _reports.Count, "Financial periods and daily reports disagree.");
        long previousEnd = 0;
        for (var index = 0; index < saved.Periods.Length; index++)
        {
            var period = saved.Periods[index];
            Require(period != null && period.Day == _billing.LastSettledDay - saved.Periods.Length + index + 1
                && period.Day == _reports[index].Day && period.FirstSequence >= 1
                && period.LastSequence >= period.FirstSequence - 1 && period.LastSequence <= World.Ledger.Count,
                "Invalid financial period or ledger boundary.");
            Require(index == 0 ? period!.Day != 1 || period.FirstSequence == 1 : period!.FirstSequence == previousEnd + 1,
                "Financial ledger periods must be contiguous.");
            previousEnd = period!.LastSequence;
            _financialPeriods.Add(period);
        }
        Require(_currentPeriodFirstSequence >= 1 && _currentPeriodFirstSequence <= World.Ledger.Count + 1L
            && _currentPeriodFirstSequence == (_financialPeriods.Count == 0 ? 1 : previousEnd + 1), "Invalid current financial period boundary.");
        ValidateFinancialEntries();
        RestorePendingBilling(saved.PendingBatches);
        ValidateFinancialCaches(false);
    }

    private void RestorePendingBilling(PendingBillingBatch[] batches)
    {
        Require(batches.LongLength == Day - 1 - _billing.LastSettledDay,
            "Outstanding billing periods are missing or repeated.");
        for (var index = 0; index < batches.Length; index++)
        {
            var batch = batches[index];
            Require(batch != null && batch.Day == _billing.LastSettledDay + index + 1
                && batch.DueTick == checked(FirstBillingTick + (batch.Day - 1) * 86400) && batch.DueTick <= Tick
                && batch.Obligations != null && batch.Obligations.Length >= 1
                && batch.Obligations.Length <= checked(World.Rooms.Count * 3 + 1), "Invalid outstanding billing period.");
            var identities = new HashSet<(long?, string)>();
            for (var obligationIndex = 0; obligationIndex < batch.Obligations.Length; obligationIndex++)
            {
                var obligation = batch.Obligations[obligationIndex];
                Require(obligation != null && !string.IsNullOrWhiteSpace(obligation.Description)
                    && identities.Add((obligation.RoomId, obligation.Category)), "Missing or repeated billing obligation.");
                if (obligationIndex == batch.Obligations.Length - 1)
                {
                    Require(obligation is { RoomId: null, AmountMinor: 0, Category: "Billing.Settlement" }
                        && obligation.Description == $"Day {batch.Day}: recurring billing settled.", "Missing pending settlement marker.");
                    continue;
                }
                Require(obligation.RoomId.HasValue && Room(obligation.RoomId.Value) is { }, "A pending bill references a missing room.");
                var room = Room(obligation.RoomId.Value)!;
                var rule = Rules.For(room.DefinitionId)!;
                var valid = obligation.Category switch
                {
                    "Operations.FacilityUpkeep" => rule.UpkeepMinor > 0 && obligation.AmountMinor == -rule.UpkeepMinor,
                    "Operations.Wages" => rule.StaffSalaryMinor > 0 && obligation.AmountMinor < 0
                        && obligation.AmountMinor >= checked(-20 * rule.StaffSalaryMinor)
                        && obligation.AmountMinor % rule.StaffSalaryMinor == 0,
                    "Lease.Home" => rule.Model == "Home" && obligation.AmountMinor is >= 0 and <= 100_000_000,
                    "Advertising.Contract" => rule.Model == "Advertising" && obligation.AmountMinor is >= 0 and <= 100_000_000,
                    _ => false
                };
                Require(valid, "A frozen bill has an invalid category, amount, or facility model.");
                var construction = World.Ledger.Single(e => e.Category == "Construction.Room" && e.EntityId == room.Id);
                Require(!World.UsesSimulationTimestamp(construction) || construction.TimestampTicks <= batch.DueTick,
                    "A pending bill predates its room.");
            }
            _pendingBilling.Add(batch with { Obligations = batch.Obligations.ToArray() });
        }
    }

    private void ValidateFinancialEntries()
    {
        var built = new HashSet<long>();
        foreach (var entry in World.Ledger)
        {
            if (entry.Category == "Construction.Room" && entry.EntityId.HasValue) built.Add(entry.EntityId.Value);
            if (FinanceClassification.IsBusinessPosting(entry.Category))
            {
                Require(entry.EntityId.HasValue && built.Contains(entry.EntityId.Value)
                    && FinanceClassification.Classify(entry.Category, entry.AmountMinor) != FinancialFlowKind.Other,
                    "A business ledger entry has an invalid room or cash direction.");
                Require(entry.Category != "Operations.Upkeep" || entry.Sequence <= _legacyFinanceThroughSequence,
                    "New bills must identify wages and facility upkeep separately.");
                Require(entry.Category is not ("Operations.FacilityUpkeep" or "Operations.Wages")
                    || entry.Sequence < _currentPeriodFirstSequence, "A recurring expense has no settled billing period.");
            }
            if (entry.Category == "Demolition.Room" && entry.EntityId.HasValue) built.Remove(entry.EntityId.Value);
        }
        long previousCloseTick = _financialPeriods.Count == 0 || _financialPeriods[0].Day == 1
            ? 0 : checked(FirstBillingTick + (_financialPeriods[0].Day - 2) * 86400);
        foreach (var period in _financialPeriods)
        {
            var endTick = checked(FirstBillingTick + (period.Day - 1) * 86400);
            var last = period.LastSequence >= period.FirstSequence ? World.Ledger[checked((int)period.LastSequence - 1)] : null;
            var modern = period.LastSequence > _legacyFinanceThroughSequence;
            if (modern)
            {
                Require(last is { Category: "Billing.Settlement", AmountMinor: 0, EntityId: null }
                    && last.Description == $"Day {period.Day}: recurring billing settled."
                    && last.TimestampTicks >= endTick && last.TimestampTicks <= Tick,
                    "A financial period must end at its recorded billing settlement.");
                endTick = last.TimestampTicks;
                Require(Entries(period.FirstSequence, period.LastSequence).Count(e => e.Category == "Billing.Settlement") == 1,
                    "A financial period contains repeated settlement markers.");
            }
            foreach (var entry in Entries(period.FirstSequence, period.LastSequence))
                Require(!World.UsesSimulationTimestamp(entry) || entry.TimestampTicks >= previousCloseTick && entry.TimestampTicks <= endTick,
                    "Financial period contains an entry outside its settlement boundaries.");
            var newBills = Entries(period.FirstSequence, period.LastSequence)
                .Where(e => e.Category is "Operations.FacilityUpkeep" or "Operations.Wages").ToArray();
            Require(newBills.All(e => e.TimestampTicks >= endTick)
                && newBills.Select(e => (e.EntityId, e.Category)).Distinct().Count() == newBills.Length,
                "A daily expense was repeated or charged before its billing boundary.");
            previousCloseTick = endTick;
        }
        Require(_financialPeriods.Count == 0 || _billing.LastSettledTick == previousCloseTick,
            "The billing schedule disagrees with its latest settlement.");
        foreach (var entry in Entries(_currentPeriodFirstSequence, World.Ledger.Count))
            Require(entry.Category != "Billing.Settlement"
                && (!World.UsesSimulationTimestamp(entry) || entry.TimestampTicks >= _billing.LastSettledTick),
                "Current financial period contains already settled history.");
    }

    private void ValidateFinancialCaches(bool legacy)
    {
        (long Revenue, long Expenses) Totals(IEnumerable<LedgerEntry> entries)
        {
            long revenue = 0, expenses = 0;
            foreach (var entry in entries)
            {
                var kind = FinanceClassification.Classify(entry.Category, entry.AmountMinor);
                if (legacy ? FinanceClassification.IsBusinessPosting(entry.Category) && entry.AmountMinor >= 0 : kind == FinancialFlowKind.OperatingRevenue)
                    revenue = checked(revenue + entry.AmountMinor);
                if (legacy ? FinanceClassification.IsBusinessPosting(entry.Category) && entry.AmountMinor < 0 : kind == FinancialFlowKind.OperatingExpense)
                    expenses = checked(expenses - entry.AmountMinor);
            }
            return (revenue, expenses);
        }
        var today = Totals(Entries(_currentPeriodFirstSequence, World.Ledger.Count));
        Require(_todayRevenue == today.Revenue && _todayExpenses == today.Expenses,
            "Current operating totals do not reconcile with the financial ledger.");
        foreach (var period in _financialPeriods)
        {
            var report = _reports.Single(r => r.Day == period.Day);
            var totals = Totals(Entries(period.FirstSequence, period.LastSequence));
            Require(report.RevenueMinor == totals.Revenue && report.ExpensesMinor == totals.Expenses,
                "A daily financial report does not reconcile with the ledger.");
        }
        var roomTotals = World.Ledger.Where(e => e.EntityId.HasValue && FinanceClassification.IsBusinessPosting(e.Category))
            .GroupBy(e => e.EntityId!.Value).ToDictionary(g => g.Key, g => (
                Revenue: g.Where(e => e.AmountMinor >= 0).Aggregate(0L, (sum, e) => checked(sum + e.AmountMinor)),
                Expenses: g.Where(e => e.AmountMinor < 0).Aggregate(0L, (sum, e) => checked(sum - e.AmountMinor))));
        foreach (var op in _operations.Values)
        {
            var totals = roomTotals.GetValueOrDefault(op.RoomId);
            Require(op.GrossRevenueMinor == totals.Revenue && op.CostsMinor == totals.Expenses,
                "Room cash totals do not reconcile with the financial ledger.");
            Require(op.LastRentDay <= Day, "A room's latest rent period is in the future.");
        }
        // Make all supported projections safe before a candidate replaces the active session.
        _ = LifetimeFinances;
        _ = CurrentFinances;
        _ = FinancialHistory;
    }

    private void MigrateLegacyFinances()
    {
        var lastDay = Day - 1;
        Require(_reports.Count == Math.Min(90, lastDay), "Legacy daily report history is incomplete.");
        Require(_reports.Select((report, index) => report.Day == lastDay - _reports.Count + index + 1).All(valid => valid),
            "Legacy report days are not consecutive.");
        long previousEnd = 0;
        for (long day = 1; day <= lastDay; day++)
        {
            var boundary = checked(FirstBillingTick + (day - 1) * 86400);
            // Old CloseDay posted combined upkeep and eligible lease/ad receipts before resetting
            // the counters. Commands issued after that tick belong to the following day.
            var closing = World.Ledger.Where(e => e.TimestampTicks == boundary && e.Category is
                "Operations.Upkeep" or "Lease.Home" or "Advertising.Contract" or "Operations.FacilityUpkeep" or "Operations.Wages")
                .Select(e => e.Sequence).DefaultIfEmpty(0).Max();
            if (closing == 0)
                closing = World.Ledger.Where(e => World.UsesSimulationTimestamp(e) && e.TimestampTicks < boundary)
                    .Select(e => e.Sequence).DefaultIfEmpty(previousEnd).Max();
            if (day > lastDay - 90) _financialPeriods.Add(new FinancialPeriod(day, previousEnd + 1, closing));
            previousEnd = closing;
        }
        _currentPeriodFirstSequence = previousEnd + 1;
        _billing = new BillingSchedule(lastDay, lastDay == 0 ? 0 : FirstBillingTick + (lastDay - 1) * 86400,
            checked(FirstBillingTick + lastDay * 86400));
        _legacyFinanceThroughSequence = World.Ledger.Count;
        // Reject invented legacy counters rather than silently converting corrupt data.
        ValidateFinancialCaches(true);
        var current = CurrentFinances;
        _todayRevenue = current.OperatingRevenueMinor;
        _todayExpenses = current.OperatingExpensesMinor;
        for (var index = 0; index < _reports.Count; index++)
        {
            var period = _financialPeriods[index];
            Require(period.Day == _reports[index].Day, "Legacy report days are not consecutive.");
            var summary = Summarize(period.Day, period.FirstSequence, period.LastSequence);
            _reports[index] = _reports[index] with { RevenueMinor = summary.OperatingRevenueMinor, ExpensesMinor = summary.OperatingExpensesMinor };
        }
        ValidateFinancialEntries();
        ValidateFinancialCaches(false);
    }
}
