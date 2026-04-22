using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Simulation;
using ValveResourceFormat.Simulation;

namespace GUI.Controls;

/// <summary>
/// UI control for testing grenade trajectory lineups against loaded map geometry.
/// Allows input of start/target positions, runs the lineup search, and displays results.
/// </summary>
internal partial class GrenadeLineupControl : UserControl
{
    private static readonly ThrowType[] AllThrowTypes =
    [
        ThrowType.LongThrow,
        ThrowType.ShortThrow,
        ThrowType.MidThrow,
        ThrowType.JumpThrow,
    ];

    private enum PickMode
    {
        None,
        Start,
        Target,
    }

    private readonly VrfGuiContext guiContext;
    private Rubikon? physicsWorld;
    private TrajectoryCalculator? trajectoryCalculator;
    private LineupSearcher? lineupSearcher;
    private GLWorldViewer? worldViewer;
    private CancellationTokenSource? searchCancellationTokenSource;
    private List<LineupSearcher.LineupResult> currentResults = [];
    private LineupSearcher.LineupResult? selectedResult;
    private PickMode currentPickMode;

    public GrenadeLineupControl(VrfGuiContext guiContext)
    {
        InitializeComponent();
        this.guiContext = guiContext;
        Dock = DockStyle.Fill;
        Disposed += OnGrenadeLineupControlDisposed;
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Themer.ThemeControl(this);
    }

    public void InitializeWithMap(Rubikon physicsWorld, GLWorldViewer? mapViewer = null)
    {
        this.physicsWorld = physicsWorld;
        trajectoryCalculator = new TrajectoryCalculator(physicsWorld);
        lineupSearcher = new LineupSearcher(trajectoryCalculator);
        AttachWorldViewer(mapViewer);
        
        // Enable the search button now that we have physics data
        findLineupButton.Enabled = true;
        debugKnownThrowButton.Enabled = true;
        statusLabel.Text = "Ready to search";
    }

    private async void OnFindLineupClick(object sender, EventArgs e)
    {
        if (searchCancellationTokenSource != null)
        {
            searchCancellationTokenSource.Cancel();
            findLineupButton.Enabled = false;
            statusLabel.Text = "Cancelling search...";
            return;
        }

        var isReverseScan = reverseScanCheckBox.Checked;

        if (isReverseScan)
        {
            if (!TryParseTargetPosition(out var targetPos))
            {
                statusLabel.Text = "Error: Invalid target position";
                return;
            }

            await RunReverseScan(targetPos).ConfigureAwait(false);
        }
        else
        {
            if (!TryParsePositions(out var startPos, out var targetPos))
            {
                statusLabel.Text = "Error: Invalid position inputs";
                return;
            }

            await RunForwardSearch(startPos, targetPos).ConfigureAwait(false);
        }
    }

    private async Task RunForwardSearch(Vector3 startPos, Vector3 targetPos)
    {
        if (lineupSearcher == null)
        {
            statusLabel.Text = "Error: Map physics not loaded";
            return;
        }

        var searcher = lineupSearcher;
        using var cancellationTokenSource = new CancellationTokenSource();
        searchCancellationTokenSource = cancellationTokenSource;
        SetSearchUiState(isSearching: true);

        IProgress<LineupSearcher.SearchProgress> progress = new Progress<LineupSearcher.SearchProgress>(UpdateSearchProgress);
        var visualization = worldViewer != null
            ? new Progress<LineupSearcher.SearchVisualization>(OnSearchVisualization)
            : null;

        if (worldViewer != null)
        {
            worldViewer.ClearTrajectory();
            worldViewer.ClearSearchVisualization();
            worldViewer.SetCameraForLineup(startPos, targetPos);
            BringRendererTabToFront();
        }

        try
        {
            var searchParamsList = GetSearchParamsForAllThrowTypes();
            var totalCandidates = searchParamsList.Sum(EstimateCandidateCount);
            var searchResults = await Task.Run(
                () =>
                {
                    var allMatches = new List<LineupSearcher.LineupResult>();
                    LineupSearcher.LineupResult? closestMiss = null;
                    long completedOffset = 0;

                    foreach (var searchParams in searchParamsList)
                    {
                        cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var perTypeTotal = EstimateCandidateCount(searchParams);
                        var typeProgress = new Progress<LineupSearcher.SearchProgress>(p =>
                            progress.Report(new LineupSearcher.SearchProgress(
                                completedOffset + p.CompletedCandidates,
                                totalCandidates,
                                allMatches.Count + p.MatchesFound)));

                        var typeResults = searcher.SearchLineupsWithDiagnostics(
                            startPos,
                            targetPos,
                            searchParams,
                            cancellationTokenSource.Token,
                            typeProgress,
                            visualization);

                        allMatches.AddRange(typeResults.Matches);

                        if (typeResults.ClosestMiss != null && (closestMiss == null || typeResults.ClosestMiss.DistanceToTarget < closestMiss.DistanceToTarget))
                        {
                            closestMiss = typeResults.ClosestMiss;
                        }

                        completedOffset += perTypeTotal;
                        progress.Report(new LineupSearcher.SearchProgress(completedOffset, totalCandidates, allMatches.Count));
                    }

                    return new LineupSearcher.SearchResults(
                        allMatches.OrderBy(r => r.DistanceToTarget).ToList(),
                        closestMiss);
                },
                cancellationTokenSource.Token).ConfigureAwait(false);

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                currentResults = searchResults.Matches.ToList();
                PopulateResultsList();

                var statusText = $"Found {searchResults.Matches.Count} valid lineups";

                if (searchResults.Matches.Count == 0 && searchResults.ClosestMiss != null)
                {
                    var miss = searchResults.ClosestMiss;
                    statusText += $" | Closest miss: {miss.DistanceToTarget:F1}u away " +
                                  $"(pitch={miss.PitchAngleDegrees:F1}°, yaw={miss.YawAngleDegrees:F1}°, " +
                                  $"speed={miss.VelocityMagnitude:F0}, " +
                                  $"landed=({miss.FinalPosition.X:F0},{miss.FinalPosition.Y:F0},{miss.FinalPosition.Z:F0}))";
                }

                statusLabel.Text = statusText;
                SetSearchUiState(isSearching: false);
            }));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    worldViewer?.ClearSearchVisualization();
                    statusLabel.Text = "Search cancelled";
                    SetSearchUiState(isSearching: false);
                }));
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    worldViewer?.ClearSearchVisualization();
                    statusLabel.Text = $"Error: {ex.Message}";
                    SetSearchUiState(isSearching: false);
                }));
            }

            VrfGuiContext.Logger.LogError(ex, "GrenadeLineupControl search error");
        }
        finally
        {
            searchCancellationTokenSource = null;
        }
    }

    private async Task RunReverseScan(Vector3 targetPos)
    {
        if (lineupSearcher == null || physicsWorld == null)
        {
            statusLabel.Text = "Error: Map physics not loaded";
            return;
        }

        var searcher = lineupSearcher;
        var physics = physicsWorld;
        using var cancellationTokenSource = new CancellationTokenSource();
        searchCancellationTokenSource = cancellationTokenSource;
        SetSearchUiState(isSearching: true);

        IProgress<LineupSearcher.ReverseScanProgress> reverseProgress = new Progress<LineupSearcher.ReverseScanProgress>(UpdateReverseScanProgress);
        var visualization = worldViewer != null
            ? new Progress<LineupSearcher.SearchVisualization>(OnSearchVisualization)
            : null;

        if (worldViewer != null)
        {
            worldViewer.ClearTrajectory();
            worldViewer.ClearSearchVisualization();
            BringRendererTabToFront();
        }

        try
        {
            var searchParamsList = GetSearchParamsForAllThrowTypes();
            var results = await Task.Run(
                () =>
                {
                    var allResults = new List<LineupSearcher.LineupResult>();
                    var completedOffset = 0;
                    var totalPositionsPerType = 0;

                    foreach (var searchParams in searchParamsList)
                    {
                        cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var typeProgress = new Progress<LineupSearcher.ReverseScanProgress>(p =>
                        {
                            if (totalPositionsPerType == 0)
                            {
                                totalPositionsPerType = p.TotalPositions;
                            }

                            reverseProgress.Report(new LineupSearcher.ReverseScanProgress(
                                completedOffset + p.CompletedPositions,
                                totalPositionsPerType * searchParamsList.Count,
                                allResults.Count + p.MatchesFound));
                        });

                        var typeResults = searcher.SearchReverseLineups(
                            targetPos,
                            searchParams,
                            physics,
                            256f,
                            cancellationTokenSource.Token,
                            typeProgress,
                            visualization);

                        allResults.AddRange(typeResults);

                        if (totalPositionsPerType > 0)
                        {
                            completedOffset += totalPositionsPerType;
                            reverseProgress.Report(new LineupSearcher.ReverseScanProgress(
                                completedOffset,
                                totalPositionsPerType * searchParamsList.Count,
                                allResults.Count));
                        }
                    }

                    return allResults.OrderBy(r => r.DistanceToTarget).ToList();
                },
                cancellationTokenSource.Token).ConfigureAwait(false);

            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                currentResults = results.ToList();
                PopulateResultsList();
                statusLabel.Text = $"Reverse scan found {results.Count} valid lineups";
                SetSearchUiState(isSearching: false);
            }));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    worldViewer?.ClearSearchVisualization();
                    statusLabel.Text = "Search cancelled";
                    SetSearchUiState(isSearching: false);
                }));
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    worldViewer?.ClearSearchVisualization();
                    statusLabel.Text = $"Error: {ex.Message}";
                    SetSearchUiState(isSearching: false);
                }));
            }

            VrfGuiContext.Logger.LogError(ex, "GrenadeLineupControl reverse scan error");
        }
        finally
        {
            searchCancellationTokenSource = null;
        }
    }

    private void AttachWorldViewer(GLWorldViewer? mapViewer)
    {
        if (ReferenceEquals(worldViewer, mapViewer))
        {
            return;
        }

        if (worldViewer != null)
        {
            worldViewer.WorldPositionPicked -= OnWorldPositionPicked;
        }

        worldViewer = mapViewer;

        if (worldViewer != null)
        {
            worldViewer.WorldPositionPicked += OnWorldPositionPicked;
        }

        pickStartButton.Enabled = worldViewer != null;
        pickTargetButton.Enabled = worldViewer != null;
        UpdatePickButtons();
    }

    private void OnGrenadeLineupControlDisposed(object? sender, EventArgs e)
    {
        searchCancellationTokenSource?.Cancel();

        if (worldViewer != null)
        {
            worldViewer.WorldPositionPicked -= OnWorldPositionPicked;
            worldViewer = null;
        }
    }

    private void OnPickStartClick(object sender, EventArgs e)
    {
        TogglePickMode(PickMode.Start);
    }

    private void OnPickTargetClick(object sender, EventArgs e)
    {
        TogglePickMode(PickMode.Target);
    }

    private void TogglePickMode(PickMode pickMode)
    {
        if (worldViewer == null)
        {
            statusLabel.Text = "Pick mode is only available when a map viewer is loaded";
            return;
        }

        currentPickMode = currentPickMode == pickMode ? PickMode.None : pickMode;
        UpdatePickButtons();

        if (currentPickMode != PickMode.None)
        {
            BringRendererTabToFront();
        }

        statusLabel.Text = currentPickMode switch
        {
            PickMode.Start => "Click in the map view to pick the start position",
            PickMode.Target => "Click in the map view to pick the target position",
            _ => "Ready to search",
        };
    }

    private void OnWorldPositionPicked(Vector3 worldPosition)
    {
        if (currentPickMode == PickMode.None || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyPickedPosition(worldPosition)));
            return;
        }

        ApplyPickedPosition(worldPosition);
    }

    private void ApplyPickedPosition(Vector3 worldPosition)
    {
        switch (currentPickMode)
        {
            case PickMode.Start:
                SetPositionFields(startXTextBox, startYTextBox, startZTextBox, worldPosition);
                statusLabel.Text = $"Start position set to ({worldPosition.X:F1}, {worldPosition.Y:F1}, {worldPosition.Z:F1})";
                break;
            case PickMode.Target:
                SetPositionFields(targetXTextBox, targetYTextBox, targetZTextBox, worldPosition);
                statusLabel.Text = $"Target position set to ({worldPosition.X:F1}, {worldPosition.Y:F1}, {worldPosition.Z:F1})";
                break;
            default:
                return;
        }

        currentPickMode = PickMode.None;
        UpdatePickButtons();
        BringContainingTabToFront();
    }

    private static void SetPositionFields(TextBox xTextBox, TextBox yTextBox, TextBox zTextBox, Vector3 position)
    {
        xTextBox.Text = position.X.ToString("0.###");
        yTextBox.Text = position.Y.ToString("0.###");
        zTextBox.Text = position.Z.ToString("0.###");
    }

    private void UpdatePickButtons()
    {
        UpdatePickButton(pickStartButton, currentPickMode == PickMode.Start, "Pick Start");
        UpdatePickButton(pickTargetButton, currentPickMode == PickMode.Target, "Pick Target");
    }

    private void UpdateSearchProgress(LineupSearcher.SearchProgress progress)
    {
        if (progress.TotalCandidates <= 0)
        {
            return;
        }

        var percent = (int)Math.Clamp(progress.CompletedCandidates * 100 / progress.TotalCandidates, 0, 100);
        searchProgressBar.Value = percent;
        statusLabel.Text = $"Searching... {progress.CompletedCandidates:N0}/{progress.TotalCandidates:N0} candidates, {progress.MatchesFound} matches";
    }

    private void UpdateReverseScanProgress(LineupSearcher.ReverseScanProgress progress)
    {
        if (progress.TotalPositions <= 0)
        {
            return;
        }

        var percent = (int)Math.Clamp(progress.CompletedPositions * 100 / progress.TotalPositions, 0, 100);
        searchProgressBar.Value = percent;
        statusLabel.Text = $"Reverse scan... {progress.CompletedPositions}/{progress.TotalPositions} positions, {progress.MatchesFound} matches";
    }

    private void SetSearchUiState(bool isSearching)
    {
        findLineupButton.Enabled = trajectoryCalculator != null || isSearching;
        findLineupButton.Text = isSearching ? "Cancel Search" : "Find Lineup";
        coarseSearchRadioButton.Enabled = !isSearching;
        defaultSearchRadioButton.Enabled = !isSearching;
        debugWideRadioButton.Enabled = !isSearching;
        throwTypeComboBox.Enabled = !isSearching;
        reverseScanCheckBox.Enabled = !isSearching;
        pickStartButton.Enabled = !isSearching && worldViewer != null;
        pickTargetButton.Enabled = !isSearching && worldViewer != null;
        searchProgressBar.Visible = isSearching;

        if (!isSearching)
        {
            searchProgressBar.Value = 0;
        }
    }

    private void OnSearchVisualization(LineupSearcher.SearchVisualization viz)
    {
        worldViewer?.ShowSearchVisualization(viz.Path, viz.IsMatch);
    }

    private static void UpdatePickButton(Button button, bool isActive, string inactiveText)
    {
        button.Text = isActive ? "Picking..." : inactiveText;
        button.BackColor = isActive ? Color.Khaki : SystemColors.Control;
    }

    private void BringContainingTabToFront()
    {
        var tabPage = FindContainingTabPage(this);

        if (tabPage?.Parent is not TabControl tabControl)
        {
            return;
        }

        tabControl.SelectedTab = tabPage;
        Focus();
    }

    private void BringRendererTabToFront()
    {
        if (worldViewer?.GLControl == null)
        {
            return;
        }

        var tabPage = FindContainingTabPage(worldViewer.GLControl);

        if (tabPage?.Parent is not TabControl tabControl)
        {
            return;
        }

        tabControl.SelectedTab = tabPage;
        worldViewer.GLControl.Focus();
    }

    private static TabPage? FindContainingTabPage(Control? control)
    {
        var current = control?.Parent;

        while (current != null && current is not TabPage)
        {
            current = current.Parent;
        }

        return current as TabPage;
    }

    private bool TryParseTargetPosition(out Vector3 targetPos)
    {
        targetPos = Vector3.Zero;

        if (!float.TryParse(targetXTextBox.Text, out var targetX) ||
            !float.TryParse(targetYTextBox.Text, out var targetY) ||
            !float.TryParse(targetZTextBox.Text, out var targetZ))
        {
            return false;
        }

        targetPos = new Vector3(targetX, targetY, targetZ);
        return true;
    }

    private bool TryParsePositions(out Vector3 startPos, out Vector3 targetPos)
    {
        startPos = Vector3.Zero;
        targetPos = Vector3.Zero;

        if (!float.TryParse(startXTextBox.Text, out var startX) ||
            !float.TryParse(startYTextBox.Text, out var startY) ||
            !float.TryParse(startZTextBox.Text, out var startZ))
        {
            return false;
        }

        if (!float.TryParse(targetXTextBox.Text, out var targetX) ||
            !float.TryParse(targetYTextBox.Text, out var targetY) ||
            !float.TryParse(targetZTextBox.Text, out var targetZ))
        {
            return false;
        }

        startPos = new Vector3(startX, startY, startZ);
        targetPos = new Vector3(targetX, targetY, targetZ);
        return true;
    }

    private GrenadeType GetSelectedGrenadeType() => throwTypeComboBox.SelectedIndex switch
    {
        1 => GrenadeType.Flashbang,
        2 => GrenadeType.HEGrenade,
        3 => GrenadeType.Decoy,
        4 => GrenadeType.Incendiary,
        _ => GrenadeType.Smoke,
    };

    private List<LineupSearcher.SearchParams> GetSearchParamsForAllThrowTypes()
    {
        return [.. AllThrowTypes.Select(CreateSearchParamsForThrowType)];
    }

    private LineupSearcher.SearchParams CreateSearchParamsForThrowType(ThrowType throwType)
    {
        var grenadeType = GetSelectedGrenadeType();

        if (debugWideRadioButton.Checked)
        {
            return LineupSearcher.SearchParams.CreateDebugWide(throwType, grenadeType);
        }

        return coarseSearchRadioButton.Checked
            ? LineupSearcher.SearchParams.CreateCoarse(throwType, grenadeType)
            : LineupSearcher.SearchParams.CreateDefault(throwType, grenadeType);
    }

    private static long EstimateCandidateCount(LineupSearcher.SearchParams searchParams)
    {
        var pitchCount = CountSteps(searchParams.MinPitch, searchParams.MaxPitch, searchParams.PitchStep);
        var yawCount = CountSteps(searchParams.MinYaw, searchParams.MaxYaw, searchParams.YawStep);
        var velocityCount = searchParams.HasVelocityRange
            ? CountSteps(searchParams.MinVelocity, searchParams.MaxVelocity, searchParams.VelocityStep)
            : 1;

        return (long)pitchCount * yawCount * velocityCount;
    }

    private static int CountSteps(float minValue, float maxValue, float step)
    {
        if (step <= 0f || maxValue < minValue)
        {
            return 0;
        }

        return (int)MathF.Floor(((maxValue - minValue) / step) + 0.0001f) + 1;
    }

    private void PopulateResultsList()
    {
        resultsListBox.Items.Clear();

        foreach (var result in currentResults.Take(20)) // Show top 20
        {
            resultsListBox.Items.Add(result);
        }

        resultsListBox.DisplayMember = string.Empty;
        resultsListBox.FormatString = string.Empty;
        resultsListBox.FormattingEnabled = true;
        resultsListBox.Format += (s, e) =>
        {
            if (e.ListItem is LineupSearcher.LineupResult r)
            {
                e.Value = $"{r.GrenadeType} / {r.ThrowType}: Pitch {r.PitchAngleDegrees:F1}°, Yaw {r.YawAngleDegrees:F1}°, Error {r.DistanceToTarget:F1}u";
            }
        };

        if (resultsListBox.Items.Count > 0)
        {
            resultsListBox.SelectedIndex = 0;
            OnResultSelected();
        }
    }

    private void OnResultSelected()
    {
        if (resultsListBox.SelectedItem is not LineupSearcher.LineupResult result)
        {
            return;
        }

        selectedResult = result;
        var details = $"Grenade: {result.GrenadeType}\n" +
                  $"Throw: {result.ThrowType}, Speed: {result.VelocityMagnitude:F0} u/s\n" +
                      $"Launch Velocity: ({result.LaunchVelocity.X:F1}, " +
                      $"{result.LaunchVelocity.Y:F1}, {result.LaunchVelocity.Z:F1})\n" +
                      $"Final Position: ({result.FinalPosition.X:F1}, " +
                      $"{result.FinalPosition.Y:F1}, {result.FinalPosition.Z:F1})\n" +
                      $"Distance to Target: {result.DistanceToTarget:F2}u";

        if (result.StartPosition != Vector3.Zero)
        {
            details += $"\nStart: ({result.StartPosition.X:F1}, {result.StartPosition.Y:F1}, {result.StartPosition.Z:F1})";
        }

        detailsLabel.Text = details;

        testInCS2Button.Enabled = true;
        debugTrajectoryButton.Enabled = true;
        VisualizeSelectedResult(result);
    }

    private void VisualizeSelectedResult(LineupSearcher.LineupResult result)
    {
        if (worldViewer == null || trajectoryCalculator == null)
        {
            return;
        }

        // Use the result's start position (from reverse scan) or fall back to the UI input
        var startPos = result.StartPosition;
        if (startPos == Vector3.Zero && TryParsePositions(out var uiStart, out _))
        {
            startPos = uiStart;
        }

        var throwHeight = 64f + LineupSearcher.GetThrowReleaseHeight(result.ThrowType);
        var throwPosition = startPos + new Vector3(0, 0, throwHeight);
        var path = trajectoryCalculator.CalculateTrajectory(throwPosition, result.LaunchVelocity, result.GrenadeType);

        worldViewer.ShowTrajectory(path);
        worldViewer.SetCameraForLineup(startPos, result.FinalPosition);
        BringRendererTabToFront();
    }

    private void OnResultsListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
        OnResultSelected();
    }

    private void OnDebugTrajectoryClick(object sender, EventArgs e)
    {
        if (selectedResult == null || trajectoryCalculator == null)
        {
            statusLabel.Text = "No lineup result selected";
            return;
        }

        var startPos = selectedResult.StartPosition;
        if (startPos == Vector3.Zero && TryParsePositions(out var uiStart, out _))
        {
            startPos = uiStart;
        }

        var throwPosition = startPos + new Vector3(0, 0, 64f + LineupSearcher.GetThrowReleaseHeight(selectedResult.ThrowType));

        // Also log the CS2 cfg values for cross-referencing
        var sourcePitch = -selectedResult.PitchAngleDegrees;
        var sourceYaw = selectedResult.YawAngleDegrees;

        var logPath = Path.Combine(Path.GetTempPath(), AppIdentity.DebugTrajectoryLogFileName);

        using (var writer = new StreamWriter(logPath))
        {
            writer.WriteLine("=== CS2 Lineup Debug Log ===");
            writer.WriteLine($"Player Ground Position (setpos): {startPos.X:F4} {startPos.Y:F4} {startPos.Z:F4}");
            writer.WriteLine($"Throw Origin (ground + 64 eye height): {throwPosition.X:F4} {throwPosition.Y:F4} {throwPosition.Z:F4}");
            writer.WriteLine($"Search Pitch: {selectedResult.PitchAngleDegrees:F4}° (positive=up)");
            writer.WriteLine($"Search Yaw: {selectedResult.YawAngleDegrees:F4}°");
            writer.WriteLine($"CS2 setang: {sourcePitch:F4} {sourceYaw:F4} 0");
            writer.WriteLine($"Grenade Type: {selectedResult.GrenadeType}");
            writer.WriteLine($"Throw Type: {selectedResult.ThrowType}");
            writer.WriteLine($"Speed: {selectedResult.VelocityMagnitude:F4} u/s");
            writer.WriteLine($"Launch Velocity: ({selectedResult.LaunchVelocity.X:F4}, {selectedResult.LaunchVelocity.Y:F4}, {selectedResult.LaunchVelocity.Z:F4})");
            writer.WriteLine();
            writer.WriteLine("Coordinate system: VRF and CS2 both use Z-up (X/Y horizontal, Z vertical)");
            writer.WriteLine("setpos X Y Z matches VRF world coordinates directly (no conversion).");
            writer.WriteLine("setang PITCH YAW ROLL: negative pitch = looking up.");
            writer.WriteLine();

            trajectoryCalculator.DebugLog = writer;
            trajectoryCalculator.CalculateTrajectory(throwPosition, selectedResult.LaunchVelocity, selectedResult.GrenadeType);
            trajectoryCalculator.DebugLog = null;
        }

        // Open the log file
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = logPath,
            UseShellExecute = true,
        });

        statusLabel.Text = $"Debug log written to {logPath}";
    }

    private async void OnDebugKnownThrowClick(object sender, EventArgs e)
    {
        if (lineupSearcher == null)
        {
            statusLabel.Text = "Error: Map physics not loaded";
            return;
        }

        if (searchCancellationTokenSource != null)
        {
            searchCancellationTokenSource.Cancel();
            statusLabel.Text = "Cancelling...";
            return;
        }

        // Known CS2 lineup coordinates for validation — Mirage T-spawn to window smoke (jump throw)
        var groundPosition = new Vector3(1422.96f, -55.97f, -103.97f);
        var ourPitch = 20.68f;
        var yaw = -168.43f;
        var speed = 750f;
        var jumpZBoost = LineupSearcher.GetThrowZBoost(ThrowType.JumpThrow);
        var grenadeType = GetSelectedGrenadeType();

        var target = new Vector3(-1176f, -587f, -103f);
        if (TryParseTargetPosition(out var targetPos))
        {
            target = targetPos;
        }

        var searcher = lineupSearcher;
        using var cancellationTokenSource = new CancellationTokenSource();
        searchCancellationTokenSource = cancellationTokenSource;
        SetSearchUiState(isSearching: true);

        IProgress<LineupSearcher.BounceTuningProgress> progress = new Progress<LineupSearcher.BounceTuningProgress>(UpdateBounceTuningProgress);
        var visualization = worldViewer != null
            ? new Progress<LineupSearcher.SearchVisualization>(OnSearchVisualization)
            : null;

        if (worldViewer != null)
        {
            worldViewer.ClearTrajectory();
            worldViewer.ClearSearchVisualization();
            worldViewer.SetCameraForLineup(groundPosition, target);
            BringRendererTabToFront();
        }

        try
        {
            var tuningResult = await Task.Run(
                () => searcher.DebugSingleThrowAutoTuneBounce(
                    groundPosition, ourPitch, yaw, speed, target, grenadeType, jumpZBoost,
                    visualizationTolerance: 50f,
                    cancellationTokenSource.Token, progress, visualization),
                cancellationTokenSource.Token).ConfigureAwait(false);

            if (IsDisposed)
            {
                return;
            }

            // Write the log file (quick I/O, fine on pool thread)
            var logPath = Path.Combine(Path.GetTempPath(), AppIdentity.DebugKnownThrowLogFileName);
            using (var writer = new StreamWriter(logPath))
            {
                writer.WriteLine($"=== Known CS2 Lineup Validation ({grenadeType}, Mirage window smoke, jump throw) ===");
                writer.WriteLine("CS2 console: setpos 1422.96 -55.97 -103.97; setang -20.68 -168.43 0");
                writer.WriteLine();
                searcher.WriteBounceTuningLog(writer, groundPosition, ourPitch, yaw, speed, target, grenadeType, jumpZBoost, tuningResult);
            }

            BeginInvoke(new Action(() =>
            {
                if (worldViewer != null && tuningResult.Path.Count > 0)
                {
                    worldViewer.ShowTrajectory(tuningResult.Path);
                    worldViewer.SetCameraForLineup(groundPosition, target);
                    BringRendererTabToFront();
                }

                statusLabel.Text = $"Bounce tuning done — best distance {tuningResult.DistanceToTarget:F1}u — log at {logPath}";
                SetSearchUiState(isSearching: false);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true,
                });
            }));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    worldViewer?.ClearSearchVisualization();
                    statusLabel.Text = "Bounce tuning cancelled";
                    SetSearchUiState(isSearching: false);
                }));
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                BeginInvoke(new Action(() =>
                {
                    statusLabel.Text = $"Error: {ex.Message}";
                    SetSearchUiState(isSearching: false);
                }));
            }
        }
        finally
        {
            searchCancellationTokenSource = null;
        }
    }

    private void UpdateBounceTuningProgress(LineupSearcher.BounceTuningProgress progress)
    {
        if (progress.TotalCandidates <= 0)
        {
            return;
        }

        var percent = (int)Math.Clamp(progress.CompletedCandidates * 100 / progress.TotalCandidates, 0, 100);
        searchProgressBar.Value = percent;
        var bestText = progress.BestDistance < float.MaxValue ? $"{progress.BestDistance:F1}u" : "none";
        statusLabel.Text = $"{progress.Phase}... {progress.CompletedCandidates:N0}/{progress.TotalCandidates:N0}, best {bestText}";
    }

    private void OnTestInCS2Click(object sender, EventArgs e)
    {
        if (selectedResult == null)
        {
            statusLabel.Text = "No lineup result selected";
            return;
        }

        // Use result's start position for reverse scan, or UI input for forward search
        var startPos = selectedResult.StartPosition;
        if (startPos == Vector3.Zero && TryParsePositions(out var uiStart, out _))
        {
            startPos = uiStart;
        }
        else if (startPos == Vector3.Zero)
        {
            statusLabel.Text = "Error: Invalid position inputs";
            return;
        }

        var mapName = CS2Launcher.ExtractMapName(guiContext.FileName);

        if (mapName == null)
        {
            statusLabel.Text = "Error: Could not determine map name";
            return;
        }

        // Source engine setang pitch is inverted from our convention:
        // In Source, negative pitch = looking up, positive = looking down
        // Our pitch: positive = up, so negate it for setang
        var sourcePitch = -selectedResult.PitchAngleDegrees;
        var sourceYaw = selectedResult.YawAngleDegrees;

        var error = CS2Launcher.LaunchLineupTest(mapName, startPos, sourcePitch, sourceYaw, selectedResult.ThrowType, selectedResult.GrenadeType);

        statusLabel.Text = error ?? $"Launching CS2 on {mapName}...";
    }

    public override string ToString() => "Lineup Search";
}

/// <summary>
/// Custom ListBox item display for LineupResult
/// </summary>
internal static class LineupResultDisplayHelper
{
    public static string GetDisplayText(LineupSearcher.LineupResult result)
    {
        return $"{result.ThrowType}: Pitch {result.PitchAngleDegrees:F1}°, " +
               $"Yaw {result.YawAngleDegrees:F1}°, " +
               $"Error {result.DistanceToTarget:F1}u";
    }
}
