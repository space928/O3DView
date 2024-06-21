using System;
using System.Text;

namespace O3DParse;

internal class O3DBinaryReader : BinaryReader
{
    private readonly Encoding encoding;

    public O3DBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
    {
        this.encoding = encoding;
    }

    public O3DVec4 ReadVec4() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    public O3DVec3 ReadVec3() => new(ReadSingle(), ReadSingle(), ReadSingle());

    public O3DVec2 ReadVec2() => new(ReadSingle(), ReadSingle());

    public O3DTransform ReadMatrix() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    public O3DWeight[] ReadWeights(bool longIndices)
    {
        int nWeights = ReadUInt16();
        O3DWeight[] weights = new O3DWeight[nWeights];
        for(int i = 0; i < nWeights; i++)
        {
            weights[i] = new(longIndices?ReadUInt32():ReadUInt16(), ReadSingle());
        }

        return weights;
    }

    public override string ReadString()
    {
        //return base.ReadString();
        int len = ReadByte();
        Span<byte> buff = stackalloc byte[len];
        int numRead = BaseStream.ReadAtLeast(buff, len, throwOnEndOfStream: true);
        return encoding.GetString(buff[..numRead]);
    }
}
