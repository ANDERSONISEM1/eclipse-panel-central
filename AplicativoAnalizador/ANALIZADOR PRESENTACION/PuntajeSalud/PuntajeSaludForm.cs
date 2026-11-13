// =========================================================
//  PuntajeSaludForm.cs
//  ✅ Reporte PDF con solo lo importante.
//  ✅ Anillo nítido: el número se dibuja dentro del anillo (sin Label encima).
//  ✅ Programas de inicio y Procesos usan las MISMAS reglas que el puntaje
//     (HealthScoreService.EstadoInicio / EstadoProcesos).
//  ✅ Filas de conteo (programas de inicio / procesos pesados) sin números repetidos.
// =========================================================

using AplicativoAnalizador.Modelo;
using AplicativoAnalizador.Servicios;
using AplicativoAnalizador.Logica;

namespace AplicativoAnalizador.Presentacion
{
    // Panel con doble buffer: evita parpadeo y bordes pixelados al repintar.
    internal class AnilloPanel : Panel
    {
        public AnilloPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    public class PuntajeSaludForm
    {
        // Fuente del número dentro del anillo (cabe bien dentro del círculo)
        static readonly Font FuenteAnillo = new("Segoe UI Semibold", 32F, FontStyle.Bold);

        // ---------- CONTROLES DE VISTA SALUD ----------
        public static Label _healthScoreLabel = new();
        public static Panel _healthRing = new();
        public static FlowLayoutPanel _healthBreakdown = new();
        public static Button _btnDownloadReport = new();
        public static int _ultimoScore = 100;

        // ---------- SERVICIOS QUE NECESITA ----------
        readonly MonitoreoVivoLogica _monitoreoVivoLogica;
        readonly ProcessService _processService;
        readonly StartupService _startupService;
        readonly HealthScoreService _healthScoreService;
        readonly ReportService _reportService;

        readonly ReporteSaludPdfService _reporteSaludPdfService = new();

        // ---------- REFERENCIA AL FORMULARIO PRINCIPAL ----------
        readonly FormPrincipal _principal;

        public PuntajeSaludForm(
            MonitoreoVivoLogica monitoreoVivoLogica,
            ProcessService processService,
            StartupService startupService,
            HealthScoreService healthScoreService,
            ReportService reportService,
            FormPrincipal principal)
        {
            _monitoreoVivoLogica = monitoreoVivoLogica;
            _processService = processService;
            _startupService = startupService;
            _healthScoreService = healthScoreService;
            _reportService = reportService;
            _principal = principal;
        }

        // =========================================================
        //  CONSTRUCCIÓN DE VISTA
        // =========================================================
        public Panel CrearVista()
        {
            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
            var title = new Label { Text = "Puntaje de salud del sistema", Font = FormPrincipal.FontDisplay, ForeColor = FormPrincipal.TextMain, AutoSize = true };
            var sub = new Label { Text = "Un único número que combina CPU, RAM, disco, procesos y programas de inicio.", ForeColor = FormPrincipal.TextDim, AutoSize = true, Margin = new Padding(0, 2, 0, 12) };
            root.Controls.Add(title); root.Controls.Add(sub);

            const int panelAnchoTotalSalud = 912;
            const int margen = 18;
            int anchoContenidoSalud = panelAnchoTotalSalud - margen * 2;

            var panel = _principal.PanelBase("Estado general");
            panel.Size = new Size(panelAnchoTotalSalud, 200);

            _healthRing = new AnilloPanel { BackColor = FormPrincipal.BgPanel, Location = new Point(margen, 50), Size = new Size(140, 140) };
            _healthRing.Paint += (_, e) => PintarAnilloSalud(e.Graphics);
            panel.Controls.Add(_healthRing);

            int posTexto = margen + 140 + 20;
            _healthScoreLabel = new Label { Text = "Calculando...", ForeColor = FormPrincipal.TextDim, Font = FormPrincipal.FontBodyBold, Location = new Point(posTexto, 60), AutoSize = true };
            panel.Controls.Add(_healthScoreLabel);

            var btnUpdateStatus = _principal.MakeButton("↻ Actualizar estado", primary: true);
            btnUpdateStatus.Location = new Point(posTexto, 92);
            btnUpdateStatus.Click += (_, _) => RenderSalud();
            panel.Controls.Add(btnUpdateStatus);

            _btnDownloadReport = _principal.MakeButton("⬇ Descargar reporte (PDF)");
            _btnDownloadReport.Location = new Point(posTexto + btnUpdateStatus.Width + 10, 92);
            _btnDownloadReport.Click += (_, _) => DescargarReporte();
            panel.Controls.Add(_btnDownloadReport);

            root.Controls.Add(panel);

            var panel2 = _principal.PanelBase("Desglose por componente");
            panel2.Size = new Size(panelAnchoTotalSalud, 260);
            _healthBreakdown = new FlowLayoutPanel { Location = new Point(margen, 50), Size = new Size(anchoContenidoSalud, 190), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            panel2.Controls.Add(_healthBreakdown);
            root.Controls.Add(panel2);

            return root;
        }

        // =========================================================
        //  RENDERIZADO Y LÓGICA
        // =========================================================
        public void RenderSalud()
        {
            if (_healthBreakdown.IsDisposed || _healthBreakdown.Parent == null) return;

            int programasHabilitados = _startupService.ContarHabilitados();
            int programasTotal = _startupService.Programas.Count;
            int procesosPesados = _processService.ContarPesados();

            var puntaje = _healthScoreService.Calcular(
                _monitoreoVivoLogica.Cpu, _monitoreoVivoLogica.Ram, _monitoreoVivoLogica.DiscoLibre,
                programasHabilitados, programasTotal, procesosPesados);

            _ultimoScore = puntaje.Total;
            _healthRing.Invalidate();

            _healthScoreLabel.Text = puntaje.Total >= 80 ? "Sistema saludable" : puntaje.Total >= 50 ? "Sistema con puntos a mejorar" : "Sistema necesita atención";
            _healthScoreLabel.ForeColor = puntaje.Total >= 80 ? FormPrincipal.Green : puntaje.Total >= 50 ? FormPrincipal.Amber : FormPrincipal.Red;

            _healthBreakdown.Controls.Clear();
            _healthBreakdown.Controls.Add(FilaSaludTexto("CPU", _monitoreoVivoLogica.Cpu, "Uso de procesador"));
            _healthBreakdown.Controls.Add(FilaSaludTexto("Memoria RAM", _monitoreoVivoLogica.Ram, "Uso de memoria"));
            _healthBreakdown.Controls.Add(FilaSaludTexto("Disco Duro (C:)", _monitoreoVivoLogica.DiscoLibre, "Espacio libre"));
            _healthBreakdown.Controls.Add(FilaSaludTexto("Programas de inicio", programasHabilitados,
                $"{programasTotal} programas de arranque  •  {programasHabilitados} habilitados"));
            _healthBreakdown.Controls.Add(FilaSaludTexto("Procesos pesados", procesosPesados,
                $"{procesosPesados} procesos con CPU ≥ 25%"));
        }

        void PintarAnilloSalud(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            const int grosor = 10;
            int inset = grosor / 2 + 3;
            var rect = new Rectangle(inset, inset, _healthRing.Width - inset * 2 - 1, _healthRing.Height - inset * 2 - 1);

            using var penBg = new Pen(FormPrincipal.BorderCol, grosor);
            g.DrawEllipse(penBg, rect);

            int score = Math.Clamp(_ultimoScore, 0, 100);
            var color = score >= 80 ? FormPrincipal.Green : score >= 50 ? FormPrincipal.Amber : FormPrincipal.Red;

            if (score >= 100)
            {
                using var penFull = new Pen(color, grosor);
                g.DrawEllipse(penFull, rect);
            }
            else if (score > 0)
            {
                float angulo = 360f * score / 100f;
                using var penScore = new Pen(color, grosor)
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round
                };
                g.DrawArc(penScore, rect, -90, angulo);
            }

            TextRenderer.DrawText(
                g,
                score.ToString(),
                FuenteAnillo,
                new Rectangle(0, 0, _healthRing.Width, _healthRing.Height),
                FormPrincipal.TextMain,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);
        }

        static string TextoDeEstado(EstadoMetrica e) => e switch
        {
            EstadoMetrica.Critico => "CRÍTICO",
            EstadoMetrica.Advertencia => "ADVERTENCIA",
            _ => "NORMAL",
        };

        Panel FilaSaludTexto(string nombre, double valor, string detalle)
        {
            const int anchoFila = 876;
            var wrap = new Panel { Size = new Size(anchoFila, 32), Margin = new Padding(0, 0, 0, 4) };
            string estado;
            Color color;

            switch (nombre)
            {
                case "CPU":
                    var mCpu = _monitoreoVivoLogica.Clasificar("cpu", valor);
                    estado = mCpu.TextoEstado; color = FormPrincipal.ColorDeEstado(mCpu.Estado);
                    break;
                case "Memoria RAM":
                    var mRam = _monitoreoVivoLogica.Clasificar("ram", valor);
                    estado = mRam.TextoEstado; color = FormPrincipal.ColorDeEstado(mRam.Estado);
                    break;
                case "Disco Duro (C:)":
                    var mDisco = _monitoreoVivoLogica.Clasificar("discoLibre", valor);
                    estado = mDisco.TextoEstado; color = FormPrincipal.ColorDeEstado(mDisco.Estado);
                    break;
                default:
                    // ✅ Misma regla que usa el cálculo del puntaje
                    var eDefault = nombre == "Programas de inicio"
                        ? HealthScoreService.EstadoInicio((int)valor)
                        : HealthScoreService.EstadoProcesos((int)valor);
                    estado = TextoDeEstado(eDefault);
                    color = FormPrincipal.ColorDeEstado(eDefault);
                    break;
            }

            var lblNombre = new Label
            {
                Text = nombre,
                ForeColor = FormPrincipal.TextMain,
                Font = FormPrincipal.FontBodyBold,
                AutoSize = true,
                Location = new Point(0, 6)
            };

            var lblEstado = new Label
            {
                Text = estado,
                ForeColor = color,
                Font = FormPrincipal.FontBodyBold,
                AutoSize = true,
                Location = new Point(160, 6)
            };

            // En las filas de conteo el detalle ya trae los números: no se repite el valor.
            bool esConteo = nombre == "Programas de inicio" || nombre == "Procesos pesados";
            string valorTexto = $"{valor:0}%";
            var lblValor = new Label
            {
                Text = esConteo ? detalle : $"{valorTexto}  •  {detalle}",
                ForeColor = FormPrincipal.TextFaint,
                Font = FormPrincipal.FontSmall,
                AutoSize = true,
                Location = new Point(250, 8)
            };

            wrap.Controls.Add(lblNombre);
            wrap.Controls.Add(lblEstado);
            wrap.Controls.Add(lblValor);
            return wrap;
        }

        // =========================================================
        //  DESCARGA DE REPORTE — EN PDF, SOLO LO IMPORTANTE
        // =========================================================
        void DescargarReporte()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Archivos PDF (*.pdf)|*.pdf",
                    Title = "Guardar reporte de salud del sistema",
                    FileName = $"Reporte_Salud_ECLIPSE_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pdf"
                };

                if (saveFileDialog.ShowDialog() != DialogResult.OK) return;

                int programasHabilitados = _startupService.ContarHabilitados();
                int procesosPesados = _processService.ContarPesados();

                var puntaje = _healthScoreService.Calcular(
                    _monitoreoVivoLogica.Cpu, _monitoreoVivoLogica.Ram, _monitoreoVivoLogica.DiscoLibre,
                    programasHabilitados, _startupService.Programas.Count, procesosPesados);

                var datosResumen = _reporteSaludPdfService.ConstruirResumen(
                    puntaje,
                    _monitoreoVivoLogica.Cpu, _monitoreoVivoLogica.Clasificar("cpu", _monitoreoVivoLogica.Cpu).TextoEstado,
                    _monitoreoVivoLogica.Ram, _monitoreoVivoLogica.Clasificar("ram", _monitoreoVivoLogica.Ram).TextoEstado,
                    _monitoreoVivoLogica.DiscoLibre, _monitoreoVivoLogica.Clasificar("discoLibre", _monitoreoVivoLogica.DiscoLibre).TextoEstado,
                    _processService.Procesos,
                    _startupService.Programas);

                _reporteSaludPdfService.GenerarPdf(datosResumen, saveFileDialog.FileName);

                MessageBox.Show(_principal,
                    $"Reporte de salud guardado exitosamente en:\n{saveFileDialog.FileName}",
                    "ECLIPSE - Reporte generado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(_principal,
                    $"Error al generar el reporte:\n{ex.Message}",
                    "ECLIPSE - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}