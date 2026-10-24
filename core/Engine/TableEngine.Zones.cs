using System;
using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Engine
{
    /// <summary>Layout and randomize zones (G18).</summary>
    public sealed partial class TableEngine
    {
        /// <summary>Height above a zone's floor where arranged objects are set down (physics settles them).</summary>
        public const float ArrangeLift = 0.002f;

        private CommandResult ArrangeZone(ArrangeZoneCommand c)
        {
            if (!(State.Zones.FirstOrDefault(z => z.Id == c.ZoneId) is LayoutZone zone))
                return ZoneFail(c.ZoneId, "a layout zone");
            var events = new List<StateEvent>();
            Arrange(zone, events);
            return CommandResult.Success(events);
        }

        private CommandResult RandomizeZone(RandomizeZoneCommand c)
        {
            if (!(State.Zones.FirstOrDefault(z => z.Id == c.ZoneId) is RandomizeZone zone))
                return ZoneFail(c.ZoneId, "a randomize zone");
            var items = ObjectsIn(zone.Id).Where(o => !o.Locked).ToList();
            var events = new List<StateEvent>();
            var poses = items.Select(o => o.Transform.Position).ToList();
            ShuffleInPlace(poses);
            for (var i = 0; i < items.Count; i++)
            {
                items[i].Transform.Position = poses[i];
                events.Add(new ObjectUpdated(items[i].Id));
            }
            foreach (var item in items.Where(o => o is Deck || (o is Bag && !(o is InfiniteBag))))
            {
                var shuffled = Shuffle(new ShuffleCommand { ObjectId = item.Id });
                if (shuffled.Ok)
                    events.AddRange(shuffled.Events.OfType<Shuffled>());
            }
            RefreshZones(items, events);
            return CommandResult.Success(events);
        }

        private CommandResult ZoneFail(string id, string kind)
        {
            var known = State.Zones.FirstOrDefault(z => z.Id == id);
            return known == null
                ? CommandResult.Fail("zoneId", ErrorCodes.UnknownZone, $"Zone '{id}' does not exist.", Suggest.OneOf(id, State.Zones.Select(z => z.Id), "a zone id"))
                : CommandResult.Fail("zoneId", ErrorCodes.WrongObjectType, $"'{id}' is a {known.Type}, not {kind}.");
        }

        /// <summary>
        /// Puts the zone's unlocked objects on its grid, in reading order of where they lie now (so a re-layout keeps the
        /// order, and a dropped object goes about where it was dropped). Cells are as big as the biggest object.
        /// </summary>
        private void Arrange(LayoutZone zone, List<StateEvent> events)
        {
            var items = ObjectsIn(zone.Id).Where(o => !o.Locked).ToList();
            if (items.Count == 0)
                return;
            var width = zone.Size.X * zone.Transform.Scale.X;
            var depth = zone.Size.Z * zone.Transform.Scale.Z;
            var spacing = Math.Max(0f, zone.Spacing);
            var cellX = items.Max(o => Footprint.Of(o).X) + spacing;
            var cellZ = items.Max(o => Footprint.Of(o).Y) + spacing;
            var down = zone.Direction == LayoutDirection.DownThenRight || zone.Direction == LayoutDirection.DownThenLeft;
            var mirror = zone.Direction == LayoutDirection.LeftThenDown || zone.Direction == LayoutDirection.DownThenLeft;
            var perLine = zone.MaxPerRow > 0 ? zone.MaxPerRow
                : Math.Max(1, (int)Math.Floor(((down ? depth : width) + spacing) / (down ? cellZ : cellX) + 1e-4));

            Vec3 Local(TableObject o) => Geometry.InverseRotate(zone.Transform.Rotation, Geometry.Subtract(o.Transform.Position, zone.Transform.Position));
            float Across(Vec3 l) => mirror ? -l.X : l.X;
            var ordered = down
                ? items.OrderBy(o => (int)Math.Floor((Across(Local(o)) + width / 2f) / cellX)).ThenByDescending(o => Local(o).Z)
                : items.OrderBy(o => (int)Math.Floor((depth / 2f - Local(o).Z) / cellZ)).ThenBy(o => Across(Local(o)));
            var list = ordered.ThenBy(o => o.Id, StringComparer.Ordinal).ToList();

            var p = zone.Transform.Position;
            var floor = Math.Max(0f, p.Y - zone.Size.Y * zone.Transform.Scale.Y / 2f) + ArrangeLift;
            var moved = new List<TableObject>();
            for (var i = 0; i < list.Count; i++)
            {
                var line = i / perLine;
                var k = i % perLine;
                var col = down ? line : k;
                var row = down ? k : line;
                var x = -width / 2f + cellX / 2f + col * cellX;
                var z = depth / 2f - cellZ / 2f - row * cellZ;
                var world = Geometry.Add(p, Geometry.Rotate(zone.Transform.Rotation, new Vec3(mirror ? -x : x, 0f, z)));
                var obj = list[i];
                var r = obj.Transform.Rotation;
                var faceable = obj is Card || obj is Deck;
                var yaw = faceable ? Geometry.FacingYaw(zone.Transform.Rotation.Y) : zone.Transform.Rotation.Y;
                var rotation = zone.Facing == LayoutFacing.Keep || !faceable
                    ? new Vec3(r.X, yaw, r.Z)
                    : new Vec3(0f, yaw, zone.Facing == LayoutFacing.FaceUp ? 0f : 180f);
                var position = new Vec3(world.X, floor, world.Z);
                if (Near(obj.Transform.Position, position) && Near(r, rotation))
                    continue;
                obj.Transform.Position = position;
                obj.Transform.Rotation = rotation;
                if (!events.Any(e => e is ObjectUpdated && e.ObjectId == obj.Id))
                    events.Add(new ObjectUpdated(obj.Id));
                moved.Add(obj);
            }
            RefreshZones(moved, events);
        }

        private static bool Near(Vec3 a, Vec3 b) => Math.Abs(a.X - b.X) < 0.0005f && Math.Abs(a.Y - b.Y) < 0.0005f && Math.Abs(a.Z - b.Z) < 0.0005f;

        /// <summary>
        /// Objects moved by a zone (an arranged row can run past the zone's edge): their zone membership follows, with
        /// the enter/leave events, so every peer derives the same membership from the same positions.
        /// </summary>
        private void RefreshZones(IEnumerable<TableObject> objects, List<StateEvent> events)
        {
            foreach (var obj in objects)
            {
                var before = ZonesOf(obj.Id);
                var after = ComputeZones(obj);
                foreach (var zone in before.Where(z => !after.Contains(z)))
                    events.Add(new ZoneLeft(obj.Id, zone));
                foreach (var zone in after.Where(z => !before.Contains(z)))
                    events.Add(new ZoneEntered(obj.Id, zone));
                _zonesOf[obj.Id] = after;
            }
        }
    }
}
