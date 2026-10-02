using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, ServiceTask> _serviceTasks = new();
    private long _nextServiceTaskId = 1;

    public IReadOnlyList<ServiceTask> ServiceTasks => _serviceTasks.Values.ToArray();
    public IReadOnlyList<ServiceStaffSummary> StaffSummaries => World.Rooms
        .Where(room => Rules.For(room.DefinitionId)?.Model == "Service").OrderBy(room => room.Id)
        .Select(room =>
        {
            var op = _operations[room.Id];
            var workers = _people.Values.Where(person => person.Role == "Staff" && person.RoomId == room.Id).ToArray();
            var accessible = IsAccessible(room.Id, true);
            return new ServiceStaffSummary(room.Id, op.Staff,
                op.Open && accessible ? Math.Max(0, op.Staff - workers.Length) : 0, workers.Length,
                workers.Count(person => person.Activity == PersonActivity.Arriving),
                workers.Count(person => person.Activity == PersonActivity.Working),
                workers.Count(person => person.Activity is PersonActivity.Returning or PersonActivity.Stranded or PersonActivity.Leaving),
                checked(op.Staff * Rules.For(room.DefinitionId)!.StaffSalaryMinor), op.Open, accessible);
        }).ToArray();

    public ServiceTask? ServiceTaskForRoom(long roomId) => _serviceTasks.Values
        .FirstOrDefault(task => task.RoomId == roomId && !ServiceTaskIsTerminal(task));
    public bool IsRoomBeingServiced(long roomId) => _serviceTasks.Values.Any(task => task.RoomId == roomId
        && task.Status == ServiceTaskStatus.InProgress);

    private static bool ServiceTaskIsTerminal(ServiceTask task)
        => task.Status is ServiceTaskStatus.Completed or ServiceTaskStatus.Cancelled;
    private static bool ServiceTaskIsClaimed(ServiceTask task)
        => task.Status is ServiceTaskStatus.Assigned or ServiceTaskStatus.Traveling or ServiceTaskStatus.InProgress;
    private bool NeedsCleaning(RoomOperation operation)
        => operation.Dirty || operation.Cleanliness < Rules.Management.ServiceCleanThreshold;
    private bool NeedsMaintenance(RoomOperation operation)
        => operation.Condition < Rules.Management.ServiceRepairThreshold;
    private ServiceTaskKind NeededServiceKind(RoomOperation operation)
        => NeedsCleaning(operation) ? NeedsMaintenance(operation) ? ServiceTaskKind.CleaningAndMaintenance : ServiceTaskKind.Cleaning
            : ServiceTaskKind.Maintenance;

    /// <summary>Discovery/dispatch cadence is logical time. A failed route leaves a visible unclaimed job.</summary>
    private void AssignServiceWork(long? requestedRoom = null)
    {
        foreach (var room in World.Rooms.OrderBy(room => room.Id))
        {
            if (requestedRoom.HasValue && requestedRoom.Value != room.Id) continue;
            if (!_operations.TryGetValue(room.Id, out var op) || !NeedsCleaning(op) && !NeedsMaintenance(op)) continue;
            if (ServiceTaskForRoom(room.Id) != null) continue;
            var id = _nextServiceTaskId;
            _nextServiceTaskId = checked(id + 1);
            _serviceTasks.Add(id, new ServiceTask(id, room.Id, NeededServiceKind(op), ServiceTaskStatus.Pending,
                null, null, Tick, Tick, null, null, "Awaiting an available service worker."));
        }

        foreach (var task in _serviceTasks.Values.Where(task => !ServiceTaskIsTerminal(task) && !ServiceTaskIsClaimed(task)
                     && (!requestedRoom.HasValue || requestedRoom.Value == task.RoomId))
                 .OrderBy(task => _operations.GetValueOrDefault(task.RoomId)?.Dirty == true ? 0 : 1)
                 .ThenBy(task => _operations.GetValueOrDefault(task.RoomId)?.Condition + _operations.GetValueOrDefault(task.RoomId)?.Cleanliness)
                 .ThenBy(task => task.Id).ToArray())
        {
            var target = Room(task.RoomId);
            if (target == null || !_operations.TryGetValue(task.RoomId, out var operation))
            { FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The serviced room was removed."); continue; }
            if (!NeedsCleaning(operation) && !NeedsMaintenance(operation))
            { FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The room no longer needs this work."); continue; }
            if (Rules.For(target.DefinitionId)?.Model == "Hotel"
                && (Occupancy(target.Id) > 0 || HotelBookingFor(target.Id) is { Status: HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn }))
            { BlockUnclaimedTask(task, "Waiting for the hotel guest to check out."); continue; }

            var depots = World.Rooms.Where(room => Rules.For(room.DefinitionId)?.Model == "Service"
                && _operations.TryGetValue(room.Id, out var op) && op.Open && op.Staff > 0 && IsAccessible(room.Id, true))
                .OrderBy(room => room.Id).ToArray();
            var available = depots.Where(depot => _people.Values.Count(person => person.Role == "Staff" && person.RoomId == depot.Id)
                < _operations[depot.Id].Staff).ToArray();
            var depot = available.FirstOrDefault(depot => Transport.CanReach(depot.Floor, depot.X, target.Floor, target.X, true));
            if (depot == null)
            {
                BlockUnclaimedTask(task, depots.Length == 0 ? "No open, staffed and accessible service depot."
                    : available.Length == 0 ? "All service workers are busy or returning."
                    : "Maintenance cannot reach this room through a service route.");
                continue;
            }
            if (_nextPersonId == long.MaxValue)
            {
                BlockUnclaimedTask(task, "No further worker identities are available.");
                continue;
            }
            var workerId = _nextPersonId;
            var route = Transport.RequestJourney(workerId, depot.Floor, depot.X, target.Floor, target.X, true, 1200);
            if (!route.Success)
            {
                Transport.CancelJourney(workerId); Transport.ForgetJourney(workerId);
                BlockUnclaimedTask(task, "The service route is temporarily unavailable."); continue;
            }
            _nextPersonId = checked(workerId + 1);
            _people.Add(workerId, new PersonState(workerId, depot.Id, "Staff", PersonActivity.Arriving, 0, Tick, 100, target.Id));
            _serviceTasks[task.Id] = task with { Kind = NeededServiceKind(operation), Status = ServiceTaskStatus.Assigned,
                WorkerPersonId = workerId, DepotRoomId = depot.Id, UpdatedTick = Tick, WorkDueTick = null,
                Reason = "Assigned; worker is departing the service depot." };
        }
        TrimServiceHistory();
    }

    private void BlockUnclaimedTask(ServiceTask task, string reason)
    {
        if (task.Status == ServiceTaskStatus.Blocked && task.Reason == reason) return;
        _serviceTasks[task.Id] = task with { Status = ServiceTaskStatus.Blocked, WorkerPersonId = null,
            DepotRoomId = null, UpdatedTick = Tick, WorkDueTick = null, FinishedTick = null, Reason = reason };
    }

    /// <summary>Staff own ordinary transport journeys; no task completion can bypass their arrival.</summary>
    private void ResolveServiceWorkers()
    {
        foreach (var worker in _people.Values.Where(person => person.Role == "Staff").ToArray())
        {
            var journey = Transport.JourneyFor(worker.Id);
            var task = _serviceTasks.Values.FirstOrDefault(task => task.WorkerPersonId == worker.Id && ServiceTaskIsClaimed(task));
            if (task == null)
            {
                if (worker.Activity == PersonActivity.Returning && journey?.State == JourneyState.Arrived
                    && Room(worker.RoomId) is { } home && journey.DestinationFloor == home.Floor && journey.DestinationX == home.X)
                {
                    CompletedTrips++;
                    Transport.CancelJourney(worker.Id); Transport.ForgetJourney(worker.Id); _people.Remove(worker.Id);
                }
                else if (worker.Activity != PersonActivity.Stranded || Tick >= worker.ActionAt)
                    ReturnServiceWorker(worker);
                continue;
            }
            var target = Room(task.RoomId); var depot = Room(worker.RoomId);
            if (target == null || depot == null || !_operations.TryGetValue(depot.Id, out var depotOperation) || !depotOperation.Open)
            {
                FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The room or service depot became unavailable.");
                ReturnServiceWorker(worker); continue;
            }
            if (journey == null || journey.State is JourneyState.Abandoned or JourneyState.Unreachable)
            {
                BlockUnclaimedTask(task, "Worker could not reach the task; awaiting an available service route.");
                RecordManagementOutcome(task.RoomId, false);
                ReturnServiceWorker(worker); continue;
            }
            if (worker.Activity == PersonActivity.Working)
            {
                if (Tick >= worker.ActionAt) CompleteService(worker, task);
                continue;
            }
            if (journey.State != JourneyState.Arrived)
            {
                if (task.Status == ServiceTaskStatus.Assigned)
                    _serviceTasks[task.Id] = task with { Status = ServiceTaskStatus.Traveling, UpdatedTick = Tick,
                        Reason = "Worker is traveling by the service transport network." };
                continue;
            }
            CompletedTrips++;
            if (Rules.For(target.DefinitionId)?.Model == "Hotel"
                && (Occupancy(target.Id) > 0 || HotelBookingFor(target.Id) is { Status: HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn }))
            {
                BlockUnclaimedTask(task, "The hotel room is occupied; work must wait for checkout.");
                ReturnServiceWorker(worker); continue;
            }
            var workDue = checked(Tick + Rules.For(depot.DefinitionId)!.ServiceSeconds);
            _people[worker.Id] = worker with { Activity = PersonActivity.Working, ActionAt = workDue };
            _serviceTasks[task.Id] = task with { Status = ServiceTaskStatus.InProgress, UpdatedTick = Tick,
                WorkDueTick = workDue, Reason = "Worker is on site; work duration is in progress." };
        }
    }

    private void CompleteService(PersonState worker, ServiceTask task)
    {
        if (!_operations.TryGetValue(task.RoomId, out var op))
        {
            FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The serviced room was removed.");
            ReturnServiceWorker(worker); return;
        }
        var clean = task.Kind is ServiceTaskKind.Cleaning or ServiceTaskKind.CleaningAndMaintenance;
        var repair = task.Kind is ServiceTaskKind.Maintenance or ServiceTaskKind.CleaningAndMaintenance;
        var postings = new List<FinancialPosting>();
        if (clean && Rules.Management.CleaningCostMinor > 0)
            postings.Add(new FinancialPosting(task.RoomId, -Rules.Management.CleaningCostMinor, "Maintenance.Cleaning",
                $"Cleaning task #{task.Id} completed by worker #{worker.Id}."));
        if (repair && Rules.RepairCostMinor > 0)
            postings.Add(new FinancialPosting(task.RoomId, -Rules.RepairCostMinor, "Maintenance.Repair",
                $"Maintenance task #{task.Id} completed by worker #{worker.Id}."));
        long cost;
        try { cost = checked((clean ? Rules.Management.CleaningCostMinor : 0) + (repair ? Rules.RepairCostMinor : 0)); }
        catch (OverflowException)
        {
            BlockUnclaimedTask(task, "Combined material costs exceed the supported monetary range; room remains unserviced.");
            RecordManagementOutcome(task.RoomId, false);
            ReturnServiceWorker(worker); return;
        }
        if (World.CashMinor < cost || !PostBatch(postings.ToArray()))
        {
            BlockUnclaimedTask(task, "Insufficient funds for the completed work's materials; room remains unserviced.");
            RecordManagementOutcome(task.RoomId, false);
            ReturnServiceWorker(worker); return;
        }
        SetOp(_operations[task.RoomId] with { Cleanliness = clean ? 100 : op.Cleanliness,
            Dirty = clean ? false : op.Dirty, Condition = repair ? 100 : op.Condition });
        FinishServiceTask(task, ServiceTaskStatus.Completed, "On-site work completed; worker is returning to the depot.");
        RecordManagementOutcome(task.RoomId, true);
        ReturnServiceWorker(worker);
    }

    private void ReturnServiceWorker(PersonState worker)
    {
        var journey = Transport.JourneyFor(worker.Id); var depot = Room(worker.RoomId);
        if (worker.Activity == PersonActivity.Returning && journey is { State: not (JourneyState.Abandoned or JourneyState.Unreachable or JourneyState.Arrived) }) return;
        if (journey?.State == JourneyState.Riding)
        {
            // Cancellation waits for a safe landing. The car retains ownership of its rider.
            _people[worker.Id] = worker with { Activity = PersonActivity.Stranded, ServiceTargetId = null,
                ActionAt = Tick + Rules.Management.ServiceRetrySeconds };
            return;
        }
        var floor = journey?.Floor ?? Room(worker.ServiceTargetId ?? worker.RoomId)?.Floor ?? 0;
        var x = (int)(journey?.X ?? Room(worker.ServiceTargetId ?? worker.RoomId)?.X ?? 0);
        if (depot == null || !Transport.CanReach(floor, x, depot.Floor, depot.X, true)
            || !Transport.CancelJourney(worker.Id).Success)
        {
            _people[worker.Id] = worker with { Activity = PersonActivity.Stranded, ServiceTargetId = null,
                ActionAt = Tick + Rules.Management.ServiceRetrySeconds };
            return;
        }
        var result = Transport.RequestJourney(worker.Id, floor, x, depot.Floor, depot.X, true, 1200);
        _people[worker.Id] = worker with { Activity = result.Success ? PersonActivity.Returning : PersonActivity.Stranded,
            ServiceTargetId = null, ActionAt = Tick + Rules.Management.ServiceRetrySeconds };
    }

    private void FinishServiceTask(ServiceTask task, ServiceTaskStatus status, string reason)
    {
        _serviceTasks[task.Id] = task with { Status = status, UpdatedTick = Tick, FinishedTick = Tick, Reason = reason };
        TrimServiceHistory();
    }

    private void CancelRoomServiceTasks(long roomId)
    {
        foreach (var task in _serviceTasks.Values.Where(task => task.RoomId == roomId && !ServiceTaskIsTerminal(task)).ToArray())
        {
            FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The serviced room was demolished.");
            if (task.WorkerPersonId is { } workerId && _people.TryGetValue(workerId, out var worker)) ReturnServiceWorker(worker);
        }
        TrimServiceHistory();
    }

    private void CancelDepotServiceTasks(long depotId)
    {
        foreach (var task in _serviceTasks.Values.Where(task => task.DepotRoomId == depotId && ServiceTaskIsClaimed(task)).ToArray())
        {
            FinishServiceTask(task, ServiceTaskStatus.Cancelled, "The manager closed the service depot; worker is returning safely.");
            if (task.WorkerPersonId is { } workerId && _people.TryGetValue(workerId, out var worker)) ReturnServiceWorker(worker);
        }
        TrimServiceHistory();
    }

    private void TrimServiceHistory()
    {
        var terminal = _serviceTasks.Values.Where(ServiceTaskIsTerminal).OrderBy(task => task.FinishedTick).ThenBy(task => task.Id).ToArray();
        foreach (var task in terminal.Take(Math.Max(0, terminal.Length - Rules.Management.ServiceHistoryLimit))) _serviceTasks.Remove(task.Id);
    }

    private void HourlyCondition()
    {
        foreach (var op in _operations.Values.ToArray())
        {
            var condition = Math.Max(0, op.Condition - Rules.Management.HourlyConditionLoss);
            var cleaningLoss = Occupancy(op.RoomId) > 0 ? Rules.Management.OccupiedHourlyCleanlinessLoss : Rules.Management.HourlyCleanlinessLoss;
            SetOp(op with { Condition = condition, Cleanliness = Math.Max(0, op.Cleanliness - cleaningLoss) });
            if (op.Condition > 35 && condition <= 35)
                Notice($"Room {op.RoomId} needs maintenance before it reaches the closure threshold (30).", "Warning");
        }
    }
}
