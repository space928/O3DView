using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace O3DView
{
    public struct Transform
    {
        public readonly Matrix4x4 Matrix => matrix;
        public Vector3 Pos
        {
            readonly get => pos;
            set
            {
                pos = value;
                matrix = matrix.SetTranslation(pos);
                TransformChanged?.Invoke(this);
            }
        }
        public Vector3 EulerRot
        {
            readonly get
            {
                Vector3 ret;

                // roll (x-axis rotation)
                float sinr_cosp = 2 * (rot.W * rot.X + rot.Y * rot.Z);
                float cosr_cosp = 1 - 2 * (rot.X * rot.X + rot.Y * rot.Y);
                ret.X = MathF.Atan2(sinr_cosp, cosr_cosp);

                // yaw (y-axis rotation)
                float sinp = MathF.Sqrt(1 + 2 * (rot.W * rot.Y - rot.X * rot.Z));
                float cosp = MathF.Sqrt(1 - 2 * (rot.W * rot.Y - rot.X * rot.Z));
                ret.Y = 2 * MathF.Atan2(sinp, cosp) - MathF.PI / 2;

                // pitch (z-axis rotation)
                float siny_cosp = 2 * (rot.W * rot.Z + rot.X * rot.Y);
                float cosy_cosp = 1 - 2 * (rot.Y * rot.Y + rot.Z * rot.Z);
                ret.Z = MathF.Atan2(siny_cosp, cosy_cosp);

                return ret;
            }
            set
            {
                Vector3 half = value * 0.5f;
                float sr, cr, sp, cp, sy, cy;
                (sr, cr) = MathF.SinCos(half.X);
                (sp, cp) = MathF.SinCos(half.Y);
                (sy, cy) = MathF.SinCos(half.Z);
                Quaternion q = new(
                    sr * cp * cy - cr * sp * sy,
                    cr * sp * cy + sr * cp * sy,
                    cr * cp * sy - sr * sp * cy,
                    cr * cp * cy - sr * sp * sy
                );

                Rot = q;
            }
        }
        public Quaternion Rot
        {
            readonly get => rot;
            set
            {
                rot = value;
                UpdateMatrix();
            }
        }
        public float Scale
        {
            readonly get => scale;
            set
            {
                scale = value;
                UpdateMatrix();
            }
        }

        private Matrix4x4 matrix;
        private Vector3 pos;
        private Quaternion rot;
        private float scale;

        public event Action<Transform>? TransformChanged;

        public Transform()
        {
            matrix = Matrix4x4.Identity;
            pos = Vector3.Zero;
            rot = Quaternion.Identity;
            scale = 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateMatrix()
        {
            matrix = (Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateScale(scale)).SetTranslation(pos);
            TransformChanged?.Invoke(this);
        }

        public void SetTransform(Vector3 pos, Vector3 eulerRot, float scale)
        {
            this.pos = pos;
            this.scale = scale;
            // Setting the EulerRot property implicitly updates the whole xform matrix using the newly set scale and position.
            EulerRot = eulerRot;
        }
    }
}
