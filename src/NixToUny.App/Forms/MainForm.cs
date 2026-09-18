using NixToUny.Nixfarma.Configuration;
using NixToUny.Nixfarma.Connection;
using NixToUny.Nixfarma.Detection;

namespace NixToUny.App.Forms;

public sealed class MainForm : Form
{
    private readonly NixfarmaDetector _detector = new();
    private readonly NixfarmaConnectionTester _connectionTester = new();

    private NixfarmaDetectionResult? _detection;
    private bool _connectionVerified;

    private readonly TextBox _txtTns = CreateReadOnlyTextBox();
    private readonly TextBox _txtAlias = CreateReadOnlyTextBox();
    private readonly TextBox _txtHost = CreateReadOnlyTextBox();
    private readonly TextBox _txtPort = CreateReadOnlyTextBox();
    private readonly TextBox _txtService = CreateReadOnlyTextBox();
    private readonly TextBox _txtUser = new() { Text = "consu", Dock = DockStyle.Fill };
    private readonly TextBox _txtPassword = new() { Text = "consu", UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly TextBox _txtConnection = CreateReadOnlyTextBox();

    private readonly Button _btnDetect = new() { Text = "Detectar Nixfarma", AutoSize = true };
    private readonly Button _btnTest = new() { Text = "Probar conexión", AutoSize = true, Enabled = false };
    private readonly Button _btnExplore = new() { Text = "Explorar BD (solo lectura)", AutoSize = true, Enabled = false };

    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "Pendiente de detección.",
        Padding = new Padding(0, 8, 0, 0)
    };

    public MainForm()
    {
        Text = "NixToUny - Nixfarma → Unycop Next";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 500);
        Size = new Size(900, 570);
        Font = new Font("Segoe UI", 9F);

        Controls.Add(BuildContent());

        _btnDetect.Click += (_, _) => DetectNixfarma();
        _btnTest.Click += async (_, _) => await TestConnectionAsync();
        _btnExplore.Click += (_, _) => OpenSchemaExplorer();
        Shown += (_, _) => DetectNixfarma();
    }

    private Control BuildContent()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 5,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            AutoSize = true,
            Text = "Nixfarma → Unycop Next",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Paso 1 · Detección y validación de la conexión Oracle de Nixfarma",
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 20)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 0, 4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, "tnsnames.ora", _txtTns);
        AddField(fields, "Alias", _txtAlias);
        AddField(fields, "Host", _txtHost);
        AddField(fields, "Puerto", _txtPort);
        AddField(fields, "Servicio", _txtService);
        AddField(fields, "Usuario", _txtUser);
        AddField(fields, "Contraseña", _txtPassword);
        AddField(fields, "Conexión (segura)", _txtConnection);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 16, 0, 0)
        };
        actions.Controls.Add(_btnDetect);
        actions.Controls.Add(_btnTest);
        actions.Controls.Add(_btnExplore);

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(fields);
        root.Controls.Add(actions);
        root.Controls.Add(_status);

        return root;
    }

    private void DetectNixfarma()
    {
        _connectionVerified = false;
        SetBusy(true, "Buscando configuración Oracle de Nixfarma...");

        try
        {
            _detection = _detector.Detect();

            if (_detection is null)
            {
                ClearDetectionFields();
                _status.Text = "No se ha encontrado un tnsnames.ora válido para Nixfarma.";
                _btnTest.Enabled = false;
                _btnExplore.Enabled = false;
                return;
            }

            _txtTns.Text = _detection.TnsNamesPath;
            _txtAlias.Text = _detection.Alias;
            _txtHost.Text = _detection.Host;
            _txtPort.Text = _detection.Port.ToString();
            _txtService.Text = _detection.ServiceName;

            var options = BuildOptions();
            _txtConnection.Text = NixfarmaConnectionFactory.BuildSafeDisplay(options);

            _status.Text = $"Nixfarma detectado: {_detection.Host}:{_detection.Port}/{_detection.ServiceName}.";
            _btnTest.Enabled = true;
        }
        catch (Exception ex)
        {
            _detection = null;
            _connectionVerified = false;
            ClearDetectionFields();
            _status.Text = $"Error durante la detección: {ex.Message}";
            _btnTest.Enabled = false;
            _btnExplore.Enabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestConnectionAsync()
    {
        if (_detection is null)
            return;

        _connectionVerified = false;
        SetBusy(true, "Probando conexión con Oracle...");

        try
        {
            var options = BuildOptions();
            _txtConnection.Text = NixfarmaConnectionFactory.BuildSafeDisplay(options);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _connectionTester.TestAsync(options, cts.Token);

            _connectionVerified = result.Success;

            _status.Text = result.Success
                ? $"✓ {result.Message} ({result.Duration.TotalMilliseconds:N0} ms)"
                : $"✗ {result.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenSchemaExplorer()
    {
        if (!_connectionVerified)
        {
            _status.Text = "Primero valida la conexión con Nixfarma.";
            return;
        }

        using var explorer = new SchemaExplorerForm(BuildOptions());
        explorer.ShowDialog(this);
    }

    private NixfarmaConnectionOptions BuildOptions()
    {
        if (_detection is null)
            throw new InvalidOperationException("Nixfarma todavía no ha sido detectado.");

        return NixfarmaConnectionOptions.FromDetection(
            _detection,
            _txtUser.Text.Trim(),
            _txtPassword.Text);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        UseWaitCursor = busy;
        _btnDetect.Enabled = !busy;
        _btnTest.Enabled = !busy && _detection is not null;
        _btnExplore.Enabled = !busy && _connectionVerified;

        if (!string.IsNullOrWhiteSpace(message))
            _status.Text = message;
    }

    private void ClearDetectionFields()
    {
        _txtTns.Clear();
        _txtAlias.Clear();
        _txtHost.Clear();
        _txtPort.Clear();
        _txtService.Clear();
        _txtConnection.Clear();
    }

    private static TextBox CreateReadOnlyTextBox() => new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window
    };

    private static void AddField(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 12, 7)
        }, 0, row);

        control.Margin = new Padding(0, 4, 0, 4);
        table.Controls.Add(control, 1, row);
    }
}
