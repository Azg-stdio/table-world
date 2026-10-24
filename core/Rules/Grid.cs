using System;

namespace Core.Rules
{
    public enum GridShape { Square, Hex }

    /// <summary>What a drop snaps to on the grid: nothing, cell centres, cell corners (where lines cross), or the nearer of both.</summary>
    public enum GridSnapping { Off, Centers, Corners, Both }

    /// <summary>
    /// The table grid (G28, TTS Grid): lines drawn on the table and where dropped objects snap. Sizes are metres on the
    /// table; the offset moves the whole grid. Objects whose <see cref="TableObject.Grid"/> is false ignore it. A hex grid
    /// has pointy-topped cells in rows <see cref="SizeZ"/> × ¾ apart, every other row shifted by half a cell (verify
    /// against TTS's hex grid when the importer maps it).
    /// </summary>
    public sealed class GridDef
    {
        public const float MinSize = 0.005f;
        public const float MaxSize = 0.5f;

        public GridShape Shape { get; set; }
        [System.ComponentModel.DefaultValue(true)]
        public bool Lines { get; set; } = true;
        public GridSnapping Snapping { get; set; }
        [System.ComponentModel.DefaultValue(0.05f)]
        public float SizeX { get; set; } = 0.05f;
        [System.ComponentModel.DefaultValue(0.05f)]
        public float SizeZ { get; set; } = 0.05f;
        public float OffsetX { get; set; }
        public float OffsetZ { get; set; }
        /// <summary>Line colour, RGB 0–1.</summary>
        public float[] Color { get; set; } = { 0f, 0f, 0f };
        [System.ComponentModel.DefaultValue(0.35f)]
        public float Opacity { get; set; } = 0.35f;

        /// <summary>
        /// Where a drop at (<paramref name="x"/>, <paramref name="z"/>) lands under <see cref="Snapping"/>; the point
        /// itself when snapping is off.
        /// </summary>
        public (float X, float Z) Snap(float x, float z)
        {
            if (Snapping == GridSnapping.Off)
                return (x, z);
            var options = new System.Collections.Generic.List<(float X, float Z)>();
            if (Snapping != GridSnapping.Corners)
                options.Add(Shape == GridShape.Hex ? HexCenter(x, z) : SquareCenter(x, z));
            if (Snapping != GridSnapping.Centers)
                options.Add(Shape == GridShape.Hex ? HexCorner(x, z) : SquareCorner(x, z));
            var best = options[0];
            foreach (var o in options)
                if (Distance(o, x, z) < Distance(best, x, z))
                    best = o;
            return best;
        }

        private static float Distance((float X, float Z) p, float x, float z) => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);

        private (float, float) SquareCenter(float x, float z) =>
            (OffsetX + ((float)Math.Floor((x - OffsetX) / SizeX) + 0.5f) * SizeX, OffsetZ + ((float)Math.Floor((z - OffsetZ) / SizeZ) + 0.5f) * SizeZ);

        private (float, float) SquareCorner(float x, float z) =>
            (OffsetX + (float)Math.Round((x - OffsetX) / SizeX) * SizeX, OffsetZ + (float)Math.Round((z - OffsetZ) / SizeZ) * SizeZ);

        /// <summary>Distance between hex rows.</summary>
        public float HexRow => SizeZ * 0.75f;

        /// <summary>Centre of hex (column, row): odd rows shift right by half a cell.</summary>
        public (float X, float Z) HexCenterOf(int column, int row) =>
            (OffsetX + column * SizeX + ((row & 1) == 1 ? SizeX / 2f : 0f), OffsetZ + row * HexRow);

        private (float, float) HexCenter(float x, float z)
        {
            var row = (int)Math.Round((z - OffsetZ) / HexRow);
            var best = HexCenterOf(0, 0);
            var bestDistance = float.MaxValue;
            for (var r = row - 1; r <= row + 1; r++)
            {
                var shift = (r & 1) == 1 ? SizeX / 2f : 0f;
                var c = (int)Math.Round((x - OffsetX - shift) / SizeX);
                for (var cc = c - 1; cc <= c + 1; cc++)
                {
                    var p = HexCenterOf(cc, r);
                    var d = Distance(p, x, z);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = p;
                    }
                }
            }
            return best;
        }

        /// <summary>The six corners of the hex around (<paramref name="cx"/>, <paramref name="cz"/>): pointy top and bottom.</summary>
        public (float X, float Z)[] HexCorners(float cx, float cz)
        {
            var half = SizeX / 2f;
            var tip = SizeZ / 2f;
            return new[]
            {
                (cx, cz + tip), (cx + half, cz + tip / 2f), (cx + half, cz - tip / 2f),
                (cx, cz - tip), (cx - half, cz - tip / 2f), (cx - half, cz + tip / 2f),
            };
        }

        private (float, float) HexCorner(float x, float z)
        {
            var (cx, cz) = HexCenter(x, z);
            var best = (cx, cz);
            var bestDistance = float.MaxValue;
            foreach (var corner in HexCorners(cx, cz))
            {
                var d = Distance(corner, x, z);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = corner;
                }
            }
            return best;
        }
    }
}
