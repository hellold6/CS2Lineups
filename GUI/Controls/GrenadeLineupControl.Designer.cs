namespace GUI.Controls;

partial class GrenadeLineupControl
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            searchCancellationTokenSource?.Dispose();
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        mainPanel = new System.Windows.Forms.TableLayoutPanel();
        positionGroupBox = new System.Windows.Forms.GroupBox();
        positionInnerPanel = new System.Windows.Forms.TableLayoutPanel();
        startPositionPanel = new System.Windows.Forms.TableLayoutPanel();
        startLabel = new System.Windows.Forms.Label();
        pickStartButton = new System.Windows.Forms.Button();
        startXLabel = new System.Windows.Forms.Label();
        startXTextBox = new System.Windows.Forms.TextBox();
        startYLabel = new System.Windows.Forms.Label();
        startYTextBox = new System.Windows.Forms.TextBox();
        startZLabel = new System.Windows.Forms.Label();
        startZTextBox = new System.Windows.Forms.TextBox();

        targetPositionPanel = new System.Windows.Forms.TableLayoutPanel();
        targetLabel = new System.Windows.Forms.Label();
        pickTargetButton = new System.Windows.Forms.Button();
        targetXLabel = new System.Windows.Forms.Label();
        targetXTextBox = new System.Windows.Forms.TextBox();
        targetYLabel = new System.Windows.Forms.Label();
        targetYTextBox = new System.Windows.Forms.TextBox();
        targetZLabel = new System.Windows.Forms.Label();
        targetZTextBox = new System.Windows.Forms.TextBox();

        searchParamsPanel = new System.Windows.Forms.TableLayoutPanel();
        searchParamsLabel = new System.Windows.Forms.Label();
        coarseSearchRadioButton = new System.Windows.Forms.RadioButton();
        defaultSearchRadioButton = new System.Windows.Forms.RadioButton();
        debugWideRadioButton = new System.Windows.Forms.RadioButton();
        throwTypeLabel = new System.Windows.Forms.Label();
        throwTypeComboBox = new System.Windows.Forms.ComboBox();
        reverseScanCheckBox = new System.Windows.Forms.CheckBox();

        findLineupButton = new System.Windows.Forms.Button();
        statusLabel = new System.Windows.Forms.Label();
        searchProgressBar = new System.Windows.Forms.ProgressBar();

        resultsGroupBox = new System.Windows.Forms.GroupBox();
        resultsListBox = new System.Windows.Forms.ListBox();
        detailsLabel = new System.Windows.Forms.Label();
        testInCS2Button = new System.Windows.Forms.Button();
        debugTrajectoryButton = new System.Windows.Forms.Button();
        debugKnownThrowButton = new System.Windows.Forms.Button();

        // Suspend layouts
        mainPanel.SuspendLayout();
        positionGroupBox.SuspendLayout();
        positionInnerPanel.SuspendLayout();
        startPositionPanel.SuspendLayout();
        targetPositionPanel.SuspendLayout();
        searchParamsPanel.SuspendLayout();
        resultsGroupBox.SuspendLayout();

        // Main panel setup (9 rows: Position, Search Params, Button, Status, Progress, Results, Test, Debug, DebugKnown)
        mainPanel.ColumnCount = 1;
        mainPanel.RowCount = 9;
        mainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        mainPanel.AutoScroll = true;
        mainPanel.Padding = new System.Windows.Forms.Padding(10);
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        mainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

        // ============ POSITION GROUP ============
        positionGroupBox.Text = "Positions";
        positionGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        positionGroupBox.AutoSize = true;
        positionGroupBox.Padding = new System.Windows.Forms.Padding(5);

        // Inner panel: holds 2 columns (start and target position side by side)
        positionInnerPanel.ColumnCount = 2;
        positionInnerPanel.RowCount = 1;
        positionInnerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        positionInnerPanel.AutoSize = true;
        positionInnerPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        positionInnerPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
        positionInnerPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));

        // Start position panel (left column)
        startPositionPanel.ColumnCount = 3;
        startPositionPanel.RowCount = 4;
        startPositionPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        startPositionPanel.AutoSize = true;
        startPositionPanel.Padding = new System.Windows.Forms.Padding(5);
        startPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        startPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        startPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));

        startLabel.Text = "Start Position (Player Location)";
        startLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
        startPositionPanel.Controls.Add(startLabel, 0, 0);
        startPositionPanel.SetColumnSpan(startLabel, 2);

        pickStartButton.Text = "Pick Start";
        pickStartButton.AutoSize = true;
        pickStartButton.Enabled = false;
        pickStartButton.Click += OnPickStartClick;
        startPositionPanel.Controls.Add(pickStartButton, 2, 0);

        startXLabel.Text = "X:";
        startPositionPanel.Controls.Add(startXLabel, 0, 1);
        startXTextBox.Width = 120;
        startXTextBox.Text = "0";
        startPositionPanel.Controls.Add(startXTextBox, 1, 1);
        startPositionPanel.SetColumnSpan(startXTextBox, 2);

        startYLabel.Text = "Y:";
        startPositionPanel.Controls.Add(startYLabel, 0, 2);
        startYTextBox.Width = 120;
        startYTextBox.Text = "0";
        startPositionPanel.Controls.Add(startYTextBox, 1, 2);
        startPositionPanel.SetColumnSpan(startYTextBox, 2);

        startZLabel.Text = "Z:";
        startPositionPanel.Controls.Add(startZLabel, 0, 3);
        startZTextBox.Width = 120;
        startZTextBox.Text = "0";
        startPositionPanel.Controls.Add(startZTextBox, 1, 3);
        startPositionPanel.SetColumnSpan(startZTextBox, 2);

        // Target position panel (right column)
        targetPositionPanel.ColumnCount = 3;
        targetPositionPanel.RowCount = 4;
        targetPositionPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        targetPositionPanel.AutoSize = true;
        targetPositionPanel.Padding = new System.Windows.Forms.Padding(5);
        targetPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        targetPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        targetPositionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));

        targetLabel.Text = "Target Position";
        targetLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
        targetPositionPanel.Controls.Add(targetLabel, 0, 0);
        targetPositionPanel.SetColumnSpan(targetLabel, 2);

        pickTargetButton.Text = "Pick Target";
        pickTargetButton.AutoSize = true;
        pickTargetButton.Enabled = false;
        pickTargetButton.Click += OnPickTargetClick;
        targetPositionPanel.Controls.Add(pickTargetButton, 2, 0);

        targetXLabel.Text = "X:";
        targetPositionPanel.Controls.Add(targetXLabel, 0, 1);
        targetXTextBox.Width = 120;
        targetXTextBox.Text = "0";
        targetPositionPanel.Controls.Add(targetXTextBox, 1, 1);
        targetPositionPanel.SetColumnSpan(targetXTextBox, 2);

        targetYLabel.Text = "Y:";
        targetPositionPanel.Controls.Add(targetYLabel, 0, 2);
        targetYTextBox.Width = 120;
        targetYTextBox.Text = "0";
        targetPositionPanel.Controls.Add(targetYTextBox, 1, 2);
        targetPositionPanel.SetColumnSpan(targetYTextBox, 2);

        targetZLabel.Text = "Z:";
        targetPositionPanel.Controls.Add(targetZLabel, 0, 3);
        targetZTextBox.Width = 120;
        targetZTextBox.Text = "0";
        targetPositionPanel.Controls.Add(targetZTextBox, 1, 3);
        targetPositionPanel.SetColumnSpan(targetZTextBox, 2);

        // Add both panels to inner panel
        positionInnerPanel.Controls.Add(startPositionPanel, 0, 0);
        positionInnerPanel.Controls.Add(targetPositionPanel, 1, 0);

        // Add inner panel to group box
        positionGroupBox.Controls.Add(positionInnerPanel);

        // Add position group to main panel row 0
        mainPanel.Controls.Add(positionGroupBox, 0, 0);

        // ============ SEARCH PARAMS GROUP ============
        searchParamsPanel.ColumnCount = 2;
        searchParamsPanel.RowCount = 6;
        searchParamsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        searchParamsPanel.AutoSize = true;
        searchParamsPanel.Padding = new System.Windows.Forms.Padding(5);
        searchParamsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        searchParamsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));

        searchParamsLabel.Text = "Search Parameters";
        searchParamsLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
        searchParamsPanel.Controls.Add(searchParamsLabel, 0, 0);
        searchParamsPanel.SetColumnSpan(searchParamsLabel, 2);

        coarseSearchRadioButton.Text = "Coarse (Fast)";
        coarseSearchRadioButton.Checked = true;
        coarseSearchRadioButton.AutoSize = true;
        searchParamsPanel.Controls.Add(coarseSearchRadioButton, 0, 1);

        defaultSearchRadioButton.Text = "Default (Balanced)";
        defaultSearchRadioButton.AutoSize = true;
        searchParamsPanel.Controls.Add(defaultSearchRadioButton, 0, 2);

        debugWideRadioButton.Text = "Debug (Wide — tolerance 500, velocity sweep)";
        debugWideRadioButton.AutoSize = true;
        searchParamsPanel.Controls.Add(debugWideRadioButton, 0, 3);
        searchParamsPanel.SetColumnSpan(debugWideRadioButton, 2);

        throwTypeLabel.Text = "Grenade Type:";
        throwTypeLabel.AutoSize = true;
        throwTypeLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
        searchParamsPanel.Controls.Add(throwTypeLabel, 0, 4);

        throwTypeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        throwTypeComboBox.Items.AddRange(new object[] { "Smoke", "Flashbang", "HE Grenade", "Decoy", "Incendiary / Molotov" });
        throwTypeComboBox.SelectedIndex = 0;
        throwTypeComboBox.Width = 150;
        searchParamsPanel.Controls.Add(throwTypeComboBox, 1, 4);

        reverseScanCheckBox.Text = "Reverse Scan (find start positions for a known target)";
        reverseScanCheckBox.AutoSize = true;
        searchParamsPanel.Controls.Add(reverseScanCheckBox, 0, 5);
        searchParamsPanel.SetColumnSpan(reverseScanCheckBox, 2);

        mainPanel.Controls.Add(searchParamsPanel, 0, 1);

        // ============ FIND LINEUP BUTTON ============
        findLineupButton.Text = "Find Lineup";
        findLineupButton.Height = 40;
        findLineupButton.Dock = System.Windows.Forms.DockStyle.Fill;
        findLineupButton.Enabled = false;
        findLineupButton.Click += OnFindLineupClick;
        mainPanel.Controls.Add(findLineupButton, 0, 2);

        // ============ STATUS LABEL ============
        statusLabel.Text = "Load a map to start";
        statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        statusLabel.AutoSize = true;
        mainPanel.Controls.Add(statusLabel, 0, 3);

        // ============ SEARCH PROGRESS ============
        searchProgressBar.Dock = System.Windows.Forms.DockStyle.Fill;
        searchProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
        searchProgressBar.Visible = false;
        mainPanel.Controls.Add(searchProgressBar, 0, 4);

        // ============ RESULTS GROUP ============
        resultsGroupBox.Text = "Results";
        resultsGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
        resultsGroupBox.AutoSize = true;
        resultsGroupBox.Padding = new System.Windows.Forms.Padding(5);

        resultsListBox.Dock = System.Windows.Forms.DockStyle.Top;
        resultsListBox.Height = 150;
        resultsListBox.SelectedIndexChanged += OnResultsListBoxSelectedIndexChanged;
        resultsGroupBox.Controls.Add(resultsListBox);

        detailsLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        detailsLabel.AutoSize = true;
        detailsLabel.Text = "Select a result to view details";
        detailsLabel.Padding = new System.Windows.Forms.Padding(5);
        resultsGroupBox.Controls.Add(detailsLabel);

        mainPanel.Controls.Add(resultsGroupBox, 0, 5);

        // ============ TEST IN CS2 BUTTON ============
        testInCS2Button.Text = "Test in CS2";
        testInCS2Button.Height = 40;
        testInCS2Button.Dock = System.Windows.Forms.DockStyle.Fill;
        testInCS2Button.Enabled = false;
        testInCS2Button.Click += OnTestInCS2Click;
        mainPanel.Controls.Add(testInCS2Button, 0, 6);

        // ============ DEBUG TRAJECTORY BUTTON ============
        debugTrajectoryButton.Text = "Debug Trajectory";
        debugTrajectoryButton.Height = 30;
        debugTrajectoryButton.Dock = System.Windows.Forms.DockStyle.Fill;
        debugTrajectoryButton.Enabled = false;
        debugTrajectoryButton.Click += OnDebugTrajectoryClick;
        mainPanel.Controls.Add(debugTrajectoryButton, 0, 7);

        // ============ DEBUG KNOWN THROW BUTTON ============
        debugKnownThrowButton.Text = "Debug Known Throw (hardcoded test)";
        debugKnownThrowButton.Height = 30;
        debugKnownThrowButton.Dock = System.Windows.Forms.DockStyle.Fill;
        debugKnownThrowButton.Enabled = false;
        debugKnownThrowButton.Click += OnDebugKnownThrowClick;
        mainPanel.Controls.Add(debugKnownThrowButton, 0, 8);

        // ============ RESUME LAYOUTS ============
        mainPanel.ResumeLayout(false);
        mainPanel.PerformLayout();
        positionGroupBox.ResumeLayout(false);
        positionGroupBox.PerformLayout();
        positionInnerPanel.ResumeLayout(false);
        positionInnerPanel.PerformLayout();
        startPositionPanel.ResumeLayout(false);
        startPositionPanel.PerformLayout();
        targetPositionPanel.ResumeLayout(false);
        targetPositionPanel.PerformLayout();
        searchParamsPanel.ResumeLayout(false);
        searchParamsPanel.PerformLayout();
        resultsGroupBox.ResumeLayout(false);
        resultsGroupBox.PerformLayout();

        Controls.Add(mainPanel);
        ResumeLayout(false);
    }

    private System.Windows.Forms.TableLayoutPanel mainPanel;
    private System.Windows.Forms.GroupBox positionGroupBox;
    private System.Windows.Forms.TableLayoutPanel positionInnerPanel;
    private System.Windows.Forms.TableLayoutPanel startPositionPanel;
    private System.Windows.Forms.Label startLabel;
    private System.Windows.Forms.Button pickStartButton;
    private System.Windows.Forms.Label startXLabel;
    private System.Windows.Forms.TextBox startXTextBox;
    private System.Windows.Forms.Label startYLabel;
    private System.Windows.Forms.TextBox startYTextBox;
    private System.Windows.Forms.Label startZLabel;
    private System.Windows.Forms.TextBox startZTextBox;

    private System.Windows.Forms.TableLayoutPanel targetPositionPanel;
    private System.Windows.Forms.Label targetLabel;
    private System.Windows.Forms.Button pickTargetButton;
    private System.Windows.Forms.Label targetXLabel;
    private System.Windows.Forms.TextBox targetXTextBox;
    private System.Windows.Forms.Label targetYLabel;
    private System.Windows.Forms.TextBox targetYTextBox;
    private System.Windows.Forms.Label targetZLabel;
    private System.Windows.Forms.TextBox targetZTextBox;

    private System.Windows.Forms.TableLayoutPanel searchParamsPanel;
    private System.Windows.Forms.Label searchParamsLabel;
    private System.Windows.Forms.RadioButton coarseSearchRadioButton;
    private System.Windows.Forms.RadioButton defaultSearchRadioButton;
    private System.Windows.Forms.RadioButton debugWideRadioButton;
    private System.Windows.Forms.Label throwTypeLabel;
    private System.Windows.Forms.ComboBox throwTypeComboBox;
    private System.Windows.Forms.CheckBox reverseScanCheckBox;

    private System.Windows.Forms.Button findLineupButton;
    private System.Windows.Forms.Label statusLabel;
    private System.Windows.Forms.ProgressBar searchProgressBar;

    private System.Windows.Forms.GroupBox resultsGroupBox;
    private System.Windows.Forms.ListBox resultsListBox;
    private System.Windows.Forms.Label detailsLabel;
    private System.Windows.Forms.Button testInCS2Button;
    private System.Windows.Forms.Button debugTrajectoryButton;
    private System.Windows.Forms.Button debugKnownThrowButton;
}
