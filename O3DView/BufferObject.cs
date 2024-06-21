using Silk.NET.OpenGL;

namespace O3DView
{
    public class BufferObject<T> : IDisposable
        where T : unmanaged
    {
        private readonly GL gl;
        private readonly BufferTargetARB type;
        public readonly uint handle;
        private int length = 0;

        public BufferObject(GL gl, ReadOnlySpan<T> data, BufferTargetARB type)
        {
            this.gl = gl;
            this.type = type;
            handle = gl.GenBuffer();
            Bind();
            gl.BufferData(type, data, BufferUsageARB.StaticDraw);
            length = data.Length;
            Unbind();
        }

        public void Bind()
        {
            gl.BindBuffer(type, handle);
        }

        public void Unbind()
        {
            gl.BindBuffer(type, 0);
        }

        public void Update(ReadOnlySpan<T> data, uint offset = 0, bool shrink = false)
        {
            if (data.Length > length && offset != 0)
                throw new NotImplementedException("Buffer isn't big enough!");
            Bind();
            if (data.Length > length || (shrink && data.Length != length))
            {
                gl.BufferData(type, data, BufferUsageARB.StaticDraw);
                length = data.Length;
            }
            else
                gl.BufferSubData(type, (nint)offset, data);
            Unbind();
        }

        public void Dispose()
        {
            gl.DeleteBuffer(handle);
        }
    }
}
