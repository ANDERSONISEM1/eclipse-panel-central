// =========================================================
//  FormPrincipal.cs
// =========================================================


using AplicativoAnalizador.Modelo;
using AplicativoAnalizador.Servicios;
using AplicativoAnalizador.Logica;

namespace AplicativoAnalizador.Presentacion
{
    public partial class FormPrincipal : Form
    {
        // ---------- PALETA: TEMA CLARO AZUL ESET ----------
        // NOTA: estos campos son "public" a propósito. Así, otros archivos
        // (ProcesosForm.cs, PuntajeSaludForm.cs, InicioWindowsForm.cs) que
        // NO son "partial" de esta clase pueden reusar la MISMA paleta en
        // vez de copiar los valores ARGB a mano. Antes estaban duplicados
        // en 3 archivos distintos, lo que hacía fácil que se desincronizaran.
        public static readonly Color BgMain = Color.FromArgb(255, 255, 255);
        static readonly Color BgSidebar = Color.FromArgb(234, 244, 251);
        public static readonly Color BgPanel = Color.FromArgb(255, 255, 255);
        public static readonly Color BgPanelAlt = Color.FromArgb(244, 249, 253);
        public static readonly Color BgInset = Color.FromArgb(240, 245, 250);
        public static readonly Color Green = Color.FromArgb(0, 88, 163);
        public static readonly Color GreenDim = Color.FromArgb(179, 214, 240);
        public static readonly Color Amber = Color.FromArgb(230, 145, 15);
        public static readonly Color Red = Color.FromArgb(211, 47, 47);
        public static readonly Color TextMain = Color.FromArgb(30, 41, 59);
        public static readonly Color TextDim = Color.FromArgb(100, 116, 139);
        public static readonly Color TextFaint = Color.FromArgb(148, 163, 184);
        public static readonly Color BorderCol = Color.FromArgb(214, 224, 233);
        static readonly Color SidebarActiveBg = Color.FromArgb(255, 255, 255);
        static readonly Color SidebarActiveText = Color.FromArgb(0, 88, 163);

        // FUENTES
        public static readonly Font FontDisplay = new("Segoe UI Semibold", 18F, FontStyle.Bold);
        public static readonly Font FontTitle = new("Segoe UI Semibold", 14F, FontStyle.Bold);
        public static readonly Font FontBody = new("Segoe UI", 10F);
        public static readonly Font FontBodyBold = new("Segoe UI Semibold", 10F, FontStyle.Bold);
        static readonly Font FontData = new("Segoe UI Semibold", 22F, FontStyle.Bold);
        public static readonly Font FontHuge = new("Segoe UI Semibold", 40F, FontStyle.Bold);
        public static readonly Font FontSmall = new("Segoe UI", 8.5F);
        static readonly Font FontMono = new("Consolas", 9F);

        // ✅ Tamaño "ideal" de la ventana (a 100% de escala). Si la pantalla es más
        // chica, se recorta automáticamente al área útil (ver AjustarAPantalla).
        const int AnchoIdeal = 1225;
        const int AltoIdeal = 780;

        // ✅ NUEVO — Agente: envía el estado de esta PC a la API central (Kubernetes).
        // Se crea al final del constructor (necesita los servicios de abajo).
        // La URL y la AGENT_KEY salen de eclipse.config.json o de variables de entorno.
        readonly AgenteReporteLogica _agente;
        readonly System.Windows.Forms.Timer _agenteTimer = new() { Interval = 5000 };

        // ---------- CAPA DE SERVICIOS / LÓGICA ----------
        readonly MonitoreoVivoLogica _monitoreoVivoLogica = new();
        readonly ProcessService _processService = new();
        readonly DiskAnalyzerService _diskAnalyzerService = new();
        readonly StartupService _startupService = new();
        readonly HealthScoreService _healthScoreService = new();
        readonly ReportService _reportService;

        // ---------- ESTADO DE UI ----------
        readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 2000 };
        readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };

        // ---------- CONTROLES QUE SE ACTUALIZAN ----------
        Panel _contentHost = new();
        readonly Dictionary<string, Panel> _vistas = new();
        Label _lblGlobalStatus = new();
        Label _lblClock = new();

        // Monitoreo (usados por MonitoreoVivoForm.cs)
        Label _liveCpuVal = new(), _liveCpuBadge = new(), _liveRamVal = new(), _liveRamBadge = new();
        Label _liveDiskVal = new(), _liveDiskBadge = new();
        Panel _liveCpuBar = new(), _liveRamBar = new(), _liveDiskBar = new();
        FlowLayoutPanel _timeline = new();
        readonly List<double> _timelineData = new();

        // Programas de inicio — movida la lógica a InicioWindowsForm.cs
        FlowLayoutPanel _startupList = new();

        // Puntaje de salud — DELEGADO A CLASE EXTERNA
        PuntajeSaludForm? _puntajeSaludForm;

        // ---------- PROPIEDADES PARA ACCESO DESDE OTRAS CLASES ----------
        public string TextoEstadoGlobal => _lblGlobalStatus.Text;
        public List<CarpetaPesada> ObtenerCarpetasPesadas() => _diskAnalyzerService.CarpetasPesadas.ToList();

        public FormPrincipal()
        {
            // ✅ Escalado por DPI: fuentes y controles proporcionales en pantallas
            // con 125%, 150%, etc. Debe ir ANTES de crear los controles.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            _reportService = new ReportService(_monitoreoVivoLogica);
            Text = "ECLIPSE — Terminal de Diagnóstico";

            // ✅ Ventana fija: sin botón de maximizar y sin redimensionar con el mouse.
            // El tamaño y la posición los fija AjustarAPantalla() al cargar.
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;

            BackColor = BgMain;
            ForeColor = TextMain;
            Font = FontBody;

            _startupService.Generar();

            // Inicializar vista de salud
            _puntajeSaludForm = new PuntajeSaludForm(
                _monitoreoVivoLogica,
                _processService,
                _startupService,
                _healthScoreService,
                _reportService,
                this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            root.Controls.Add(BuildSidebar(), 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(BuildTopBar(), 0, 0);
            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = BgMain, Padding = new Padding(24), AutoScroll = true };
            right.Controls.Add(_contentHost, 0, 1);
            root.Controls.Add(right, 1, 0);

            _vistas["monitoreo"] = BuildViewMonitoreo();
            _vistas["mantenimiento"] = BuildViewMantenimiento();
            _vistas["procesos"] = ProcesosView.CrearVista(_processService, this);
            _vistas["red"] = BuildViewRed();
            _vistas["disco"] = BuildViewAnalizadorDisco();
            _vistas["inicio"] = InicioWindowsForm.CrearVista(_startupService, this, _startupList);
            _vistas["salud"] = _puntajeSaludForm.CrearVista();

            foreach (var v in _vistas.Values)
            {
                v.Dock = DockStyle.Fill;
                v.Visible = false;
                _contentHost.Controls.Add(v);
            }

            MostrarVista("monitoreo");

            _liveTimer.Tick += (_, _) => LiveTick();
            _liveTimer.Start();
            _clockTimer.Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();

            VerificarRed();
            RenderProgramasInicio();
            // Antes, la etiqueta de "espacio libre en disco" del panel de
            // Mantenimiento se llenaba con datos de _mantenimientoLogica (en GB)
            // y a los 2 segundos el primer LiveTick la sobrescribía con datos
            // de _monitoreoVivoLogica (en %). Dos formatos/fuentes distintas
            // peleando por la misma etiqueta. Ahora se inicializa una sola vez
            // aquí, con la misma fuente de verdad que usa el resto del dashboard.
            ActualizarEstadoGlobal();
            RenderSalud();

            // ✅ NUEVO — Agente: se crea AL FINAL del constructor porque necesita
            // que ya existan todos los servicios (incluido _networkService, que
            // se declara en RedForm.cs como campo de esta misma clase partial).
            _agente = new AgenteReporteLogica(
                _monitoreoVivoLogica,
                _processService,
                _startupService,
                _healthScoreService,
                _networkService,
                ConfigAgente.Cargar());
            _agente.Iniciar();

            _agenteTimer.Interval = _agente.IntervaloSegundos * 1000;
            _agenteTimer.Tick += (_, _) => _agente.Tick();
            _agenteTimer.Start();

            // ✅ Si el usuario cambia la resolución o conecta/desconecta un monitor,
            // la ventana se reajusta sola.
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += AlCambiarPantalla;
        }

        // =========================================================
        //  AJUSTE A LA PANTALLA — tamaño fijo, nunca más grande que
        //  el área útil (sin tapar la barra de tareas) y centrado.
        // =========================================================
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            AjustarAPantalla();
            InicializarBandeja(); // ✅ NUEVO — ícono en la bandeja del sistema
        }

        void AlCambiarPantalla(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(AjustarAPantalla));
            else AjustarAPantalla();
        }

        void AjustarAPantalla()
        {
            // Pantalla donde está la ventana (o la principal si aún no tiene handle)
            var pantalla = IsHandleCreated ? Screen.FromControl(this) : Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var area = pantalla.WorkingArea;

            // Tamaño ideal escalado por DPI, pero nunca más del 95% del área útil
            // (así siempre queda un margen y no tapa la barra de tareas)
            float escala = DeviceDpi / 96f;
            int ancho = Math.Min((int)(AnchoIdeal * escala), (int)(area.Width * 0.95));
            int alto = Math.Min((int)(AltoIdeal * escala), (int)(area.Height * 0.95));

            // Se liberan los límites anteriores para que el nuevo tamaño no sea
            // recortado (importante al cambiar de monitor o de resolución)
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;

            Size = new Size(ancho, alto);
            Location = new Point(
                area.Left + (area.Width - ancho) / 2,
                area.Top + (area.Height - alto) / 2);

            // Se bloquea el tamaño: estático, no cambia por ningún motivo
            MinimumSize = Size;
            MaximumSize = Size;
        }

        // =========================================================
        //  CIERRE DE LA APLICACIÓN — detiene el agente de reportes.
        // =========================================================
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= AlCambiarPantalla;
            _agenteTimer.Stop();
            _agente.Dispose();
            base.OnFormClosed(e);
        }

        // =========================================================
        //  SIDEBAR
        // =========================================================
        Panel BuildSidebar()
        {
            var sb = new Panel { Dock = DockStyle.Fill, BackColor = BgSidebar };
            sb.Paint += (_, e) => e.Graphics.DrawLine(new Pen(BorderCol), sb.Width - 1, 0, sb.Width - 1, sb.Height);

            var brand = new Label
            {
                Text = "ECLIPSE",
                Font = FontDisplay,
                ForeColor = Green,
                AutoSize = false,
                Height = 44,
                Dock = DockStyle.Top,
                Padding = new Padding(16, 16, 0, 0),
            };

            var sub = new Label
            {
                Text = "TERMINAL DE DIAGNÓSTICO",
                Font = FontSmall,
                ForeColor = TextDim,
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(16, 0, 0, 0),
            };

            sb.Controls.Add(sub);
            sb.Controls.Add(brand);

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10, 12, 10, 0),
            };
            sb.Controls.Add(nav);
            nav.BringToFront();

            (string key, string label)[] items =
            {
                ("monitoreo", "Monitoreo en vivo"),
                ("mantenimiento", "Mantenimiento"),
                ("disco", "Analizador de disco"),
                ("red", "Red"),
                ("procesos", "Procesos"),
                ("inicio", "Inicio de Windows"),
                ("salud", "Puntaje de salud"),
            };

            var buttons = new Dictionary<string, Button>();
            foreach (var (key, label) in items)
            {
                var btn = new Button
                {
                    Text = label,
                    Width = 196,
                    Height = 36,
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 0, 0),
                    BackColor = BgSidebar,
                    ForeColor = TextDim,
                    Font = FontBody,
                    Margin = new Padding(0, 2, 0, 0),
                    Cursor = Cursors.Hand,
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (_, _) =>
                {
                    MostrarVista(key);
                    foreach (var b in buttons.Values) { b.ForeColor = TextDim; b.BackColor = BgSidebar; b.Font = FontBody; }
                    btn.ForeColor = SidebarActiveText;
                    btn.BackColor = SidebarActiveBg;
                    btn.Font = FontBodyBold;
                };
                buttons[key] = btn;
                nav.Controls.Add(btn);
            }

            buttons["monitoreo"].ForeColor = SidebarActiveText;
            buttons["monitoreo"].BackColor = SidebarActiveBg;
            buttons["monitoreo"].Font = FontBodyBold;

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = BgSidebar };
            var dot = new Label { Text = "●", ForeColor = Green, Location = new Point(16, 8), AutoSize = true };
            _lblClock = new Label { Text = "--:--:--", ForeColor = TextFaint, Font = FontSmall, Location = new Point(16, 30), AutoSize = true };
            foot.Controls.Add(dot);
            foot.Controls.Add(_lblClock);
            sb.Controls.Add(foot);

            return sb;
        }

        void MostrarVista(string key)
        {
            foreach (var kv in _vistas) kv.Value.Visible = (kv.Key == key);
            if (key == "salud") RenderSalud();
            if (key == "procesos") ProcesosView.RefrescarSiExiste();
        }

        // =========================================================
        //  BARRA SUPERIOR
        // =========================================================
        Panel BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Fill, BackColor = BgMain, Padding = new Padding(20, 0, 20, 0) };
            bar.Paint += (_, e) => e.Graphics.DrawLine(new Pen(BorderCol), 0, bar.Height - 1, bar.Width, bar.Height - 1);

            _lblGlobalStatus = new Label
            {
                Text = "● SISTEMA NORMAL",
                ForeColor = Green,
                Font = FontBodyBold,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom
            };
            bar.Resize += (_, _) => _lblGlobalStatus.Location = new Point(20, (bar.Height - _lblGlobalStatus.Height) / 2);
            bar.Controls.Add(_lblGlobalStatus);

           var host = new Label { Text = $"NOMBRE: {Environment.MachineName.ToUpper()}", ForeColor = TextDim, Font = FontSmall, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom };   bar.Controls.Add(host);
            bar.Resize += (_, _) => host.Location = new Point(bar.Width - host.Width - 24, (bar.Height - host.Height) / 2);

            return bar;
        }

        // =========================================================
        //  HELPERS DE UI — COMPARTIDOS
        // =========================================================
        public Panel PanelBase(string titulo, string? hint = null)
        {
            var p = new Panel { BackColor = BgPanel, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 12), AutoSize = false };
            p.Paint += (_, e) =>
            {
                using var pen = new Pen(BorderCol);
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                using var penAccent = new Pen(Green, 2);
                e.Graphics.DrawLines(penAccent, new[] { new Point(0, 8), new Point(0, 0), new Point(8, 0) });
                e.Graphics.DrawLines(penAccent, new[] { new Point(p.Width - 9, 0), new Point(p.Width - 1, 0), new Point(p.Width - 1, 8) });
                e.Graphics.DrawLines(penAccent, new[] { new Point(0, p.Height - 9), new Point(0, p.Height - 1), new Point(8, p.Height - 1) });
                e.Graphics.DrawLines(penAccent, new[] { new Point(p.Width - 9, p.Height - 1), new Point(p.Width - 1, p.Height - 1), new Point(p.Width - 1, p.Height - 9) });
            };

            var head = new Label { Text = titulo, Font = FontTitle, ForeColor = Green, AutoSize = true, Location = new Point(18, 10) };
            p.Controls.Add(head);
            if (hint != null)
            {
                var h = new Label { Text = hint, Font = FontSmall, ForeColor = TextFaint, AutoSize = true, Location = new Point(18, 36) };
                p.Controls.Add(h);
            }
            return p;
        }

        public const int MetricCardWidth = 280;
        public const int MetricCardGap = 16;

        public Panel MetricCard(string label, out Label lblVal, out Panel barFill, out Label lblBadge, string sub)
        {
            var card = new Panel { BackColor = BgPanelAlt, Size = new Size(MetricCardWidth, 124), Margin = new Padding(0, 0, MetricCardGap, 0) };
            card.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(BorderCol), 0, 0, card.Width - 1, card.Height - 1);

            var lblLabel = new Label { Text = label.ToUpper(), Font = FontSmall, ForeColor = TextDim, AutoSize = true, Location = new Point(14, 10) };
            var badge = new Label
            {
                Text = "NORMAL",
                Font = FontSmall,
                ForeColor = Green,
                BackColor = GreenDim,
                AutoSize = true,
                Padding = new Padding(7, 3, 7, 3),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            badge.Location = new Point(card.Width - badge.PreferredWidth - 14, 8);

            var val = new Label { Text = "0%", Font = FontData, ForeColor = TextMain, AutoSize = true, Location = new Point(14, 32) };
            var track = new Panel { BackColor = BgInset, Location = new Point(14, 76), Size = new Size(card.Width - 28, 10) };
            track.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(BorderCol), 0, 0, track.Width - 1, track.Height - 1);
            var fill = new Panel { BackColor = Green, Location = new Point(0, 0), Size = new Size(0, 10) };
            track.Controls.Add(fill);

            var lblSub = new Label { Text = sub, Font = FontSmall, ForeColor = TextFaint, AutoSize = true, Location = new Point(14, 94), MaximumSize = new Size(card.Width - 28, 0) };

            card.Controls.Add(lblLabel);
            card.Controls.Add(badge);
            card.Controls.Add(val);
            card.Controls.Add(track);
            card.Controls.Add(lblSub);

            lblVal = val; barFill = fill; lblBadge = badge;
            return card;
        }

        public Button MakeButton(string text, bool primary = false)
        {
            var btn = new Button
            {
                Text = text,
                Height = 34,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 4, 14, 4),
                FlatStyle = FlatStyle.Flat,
                Font = FontBody,
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderColor = GreenDim;
            if (primary) { btn.BackColor = Green; btn.ForeColor = Color.White; btn.FlatAppearance.BorderSize = 0; }
            else { btn.BackColor = BgPanel; btn.ForeColor = Green; btn.FlatAppearance.BorderSize = 1; }
            return btn;
        }

        public static Color ColorDeEstado(EstadoMetrica estado) => estado switch
        {
            EstadoMetrica.Critico => Red,
            EstadoMetrica.Advertencia => Amber,
            _ => Green,
        };

        public static Color ColorChipDeEstado(EstadoMetrica estado) => estado switch
        {
            EstadoMetrica.Critico => Color.FromArgb(252, 226, 226),
            EstadoMetrica.Advertencia => Color.FromArgb(253, 236, 210),
            _ => GreenDim,
        };

        public void PintarMetrica(Label lblVal, Panel barFill, Label badge, Panel track, string tipo, double valor)
        {
            var metrica = _monitoreoVivoLogica.Clasificar(tipo, valor);
            var color = ColorDeEstado(metrica.Estado);
            lblVal.Text = tipo == "discoLibre" ? $"{valor:0}% libre" : $"{valor:0}%";
            double pct = tipo == "discoLibre" ? (100 - valor) : valor;
            int w = (int)(track.Width * Math.Clamp(pct, 0, 100) / 100.0);
            barFill.Size = new Size(Math.Max(0, w), track.Height);
            barFill.BackColor = color;
            badge.Text = metrica.TextoEstado;
            badge.ForeColor = color;
            badge.BackColor = ColorChipDeEstado(metrica.Estado);
            badge.Location = new Point(badge.Parent!.Width - badge.PreferredWidth - 14, 8);
        }

        // =========================================================
        //  PROGRAMAS DE INICIO — LLAMADA A CLASE EXTERNA
        // =========================================================
        public void RenderProgramasInicio()
        {
            InicioWindowsForm.Renderizar(_startupService, _startupList, this, RenderSalud);
        }

        // =========================================================
        //  PUNTAJE DE SALUD — DELEGADO A CLASE EXTERNA
        // =========================================================
        public void RenderSalud()
        {
            _puntajeSaludForm?.RenderSalud();
        }

        // =========================================================
        //  ÍCONO EN LA BANDEJA DEL SISTEMA (área de notificación)
        // =========================================================
        private NotifyIcon _notifyIcon = null!;
        private ContextMenuStrip _menuBandeja = null!;
        private bool _cerrarDefinitivamente;

        void InicializarBandeja()
        {
            // Menú que aparece al hacer clic derecho sobre el ícono
            _menuBandeja = new ContextMenuStrip();
            _menuBandeja.Items.Add("Mostrar ECLIPSE", null, (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            });
            _menuBandeja.Items.Add("-"); // Separador
            _menuBandeja.Items.Add("Cerrar por completo", null, (s, e) =>
            {
                _cerrarDefinitivamente = true;
                this.Close();
            });

            // Crear el ícono en la bandeja
            _notifyIcon = new NotifyIcon
            {
                Text = "ECLIPSE — Monitoreo activo ✅",
                Icon = SystemIcons.Application, // Ícono por defecto de Windows
                ContextMenuStrip = _menuBandeja,
                Visible = true
            };

            // Al hacer doble clic en el ícono → abre la ventana
            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            };
        }

        // Esto controla qué pasa al presionar la X de cerrar
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // Si NO elegí "cerrar por completo", solo esconder la ventana.
            // (Si Windows se apaga o cierra la sesión, sí se deja cerrar.)
            if (!_cerrarDefinitivamente && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // ❌ No cerramos el programa
                this.Hide();     // Escondemos la ventana nada más

                // 🟢 Mensajito que aparece al lado del ícono
                _notifyIcon?.ShowBalloonTip(
                    2000,
                    "ECLIPSE",
                    "Sigo monitoreando ✅\nClic en el ícono para abrir",
                    ToolTipIcon.Info
                );
            }
            else
            {
                // ✅ Si sí vamos a cerrar, limpiamos el ícono
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
        }

    }
}