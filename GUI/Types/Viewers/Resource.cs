using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    enum ResourceViewMode
    {
        Default,
        ViewerOnly,
        ResourceBlocksOnly,
    };

    class Resource(VrfGuiContext vrfGuiContext, ResourceViewMode viewMode, bool verifyFileSize) : IViewer, IDisposable
    {
        private ValveResourceFormat.Resource? resource;
        private RendererContext? rendererContext;
        public GLBaseControl? GLViewer { get; private set; }
        private CodeTextBox? GLViewerError;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            resource = resourceTemp;

            try
            {
                if (stream != null)
                {
                    resource.Read(stream, verifyFileSize);
                }
                else
                {
                    resource.Read(vrfGuiContext.FileName);
                }

                resourceTemp = null;
            }
            finally
            {
                // Only dispose resource if it throws within Read(), tough luck below this
                resourceTemp?.Dispose();
            }

            if (viewMode == ResourceViewMode.ResourceBlocksOnly || resource.ResourceType != ResourceType.Map)
            {
                return;
            }

            try
            {
                InitializeMapViewer(vrfGuiContext, resource);
            }
            catch (Exception ex)
            {
                GLViewerError = CodeTextBox.CreateFromException(ex);
            }
        }

        private void InitializeMapViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource)
        {
            rendererContext = vrfGuiContext.CreateRendererContext();

            var worldResource = vrfGuiContext.LoadFileCompiled(WorldLoader.GetWorldNameFromMap(resource.FileName!));

            if (worldResource?.DataBlock is World mapWorldData)
            {
                GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, mapWorldData, resource.ExternalReferences);
                GLViewer.InitializeLoad();
            }
            else
            {
                worldResource?.Dispose();
            }
        }

        public void Create(TabPage containerTabPage)
        {
            Debug.Assert(resource is not null);

            var isPreview = viewMode == ResourceViewMode.ViewerOnly;

            var resTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
                Multiline = true,
            };
            containerTabPage.Controls.Add(resTabs);

            if (GLViewerError == null && GLViewer != null)
            {
                try
                {
                    AddMapViewer(vrfGuiContext, isPreview, resTabs);
                }
                catch (Exception ex)
                {
                    GLViewerError = CodeTextBox.CreateFromException(ex);
                }
            }

            if (GLViewerError != null)
            {
                GLViewer?.Dispose();
                GLViewer = null;
                var errorTab = new ThemedTabPage("Viewer Error");
                errorTab.Controls.Add(GLViewerError);
                resTabs.TabPages.Add(errorTab);
            }

            if (resTabs.TabPages.Count == 0)
            {
                var unsupportedTab = new ThemedTabPage("Unsupported");
                unsupportedTab.Controls.Add(CodeTextBox.Create($"{resource.ResourceType} files are not supported. Open a compiled map (.vmap_c)."));
                resTabs.TabPages.Add(unsupportedTab);
            }

            if (isPreview)
            {
                // Preview only displays the first tab page, so copy over the contents
                var previewTab = resTabs.TabPages[0];

                foreach (Control c in previewTab.Controls)
                {
                    containerTabPage.Controls.Add(c);
                }

                resTabs.Dispose();

                return;
            }

            // Strictly speaking this event should not be needed, but we have it for safety.
            // Handled by OpenFile -> OnTabDisposed
            void OnTabDisposed(object? sender, EventArgs e)
            {
                resTabs.Disposed -= OnTabDisposed;

                resource?.Dispose();
            }

            resTabs.Disposed += OnTabDisposed;
        }

        private void AddMapViewer(VrfGuiContext vrfGuiContext, bool isPreview, TabControl resTabs)
        {
            Debug.Assert(GLViewer != null);

            var glViewerControl = GLViewer.InitializeUiControls(isPreview);

            var mapTabPage = new ThemedTabPage("MAP");
            resTabs.TabPages.Add(mapTabPage);
            mapTabPage.Controls.Add(glViewerControl);

            if (!isPreview && GLViewer is GLWorldViewer glWorldViewer && glWorldViewer.Scene.PhysicsWorld != null)
            {
                var grenadeTabPage = new ThemedTabPage("Grenade Lineups");
                var grenadeControl = new GrenadeLineupControl(vrfGuiContext)
                {
                    Dock = DockStyle.Fill,
                };
                grenadeTabPage.Controls.Add(grenadeControl);
                grenadeControl.InitializeWithMap(glWorldViewer.Scene.PhysicsWorld, glWorldViewer);
                resTabs.TabPages.Add(grenadeTabPage);
            }

            GLViewer.InitializeRenderLoop();
        }

        public void Dispose()
        {
            resource?.Dispose();
            rendererContext?.Dispose();
            GLViewer?.Dispose();
            GLViewerError?.Dispose();
        }
    }
}
