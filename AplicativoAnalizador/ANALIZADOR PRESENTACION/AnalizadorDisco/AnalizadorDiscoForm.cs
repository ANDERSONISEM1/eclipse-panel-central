// =========================================================
//  AnalizadorDiscoForm.cs — COMPLETO, SIN DUPLICADOS ✅
// =========================================================
using AplicativoAnalizador.Modelo;
using AplicativoAnalizador.Servicios;
using AplicativoAnalizador.Logica;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AplicativoAnalizador.Presentacion
{
    public partial class FormPrincipal
    {
        // ⚠️ QUITAMOS la declaración duplicada de _diskAnalyzerService
        // ⚠️ Déjala ÚNICAMENTE en FormPrincipal.cs como ya la tenías

        // =========================================================
        //  05 — ANALIZADOR DE DISCO
        // =========================================================
        Panel _diskFolderList = new();
        Label _diskScanStatus = new();
        ProgressBar _diskScanProgress = new();
        Button _btnScanDisk = new();

        Panel BuildViewAnalizadorDisco()
        {
            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
            var title = new Label { Text = "Analizador de disco", Font = FontDisplay, ForeColor = TextMain, AutoSize = true };
            var sub = new Label { Text = "Encuentra qué carpetas ocupan más espacio en tu disco.", ForeColor = TextDim, AutoSize = true, Margin = new Padding(0, 2, 0, 12) };
            root.Controls.Add(title); root.Controls.Add(sub);

            var cols = new FlowLayoutPanel { AutoSize = true, WrapContents = false };

            // Panel de escaneo
            var scanPanel = PanelBase("Escaneo del disco", "Recorre carpetas en segundo plano");
            scanPanel.Size = new Size(392, 390);

            _btnScanDisk = MakeButton("🔍 Escanear disco (C:)", primary: true);
            _btnScanDisk.Location = new Point(18, 64);
            _btnScanDisk.Click += async (_, _) => await EscanearDisco();

            _diskScanProgress = new ProgressBar { Location = new Point(_btnScanDisk.Right + 16, 70), Size = new Size(240, 20), Style = ProgressBarStyle.Continuous };
            _diskScanStatus = new Label { Text = "Sin escanear todavía.", ForeColor = TextFaint, Font = FontSmall, Location = new Point(18, 108), AutoSize = true };

            scanPanel.Controls.Add(_btnScanDisk);
            scanPanel.Controls.Add(_diskScanProgress);
            scanPanel.Controls.Add(_diskScanStatus);

            // Panel de resultados
            var p1 = PanelBase("Carpetas más pesadas");
            p1.Size = new Size(520, 390);
            _diskFolderList = new FlowLayoutPanel { Location = new Point(18, 55), Size = new Size(484, 320), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            p1.Controls.Add(_diskFolderList);

            cols.Controls.Add(scanPanel);
            cols.Controls.Add(p1);
            root.Controls.Add(cols);

            RenderPlaceholderDisco();
            return root;
        }

        void RenderPlaceholderDisco()
        {
            _diskFolderList.Controls.Clear();
            var l1 = new Label { Text = "Presiona \"Escanear disco\" para ver el ranking.", ForeColor = TextFaint, Font = FontSmall, AutoSize = true };
            _diskFolderList.Controls.Add(l1);
        }

        async Task EscanearDisco()
        {
            _btnScanDisk.Enabled = false;
            _diskScanProgress.Value = 0;

            var progreso = new Progress<int>(p =>
            {
                _diskScanProgress.Value = p;
                _diskScanStatus.Text = $"Escaneando... {p}%";
            });

            var carpetas = await _diskAnalyzerService.EscanearAsync(progreso);
            RenderResultadosDisco();

            _diskScanStatus.Text = $"✅ Escaneo completado — {DateTime.Now:HH:mm:ss} | Carpetas: {carpetas.Count}";
            _btnScanDisk.Enabled = true;
        }

        void RenderResultadosDisco()
        {
            _diskFolderList.Controls.Clear();
            var topCarpetas = _diskAnalyzerService.CarpetasPesadas.OrderByDescending(c => c.TamañoGb).Take(8).ToList();
            double maxGb = topCarpetas.Count > 0 ? topCarpetas.Max(c => c.TamañoGb) : 1;

            var lblHead = new Label { Text = "Ruta de carpeta                ▸ Espacio", Font = FontBodyBold, ForeColor = TextDim, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            _diskFolderList.Controls.Add(lblHead);

            foreach (var c in topCarpetas)
                _diskFolderList.Controls.Add(BarraRanking(c.Ruta, c.TamañoGb, maxGb, $"{c.TamañoGb:0.0} GB"));
        }

        Panel BarraRanking(string etiqueta, double valor, double maxValor, string detalle)
        {
            const int anchoDisponible = 460;
            var wrap = new Panel { Size = new Size(anchoDisponible, 52), Margin = new Padding(0, 0, 0, 6) };

            var lbl = new Label
            {
                Text = etiqueta,
                ForeColor = TextMain,
                Font = FontBodyBold,
                AutoSize = true,
                Location = new Point(0, 0),
                MaximumSize = new Size(anchoDisponible - 90, 0)
            };

            var track = new Panel { BackColor = BgInset, Location = new Point(0, 22), Size = new Size(anchoDisponible - 14, 10) };
            track.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(BorderCol), 0, 0, track.Width - 1, track.Height - 1);

            int w = (int)(track.Width * Math.Clamp(valor / Math.Max(maxValor, 0.001), 0, 1));
            var fill = new Panel { BackColor = Green, Location = new Point(0, 0), Size = new Size(w, 10) };
            track.Controls.Add(fill);

            var lblDet = new Label { Text = detalle, ForeColor = TextFaint, Font = FontSmall, AutoSize = true, Location = new Point(0, 36) };

            wrap.Controls.Add(lbl);
            wrap.Controls.Add(track);
            wrap.Controls.Add(lblDet);
            return wrap;
        }
    }
}