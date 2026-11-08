using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static GUI.Controls.SavedCameraPositionsControl;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    class GLWorldViewer : GLSceneViewer
    {
        public event Action<Vector3>? WorldPositionPicked;

        private readonly World? world;
        private readonly WorldNode? worldNode;
        private readonly ResourceExtRefList? mapExternalReferences;
        private CheckedListBox? worldLayersComboBox;
        private CheckedListBox? physicsGroupsComboBox;
        private ComboBox? cameraComboBox;
        private SavedCameraPositionsControl? savedCameraPositionsControl;
        private EntityInfoForm? entityInfoForm;
        private bool ignoreLayersChangeEvents = true;
        private List<Matrix4x4> CameraMatrices = [];
        private WorldNodeLoader? LoadedWorldNode;
        public WorldLoader? LoadedWorld;

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, World world, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.world = world;
            mapExternalReferences = externalReferences;
        }

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, WorldNode worldNode, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.worldNode = worldNode;
            mapExternalReferences = externalReferences;
        }

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
        }

        private void AddSceneExposureSlider()
        {
            Debug.Assert(UiControl != null);

            var exposureLabel = new Label();
            void UpdateExposureText(float exposure)
            {
                exposureLabel.Text = $"Exposure: {exposure:0.00}";
            }

            UiControl.AddControl(exposureLabel);

            var exposureSlider = UiControl.AddTrackBar((exposureAmount) =>
            {
                var exposure = exposureAmount * 10;
                UpdateExposureText(exposure);
                Renderer.Postprocess.CustomExposure = exposure;

                // also set auto exposure to off for debugging, in case this is slowing things down
                Renderer.Postprocess.State = Renderer.Postprocess.State with { ExposureSettings = Renderer.Postprocess.State.ExposureSettings with { AutoExposureEnabled = false } };
            });

            var sceneExposure = Renderer.Postprocess.CurrentExposure;
            exposureSlider.Slider.Value = sceneExposure / 10;
            UpdateExposureText(sceneExposure);
        }

        private void OnGetOrSetPositionFromClipboardRequest(object? sender, bool isSetRequest)
        {
            var pitch = 0.0f;
            var yaw = 0.0f;

            if (!isSetRequest)
            {
                var loc = Renderer.Camera.Location;
                pitch = -1.0f * float.RadiansToDegrees(Renderer.Camera.Pitch);
                yaw = float.RadiansToDegrees(Renderer.Camera.Yaw);

                Clipboard.SetText($"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {pitch:F6} {yaw:F6} 0.0");

                return;
            }

            var text = Clipboard.GetText();
            var pos = Regexes.SetPos().Match(text);
            var ang = Regexes.SetAng().Match(text);

            if (!pos.Success)
            {
                Log.Error(nameof(GLWorldViewer), "Failed to find setpos in clipboard text.");
                return;
            }

            var x = float.Parse(pos.Groups["x"].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(pos.Groups["y"].Value, CultureInfo.InvariantCulture);
            var z = float.Parse(pos.Groups["z"].Value, CultureInfo.InvariantCulture);

            if (ang.Success)
            {
                pitch = -1f * float.DegreesToRadians(float.Parse(ang.Groups["pitch"].Value, CultureInfo.InvariantCulture));
                yaw = float.DegreesToRadians(float.Parse(ang.Groups["yaw"].Value, CultureInfo.InvariantCulture));
            }

            Input.SaveCameraForTransition();
            Input.Camera.SetLocationPitchYaw(new Vector3(x, y, z), pitch, yaw);
        }

        private void OnRestoreCameraRequest(object? sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Input.SaveCameraForTransition();
                    Input.Camera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        savedFloats[3],
                        savedFloats[4]);
                }
            }
        }

        private void OnSaveCameraRequest(object? sender, EventArgs e)
        {
            var cam = Renderer.Camera;
            var saveName = $"Camera at {cam.Location.X:F0} {cam.Location.Y:F0} {cam.Location.Z:F0}";
            var originalName = saveName;
            var duplicateCameraIndex = 1;

            while (Settings.Config.SavedCameras.ContainsKey(saveName))
            {
                saveName = $"{originalName} (#{duplicateCameraIndex++})";
            }

            Settings.Config.SavedCameras.Add(saveName, [cam.Location.X, cam.Location.Y, cam.Location.Z, cam.Pitch, cam.Yaw]);
            Settings.InvokeRefreshCamerasOnSave();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();

            if (worldNode != null)
            {
                base.LoadDefaultLighting();

                Scene.LightingInfo.CubemapType = CubemapType.None; // default envmap actually looks bad
                Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;
                Scene.LightingInfo.SunLightShadowCoverageScale = 2f;

                var post = new ScenePostProcessVolume(Scene)
                {
                    HasBloom = true,
                    IsMaster = true,
                };

                Scene.PostProcessInfo.AddPostProcessVolume(post);
            }
        }

        protected override void LoadScene()
        {
            var cameraSet = false;

            if (world != null)
            {
                LoadedWorld = new WorldLoader(world, Scene);
                LoadedWorld.Load(mapExternalReferences);

                if (LoadedWorld.SkyboxScene != null)
                {
                    Renderer.SkyboxScene = LoadedWorld.SkyboxScene;
                }

                if (LoadedWorld.Skybox2D != null)
                {
                    Renderer.Skybox2D = LoadedWorld.Skybox2D;
                }

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene);

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    CameraMatrices = LoadedWorld.CameraMatrices;

                    Input.Camera.SetFromTransformMatrix(CameraMatrices[0]);
                    cameraSet = true;
                }
            }

            if (!cameraSet)
            {
                Input.Camera.SetLocation(new Vector3(256));
                Input.Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                LoadedWorldNode = new WorldNodeLoader(Scene.RendererContext, worldNode, mapExternalReferences);
                LoadedWorldNode.Load(Scene);
            }
        }

        protected override void OnFirstPaint()
        {
            base.OnFirstPaint();

            Input.MoveCamera(new Vector3(0, -150f, 0));
            Input.MoveCamera(new Vector3(0, 150f, 0), transition: true);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers]);
            });
            physicsGroupsComboBox = UiControl.AddMultiSelection("Physics Groups", null, (physicsGroups) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledPhysicsGroups([.. physicsGroups]);
            });

            using (UiControl.BeginGroup("Camera"))
            {
                savedCameraPositionsControl = new SavedCameraPositionsControl();
                savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
                savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
                savedCameraPositionsControl.GetOrSetPositionFromClipboardRequest += OnGetOrSetPositionFromClipboardRequest;
                UiControl.AddControl(savedCameraPositionsControl);

                if (LoadedWorld != null && LoadedWorld.CameraMatrices.Count > 0)
                {
                    cameraComboBox = UiControl.AddSelection("Map Camera", (cameraName, index) =>
                    {
                        if (index > 0)
                        {
                            Input.SaveCameraForTransition();
                            Input.Camera.SetFromTransformMatrix(CameraMatrices[index - 1]);
                        }
                    });
                    cameraComboBox.BeginUpdate();
                    cameraComboBox.Items.Add("Set view to camera…");
                    cameraComboBox.Items.AddRange([.. LoadedWorld.CameraNames]);
                    cameraComboBox.SelectedIndex = 0;
                    cameraComboBox.EndUpdate();
                }
            }

            if (world != null)
            {
                var uniqueWorldLayers = new HashSet<string>(4);
                var uniquePhysicsGroups = new HashSet<string>();

                foreach (var node in Scene.AllNodes)
                {
                    if (node.LayerName?.StartsWith("LightProbeGrid", StringComparison.Ordinal) == true)
                    {
                        continue;
                    }

                    if (node.LayerName != null)
                    {
                        uniqueWorldLayers.Add(node.LayerName);
                    }

                    if (node is PhysSceneNode physSceneNode)
                    {
                        uniquePhysicsGroups.Add(physSceneNode.PhysGroupName);
                    }
                }

                if (uniqueWorldLayers.Count > 0 && LoadedWorld != null)
                {
                    Debug.Assert(worldLayersComboBox != null);

                    worldLayersComboBox.BeginUpdate();

                    SetAvailableLayers(uniqueWorldLayers);

                    foreach (var worldLayer in LoadedWorld.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    Scene.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                    SkyboxScene?.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                }

                if (uniquePhysicsGroups.Count > 0)
                {
                    SetAvailablePhysicsGroups(uniquePhysicsGroups);
                }

                using (UiControl.BeginGroup("World"))
                {
                    if (Renderer.SkyboxScene != null)
                    {
                        UiControl.AddCheckBox("Show Skybox", Renderer.ShowSkybox, (v) => Renderer.ShowSkybox = v);
                    }

                    UiControl.AddCheckBox("Show Fog", Scene.FogEnabled, v => Scene.FogEnabled = v);
                    UiControl.AddCheckBox("Color Correction", Renderer.Postprocess.ColorCorrectionEnabled, v => Renderer.Postprocess.ColorCorrectionEnabled = v);
                    UiControl.AddCheckBox("Occlusion Culling", Scene.EnableOcclusionCulling, (v) => Scene.EnableOcclusionCulling = v);

                    // TODO: PVS culling is not implemented yet
                    // if (Scene.VoxelVisibility != null)
                    // {
                    //     UiControl.AddCheckBox("PVS Culling", Scene.EnablePvsCulling, v => Scene.EnablePvsCulling = v);
                    // }
                    UiControl.AddCheckBox("Gpu Culling", Scene.EnableIndirectDraws, v =>
                    {
                        Scene.EnableIndirectDraws = v;
                    });

                    UiControl.AddCheckBox("Depth Prepass", Scene.EnableDepthPrepass, (v) => Scene.EnableDepthPrepass = v);

                    var enableLights = Scene.LightingInfo.BarnLights.Count < 40;
                    Renderer.ViewBuffer!.Data!.ExperimentalLightsEnabled = enableLights;

                    UiControl.AddCheckBox("Experimental Lights", enableLights, v => Renderer.ViewBuffer!.Data!.ExperimentalLightsEnabled = v);

                    AddSceneExposureSlider();
                }
            }

            if (worldNode != null && worldLayersComboBox != null)
            {
                var worldLayers = Scene.AllNodes
                    .Select(static r => r.LayerName)
                    .OfType<string>()
                    .Distinct()
                    .ToList();
                SetAvailableLayers(worldLayers);

                for (var i = 0; i < worldLayersComboBox.Items.Count; i++)
                {
                    worldLayersComboBox.SetItemChecked(i, true);
                }
            }

            savedCameraPositionsControl.RefreshSavedPositions();

            ignoreLayersChangeEvents = false;

            base.AddUiControls();

            if (world != null)
            {
                AddDOFControls();
            }
        }

        public void AddDOFControls()
        {
            Debug.Assert(UiControl != null);

            var groupBoxPanel = new Panel
            {
                Height = UiControl.AdjustForDPI(230),
                Padding = new(0, 2, 0, 2),
            };

            var groupBox = new ThemedGroupBox
            {
                Text = "Depth Of Field",
                Dock = DockStyle.Fill,
                Padding = new(4, 8, 4, 4),
            };

            var controlsContainer = new Panel
            {
                Height = UiControl.AdjustForDPI(175),
                Dock = DockStyle.Top
            };

            void dofCheckBoxAction(bool v)
            {
                Renderer.Postprocess.DOF.Enabled = v;

                controlsContainer.Enabled = v;
                controlsContainer.Visible = v;

                var height = v ? 230 : 60;
                groupBoxPanel.Height = UiControl.AdjustForDPI(height);
            }

            dofCheckBoxAction(Renderer.Postprocess.DOF.Enabled);

            var checkBox = RendererControl.CreateCheckBox("Enabled", Renderer.Postprocess.DOF.Enabled, dofCheckBoxAction);
            checkBox.Dock = DockStyle.Top;

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far blurry", val =>
            {
                Renderer.Postprocess.DOF.FarBlurry = val;
            }, Renderer.Postprocess.DOF.FarBlurry, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far crisp", val =>
            {
                Renderer.Postprocess.DOF.FarCrisp = val;
            }, Renderer.Postprocess.DOF.FarCrisp, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near blurry", val =>
            {
                Renderer.Postprocess.DOF.NearBlurry = -val;
            }, Renderer.Postprocess.DOF.NearBlurry, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near crisp", val =>
            {
                Renderer.Postprocess.DOF.NearCrisp = val;
            }, Renderer.Postprocess.DOF.NearCrisp, 0, 1000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Max Blur Size", val =>
            {
                Renderer.Postprocess.DOF.MaxBlurSize = val;
            }, Renderer.Postprocess.DOF.MaxBlurSize, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Radius Scale", val =>
            {
                Renderer.Postprocess.DOF.RadScale = val;
            }, Renderer.Postprocess.DOF.RadScale, 0, 1));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Focal Distance", val =>
            {
                Renderer.Postprocess.DOF.FocalDistance = val;
            }, Renderer.Postprocess.DOF.FocalDistance, 0, 10000));

            foreach (Control control in controlsContainer.Controls)
            {
                control.Dock = DockStyle.Top;
                control.Margin = new Padding(4);
            }

            groupBox.Controls.Add(controlsContainer);
            groupBox.Controls.Add(checkBox);
            groupBoxPanel.Controls.Add(groupBox);

            UiControl.AddControl(groupBoxPanel);
        }

        public void SelectAndFocusEntity(EntityLump.Entity entity)
        {
            if (UiControl != null && UiControl.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var node = Scene.Find(entity);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.Find(entity);
            }

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
        }

        private void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            Debug.Assert(SelectedNodeRenderer != null);

            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);

            var bbox = node.BoundingBox;
            var size = bbox.Size;
            var maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z);
            var distance = maxDimension * 1.2f;
            var cameraHeight = bbox.Center.Y + size.Y * 2f;

            var location = new Vector3(bbox.Center.X + distance, cameraHeight, bbox.Center.Z + distance);
            Input.SaveCameraForTransition();
            Input.Camera.SetLocation(location);
            Input.Camera.LookAt(bbox.Center);

            // Ensure the node is visible
            if (!node.LayerEnabled && worldLayersComboBox != null && node.LayerName != null)
            {
                var layerId = worldLayersComboBox.Items.IndexOf(node.LayerName);

                if (layerId >= 0)
                {
                    worldLayersComboBox.SetItemChecked(layerId, true);
                }
            }

            if (node is PhysSceneNode physNode && !physNode.Enabled && physicsGroupsComboBox != null && physNode.PhysGroupName != null)
            {
                var physId = physicsGroupsComboBox.Items.IndexOf(physNode.PhysGroupName);

                if (physId >= 0)
                {
                    physicsGroupsComboBox.SetItemChecked(physId, true);
                }
            }
        }

        private void ShowSceneNodeDetails(SceneNode sceneNode)
        {
            var isEntity = sceneNode.EntityData != null;
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm();
                entityInfoForm.Show();
                entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick += OnEntityInfoOutputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.Disposed += OnEntityInfoFormDisposed;
            }

            Debug.Assert(entityInfoForm != null);

            entityInfoForm.EntityInfoControl.Clear();

            if (isEntity)
            {
                ShowEntityProperties(sceneNode);
            }
            else
            {
                entityInfoForm.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

                static string FormatVector(Vector3 vector)
                {
                    return $"{vector.X:F2} {vector.Y:F2} {vector.Z:F2}";
                }

                static string ToRenderColor(Vector4 tint)
                {
                    tint *= 255.0f;
                    return $"{tint.X:F0} {tint.Y:F0} {tint.Z:F0}";
                }

                if (sceneNode is SceneAggregate.Fragment sceneFragment)
                {
                    var material = sceneFragment.DrawCall.Material.Material;
                    entityInfoForm.EntityInfoControl.AddProperty("Shader", material.ShaderName);
                    entityInfoForm.EntityInfoControl.AddProperty("Material", material.Name);
                    entityInfoForm.EntityInfoControl.AddProperty("Aggregate Model", sceneFragment.Name!);

                    var tris = sceneFragment.DrawCall.IndexCount / 3;
                    if (sceneFragment.DrawCall.NumMeshlets > 0)
                    {
                        var clusters = sceneFragment.DrawCall.NumMeshlets;
                        var trisPerCluster = tris / clusters;
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                    }
                    else
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles", $"{tris}");
                    }

                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                    if (sceneFragment.Tint != Vector4.One)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Instance Tint", ToRenderColor(sceneFragment.Tint));
                        entityInfoForm.EntityInfoControl.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.Tint));
                    }
                }
                else if (sceneNode is ModelSceneNode modelSceneNode)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Model", modelSceneNode.Name!);
                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(modelSceneNode.Tint));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{modelSceneNode.Tint.W:F6}");

                    if (modelSceneNode.LightingOrigin.HasValue)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Custom Lighting Origin", FormatVector(modelSceneNode.LightingOrigin.Value));
                    }
                }

                if (sceneNode.CubeMapPrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
                }

                if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
                }

                entityInfoForm.EntityInfoControl.AddProperty("Flags", sceneNode.Flags.ToString());
                entityInfoForm.EntityInfoControl.AddProperty("Layer", sceneNode.LayerName ?? string.Empty);
            }

            if (SkyboxScene != null && sceneNode.Scene == SkyboxScene)
            {
                entityInfoForm.Text += " (in 3D skybox)";
            }

            entityInfoForm.EntityInfoControl.ShowOutputsTabIfAnyData();
            entityInfoForm.EntityInfoControl.Show();
        }

        private void OnEntityInfoOutputsCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            if (e.ColumnIndex != 1)
            {
                return;
            }

            var entityName = (string)(entityInfoForm.EntityInfoControl.OutputsGrid[e.ColumnIndex, e.RowIndex].Value ?? string.Empty);

            if (string.IsNullOrEmpty(entityName))
            {
                return;
            }

            var node = Scene.FindNodeByKeyValue("targetname", entityName);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.FindNodeByKeyValue("targetname", entityName);
            }

            if (node == null)
            {
                return;
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void OnEntityInfoFormDisposed(object? sender, EventArgs e)
        {
            if (entityInfoForm == null)
            {
                return;
            }

            entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick -= OnEntityInfoOutputsCellDoubleClick;
            entityInfoForm.EntityInfoControl.Disposed -= OnEntityInfoFormDisposed;
            entityInfoForm = null;
        }

        protected override void OnPicked(object? sender, PickingResponse pickingResponse)
        {
            Debug.Assert(SelectedNodeRenderer != null);

            if (pickingResponse.Intent == PickingIntent.Select
                && TryGetPickedWorldPosition(pickingResponse, out var worldPosition))
            {
                WorldPositionPicked?.Invoke(worldPosition);
            }

            var pixelInfo = pickingResponse.PixelInfo;

            // Void
            if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0)
            {
                SelectedNodeRenderer.SelectNode(null);
                return;
            }

            var isInSkybox = pixelInfo.IsSkybox > 0;
            var sceneNode = isInSkybox ? SkyboxScene?.Find(pixelInfo.ObjectId) : Scene.Find(pixelInfo.ObjectId);

            if (sceneNode == null)
            {
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Select)
            {
                if ((Control.ModifierKeys & Keys.Control) > 0)
                {
                    SelectedNodeRenderer.ToggleNode(sceneNode);
                }
                else
                {
                    SelectedNodeRenderer.SelectNode(sceneNode);
                }

                //Update the entity properties window if it was opened
                if (entityInfoForm != null)
                {
                    Program.MainForm.Invoke(() =>
                    {
                        ShowSceneNodeDetails(sceneNode);
                    });
                }
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Details)
            {
                Program.MainForm.Invoke(() =>
                {
                    ShowSceneNodeDetails(sceneNode);
                    entityInfoForm?.EntityInfoControl.Focus();
                });
                return;
            }
        }

        private bool TryGetPickedWorldPosition(PickingResponse pickingResponse, out Vector3 worldPosition)
        {
            worldPosition = Vector3.Zero;

            if (Scene.PhysicsWorld == null || GLControl == null)
            {
                return false;
            }

            if (!TryGetPickRayDirection(pickingResponse.CursorPositionX, pickingResponse.CursorPositionY, out var rayDirection))
            {
                return false;
            }

            var rayStart = Renderer.Camera.Location;
            var rayEnd = rayStart + rayDirection * 100_000f;
            var traceResult = Scene.PhysicsWorld.TraceRay(rayStart, rayEnd);

            if (!traceResult.Hit)
            {
                return false;
            }

            worldPosition = traceResult.HitPosition;
            return true;
        }

        private bool TryGetPickRayDirection(int cursorPositionX, int cursorPositionY, out Vector3 rayDirection)
        {
            rayDirection = Vector3.Zero;

            if (GLControl == null || GLControl.Width <= 0 || GLControl.Height <= 0)
            {
                return false;
            }

            var normalizedX = ((cursorPositionX + 0.5f) / GLControl.Width) * 2f - 1f;
            var normalizedY = 1f - (((cursorPositionY + 0.5f) / GLControl.Height) * 2f);

            var halfVerticalFov = Renderer.Camera.GetFOV() * 0.5f;
            var tanHalfVerticalFov = MathF.Tan(halfVerticalFov);
            var tanHalfHorizontalFov = tanHalfVerticalFov * Renderer.Camera.AspectRatio;

            var ray = Renderer.Camera.Forward
                + Renderer.Camera.Right * (normalizedX * tanHalfHorizontalFov)
                + Renderer.Camera.Up * (normalizedY * tanHalfVerticalFov);

            if (ray.LengthSquared() <= float.Epsilon)
            {
                return false;
            }

            rayDirection = Vector3.Normalize(ray);
            return true;
        }

        private void ShowEntityProperties(SceneNode sceneNode)
        {
            Debug.Assert(entityInfoForm != null);
            Debug.Assert(sceneNode.EntityData != null);

            entityInfoForm.EntityInfoControl.PopulateFromEntity(sceneNode.EntityData);

            var classname = sceneNode.EntityData.GetStringProperty("classname");
            entityInfoForm.Text = $"Entity: {classname}";
        }

        private void SetAvailableLayers(IEnumerable<string> worldLayers)
        {
            Debug.Assert(worldLayersComboBox != null);

            worldLayersComboBox.Items.Clear();

            var worldLayersArray = worldLayers.ToArray();

            if (worldLayersArray.Length > 0)
            {
                worldLayersComboBox.Enabled = true;
                worldLayersComboBox.Items.AddRange(worldLayersArray);
            }
            else
            {
                worldLayersComboBox.Enabled = false;
            }
        }

        private const string PhysicsRenderAsOpaque = "S2V: Render as opaque";

        private void SetAvailablePhysicsGroups(IEnumerable<string> physicsGroups)
        {
            Debug.Assert(physicsGroupsComboBox != null);

            physicsGroupsComboBox.Items.Clear();

            var physicsGroupsArray = physicsGroups
                .OrderByDescending(static s => s.StartsWith('-'))
                .ToArray();

            if (physicsGroupsArray.Length > 0)
            {
                physicsGroupsComboBox.Enabled = true;
                physicsGroupsComboBox.Items.AddRange(physicsGroupsArray);
                physicsGroupsComboBox.Items.Add(PhysicsRenderAsOpaque);
            }
            else
            {
                physicsGroupsComboBox.Enabled = false;
            }
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            var renderTranslucent = !physicsGroups.Contains(PhysicsRenderAsOpaque);

            if (!renderTranslucent)
            {
                physicsGroups.Remove(PhysicsRenderAsOpaque);
            }

            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
                physNode.IsTranslucentRenderMode = renderTranslucent;
            }

            using var lockedGl = MakeCurrent();

            Scene.UpdateOctrees();
            SkyboxScene?.UpdateOctrees();
        }

        private List<LineSceneNode> trajectoryLineNodes = [];

        private const int MaxExplorationSlots = 14;
        private readonly List<List<LineSceneNode>> explorationSlots = [];
        private int nextExplorationSlot;
        private readonly List<LineSceneNode> matchLineNodes = [];
        private readonly ConcurrentQueue<(IReadOnlyList<Vector3> Path, bool IsMatch)> pendingVisualizations = new();

        /// <summary>
        /// Display a trajectory path as a series of line segments in the 3D scene.
        /// </summary>
        public void ShowTrajectory(IReadOnlyList<Vector3> path)
        {
            ClearTrajectory();
            ClearSearchVisualization();

            if (path.Count < 2)
            {
                return;
            }

            using var lockedGl = MakeCurrent();

            var trajectoryColor = new Color32(255, 100, 0);
            var endColor = Color32.Red;

            for (var i = 0; i < path.Count - 1; i++)
            {
                var t = (float)i / (path.Count - 1);
                var segColor = new Color32(
                    (byte)(255 - (int)(155 * t)),
                    (byte)(100 - (int)(100 * t)),
                    0
                );

                var lineNode = new LineSceneNode(Scene, path[i], path[i + 1], segColor, segColor);
                Scene.Add(lineNode, true);
                trajectoryLineNodes.Add(lineNode);
            }

            Scene.UpdateOctrees();
        }

        /// <summary>
        /// Remove any previously displayed trajectory lines from the scene.
        /// </summary>
        public void ClearTrajectory()
        {
            if (trajectoryLineNodes.Count == 0)
            {
                return;
            }

            using var lockedGl = MakeCurrent();

            foreach (var node in trajectoryLineNodes)
            {
                Scene.Remove(node, true);
            }

            trajectoryLineNodes.Clear();
            Scene.UpdateOctrees();
        }

        /// <summary>
        /// Position the camera at the given start position (at player eye height) looking toward the target.
        /// </summary>
        public void SetCameraForLineup(Vector3 startPosition, Vector3 targetPosition)
        {
            var eyeHeight = new Vector3(0, 0, 64);
            var cameraPos = startPosition + eyeHeight;

            Renderer.Camera.SetLocation(cameraPos);
            Renderer.Camera.LookAt(targetPosition);
            Renderer.Camera.RecalculateMatrices();
        }

        /// <summary>
        /// Queue a trajectory for visualization during the next render frame.
        /// This is safe to call from any thread and will not block the render loop.
        /// </summary>
        public void ShowSearchVisualization(IReadOnlyList<Vector3> path, bool isMatch)
        {
            if (path.Count < 2)
            {
                return;
            }

            pendingVisualizations.Enqueue((path, isMatch));
        }

        /// <summary>
        /// Process queued search visualizations. Called during the render loop
        /// which already holds the GL context, so camera input is not blocked.
        /// </summary>
        private void ProcessPendingVisualizations()
        {
            var processed = 0;

            while (pendingVisualizations.TryDequeue(out var viz) && processed < 4)
            {
                var (path, isMatch) = viz;
                var step = Math.Max(1, path.Count / 50);

                if (isMatch)
                {
                    var color = Color32.Green;

                    for (var i = 0; i < path.Count - step; i += step)
                    {
                        var endIdx = Math.Min(i + step, path.Count - 1);
                        var lineNode = new LineSceneNode(Scene, path[i], path[endIdx], color, color);
                        Scene.Add(lineNode, true);
                        matchLineNodes.Add(lineNode);
                    }
                }
                else
                {
                    if (explorationSlots.Count >= MaxExplorationSlots)
                    {
                        var oldSlot = explorationSlots[nextExplorationSlot];

                        foreach (var node in oldSlot)
                        {
                            Scene.Remove(node, true);
                        }

                        oldSlot.Clear();
                    }
                    else
                    {
                        explorationSlots.Add([]);
                    }

                    var slotIndex = Math.Min(nextExplorationSlot, explorationSlots.Count - 1);
                    var slot = explorationSlots[slotIndex];
                    var color = new Color32(70, 130, 230);

                    for (var i = 0; i < path.Count - step; i += step)
                    {
                        var endIdx = Math.Min(i + step, path.Count - 1);
                        var lineNode = new LineSceneNode(Scene, path[i], path[endIdx], color, color);
                        Scene.Add(lineNode, true);
                        slot.Add(lineNode);
                    }

                    nextExplorationSlot = (nextExplorationSlot + 1) % MaxExplorationSlots;
                }

                processed++;
            }

            if (processed > 0)
            {
                Scene.UpdateOctrees();
            }
        }

        /// <inheritdoc/>
        protected override void OnPaint(float frameTime)
        {
            ProcessPendingVisualizations();
            base.OnPaint(frameTime);
        }

        /// <summary>
        /// Remove all search visualization lines (exploration and match trajectories).
        /// </summary>
        public void ClearSearchVisualization()
        {
            // Drain any pending items so they don't get rendered after clearing
            while (pendingVisualizations.TryDequeue(out _)) { }

            if (explorationSlots.Count == 0 && matchLineNodes.Count == 0)
            {
                return;
            }

            using var lockedGl = MakeCurrent();

            foreach (var slot in explorationSlots)
            {
                foreach (var node in slot)
                {
                    Scene.Remove(node, true);
                }
            }

            explorationSlots.Clear();
            nextExplorationSlot = 0;

            foreach (var node in matchLineNodes)
            {
                Scene.Remove(node, true);
            }

            matchLineNodes.Clear();
            Scene.UpdateOctrees();
        }
    }
}
