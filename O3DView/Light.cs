using System.Numerics;
using System.Runtime.InteropServices;

namespace O3DView
{
    public class Light : SceneObject
    {
        public bool on = true;
        public Vector3 colour;
        public float intensity;
        public Vector3 ambient;
        public LightMode mode = LightMode.Directional;
        public float range = 1000;

        private static int lightNo = 0;

        private Vector3 lightDir;

        public Light()
        {
            transform = new();
            name = $"Light_{lightNo++}";
            transform.TransformChanged += Transform_TransformChanged;
            Transform_TransformChanged(transform);
        }

        public static Span<ShaderData> CopyLightData(IEnumerable<Light> src, Span<ShaderData> dst, Bounds? target = null)
        {
            int i = 0;
            foreach(var light in src)
            {
                if (!light.on)
                    continue;
                if (target != null
                    && (light.mode == LightMode.Point || light.mode == LightMode.Spot)
                    && Vector3.DistanceSquared(target.Value.center, light.transform.Pos) > light.range)
                    continue;
                //if (i >= dst.Length)
                //    break;

                ref var d = ref dst[i++];
                d.mode = light.mode;
                d.col = light.colour;
                d.intensity = light.intensity;
                d.dir = light.lightDir;
                d.pos = light.transform.Pos;
                d.amb = light.ambient;
                d.range = light.range;
            }
            return dst[..i];
        }

        private void Transform_TransformChanged(Transform obj)
        {
            lightDir = Vector3.Transform((-Vector3.UnitY), transform.Rot);
        }

        public void Bind(Shader shader)
        {
            shader.SetUniform("uLightMode", (int)mode);
            shader.SetUniform("uLightCol", colour);
            shader.SetUniform("uLightIntensity", intensity);
            shader.SetUniform("uLightAmb", ambient);
            shader.SetUniform("uLightDir", lightDir);
            shader.SetUniform("uLightPos", transform.Pos);
        }

        [StructLayout(LayoutKind.Explicit, Size = 80)]
        public struct ShaderData
        {
            [FieldOffset(0)] public LightMode mode;
            [FieldOffset(16)] public Vector3 col;
            [FieldOffset(28)] public float intensity;
            [FieldOffset(32)] public Vector3 dir;
            [FieldOffset(48)] public Vector3 pos;
            [FieldOffset(64)] public Vector3 amb;
            [FieldOffset(76)] public float range;
        }
    }

    public enum LightMode : int
    {
        Ambient = 0,
        Directional = 1,
        Point = 2,
        Spot = 3
    }
}
