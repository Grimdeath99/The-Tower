using Godot;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Simulation;

namespace VerticalDistrict.Game;

public partial class Main
{
    private AcceptDialog _financesDialog = null!;
    private OptionButton _financeView = null!, _financePeriod = null!, _financeRoom = null!;
    private RichTextLabel _financeText = null!;
    private Label _financeClock = null!;
    private readonly List<long> _financeDays = new();
    private readonly List<long?> _financeRooms = new();
    private GameSession? _financeSession;
    private int _financeLedgerCount = -1;
    private int _financePendingCount = -1;
    private long _financeDay = -1;
    private bool _refreshingFinances;

    private static string FinanceMoney(long amount) => (amount < 0 ? "−$" : "$") + Math.Abs(amount / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    private void BuildFinancesDialog()
    {
        _financesDialog = Dialog("Finances — cash, operations and bills", new Vector2I(920, 740), out var body);
        _financeClock = Text("", 15, Accent); body.AddChild(_financeClock);
        var views = new HBoxContainer();
        _financeView = new OptionButton { CustomMinimumSize = new Vector2(195, 0) };
        foreach (var name in new[] { "Overview", "Transactions", "Upcoming bills" }) _financeView.AddItem(name);
        _financeView.ItemSelected += _ => RefreshFinances(true);
        views.AddChild(_financeView);
        _financePeriod = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        _financePeriod.ItemSelected += _ => RefreshFinances(true); views.AddChild(_financePeriod);
        body.AddChild(views);
        _financeRoom = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _financeRoom.ItemSelected += _ => RefreshFinances(true); body.AddChild(_financeRoom);
        _financeText = new RichTextLabel
        {
            FitContent = true, BbcodeEnabled = false, SelectionEnabled = true,
            CustomMinimumSize = new Vector2(0, 520), MouseFilter = MouseFilterEnum.Stop
        };
        body.AddChild(_financeText);
    }

    private void OpenFinances(long? roomId = null)
    {
        _financeView.Select(roomId.HasValue ? 1 : 0);
        _financeDays.Clear(); _financeRooms.Clear();
        _financesDialog.PopupCentered();
        RefreshFinances(true);
        if (roomId.HasValue)
        {
            var index = _financeRooms.IndexOf(roomId);
            if (index >= 0) _financeRoom.Select(index);
            RefreshFinances(true);
        }
    }

    private string FinanceRoomName(long id)
    {
        var room = World.Rooms.FirstOrDefault(r => r.Id == id);
        return room == null ? $"Removed facility #{id}" : $"{Catalog.Get(room.DefinitionId).Name} #{id} · floor {FloorName(room.Floor)}";
    }

    // Transport IDs and room IDs use different namespaces. A matching bank ID is not a room payment.
    private static bool IsRoomTransaction(LedgerEntry entry)
        => entry.EntityId.HasValue && (entry.Category is "Construction.Room" or "Demolition.Room"
            || entry.Category.StartsWith("Lease.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Sales.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Operations.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Condo.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Parking.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Advertising.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Maintenance.", StringComparison.Ordinal)
            || entry.Category.StartsWith("Event.", StringComparison.Ordinal));

    private static string FlowName(FinancialFlowKind kind) => kind switch
    {
        FinancialFlowKind.OperatingRevenue => "Operating revenue",
        FinancialFlowKind.OperatingExpense => "Operating expense",
        FinancialFlowKind.CapitalReceipt => "Capital receipt",
        FinancialFlowKind.CapitalSpending => "Capital spending",
        FinancialFlowKind.Financing => "Financing",
        _ => "Other cash flow"
    };

    private static string FinanceCategoryName(string category) => category switch
    {
        "Construction.Floor" => "Floor construction", "Construction.Room" => "Facility construction",
        "Demolition.Room" => "Facility salvage", "Demolition.Floor" => "Floor removal",
        "Transport.Construction" => "Transport construction", "Condo.Buyback" => "Condominium repurchase",
        "Sales.Condo" => "Condominium sale", "Lease.Office" => "Office rent", "Lease.Home" => "Residential rent",
        "Sales.Food" => "Food purchases", "Sales.Shop" => "Shop purchases", "Sales.Hotel" => "Hotel stays",
        "Sales.Cinema" => "Cinema admissions", "Sales.Event" => "Event admissions", "Parking.Departure" => "Parking stays",
        "Advertising.Contract" => "Advertising contracts", "Operations.Upkeep" => "Upkeep and wages (imported)",
        "Operations.FacilityUpkeep" => "Facility upkeep", "Operations.Wages" => "Staff wages",
        "Maintenance.Repair" => "Cleaning and maintenance", "Event.Preparation" => "Event preparation",
        "Billing.Settlement" => "Daily settlement", _ => category
    };

    private void RefreshFinances(bool force = false)
    {
        if (_financesDialog == null || !_financesDialog.Visible || _refreshingFinances) return;
        _financeClock.Text = $"{Session.ClockText} · Current cash {FinanceMoney(World.CashMinor)}";
        // Reformat only when financial state or a filter changes, not every rendered frame.
        if (!force && ReferenceEquals(_financeSession, Session) && _financeLedgerCount == World.Ledger.Count && _financeDay == Session.Day && _financePendingCount == Session.PendingBillingPeriods) return;
        _refreshingFinances = true;
        try
        {
            var chosenDay = _financeDays.Count > _financePeriod.Selected && _financePeriod.Selected >= 0 ? _financeDays[_financePeriod.Selected] : 0;
            var chosenRoom = _financeRooms.Count > _financeRoom.Selected && _financeRoom.Selected >= 0 ? _financeRooms[_financeRoom.Selected] : null;
            var history = Session.FinancialHistory;
            var current = Session.CurrentFinances;
            _financePeriod.Clear(); _financeDays.Clear();
            _financePeriod.AddItem($"Open billing day {current.Day}"); _financeDays.Add(0);
            _financePeriod.AddItem("All recorded history"); _financeDays.Add(-1);
            foreach (var closed in history.Reverse()) { _financePeriod.AddItem($"Closed Day {closed.Day}"); _financeDays.Add(closed.Day!.Value); }
            _financePeriod.Select(Math.Max(0, _financeDays.IndexOf(chosenDay)));
            _financeRoom.Clear(); _financeRooms.Clear();
            _financeRoom.AddItem("All facilities and district transactions"); _financeRooms.Add(null);
            var roomIds = World.Rooms.Select(r => r.Id).Concat(World.Ledger.Where(IsRoomTransaction).Select(e => e.EntityId!.Value)).Distinct().Order();
            foreach (var id in roomIds) { _financeRoom.AddItem(FinanceRoomName(id)); _financeRooms.Add(id); }
            _financeRoom.Select(Math.Max(0, _financeRooms.IndexOf(chosenRoom)));
            _financeRoom.Visible = _financeView.Selected != 0;
            _financePeriod.Disabled = _financeView.Selected == 2;
            chosenDay = _financeDays[_financePeriod.Selected]; chosenRoom = _financeRooms[_financeRoom.Selected];
            long? day = chosenDay == -1 ? null : chosenDay == 0 ? current.Day : chosenDay;
            var summary = day == null ? Session.LifetimeFinances : day == current.Day ? current : history.Single(r => r.Day == day);
            _financeText.Text = _financeView.Selected switch
            {
                1 => FormatFinanceTransactions(day, chosenRoom),
                2 => FormatUpcomingBills(chosenRoom),
                _ => FormatFinancialSummary(summary, history)
            };
            _financeSession = Session; _financeLedgerCount = World.Ledger.Count; _financeDay = Session.Day; _financePendingCount = Session.PendingBillingPeriods;
        }
        finally { _refreshingFinances = false; }
    }

    private string FormatFinancialSummary(FinancialSummary summary, IReadOnlyList<FinancialSummary> history)
    {
        var lines = new List<string>
        {
            summary.Day.HasValue ? $"DAY {summary.Day} · DISTRICT TOTALS" : "ALL RECORDED HISTORY · DISTRICT TOTALS",
            $"Operating revenue     {FinanceMoney(summary.OperatingRevenueMinor)}",
            $"Operating expenses   {FinanceMoney(summary.OperatingExpensesMinor)}",
            $"OPERATING RESULT    {FinanceMoney(summary.OperatingProfitMinor)}",
            "",
            $"Capital spending       {FinanceMoney(summary.CapitalSpendingMinor)}",
            $"Capital receipts         {FinanceMoney(summary.CapitalReceiptsMinor)}",
            $"Other cash flows        {FinanceMoney(summary.OtherNetMinor)}",
            $"NET CASH FLOW          {FinanceMoney(summary.NetCashFlowMinor)}",
            $"Opening cash              {FinanceMoney(summary.OpeningCashMinor)}",
            $"Closing cash               {FinanceMoney(summary.ClosingCashMinor)}",
            "Construction, salvage and condominium ownership payments are capital. They affect cash without changing operating profit.",
            "", "CATEGORY BREAKDOWN"
        };
        if (summary.FinancingNetMinor != 0) lines.Insert(9, $"Financing net              {FinanceMoney(summary.FinancingNetMinor)}");
        if (summary.Breakdown.Count == 0) lines.Add("No transactions in this period.");
        foreach (var group in summary.Breakdown)
            lines.Add($"{FinanceCategoryName(group.Category)} · {FlowName(group.Kind)}\n{FinanceMoney(group.AmountMinor)} · {group.TransactionCount} transactions");
        if (summary.ContainsLegacyTimestamps)
            lines.Add("Imported construction history retains its original command order; those entries have no invented calendar time.");
        var pending = Session.PendingBillingPeriods > 0;
        lines.AddRange(new[] { "", pending ? "OUTSTANDING DAILY BILL" : "NEXT DAILY BILL", $"Due {ClockAt(Session.Billing.NextDueTick)} · last settled day {Session.Billing.LastSettledDay}",
            $"{(pending ? "Outstanding" : "Estimated")} upkeep and wages: {FinanceMoney(-Session.UpcomingBills.Sum(b => b.AmountMinor))}",
            pending ? $"{Session.PendingBillingPeriods} billing periods await settlement. Captured amounts are saved; later staffing and price changes do not rewrite them. Check the financial warning in Reports."
                : "Full daily upkeep and current staff allocations are billed at midnight, including a partial first day. Changes before midnight update the estimate.",
            "", "CASH BALANCE HISTORY · LATEST 14 CLOSED DAYS" });
        if (history.Count == 0) lines.Add("The first day closes at midnight.");
        foreach (var closed in history.TakeLast(14).Reverse())
            lines.Add($"Day {closed.Day}: operating {FinanceMoney(closed.OperatingProfitMinor)} · net cash {FinanceMoney(closed.NetCashFlowMinor)} · closing {FinanceMoney(closed.ClosingCashMinor)}");
        return string.Join("\n", lines);
    }

    private string FormatFinanceTransactions(long? day, long? roomId)
    {
        var entries = Session.FinanceTransactions(day).Where(e => roomId == null || IsRoomTransaction(e) && e.EntityId == roomId).ToArray();
        var lines = new List<string> { $"{(day.HasValue ? "Day " + day : "All history")} · {(roomId.HasValue ? FinanceRoomName(roomId.Value) : "District ledger")}",
            $"{entries.Length} transactions · latest 100 shown. Narrow the day or facility to inspect older entries.", "" };
        if (entries.Length == 0) lines.Add("No transactions match this selection.");
        foreach (var entry in entries.TakeLast(100).Reverse())
        {
            var time = World.UsesSimulationTimestamp(entry) ? ClockAt(entry.TimestampTicks) + $":{(entry.TimestampTicks + 28500) % 60:00}" : "Imported command #" + entry.TimestampTicks;
            var related = IsRoomTransaction(entry) ? FinanceRoomName(entry.EntityId!.Value) : entry.EntityId.HasValue ? "Related transport/entity #" + entry.EntityId : "District";
            lines.Add($"#{entry.Sequence} · {time}\n{FinanceCategoryName(entry.Category)} · {FlowName(FinanceClassification.Classify(entry.Category, entry.AmountMinor))}\n{FinanceMoney(entry.AmountMinor)} · balance {FinanceMoney(entry.BalanceAfterMinor)}\n{related}\n{entry.Description}\n");
        }
        return string.Join("\n", lines);
    }

    private string FormatUpcomingBills(long? roomId)
    {
        var bills = Session.UpcomingBills.Where(b => roomId == null || b.RoomId == roomId).ToArray();
        var pending = Session.PendingBillingPeriods > 0;
        var lines = new List<string> { $"Due {ClockAt(Session.Billing.NextDueTick)} · billing day {Session.Billing.LastSettledDay + 1}",
            $"{(pending ? "Outstanding upkeep and wages" : "Estimated charge")} {FinanceMoney(-bills.Sum(b => b.AmountMinor))}",
            pending ? $"{Session.PendingBillingPeriods} frozen billing periods await payment, oldest first. These amounts survive save/load and later staffing or price changes. Referenced facilities cannot be demolished until settlement."
                : "Estimates use current rooms and staff. Full daily bills settle once at midnight. Closed facilities still incur upkeep and assigned wages. No proration is applied.",
            "Conditional rent and sales are recorded when their existing gameplay billing rules are met; they are not guaranteed future receipts.", "" };
        if (bills.Length == 0) lines.Add("No scheduled upkeep or wages for this selection.");
        foreach (var bill in bills) lines.Add($"{FinanceRoomName(bill.RoomId)}\n{FinanceCategoryName(bill.Category)}: {FinanceMoney(bill.AmountMinor)}\n");
        return string.Join("\n", lines);
    }

    private async Task VerifyFinanceInspection()
    {
        _runner.SetSpeed(0);
        var initial = Session.Serialize();
        OpenFinances();
        if (!BlocksWorldInput || !_financeText.Text.Contains("OPERATING RESULT") || !_financeText.Text.Contains(FinanceMoney(Session.CurrentFinances.CapitalSpendingMinor)))
            throw new InvalidOperationException("Finance overview omitted actual operating/capital state or failed to block world input.");
        _financeView.Select(1); RefreshFinances(true);
        if (!_financeText.Text.Contains("Facility construction")) throw new InvalidOperationException("Finance ledger omitted construction transactions.");
        _financeView.Select(2); RefreshFinances(true);
        if (!_financeText.Text.Contains("Facility upkeep")) throw new InvalidOperationException("Scheduled facility bills are not visible.");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        RefreshFinances(true);
        if (Session.Serialize() != initial) throw new InvalidOperationException("Finance panels changed authoritative state while paused.");
        _financesDialog.Hide();
        var savePath = System.IO.Path.Combine(_saveDirectory, "finance-boundary.json");
        Session.Advance(checked((int)(Session.Billing.NextDueTick - Session.Tick - 1)));
        if (!SaveTo(savePath)) throw new InvalidOperationException("Pre-billing save failed.");
        Session.Step(); var settled = Session.Serialize();
        LoadFrom(savePath); Session.Step();
        if (Session.Serialize() != settled) throw new InvalidOperationException("Billing changed after in-game save and reload.");
        OpenFinances();
        if (Session.FinancialHistory.Count == 0 || !_financeText.Text.Contains("Day 1:"))
            throw new InvalidOperationException("Finance panel did not show settled daily cash history.");
        _financePeriod.Select(_financeDays.IndexOf(1)); RefreshFinances(true);
        if (!_financeText.Text.Contains("Facility upkeep")) throw new InvalidOperationException("Closed-day bills were attributed to the wrong period.");
        _financesDialog.Hide();
    }
}
