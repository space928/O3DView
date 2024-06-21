using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace O3DView
{
    public class Camera : SceneObject
    {
        public Vector3 direction = Vector3.UnitZ;
        public Vector3 target = Vector3.Zero;
        public float orbitDist = 10;
        public float panSpeed = 1;
        public float orbitSpeed = 1;
        public float zoomSpeed = 1;
        public float moveSpeed = 1;

        public IKeyboard? Keyboard { set => keyboard = value; }
        public IMouse? Mouse { set => mouse = value; }
        public Matrix4x4 View => view;

        private const float panSpeedMul = 0.013f;
        private const float orbitSpeedMul = 0.005f;
        private const float zoomSpeedMul = 0.3f;
        private const float moveSpeedMul = 10f;

        private Matrix4x4 view;
        private Vector3 pos;
        private Vector2 rotation = new (MathF.PI/2, 0);
        private IKeyboard? keyboard;
        private IMouse? mouse;
        private Vector2 lastMouse;
        private float lastScroll;
        private readonly ImGuiIOPtr imGuiIO;
        //private Vector3 lastXformPos;

        private static int cameraNo = 0;

        public Camera()
        {
            imGuiIO = ImGui.GetIO();
            /*transform.TransformChanged += _ =>
            {
                var delta = transform.Pos - lastXformPos;
                lastXformPos = transform.Pos;
                target += delta;
            };*/
            name = $"Camera_{cameraNo++}";
        }

        private void UpdateMatrix()
        {
            direction.X = MathF.Cos(rotation.X) * MathF.Cos(rotation.Y);
            direction.Y = MathF.Sin(rotation.Y);
            direction.Z = MathF.Sin(rotation.X) * MathF.Cos(rotation.Y);

            pos = target + direction * orbitDist;

            var right = Vector3.Cross(direction, Vector3.UnitY);
            right = Vector3.Normalize(right);
            var up = Vector3.Cross(right, direction);
            up = Vector3.Normalize(up);

            view = Matrix4x4.CreateLookTo(pos, -direction, up);

            //transform.SetTransform(pos, )
            transform.Pos = pos;
        }

        public void Focus(Bounds bounds)
        {
            target = bounds.center;
            orbitDist = bounds.size.Length();
            UpdateMatrix();
        }

        public void OnRender(double deltaTime)
        {
            if (keyboard == null || mouse == null)
                return;

            if (imGuiIO.WantCaptureMouse)
                return;

            float moveFactor = (float)(moveSpeed * moveSpeedMul * deltaTime);
            var keyBoardMove = Vector4.Zero;
            if (keyboard.IsKeyPressed(Key.Up))
                keyBoardMove.Z = -moveFactor;
            else if(keyboard.IsKeyPressed(Key.Down))
                keyBoardMove.Z = moveFactor;
            if (keyboard.IsKeyPressed(Key.Left))
                keyBoardMove.X = -moveFactor;
            else if (keyboard.IsKeyPressed(Key.Right))
                keyBoardMove.X = moveFactor;
            //if (keyboard.IsKeyPressed(Key.Space))
            //    keyBoardMove.Y = moveFactor;
            //else if (keyboard.IsKeyPressed(Key.ControlLeft))
            //    keyBoardMove.Y = -moveFactor;

            if (keyBoardMove != Vector4.Zero)
            {
                Vector4 offset = Vector4.Transform(keyBoardMove, Matrix4x4.Transpose(view));
                target += new Vector3(offset.X, offset.Y, offset.Z);

                UpdateMatrix();
            }

            var mousePos = mouse.Position;
            var delta = mousePos - lastMouse;
            if(mouse.IsButtonPressed(MouseButton.Left) && mouse.IsButtonPressed(MouseButton.Right))
            {
                // Zoom
                orbitDist -= orbitDist * delta.Y * panSpeed * panSpeedMul;
                orbitDist = MathF.Max(0.01f, orbitDist);
                UpdateMatrix();
            } else if(mouse.IsButtonPressed(MouseButton.Left))
            {
                // Orbit
                rotation -= new Vector2(-delta.X, -delta.Y) * orbitSpeed * orbitSpeedMul;
                rotation.Y = MathF.Min(MathF.Max(rotation.Y, -MathF.PI / 2 + (float)1e-6), MathF.PI / 2 - (float)1e-6);

                UpdateMatrix();
            } else if (mouse.IsButtonPressed(MouseButton.Right))
            {
                // Pan
                float speed = panSpeed * panSpeedMul;
                Vector4 delta4 = new(-delta.X * speed, delta.Y * speed, 0, 0);
                Vector4 offset = Vector4.Transform(delta4, Matrix4x4.Transpose(view));
                target += new Vector3(offset.X, offset.Y, offset.Z);

                UpdateMatrix();
            }
            var scroll = mouse.ScrollWheels[0].Y;
            //var deltaScroll = scroll - lastScroll;
            if(MathF.Abs(scroll) > 1e-6)
            {
                orbitDist -= orbitDist * scroll * zoomSpeed * zoomSpeedMul;
                orbitDist = MathF.Max(0.01f, orbitDist);
                UpdateMatrix();
            }

            lastMouse = mousePos;
            lastScroll = scroll;
        }
    }
}
