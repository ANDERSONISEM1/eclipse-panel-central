
using AplicativoAnalizador.Servicios;
using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AplicativoAnalizador.Presentacion
{
    public static class ProcesosView
    {
        static ListView? _procList;
        static Label? _procSelInfo;
        static Button? _btnKillProc;
        static Button? _btnRefresh;
        static ProcessService? _servicio;
        static FormPrincipal? _padre;
        static CancellationTokenSource? _cts;
        static readonly int IntervaloRefrescoMs = 2000; // ⏱️ Cada 2 segundos

        // ✅ Quitar scroll horizontal a nivel de Windows
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        const int GWL_STYLE = -16;
        const int WS_HSCROLL = 0x00100000;

        static void QuitarScrollHorizontal(ListView? lv)
        {
            if (lv == null || !lv.IsHandleCreated) return;
            int estilo = GetWindowLong(lv.Handle, GWL_STYLE);
            if ((estilo & WS_HSCROLL) != 0)
            {
                estilo &= ~WS_HSCROLL;
                SetWindowLong(lv.Handle, GWL_STYLE, estilo);
            }
        }

        public static Panel CrearVista(ProcessService servicio, FormPrincipal padre)
        {
            _servicio = servicio;
            _padre = padre;

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 16),
                BackColor = FormPrincipal.BgMain
            };

            var title = new Label
            {
                Text = "Procesos activos",
                Font = FormPrincipal.FontDisplay,
                ForeColor = FormPrincipal.TextMain,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var sub = new Label
            {
                Text = "La lista se actualiza automáticamente cada 2 segundos. Los procesos del sistema aparecen al final.",
                ForeColor = FormPrincipal.TextDim,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 16)
            };

            root.Controls.Add(title);
            root.Controls.Add(sub);

            int panelWidth = 912;
            var panel = padre.PanelBase("Todos los procesos");
            panel.Size = new Size(panelWidth, 490);

            var barraSuperior = new Panel
            {
                Location = new Point(18, 50),
                Size = new Size(panelWidth - 36, 72),
                BackColor = FormPrincipal.BgPanelAlt
            };
            barraSuperior.Paint += (_, e) => e.Graphics.DrawRectangle(new Pen(FormPrincipal.BorderCol), 0, 0, barraSuperior.Width - 1, barraSuperior.Height - 1);

            _procSelInfo = new Label
            {
                Text = "Selecciona un proceso de la lista para ver acciones disponibles.",
                ForeColor = FormPrincipal.TextDim,
                Font = FormPrincipal.FontBody,
                Location = new Point(14, 10),
                AutoSize = true
            };

            // ✅ SOLO botón Finalizar proceso
            _btnKillProc = padre.MakeButton("✕ Finalizar proceso");
            _btnKillProc.Location = new Point(0, 38);
            _btnKillProc.Enabled = false;
            _btnKillProc.Click += (_, _) => FinalizarProcesoSeleccionado();

            _btnRefresh = padre.MakeButton("↻ Actualizar");
            _btnRefresh.Location = new Point(_btnKillProc.Right + 80, 38);
            _btnRefresh.Click += async (_, _) => await RefrescarProcesosAsync();

            barraSuperior.Controls.Add(_procSelInfo);
            barraSuperior.Controls.Add(_btnKillProc);
            barraSuperior.Controls.Add(_btnRefresh);

            panel.Controls.Add(barraSuperior);

            _procList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                Location = new Point(18, barraSuperior.Bottom + 10),
                Size = new Size(panelWidth - 36, 340),
                BackColor = FormPrincipal.BgPanel,
                ForeColor = FormPrincipal.TextMain,
                Font = FormPrincipal.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                OwnerDraw = true
            };

            // ✅ COLUMNAS: Proceso | CPU | Prioridad | Estado
            _procList.Columns.Add("Proceso", 260);
            _procList.Columns.Add("CPU %", 90);
            _procList.Columns.Add("Prioridad", 120);
            _procList.Columns.Add("Estado", 350);

            _procList.ColumnWidthChanging += (_, e) =>
            {
                e.Cancel = true;
                e.NewWidth = _procList.Columns[e.ColumnIndex].Width;
            };

            _procList.HandleCreated += (_, _) => QuitarScrollHorizontal(_procList);
            _procList.Resize += (_, _) => QuitarScrollHorizontal(_procList);

            _procList.DrawColumnHeader += (_, e) =>
            {
                e.Graphics.FillRectangle(new SolidBrush(FormPrincipal.BgPanelAlt), e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header?.Text, FormPrincipal.FontBodyBold, e.Bounds, FormPrincipal.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };

            _procList.DrawItem += (_, e) => e.DrawDefault = true;

            _procList.DrawSubItem += (_, e) =>
            {
                var bg = e.Item?.Selected == true ? FormPrincipal.GreenDim : FormPrincipal.BgPanel;
                e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);
                var color = e.Item?.ForeColor ?? FormPrincipal.TextMain;
                TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", FormPrincipal.FontBody, e.Bounds, color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };

            _procList.SelectedIndexChanged += (_, _) => ActualizarPanelSeleccionProceso();

            panel.Controls.Add(_procList);
            root.Controls.Add(panel);

            _ = IniciarRefrescoAutomatico();
            return root;
        }

        public static void DetenerRefresco()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public static async void RefrescarSiExiste()
        {
            if (_procList != null) await RefrescarProcesosAsync();
        }

        static async Task IniciarRefrescoAutomatico()
        {
            DetenerRefresco();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefrescarProcesosAsync(silencioso: true);
                    await Task.Delay(IntervaloRefrescoMs, token);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(IntervaloRefrescoMs, token); }
            }
        }

        static async Task RefrescarProcesosAsync(bool silencioso = false)
        {
            if (_servicio == null) return;
            if (!silencioso && _btnRefresh != null)
            {
                _btnRefresh.Enabled = false;
                _btnRefresh.Text = "Analizando...";
            }

            await _servicio.RefrescarAsync();
            RepintarListaProcesos();

            if (!silencioso && _btnRefresh != null)
            {
                _btnRefresh.Text = "↻ Actualizar";
                _btnRefresh.Enabled = true;
            }
        }

        static void RepintarListaProcesos()
        {
            if (_procList == null || _servicio == null) return;

            string? nombreTopVisible = _procList.TopItem?.Tag as string;
            var seleccionado = _procList.SelectedItems.Count > 0
                ? (string?)_procList.SelectedItems[0].Tag
                : null;

            _procList.Items.Clear();

            var listaOrdenada = _servicio.Procesos
                .OrderBy(p => p.EsCritico)
                .ThenByDescending(p => p.Cpu)
                .ToList();

            foreach (var p in listaOrdenada)
            {
                string estado;
                Color color;
                if (p.EsCritico)
                {
                    estado = "🔒 Protegido (sistema)";
                    color = FormPrincipal.TextDim;
                }
                else if (p.Cpu >= 25)
                {
                    estado = "⚠ Causa de lentitud";
                    color = FormPrincipal.Red;
                }
                else if (p.Cpu >= 10)
                {
                    estado = "Uso moderado";
                    color = FormPrincipal.Amber;
                }
                else
                {
                    estado = "Se puede cerrar";
                    color = Color.FromArgb(30, 120, 60);
                }

                var item = new ListViewItem(p.Nombre)
                {
                    ForeColor = color,
                    Tag = p.Nombre
                };
                item.SubItems.Add(p.Cpu.ToString("0.0") + "%");
                item.SubItems.Add(p.Prioridad);
                item.SubItems.Add(estado);
                _procList.Items.Add(item);
            }

            if (!string.IsNullOrEmpty(seleccionado))
            {
                foreach (ListViewItem item in _procList.Items)
                {
                    if ((string?)item.Tag == seleccionado)
                    {
                        item.Selected = true;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(nombreTopVisible))
            {
                ListViewItem? itemTop = null;
                foreach (ListViewItem item in _procList.Items)
                {
                    if ((string?)item.Tag == nombreTopVisible)
                    {
                        itemTop = item;
                        break;
                    }
                }
                if (itemTop != null)
                {
                    _procList.TopItem = itemTop;
                }
                else if (_procList.Items.Count > 0)
                {
                    _procList.EnsureVisible(_procList.Items.Count - 1);
                }
            }

            QuitarScrollHorizontal(_procList);
            ActualizarPanelSeleccionProceso();
        }

        static void ActualizarPanelSeleccionProceso()
        {
            if (_procList == null || _procSelInfo == null || _btnKillProc == null || _servicio == null) return;

            if (_procList.SelectedItems.Count == 0)
            {
                _procSelInfo.Text = "Selecciona un proceso de la lista para ver acciones disponibles.";
                _procSelInfo.ForeColor = FormPrincipal.TextDim;
                _btnKillProc.Enabled = false;
                return;
            }

            var nombre = (string)_procList.SelectedItems[0].Tag!;
            var proc = _servicio.Procesos.FirstOrDefault(p => p.Nombre == nombre);
            if (proc == null) return;

            if (proc.EsCritico)
            {
                _procSelInfo.Text = $"⚠ \"{nombre}\" es del sistema → NO se puede finalizar.";
                _procSelInfo.ForeColor = FormPrincipal.Amber;
                _btnKillProc.Enabled = false;
                return;
            }

            _procSelInfo.Text = $"\"{nombre}\" → CPU {proc.Cpu:0.0}%";
            _procSelInfo.ForeColor = FormPrincipal.TextDim;
            _btnKillProc.Enabled = true;
        }

        static void FinalizarProcesoSeleccionado()
        {
            if (_procList == null || _servicio == null || _padre == null) return;
            if (_procList.SelectedItems.Count == 0) return;

            var nombre = (string)_procList.SelectedItems[0].Tag!;
            var proc = _servicio.Procesos.FirstOrDefault(p => p.Nombre == nombre);
            if (proc == null) return;

            if (proc.EsCritico)
            {
                MessageBox.Show(_padre,
                    $"\"{nombre}\" es un proceso protegido del sistema y no puede finalizarse.",
                    "ECLIPSE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(_padre,
                $"¿Finalizar el proceso \"{nombre}\"?",
                "ECLIPSE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            bool ok = _servicio.Finalizar(nombre);
            if (!ok)
            {
                MessageBox.Show(_padre,
                    $"No se pudo finalizar \"{nombre}\". Prueba ejecutando la app como administrador.",
                    "ECLIPSE", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RepintarListaProcesos();
            _padre.RenderSalud();
        }
    }
}