using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace O3DView
{
    public class Texture2D : IDisposable
    {
        public bool IsValid => isValid;
        public PixelFormat Format => pixelFormat;

        protected readonly uint handle;
        protected readonly GL gl;
        protected PixelFormat pixelFormat;
        protected InternalFormat internalFormat;
        protected PixelType pixelType;
        protected bool isValid = true;

        private static uint currentTexture = 0;

        public Texture2D(GL gl)
        {
            this.gl = gl;
            handle = gl.CreateTexture(TextureTarget.Texture2D);
        }

        public void Bind()
        {
            if(currentTexture != handle)
                gl.BindTexture(TextureTarget.Texture2D, handle);
            currentTexture = handle;
        }

        public void Bind(uint unit)
        {
            gl.BindTextureUnit(unit, handle);
        }

        public static void BindTextures(uint startUnit, ReadOnlySpan<Texture2D> textures)
        {
            if (textures.Length < 1)
                return;

            Span<uint> handles = stackalloc uint[textures.Length];
            for(int i = 0; i < textures.Length; i++)
                handles[i] = textures[i].handle;

            var gl = textures[0].gl;
            gl.BindTextures(startUnit, handles);
        }

        public void Unbind()
        {
            if (currentTexture != handle)
                throw new Exception($"Can't unbind texture {handle} as it isn't bound! (current = {currentTexture})");
            gl.BindTexture(TextureTarget.Texture2D, 0);
            currentTexture = 0;
        }

        public void Dispose()
        {
            gl.DeleteTexture(handle);
            isValid = false;
        }
    }
}
