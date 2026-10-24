using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Tests.Engine;

namespace Core.Tests.Rules
{
    /// <summary>G28: the table grid snaps drops to squares or hexes, is a host setting, and validates.</summary>
    public class GridTests
    {
        [Theory]
        [InlineData(GridSnapping.Centers, 0.07f, 0.02f, 0.075f, 0.025f)]
        [InlineData(GridSnapping.Corners, 0.07f, 0.02f, 0.05f, 0f)]
        [InlineData(GridSnapping.Both, 0.07f, 0.02f, 0.075f, 0.025f)]
        [InlineData(GridSnapping.Both, 0.098f, 0.004f, 0.1f, 0f)]
        [InlineData(GridSnapping.Off, 0.07f, 0.02f, 0.07f, 0.02f)]
        public void Square_SnapsToCentresCornersOrTheNearer(GridSnapping snapping, float x, float z, float ex, float ez)
        {
            var grid = new GridDef { SizeX = 0.05f, SizeZ = 0.05f, Snapping = snapping };
            var (sx, sz) = grid.Snap(x, z);
            Assert.Equal(ex, sx, 4);
            Assert.Equal(ez, sz, 4);
        }

        [Fact]
        public void Square_FollowsTheOffset_AndNegativeCoordinates()
        {
            var grid = new GridDef { SizeX = 0.1f, SizeZ = 0.1f, OffsetX = 0.03f, Snapping = GridSnapping.Centers };
            var (x, z) = grid.Snap(-0.01f, -0.12f);
            Assert.Equal(-0.02f, x, 4);
            Assert.Equal(-0.15f, z, 4);
        }

        [Fact]
        public void Hex_SnapsToTheNearestCentre_OddRowsShifted()
        {
            var grid = new GridDef { Shape = GridShape.Hex, SizeX = 0.04f, SizeZ = 0.046f, Snapping = GridSnapping.Centers };
            Assert.Equal((0.04f, 0f), grid.Snap(0.035f, 0.004f));
            var (x, z) = grid.Snap(0.021f, 0.033f);
            Assert.Equal(0.02f, x, 4);
            Assert.Equal(grid.HexRow, z, 4);
            // A point exactly on a centre stays there; every corner is half a cell (tip) or less from its centre.
            var c = grid.HexCenterOf(3, -2);
            Assert.Equal(c, grid.Snap(c.X, c.Z));
            Assert.All(grid.HexCorners(c.X, c.Z), k => Assert.True(System.Math.Abs(k.Z - c.Z) <= grid.SizeZ / 2f + 1e-6));
            var corner = grid.HexCorners(c.X, c.Z)[1];
            grid.Snapping = GridSnapping.Corners;
            Assert.Equal(corner.X, grid.Snap(corner.X + 0.001f, corner.Z).X, 3);
        }

        [Fact]
        public void SetGrid_IsAHostSetting_SavedWithTheTable_AndValidated()
        {
            var engine = EngineFixture.Create();
            engine.Ok(new SetGridCommand { Grid = new GridDef { Shape = GridShape.Hex, Snapping = GridSnapping.Centers, SizeX = 0.03f, SizeZ = 0.035f } });
            var json = TableJson.Serialize(engine.State);
            Assert.Contains(@"""shape"": ""hex""", json);
            Assert.Equal(GridShape.Hex, TableJson.Parse(json).State!.Grid!.Shape);
            engine.Fails(new SetGridCommand { Grid = new GridDef { SizeX = 5f } }, ErrorCodes.InvalidValue);
            engine.Fails(new SetGridCommand { Grid = new GridDef { Opacity = 2f } }, ErrorCodes.InvalidValue);
            engine.Ok(new SetGridCommand { Grid = null });
            Assert.Null(engine.State.Grid);
            Assert.True(typeof(IHostCommand).IsAssignableFrom(typeof(SetGridCommand)));
        }

        /// <summary>
        /// Table JSON skips default values; a property whose initial value is not the type's default (true, 0.35) must say
        /// so, or false and 0 are dropped on save and read back as the initial value.
        /// </summary>
        [Fact]
        public void NonDefaultInitialValues_SurviveASave()
        {
            var state = TableJson.Parse(EngineFixture.Json).State!;
            state.Grid = new GridDef { Lines = false, Opacity = 0f };
            state.Zones.Add(new LayoutZone { Id = "row", Spacing = 0f });
            var back = TableJson.Parse(TableJson.Serialize(state)).State!;
            Assert.False(back.Grid!.Lines);
            Assert.Equal(0f, back.Grid.Opacity);
            Assert.Equal(0f, back.Zones.OfType<LayoutZone>().Single().Spacing);
            var defaults = TableJson.Serialize(new TableState { Grid = new GridDef() });
            Assert.DoesNotContain("lines", defaults);
            Assert.DoesNotContain("opacity", defaults);
        }

        [Fact]
        public void ObjectsCanOptOut_AndOnlyTheOptOutIsSaved()
        {
            var engine = EngineFixture.Create();
            engine.Find("d6")!.Grid = false;
            var json = TableJson.Serialize(engine.State);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, @"""grid"": false"));
            Assert.False(TableJson.Parse(json).State!.Objects.Single(o => o.Id == "d6").Grid);
        }
    }
}
