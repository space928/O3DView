using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace O3DParse;

public struct O3DFile
{
    public byte version;
    public O3DExtendedOptions extendedOptions;
    public uint encryptionKey;

    public O3DVert[] vertices;
    public O3DTri[] triangles;
    public O3DMaterial[] materials;
    public O3DBone[] bones;
    public O3DTransform transform;

    /// <summary>
    /// Constructs a new O3D file struct.
    /// </summary>
    public O3DFile()
    {
        version = 7;
        extendedOptions = O3DExtendedOptions.NONE;
        encryptionKey = 0xffffffff;

        vertices = [];
        triangles = [];
        materials = [];
        bones = [];
        transform = new O3DTransform();
    }

    /// <summary>
    /// Constructs a deep copy of an existing <see cref="O3DFile"/>.
    /// </summary>
    /// <param name="other"></param>
    public O3DFile(O3DFile other)
    {
        version = other.version;
        extendedOptions = other.extendedOptions;
        encryptionKey = other.encryptionKey;

        vertices = new O3DVert[other.vertices.Length];
        triangles = new O3DTri[other.vertices.Length];
        materials = new O3DMaterial[other.vertices.Length];
        bones = new O3DBone[other.vertices.Length];
        transform = other.transform;

        Array.Copy(other.vertices, vertices, vertices.Length);
        Array.Copy(other.triangles, triangles, triangles.Length);
        Array.Copy(other.materials, materials, materials.Length);
        Array.Copy(other.bones, bones, bones.Length);
    }
}

[Flags]
public enum O3DExtendedOptions : byte
{
    NONE = 0,
    LONG_TRIANGLE_INDICES = 1,
    ALT_ENCRYPTION_SEED = 2,
}

[StructLayout(LayoutKind.Sequential, Size = 20)]
public struct O3DVert(O3DVec3 pos, O3DVec3 normal, O3DVec2 uv)
{
    public O3DVec3 pos = pos;
    public O3DVec3 normal = normal;
    public O3DVec2 uv = uv;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(pos, normal, uv);
    }
}

[StructLayout(LayoutKind.Sequential, Size = 14)]
public struct O3DTri(uint a, uint b, uint c, ushort mat)
{
    public uint a = a, b = b, c = c;
    public ushort mat = mat;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(a, b, c, mat);
    }
}

public struct O3DMaterial(O3DVec4 diffuse, O3DVec3 spec, O3DVec3 emission, float specPower, string textureName)
{
    public O3DVec4 diffuse = diffuse;
    public O3DVec3 spec = spec, emission = emission;
    public float specPower = specPower;
    public string textureName = textureName;

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(diffuse, spec, emission, specPower, textureName);
    }
}

public struct O3DBone(string name, O3DWeight[] weights)
{
    public string name = name;
    public O3DWeight[] weights = weights;
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct O3DWeight(uint index, float weight)
{
    public uint index = index;
    public float weight = weight;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(index, weight);
    }
}

[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct O3DVec4(float x, float y, float z, float w)
{
    public float x = x, y = y, z = z, w = w;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(x, y, z, w);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator O3DVec4(Vector4 v)
    {
        return Unsafe.As<Vector4, O3DVec4>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(O3DVec4 v)
    {
        return Unsafe.As<O3DVec4, Vector4>(ref v);
    }
}

[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct O3DVec3(float x, float y, float z)
{
    public float x = x, y = y, z = z;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator O3DVec3(Vector3 v)
    {
        return Unsafe.As<Vector3, O3DVec3>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(O3DVec3 v)
    {
        return Unsafe.As<O3DVec3, Vector3>(ref v);
    }
}

[StructLayout(LayoutKind.Sequential, Size = 8)]
public struct O3DVec2(float x, float y)
{
    public float x = x, y = y;

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(x, y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator O3DVec2(Vector2 v)
    {
        return Unsafe.As<Vector2, O3DVec2>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(O3DVec2 v)
    {
        return Unsafe.As<O3DVec2, Vector2>(ref v);
    }
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct O3DTransform(float m00, float m01, float m02, float m03,
    float m10, float m11, float m12, float m13,
    float m20, float m21, float m22, float m23,
    float m30, float m31, float m32, float m33)
{
    public float m00 = m00, m01 = m01, m02 = m02, m03 = m03,
                 m10 = m10, m11 = m11, m12 = m12, m13 = m13,
                 m20 = m20, m21 = m21, m22 = m22, m23 = m23,
                 m30 = m30, m31 = m31, m32 = m32, m33 = m33;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator O3DTransform(Matrix4x4 v)
    {
        return Unsafe.As<Matrix4x4, O3DTransform>(ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Matrix4x4(O3DTransform v)
    {
        return Unsafe.As<O3DTransform, Matrix4x4>(ref v);
    }
}