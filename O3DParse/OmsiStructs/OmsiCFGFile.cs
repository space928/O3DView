using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace O3DParse.Ini;

// With thanks to https://roadhog123.co.uk/omsi-wiki/quick-reference/keywords
// for help with some of the keyword definitions
[Serializable]
[OmsiIniCommand("model")]
public class OmsiCFGFile
{
    [OmsiIniCommandFile] public string? path;

    public VFDCommand? vfd;
    public DetailFactorCommand? detailFactor;
    public TexDetailFactorCommand? texDetailFactor;
    public NoDistanceCheckCommand? noDistanceCheck;
    public TerrainHoleCommand? terrainHole;
    public ItemCommand? item;
    public SetVarCommand? setVar;
    public CTCCommand? ctc;
    public TextTextureEnhCommand[]? textTextures;
    //public TextTextureEnhCommand[]? textTexturesEnh;
    public ScriptTextureCommand[]? scriptTextures;
    public LODCommand[]? lods;

    public MeshCommand[]? meshes; // Normally meshes belong to a LOD, but it's possible to define a model without lods
    public InteriorLightCommand[]? interiorLights;
    public SpotLightCommand[]? spotLights;
    public LightEnh2Command[]? lights;          // Technically [light]s belong to a [mesh], but it's not uncommon for authors to place 
    //public LightEnhCommand[]? lightEnhs;    // all their lights after all the meshes (and spotlights), omsi doesn't care because it 
    //public LightEnh2Command[]? lightEnh2s;  // just adds them to the last mesh, but our parser can't do that, so we put them here...

    [OmsiIniComments] public string? comments;
    [OmsiIniComments(true)] public string? postComments;
}

public struct RGBColour
{
    public byte r;
    public byte g;
    public byte b;

    public readonly Vector3 ToVector3 => new Vector3(r, g, b) * (1 / 255f);
}

public struct RGBAColour
{
    public byte r;
    public byte g;
    public byte b;
    public byte a;

    public readonly Vector4 ToVector4 => new Vector4(r, g, b, a) * (1 / 255f);
}

public struct BGRAColour
{
    public byte b;
    public byte g;
    public byte r;
    public byte a;

    /// <summary>
    /// Returns an RGBA Vector4 in the range 0-1
    /// </summary>
    public readonly Vector4 ToVector4 => new Vector4(r, g, b, a) * (1 / 255f);
}

[OmsiIniCommand("VFDmaxmin")]
public struct VFDCommand
{
    public Vector3 min;
    public Vector3 max;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("detail_factor")]
public struct DetailFactorCommand
{
    public float detailFactor;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("tex_detail_factor")]
public struct TexDetailFactorCommand
{
    public float detailFactor;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("noDistanceCheck")]
public struct NoDistanceCheckCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("terrainhole")]
public struct TerrainHoleCommand
{
    public string path;

    [OmsiIniComments] public string comments;
}

#region CTI Commands
[OmsiIniCommand("item")]
public struct ItemCommand
{
    public string name;
    public string ctcTexture;
    public string path;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("setvar")]
public struct SetVarCommand
{
    public string var;
    public float value;

    [OmsiIniComments] public string comments;
}
#endregion

[OmsiIniCommand("CTC")]
public struct CTCCommand
{
    public string colourScheme;
    public string path;
    public string unk;

    [OmsiIniComments] public string comments;
    public CTCTextureCommand[]? textures;
}

[OmsiIniCommand("CTCTexture")]
public struct CTCTextureCommand
{
    public string name;
    public string path;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("texttexture")]
public struct TextTextureCommand
{
    public string stringVar;
    public string fontName;
    public uint width;
    public uint height;
    public bool fullcolour;
    public RGBColour colour;

    [OmsiIniComments] public string comments;
}

public enum TextAlign : int
{
    Centred,
    Left,
    Right,
    Justify,
    JustifyLeft,
    JustifyRight
}

[OmsiIniCommand("texttexture_enh")]
[OmsiIniDerivedCommand<TextTextureCommand>]
public struct TextTextureEnhCommand
{
    public string? stringVar;
    public string? fontName;
    public uint width;
    public uint height;
    public bool fullcolour;
    public RGBColour colour;
    public TextAlign textAlign = TextAlign.Left;
    public bool gridAlign = true;

    [OmsiIniComments] public string? comments;

    public TextTextureEnhCommand() { }
}

[OmsiIniCommand("scripttexture")]
public struct ScriptTextureCommand
{
    public uint width, height;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("LOD")]
public struct LODCommand
{
    public float viewRadius;

    [OmsiIniComments] public string comments;
    public MeshCommand[]? meshes;
    // TODO: Do interior lights belong to a LOD?
    //public InteriorLightCommand[]? interiorLights;
    //public SpotLightCommand[]? spotLights;
}

[OmsiIniCommand("interiorlight")]
public struct InteriorLightCommand
{
    public string var;
    public float range;
    public RGBColour colour;
    public Vector3 pos;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("spotlight")]
public struct SpotLightCommand
{
    public Vector3 pos;
    public Vector3 direction;
    public Vector3 colour;
    public float range;
    public float innerRadius;
    public float outerRadius;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("mesh")]
public struct MeshCommand
{
    public string path;

    public MeshIdentCommand? meshIdent;
    public VisibleCommand[]? visibles;
    public IlluminationInteriorCommand? illuminationInterior;
    public MatlCommand[]? matls;
    public MatlChangeCommand[]? matlChanges;
    public AnimParentCommand? animParent;
    public IsShadowCommand? isShadow;
    public ShadowCommand? shadow;
    public MouseEventCommand? mouseEvent;
    public NewAnimCommand[]? anims;
    public ViewpointCommand? viewpoint;
    public SetBoneCommand[]? bones;
    public SmoothSkinCommand? smoothSkin;
    public IlluminationCommand? illumination;
    public LightEnh2Command[]? lights;
    //public LightEnhCommand[]? lightEnhs;
    //public LightEnh2Command[]? lightEnh2s;
    public TexChangesCommand? texChanges;
    public SmokeCommand[]? smokes;
    public ParticleEmitterCommand[]? particleEmitters;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("visible")]
public struct VisibleCommand
{
    public string scriptVar;
    public float value;

    [OmsiIniComments] public string comments;
}

[Flags]
public enum OmsiViewpoint
{
    Never,
    Exterior = 1 << 0,
    Interior = 1 << 1,
    AI = 1 << 2,
}

[OmsiIniCommand("viewpoint")]
public struct ViewpointCommand
{
    public OmsiViewpoint viewpoint;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("mesh_ident")]
public struct MeshIdentCommand
{
    public string name;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("animparent")]
public struct AnimParentCommand
{
    public string parentName;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("mouseevent")]
public struct MouseEventCommand
{
    public string triggerName;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("isshadow")]
public struct IsShadowCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("shadow")]
public struct ShadowCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("setbone")]
public struct SetBoneCommand
{
    public string name;
    public int value;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("smoothskin")]
public struct SmoothSkinCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("illumination")]
public struct IlluminationCommand
{
    public int value;

    [OmsiIniComments] public string comments;
}

#region Anim
[OmsiIniCommand("newanim")]
public struct NewAnimCommand
{
    public AnimOriginFromMeshCommand? originFromMesh;
    public AnimOriginTransCommand? originTrans;
    public AnimOriginRotXCommand? originRotX;
    public AnimOriginRotYCommand? originRotY;
    public AnimOriginRotZCommand? originRotZ;
    public AnimTransCommand? animTrans;
    public AnimRotCommand? rot;
    public AnimOffsetCommand? offset;
    public AnimMaxspeedCommand? maxspeed;
    public AnimDelayCommand? delay;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("origin_from_mesh")]
[OmsiIniVerbatimCommand]
public struct AnimOriginFromMeshCommand { }

[OmsiIniCommand("origin_trans")]
[OmsiIniVerbatimCommand]
public struct AnimOriginTransCommand
{
    public Vector3 origin;
}

[OmsiIniCommand("origin_rot_x")]
[OmsiIniVerbatimCommand]
public struct AnimOriginRotXCommand
{
    public float rotX;
}

[OmsiIniCommand("origin_rot_y")]
[OmsiIniVerbatimCommand]
public struct AnimOriginRotYCommand
{
    public float rotY;
}

[OmsiIniCommand("origin_rot_z")]
[OmsiIniVerbatimCommand]
public struct AnimOriginRotZCommand
{
    public float rotZ;
}

[OmsiIniCommand("anim_trans")]
[OmsiIniVerbatimCommand]
public struct AnimTransCommand
{
    public string animCurve;
    public float delta;
}

[OmsiIniCommand("anim_rot")]
[OmsiIniVerbatimCommand]
public struct AnimRotCommand
{
    public string animCurve;
    public float delta;
}

[OmsiIniCommand("offset")]
[OmsiIniVerbatimCommand]
public struct AnimOffsetCommand
{
    public float offset;
}

[OmsiIniCommand("maxspeed")]
[OmsiIniVerbatimCommand]
public struct AnimMaxspeedCommand
{
    public float maxspeed;
}

[OmsiIniCommand("delay")]
[OmsiIniVerbatimCommand]
public struct AnimDelayCommand
{
    public float delay;
}
#endregion

[Flags]
public enum OmsiLightEffect
{
    None,
    Star = 1 << 0,
    NoFog = 1 << 1,
    EffectOnly = 1 << 2,
}

[OmsiIniCommand("light")]
public struct LightCommand
{
    public string brightnessVar;
    public float size;
    public RGBColour colour;
    public Vector3 position;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("light_enh")]
public struct LightEnhCommand
{
    public Vector3 position;
    public RGBColour colour;
    public float size;
    public string brightnessVar;
    public float brightness;
    public float zOffset;
    public OmsiLightEffect effect;
    public float fadeTime;
    [OmsiIniOptional] public string? bitmap;

    [OmsiIniComments] public string comments;
}

public enum OmsiLightRotationAxis
{
    DirectionVector,
    RotationAxis,
    Free
}

[OmsiIniCommand("light_enh_2")]
[OmsiIniDerivedCommand<LightEnhCommand>]
[OmsiIniDerivedCommand<LightCommand>]
public struct LightEnh2Command
{
    public Vector3 position;
    public Vector3 direction;
    public Vector3 rotationAxis;
    public bool omnidirectional;
    public OmsiLightRotationAxis rotationMode;
    public RGBColour colour;
    public float size;
    public float innerConeAngle;
    public float outerConeAngle;
    public string brightnessVar;
    public float brightness;
    public float zOffset;
    public OmsiLightEffect effect;
    public bool coneEffect;
    public float fadeTime;
    [OmsiIniOptional] public string? bitmap;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("illumination_interior")]
public struct IlluminationInteriorCommand
{
    public int lightA, lightB, lightC, lightD;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("texchanges")]
public struct TexChangesCommand
{
    public string path;

    [OmsiIniComments] public string comments;
}

#region Particle System Commands
[OmsiIniCommand("smoke")]
public struct SmokeCommand
{
    public Vector3 pos;
    public Vector3 direction;
    // All of these can be either an OSC variable name, or a float
    public string initialSpeed;
    public string speedVariation;
    public string emissionRate;
    public string lifespan;
    public string drag;
    public string gravity;
    public string initialSize;
    public string growRate;
    public string initialAlpha;
    public string alphaVariation;
    public Vector3 colour;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("particle_emitter")]
public struct ParticleEmitterCommand
{
    // TODO: Implement particle emitter, this command is quite complex.

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("PS_attachTo")]
public struct PSAttachToCommand
{
    public int unk1;
    public int unk2;
    public int unk3;
    // TODO: Work out what these mean...

    [OmsiIniComments] public string comments;
}

// TODO: There are a bunch of other PS "commands" such as:
// --PS_instExplosion_partcount--
// --PS_livetime--
// --PS_emitter_livetime--
// etc...
#endregion

#region Matl Commands
[OmsiIniCommand("matl")]
public struct MatlCommand
{
    public string path;
    public uint? index;

    public AlphascaleCommand? alphascale;
    public AllcolorCommand? allcolor;
    public MatlAlphaCommand? alpha;
    public MatlBumpmapCommand? bumpmap;
    public MatlEnvmapCommand? envmap;
    public MatlEnvmapMaskCommand? envmapMask;
    public MatlFreetexCommand[]? freetexes;
    public MatlLightmapCommand? lightmap;
    public MatlNightmapCommand? nightmap;
    public MatlNoZWriteCommand? noZWrite;
    public MatlNoZCheckCommand? noZCheck;
    public MatlTexAdressBorderCommand? texadressborder;
    public MatlTexAdressClampCommand? texadressclamp;
    public MatlTexAdressMirrorCommand? texadressmirror;
    public MatlTexAdressMirrorOnceCommand? texadressmirroronce;
    public MatlTransmapCommand? transmap;
    public MatlTexCoordTransXCommand? texcoordTransX;
    public MatlTexCoordTransYCommand? texcoordTransY;
    public MatlUseScriptTextureCommand? useScriptTexture;
    public MatlUseTextTextureCommand? useTextTexture;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_change")]
public struct MatlChangeCommand
{
    public string path;
    public float value;
    public string var;

    public MatlItemCommand[]? items;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_item")]
public struct MatlItemCommand
{
    public AlphascaleCommand? alphascale;
    public AllcolorCommand? allcolor;
    public MatlAlphaCommand? alpha;
    public MatlBumpmapCommand? bumpmap;
    public MatlEnvmapCommand? envmap;
    public MatlEnvmapMaskCommand? envmapMask;
    public MatlFreetexCommand[]? freetexes;
    public MatlLightmapCommand? lightmap;
    public MatlNightmapCommand? nightmap;
    public MatlNoZWriteCommand? noZWrite;
    public MatlNoZCheckCommand? noZCheck;
    public MatlRaindropMap? raindropMap;
    public MatlTexAdressBorderCommand? texadressborder;
    public MatlTexAdressClampCommand? texadressclamp;
    public MatlTexAdressMirrorCommand? texadressmirror;
    public MatlTexAdressMirrorOnceCommand? texadressmirroronce;
    public MatlTransmapCommand? transmap;
    public MatlTexCoordTransXCommand? texcoordTransX;
    public MatlTexCoordTransYCommand? texcoordTransY;
    public MatlUseScriptTextureCommand? useScriptTexture;
    public MatlUseTextTextureCommand? useTextTexture;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("alphascale")]
public struct AlphascaleCommand
{
    public string var;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("allcolor")]
public struct AllcolorCommand
{
    public Vector4 diffuse;
    public Vector3 ambient;
    public Vector3 specular;
    public Vector3 emission;
    public float specPower;

    [OmsiIniComments] public string comments;
}

public enum MatlAlphaMode
{
    Opaque,
    Cutout,
    Blend
}

[OmsiIniCommand("matl_alpha")]
public struct MatlAlphaCommand
{
    public MatlAlphaMode alphaMode;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_bumpmap")]
public struct MatlBumpmapCommand
{
    public string path;
    public float strength;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_envmap")]
public struct MatlEnvmapCommand
{
    public string path;
    public float reflectivity;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_envmap_mask")]
public struct MatlEnvmapMaskCommand
{
    public string path;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_freetex")]
public struct MatlFreetexCommand
{
    public string path;
    public string varName;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_lightmap")]
public struct MatlLightmapCommand
{
    public string path;
    [OmsiIniOptional] public string? var;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_nightmap")]
public struct MatlNightmapCommand
{
    public string path;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_noZwrite")]
public struct MatlNoZWriteCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_noZcheck")]
public struct MatlNoZCheckCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_raindropmap")]
public struct MatlRaindropMap
{
    public string path;
    public string var;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_texadress_border")]
public struct MatlTexAdressBorderCommand
{
    public BGRAColour borderColour;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_texadress_clamp")]
public struct MatlTexAdressClampCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_texadress_mirror")]
public struct MatlTexAdressMirrorCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_texadress_mirroronce")]
public struct MatlTexAdressMirrorOnceCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("matl_transmap")]
public struct MatlTransmapCommand
{
    public string path;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("texcoordtransX")]
public struct MatlTexCoordTransXCommand
{
    public string var;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("texcoordtransY")]
public struct MatlTexCoordTransYCommand
{
    public string var;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("useScriptTexture")]
public struct MatlUseScriptTextureCommand
{
    public int index;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("useTextTexture")]
public struct MatlUseTextTextureCommand
{
    public string var;

    [OmsiIniComments] public string comments;
}
#endregion
