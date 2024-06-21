using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace O3DParse.Ini;

/// <summary>
/// Represents a paths file used by a bus.
/// </summary>
[Serializable]
[OmsiIniCommand("paths")]
public class OmsiPaths
{
    [OmsiIniCommandFile] public string path = "";

    public StepSoundPackCommand[]? stepSounds;
    public PathPntCommand[]? points;
    public PathLinkCommand[]? links;

    // These fields are treated specially because they effectively belong to the PathLinkCommands
    [OmsiIniSkip(SkipSerialization.Serialization)] public NextRoomHeightCommand? _nextRoomHeight;
    [OmsiIniSkip(SkipSerialization.Serialization)] public NextStepSoundCommand? _nextStepSound;

    [OmsiIniComments] public string? comments;
    [OmsiIniComments(true)] public string? postComments;

    internal float prevRoomHeight = float.MinValue;
    internal int prevStepSound = int.MinValue;
}

[OmsiIniCommand("pathpnt")]
public struct PathPntCommand
{
    public Vector3 position;

    [OmsiIniComments] public string comments;
}

public class PathLinkSerializer : IOmsiIniCommandCustomSerializer
{
    public void Deserialize(ref object target, Type targetType, IniFileReader reader, ref int lineNumber, object? parent)
    {
        if (parent is not OmsiPaths paths)
            throw new OmsiIniSerializationException($"PathLinkSerializer expected the [pathlink] command to belong to an {nameof(OmsiPaths)}!");

        PathLinkCommand pathLink;
        if (targetType == typeof(PathLinkOneWayCommand))
            pathLink = new PathLinkOneWayCommand();
        else if (targetType == typeof(PathLinkTwoWayCommand))
            pathLink = new PathLinkTwoWayCommand();
        else
            throw new OmsiIniSerializationException();

        pathLink.a = (int)OmsiIniSerializer<PathLinkSerializer>.DeserializeSimpleType(reader, typeof(int), ref lineNumber, null, targetType);
        pathLink.b = (int)OmsiIniSerializer<PathLinkSerializer>.DeserializeSimpleType(reader, typeof(int), ref lineNumber, null, targetType);

        // Get the value of the last room height/step sound command; this is the whole reason we need this custom deserializer
        pathLink.roomHeight = paths._nextRoomHeight?.height ?? 0;
        pathLink.stepSound = paths._nextStepSound?.index ?? 0;

        if (targetType == typeof(PathLinkOneWayCommand))
            target = (PathLinkOneWayCommand) pathLink;
        else if (targetType == typeof(PathLinkTwoWayCommand))
            target = (PathLinkTwoWayCommand) pathLink;
    }

    public void Serialize(object? obj, Type objType, string? command, TextWriter writer, object? parent)
    {
        if (parent is not OmsiPaths paths)
            throw new OmsiIniSerializationException($"PathLinkSerializer expected the [pathlink] command to belong to an {nameof(OmsiPaths)}!");

        if (obj is not PathLinkCommand link)
            throw new OmsiIniSerializationException();

        // If the step sound/room height needs to change for this next PathLink, write the relevant command now.
        if (link.stepSound != paths.prevStepSound)
        {
            writer.WriteLine("[next_stepsound]");
            writer.WriteLine(link.stepSound);
            writer.WriteLine();
        }

        if (link.roomHeight != paths.prevRoomHeight)
        {
            writer.WriteLine("[next_roomheight]");
            writer.WriteLine(link.roomHeight);
            writer.WriteLine();
        }

        writer.WriteLine(command);
        writer.WriteLine(link.a);
        writer.WriteLine(link.b);

        paths.prevRoomHeight = link.roomHeight;
        paths.prevStepSound = link.stepSound;
    }
}

[OmsiIniCommand("___pathlink")]
[OmsiIniDerivedCommand<PathLinkOneWayCommand>]
[OmsiIniDerivedCommand<PathLinkTwoWayCommand>]
[OmsiIniCommandSerializer<PathLinkSerializer>]
public class PathLinkCommand
{
    public int a;
    public int b;

    // We have to do some jiggery pokery to get [next_stepsound] and [next_roomheight] commands to work with this serializer.
    [OmsiIniSkip] public int stepSound;
    [OmsiIniSkip] public float roomHeight;

    [OmsiIniComments] public string? comments;
}

[OmsiIniCommand("pathlink")]
public class PathLinkTwoWayCommand : PathLinkCommand { }

[OmsiIniCommand("pathlink_oneway")]
public class PathLinkOneWayCommand : PathLinkCommand { }

[OmsiIniCommand("stepsoundpack")]
public struct StepSoundPackCommand
{
    public string[] soundFiles;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("next_stepsound")]
public struct NextStepSoundCommand
{
    public int index;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("next_roomheight")]
public struct NextRoomHeightCommand
{
    public float height;

    [OmsiIniComments] public string comments;
}
