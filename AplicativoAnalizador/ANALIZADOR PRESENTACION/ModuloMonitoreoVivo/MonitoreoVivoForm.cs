// =========================================================
    //  MonitoreoVivoForm.cs
    // =========================================================

using AplicativoAnalizador.Modelo;

namespace AplicativoAnalizador.Presentacion
{
    // Este archivo es la parte de CAPA DE PRESENTACIÓN del módulo
    // "Monitoreo en vivo". Es un partial de FormPrincipal (mismo class,
    // mismo Form, misma interfaz visual pixel-por-pixel que ya tenías)
    // para poder separar el código por módulos SIN romper la ventana
    // única que usas hoy. El día que cada módulo tenga su propio Form,
    // este archivo se convierte directamente en MonitoreoVivoForm : Form.
    //
    // La única diferencia real es que ahora consume _monitoreoVivoLogica
    // (datos reales de tu PC) en vez del MonitorService simulado.
    public partial class FormPrincipal
    {
        // =========================================================
        //  01 — MONITOREO EN VIVO
        // =========================================================
        Panel BuildViewMonitoreo()
        {
            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 16),
                BackColor = BgMain
            };
            var title = new Label
            {
                Text = "Monitoreo en tiempo real",
                Font = FontDisplay,
                ForeColor = TextMain,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            var sub = new Label
            {
                Text = "Actualización automática cada 2 segundos. Azul = normal, ámbar = advertencia, rojo = crítico.",
                ForeColor = TextDim,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16)
            };
            root.Controls.Add(title);
            root.Controls.Add(sub);
            const int panelWidth = 912;
            const int panelPad = 20;
            int cardsWidth = panelWidth - panelPad * 2;
            var panel = PanelBase("Actividad en vivo");
            panel.Size = new Size(panelWidth, 260);
            panel.AutoSize = false;
            panel.Margin = new Padding(0, 0, 0, 16);
            var cardsContainer = new FlowLayoutPanel
            {
                AutoSize = false,
                Width = cardsWidth,
                Height = 130,
                Location = new Point(panelPad, 46),
                WrapContents = false,
                BackColor = Color.Transparent
            };
            var c1 = MetricCard("CPU en vivo", out _liveCpuVal, out _liveCpuBar, out _liveCpuBadge, "Procesamiento");
            var c2 = MetricCard("RAM en vivo", out _liveRamVal, out _liveRamBar, out _liveRamBadge, "Memoria en uso");
            var c3 = MetricCard("DISCO (C:)", out _liveDiskVal, out _liveDiskBar, out _liveDiskBadge, "Crítico < 10% libre");
            cardsContainer.Controls.Add(c1);
            cardsContainer.Controls.Add(c2);
            cardsContainer.Controls.Add(c3);
            panel.Controls.Add(cardsContainer);
            var btnToggle = MakeButton("⏸ Pausar monitoreo");
            btnToggle.Location = new Point(panelPad, 190);
            btnToggle.Click += (_, _) =>
            {
                if (_liveTimer.Enabled)
                {
                    _liveTimer.Stop();
                    btnToggle.Text = "▶ Reanudar monitoreo";
                }
                else
                {
                    _liveTimer.Start();
                    btnToggle.Text = "⏸ Pausar monitoreo";
                }
            };
            panel.Controls.Add(btnToggle);
            root.Controls.Add(panel);

            var panel2 = PanelBase("Gráfica de actividad");
            panel2.Size = new Size(panelWidth, 180);
            panel2.Margin = new Padding(0, 0, 0, 0);
            _timeline = new FlowLayoutPanel
            {
                Location = new Point(panelPad, 45),
                Size = new Size(panelWidth - panelPad * 2, 90),
                BackColor = BgInset,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(4, 4, 4, 4)
            };
            panel2.Controls.Add(_timeline);
            var lblT = new Label
            {
                Text = "Barra más alta = mayor actividad",
                Font = FontSmall,
                ForeColor = TextFaint,
                Location = new Point(panelPad, 142),
                AutoSize = true
            };
            panel2.Controls.Add(lblT);
            root.Controls.Add(panel2);

            // Historial inicial vacío: se llena en vivo con lecturas reales,
            // ya no se rellena con números aleatorios como antes.
            _timelineData.Clear();
            RenderTimeline();
            return root;
        }

        void LiveTick()
        {
            var (cpu, ram, disco) = _monitoreoVivoLogica.Tick();
            PintarMetrica(_liveCpuVal, _liveCpuBar, _liveCpuBadge, (Panel)_liveCpuBar.Parent!, "cpu", cpu.Valor);
            PintarMetrica(_liveRamVal, _liveRamBar, _liveRamBadge, (Panel)_liveRamBar.Parent!, "ram", ram.Valor);
            PintarMetrica(_liveDiskVal, _liveDiskBar, _liveDiskBadge, (Panel)_liveDiskBar.Parent!, "discoLibre", disco.Valor);
            ActualizarEstadoGlobal(cpu.Estado, ram.Estado, disco.Estado);
            _timelineData.Add(cpu.Valor);
            if (_timelineData.Count > 24) _timelineData.RemoveAt(0);
            RenderTimeline();
            RenderSalud();
        }

        void RenderTimeline()
        {
            _timeline.Controls.Clear();
            int barW = Math.Max(8, (_timeline.Width - _timelineData.Count * 4) / Math.Max(1, _timelineData.Count == 0 ? 1 : _timelineData.Count));
            foreach (var v in _timelineData)
            {
                var estado = _monitoreoVivoLogica.Clasificar("cpu", v).Estado;
                var bar = new Panel
                {
                    Width = barW,
                    Height = Math.Max(4, (int)(v / 100.0 * 82)),
                    BackColor = ColorDeEstado(estado),
                    Margin = new Padding(2, 0, 2, 0),
                    Anchor = AnchorStyles.Bottom
                };
                _timeline.Controls.Add(bar);
            }
        }

        void ActualizarEstadoGlobal(EstadoMetrica? cpuEstado = null, EstadoMetrica? ramEstado = null, EstadoMetrica? discoEstado = null)
        {
            var ec = cpuEstado ?? _monitoreoVivoLogica.Clasificar("cpu", _monitoreoVivoLogica.Cpu).Estado;
            var er = ramEstado ?? _monitoreoVivoLogica.Clasificar("ram", _monitoreoVivoLogica.Ram).Estado;
            var ed = discoEstado ?? _monitoreoVivoLogica.Clasificar("discoLibre", _monitoreoVivoLogica.DiscoLibre).Estado;
            if (ec == EstadoMetrica.Critico || er == EstadoMetrica.Critico || ed == EstadoMetrica.Critico)
            { _lblGlobalStatus.Text = "● SISTEMA CRÍTICO"; _lblGlobalStatus.ForeColor = Red; }
            else if (ec == EstadoMetrica.Advertencia || er == EstadoMetrica.Advertencia || ed == EstadoMetrica.Advertencia)
            { _lblGlobalStatus.Text = "● SISTEMA CON ADVERTENCIAS"; _lblGlobalStatus.ForeColor = Amber; }
            else
            { _lblGlobalStatus.Text = "● SISTEMA NORMAL"; _lblGlobalStatus.ForeColor = Green; }
            var metricaDisco = _monitoreoVivoLogica.Clasificar("discoLibre", _monitoreoVivoLogica.DiscoLibre);
            _maintDiskInfo.Text = $"Espacio libre actual en disco: {_monitoreoVivoLogica.DiscoLibre:0}% — {metricaDisco.TextoEstado}";
            _maintDiskInfo.ForeColor = ColorDeEstado(metricaDisco.Estado);
        }
    }
}