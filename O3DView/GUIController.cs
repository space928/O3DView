using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Microsoft.Win32;
using O3DParse;
using O3DParse.Ini;

#if ALLOW_DECRYPTION
using O3DParse.Encryption;
#endif

namespace O3DView
{
    internal partial class GUIController : IDisposable
    {
        private readonly ImGuiController controller;
        private readonly GL gl;
        private readonly IInputContext inputContext;
        private readonly IWindow window;
        private readonly Program program;
        private readonly SynchronizationContext syncContext;
        private readonly GCHandle imguiIniPathHandle;
        private readonly List<(Severity sev, string caption, string msg)> errorLog = [];
        private readonly string[] blendModes;
        private readonly string[] depthModes;
        private readonly string[] lightModes;

        private readonly float windowSnapRange = 10;

        private readonly object logFileLock = new();
        private float loadingProgress = -1;
        private string loadingMessage = "";
        private bool aboutWindowOpen = false;
        private bool errorMessageOpen = false;
        private bool errorLogOpen = false;
        private bool perfOverlayOpen = false;
        private bool cfgViewerOpen = false;
        private string? errorMessage = null;
        private string? errorCaption = null;
#if DEBUG
        private bool imGUIDemoOpen = false;
#endif
        private bool shouldRepositionWindows = true;
        private WindowSnapEdge inspectorWindowSnap;
        private WindowSnapEdge hierarchyWindowSnap;
        private WindowSnapEdge aboutWindowSnap;
        private WindowSnapEdge logWindowSnap;
        private WindowSnapEdge cfgWindowSnap;

        private CFGFieldCache cfgFieldCache;

        public GUIController(IWindow window, IInputContext inputContext, GL gl, Program program)
        {
            this.window = window;
            this.gl = gl;
            this.inputContext = inputContext;
            controller = new ImGuiController(
                gl,
                window,
                inputContext
            );
            unsafe
            {
                string imguiIniPath = Path.Combine(program.appdataPath, "imgui.ini");
                imguiIniPathHandle = GCHandle.Alloc(Encoding.UTF8.GetBytes(imguiIniPath), GCHandleType.Pinned);
                ImGui.GetIO().NativePtr->IniFilename = (byte*)imguiIniPathHandle.AddrOfPinnedObject();
            }
            this.program = program;
            syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

            blendModes = Enum.GetNames(typeof(Material.BlendMode));
            depthModes = Enum.GetNames(typeof(Material.DepthMode));
            lightModes = Enum.GetNames(typeof(LightMode));

            var kb = inputContext.Keyboards.FirstOrDefault();
            if (kb != null)
            {
                kb.KeyDown += Kb_KeyDown;
            }

            window.FramebufferResize += Window_Resize;

            LogMessage("O3DView GUI Initialised!");
        }

        private void Kb_KeyDown(IKeyboard kb, Key key, int arg3)
        {
            switch (key)
            {
                case Key.O:
                    if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.Ctrl))
                        OpenFile();
                    break;
                case Key.F:
                    if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.None))
                        Focus();
                    break;
                case Key.P:
                    if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.Ctrl))
                        perfOverlayOpen = true;
                    break;
                case Key.S:
                    if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.Ctrl))
                        SaveFile(true, false);
                    else if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.Ctrl | ExtensionMethods.ModifierKeys.Shift))
                        SaveFile(true, true);
                    else if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.Ctrl | ExtensionMethods.ModifierKeys.Alt))
                        SaveFile(false, false);
                    break;
                case Key.Delete:
                    if (kb.IsModifierKeyDown(ExtensionMethods.ModifierKeys.None))
                        DeleteObj();
                    break;
                default:
                    break;
            }
        }

        public void Render(double delta)
        {
            controller.Update((float)delta);

            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("Open", "CTRL+O"))
                        OpenFile();
                    if (ImGui.MenuItem("Save Selection", "CTRL+S"))
                        SaveFile(true, false);
                    if (ImGui.MenuItem("Save Selection As", "CTRL+Shift+S"))
                        SaveFile(true, true);
                    if (ImGui.MenuItem("Save All", "CTRL+Alt+S"))
                        SaveFile(false, false);

                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Delete", "Delete"))
                        DeleteObj();
                    if (ImGui.MenuItem("Delete All"))
                        DeleteObj(true);
                    if (ImGui.MenuItem("Create Light"))
                        CreateLight();
                    ImGui.MenuItem("Should Cache Materials", "", ref Program.cacheMaterials);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("View"))
                {
                    if (ImGui.MenuItem("Focus", "F"))
                        Focus();
                    ImGui.MenuItem("Highlight Selection", "", ref program.selectionHighlight);
                    ImGui.MenuItem("Fullbright", "", ref program.fullbright);
                    ImGui.MenuItem("Wireframe", "", ref program.wireframe);
                    if (ImGui.MenuItem("Performance Overlay", "CTRL+P", ref perfOverlayOpen))
                        perfOverlayOpen = true;
                    if (ImGui.MenuItem("Log Window", "", ref errorLogOpen))
                        errorLogOpen = true;
                    if (ImGui.MenuItem("CFG Viewer Window", "", ref cfgViewerOpen))
                        cfgViewerOpen = true;
#if DEBUG
                    if (ImGui.MenuItem("ImGUI Debug"))
                        imGUIDemoOpen = true;
#endif
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Help"))
                {
                    if (ImGui.MenuItem("About"))
                        aboutWindowOpen = true;
                    if (ImGui.MenuItem("Register File Association"))
                        RegisterFileAssociation(true);
                    if (ImGui.MenuItem("Unregister File Association"))
                        RegisterFileAssociation(false);
                    ImGui.EndMenu();
                }

                ImGui.EndMainMenuBar();
            }

            DrawInspectorWindow();
            DrawHierarchyWindow();
#if DEBUG
            if (imGUIDemoOpen)
                ImGui.ShowDemoWindow(ref imGUIDemoOpen);
#endif
            DrawPerfOverlay();
            DrawLogWindow();
            DrawCFGViewerWindow();

            if (aboutWindowOpen)
                DrawAbout();

            if (loadingProgress >= 0)
                DrawLoadingBar();

            if (errorMessageOpen)
            {
                ImGui.OpenPopup("ErrorMessage");
                errorMessageOpen = false;
            }
            if (ImGui.BeginPopup("ErrorMessage"))
            {
                ImGui.Text(errorCaption);
                ImGui.TextColored(new System.Numerics.Vector4(1, .2f, .2f, 1), errorMessage);
                ImGui.EndPopup();
            }

            shouldRepositionWindows = false;

            controller.Render();
        }

        public void Dispose()
        {
            controller.Dispose();
            imguiIniPathHandle.Free();
        }

        private void Window_Resize(Silk.NET.Maths.Vector2D<int> size)
        {
            shouldRepositionWindows = true;
        }

        private void SnapWindowToEdge(ref WindowSnapEdge snapEdge)
        {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var viewSize = ImGui.GetWindowViewport().WorkSize;
            var workPos = ImGui.GetWindowViewport().WorkPos;
            pos -= workPos;

            var topLeft = Vector2.Abs(pos);
            var bottomRight = Vector2.Abs(pos + size - viewSize);
            var bottomRightNew = viewSize - size;

            if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Left) != 0)
                || (topLeft.X != 0 && topLeft.X <= windowSnapRange))
            {
                // Left snap
                pos.X = 0;
                ImGui.SetWindowPos(pos + workPos);
                snapEdge |= WindowSnapEdge.Left;
            }
            else if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Right) != 0)
                || (bottomRight.X != 0 && bottomRight.X <= windowSnapRange))
            {
                // Right snap
                pos.X = bottomRightNew.X;
                ImGui.SetWindowPos(pos + workPos);
                snapEdge |= WindowSnapEdge.Right;
            }
            else if (topLeft.X != 0 && bottomRight.X != 0)
            {
                // Clear any old left/right snaps if the window isn't on the edge
                snapEdge &= ~(WindowSnapEdge.Left | WindowSnapEdge.Right);
            }

            if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Top) != 0)
                || (topLeft.Y != 0 && topLeft.Y <= windowSnapRange))
            {
                // Top snap
                pos.Y = 0;
                ImGui.SetWindowPos(pos + workPos);
                snapEdge |= WindowSnapEdge.Top;
            }
            else if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Bottom) != 0)
                || (bottomRight.Y != 0 && bottomRight.Y <= windowSnapRange))
            {
                // Bottom snap
                pos.Y = bottomRightNew.Y;
                ImGui.SetWindowPos(pos + workPos);
                snapEdge |= WindowSnapEdge.Bottom;
            }
            else if (topLeft.Y != 0 && bottomRight.Y != 0)
            {
                // Clear any old top/bottom snaps if the window isn't on the edge
                snapEdge &= ~(WindowSnapEdge.Top | WindowSnapEdge.Bottom);
            }
        }

        public void ShowErrorPopup(string caption, string message)
        {
            LogMessage(message, caption, Severity.Error);
        }

        public void LogMessage(string message, [CallerMemberName] string? caption = null, Severity severity = Severity.Info)
        {
            errorLog.Add((severity, caption ?? severity.ToString(), message));
            if (severity >= Severity.Warning)
            {
                errorCaption = caption ?? severity.ToString();
                errorMessage = message;
                errorMessageOpen = true;
            }
            if (program.logToFile)
            {
                lock (logFileLock)
                {
                    File.AppendAllText(program.logFileName, $"[{DateTime.Now}] [{severity}] [{caption}] {message}\n");
                }
            }
        }

        public bool IsProgressBarVisible => loadingProgress == -1;

        public void ShowProgressBar(string message)
        {
            loadingMessage = message;
            if (loadingProgress < 0)
                loadingProgress = 0;
        }

        public void HideProgressBar()
        {
            loadingProgress = -1;
        }

        public void UpdateProgress(float fraction)
        {
            loadingProgress = fraction;
        }

        private void DrawInspectorWindow()
        {
            ImGui.Begin("Inspector");
            ImGui.SetWindowSize(new Vector2(280, 320), ImGuiCond.Once);
            ImGui.SetWindowPos(new Vector2(1, 50), ImGuiCond.Once);
            SnapWindowToEdge(ref inspectorWindowSnap);

            if (ImGui.CollapsingHeader("Scene", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.SliderFloat("Lightmap Strength", ref program.lightmapStrength, 0, 2);
                ImGui.SliderFloat("Nightmap Strength", ref program.nightmapStrength, 0, 2);
            }

            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), $"Selected: {program.SelectedObject?.name ?? "NONE"}");
            if (program.SelectedObject is SceneObject obj)
                ImGui.InputText("Name", ref obj.name, 255);
            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (program.SelectedObject != null)
                {
                    ref var xform = ref program.SelectedObject.transform;
                    var p = xform.Pos;
                    if (ImGui.DragFloat3("Position", ref p, 0.1f))
                        xform.Pos = p;
                    var r = xform.EulerRot * (180 / MathF.PI);
                    if (ImGui.DragFloat3("Rotation", ref r, 0.5f))
                        xform.EulerRot = r * (MathF.PI / 180);
                    //var q = (xform.Rot * (180 / MathF.PI));
                    //var qv = new Vector4(q.X, q.Y, q.Z, q.W);
                    //if (ImGui.DragFloat4("RotationQ", ref qv, 0.5f))
                     //   xform.Rot = new Quaternion(qv.X, qv.Y, qv.Z, qv.W) * (MathF.PI / 180);
                    var s = xform.Scale;
                    if (ImGui.DragFloat("Scale", ref s, 0.1f))
                        xform.Scale = s;
                }
            }

            switch (program.SelectedObject)
            {
                case O3DMesh mesh:
                    {
                        var o3d = mesh.O3D;
                        if (ImGui.CollapsingHeader("Loaded Model Properties", ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            ImGui.Text("File:");
                            ImGui.Indent();
                            ImGui.TextWrapped(Path.GetFileName(mesh.O3DPath));
                            ImGui.Unindent();

                            ImGui.Text($"Version: {o3d.version}");
                            ImGui.Text($"Encryption key: 0x{o3d.encryptionKey:X8}");
                            ImGui.Text($"Extended options: {o3d.extendedOptions}");
                            ImGui.Text($"Vertices: {o3d.vertices.Length}");
                            ImGui.Text($"Triangles: {o3d.triangles.Length}");
                            ImGui.Text($"Materials: {o3d.materials.Length}");
                            ImGui.Text($"Bones: {o3d.bones.Length}");
                        }

                        if (ImGui.CollapsingHeader("Materials", ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            for (int m = 0; m < o3d.materials.Length; m++)
                            {
                                ref var mat = ref o3d.materials[m];
                                var matRender = mesh.submeshes[m].mat;
                                if (ImGui.TreeNode($"Material {m}: '{mat.textureName}'"))
                                {
                                    Vector4 diff = mat.diffuse;
                                    if (ImGui.ColorEdit4("Diffuse", ref diff))
                                    {
                                        mat.diffuse = diff;
                                        matRender.UpdateFromO3D(mat, mesh.O3DPath);
                                    }
                                    Vector3 spec = mat.spec;
                                    if (ImGui.ColorEdit3("Specular", ref spec))
                                    {
                                        mat.spec = spec;
                                        matRender.UpdateFromO3D(mat, mesh.O3DPath);
                                    }
                                    float specPower = mat.specPower;
                                    if (ImGui.SliderFloat("Spec Power", ref specPower, 0, 128, "%3f", ImGuiSliderFlags.Logarithmic))
                                    {
                                        mat.specPower = specPower;
                                        matRender.UpdateFromO3D(mat, mesh.O3DPath);
                                    }
                                    Vector3 emit = mat.emission;
                                    if (ImGui.ColorEdit3("Emission", ref emit))
                                    {
                                        mat.emission = emit;
                                        matRender.UpdateFromO3D(mat, mesh.O3DPath);
                                    }
                                    if (!(matRender.texture?.IsValid ?? false))
                                    {
                                        ImGui.TextColored(new(1, .3f, .3f, 1), "Not Loaded!");
                                        ImGui.SameLine();
                                    }
                                    string tex = mat.textureName;
                                    if (ImGui.InputText("Texture", ref tex, 254))
                                    {
                                        mat.textureName = tex;
                                        matRender.UpdateFromO3D(mat, mesh.O3DPath);
                                    }
                                    ImGui.Text("Non-O3D Parameters:");
                                    var blendMode = (int)matRender.blendMode;
                                    if (ImGui.Combo("Blend Mode", ref blendMode, blendModes, blendModes.Length))
                                        matRender.blendMode = (Material.BlendMode)blendMode;
                                    var depthMode = (int)matRender.depthMode;
                                    if (ImGui.CheckboxFlags("ZTest", ref depthMode, (int)Material.DepthMode.ZTest))
                                        matRender.depthMode = (Material.DepthMode)depthMode;
                                    if (ImGui.CheckboxFlags("ZWrite", ref depthMode, (int)Material.DepthMode.ZWrite))
                                        matRender.depthMode = (Material.DepthMode)depthMode;
                                    ImGui.SliderFloat("Envmap Strength", ref matRender.envMapStrength, 0, 1, "%3f");
                                    ImGui.SliderFloat("Bumpmap Strength", ref matRender.bumpMapStrength, 0, 1, "%3f");
                                    //ImGui.BeginDisabled();
                                    ImGui.Text($"Transmap: {(matRender.transMap is O3DTexture transMap ? transMap.FileName : "NONE")}");
                                    ImGui.Text($"Bumpmap: {(matRender.bumpMap is O3DTexture bumpMap ? bumpMap.FileName : "NONE")}");
                                    ImGui.Text($"Envmap: {(matRender.envMap is O3DTexture envMap ? envMap.FileName : "NONE")}");
                                    ImGui.Text($"Lightmap: {(matRender.lightMap is O3DTexture lightMap ? lightMap.FileName : "NONE")}");
                                    ImGui.Text($"Nightmap: {(matRender.nightMap is O3DTexture nightMap ? nightMap.FileName : "NONE")}");
                                    //ImGui.EndDisabled();
                                    ImGui.TreePop();
                                }
                            }
                        }

                        break;
                    }

                case Light light:
                    if (ImGui.CollapsingHeader("Light Settings", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        ImGui.Checkbox("On", ref light.on);
                        int lightMode = (int)light.mode;
                        if (ImGui.Combo("Type", ref lightMode, lightModes, lightModes.Length))
                            light.mode = (LightMode)lightMode;
                        ImGui.SliderFloat("Intensity", ref light.intensity, 0, 10, "%3f", ImGuiSliderFlags.Logarithmic);
                        ImGui.ColorEdit3("Colour", ref light.colour);
                        ImGui.ColorEdit3("Ambient", ref light.ambient);
                        ImGui.SliderFloat("Range", ref light.range, 0, 100, "%3f", ImGuiSliderFlags.Logarithmic);
                    }
                    break;
                case Camera camera:
                    if (ImGui.CollapsingHeader("Camera Settings", ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        ImGui.SliderFloat("Move Speed", ref camera.moveSpeed, 0, 10, "%3f", ImGuiSliderFlags.Logarithmic);
                        ImGui.SliderFloat("Orbit Speed", ref camera.orbitSpeed, 0, 10, "%3f", ImGuiSliderFlags.Logarithmic);
                        ImGui.SliderFloat("Pan Speed", ref camera.panSpeed, 0, 10, "%3f", ImGuiSliderFlags.Logarithmic);
                        ImGui.SliderFloat("Zoom Speed", ref camera.zoomSpeed, 0, 10, "%3f", ImGuiSliderFlags.Logarithmic);
                    }
                    break;
            }
            ImGui.End();
        }

        private void DrawHierarchyWindow()
        {
            ImGui.Begin("Hierarchy");
            ImGui.SetWindowSize(new Vector2(280, 320), ImGuiCond.Once);
            ImGui.SetWindowPos(new Vector2(window.Size.X - 281, 50), ImGuiCond.Once);
            SnapWindowToEdge(ref hierarchyWindowSnap);

            if (ImGui.CollapsingHeader("Meshes"))
            {
                ImGui.PushID("Meshes");
                if (ImGui.BeginTable("MeshesTab", 2, ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    if (program.SceneGraph?.meshes is List<Mesh> meshes)
                    {
                        for (int m = 0; m < meshes.Count; m++)
                        {
                            ImGui.TableNextColumn();
                            var mesh = meshes[m];
                            ImGui.SetNextItemAllowOverlap();
                            ImGui.PushID(m);
                            ImGui.Checkbox("", ref meshes[m].visible);
                            ImGui.TableNextColumn(); ImGui.SameLine();
                            if (ImGui.Selectable(mesh.name, program.SelectedObject == mesh))
                            {
                                program.Select(mesh);
                            }
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndTable();
                }
                ImGui.PopID();
            }
            if (ImGui.CollapsingHeader("Lights"))
            {
                if (program.SceneGraph?.lights is List<Light> lights)
                {
                    ImGui.PushID("Lights");
                    if (ImGui.BeginTable("LightsTab", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed);
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                        for (int i = 0; i < lights.Count; i++)
                        {
                            ImGui.TableNextColumn();
                            var light = lights[i];
                            ImGui.SetNextItemAllowOverlap();
                            ImGui.PushID(i);
                            ImGui.Checkbox("", ref lights[i].on);
                            ImGui.TableNextColumn(); ImGui.SameLine();
                            if (ImGui.Selectable(light.name, program.SelectedObject == light))
                            {
                                program.Select(light);
                            }
                            ImGui.PopID();
                        }
                        ImGui.EndTable();
                    }
                    ImGui.PopID();
                }
            }
            if (ImGui.CollapsingHeader("Cameras"))
            {
                if (program.SceneGraph?.camera is Camera camera)
                {
                    ImGui.PushID("Cameras");
                    if (ImGui.BeginTable("CamerasTab", 2, ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed);
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemAllowOverlap();
                        bool t = true;
                        ImGui.Checkbox("", ref t);
                        ImGui.TableNextColumn(); ImGui.SameLine();
                        if (ImGui.Selectable(camera.name, program.SelectedObject == camera))
                        {
                            program.Select(camera);
                        }

                        ImGui.EndTable();
                    }
                    ImGui.PopID();
                }
            }

            ImGui.End();
        }

        private void DrawAbout()
        {
            ImGui.Begin("About", ref aboutWindowOpen);
            SnapWindowToEdge(ref aboutWindowSnap);

            ImGui.Text("O3DView - A simple O3D file viewer");
            ImGui.Separator();
            ImGui.Text("Copyright Thomas Mathieson 2024 All Rights Reserved");
#if DEBUG
            string build = "Debug";
#else
            string build = "Release";
#endif
            ImGui.Text($"Version: {Application.ProductVersion}-{build}");
            ImGui.Separator();

            ImGui.End();
        }

        private void DrawPerfOverlay()
        {
            if (perfOverlayOpen)
            {
                var overlayFlags = ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing;
                ImGui.SetNextWindowBgAlpha(0.4f);
                ImGui.SetNextWindowPos(new(8, ImGui.GetMainViewport().Size.Y - 8), ImGuiCond.Always, new(0, 1));
                if (ImGui.Begin("Perf Overlay", ref perfOverlayOpen, overlayFlags))
                {
                    ImGui.Text($"Verts:      {program.RenderedVerts,7}");
                    ImGui.Text($"Tris:       {program.RenderedTris,7}");
                    ImGui.Text($"Draw calls: {program.DrawCalls,7}");
                    ImGui.Text($"Lights:     {program.SceneGraph?.lights?.Count ?? 0,7}");
                    ImGui.Text($"FPS:        {program.FPS,7:F2}");
                    if (ImGui.BeginPopupContextWindow())
                    {
                        if (ImGui.MenuItem("Close"))
                            perfOverlayOpen = false;
                        ImGui.EndPopup();
                    }
                    ImGui.End();
                }
            }
        }

        private void DrawLoadingBar()
        {
            var overlayFlags = ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing;
            ImGui.SetNextWindowBgAlpha(0.4f);
            var viewportSize = ImGui.GetMainViewport().Size;
            ImGui.SetNextWindowPos(new(viewportSize.X / 2, viewportSize.Y - 8), ImGuiCond.Always, new(0.5f, 1));
            if (ImGui.Begin("Loading Bar", overlayFlags))
            {
                ImGui.Text(loadingMessage);
                ImGui.ProgressBar(loadingProgress, new(240, 16));
                ImGui.End();
            }
        }

        private void DrawLogWindow()
        {
            if (errorLogOpen)
            {
                if (ImGui.Begin("Log", ref errorLogOpen))
                {
                    ImGui.SetWindowSize(new(500, 300), ImGuiCond.Once);
                    SnapWindowToEdge(ref logWindowSnap);
                    if (ImGui.Button("Clear"))
                        errorLog.Clear();
                    ImGui.SameLine();
                    bool scrollToEnd = ImGui.Button("Scroll To End");
                    if (ImGui.BeginTable("LogTab", 4, ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit))
                    {
                        ImGui.TableSetupColumn("ID");
                        ImGui.TableSetupColumn("Severity");
                        ImGui.TableSetupColumn("Caption");
                        ImGui.TableSetupColumn("Message");
                        ImGui.TableHeadersRow();
                        for (int i = 0; i < errorLog.Count; i++)
                        {
                            ImGui.TableNextColumn();
                            ImGui.Text($"{i}");
                            var (sev, caption, msg) = errorLog[i];
                            ImGui.TableNextColumn();
                            ImGui.Text($"{sev}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{caption}");
                            ImGui.TableNextColumn();
                            ImGui.TextColored(sev >= Severity.Warning ? new(1, .5f, .5f, 1) : new(1, 1, 1, 1), $"{msg}");
                        }
                        if (scrollToEnd)
                            ImGui.SetScrollHereY();
                        ImGui.EndTable();
                    }
                    ImGui.End();
                }
            }
        }

        private struct CFGFieldCache
        {
            public FieldInfo? field;
            public Type type;
            public CFGFieldCache[]? children;
            public bool isArray;
            public bool isNullable;
            public FieldUIType uiType;
        }

        private enum FieldUIType
        {
            None,
            String,
            Bool,
            Int,
            UInt,
            Float,
            Vector2,
            Vector3,
            Vector4,
            ColourRGB,
            ColourRGBA
        }

        private FieldUIType DetermineUIType(Type type)
        {
            if (type == typeof(bool))
                return FieldUIType.Bool;
            else if (type == typeof(byte))
                return FieldUIType.UInt;
            else if (type == typeof(sbyte))
                return FieldUIType.Int;
            else if (type == typeof(char))
                return FieldUIType.String;
            else if (type == typeof(decimal))
                return FieldUIType.Float;
            else if (type == typeof(double))
                return FieldUIType.Float;
            else if (type == typeof(float))
                return FieldUIType.Float;
            else if (type == typeof(int))
                return FieldUIType.Int;
            else if (type == typeof(uint))
                return FieldUIType.UInt;
            else if (type == typeof(nint))
                return FieldUIType.UInt;
            else if (type == typeof(long))
                return FieldUIType.Int;
            else if (type == typeof(ulong))
                return FieldUIType.UInt;
            else if (type == typeof(short))
                return FieldUIType.Int;
            else if (type == typeof(ushort))
                return FieldUIType.UInt;
            else if (type == typeof(string))
                return FieldUIType.String;
            else if (type == typeof(Vector2))
                return FieldUIType.Vector2;
            else if (type == typeof(Vector3))
                return FieldUIType.Vector3;
            else if (type == typeof(Vector4))
                return FieldUIType.Vector4;
            else if (type == typeof(RGBColour))
                return FieldUIType.ColourRGB;
            else if (type == typeof(RGBAColour))
                return FieldUIType.ColourRGBA;

            return FieldUIType.None;
        }

        private CFGFieldCache BuildCFGFieldCache(Type type, FieldInfo? field = null, int recurseDepth = 0)
        {
            CFGFieldCache cache = new();
            var unitType = type;
            if (Nullable.GetUnderlyingType(unitType) is Type nullableType)
            {
                unitType = nullableType;
                cache.isNullable = true;
            }
            if (unitType?.IsArray ?? false)
            {
                unitType = unitType.GetElementType();
                cache.isArray = true;
            }
            if (unitType == null)
                return cache;

            cache.uiType = DetermineUIType(unitType);
            cache.type = unitType;
            cache.field = field;
            if (cache.uiType == FieldUIType.None && recurseDepth < 10)
            {
                var fields = unitType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                cache.children = fields.Where(c => 
                {
                    return c.GetCustomAttribute<OmsiIniCommentsAttribute>() == null
                        && c.GetCustomAttribute<OmsiIniCommandFileAttribute>() == null;
                }).Select(c =>
                {
                    return BuildCFGFieldCache(c.FieldType, c, recurseDepth + 1);
                }).ToArray();
            }

            return cache;
        }

        private void DrawCFGViewerWindow()
        {
            if (cfgViewerOpen)
            {
                if (ImGui.Begin("CFG Viewer", ref cfgViewerOpen))
                {
                    ImGui.SetWindowSize(new(500, 300), ImGuiCond.Once);
                    SnapWindowToEdge(ref cfgWindowSnap);

                    ImGui.TextWrapped("This window allows you to view the contents of the last loaded CFG/SCO/BUS/OVH file as OMSI " +
                        "would read it. This can be helpful when diagnosing weird behaviours with CFG files and to help understand " +
                        "the implicit hierarchy between the different tags. Currently, this window is read only.");
                    ImGui.Spacing();

                    ImGui.Text($"Loaded: '{program.lastLoadedCFGPath}'");
                    if (program.lastLoadedCFG == null || program.lastLoadedCFGPath == null)
                    {
                        ImGui.End();
                        return;
                    }

                    ImGui.Text($"Type: {program.lastLoadedCFG.GetType().Name}");
                    ImGui.Spacing();

                    if (program.lastLoadedCFG.GetType() != cfgFieldCache.type)
                        cfgFieldCache = BuildCFGFieldCache(program.lastLoadedCFG.GetType());

                    DrawCFGFieldCache(program.lastLoadedCFG, cfgFieldCache);

                    ImGui.End();
                }
            }
        }

        private void DrawCFGFieldCache(object? obj, CFGFieldCache fields)
        {
            if (obj == null)
                return;

            if (fields.uiType != FieldUIType.None && fields.field != null)
            {
                if (obj.GetType().IsArray)
                {
                    var arr = (Array)obj;
                    if (ImGui.TreeNode($"{fields.field.Name} ({arr.Length})"))
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            DrawSimpleField(arr.GetValue(i)!, $"{fields.field.Name}[{i}]", fields.uiType);
                        }
                        ImGui.TreePop();
                    }
                }
                else
                {
                    DrawSimpleField(obj, fields.field.Name, fields.uiType);
                }
                return;
            }

            string label = fields.field != null ? fields.field.Name : fields.type.Name;
            if (obj.GetType().IsArray)
            {
                var arr = (Array)obj;
                if (ImGui.TreeNode($"{label} ({arr.Length})"))
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        DrawSubTree(arr.GetValue(i), fields, $"{label}[{i}]");
                    }
                    ImGui.TreePop();
                }
            } else
            {
                DrawSubTree(obj, fields, label);
            }

            void DrawSubTree(object? obj, CFGFieldCache fields, string label)
            {
                if (ImGui.TreeNode(label))
                {
                    if (fields.children != null)
                    {
                        foreach (var child in fields.children)
                        {
                            if (child.field == null)
                                continue;
                            var childObj = child.field.GetValue(obj);
                            DrawCFGFieldCache(childObj, child);
                        }
                    }

                    ImGui.TreePop();
                }
            }
        }

        private static void DrawSimpleField(object obj, string fieldName, FieldUIType type)
        {
            switch (type)
            {
                case FieldUIType.String:
                    string valstr = (string)obj;
                    ImGui.InputText(fieldName, ref valstr, 255);
                    break;
                case FieldUIType.Bool:
                    bool valb = (bool)obj;
                    ImGui.Checkbox(fieldName, ref valb);
                    break;
                case FieldUIType.Int:
                    var vali = unchecked((int)obj);
                    ImGui.InputInt(fieldName, ref vali);
                    break;
                case FieldUIType.UInt:
                    var valui = unchecked((int)(uint)obj);
                    ImGui.InputInt(fieldName, ref valui);
                    break;
                case FieldUIType.Float:
                    var valf = (float)obj;
                    ImGui.InputFloat(fieldName, ref valf);
                    break;
                case FieldUIType.Vector2:
                    var valv2 = (Vector2)obj;
                    ImGui.InputFloat2(fieldName, ref valv2);
                    break;
                case FieldUIType.Vector3:
                    var valv3 = (Vector3)obj;
                    ImGui.InputFloat3(fieldName, ref valv3);
                    break;
                case FieldUIType.Vector4:
                    var valv4 = (Vector4)obj;
                    ImGui.InputFloat4(fieldName, ref valv4);
                    break;
                case FieldUIType.ColourRGB:
                    var valrgb = (RGBColour)obj;
                    var valrgbv = new Vector3(valrgb.r / 255f, valrgb.g / 255f, valrgb.b / 255f);
                    ImGui.ColorEdit3(fieldName, ref valrgbv);
                    break;
                case FieldUIType.ColourRGBA:
                    var valrgba = (RGBAColour)obj;
                    var valrgbav = new Vector4(valrgba.r / 255f, valrgba.g / 255f, valrgba.b / 255f, valrgba.a / 255f);
                    ImGui.ColorEdit4(fieldName, ref valrgbav);
                    break;
            }
        }

        private void OpenFile()
        {
            using OpenFileDialog dialog = new()
            {
                CheckFileExists = true,
                DefaultExt = ".o3d",
                Multiselect = true,
                Title = "Open An O3D Model File",
                Filter = "Supported files (*.o3d;*.cfg;*.sco;*.bus;*.ovh)|*.o3d;*.cfg;*.sco;*.bus;*.ovh" +
                "|O3D files (*.o3d)|*.o3d" +
                "|Model files (*.cfg)|*.cfg" +
                "|SCO files (*.sco)|*.sco" +
                "|BUS files (*.bus;*.ovh)|*.bus;*.ovh" +
                "|All files (*.*)|*.*"
            };
            var res = new DialogResult[1] { DialogResult.None };
            syncContext.Send(x =>
            {
                if (x is DialogResult[] res)
                    res[0] = dialog.ShowDialog();
            }, res);
            if (res[0] == DialogResult.OK)
            {
                program.LoadFiles(dialog.FileNames.Select(x => new Program.LoadMeshItem(x)));
            }
        }

        private void SaveFile(bool selectionOnly, bool saveAs)
        {
            string? dstPath = (program.SelectedObject is O3DMesh meshPath) ? meshPath.O3DPath : null;
            if (saveAs)
            {
                using SaveFileDialog dialog = new()
                {
                    DefaultExt = ".o3d",
                    AddExtension = true,
                    Title = "Save O3D As",
                    Filter = "o3d files (*.o3d)|*.o3d|All files (*.*)|*.*"
                };
                var res = new DialogResult[1] { DialogResult.None };
                syncContext.Send(x =>
                {
                    if (x is DialogResult[] res)
                        res[0] = dialog.ShowDialog();
                }, res);
                if (res[0] == DialogResult.OK)
                {
                    dstPath = dialog.FileName;
                }
                else
                {
                    return;
                }
            }
            if (selectionOnly)
            {
                if (program.SelectedObject is O3DMesh mesh && dstPath != null)
                {
                    try
                    {
                        // TODO: Reapply transformation
                        var o3dFile = mesh.O3D;
#if ALLOW_DECRYPTION
                        if (o3dFile.encryptionKey != 0xffffffff && o3dFile.version > 3)
                        {
                            o3dFile = new(o3dFile); // Create a deep copy of the o3d file before encrypting it
                            O3DEncryption.EncryptO3D(ref o3dFile, o3dFile.encryptionKey, (o3dFile.extendedOptions & O3DExtendedOptions.ALT_ENCRYPTION_SEED) != 0);
                        }
#endif
                        O3DParser.WriteO3D(dstPath, o3dFile);
                        LogMessage($"Saved {mesh.name} as {dstPath}...");
                    }
                    catch (Exception ex)
                    {
                        ShowErrorPopup($"Error saving {mesh.name}", ex.Message);
                    }
                }
            }
            else
            {
                foreach (var mesh in program.SceneGraph?.meshes ?? [])
                {
                    if (mesh is O3DMesh o3d)
                    {
                        try
                        {
                            // TODO: Reapply transformation
                            var o3dFile = o3d.O3D;
#if ALLOW_DECRYPTION
                            if (o3dFile.encryptionKey != 0xffffffff && o3dFile.version > 3)
                            {
                                o3dFile = new(o3dFile); // Create a deep copy of the o3d file before encrypting it
                                O3DEncryption.EncryptO3D(ref o3dFile, o3dFile.encryptionKey, (o3dFile.extendedOptions & O3DExtendedOptions.ALT_ENCRYPTION_SEED) != 0);
                            }
#endif
                            O3DParser.WriteO3D(o3d.O3DPath, o3dFile);
                            LogMessage($"Saved {mesh.name} as {o3d.O3DPath}...");
                        }
                        catch (Exception ex)
                        {
                            ShowErrorPopup($"Error saving {mesh.name}", ex.Message);
                        }
                    }
                }
            }
        }

        private void DeleteObj(bool all = false)
        {
            if (program.SceneGraph == null)
                return;
            if (all)
            {
                LogMessage($"Deleting {program.SceneGraph.meshes.Count} meshes...");
                program.SceneGraph.meshes.Clear();
                if (program.SceneGraph.lights.Count > 1)
                    program.SceneGraph.lights.RemoveRange(1, program.SceneGraph.lights.Count - 1);
            }
            else if (program.SelectedObject is Mesh m)
            {
                LogMessage($"Deleting {m.name}...");
                program.SceneGraph.meshes.Remove(m);
            }
            else if (program.SelectedObject is Light l)
            {
                LogMessage($"Deleting {l.name}...");
                program.SceneGraph.lights.Remove(l);
            }
        }

        private void Focus()
        {
            program.Focus(program.SelectedObject);
        }

        private void CreateLight()
        {
            var l = new Light()
            {
                colour = new(.5f, .5f, .5f),
                ambient = new(0, 0, 0),
                mode = LightMode.Directional,
                intensity = 1
            };
            program.SceneGraph?.lights?.Add(l);
            program.Select(l);
        }

        private readonly string regFileAssocKey = @"Software\Classes\.o3d";
        private readonly string regFileActionClass = @"Software\Classes\O3DView";
        private void RegisterFileAssociation(bool register)
        {
            if (register)
            {
                // Register
                LogMessage("Attempting to register file association...");
                try
                {
                    // Save the old value
                    var curr = Registry.GetValue(@"HKEY_CURRENT_USER\" + regFileAssocKey, "", null);
                    if (curr != null)
                        Registry.SetValue(@"HKEY_CURRENT_USER\" + regFileAssocKey, "_old", curr, RegistryValueKind.String);
                    Registry.SetValue(@"HKEY_CURRENT_USER\" + regFileAssocKey, "", "O3DView", RegistryValueKind.String);

                    string path = Path.ChangeExtension(Assembly.GetEntryAssembly()?.Location, ".exe") ?? throw new Exception("Couldn't get executable path!");
                    Registry.SetValue(@"HKEY_CURRENT_USER\" + regFileActionClass + @"\shell\open\command", "",
                        $@"""{path}"" ""%1"""
                        , RegistryValueKind.String);
                    Registry.SetValue(@"HKEY_CURRENT_USER\" + regFileActionClass + @"\DefaultIcon", "",
                        $@"""{path}"",0"
                        , RegistryValueKind.String);
                }
                catch (Exception ex)
                {
                    ShowErrorPopup("Failed to register file association:", ex.Message);
                }
            }
            else
            {
                // Unregister
                LogMessage("Attempting to unregister file association...");
                try
                {
                    var old = Registry.GetValue(@"HKEY_CURRENT_USER\" + regFileAssocKey, "_old", null);
                    if (old != null)
                        Registry.SetValue(@"HKEY_CURRENT_USER\" + regFileAssocKey, "", old, RegistryValueKind.String);
                    else
                    {
                        Registry.CurrentUser.DeleteSubKey(regFileAssocKey, false);
                    }
                    Registry.CurrentUser.DeleteSubKeyTree(regFileActionClass, false);
                }
                catch (Exception ex)
                {
                    ShowErrorPopup("Failed to unregister file association:", ex.Message);
                }
            }
            try
            {
                var CurrentUser = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.o3d", true) ?? throw new Exception("Couldn't open FileExts key!");
                CurrentUser.DeleteSubKey("UserChoice", false);
                CurrentUser.CreateSubKey("UserChoice").SetValue("ProgId", @"O3DView");
                CurrentUser.Close();
                Registry.CurrentUser.Close();
            }
            catch (Exception ex)
            {
                ShowErrorPopup("Failed to register/unregister file association:", ex.Message);
            }

            // Tell explorer the file association has been changed
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }

        public enum Severity
        {
            Debug = 0,
            Info = 1,
            Warning = 2,
            Error = 3,
        }

        [LibraryImport("shell32.dll", SetLastError = true)]
        public static partial void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }

    [Flags]
    internal enum WindowSnapEdge
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Top = 1 << 2,
        Bottom = 1 << 3,
    }
}
