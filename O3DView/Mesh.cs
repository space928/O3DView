using O3DParse;
using O3DParse.Ini;
using Silk.NET.OpenGL;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace O3DView
{
    public class Mesh : SceneObject
    {
        public readonly List<SubMesh> submeshes;
        public bool visible = true;
        public int VertCount => nverts;
        public int TriCount => ntris;

        public Bounds Bounds => objBounds;

        protected Bounds meshBounds;
        protected Bounds objBounds;
        protected int nverts = 0;
        protected int ntris = 0;

        protected static int meshNo = 0;

        public Mesh()
        {
            transform = new();
            submeshes = [];
            name = $"Mesh_{meshNo++}";
            transform.TransformChanged += _ => ComputeBounds();
        }

        public struct SubMesh
        {
            public VertexArrayObject<float, uint> vao;
            public Material mat;
            public uint vertCount;
        }

        public void ComputeBounds()
        {
            (var min, var max) = meshBounds.ToMinMax();
            min = Vector3.Transform(min, transform.Matrix);
            max = Vector3.Transform(max, transform.Matrix);
            objBounds = Bounds.FromMinMax(min, max);
        }
    }

    public struct Bounds
    {
        public Vector3 center;
        public Vector3 size;

        public static Bounds FromMinMax(Vector3 min, Vector3 max)
        {
            var c = (min + max) / 2;
            var s = max - min;
            Bounds ret = new()
            {
                center = c,
                size = s
            };
            return ret;
        }

        public static (Vector3 min, Vector3 max) ToMinMax(Bounds bounds)
        {
            return bounds.ToMinMax();
        }

        public readonly (Vector3 min, Vector3 max) ToMinMax()
        {
            var hsize = size / 2;
            return (center - hsize,
                    center + hsize);
        }

        public readonly Bounds Union(Bounds other)
        {
            var a = ToMinMax();
            var b = other.ToMinMax();
            var min = Vector3.Min(a.min, b.min);
            var max = Vector3.Max(a.max, b.max);

            return FromMinMax(min, max);
        }

        public readonly Bounds Intersection(Bounds other)
        {
            var a = ToMinMax();
            var b = other.ToMinMax();
            var min = Vector3.Max(a.min, b.min);
            var max = Vector3.Min(a.max, b.max);

            return FromMinMax(min, max);
        }
    }

    public class O3DMesh : Mesh
    {
        public O3DFile O3D => o3d;
        public MeshCommand? CFGMesh => meshCommand;
        public string O3DPath => o3dPath;

        private readonly GL gl;
        private readonly O3DFile o3d;
        private readonly MeshCommand? meshCommand;
        private readonly string o3dPath;
        private readonly string? cfgPath;

        // WARNING: Not thread-safe. Because this class calls OpenGL methods, it's already not thread-safe, so this is fine...
        private static readonly Dictionary<string, int> matls = [];

        public O3DMesh(O3DFile o3d, string o3dPath, GL gl, Shader shader, SceneGraph scene, MeshCommand? cfgMesh = null, string? cfgPath = null)
        {
            this.gl = gl;
            this.o3d = o3d;
            this.o3dPath = o3dPath;
            this.cfgPath = cfgPath;
            this.meshCommand = cfgMesh;
            meshNo++;
            name = Path.GetFileNameWithoutExtension(o3dPath);
            CreateFromO3D(shader, scene);
            ComputeAABB();
        }

        private void CreateFromO3D(Shader shader, SceneGraph scene)
        {
            VertexArrayObject<float, uint>.UnbindAny(gl);

            // Create a new vertex buffer
            // Axis conversion
            var vertsCpy = ArrayPool<O3DVert>.Shared.Rent(o3d.vertices.Length);
            unsafe
            {
                fixed (O3DVert* src = o3d.vertices)
                fixed (O3DVert* dst = vertsCpy)
                {
                    long size = o3d.vertices.Length * Unsafe.SizeOf<O3DVert>();
                    System.Buffer.MemoryCopy(src, dst, size, size);
                }
            }
            for (int v = 0; v < o3d.vertices.Length; v++)
            {
                vertsCpy[v].pos.x = -vertsCpy[v].pos.x;
                vertsCpy[v].normal.x = -vertsCpy[v].normal.x;
            }

            var verts = MemoryMarshal.Cast<O3DVert, float>(vertsCpy.AsSpan(0, o3d.vertices.Length));
            BufferObject<float> vbo = new(gl, verts, BufferTargetARB.ArrayBuffer);

            nverts = o3d.vertices.Length;
            ntris = o3d.triangles.Length;

            bool hasMatlCommands = meshCommand != null && (meshCommand?.matls?.Length ?? 0) > 0;
            if (hasMatlCommands)
            {
                matls.Clear();
                matls.EnsureCapacity(o3d.materials.Length);
                /*matls.EnsureCapacity(meshCommand?.matlCommands.Length ?? 0);
                foreach(var mat in meshCommand?.matlCommands!)
                {
                    matls.Add(mat.path, mat);
                }*/
            }

            for (int m = 0; m < o3d.materials.Length; m++)
            {
                uint[] tris = ArrayPool<uint>.Shared.Rent(o3d.triangles.Length * 3);
                int dstInd = 0;
                for (int i = 0; i < o3d.triangles.Length; i++)
                {
                    var tri = o3d.triangles[i];
                    if (tri.mat == m)
                    {
                        tris[dstInd++] = tri.c;
                        tris[dstInd++] = tri.b;
                        tris[dstInd++] = tri.a;
                    }
                }
                BufferObject<uint> ibo = new(gl, tris.AsSpan()[..dstInd], BufferTargetARB.ElementArrayBuffer);
                VertexArrayObject<float, uint> vao = new(gl, vbo, ibo);
                vao.Bind();
                vao.VertexAttributePointer(0, 3, VertexAttribType.Float, 8, 0);
                vao.VertexAttributePointer(1, 3, VertexAttribType.Float, 8, 3);
                vao.VertexAttributePointer(2, 2, VertexAttribType.Float, 8, 6);

                if (shader == null)
                    throw new Exception("Couldn't create O3D material! Main shader is not initialised!");

                MatlCommand? matl = null;
                if (hasMatlCommands)
                {
                    string tex = o3d.materials[m].textureName;
                    if (matls.TryGetValue(tex, out int ind))
                    {
                        ind++;
                        matls[tex] = ind;
                    } else
                    {
                        ind = 0;
                        matls.Add(tex, ind);
                    }

                    matl = meshCommand?.matls?.FirstOrDefault(x => ind == x.index && string.Equals(x.path, tex, StringComparison.OrdinalIgnoreCase));
                }

                var mat = Material.CreateFromO3D(o3d.materials[m], o3dPath, shader, gl, scene, matl, cfgPath, meshCommand?.isShadow != null);

                submeshes.Add(new()
                {
                    vao = vao,
                    mat = mat,
                    vertCount = (uint)dstInd//o3d.vertices.Length
                });
                vao.Unbind();
                ArrayPool<O3DVert>.Shared.Return(vertsCpy);
            }
        }

        private void ComputeAABB()
        {
            // Compute the AABB
            Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);
            foreach (O3DVert v in o3d.vertices)
            {
                Vector3 pos = v.pos;
                pos.X = -pos.X;
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }
            meshBounds = Bounds.FromMinMax(min, max);
            ComputeBounds();
        }
    }
}
