using Godot;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

public partial class TowerCanvas
{
    private sealed record PersonVisual(PersonState Person, JourneyView Journey, Vector2 Position);

    private IEnumerable<PersonVisual> PersonVisuals()
    {
        var journeys = OwnerGame.Session.Transport.Journeys.ToDictionary(j => j.PersonId);
        // Queue spacing is cosmetic. Physical positions, capacity and dispatch remain in the core.
        var queueOffsets = new Dictionary<long, Vector2>();
        foreach (var group in journeys.Values.Where(j => j.State == JourneyState.Waiting).GroupBy(j => (j.Floor, j.BankId)))
        {
            var index = 0;
            foreach (var j in group.OrderBy(j => j.WaitSinceTick).ThenBy(j => j.PersonId))
            { queueOffsets[j.PersonId] = new Vector2(-8 * (index % 6 + 1), -5 * (index / 6 % 3)); index++; }
        }
        foreach (var person in OwnerGame.Session.People)
        {
            if (!journeys.TryGetValue(person.Id, out var journey)) continue;
            var position = new Vector2((float)((journey.X + .5) * Bay), (float)(-journey.DrawFloor * Story - 7));
            if (person.Activity == PersonActivity.Visiting)
            {
                var room = OwnerGame.World.Rooms.FirstOrDefault(r => r.Id == person.RoomId);
                if (room != null) position.X = room.X * Bay + 12 + (person.Id % Math.Max(1, OwnerGame.Catalog.Get(room.DefinitionId).Width - 1)) * Bay;
            }
            if (queueOffsets.TryGetValue(person.Id, out var offset)) position += offset;
            yield return new PersonVisual(person, journey, position);
        }
    }

    public Vector2? PersonScreenPosition(long id)
    {
        var visual = PersonVisuals().FirstOrDefault(p => p.Person.Id == id);
        return visual == null ? null : Origin + visual.Position * Zoom;
    }

    public Vector2? BankScreenPosition(int id)
    {
        var car = OwnerGame.Session.Transport.Cars.FirstOrDefault(c => c.BankId == id);
        var bank = OwnerGame.Session.Transport.Banks.FirstOrDefault(b => b.Definition.Id == id);
        return car == null || bank == null ? null : Origin + new Vector2((bank.Definition.X + .5f) * Bay, (float)(-(car.DrawFloor + .5) * Story)) * Zoom;
    }

    public void FocusPerson(long id)
    {
        var view = OwnerGame.Session.InspectPerson(id);
        if (view?.Journey is { State: JourneyState.Riding, BankId: { } bank }) { FocusBank(bank); return; }
        if (PersonScreenPosition(id) is { } position) Pan(new Vector2(Size.X * .5f, Size.Y * .6f) - position);
    }

    public void FocusBank(int id)
    { if (BankScreenPosition(id) is { } position) Pan(new Vector2(Size.X * .5f, Size.Y * .6f) - position); }

    private bool TryInspectAt(Vector2 point)
    {
        var person = PersonVisuals().Where(p => p.Journey.State != JourneyState.Riding)
            .Select(p => (p.Person.Id, Point: Origin + (p.Position + new Vector2(0, -9)) * Zoom))
            .Where(p => Math.Abs(p.Point.X - point.X) <= Math.Max(6, 7 * Zoom) && Math.Abs(p.Point.Y - point.Y) <= Math.Max(9, 14 * Zoom))
            .OrderBy(p => p.Point.DistanceSquaredTo(point)).ThenBy(p => p.Id).FirstOrDefault();
        if (person.Id > 0) { OwnerGame.SelectPerson(person.Id); return true; }
        foreach (var car in OwnerGame.Session.Transport.Cars)
        {
            if (BankScreenPosition(car.BankId) is not { } position) continue;
            if (new Rect2(position - new Vector2(12, 26) * Zoom, new Vector2(24, 52) * Zoom).HasPoint(point))
            { OwnerGame.SelectBank(car.BankId); return true; }
        }
        var cell = Cell(point);
        var shaft = OwnerGame.Session.Transport.Banks.FirstOrDefault(b => b.Definition.X == cell.X && cell.Floor >= b.Definition.MinFloor && cell.Floor <= b.Definition.MaxFloor);
        if (shaft != null) { OwnerGame.SelectBank(shaft.Definition.Id); return true; }
        return false;
    }
}
