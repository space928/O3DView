using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace O3DParse;

internal class O3DBinaryWriter : BinaryWriter
{
    private readonly Encoding encoding;

    public O3DBinaryWriter(Stream input, Encoding encoding) : base(input, encoding)
    {
        this.encoding = encoding;
    }

    public void WriteHeader(Array array, bool longHeader, [CallerMemberName] string? memberName = null)
    {
        if (array.LongLength > (longHeader ? uint.MaxValue : ushort.MaxValue))
            throw new O3DException($"O3D file has too many {memberName}! " +
                $"{array.LongLength}/{(longHeader ? uint.MaxValue : ushort.MaxValue)}, consider using a newer O3D file version.");

        if (longHeader)
            Write(array.Length);
        else
            Write((ushort)array.Length);
    }

    public void WriteVec4(O3DVec4 vec)
    {
        Write(vec.x);
        Write(vec.y);
        Write(vec.z);
        Write(vec.w);
    }

    public void WriteVec3(O3DVec3 vec)
    {
        Write(vec.x);
        Write(vec.y);
        Write(vec.z);
    }

    public void WriteVec2(O3DVec2 vec)
    {
        Write(vec.x);
        Write(vec.y);
    }

    public void WriteMatrix(O3DTransform vec)
    {
        Write(vec.m00);
        Write(vec.m01);
        Write(vec.m02);
        Write(vec.m03);
        Write(vec.m10);
        Write(vec.m11);
        Write(vec.m12);
        Write(vec.m13);
        Write(vec.m20);
        Write(vec.m21);
        Write(vec.m22);
        Write(vec.m23);
        Write(vec.m30);
        Write(vec.m31);
        Write(vec.m32);
        Write(vec.m33);
    }

    public void WriteWeights(O3DWeight[] weights, bool longIndices)
    {
        Write((ushort)weights.Length);
        for(int i = 0; i < weights.Length; i++)
        {
            if(longIndices)
                Write((uint)weights[i].index);
            else
                Write((ushort)weights[i].index);
            Write(weights[i].weight);
        }
    }

    public void WriteString(string str)
    {
        //base.Write(str);
        if (str.Length > byte.MaxValue)
            throw new O3DException($"String '{str}' is too long for the O3D file format!");

        Span<byte> bytes = stackalloc byte[512]; 
        int len = encoding.GetBytes(str, bytes[1..]);
        bytes[0] = (byte)len;
        OutStream.Write(bytes[..(len + 1)]);
    }
}
