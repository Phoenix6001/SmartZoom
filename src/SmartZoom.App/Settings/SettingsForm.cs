using System.Diagnostics;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Tray;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Interop;

namespace SmartZoom.App.Settings;

/// <summary>
/// The settings window. Edits a copy, so nothing takes effect until Save — and when it does, it takes effect
/// at once rather than at the next restart.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly SettingsApplier _applier;
    private readonly AppPaths _paths;
    private readonly SmartZoomSettings _working;

    private readonly ZoomPage _zoom;
    private readonly DiagnosticsPage _diagnostics;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Name = "Tabs" };
    private readonly Label _problems = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Height = 44,
        ForeColor = Palette.ErrorText,
        Name = "Problems",
    };

    private readonly Button _save = new() { Text = "&Save", AutoSize = true, Name = "Save" };
    private readonly Button _close = new() { Text = "&Close", AutoSize = true, Name = "Close" };

    /// <summary>Opens the window on a copy of the settings in force.</summary>
    /// <param name="applier">Validates and applies whatever is saved here.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="engine">Supplies the strategies the applications page offers.</param>
    /// <param name="triggers">Silenced while a trigger is being recorded.</param>
    /// <param name="systemInput">Supplies the system double-click time the triggers page shows as the default window.</param>
    /// <param name="paths">For the "open the file" escape hatch.</param>
    /// <param name="recorder">What has gone wrong so far, shown on the Diagnostics tab.</param>
    /// <param name="facts">What this machine is, shown on the Diagnostics tab.</param>
    /// <param name="initialTab">Which tab the window opens on.</param>
    public SettingsForm(
        SettingsApplier applier,
        SettingsHolder holder,
        ZoomEngine engine,
        ITriggerSource triggers,
        ISystemInput systemInput,
        AppPaths paths,
        DiagnosticRecorder recorder,
        IMachineFacts facts,
        SettingsTab initialTab = SettingsTab.Triggers)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(systemInput);

        _applier = applier;
        _paths = paths;
        _working = SettingsStore.Clone(holder.Current);

        Text = "SmartZoom settings";
        Name = "SmartZoomSettings";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 520);
        ClientSize = new Size(760, 600);
        Icon = TrayIcon.Load();
        CancelButton = _close;

        _zoom = new ZoomPage(_working, OpenSettingsFile);
        _diagnostics = new DiagnosticsPage(recorder, _working.Diagnostics, facts, paths);

        _tabs.TabPages.Add(Page("Triggers", new TriggersPage(_working.Triggers, triggers, systemInput.DoubleClickTimeMs)));
        _tabs.TabPages.Add(Page("Applications", new ApplicationsPage(_working.Routing.Apps, engine.Current.Router.Adapters)));
        _tabs.TabPages.Add(Page("Zoom", _zoom));
        _tabs.TabPages.Add(Page("Diagnostics", _diagnostics));
        _tabs.SelectedIndex = (int)initialTab;

        _save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);

        // Close() rather than a DialogResult: this window is shown modelessly, and DialogResult only closes
        // a form that was opened with ShowDialog. CancelButton routes Escape through the same handler.
        _close.Click += (_, _) => Close();

        Controls.Add(BuildLayout(_tabs));
    }

    /// <summary>Brings one tab to the front of an already-open window.</summary>
    /// <param name="tab">The tab to show.</param>
    /// <remarks>
    /// The Diagnostics tab is re-rendered on the way in: its report is a snapshot, and the press the user is
    /// asking about is the one they made a moment ago.
    /// </remarks>
    public void ShowTab(SettingsTab tab)
    {
        _tabs.SelectedIndex = (int)tab;

        if (tab == SettingsTab.Diagnostics)
            _diagnostics.RenderReport();
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    private TableLayoutPanel BuildLayout(Control tabs)
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_close, _save]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs);
        layout.Controls.Add(_problems);
        layout.Controls.Add(buttons);
        return layout;
    }

    /// <summary>
    /// Saving applies: the work happens off the UI thread, because putting settings into force waits for any
    /// zoom already running.
    /// </summary>
    private async Task SaveAsync()
    {
        _zoom.Harvest();

        _save.Enabled = false;
        _problems.Text = "Applying…";
        try
        {
            var result = await Task.Run(() => _applier.ApplyAsync(_working)).ConfigureAwait(true);
            Report(result);

            if (result.InForce)
                Close();
        }
        finally
        {
            _save.Enabled = true;
        }
    }

    private void Report(SettingsApplyResult result)
    {
        if (result.Outcome == SettingsApplyOutcome.AppliedButNotSaved)
        {
            MessageBox.Show(
                this,
                $"The settings are in force, but the file could not be written, so they will be lost on restart.{Environment.NewLine}{Environment.NewLine}{result.Detail}",
                "SmartZoom",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _problems.Text = result.Problems.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, result.Problems.Select(p => p.ToString()));
        _problems.ForeColor = result.InForce ? SystemColors.GrayText : Palette.ErrorText;
    }

    private void OpenSettingsFile()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(_paths.SettingsFile) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Nothing is registered for .json. Not worth a dialog; the path is in the window's own docs.
        }
    }
}
