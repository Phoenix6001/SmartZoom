using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>The gestures that start a zoom: add, edit, remove. Any number of them can be active at once.</summary>
internal sealed class TriggersPage : UserControl
{
    private readonly ITriggerSource _triggers;
    private readonly uint _systemDoubleClickMs;
    private readonly IList<TriggerSettings> _model;

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Name = "TriggerList",
    };

    private readonly Button _add = new() { Text = "&Add…", AutoSize = true, Name = "AddTrigger" };
    private readonly Button _edit = new() { Text = "&Edit…", AutoSize = true, Name = "EditTrigger", Enabled = false };
    private readonly Button _remove = new() { Text = "&Remove", AutoSize = true, Name = "RemoveTrigger", Enabled = false };

    /// <summary>Creates the page over the settings being edited.</summary>
    /// <param name="model">The trigger list from the working copy; edited in place.</param>
    /// <param name="triggers">The live trigger source, silenced while a trigger is being recorded.</param>
    /// <param name="systemDoubleClickMs">Windows' own double-click time, the default for a new double-tap trigger.</param>
    public TriggersPage(IList<TriggerSettings> model, ITriggerSource triggers, uint systemDoubleClickMs)
    {
        _model = model;
        _triggers = triggers;
        _systemDoubleClickMs = systemDoubleClickMs;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        _list.Columns.Add("Trigger", 200);
        _list.Columns.Add("Presses", 110);
        _list.Columns.Add("Hidden from the app", 160);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => Edit();

        _add.Click += (_, _) => Add();
        _edit.Click += (_, _) => Edit();
        _remove.Click += (_, _) => Remove();

        Controls.Add(BuildLayout());
        Reload();
    }

    private TableLayoutPanel BuildLayout()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.AddRange([_add, _edit, _remove]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Any of these starts a zoom. A mouse button and a keyboard shortcut can both be active, "
                + "which is useful when the same settings are shared between a desktop and a laptop.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 40,
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(_list);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void Add()
    {
        using var recorder = new TriggerRecorderDialog(_triggers, _systemDoubleClickMs, existing: null);
        if (recorder.ShowDialog(this) != DialogResult.OK)
            return;

        _model.Add(recorder.Result);
        Reload();
        Select(_model.Count - 1);
    }

    private void Edit()
    {
        if (Selected is not { } index)
            return;

        using var recorder = new TriggerRecorderDialog(_triggers, _systemDoubleClickMs, _model[index]);
        if (recorder.ShowDialog(this) != DialogResult.OK)
            return;

        _model[index] = recorder.Result;
        Reload();
        Select(index);
    }

    private void Remove()
    {
        if (Selected is not { } index)
            return;

        // The last one is not stopped here: the validator explains why one is needed, and taking the choice
        // away mid-edit is worse than letting it be corrected before Save.
        _model.RemoveAt(index);
        Reload();
        Select(Math.Min(index, _model.Count - 1));
    }

    private int? Selected => _list.SelectedIndices.Count == 0 ? null : _list.SelectedIndices[0];

    private void Select(int index)
    {
        if (index >= 0 && index < _list.Items.Count)
            _list.Items[index].Selected = true;

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _edit.Enabled = Selected is not null;
        _remove.Enabled = Selected is not null;
    }

    /// <summary>Redraws the list from the model, which is the only thing that holds the order.</summary>
    private void Reload()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var trigger in _model)
        {
            var presses = trigger.TapCount == 2
                ? $"Double-tap ({trigger.DoubleTapWindowMs ?? _systemDoubleClickMs} ms)"
                : "Every press";

            _list.Items.Add(new ListViewItem([Describe(trigger), presses, trigger.SwallowClicks ? "Yes" : "No"]));
        }

        _list.EndUpdate();
        UpdateButtons();
    }

    /// <summary>What the trigger is, in the words the recorder used, or the raw text when it cannot be read.</summary>
    private static string Describe(TriggerSettings trigger) =>
        trigger.Mouse is { } button and not MouseButton.None ? button.ToString() : trigger.Keys ?? "(not set)";
}
