using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace O3DParse
{
    /// <inheritdoc cref="OmsiIniSerializer{T}"/>
    public static class OmsiIniSerializer
    {
        /// <inheritdoc cref="OmsiIniSerializer{T}.Deserialize(Stream, bool, bool, string?, HashSet{ValueTuple{string, string}}?)"/>
        /// <typeparam name="T">A serializable type annotated appropriately with <see cref="OmsiIniCommandAttribute"/> attributes.</typeparam>
        public static T DeserializeIniFile<T>(Stream stream, bool leaveOpen = true, bool recursive = false, string? filepath = null
#if DEBUG
            , HashSet<(string parent, string command)>? unparsedCommands = null
#endif
            ) where T : new()
        {
            var serializer = new OmsiIniSerializer<T>();
            return serializer.Deserialize(stream, leaveOpen, recursive, filepath
#if DEBUG
                , unparsedCommands
#endif
                );
        }

        /// <inheritdoc cref="OmsiIniSerializer{T}.Serialize(T, Stream, bool)"/>
        /// <typeparam name="T">A serializable type annotated appropriately with <see cref="OmsiIniCommandAttribute"/> attributes.</typeparam>
        public static void SerializeIniFile<T>(T obj, Stream stream, bool leaveOpen = true) where T : new()
        {
            var serializer = new OmsiIniSerializer<T>();
            serializer.Serialize(obj, stream, leaveOpen);
        }
    }

    /// <summary>
    /// A generic serializer/deserializer for Omsi Ini files.
    /// </summary>
    /// <typeparam name="T">A serializable type annotated appropriately with <see cref="OmsiIniCommandAttribute"/> attributes.</typeparam>
    public class OmsiIniSerializer<T> where T : new()
    {
        private readonly List<string> _dbg_parsedCommands = [];
        private int lineNumber = 0;
        private readonly Dictionary<Type, FieldInfo[]> simpleTypeFieldsCache = [];
        private readonly Dictionary<Array, CachedArray> arrayCache = [];
        private readonly RefStack<Array> cachedArrays = [];
        private readonly RefStack<(object obj, CommandItem? cmd)> parents = [];
#if DEBUG
        private HashSet<(string parent, string command)>? unparsedCommands = null;
#endif
        private static readonly Dictionary<Type, CommandItem> commandTreeCache = [];

        #region Deserializer
        /// <summary>
        /// Deserializes an Ini file from a stream into a new instance of type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="stream">The stream to parse.</param>
        /// <param name="leaveOpen">Whether the stream should be left open after this method completes.</param>
        /// <param name="recursive">Whether parsable referenced files should also be deserialized. Requires the filepath to be set to take effect.</param>
        /// <param name="filepath">If using recursive parsing, the filepath of the file to deserialize.</param>
        /// <param name="unparsedCommands">[Debug only] Optionally, a set to populate with any unparsed commands.</param>
        /// <returns>A new instance of <typeparamref name="T"/> containing the deserialized data.</returns>
        public T Deserialize(Stream stream, bool leaveOpen = true, bool recursive = false, string? filepath = null
#if DEBUG
            , HashSet<(string parent, string command)>? unparsedCommands = null
#endif
            )
        {
            /* Some explanation of what we're trying to do:
             * A cfg file looks like:
             *      [LOD]   <- command
             *      0.1     <- args/simple fields
             *      
             *      [mesh]
             *      filename.o3d
             *      
             *      [mesh]
             *      filename1.o3d
             *      
             *      [LOD]
             *      0.5
             *      
             *      [mesh]
             *      filename2.o3d
             * 
             * There is an implicit hierarchy in the commands:
             *    CFG
             *    |- LOD
             *    |  |- mesh
             *    |  \- mesh
             *    |- LOD
             *    |  \- mesh
             * 
             * To parse this:
             *  1. Read lines until we find a command [xxx]
             *  2. Determine which class/struct it belongs too
             *     \-> This must class/struct must be either a field of the last processed object or one of it's parents'
             *  3. Deserialize the simple fields of said struct/class
             *  4. If the struct/class belongs to a parent of the last processed object, then we've finished deserialising 
             *     the current object and it's value can be set in it's parent(s) fields.
             *     
             * As such:         [parent objects stack]
             *    CFG           push(CFG)
             *    |- LOD        push(LOD)
             *    |  |- mesh    push(mesh)
             *    |  \- mesh    v = pop(); peek().set(v); push(mesh)
             *    |- LOD        v = pop(); peek().set(v); v = pop(); peek().set(v); push(LOD)
             *    |  \- mesh    push(mesh)
             *                  v = pop(); peek().set(v); v = pop(); peek().set(v); v = pop(); peek().set(v);
             */

            // TODO: StreamReader makes a new StringBuild instance every call to readline, which could possibly be optimised...
            //using TextReader reader = new StreamReader(stream, leaveOpen: leaveOpen);
            using IniFileReader reader = new(stream, leaveOpen: leaveOpen);
            lineNumber = 0;

            // Use reflection to build a tree that describes the destination structure
            if (!commandTreeCache.TryGetValue(typeof(T), out CommandItem? commandTreeRoot))
            {
                commandTreeRoot = BuildCommandTree(typeof(T));
                commandTreeCache.Add(typeof(T), commandTreeRoot);
            }
            CommandItem lastCommand = commandTreeRoot;

#if DEBUG
            this.unparsedCommands = unparsedCommands;
#endif

            arrayCache.Clear();
            cachedArrays.Clear();
            parents.Clear();
            StringBuilder comments = new();
            parents.Push((new T(), null));
            while (reader.Peek() != -1)
            {
                if (!ReadCommand(reader, lastCommand, comments, out CommandItem? commandItem, out int commandHierarchyLevel))
                    break;
                lastCommand = commandItem;
                _dbg_parsedCommands.Add(commandItem.type?.Name ?? "null");

                // If needed set the values of any objects we've finished deserializing on their parents
                PopulateParentObjects(commandHierarchyLevel);

                object commandObj = Activator.CreateInstance(commandItem.type!)!;

                // Deserialize the commands fields
                if (commandItem.customSerializer is IOmsiIniCommandCustomSerializer customSerializer)
                {
                    object? parent = parents.Count > 0 ? parents.Peek().obj : null;
                    customSerializer.Deserialize(ref commandObj, commandItem.type!, reader, ref lineNumber, parent);
                }
                else
                {
                    foreach (var field in commandItem.simpleFields)
                    {
                        if (!DeserializeBasicItem(ref commandObj, field, commandItem.type!, reader))
                            break;
                        if (recursive && commandItem.subFileField != null && field == commandItem.subFileField && filepath != null)
                        {
                            var fieldVal = field.GetValue(commandObj);
                            if (fieldVal is not string npath)
                                throw new OmsiIniSerializationException($"OmsiIniCommandFile must be a string!", field, commandItem.type!, lineNumber);

#if DEBUG
                            // Fail silently in debug builds
                            try
                            {
#endif
                                npath = Path.Combine(Path.GetDirectoryName(filepath) ?? "", npath);
                                using var fs = File.OpenRead(npath);

                                var deserializer = typeof(OmsiIniSerializer).GetMethod(nameof(OmsiIniSerializer.DeserializeIniFile))?.MakeGenericMethod(commandItem.type!);
                                if (deserializer == null)
                                    throw new OmsiIniSerializationException($"Couldn't construct deserializer for {field.FieldType}!", field, commandItem.type!, lineNumber);

                                // WARNING: If the method signature of DeserializeIniFile changes, this will fail at runtime!
                                commandObj = deserializer.Invoke(null, [fs, true, recursive, npath
#if DEBUG
                                , unparsedCommands
#endif
                                ])!;

                                // Reset the path which has now been discarded
                                field.SetValue(commandObj, fieldVal);
#if DEBUG
                            }
                            catch { }
#endif
                        }
                    }
                }

                if (commandItem.commentField is FieldInfo commentField)
                {
                    if (comments.Length > 0)
                    {
                        commentField.SetValue(commandObj, comments.ToString());
                        comments.Clear();
                    }
                }

                if ((commandItem.skip & SkipSerialization.Deserialization) == 0)
                    parents.Push((commandObj, commandItem));
            }

            // Finish populating the parent objects
            PopulateParentObjects(parents.Count - 1);

            // Now that we're done populate all the cached arrays
            // TODO: I'm having problems with getting references to value types here :-(
            /*foreach (var cached in arrayCache.Values)
            {
                var elType = cached.field.FieldType.GetElementType();
                var array = Array.CreateInstance(elType!, cached.list.Count);
                for (int i = 0; i < array.Length; i++)
                    array.SetValue(cached.list[i], i);
                var parent = cached.parent;
                //if (parent is RefContainer.Ref parentRef)
                //    parent = refStructs.Get(parentRef);
                cached.field.SetValue(parent, array);
            }*/

            var ret = parents.Peek();
            PopulateCachedArrays(ref ret.obj, commandTreeRoot);

            if (ret.cmd?.postCommentField is FieldInfo postCommentField)
            {
                if (comments.Length > 0)
                    postCommentField.SetValue(ret.obj, comments.ToString());
            }

            return (T)ret.obj;
        }

        private bool ReadCommand(IniFileReader reader, CommandItem commandTree, StringBuilder comments, [MaybeNullWhen(false)] out CommandItem commandItem, out int hierarchyLevel)
        {
            commandItem = null;
            hierarchyLevel = 0;
            bool enabled = true;
            Span<char> lineBuff = stackalloc char[1024];
            while (true)
            {
                int charsRead = reader.ReadLine(lineBuff);
                if (charsRead < 0)
                    return false;
                Span<char> line = lineBuff[..Math.Min(lineBuff.Length, charsRead)];
                lineNumber++;

                if (!enabled)
                {
                    if (line.StartsWith("-<ENABLED>-"))
                        enabled = true;
                    goto SkipLine;
                }
                else
                {
                    if (line.StartsWith("-<DISABLED>-"))
                    {
                        enabled = false;
                        goto SkipLine;
                    }
                }

                string command; // Sadly, for now dicts don't support Span<char> lookups (coming in .NET9)
                if (!commandTree.childrenAreVerbatim)
                {
                    // Determine if a command (a line begining with a '[' char followed by a word and then another ']' char)
                    if (line.Length <= 2 || line[0] != '[' || line[^1] != ']')
                        goto SkipLine;

                    command = line[1..^1].ToString();
                }
                else
                {
                    command = line.ToString();
                }

                // Work out which command was invoked
                hierarchyLevel = 0;
                if (!commandTree.children.TryGetValue(command, out commandItem))
                {
                    // Try searching the parents of the command tree for the command
                    var candTree = commandTree.parent;
                    while (candTree != null)
                    {
                        // Parent items are not verbatim, trim the [] chars...
                        if (commandTree.childrenAreVerbatim && !candTree.childrenAreVerbatim)
                        {
                            if (line.Length <= 2 || line[0] != '[' || line[^1] != ']')
                                goto SkipLine;

                            command = line[1..^1].ToString();
                        }

                        hierarchyLevel++;
                        if (candTree.children.TryGetValue(command, out commandItem))
                            break;

                        candTree = candTree.parent;
                    }
                }

                if (commandItem != null)
                    return true;

#if DEBUG
                unparsedCommands?.Add((commandTree.command ?? "ROOT", command));
#endif
            SkipLine:
                if (AppendComment(reader, comments, line, lineBuff, charsRead))
                    continue;
                else
                    return false;
            }

            static bool AppendComment(IniFileReader reader, StringBuilder comments, Span<char> line, Span<char> lineBuff, int charsRead)
            {
                comments.Append(line);
                while (charsRead > line.Length)
                {
                    charsRead = reader.ReadLine(lineBuff);
                    if (charsRead < 0)
                        return false;
                    comments.Append(lineBuff[..Math.Min(lineBuff.Length, charsRead)]);
                }
                comments.AppendLine();
                return true;
            }
        }

        private void PopulateParentObjects(int commandHierarchyLevel)
        {
            for (int i = 0; i < commandHierarchyLevel; i++)
            {
                var (finishedObj, finishedCmd) = parents.Pop();
                var parentObj = parents.Peek().obj;

                if (finishedCmd == null || finishedCmd.field == null)
                    throw new NullReferenceException();

                PopulateCachedArrays(ref finishedObj, finishedCmd);

                finishedObj = OmsiIniSerializer<T>.ConvertDerivedObject(finishedObj, finishedCmd);

                if (finishedCmd.isArray)
                {
                    // Arrays are a special case, since we want to append to them instead of set them
                    var prevArray = (Array?)finishedCmd.field.GetValue(parentObj);
#if false
                    if (prevArray != null)
                    {
                        var array = Array.CreateInstance(finishedCmd.storageType!, prevArray.Length + 1);
                        Array.Copy(prevArray, array, prevArray.Length);
                        array.SetValue(finishedObj, prevArray.Length);
                        finishedCmd.field.SetValue(parentObj, array);
                    }
                    else
                    {
                        var array = Array.CreateInstance(finishedCmd.storageType!, 1);
                        array.SetValue(finishedObj, 0);
                        finishedCmd.field.SetValue(parentObj, array);
                    }
#else
                    List<object?> parentList;
                    if (prevArray != null && arrayCache.TryGetValue(prevArray, out CachedArray cached))
                    {
                        parentList = cached.list;
                    }
                    else
                    {
                        // While building the object we create a temporary list to reduce the amount of array reallocations needed;
                        // these lists will be converted back to arrays later.
                        var array = Array.CreateInstance(finishedCmd.storageType!, 0);
                        finishedCmd.field.SetValue(parentObj, array);
                        List<object?> list = [];
                        arrayCache.Add(array, new(list, parentObj, finishedCmd.field));
                        cachedArrays.Push(array);
                        parentList = list;
                    }
                    parentList.Add(finishedObj);
#endif
                }
                else
                {
                    finishedCmd.field.SetValue(parentObj, finishedObj);
                }
            }
        }

        private static object? ConvertDerivedObject(object? obj, CommandItem item)
        {
            if (item.type == item.storageType)
                return obj;
            if (obj == null || item.type == null || item.storageType == null)
                return null;
            if (item.type.IsAssignableTo(item.storageType))
                return obj;

            var dst = Activator.CreateInstance(item.storageType);
            foreach ((var srcField, var dstField) in item.storageFields)
            {
                var val = srcField.GetValue(obj);
                dstField.SetValue(dst, val);
            }

            return dst;
        }

        private void PopulateCachedArrays(ref object obj, CommandItem item)
        {
            foreach (var child in item.children.Values)
            {
                if (!child.isArray)
                    continue;

                if (child?.field?.GetValue(obj) is not Array arr)
                    continue;

                if (!arrayCache.Remove(arr, out var cached))
                    continue;

                var newArr = Array.CreateInstance(child.storageType!, cached.list.Count);
                for (int i = 0; i < newArr.Length; i++)
                    newArr.SetValue(cached.list[i], i);

                cached.field.SetValue(obj, newArr);
            }
        }

        private static bool IsSimple(Type type)
        {
            return type.IsPrimitive
              || type.IsEnum
              //|| type.Equals(typeof(string))
              || type.Equals(typeof(decimal));
        }

        private bool DeserializeBasicItem(ref object parent, FieldInfo field, Type parentType, IniFileReader reader)
        {
            var targetType = field.FieldType;
            if (IsSimple(targetType))
            {
                object value = DeserializeSimpleType(reader, targetType, ref lineNumber, field, parentType);

                field.SetValue(parent, value);
            }
            else if (targetType.IsArray)
            {
                var elType = targetType.GetElementType()!; // We know that targetType is an array hence this never returns null

                Span<char> line = stackalloc char[256];
                var read = reader.ReadLine(line);
                if (read < 0)
                    throw new OmsiIniSerializationException($"Reached the end of the file unexpectedly!", field, parentType, lineNumber);
                if (read > line.Length)
                    throw new OmsiIniSerializationException($"Value of '{line}' couldn't be parsed as the length of an array of {elType.Name}! Line was too long to be parsed.", field, parentType, lineNumber);
                line = line[..read];

                lineNumber++;
                if (!uint.TryParse(line, out uint arrSize))
                    throw new OmsiIniSerializationException($"Value of '{line}' couldn't be parsed as the length of an array of {elType.Name}!", field, parentType, lineNumber);

                var arr = Array.CreateInstance(elType, arrSize);
                for (int i = 0; i < arrSize; i++)
                    arr.SetValue(DeserializeSimpleType(reader, elType, ref lineNumber, field, parentType), i);
                field.SetValue(parent, arr);
            }
            else if (targetType == typeof(string))
            {
                if (field.GetCustomAttribute<OmsiIniOptionalAttribute>() is OmsiIniOptionalAttribute optional)
                {
                    try
                    {
                        object value = DeserializeSimpleType(reader, targetType, ref lineNumber, field, parentType);
                        field.SetValue(parent, value);
                    }
                    catch
                    {
                        field.SetValue(parent, optional.DefaultValue);
                        return false;
                    }
                }
                else
                {
                    object value = DeserializeSimpleType(reader, targetType, ref lineNumber, field, parentType);
                    field.SetValue(parent, value);
                }
            }
            else if (Nullable.GetUnderlyingType(targetType) is Type nullableType)
            {
                try
                {
                    object value = DeserializeSimpleType(reader, nullableType, ref lineNumber, field, parentType);

                    field.SetValue(parent, value);
                }
                catch
                {
                    if (field.GetCustomAttribute<OmsiIniOptionalAttribute>() is OmsiIniOptionalAttribute optional)
                        field.SetValue(parent, optional.DefaultValue);
                    return false;
                }
                //throw new OmsiIniSerializationException($"Nullable command parameters are not yet supported!", field, parentType, lineNumber);
            }
            else
            {
                if (!simpleTypeFieldsCache.TryGetValue(targetType, out var fields))
                {
                    fields = targetType.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    simpleTypeFieldsCache.Add(targetType, fields);
                }
                var o = field.GetValue(parent);
                o ??= Activator.CreateInstance(targetType);
                if (o == null)
                    throw new OmsiIniSerializationException($"An instance of {targetType.Name} can't be created (is it a nullable?).", field, parentType);
                foreach (var childField in fields)
                    if (!DeserializeBasicItem(ref o, childField, targetType, reader))
                        break;
                field.SetValue(parent, o);
            }

            return true;
        }

        /// <summary>
        /// Deserializes a single field from an ini file.
        /// </summary>
        /// <param name="reader">The text reader to read from.</param>
        /// <param name="targetType">The type of the field to deserialize.</param>
        /// <param name="lineNumber">The line number to increment upon successful deserialization.</param>
        /// <param name="field">For improved error logging, the FieldInfo this field belong to.</param>
        /// <param name="parentType">For improved error logging, the type of the parent object to this field.</param>
        /// <returns>The deserialized field as an object.</returns>
        /// <exception cref="OmsiIniSerializationException"></exception>
        public static object DeserializeSimpleType(IniFileReader reader, Type targetType, ref int lineNumber, FieldInfo? field = null, Type? parentType = null)
        {
            var simpleType = targetType;
            if (targetType.IsEnum)
                simpleType = targetType.GetEnumUnderlyingType();
            object value;
            Span<char> line = stackalloc char[256];
            var read = reader.ReadLine(line);
            if (read < 0)
                throw new OmsiIniSerializationException($"Reached the end of the file unexpectedly!", field, parentType, lineNumber);
            if (read > line.Length)
                throw new OmsiIniSerializationException($"Value of '{line}' couldn't be parsed as a {targetType.Name}! Line was too long to be parsed.", field, parentType, lineNumber);
            line = line[..read];

            try
            {
                if (simpleType == typeof(bool))
                    value = int.Parse(line) != 0;
                else if (simpleType == typeof(byte))
                    value = byte.Parse(line);
                else if (simpleType == typeof(sbyte))
                    value = sbyte.Parse(line);
                else if (simpleType == typeof(char))
                    value = line[0];//char.Parse(line);
                else if (simpleType == typeof(decimal))
                    value = decimal.Parse(line);
                else if (simpleType == typeof(double))
                    value = double.Parse(line);
                else if (simpleType == typeof(float))
                    value = float.Parse(line);
                else if (simpleType == typeof(int))
                    value = int.Parse(line);
                else if (simpleType == typeof(uint))
                    value = uint.Parse(line);
                else if (simpleType == typeof(nint))
                    value = nint.Parse(line);
                else if (simpleType == typeof(long))
                    value = long.Parse(line);
                else if (simpleType == typeof(ulong))
                    value = ulong.Parse(line);
                else if (simpleType == typeof(short))
                    value = short.Parse(line);
                else if (simpleType == typeof(ushort))
                    value = ushort.Parse(line);
                else if (simpleType == typeof(string))
                    value = line.ToString();
                else
                    throw new OmsiIniSerializationException($"Fields of type {targetType.Name} are not supported!", field, parentType, lineNumber);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new OmsiIniSerializationException($"Value of '{line}' couldn't be parsed as a {targetType.Name}!", field, parentType, lineNumber, ex);
            }

            if (targetType.IsEnum)
                value = Enum.ToObject(targetType, value);

            lineNumber++;
            return value;
        }
        #endregion Deserializer

        #region Serializer
        /// <summary>
        /// Serializes an object of type <typeparamref name="T"/> into an Ini file.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="stream">A writable stream to serialize the object into.</param>
        /// <param name="leaveOpen">Whether the stream should be closed automatically when this method exits.</param>
        public void Serialize(T obj, Stream stream, bool leaveOpen = true)
        {
            using TextWriter writer = new StreamWriter(stream, leaveOpen: leaveOpen);
            lineNumber = 0;
            simpleTypeFieldsCache.Clear();

            // Use reflection to build a tree that describes the destination structure
            if (!commandTreeCache.TryGetValue(typeof(T), out CommandItem? commandTreeRoot))
            {
                commandTreeRoot = BuildCommandTree(typeof(T));
                commandTreeCache.Add(typeof(T), commandTreeRoot);
            }

            var commandItem = commandTreeRoot;
            var commandObj = obj;
            SerializeCommand(writer, commandItem, commandObj, null);
        }

        private void SerializeCommand(TextWriter writer, CommandItem commandItem, object? commandObj, object? parent)
        {
            if (commandObj == null)
                return;

            // Write any pre-comments
            if (commandItem.commentField is FieldInfo commentsField
                && commentsField.GetValue(commandObj) is string comments)
            {
                writer.Write(comments);
            }

            if ((commandItem.skip & SkipSerialization.Serialization) != 0)
                return;

            // Resolve polymorphic commands
            if (commandItem.isPolymorphic)
            {
                var t = commandObj.GetType();
                if (commandTreeCache.TryGetValue(t, out var newCommandItem))
                {
                    if (newCommandItem != null)
                        commandItem = newCommandItem;
                } else
                {
                    string? cmdStr = t.GetCustomAttribute<OmsiIniCommandAttribute>()?.CommandIdentifier;
                    if (cmdStr != null && commandItem.parent is CommandItem parentCmd)
                    {
                        if (parentCmd.childrenAreVerbatim && !commandItem.isVerbatim)
                            cmdStr = $"[{cmdStr}]";

                        if (parentCmd.children.TryGetValue(cmdStr, out var newCommandItem1))
                        {
                            commandTreeCache.Add(t, newCommandItem1);
                            commandItem = newCommandItem1;
                        }
                    }
                }
            }

            // Write the simple fields
            if (commandItem.customSerializer is IOmsiIniCommandCustomSerializer customSerializer)
            {
                string? cmd = commandItem.isVerbatim ? commandItem.command : $"[{commandItem.command}]";
                customSerializer.Serialize(commandObj, commandItem.type!, cmd, writer, parent);
            }
            else
            {
                // Write the command
                if (commandItem.command is string cmd)
                {
                    if (commandItem.isVerbatim)
                    {
                        writer.WriteLine(cmd);
                    }
                    else
                    {
                        writer.Write('[');
                        writer.Write(cmd);
                        writer.WriteLine(']');
                    }
                    lineNumber++;
                }

                foreach (var sfield in commandItem.simpleFields)
                {
                    WriteSimpleField(writer, sfield.GetValue(commandObj), sfield.FieldType);
                }
            }

            // Now write the contents of each child
            // Only write the root object, all children which have a [OmsiIniCommandFile] attribute should be written to separate files.
            if (commandItem.parent == null || commandItem.subFileField == null)
            {
                foreach (var child in commandItem.children.Values)
                {
                    // If this command is for a derived type, skip it, it's contents are in the storage type already.
                    if (child.type != child.storageType)
                        continue;

                    var childObj = child.field?.GetValue(commandObj);

                    if (child.type == null)
                        continue;
                    if ((child.isNullable || child.isArray) && childObj == null)
                        continue;

                    if (child.isArray && childObj is Array childArray)
                    {
                        foreach (var v in childArray)
                            SerializeCommand(writer, child, v, commandObj);
                    }
                    else
                    {
                        SerializeCommand(writer, child, childObj, commandObj);
                    }
                }
            }

            // Write any post-comments
            if (commandItem.postCommentField is FieldInfo postCommentField
                && postCommentField.GetValue(commandObj) is string postComments)
            {
                writer.Write(postComments);
            }
        }

        private void WriteSimpleField(TextWriter writer, object? val, Type ftype)
        {
            if (val is bool valBool)
            {
                writer.WriteLine(valBool ? (byte)1 : (byte)0);
                lineNumber++;
            }
            else if (ftype.IsPrimitive || ftype.Equals(typeof(string)) || ftype.Equals(typeof(decimal)))
            {
                writer.WriteLine(val);
                lineNumber++;
            }
            else if (ftype.IsEnum)
            {
                var baseType = Enum.GetUnderlyingType(ftype);
                writer.WriteLine(Convert.ChangeType(val, baseType));
                lineNumber++;
            }
            else if (Nullable.GetUnderlyingType(ftype) is Type nullableType)
            {
                WriteSimpleField(writer, val, nullableType);
            }
            else if (ftype.IsArray && val is Array valArr)
            {
                writer.WriteLine(valArr.Length);
                lineNumber++;
                var itemType = ftype.GetElementType();
                if (itemType == null)
                    return;
                foreach (var v in valArr)
                    WriteSimpleField(writer, v, itemType);
            }
            else
            {
                if (!simpleTypeFieldsCache.TryGetValue(ftype, out var fields))
                {
                    fields = ftype.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    simpleTypeFieldsCache.Add(ftype, fields);
                }
                foreach (var childField in fields)
                    WriteSimpleField(writer, childField.GetValue(val), childField.FieldType);
            }
        }
        #endregion Serializer


        private struct FieldAttributes
        {
            public OmsiIniCommandAttribute? tCommand; // Applies to the field type
            //public OmsiIniCommandSerializerAttribute? serializer;
            public OmsiIniVerbatimCommandAttribute? tVerbatim; // Applies to the field type
            public List<Attribute?>? tDerived; // Applies to the field type

            public OmsiIniCommentsAttribute? comments;
            public OmsiIniCommandFileAttribute? commandFile;
            public OmsiIniSkipAttribute? skip;
            public OmsiIniOptionalAttribute? optional;

            public Type? type;
        }

        /// <summary>
        /// Builds a tree of type metadata used to help the serializer/deserializer.
        /// </summary>
        /// <param name="parent">The type to build a command tree for.</param>
        /// <param name="myField">Optionally, the field that the given type belongs to.</param>
        /// <param name="parentItem">Optionally, the command item that this type is a child of.</param>
        /// <returns>A command item, which contains metadata for the given type.</returns>
        /// <exception cref="OmsiIniSerializationException"></exception>
        private static CommandItem BuildCommandTree(Type parent, 
            FieldInfo? myField = null, CommandItem? parentItem = null, SkipSerialization skipSerialization = SkipSerialization.None, bool isPolymorphic = false)
        {
            // Get the metadata for this type
            var cmdAttr = parent.GetCustomAttribute<OmsiIniCommandAttribute>();
            var serializerAttr = parent.GetCustomAttribute(typeof(OmsiIniCommandSerializerAttribute<>));
            var customSerializer = serializerAttr?.GetType()?.GenericTypeArguments[0];
            CommandItem cmd = new()
            {
                type = parent,
                storageType = parent,
                field = myField,
                parent = parentItem,
                command = cmdAttr?.CommandIdentifier,
                isVerbatim = parent.GetCustomAttribute<OmsiIniVerbatimCommandAttribute>() is not null,
                recursiveDepth = parentItem?.recursiveDepth ?? 0,
                childrenAreVerbatim = parentItem?.childrenAreVerbatim ?? false,
                skip = skipSerialization,
                isPolymorphic = isPolymorphic
            };
            if (customSerializer != null)
                cmd.customSerializer = Activator.CreateInstance(customSerializer) as IOmsiIniCommandCustomSerializer;

            if (myField?.FieldType is Type myType)
            {
                if (Nullable.GetUnderlyingType(myType) is Type underlyingType)
                {
                    cmd.isNullable = true;
                    myType = underlyingType;
                }
                if (myType.IsArray)
                    cmd.isArray = true;
                cmd.storageType = cmd.isArray ? myType.GetElementType() : myType;
            }

            // Recursion limit for self-referencial types (trees, linked lists, etc...)
            if (cmd.recursiveDepth >= CommandItem.MAX_RECURSE_DEPTH)
                return cmd;

            var fields = parent.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var fieldAttributes = new Dictionary<FieldInfo, FieldAttributes>();

            // Get child field metadata & build attribute cache
            foreach (var field in fields)
            {
                var ftype = field.FieldType;
                ftype = Nullable.GetUnderlyingType(ftype) ?? ftype;
                if (ftype?.IsArray ?? false)
                    ftype = ftype.GetElementType();

                var attrCache = new FieldAttributes();
                var typeAttrs = ftype?.GetCustomAttributes(true) ?? [];
                var fieldAttrs = field.GetCustomAttributes(true);
                foreach (var attr in typeAttrs)
                    switch (attr)
                    {
                        case OmsiIniCommandAttribute command:
                            attrCache.tCommand = command;
                            break;
                        case IOmsiIniDerivedCommandAttribute derived:
                            attrCache.tDerived ??= [];
                            attrCache.tDerived.Add(attr as Attribute);
                            break;
                        case OmsiIniVerbatimCommandAttribute verbatim:
                            attrCache.tVerbatim = verbatim;
                            break;
                    }

                foreach (var attr in fieldAttrs)
                    switch (attr)
                    {
                        case OmsiIniCommentsAttribute comments:
                            attrCache.comments = comments;
                            break;
                        case OmsiIniCommandFileAttribute commandFile:
                            attrCache.commandFile = commandFile;
                            break;
                        case OmsiIniSkipAttribute skip:
                            attrCache.skip = skip;
                            break;
                        case OmsiIniOptionalAttribute optional:
                            attrCache.optional = optional;
                            break;
                    }
                attrCache.type = ftype;

                fieldAttributes.Add(field, attrCache);

                if (attrCache.tVerbatim is not null)
                    cmd.childrenAreVerbatim = true;
            }

            // Build child field subtrees
            foreach (var field in fields)
            {
                var fdata = fieldAttributes[field];
                //if (fdata.skip != null)
                //    continue;

                if (fdata.comments != null)
                {
                    if (fdata.comments.PostComments)
                        cmd.postCommentField = field;
                    else
                        cmd.commentField = field;
                }
                else
                {
                    if (fdata.type == null)
                        continue;

                    if (fdata.tCommand != null)
                    {
                        string cmdIdent = fdata.tCommand.CommandIdentifier;
                        bool shouldBracketCommand = cmd.childrenAreVerbatim && fdata.tVerbatim == null;
                        if (shouldBracketCommand)
                            cmdIdent = $"[{cmdIdent}]";

                        if (cmd.parent?.children?.ContainsKey(cmdIdent) ?? false)
                            cmd.recursiveDepth++;

                        var skipMode = fdata.skip?.Skip ?? SkipSerialization.None;
                        cmd.children.TryAdd(cmdIdent, BuildCommandTree(fdata.type, field, cmd, skipMode, fdata.tDerived != null));

                        if (fdata.tDerived != null)
                        {
                            foreach (var derivedCommand in fdata.tDerived)
                            {
                                var derivedType = derivedCommand?.GetType()?.GenericTypeArguments[0];
                                if (derivedType == null)
                                    continue;
                                if (derivedType.GetCustomAttribute<OmsiIniCommandAttribute>() is not OmsiIniCommandAttribute derivedCommandAttr)
                                    throw new OmsiIniSerializationException(
                                        $"Can't use type {derivedType?.FullName} as a derived type as it has no OmsiIniCommand attribute!", field, parent);

                                string derivedCommandIdent = derivedCommandAttr.CommandIdentifier;
                                if (shouldBracketCommand)
                                    derivedCommandIdent = $"[{derivedCommandIdent}]";

                                cmd.children.Add(derivedCommandIdent, BuildCommandTree(derivedType, field, cmd, skipMode, true));
                            }
                        }
                    }

                    if (fdata.commandFile != null)
                    {
                        cmd.subFileField = field;
                    }
                }
            }

            // Find all the "simple" child fields (ie: those with types that aren't associated with a command)
            HashSet<string> simpleFieldNames = [];
            cmd.simpleFields = fields.Where(x => {
                var fdata = fieldAttributes[x];
                return fdata.tCommand == null
                    && fdata.comments == null
                    && fdata.skip == null
                    && simpleFieldNames.Add(x.Name);
                })
                .ToArray();
            if (cmd.storageType != cmd.type)
            {
                var storageFields = cmd.storageType!.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var storageFieldsDict = new Dictionary<string, FieldInfo>(storageFields.Select(x => new KeyValuePair<string, FieldInfo>(x.Name, x)));
                cmd.storageFields = fields.Where(x => storageFieldsDict.ContainsKey(x.Name))
                    .Select(x => (x, storageFieldsDict[x.Name]))
                    .ToArray();
            }
            return cmd;
        }

        private struct CachedArray(List<object?> list, object parent, FieldInfo field)
        {
            public List<object?> list = list;
            public object parent = parent;
            public FieldInfo field = field;
        }

        class CommandItem
        {
            public string? command;
            public bool isVerbatim;
            public IOmsiIniCommandCustomSerializer? customSerializer;
            public CommandItem? parent;
            public Type? type;
            public Type? storageType;
            public bool isNullable = false;
            public bool isArray = false;
            public bool isPolymorphic = false;
            public SkipSerialization skip = SkipSerialization.None;
            public FieldInfo? field;
            public FieldInfo[] simpleFields = []; // Fields which can be deserialised without needing to parse additional commands
            public FieldInfo? commentField;
            public FieldInfo? postCommentField;
            public FieldInfo? subFileField; // The field with the [OmsiIniCommandFile] Attribute
            public Dictionary<string, CommandItem> children = []; // Fields associated with additional sub-commands
            public bool childrenAreVerbatim;
            public (FieldInfo src, FieldInfo dst)[] storageFields = [];
            public int recursiveDepth = 0;

            public const int MAX_RECURSE_DEPTH = 10;

            public CommandItem() { }
        }
    }

    /// <summary>
    /// Defines a custom serializer/deserializer for an Ini command.
    /// </summary>
    public interface IOmsiIniCommandCustomSerializer
    {
        /// <summary>
        /// Implements deserialization of a single command. This should read the necessary lines from the 
        /// reader, populating the target object and incrementing the line number as needed.
        /// </summary>
        /// <param name="target">The object to populate with deserialized data.</param>
        /// <param name="targetType">The type of the target object.</param>
        /// <param name="reader">The instance of the text reader for the ini file.</param>
        /// <param name="lineNumber">The current line number in the ini file.</param>
        /// <param name="parent">The object that the target object belongs to or null for root objects.</param>
        public void Deserialize(ref object target, Type targetType, IniFileReader reader, ref int lineNumber, object? parent);
        /// <summary>
        /// Implements serialization of a single command. This should serialize the command name and all the needed fields of 
        /// command, writing them to the text writer.
        /// </summary>
        /// <param name="obj">The object to seriallize.</param>
        /// <param name="objType">The type of the object to serialize.</param>
        /// <param name="commandName">The name of the command to be serialized (including square brackets if needed).</param>
        /// <param name="writer">The instance of the text writer for the ini file.</param>
        /// <param name="parent">The parent object of the object to serialize or null for root objects.</param>
        public void Serialize(object? obj, Type objType, string? commandName, TextWriter writer, object? parent);
    }

    /// <summary>
    /// Marks a class or struct as being an Ini file command.
    /// </summary>
    /// <param name="commandIdentifier">The ini file command name, not including square brackets. 
    /// For commands which don't include square brackets, add an <see cref="OmsiIniVerbatimCommandAttribute"/></param>
    [AttributeUsage(System.AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class OmsiIniCommandAttribute(string commandIdentifier) : Attribute
    {
        readonly string commandIdentifier = commandIdentifier;

        public string CommandIdentifier => commandIdentifier;
    }

    /// <summary>
    /// Overrides the serialization and deserialization behaviour for the fields of the annnotated struct.
    /// </summary>
    /// <remarks>
    /// This does not override the deserialization of child commands!
    /// </remarks>
    /// <typeparam name="T">An <see cref="IOmsiIniCommandCustomSerializer"/></typeparam>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class OmsiIniCommandSerializerAttribute<T>() : Attribute where T : IOmsiIniCommandCustomSerializer { }

    interface IOmsiIniDerivedCommandAttribute { }

    /// <summary>
    /// Deserializes commands of type <typeparamref name="T"/> into the annotated type.
    /// Useful for commands which have been extended. For instance a [texturetexture] can be deserialized into a [texttexture_enh].
    /// </summary>
    /// <remarks>
    /// This attribute should be placed on a type which has enough corresponding fields to fully contain the other type.
    /// In the case of [texturetexture] and [texttexture_enh], the <see cref="OmsiIniDerivedCommandAttribute{T}"/> would be placed 
    /// on the [texttexture_enh] as it can fully contain a [texturetexture].
    /// </remarks>
    /// <typeparam name="T">An <see cref="IOmsiIniCommandCustomSerializer"/></typeparam>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class OmsiIniDerivedCommandAttribute<T>() : Attribute, IOmsiIniDerivedCommandAttribute { }

    /// <summary>
    /// Informs the serializer that this command does not need to be surrounded by square brackets. This incurs a small performance cost.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class OmsiIniVerbatimCommandAttribute() : Attribute { }

    /// <summary>
    /// Marks a field for storage of comments and unparsed commands in an Ini file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class OmsiIniCommentsAttribute(bool postComments = false) : Attribute
    {
        readonly bool postComments = postComments;

        public bool PostComments => postComments;
    }

    /// <summary>
    /// Marks a field as optional in an Ini file.
    /// </summary>
    /// <remarks>
    /// This only applies parameters of commands, not child commands. The field must also have a nullable type.
    /// </remarks>
    /// <param name="defaultValue">The value to assign to the optional field if it isn't found.</param>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class OmsiIniOptionalAttribute(object? defaultValue = null) : Attribute
    {
        readonly object? defaultValue = defaultValue;

        public object? DefaultValue => defaultValue;
    }

    /// <summary>
    /// Marks a field as a path to an Ini field of the type of the parent class/struct.
    /// <para/>
    /// This allows child Ini files referenced by Ini files to be parsed automatically.
    /// </summary>
    /// <remarks>
    /// Only valid on <see cref="string"/> fields.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class OmsiIniCommandFileAttribute() : Attribute { }

    [Flags]
    public enum SkipSerialization
    {
        None = 0,
        Serialization = 1 << 0,
        Deserialization = 1 << 1,
        Both = Serialization | Deserialization,
    }

    /// <summary>
    /// Skips this field from serialization/deserialization.
    /// </summary>
    /// <param name="skip">Whether serialization or deserialization should be skipped (only applies to sub-commands, simple fields ignore this)</param>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class OmsiIniSkipAttribute(SkipSerialization skip = SkipSerialization.Both) : Attribute
    {
        readonly SkipSerialization skip = skip;

        public SkipSerialization Skip => skip;
    }

    public class OmsiIniSerializationException : SerializationException
    {
        public OmsiIniSerializationException() { }
        public OmsiIniSerializationException(string message) : base(message) { }
        public OmsiIniSerializationException(string? message, FieldInfo? field, Type? parent, int? lineNumber = null)
            : base(MakeMessage(message, field, parent, lineNumber)) { }
        public OmsiIniSerializationException(string? message, FieldInfo? field, Type? parent, Exception? innerException)
            : base(MakeMessage(message, field, parent, null), innerException) { }
        public OmsiIniSerializationException(string? message, FieldInfo? field, Type? parent, int? lineNumber, Exception? innerException)
            : base(MakeMessage(message, field, parent, lineNumber), innerException) { }

        private static string? MakeMessage(string? message, FieldInfo? field, Type? parent, int? lineNumber)
        {
            if (field != null && parent != null)
            {
                return $"Couldn't deserialize field [{field.FieldType.Name} {field.Name}] of {parent.Name}! " +
                    $"{(lineNumber != null ? $" At file line {lineNumber}" : "")}" +
                    $"\n{message}";
            }
            return message;
        }
    }
}
