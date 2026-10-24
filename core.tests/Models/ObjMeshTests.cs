using System.Linq;
using Core.Models;

namespace Core.Tests.Models
{
    /// <summary>OBJ meshes (TTS Custom_Model): faces, shared corners, negative indices, left-handed conversion, limits.</summary>
    public class ObjMeshTests
    {
        private const string Quad = @"# a unit square in the XZ plane, facing up
v 0 0 0
v 1 0 0
v 1 0 1
v 0 0 1
vt 0 0
vt 1 0
vt 1 1
vt 0 1
vn 0 1 0
f 1/1/1 2/2/1 3/3/1 4/4/1
";

        [Fact]
        public void Quad_BecomesTwoTriangles_MirroredOnX_WithItsWindingReversed()
        {
            var mesh = ObjMesh.Parse(Quad, out var error)!;
            Assert.Null(error);
            Assert.Equal(4, mesh.Positions.Count);
            Assert.Equal(2, mesh.TriangleCount);
            Assert.Equal(-1f, mesh.Positions[1].X);
            Assert.Equal(new[] { 0, 2, 1, 0, 3, 2 }, mesh.Triangles);
            Assert.Equal(1f, mesh.Normals[0].Y);
            Assert.Equal(1f, mesh.Uvs[2].X);
        }

        [Fact]
        public void CornersAreShared_OnlyWhenPositionUvAndNormalAllMatch()
        {
            const string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nvt 0 0\nvt 1 1\nf 1/1 2/1 3/1\nf 2/1 4/1 3/1\nf 1/2 2/1 3/1\n";
            var mesh = ObjMesh.Parse(obj, out _)!;
            Assert.Equal(5, mesh.Positions.Count); // four shared corners plus one with another uv
            Assert.Empty(mesh.Normals);
        }

        [Fact]
        public void NegativeIndices_CommentsAndOtherStatements_Work()
        {
            const string obj = "mtllib x.mtl\no thing\ng group\nusemtl red\ns 1\nv 0 0 0 # first\nv 1 0 0\nv 0 0 1\nf -3 -2 -1\n";
            var mesh = ObjMesh.Parse(obj, out var error)!;
            Assert.Null(error);
            Assert.Equal(1, mesh.TriangleCount);
        }

        [Theory]
        [InlineData("", "no faces")]
        [InlineData("v 0 0\nf 1 1 1", "three numbers")]
        [InlineData("v 0 0 0\nv 1 0 0\nf 1 2", "three corners")]
        [InlineData("v 0 0 0\nf 1 2 3", "points at no vertex")]
        [InlineData("v 0 0 NaN\n", "three numbers")]
        public void BadFiles_AreRefusedWithAReason(string obj, string reason)
        {
            Assert.Null(ObjMesh.Parse(obj, out var error));
            Assert.Contains(reason, error);
        }

        [Fact]
        public void Polygons_AreFanned()
        {
            var pentagon = "v 0 0 0\nv 1 0 0\nv 1.5 0 1\nv 0.5 0 2\nv -0.5 0 1\nf 1 2 3 4 5\n";
            var mesh = ObjMesh.Parse(pentagon, out _)!;
            Assert.Equal(3, mesh.TriangleCount);
            Assert.True(mesh.Triangles.All(i => i >= 0 && i < 5));
        }
    }
}
