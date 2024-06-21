using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Numerics;
using O3DParse;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.IO;
using O3DParse.Ini;
#if ALLOW_DECRYPTION
using O3DParse.Encryption;
#endif

namespace O3DView
{
    class Program
    {
        [NotNullIfNotNull(nameof(renderer))]
        public SceneGraph? SceneGraph => renderer?.scene;
        public SceneObject? SelectedObject => selectedObject;
        public readonly string appdataPath;
        public float FPS => fps;
        public int RenderedVerts => renderer?.RenderedVerts ?? 0;
        public int RenderedTris => renderer?.RenderedTris ?? 0;
        public int DrawCalls => renderer?.DrawCalls ?? 0;
        public bool selectionHighlight = false;
        public bool fullbright = false;
        public bool wireframe = false;
        public float nightmapStrength = 0;
        public float lightmapStrength = 0;
        public OmsiCFGFile? lastLoadedCFG = null;
        public string? lastLoadedCFGPath = null;
        public static bool cacheMaterials = false;

        private readonly TimeSpan maxLoadTimePerBatch = TimeSpan.FromMilliseconds(25);
        public readonly string logFileName = "O3DView-log.txt";
#if DEBUG
        public bool logToFile = true;
#else
        public bool logToFile = false;
#endif

        private SceneObject? selectedObject;
        private Renderer? renderer;
        private IInputContext? inputContext;
        private IKeyboard? mainKeyboard;
        private GUIController? guiController;
        private float fps;
        private readonly ConcurrentQueue<LoadMeshItem> fileLoadQueue = [];
        private int fileLoadTotal = 0;
        private readonly Stopwatch loadFilesSW = new();

        private string[]? args;

        [STAThread]
        static void Main(string[] args)
        {
            Program program = new();
            program.args = args;
            program.StartApp();
        }

        public Program()
        {
            appdataPath = Environment.ExpandEnvironmentVariables(@"%APPDATA%\O3DView");
            if (!Directory.Exists(appdataPath))
                Directory.CreateDirectory(appdataPath);
        }

        public void StartApp()
        {
            var wndOptions = new WindowOptions(ViewOptions.Default);
            wndOptions.Samples = 4;
            wndOptions.Title = "O3DView - Fast O3D File Viewer";
            wndOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new(4, 3));
            using var window = Window.Create(wndOptions);
            GL? gl = null;

            window.Load += () =>
            {
                gl = window.CreateOpenGL();
#if DEBUG
                gl.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DebugSeverityLow, null, true);
                gl.DebugMessageCallback<nint>((GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam) =>
                {
                    if ((uint)severity == (uint)DebugSeverity.DebugSeverityNotification)
                        return;
                    string msg = Marshal.PtrToStringUTF8(message, length);
                    Debug.WriteLine($"[GL_Debug] [{(DebugSeverity)severity}] [{(DebugType)type}] {msg}");
                }, 0);
                gl.Enable(EnableCap.DebugOutput);
#endif
                inputContext = window.CreateInput();
                guiController = new(window, inputContext, gl, this);
                guiController.LogMessage($"OpenGL Version: '{gl.GetStringS(StringName.Version)}' " +
                    $"Vendor: '{gl.GetStringS(StringName.Vendor)}' " +
                    $"Renderer: '{gl.GetStringS(StringName.Renderer)}' " +
                    $"GLSL Version: '{gl.GetStringS(StringName.ShadingLanguageVersion)}'");
                SetupInput();
                renderer = new(gl, window, (string caption, string message) => guiController.ShowErrorPopup(caption, message), this);
                renderer.scene.camera.Mouse = inputContext.Mice.FirstOrDefault();
                renderer.scene.camera.Keyboard = mainKeyboard;

                Focus(null);
                if (args?.Length > 0)
                {
                    LoadFiles(args.Select(x => new LoadMeshItem(x)));
                }
            };

            // Handle resizes
            window.FramebufferResize += size =>
            {
                if (gl == null)
                    return;

                gl.Viewport(size);
            };

            // The render function
            window.Render += delta =>
            {
                if (guiController == null || renderer == null)
                    return;

                fps = ((float)(1 / delta) * .1f + fps * .9f);

                renderer.Render(delta);

                guiController.Render(delta);

                if (!fileLoadQueue.IsEmpty)
                    LoadFilesBatch();
            };

            window.FileDrop += (files) =>
            {
                LoadFiles(files.Select(x => new LoadMeshItem(x)));
            };

            // The closing function
            window.Closing += () =>
            {
                guiController?.Dispose();
                gl?.Dispose();
                inputContext?.Dispose();
            };

            window.Run();
        }

        private void SetupInput()
        {
            if (inputContext == null)
                return;

            mainKeyboard = inputContext.Keyboards.FirstOrDefault();
            /*if (mainKeyboard != null)
            {
                mainKeyboard.KeyDown += MainKeyboard_KeyDown;
                mainKeyboard.KeyUp += MainKeyboard_KeyUp;
            }

            for (int i = 0; i < inputContext.Mice.Count; i++)
            {
                var mouse = inputContext.Mice[i];
                mouse.Cursor.CursorMode = CursorMode.Normal;
                mouse.MouseMove += HandleMouseMove;
                mouse.Scroll += HandleMouseWheel;
                mouse.MouseDown += HandleMouseDown;
                mouse.MouseUp += HandleMouseUp;
            }*/
        }

        private void LoadFilesBatch()
        {
            loadFilesSW.Restart();
            do
            {
                if (fileLoadQueue.TryDequeue(out var file))
                {
                    string ext = Path.GetExtension(file.path);
                    switch (ext)
                    {
                        case ".sco":
                            LoadSco(file.path);
                            break;
                        case ".cfg":
                            LoadCFGModel(file.path);
                            break;
                        case ".ovh":
                        case ".bus":
                            LoadBus(file.path);
                            break;
                        case ".o3d":
                        case ".rdy":
                            LoadO3D(file);
                            break;
                        default:
                            guiController?.ShowErrorPopup($"Error Loading File: '{file}'", $"Unsupported file extension '{ext}'!");
                            break;
                    }
                }
            } while (!fileLoadQueue.IsEmpty && loadFilesSW.Elapsed < maxLoadTimePerBatch);
        }

        public void LoadFiles(IEnumerable<LoadMeshItem> files)
        {
            guiController?.ShowProgressBar($"Loading files...");
            if (renderer == null || SceneGraph == null)
                return;
            Task.Run(() =>
            {
                foreach (var file in files)
                {
                    if (File.Exists(file.path))
                    {
                        fileLoadQueue.Enqueue(file);
                        fileLoadTotal++;
                    }
                    else if (Directory.Exists(file.path))
                    {
                        var subfiles = Directory.GetFiles(file.path, "*.o3d", SearchOption.AllDirectories);
                        foreach (var subfile in subfiles)
                        {
                            fileLoadQueue.Enqueue(new(subfile, null, null));
                            fileLoadTotal++;
                        }
                    }
                }

                // Technically a race condition, but for now, who cares...
                if (guiController?.IsProgressBarVisible ?? true)
                    return;

                guiController?.ShowProgressBar($"Loading {fileLoadTotal} files...");
                Stopwatch sw = Stopwatch.StartNew();
                while (!fileLoadQueue.IsEmpty)
                {
                    Thread.Sleep(maxLoadTimePerBatch);
                    guiController?.UpdateProgress(1 - fileLoadQueue.Count / MathF.Max(fileLoadTotal - 1, 1));
                }
                guiController?.HideProgressBar();
                sw.Stop();
                fileLoadTotal = 0;
                Focus(null);
                guiController?.LogMessage($"Loading completed in {sw.Elapsed}!");
            });
        }

        private void LoadO3D(LoadMeshItem file)
        {
            try
            {
                var o3d = O3DParser.ReadO3D(file.path);
#if ALLOW_DECRYPTION
                        if (o3d.encryptionKey != 0xffffffff)
                            O3DEncryption.DecryptO3D(ref o3d);
#endif
                if (renderer == null || SceneGraph == null)
                    return;
                var mesh = renderer.CreateMeshFromO3D(o3d, file.path, file.cfgMesh, file.cfgPath);
                SceneGraph.meshes.Add(mesh);
                guiController?.LogMessage($"Loaded file: '{file.path}'");
            }
            catch (Exception ex)
            {
                guiController?.ShowErrorPopup($"Error Loading O3D File: '{file}'", ex.ToString());
            }
        }

        public void LoadBus(string path)
        {
            try
            {
                string basePath = Path.GetDirectoryName(path) ?? throw new Exception($"Couldn't get directory of '{path}'!");
                basePath = Path.Combine(basePath, "model");

                using var fs = File.OpenRead(path);
                var cfg = OmsiIniSerializer.DeserializeIniFile<OmsiRoadVehicle>(fs, recursive: true, filepath: path);
                lastLoadedCFG = cfg;
                lastLoadedCFGPath = path;

                LoadMapObj(path, basePath, cfg);
                if (cfg.model != null)
                    LoadMapObj(path, basePath, cfg.model);

                guiController?.LogMessage($"Loaded bus: '{path}'");
            }
            catch (Exception ex)
            {
                guiController?.ShowErrorPopup($"Error Loading Bus File: '{path}'", ex.ToString());
            }
        }

        public void LoadSco(string path)
        {
            try
            {
                string basePath = Path.GetDirectoryName(path) ?? throw new Exception($"Couldn't get directory of '{path}'!");
                basePath = Path.Combine(basePath, "model");

                using var fs = File.OpenRead(path);
                var cfg = OmsiIniSerializer.DeserializeIniFile<OmsiComplMapObj>(fs, recursive: true, filepath: path);
                lastLoadedCFG = cfg;
                lastLoadedCFGPath = path;

                LoadMapObj(path, basePath, cfg);
                if (cfg.model != null)
                    LoadMapObj(path, basePath, cfg.model);

                guiController?.LogMessage($"Loaded sco: '{path}'");
            }
            catch (Exception ex)
            {
                guiController?.ShowErrorPopup($"Error Loading sco File: '{path}'", ex.ToString());
            }
        }

        public void LoadCFGModel(string path)
        {
            try
            {
                string basePath = Path.GetDirectoryName(path) ?? throw new Exception($"Couldn't get directory of '{path}'!");

                using var fs = File.OpenRead(path);
                var cfg = OmsiIniSerializer.DeserializeIniFile<OmsiCFGFile>(fs);
                lastLoadedCFG = cfg;
                lastLoadedCFGPath = path;

                LoadMapObj(path, basePath, cfg);

                guiController?.LogMessage($"Loaded cfg: '{path}'");
            }
            catch (Exception ex)
            {
                guiController?.ShowErrorPopup($"Error Loading CFG File: '{path}'", ex.ToString());
            }
        }

        private void LoadMapObj(string path, string basePath, OmsiCFGFile cfg)
        {
            MeshCommand[] meshes = [];
            InteriorLightCommand[] interiorLights = [];
            if (cfg.lods != null && cfg.lods.Length > 0)
            {
                meshes = cfg.lods[0].meshes ?? meshes;
                interiorLights = cfg.interiorLights ?? interiorLights;
            }

            if (cfg.meshes != null && cfg.meshes.Length > 0)
                meshes = [..meshes, ..cfg.meshes];
            if (cfg.interiorLights != null && cfg.interiorLights.Length > 0)
                interiorLights = [..interiorLights, ..cfg.interiorLights];

            if (meshes != null)
            {
                LoadFiles(meshes.Select(x =>
                {
                    if (x.path.EndsWith(".x"))
                    {
                        guiController?.LogMessage($"X file importing is not supported: {x.path}", severity: GUIController.Severity.Warning);
                        return new LoadMeshItem("");
                    }
                    return new LoadMeshItem(Path.Combine(basePath, x.path), x, path);
                }));
            }

            if (interiorLights != null)
            {
                foreach (var light in interiorLights)
                {
                    var l = new Light()
                    {
                        ambient = Vector3.Zero,
                        colour = light.colour.ToVector3,
                        intensity = 1,
                        range = light.range,
                        on = true,
                        mode = LightMode.Point,
                    };
                    l.transform.Pos = new(light.pos.X, light.pos.Z, light.pos.Y);
                    SceneGraph?.lights.Add(l);
                }
            }
        }

        public void Select(SceneObject? mesh)
        {
            selectedObject = mesh;
        }

        public void Focus(SceneObject? obj)
        {
            if (renderer == null)
                return;
            if ((obj == null || obj is not Mesh) && renderer.scene.meshes.Count > 0)
                renderer.scene.camera.Focus(renderer.scene.meshes.Select(x => x.Bounds).Aggregate((last, next) => last.Union(next)));
            else if (obj is Mesh mesh)
                renderer.scene.camera.Focus(mesh.Bounds);
            else
                renderer.scene.camera.Focus(new() { center = Vector3.Zero, size = Vector3.One * 5 });
        }

        public record struct LoadMeshItem
        {
            public string path;
            public MeshCommand? cfgMesh;
            public string? cfgPath;

            public LoadMeshItem(string path)
            {
                this.path = path;
            }

            public LoadMeshItem(string path, MeshCommand? cfgMesh, string? cfgPath)
            {
                this.path = path;
                this.cfgMesh = cfgMesh;
                this.cfgPath = cfgPath;
            }
        }
    }
}
