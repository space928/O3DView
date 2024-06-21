using System;
using System.Text;

namespace O3DParse;

public static class O3DParser
{
    /// <summary>
    /// Loads an O3D file from a file path.
    /// </summary>
    /// <param name="path">The path to the O3D file to load.</param>
    /// <returns>The parsed O3D file.</returns>
    /// <exception cref="O3DException"></exception>
    /// <exception cref="IOException"></exception>
    public static O3DFile ReadO3D(string path)
    {
        O3DFile o3d = new();
        using FileStream f = File.OpenRead(path);
        using O3DBinaryReader reader = new(f, Encoding.Latin1);

        if (reader.ReadUInt16() != 0x1984)
            throw new O3DException("O3D file does not have a valid header!");

        bool longHeader = false;
        o3d.version = reader.ReadByte();
        if (o3d.version > 3)
        {
            o3d.extendedOptions = (O3DExtendedOptions)reader.ReadByte();
            o3d.encryptionKey = reader.ReadUInt32();

            //if (o3d.encryptionKey != 0xffffffff && !allowDecryption)
            //    throw new O3DException("O3D file is already encrypted!");

            longHeader = true;
        }

        while (reader.PeekChar() != -1)
        {
            switch (reader.ReadByte())
            {
                case 0x17:
                    uint len = longHeader ? reader.ReadUInt32() : reader.ReadUInt16();
                    if (len > 1000000)
                        throw new O3DException($"Tried to read {len} vertices! That's quite a lot, is the file corrupted?");
                    o3d.vertices = new O3DVert[len];
                    for (int i = 0; i < o3d.vertices.Length; i++)
                        o3d.vertices[i] = new(reader.ReadVec3(), reader.ReadVec3(), reader.ReadVec2());
                    break;
                case 0x49:
                    len = longHeader ? reader.ReadUInt32() : reader.ReadUInt16();
                    if (len > 1000000)
                        throw new O3DException($"Tried to read {len} triangles! That's quite a lot, is the file corrupted?");
                    o3d.triangles = new O3DTri[len];
                    if ((o3d.extendedOptions & O3DExtendedOptions.LONG_TRIANGLE_INDICES) != 0)
                        for (int i = 0; i < o3d.triangles.Length; i++)
                            o3d.triangles[i] = new(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt16());
                    else
                        for (int i = 0; i < o3d.triangles.Length; i++)
                            o3d.triangles[i] = new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
                    break;
                case 0x26:
                    len = reader.ReadUInt16();
                    if (len > 10000)
                        throw new O3DException($"Tried to read {len} materials! That's quite a lot, is the file corrupted?");
                    o3d.materials = new O3DMaterial[len];
                    for (int i = 0; i < o3d.materials.Length; i++)
                        o3d.materials[i] = new(reader.ReadVec4(), reader.ReadVec3(), reader.ReadVec3(), reader.ReadSingle(), reader.ReadString());
                    break;
                case 0x54:
                    len = longHeader ? reader.ReadUInt32() : reader.ReadUInt16();
                    if (len > 1000000)
                        throw new O3DException($"Tried to read {len} bones! That's quite a lot, is the file corrupted?");
                    o3d.bones = new O3DBone[len];
                    for (int i = 0; i < o3d.bones.Length; i++)
                        o3d.bones[i] = new(reader.ReadString(), reader.ReadWeights((o3d.extendedOptions & O3DExtendedOptions.LONG_TRIANGLE_INDICES) != 0));
                    break;
                case 0x79:
                    o3d.transform = reader.ReadMatrix();
                    break;
                default:
                    throw new O3DException($"Unexpected section header found in O3D! At offset 0x{reader.BaseStream.Position - 1:X8}.");
            }
        }

        // Decrypt the file if needed
        //if (o3d.encryptionKey != 0xffffffff && allowDecryption)
        //    DecryptO3D(ref o3d);

        return o3d;
    }

    /// <summary>
    /// Writes an O3D file to disk.
    /// </summary>
    /// <param name="path">The filepath to write to.</param>
    /// <param name="o3d">The O3D file to encrypt.</param>
    /// <exception cref="O3DException"></exception>
    /// <exception cref="IOException"></exception>
    public static void WriteO3D(string path, O3DFile o3d)
    {
        if (o3d.encryptionKey != 0xffffffff && o3d.version <= 3)
            throw new O3DException($"O3D version {o3d.version} does not support encryption!");

        bool longHeader = o3d.version > 3;

        using FileStream f = File.Open(path, FileMode.Create);
        using O3DBinaryWriter writer = new(f, Encoding.Latin1);

        // Magic number
        writer.Write((ushort)0x1984);
        // File version
        writer.Write(o3d.version);
        if (longHeader)
        {
            // Extended header
            writer.Write((byte)o3d.extendedOptions);
            // Encryption key
            writer.Write(o3d.encryptionKey);
        }

        // Verts
        if (o3d.vertices.Length > 0)
        {
            writer.Write((byte)0x17);
            writer.WriteHeader(o3d.vertices, longHeader);
            for (int i = 0; i < o3d.vertices.Length; i++)
            {
                writer.WriteVec3(o3d.vertices[i].pos);
                writer.WriteVec3(o3d.vertices[i].normal);
                writer.WriteVec2(o3d.vertices[i].uv);
            }
        }

        // Tris
        if (o3d.triangles.Length > 0)
        {
            writer.Write((byte)0x49);
            writer.WriteHeader(o3d.triangles, longHeader);
            for (int i = 0; i < o3d.triangles.Length; i++)
            {
                if ((o3d.extendedOptions & O3DExtendedOptions.LONG_TRIANGLE_INDICES) != 0)
                {
                    writer.Write(o3d.triangles[i].a);
                    writer.Write(o3d.triangles[i].b);
                    writer.Write(o3d.triangles[i].c);
                }
                else
                {
                    writer.Write((ushort)o3d.triangles[i].a);
                    writer.Write((ushort)o3d.triangles[i].b);
                    writer.Write((ushort)o3d.triangles[i].c);
                }
                writer.Write(o3d.triangles[i].mat);
            }
        }

        // Materials
        if (o3d.materials.Length > 0)
        {
            writer.Write((byte)0x26);
            writer.WriteHeader(o3d.materials, false);
            for (int i = 0; i < o3d.materials.Length; i++)
            {
                writer.WriteVec4(o3d.materials[i].diffuse);
                writer.WriteVec3(o3d.materials[i].spec);
                writer.WriteVec3(o3d.materials[i].emission);
                writer.Write(o3d.materials[i].specPower);
                writer.WriteString(o3d.materials[i].textureName);
            }
        }

        // Bones
        if (o3d.bones.Length > 0)
        {
            writer.Write((byte)0x54);
            writer.WriteHeader(o3d.bones, longHeader);
            for (int i = 0; i < o3d.bones.Length; i++)
            {
                writer.WriteString(o3d.bones[i].name);
                writer.WriteWeights(o3d.bones[i].weights, (o3d.extendedOptions & O3DExtendedOptions.LONG_TRIANGLE_INDICES) != 0);
            }
        }

        // Transform
        writer.Write((byte)0x79);
        writer.WriteMatrix(o3d.transform);
    }
}
