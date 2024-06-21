using O3DParse;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using StbImageSharp;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace O3DView
{
    public static class ExtensionMethods
    {
        public static Matrix4x4 SetTranslation(this Matrix4x4 mat, Vector3 trans)
        {
            mat.M41 = trans.X;
            mat.M42 = trans.Y;
            mat.M43 = trans.Z;
            return mat;
        }

        [Flags]
        public enum ModifierKeys
        {
            None = 0,
            Ctrl = 1,
            Shift = 2,
            Alt = 4,
            Win = 8
        }

        public static bool IsModifierKeyDown(this IKeyboard kb, ModifierKeys keys)
        {
            ModifierKeys pressed = ModifierKeys.None;
            pressed |= (kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight)) ? ModifierKeys.Ctrl : ModifierKeys.None;
            pressed |= (kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight)) ? ModifierKeys.Shift : ModifierKeys.None;
            pressed |= (kb.IsKeyPressed(Key.AltLeft) || kb.IsKeyPressed(Key.AltRight)) ? ModifierKeys.Alt : ModifierKeys.None;
            pressed |= (kb.IsKeyPressed(Key.SuperLeft) || kb.IsKeyPressed(Key.SuperRight)) ? ModifierKeys.Win : ModifierKeys.None;

            return pressed == keys;
        }

        public static (PixelFormat pxFmt, InternalFormat intFmt, PixelType pxType) ToOpenGLFormat(this ColorComponents components)
        {
            switch(components)
            {
                case ColorComponents.Grey:
                    return (PixelFormat.Red, InternalFormat.R8, PixelType.UnsignedByte);
                case ColorComponents.GreyAlpha:
                    return (PixelFormat.RG, InternalFormat.RG8, PixelType.UnsignedByte);
                case ColorComponents.RedGreenBlue:
                    return (PixelFormat.Rgb, InternalFormat.Rgb8, PixelType.UnsignedByte);
                case ColorComponents.RedGreenBlueAlpha:
                    return (PixelFormat.Rgba, InternalFormat.Rgba8, PixelType.UnsignedByte);
                case ColorComponents.Default:
                default:
                    throw new Exception("Unknown pixel format 'Default'!");
            }
        }
    }
}
