using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using O3DParse;
using O3DParse.Ini;

namespace O3DParse.Test;

internal class Program
{
    const int testIters = 100;

    static void Main(string[] args)
    {
        //string path = args[0];
        //string path = @"export_test.cfg"; // args[0]
        //string path = @"C:\Program Files\OMSI 2\Vehicles\GPM_C2\C2_NGT_V3.bus";
        //string path = @"C:\Program Files\OMSI 2\maps\Grand Paris-Moulon\tile_32_-21.map";
        //string path = @"C:\Program Files\OMSI 2\Vehicles\GPM_C2\Model\modeldata_C2_NGT_V3.cfg";
        //string path = @"C:\Program Files\OMSI 2\Vehicles\MB_C2_EN_BVG\MB_KI_C2_E6_Gn_UE_main.bus";
        string path = @"C:\Program Files\OMSI 2\Vehicles\MAN_SD202\Model\paths.cfg";

        Console.WriteLine("#############################");
        Console.WriteLine("#####  O3DParse Tests  ######");
        Console.WriteLine("#############################");

        string tmpPath = path;//@"C:\Program Files\OMSI 2\Vehicles\MB_C2_EN_BVG\MB_KI_C2_E6_Gn_UE_main.bus";
        using var fs = File.OpenRead(tmpPath);
        var cfg = OmsiIniSerializer.DeserializeIniFile<OmsiPaths>(fs, recursive: true, filepath: tmpPath);

        SpeedTest<OmsiPaths>(path);
        UnparsedCommands<OmsiPaths>(path);
        ReExport<OmsiPaths>(path);

        foreach (var p in args)
        {
            UnparsedCommands<OmsiCFGFile>(p);
        }

        TestMany();

        Console.ReadKey();
    }

    public static void SpeedTest<T>(string path) where T : new()
    {
        Console.WriteLine("\n## Start Speed Test ##");

        using var rs = new MemoryStream(File.ReadAllBytes(path));
        using var ws = new MemoryStream(8192);
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < testIters; i++)
        {
            //using var fs = File.Open(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            OmsiIniSerializer.DeserializeIniFile<T>(rs);
            rs.Position = 0;
        }
        TimeSpan readTime = sw.Elapsed / testIters;

        var cfg = OmsiIniSerializer.DeserializeIniFile<T>(rs);
        sw.Restart();
        for (int i = 0; i < testIters; i++)
        {
            OmsiIniSerializer.SerializeIniFile<T>(cfg, ws);
            ws.Position = 0;
        }
        TimeSpan writeTime = sw.Elapsed / testIters;
        sw.Stop();
        Console.WriteLine($"Parsed Ini file ({path}); deserialized in: {readTime}, serialized in: {writeTime}; averaged over {testIters} iterations!");
    }

    public static void UnparsedCommands<T>(string path) where T : new()
    {
        Console.WriteLine("\n## Start Unparsed Commands Test ##");

        HashSet<(string parent, string command)> unparsed = [];
        using var fs = File.OpenRead(path);
        //OmsiIniSerializer.DeserializeIniFile<OmsiCFGFile>(fs, unparsedCommands: unparsed);
        OmsiIniSerializer.DeserializeIniFile<T>(fs,
#if DEBUG
            unparsedCommands: unparsed, 
#endif
            filepath: path, recursive: true);

        Console.WriteLine($"{unparsed.Count} unparsed commands in {path}:");
        List<(string parent, string command)> unparsedSorted = new(unparsed);
        unparsedSorted.Sort((x, y) => x.command.CompareTo(y.command));
        foreach (var (parent, command) in unparsedSorted)
            Console.WriteLine($"\t[{command}] after [{parent}]");
    }

    public static void ReExport<T>(string path) where T : new()
    {
        Console.WriteLine("\n## Start Re-Export Test ##");

        using var fs = File.OpenRead(path);
        using var ws = new MemoryStream(8192);
        //var cfg = OmsiIniSerializer.DeserializeIniFile<OmsiCFGFile>(fs);
        var cfg = OmsiIniSerializer.DeserializeIniFile<T>(fs, filepath: path, recursive: true);
        OmsiIniSerializer.SerializeIniFile(cfg, ws);

        var s = Encoding.Default.GetString(ws.ToArray());
        File.WriteAllText("re-export-test.cfg", s);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            IncludeFields = true,
            WriteIndented = true,
            IgnoreReadOnlyFields = true,
            IgnoreReadOnlyProperties = true,
        };
        string json = JsonSerializer.Serialize(cfg, jsonOptions);
        File.WriteAllText("re-export-test.json", json);
    }

    public static void TestMany()
    {
        Console.WriteLine("\n## Start Many Files Test ##");

        // Test on every *.sco|*.cfg|*.bus|*.map|*.cti in my Omsi dir
        //if (!File.Exists("test_files.txt"))
        //    return;

        //var lines = File.ReadAllLines("test_files.txt");
        string dir = @"C:\Program Files\OMSI 2\";
        var lines = Directory.EnumerateFiles(dir, "*.sco", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(dir, "*.cfg", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(dir, "*.bus", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(dir, "*.map", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(dir, "*.cti", SearchOption.AllDirectories))
            .ToArray();

        ProgressBarString progress = new(30);
        int total = lines.Length;
        int curr = 0;
        int consoleWidth = Console.BufferWidth;
        Console.WriteLine($"Processing {lines.Length} files...\n");
        var progressUpdater = Task.Run(() =>
        {
            while (curr < total)
            {
                progress.SetProgress(curr, total);

                Console.Write("\x1b[1A");
                Console.WriteLine($"Parsing [{progress}] [{curr + 1,6}/{total}] '{System.IO.Path.GetFileName(lines[Math.Min(curr, total - 1)])}'..."
                    .PadRight(consoleWidth - 1)[..(consoleWidth - 1)]);

                Thread.Sleep(20);
            }
        });

        HashSet<(string parent, string command)> unparsed = [];
        ConcurrentBag<(Exception ex, string file)> errors = [];

        var cfgSerializer = new ThreadLocal<OmsiIniSerializer<OmsiCFGFile>>(() => new());// new OmsiIniSerializer<OmsiCFGFile>();
        var cmoSerializer = new ThreadLocal<OmsiIniSerializer<OmsiComplMapObj>>(() => new());

        Stopwatch sw = Stopwatch.StartNew();
        foreach (var line in lines)
        //Parallel.ForEach(lines, line =>
        {
            Interlocked.Increment(ref curr);

            try
            {
                var cfg = LoadOmsiFile(line, unparsed);
            }
            catch (Exception ex)
            {
                errors.Add((ex, line));
            }

            //UnparsedCommands(line);
        }//);
        curr = total;
        Console.WriteLine($"DONE IN {sw.Elapsed}!");

        // Print all the unparsed commands...
        Console.WriteLine($"{unparsed.Count} unparsed commands in:");
        List<(string parent, string command)> unparsedSorted = new(unparsed);
        unparsedSorted.Sort((x, y) => x.command.CompareTo(y.command));
        foreach (var (parent, command) in unparsedSorted)
            Console.WriteLine($"\t[{command}] after [{parent}]");

        // Print all the errors commands...
        Console.WriteLine($"{errors.Count} exceptions while parsing:");
        foreach (var (ex, file) in errors)
            Console.WriteLine($"\t[{file}] {ex.Message}");
    }

    private static object? LoadOmsiFile(string line, HashSet<(string parent, string command)> unparsedCommands)
    {
        using var fs = File.OpenRead(line);
        object? cfg = null;
        switch (System.IO.Path.GetExtension(line))
        {
            case ".cfg":
                if (line.Contains("maps")
                    || line.Contains("Weather")
                    || line.Contains("texture", StringComparison.CurrentCultureIgnoreCase)
                    || line.Contains("template")
                    || line.Contains("sounds"))
                    break;

                cfg = OmsiIniSerializer.DeserializeIniFile<OmsiCFGFile>(fs,
#if DEBUG
            unparsedCommands: unparsedCommands, 
#endif
                    recursive: true, filepath: line);
                break;
            case ".sco":
            case ".cti":
            case ".map":
                cfg = OmsiIniSerializer.DeserializeIniFile<OmsiComplMapObj>(fs,
#if DEBUG
            unparsedCommands: unparsedCommands, 
#endif
                    recursive: true, filepath: line);
                break;
            case ".bus":
            case ".ovh":
                cfg = OmsiIniSerializer.DeserializeIniFile<OmsiRoadVehicle>(fs,
#if DEBUG
            unparsedCommands: unparsedCommands, 
#endif
                    recursive: true, filepath: line);
                break;
        }

        return cfg;
    }
}
