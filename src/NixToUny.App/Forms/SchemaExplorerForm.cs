using NixToUny.Nixfarma.Analysis;
using NixToUny.Nixfarma.Configuration;
using NixToUny.Nixfarma.Schema;

namespace NixToUny.App.Forms;

public sealed class SchemaExplorerForm : Form
{
    private readonly NixfarmaConnectionOptions _options;
    private readonly NixfarmaSchemaExplorer _explorer;

    private readonly Button _btnDiscover = new() { Text = "Descubrir esquema", AutoSize = true };
    private readonly ComboBox _cmbArea = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly Button _btnCandidates = new() { Text = "Buscar candidatos", AutoSize = true };
    private readonly TextBox _txtSearch = new() { Width = 220, PlaceholderText = "tabla o columna..." };
    private readonly Button _btnSearch = new() { Text = "Buscar", AutoSize = true };
    private readonly Button _btnCopySampleQuery = new() { Text = "Copiar SELECT 20", AutoSize = true };
    private readonly Button _btnExportSchema = new() { Text = "Exportar esquema completo (.json)", AutoSize = true };
    private readonly Button _btnExportSnapshot = new() { Text = "Exportar snapshot completo (.zip)", AutoSize = true };
    private readonly CheckBox _chkSanitizeSnapshot = new()
    {
        Text = "Anonimizar snapshot",
        AutoSize = true,
        Checked = false,
        Margin = new Padding(8, 7, 0, 0)
    };

    private readonly DataGridView _objects = CreateGrid();
    private readonly DataGridView _columns = CreateGrid();
    private readonly DataGridView _relations = CreateGrid();

    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "Solo lectura · el explorador consulta únicamente metadatos Oracle.",
        Padding = new Padding(0, 6, 0, 6)
    };

    private bool _busy;
    private bool _suppressSelection;
    private CancellationTokenSource? _detailCts;

    public SchemaExplorerForm(NixfarmaConnectionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _explorer = new NixfarmaSchemaExplorer(_options);

        Text = "NixToUny - Explorador Oracle de Nixfarma";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 650);
        Size = new Size(1250, 780);
        Font = new Font("Segoe UI", 9F);

        ConfigureGrids();
        Controls.Add(BuildContent());

        _cmbArea.DataSource = Enum.GetValues<NixfarmaDataArea>();
        _cmbArea.Format += (_, e) =>
        {
            if (e.ListItem is NixfarmaDataArea area)
                e.Value = GetAreaText(area);
        };

        _btnDiscover.Click += async (_, _) => await DiscoverAsync(refresh: true);
        _btnCandidates.Click += async (_, _) => await FindCandidatesAsync();
        _btnSearch.Click += async (_, _) => await SearchAsync();
        _btnCopySampleQuery.Click += (_, _) => CopySampleQuery();
        _btnExportSchema.Click += async (_, _) => await ExportSchemaAsync();
        _btnExportSnapshot.Click += async (_, _) => await ExportSnapshotAsync();
        _txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            await SearchAsync();
        };

        _objects.SelectionChanged += async (_, _) => await LoadSelectedObjectAsync();
        Shown += async (_, _) => await DiscoverAsync(refresh: false);
    }

    private Control BuildContent()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Explorador de la base de datos de Nixfarma",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        actions.Controls.Add(_btnDiscover);
        actions.Controls.Add(new Label
        {
            Text = "Área:",
            AutoSize = true,
            Margin = new Padding(18, 7, 4, 0)
        });
        actions.Controls.Add(_cmbArea);
        actions.Controls.Add(_btnCandidates);
        actions.Controls.Add(new Label
        {
            Text = "Búsqueda:",
            AutoSize = true,
            Margin = new Padding(18, 7, 4, 0)
        });
        actions.Controls.Add(_txtSearch);
        actions.Controls.Add(_btnSearch);
        actions.Controls.Add(_btnCopySampleQuery);
        actions.Controls.Add(_btnExportSchema);
        actions.Controls.Add(_btnExportSnapshot);
        actions.Controls.Add(_chkSanitizeSnapshot);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 500
        };

        var left = new GroupBox
        {
            Text = "Tablas y vistas accesibles",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        left.Controls.Add(_objects);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var columnsPage = new TabPage("Columnas");
        columnsPage.Controls.Add(_columns);

        var relationsPage = new TabPage("Relaciones FK");
        relationsPage.Controls.Add(_relations);

        tabs.TabPages.Add(columnsPage);
        tabs.TabPages.Add(relationsPage);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(tabs);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(_status, 0, 3);

        return root;
    }

    private void ConfigureGrids()
    {
        _objects.Columns.Add("Owner", "Owner");
        _objects.Columns.Add("Name", "Objeto");
        _objects.Columns.Add("Type", "Tipo");
        _objects.Columns.Add("Score", "Score");
        _objects.Columns.Add("Matches", "Coincidencias");

        _objects.Columns["Owner"]!.Width = 110;
        _objects.Columns["Name"]!.Width = 190;
        _objects.Columns["Type"]!.Width = 70;
        _objects.Columns["Score"]!.Width = 60;
        _objects.Columns["Matches"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        _columns.Columns.Add("Position", "#");
        _columns.Columns.Add("Name", "Columna");
        _columns.Columns.Add("Type", "Tipo");
        _columns.Columns.Add("Nullable", "NULL");
        _columns.Columns.Add("PK", "PK");

        _columns.Columns["Position"]!.Width = 45;
        _columns.Columns["Name"]!.Width = 220;
        _columns.Columns["Type"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _columns.Columns["Nullable"]!.Width = 60;
        _columns.Columns["PK"]!.Width = 45;

        _relations.Columns.Add("Direction", "Dirección");
        _relations.Columns.Add("Source", "Origen");
        _relations.Columns.Add("SourceColumn", "Columna");
        _relations.Columns.Add("Target", "Destino");
        _relations.Columns.Add("TargetColumn", "Columna destino");
        _relations.Columns.Add("Constraint", "Constraint");

        _relations.Columns["Direction"]!.Width = 75;
        _relations.Columns["Source"]!.Width = 180;
        _relations.Columns["SourceColumn"]!.Width = 130;
        _relations.Columns["Target"]!.Width = 180;
        _relations.Columns["TargetColumn"]!.Width = 130;
        _relations.Columns["Constraint"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private async Task DiscoverAsync(bool refresh)
    {
        if (_busy)
            return;

        SetBusy(true, "Leyendo tablas y vistas accesibles...");

        try
        {
            if (refresh)
                _explorer.ClearCache();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var objects = await _explorer.DiscoverObjectsAsync(cts.Token);

            PopulateObjects(objects);
            _status.Text = $"✓ {objects.Count:N0} tablas/vistas descubiertas. No se ha leído ni modificado ningún dato funcional.";
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ No se pudo explorar el esquema: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FindCandidatesAsync()
    {
        if (_busy || _cmbArea.SelectedItem is not NixfarmaDataArea area)
            return;

        SetBusy(true, $"Buscando candidatos para {GetAreaText(area).ToLowerInvariant()}...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var results = await _explorer.FindCandidatesAsync(area, 40, cts.Token);

            PopulateCandidates(results);
            _status.Text = results.Count == 0
                ? $"No se encontraron candidatos claros para {GetAreaText(area).ToLowerInvariant()}."
                : $"✓ {results.Count} candidatos heurísticos para {GetAreaText(area).ToLowerInvariant()}. Revisa columnas y relaciones antes de dar uno por válido.";
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ Error buscando candidatos: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SearchAsync()
    {
        if (_busy)
            return;

        var query = _txtSearch.Text.Trim();

        if (query.Length == 0)
        {
            await DiscoverAsync(refresh: false);
            return;
        }

        SetBusy(true, $"Buscando '{query}' en nombres de tablas y columnas...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var results = await _explorer.SearchAsync(query, 100, cts.Token);

            PopulateCandidates(results);
            _status.Text = $"✓ {results.Count} coincidencias para '{query}'.";
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ Error de búsqueda: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadSelectedObjectAsync()
    {
        if (_suppressSelection || _objects.SelectedRows.Count == 0)
            return;

        if (_objects.SelectedRows[0].Tag is not OracleObjectInfo selected)
            return;

        _detailCts?.Cancel();
        _detailCts?.Dispose();
        _detailCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            _status.Text = $"Leyendo metadatos de {selected.QualifiedName}...";

            var columnsTask = _explorer.DiscoverColumnsAsync(
                selected.Owner,
                selected.Name,
                _detailCts.Token);

            var relationsTask = string.Equals(selected.ObjectType, "TABLE", StringComparison.OrdinalIgnoreCase)
                ? _explorer.DiscoverRelationsAsync(selected.Owner, selected.Name, _detailCts.Token)
                : Task.FromResult<IReadOnlyList<OracleRelationInfo>>([]);

            await Task.WhenAll(columnsTask, relationsTask);

            PopulateColumns(await columnsTask);
            PopulateRelations(selected, await relationsTask);

            _status.Text =
                $"✓ {selected.QualifiedName}: {(await columnsTask).Count} columnas, {(await relationsTask).Count} relaciones FK.";
        }
        catch (OperationCanceledException)
        {
            // Cambiar rápidamente de selección cancela la lectura anterior.
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ Error leyendo {selected.QualifiedName}: {ex.Message}";
        }
    }

    private void CopySampleQuery()
    {
        if (_objects.SelectedRows.Count == 0 ||
            _objects.SelectedRows[0].Tag is not OracleObjectInfo selected)
        {
            _status.Text = "Selecciona primero una tabla o vista.";
            return;
        }

        var sql = SampleQueryBuilder.Build(selected, 20);
        Clipboard.SetText(sql);

        _status.Text =
            $"✓ Consulta de muestra copiada para {selected.QualifiedName}. " +
            "Devuelve como máximo 20 filas y no modifica datos.";
    }

    private async Task ExportSchemaAsync()
    {
        if (_busy)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Exportar esquema completo accesible de Nixfarma",
            Filter = "JSON (*.json)|*.json",
            FileName = $"nixfarma-schema-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = "json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetBusy(true, "Inventariando todas las tablas, vistas, columnas y relaciones accesibles...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var exporter = new NixfarmaSchemaReportExporter(_explorer);

            var report = await exporter.ExportAsync(
                dialog.FileName,
                20,
                cts.Token);

            _status.Text =
                $"✓ Esquema exportado: {report.TableCount:N0} tablas, {report.ViewCount:N0} vistas, " +
                $"{report.ColumnCount:N0} columnas y {report.RelationCount:N0} relaciones en " +
                $"{report.OwnerCount:N0} owners. El JSON no contiene registros ni credenciales.";
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ No se pudo exportar el mapa: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportSnapshotAsync()
    {
        if (_busy)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Exportar snapshot técnico completo de Nixfarma",
            Filter = "ZIP (*.zip)|*.zip",
            FileName = $"nixfarma-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = "zip"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var answer = MessageBox.Show(
            this,
            "Se intentarán leer hasta 10 filas de cada tabla y vista accesible. " +
            (_chkSanitizeSnapshot.Checked
                ? "La anonimización está activada. "
                : "La anonimización está desactivada y se exportarán valores reales. ") +
            "Los LOB/binarios se omiten. La operación puede tardar varios minutos. ¿Continuar?",
            "Exportar snapshot completo",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (answer != DialogResult.Yes)
            return;

        SetBusy(true, "Preparando snapshot completo...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

            var progress = new Progress<NixfarmaSnapshotProgress>(p =>
            {
                _status.Text =
                    $"Snapshot {p.Current:N0}/{p.Total:N0} · {p.QualifiedName} · {p.Status}";
            });

            var exporter = new NixfarmaSnapshotExporter(_options, _explorer);

            var manifest = await exporter.ExportAsync(
                dialog.FileName,
                10,
                _chkSanitizeSnapshot.Checked,
                progress,
                cts.Token);

            _status.Text =
                $"✓ Snapshot terminado: {manifest.SuccessfulSamples:N0}/{manifest.ObjectCount:N0} objetos " +
                $"con muestra; {manifest.FailedSamples:N0} errores. Archivo: {dialog.FileName}";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "✗ La exportación del snapshot fue cancelada o superó el tiempo máximo.";
        }
        catch (Exception ex)
        {
            _status.Text = $"✗ No se pudo generar el snapshot: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateObjects(IReadOnlyList<OracleObjectInfo> objects)
    {
        _suppressSelection = true;

        try
        {
            _objects.Rows.Clear();

            foreach (var obj in objects)
            {
                var index = _objects.Rows.Add(
                    obj.Owner,
                    obj.Name,
                    obj.ObjectType,
                    string.Empty,
                    string.Empty);

                _objects.Rows[index].Tag = obj;
            }

            SelectFirstObject();
        }
        finally
        {
            _suppressSelection = false;
        }

        _ = LoadSelectedObjectAsync();
    }

    private void PopulateCandidates(IReadOnlyList<SchemaCandidateResult> results)
    {
        _suppressSelection = true;

        try
        {
            _objects.Rows.Clear();

            foreach (var result in results)
            {
                var details = result.MatchingColumns.Count == 0
                    ? string.Join(", ", result.MatchedTerms)
                    : $"{string.Join(", ", result.MatchedTerms)} · cols: {string.Join(", ", result.MatchingColumns.Take(8))}";

                var index = _objects.Rows.Add(
                    result.Object.Owner,
                    result.Object.Name,
                    result.Object.ObjectType,
                    result.Score,
                    details);

                _objects.Rows[index].Tag = result.Object;
            }

            SelectFirstObject();
        }
        finally
        {
            _suppressSelection = false;
        }

        _ = LoadSelectedObjectAsync();
    }

    private void PopulateColumns(IReadOnlyList<OracleColumnInfo> columns)
    {
        _columns.Rows.Clear();

        foreach (var column in columns)
        {
            _columns.Rows.Add(
                column.Position,
                column.Name,
                FormatDataType(column),
                column.IsNullable ? "Sí" : "No",
                column.IsPrimaryKey ? "✓" : string.Empty);
        }
    }

    private void PopulateRelations(
        OracleObjectInfo selected,
        IReadOnlyList<OracleRelationInfo> relations)
    {
        _relations.Rows.Clear();

        foreach (var relation in relations)
        {
            var outgoing = relation.IsOutgoingFrom(selected.Owner, selected.Name);

            _relations.Rows.Add(
                outgoing ? "SALE" : "ENTRA",
                $"{relation.Owner}.{relation.TableName}",
                relation.ColumnName,
                $"{relation.ReferencedOwner}.{relation.ReferencedTableName}",
                relation.ReferencedColumnName,
                relation.ConstraintName);
        }
    }

    private void SelectFirstObject()
    {
        _columns.Rows.Clear();
        _relations.Rows.Clear();

        if (_objects.Rows.Count == 0)
            return;

        _objects.ClearSelection();
        _objects.Rows[0].Selected = true;
        _objects.CurrentCell = _objects.Rows[0].Cells["Name"];
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UseWaitCursor = busy;

        _btnDiscover.Enabled = !busy;
        _btnCandidates.Enabled = !busy;
        _btnSearch.Enabled = !busy;
        _btnCopySampleQuery.Enabled = !busy;
        _btnExportSchema.Enabled = !busy;
        _btnExportSnapshot.Enabled = !busy;
        _chkSanitizeSnapshot.Enabled = !busy;
        _cmbArea.Enabled = !busy;
        _txtSearch.Enabled = !busy;

        if (!string.IsNullOrWhiteSpace(message))
            _status.Text = message;
    }

    private static string FormatDataType(OracleColumnInfo column)
    {
        if (column.DataPrecision is not null)
        {
            return column.DataScale is > 0
                ? $"{column.DataType}({column.DataPrecision},{column.DataScale})"
                : $"{column.DataType}({column.DataPrecision})";
        }

        return column.DataType.Contains("CHAR", StringComparison.OrdinalIgnoreCase) ||
               column.DataType.Contains("RAW", StringComparison.OrdinalIgnoreCase)
            ? $"{column.DataType}({column.DataLength})"
            : column.DataType;
    }

    private static string GetAreaText(NixfarmaDataArea area) => area switch
    {
        NixfarmaDataArea.Articles => "Artículos",
        NixfarmaDataArea.Families => "Familias",
        NixfarmaDataArea.Customers => "Clientes",
        NixfarmaDataArea.Credits => "Créditos",
        NixfarmaDataArea.Stock => "Stock",
        _ => area.ToString()
    };

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        AutoGenerateColumns = false,
        BackgroundColor = SystemColors.Window
    };
}
