using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// Which strategy handles which application. The list of strategies and the prose describing them come from
/// the adapters themselves, so this page needs no maintenance when one is added.
/// </summary>
internal sealed class ApplicationsPage : UserControl
{
    private const string ProcessColumn = "Process";
    private const string StrategyColumn = "Strategy";
    private const string SourceColumn = "Source";
    private const string UserSource = "Yours";
    private const string BuiltInSource = "Built in";

    /// <summary>Shown instead of <see cref="AdapterId.None"/>.</summary>
    private const string NotHandled = "Not handled";

    private readonly IDictionary<string, AdapterId> _model;
    private readonly IReadOnlyList<AdapterDescriptor> _adapters;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        EditMode = DataGridViewEditMode.EditOnEnter,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        Name = "ApplicationGrid",
    };

    private readonly Button _add = new() { Text = "&Add application", AutoSize = true, Name = "AddApplication" };
    private readonly Button _remove = new() { Text = "&Remove", AutoSize = true, Name = "RemoveApplication", Enabled = false };
    private readonly Label _description = new() { Dock = DockStyle.Fill, AutoSize = true, Height = 48 };

    /// <summary>Creates the page.</summary>
    /// <param name="model">The routing map from the working copy; edited in place when the grid changes.</param>
    /// <param name="adapters">The strategies this build has, from the live router.</param>
    public ApplicationsPage(IDictionary<string, AdapterId> model, IReadOnlyList<AdapterDescriptor> adapters)
    {
        _model = model;
        _adapters = adapters;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        BuildColumns();
        _grid.CellValueChanged += (_, _) => Harvest();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // A combo box does not commit until focus leaves the cell, which makes the grid feel broken.
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.DataError += (_, e) => e.ThrowException = false;

        _add.Click += (_, _) => Add();
        _remove.Click += (_, _) => Remove();

        Controls.Add(BuildLayout());
        Reload();
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ProcessColumn,
            HeaderText = "Application",
            ToolTipText = "The process name, with or without \".exe\".",
            FillWeight = 40,
        });

        var strategy = new DataGridViewComboBoxColumn
        {
            Name = StrategyColumn,
            HeaderText = "Handled by",
            FillWeight = 30,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
        };

        foreach (var adapter in _adapters.Select(a => a.DisplayName).Order(StringComparer.CurrentCulture))
            strategy.Items.Add(adapter);

        strategy.Items.Add(NotHandled);
        _grid.Columns.Add(strategy);

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = SourceColumn,
            HeaderText = "Source",
            ReadOnly = true,
            FillWeight = 30,
        });
    }

    private TableLayoutPanel BuildLayout()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([_add, _remove]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Applications SmartZoom already knows about are shown greyed out; you only need a row here to "
                + "change one of those, or to add something it has never heard of.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 40,
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(_grid);
        layout.Controls.Add(_description);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void Add()
    {
        var row = _grid.Rows[_grid.Rows.Add("", _adapters[0].DisplayName, UserSource)];
        row.Cells[ProcessColumn].Selected = true;
        _grid.BeginEdit(selectAll: true);
    }

    private void Remove()
    {
        if (_grid.CurrentRow is not { } row || row.ReadOnly)
            return;

        _grid.Rows.Remove(row);
        Harvest();
    }

    private void UpdateSelection()
    {
        var row = _grid.CurrentRow;
        _remove.Enabled = row is not null && !row.ReadOnly;

        var name = row?.Cells[StrategyColumn].Value as string;
        _description.Text = _adapters.FirstOrDefault(a => a.DisplayName == name)?.Description
            ?? (name == NotHandled ? "SmartZoom ignores this application; its own zoom is left alone." : string.Empty);
    }

    /// <summary>Reads the grid back into the settings. The grid is the truth while this page is open.</summary>
    private void Harvest()
    {
        _model.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.ReadOnly || row.Cells[ProcessColumn].Value is not string process || string.IsNullOrWhiteSpace(process))
                continue;

            _model[process.Trim()] = Id(row.Cells[StrategyColumn].Value as string);
        }
    }

    private AdapterId Id(string? displayName) =>
        _adapters.FirstOrDefault(a => a.DisplayName == displayName)?.Id ?? AdapterId.None;

    private void Reload()
    {
        _grid.Rows.Clear();

        // The user's own entries first, because those are the ones they came here to change.
        foreach (var (process, id) in _model)
        {
            var name = _adapters.FirstOrDefault(a => a.Id == id)?.DisplayName
                ?? (id == AdapterId.None ? NotHandled : id.Value);

            _grid.Rows.Add(process, name, UserSource);
        }

        // Then what the adapters claim on their own, so the defaults are visible without being editable: an
        // entry here would say the same thing and then go stale the next time the defaults move.
        foreach (var adapter in _adapters)
        {
            foreach (var process in adapter.DefaultProcesses)
            {
                if (_model.ContainsKey(process))
                    continue;

                var row = _grid.Rows[_grid.Rows.Add(process, adapter.DisplayName, BuiltInSource)];
                row.ReadOnly = true;
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;

                // A read-only combo cell still paints its arrow, which invites a click that does nothing.
                row.Cells[StrategyColumn] = new DataGridViewTextBoxCell { Value = adapter.DisplayName };
            }
        }

        UpdateSelection();
    }
}
