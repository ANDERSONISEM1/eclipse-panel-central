// =========================================================
//  MantenimientoForm.cs
// =========================================================
using AplicativoAnalizador.Datos.ModuloMantenimiento;
using AplicativoAnalizador.Logica.ModuloMantenimiento;

namespace AplicativoAnalizador.Presentacion
{
    // ⚠️ El namespace se mantiene "AplicativoAnalizador.Presentacion"
    // (igual que FormPrincipal.cs): una "partial class" necesita el MISMO
    // namespace + mismo nombre de clase en todos sus archivos.
    public partial class FormPrincipal
    {
        // ---------- CAPA DE LÓGICA DEL MÓDULO ----------
        readonly MantenimientoLogica _mantenimientoLogica = new();

        // ---------- CONTROLES DEL MÓDULO ----------
        Label _taskTemp = new(), _taskTempFolder = new(), _taskPrefetch = new(), _taskTrash = new(), _taskWinUpdate = new();
        Label _maintResult = new(), _maintTotal = new(), _maintDiskInfo = new();
        Button _btnRunMaint = new();

        // =========================================================
        //  02 — MANTENIMIENTO
        // =========================================================
        Panel BuildViewMantenimiento()
        {
            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
            var title = new Label { Text = "Mantenimiento automático", Font = FontDisplay, ForeColor = TextMain, AutoSize = true };
            var sub = new Label { Text = "Ejecuta tareas de limpieza y libera espacio en disco", ForeColor = TextDim, AutoSize = true, Margin = new Padding(0, 2, 0, 12) };
            root.Controls.Add(title); root.Controls.Add(sub);

            var cols = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var panel = PanelBase("Tareas disponibles");
            panel.Size = new Size(540, 390);

            // NOTA: este texto es solo un placeholder de construcción. La fuente
            // de verdad real (con % en vivo y color según estado) la fija
            // ActualizarEstadoGlobal() en MonitoreoVivoForm.cs.
            _maintDiskInfo = new Label
            {
                Text = "Espacio libre actual en disco: calculando...",
                Font = FontBodyBold,
                ForeColor = TextDim,
                Location = new Point(18, 50),
                AutoSize = true
            };
            panel.Controls.Add(_maintDiskInfo);

            int y = 78;
            _taskTemp = AddTaskRow(panel, "🗑 Archivos temporales", ref y);
            _taskTempFolder = AddTaskRow(panel, "📁 Carpeta Temp", ref y);
            _taskPrefetch = AddTaskRow(panel, "🧹 Prefetch", ref y);
            _taskTrash = AddTaskRow(panel, "🗑 Papelera de reciclaje", ref y);
            _taskWinUpdate = AddTaskRow(panel, "🪟 Caché de Windows Update", ref y);

            _btnRunMaint = MakeButton("▶ Ejecutar mantenimiento", primary: true);
            _btnRunMaint.Location = new Point(18, y + 12);
            _btnRunMaint.Click += (_, _) => EjecutarMantenimiento();

            var btnReset = MakeButton("Reiniciar tareas");
            btnReset.Location = new Point(_btnRunMaint.Right + 10, y + 12);
            btnReset.Click += (_, _) =>
            {
                _taskTemp.Text = "pendiente"; _taskTempFolder.Text = "pendiente";
                _taskPrefetch.Text = "pendiente"; _taskTrash.Text = "pendiente"; _taskWinUpdate.Text = "pendiente";
                foreach (var l in new[] { _taskTemp, _taskTempFolder, _taskPrefetch, _taskTrash, _taskWinUpdate }) l.ForeColor = TextDim;
                _maintResult.Text = "Aún no se ha ejecutado el mantenimiento.";
                _mantenimientoLogica.ReiniciarSesion();
                _maintTotal.Text = FormatoTamano.Formatear(0);
                ActualizarEstadoGlobal();
            };
            panel.Controls.Add(_btnRunMaint);
            panel.Controls.Add(btnReset);

            var panel2 = PanelBase("Resultado");
            panel2.Size = new Size(372, 390);
            var resultStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false,
                Location = new Point(18, 50),
                Size = new Size(300, 320),
                BackColor = Color.Transparent,
            };
            _maintResult = new Label { Text = "Aún no se ha ejecutado el mantenimiento.", ForeColor = TextDim, AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 0, 0, 18) };
            _maintTotal = new Label { Text = FormatoTamano.Formatear(0), Font = FontData, ForeColor = TextMain, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            var lblSubT = new Label { Text = "Espacio total liberado en esta sesión", Font = FontSmall, ForeColor = TextFaint, AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0) };
            resultStack.Controls.Add(_maintResult);
            resultStack.Controls.Add(_maintTotal);
            resultStack.Controls.Add(lblSubT);
            panel2.Controls.Add(resultStack);

            cols.Controls.Add(panel);
            cols.Controls.Add(panel2);
            root.Controls.Add(cols);
            return root;
        }

        Label AddTaskRow(Panel parent, string label, ref int y)
        {
            var l = new Label { Text = label, ForeColor = TextDim, Location = new Point(18, y), AutoSize = true };
            var v = new Label { Text = "pendiente", ForeColor = TextDim, Location = new Point(310, y), AutoSize = true };
            parent.Controls.Add(l); parent.Controls.Add(v);
            y += 28;
            return v;
        }

        async void EjecutarMantenimiento()
        {
            _btnRunMaint.Enabled = false;
            _btnRunMaint.Text = "Ejecutando...";
            long liberadoEstaCorrida = 0;
            bool requiereAdmin = false;

            bool esAdmin = MantenimientoLogica.EsAdministrador();
            _maintResult.ForeColor = TextDim;
            _maintResult.Text = "Ejecutando mantenimiento...";

            var filas = new (string nombre, Label lbl)[]
            {
                (CatalogoTareasMantenimiento.ArchivosTemporales, _taskTemp),
                (CatalogoTareasMantenimiento.CarpetaTemp, _taskTempFolder),
                (CatalogoTareasMantenimiento.Prefetch, _taskPrefetch),
                (CatalogoTareasMantenimiento.PapeleraReciclaje, _taskTrash),
                (CatalogoTareasMantenimiento.CacheWindowsUpdate, _taskWinUpdate),
            };

            foreach (var (nombre, lbl) in filas)
            {
                lbl.Text = "limpiando...";
                lbl.ForeColor = Amber;

                // ✅ La limpieza corre en un hilo aparte: la ventana ya no se congela.
                var resultado = await Task.Run(() => _mantenimientoLogica.EjecutarTarea(nombre));

                liberadoEstaCorrida += resultado.BytesLiberados;

                bool tareaDeAdmin =
                    nombre == CatalogoTareasMantenimiento.CarpetaTemp ||
                    nombre == CatalogoTareasMantenimiento.Prefetch ||
                    nombre == CatalogoTareasMantenimiento.CacheWindowsUpdate;

                // Solo se avisa si de verdad faltan permisos de administrador.
                bool faltaAdmin = !esAdmin && tareaDeAdmin
                                  && resultado.ArchivosEliminados == 0
                                  && resultado.ElementosOmitidos > 0;

                if (faltaAdmin) requiereAdmin = true;

                if (!resultado.Exito)
                {
                    lbl.Text = "✗ error";
                    lbl.ForeColor = Red;
                }
                else if (faltaAdmin)
                {
                    lbl.Text = "Requiere administrador";
                    lbl.ForeColor = Amber;
                }
                else
                {
                    lbl.Text = $"✓ {resultado.TamanoLegible}";
                    lbl.ForeColor = Green;
                }

                _maintTotal.Text = FormatoTamano.Formatear(_mantenimientoLogica.TotalLiberadoBytes);
            }

            ActualizarEstadoGlobal();
            RenderSalud();

            _btnRunMaint.Enabled = true;
            _btnRunMaint.Text = "▶ Ejecutar mantenimiento";

            string mensaje = $"Mantenimiento completado. Se liberaron {FormatoTamano.Formatear(liberadoEstaCorrida)} de espacio.";

            if (requiereAdmin)
                mensaje += "\n\nAlgunas tareas requieren ejecutar la aplicación como administrador.";

            _maintResult.Text = mensaje;
            _maintResult.ForeColor = TextDim;
        }
    }
}