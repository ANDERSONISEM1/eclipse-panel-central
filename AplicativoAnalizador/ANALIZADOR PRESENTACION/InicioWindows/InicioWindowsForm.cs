// =========================================================
//  InicioWindowsForm.cs
//  Misma lista que "Aplicaciones de arranque" del Administrador
//  de tareas: nombre, editor, estado e impacto.
//  Botón Habilitar/Deshabilitar: escribe el cambio donde Windows lo
//  guarda, así que el Administrador de tareas lo refleja.
//  Si el programa es del equipo (HKLM / carpeta de todos los usuarios)
//  y la app no es administrador, ofrece reiniciarse como administrador.
//  Se refresca sola al entrar a la pantalla y al volver a enfocar
//  la ventana.
// =========================================================

using AplicativoAnalizador.Logica;
using AplicativoAnalizador.Modelo;
using AplicativoAnalizador.Servicios;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;

namespace AplicativoAnalizador.Presentacion
{
    public static class InicioWindowsForm
    {
        const int panelAnchoTotal = 912;
        const int margenInterior = 18;
        const int anchoContenido = panelAnchoTotal - margenInterior * 2;
        static readonly ToolTip _tooltip = new() { AutoPopDelay = 8000, InitialDelay = 200, ReshowDelay = 100 };
        static bool _refrescando;

        // =========================================================
        //  CONSTRUIR VISTA
        // =========================================================
        public static Panel CrearVista(StartupService startupService, FormPrincipal formPrincipal, FlowLayoutPanel startupList)
        {
            var root = new Panel { Dock = DockStyle.Fill };

            var title = new Label
            {
                Text = "Programas de inicio de Windows",
                Font = FormPrincipal.FontDisplay,
                ForeColor = FormPrincipal.TextMain,
                AutoSize = true,
                Location = new Point(0, 0)
            };

            var sub = new Label
            {
                Text = "Estos programas arrancan junto con Windows. Los cambios que hagas aquí se reflejan en el Administrador de tareas. " +
                       "Algunos piden permisos de administrador según dónde estén guardados.",
                ForeColor = FormPrincipal.TextDim,
                AutoSize = true,
                Location = new Point(0, 30),
                MaximumSize = new Size(860, 0)
            };

            root.Controls.Add(title);
            root.Controls.Add(sub);

            var panel = formPrincipal.PanelBase("Lista de arranque", string.Empty);
            panel.Location = new Point(0, 86);
            panel.Size = new Size(panelAnchoTotal, 430);
            panel.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            var btnAdmin = formPrincipal.MakeButton("Abrir Administrador de tareas");
            btnAdmin.Location = new Point(panelAnchoTotal - margenInterior - btnAdmin.PreferredSize.Width, 8);
            btnAdmin.Click += (_, _) => AbrirAdministradorDeTareas(formPrincipal);
            panel.Controls.Add(btnAdmin);

            startupList.Location = new Point(margenInterior, 60);
            startupList.Size = new Size(anchoContenido, 360);
            startupList.FlowDirection = FlowDirection.TopDown;
            startupList.WrapContents = false;
            startupList.AutoScroll = true;

            panel.Controls.Add(startupList);

            // Se refresca al mostrar la pantalla y al volver a la ventana
            // (por ejemplo, después de cambiar algo en el Administrador de tareas).
            root.VisibleChanged += (_, _) =>
            {
                if (root.Visible) Refrescar(startupService, startupList, formPrincipal);
            };
            formPrincipal.Activated += (_, _) =>
            {
                if (root.Visible) Refrescar(startupService, startupList, formPrincipal);
            };

            root.Controls.Add(panel);
            return root;
        }

        // =========================================================
        //  REFRESCAR: vuelve a leer Windows y repinta
        // =========================================================
        public static void Refrescar(StartupService startupService, FlowLayoutPanel startupList, FormPrincipal formPrincipal)
        {
            if (_refrescando) return;
            _refrescando = true;
            try
            {
                startupService.Generar();
                Renderizar(startupService, startupList, formPrincipal, null);
                formPrincipal.RenderSalud();
            }
            finally
            {
                _refrescando = false;
            }
        }

        // =========================================================
        //  RENDERIZAR LISTA
        // =========================================================
        public static void Renderizar(StartupService startupService, FlowLayoutPanel startupList,
            FormPrincipal formPrincipal, Action? alActualizarSalud = null)
        {
            var scroll = startupList.AutoScrollPosition;

            startupList.SuspendLayout();
            startupList.Controls.Clear();

            int anchoCaja = anchoContenido - 4 - SystemInformation.VerticalScrollBarWidth;

            if (startupService.Programas.Count == 0)
            {
                startupList.Controls.Add(new Label
                {
                    Text = "No se encontraron programas de inicio.",
                    ForeColor = FormPrincipal.TextFaint,
                    Font = FormPrincipal.FontSmall,
                    AutoSize = true
                });
            }

            foreach (var prog in startupService.Programas)
            {
                var box = new Panel
                {
                    Width = anchoCaja,
                    Height = 56,
                    BackColor = FormPrincipal.BgPanelAlt,
                    Margin = new Padding(0, 0, 0, 8)
                };

                box.Paint += (_, e) =>
                {
                    using var pen = new Pen(FormPrincipal.BorderCol);
                    e.Graphics.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1);
                };

                // ---- Botón (derecha) ----
                var btnToggle = formPrincipal.MakeButton(
                    prog.EsProtegido ? "🔒 Protegido" : prog.Habilitado ? "Deshabilitar" : "Habilitar");
                btnToggle.AutoSize = false;
                btnToggle.Size = new Size(124, 34);
                btnToggle.Location = new Point(anchoCaja - 124 - 16, 11);
                btnToggle.Enabled = !prog.EsProtegido;

                // ---- Chip de impacto ----
                var (colorImpacto, fondoImpacto) = ColoresImpacto(prog.Impacto);
                var chipImpacto = Chip("IMPACTO: " + prog.Impacto.ToUpper(), colorImpacto, fondoImpacto);
                chipImpacto.Location = new Point(btnToggle.Left - 12 - chipImpacto.PreferredWidth, 16);

                // ---- Chip de estado ----
                var chipEstado = prog.Habilitado
                    ? Chip("HABILITADO", FormPrincipal.Green, FormPrincipal.GreenDim)
                    : Chip("DESHABILITADO", FormPrincipal.TextDim, Color.FromArgb(226, 232, 240));
                chipEstado.Location = new Point(chipImpacto.Left - 8 - chipEstado.PreferredWidth, 16);

                // ---- Textos (izquierda) ----
                int anchoTexto = chipEstado.Left - 16 - 12;

                var lblNombre = new Label
                {
                    Text = prog.Nombre,
                    ForeColor = FormPrincipal.TextMain,
                    Font = FormPrincipal.FontBodyBold,
                    Location = new Point(16, 8),
                    AutoSize = false,
                    AutoEllipsis = true,
                    Size = new Size(anchoTexto, 20)
                };

                var lblEditor = new Label
                {
                    Text = $"{prog.Editor}  ·  {OrigenLegible(prog.TipoOrigen)}",
                    ForeColor = FormPrincipal.TextFaint,
                    Font = FormPrincipal.FontSmall,
                    Location = new Point(16, 29),
                    AutoSize = false,
                    AutoEllipsis = true,
                    Size = new Size(anchoTexto, 18)
                };

                string detalle = string.IsNullOrWhiteSpace(prog.Comando) ? prog.Nombre : prog.Comando;
                _tooltip.SetToolTip(lblNombre, detalle);
                _tooltip.SetToolTip(lblEditor, detalle);
                if (prog.EsProtegido) _tooltip.SetToolTip(btnToggle, prog.RazonProtegido);

                var progRef = prog;
                btnToggle.Click += (_, _) =>
                {
                    var resultado = startupService.AlternarEstado(progRef);

                    if (!resultado.Exito)
                    {
                        // Programa del equipo y la app no es administrador: se ofrece reiniciar con permisos.
                        if (InicioWindowsLogica.RequiereAdmin(progRef.TipoOrigen) && !EsAdministrador())
                        {
                            var respuesta = MessageBox.Show(formPrincipal,
                                $"Para cambiar \"{progRef.Nombre}\" se necesitan permisos de administrador.\n\n" +
                                "¿Quieres reiniciar ECLIPSE como administrador ahora?",
                                "ECLIPSE", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (respuesta == DialogResult.Yes)
                                ReiniciarComoAdministrador(formPrincipal);
                            return;
                        }

                        MessageBox.Show(
                            resultado.Motivo,
                            $"No se pudo cambiar \"{progRef.Nombre}\"",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    // Se vuelve a leer Windows para mostrar el estado real.
                    Refrescar(startupService, startupList, formPrincipal);
                    alActualizarSalud?.Invoke();
                };

                box.Controls.Add(lblNombre);
                box.Controls.Add(lblEditor);
                box.Controls.Add(chipEstado);
                box.Controls.Add(chipImpacto);
                box.Controls.Add(btnToggle);
                startupList.Controls.Add(box);
            }

            startupList.ResumeLayout();

            // Conserva la posición del scroll
            startupList.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        }

        // =========================================================
        //  ADMINISTRADOR
        // =========================================================
        static bool EsAdministrador()
        {
            try
            {
                using var identidad = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        static void ReiniciarComoAdministrador(FormPrincipal formPrincipal)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                });
                Application.Exit();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // El usuario canceló el aviso de permisos: no es un error.
            }
            catch (Exception ex)
            {
                MessageBox.Show(formPrincipal,
                    $"No se pudo reiniciar como administrador:\n{ex.Message}",
                    "ECLIPSE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =========================================================
        //  ABRIR EL ADMINISTRADOR DE TAREAS (pestaña de inicio)
        // =========================================================
        static void AbrirAdministradorDeTareas(FormPrincipal formPrincipal)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskmgr.exe", "/0 /startup") { UseShellExecute = true });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // El usuario canceló el aviso de permisos: no es un error.
            }
            catch (Exception ex)
            {
                MessageBox.Show(formPrincipal,
                    $"No se pudo abrir el Administrador de tareas:\n{ex.Message}",
                    "ECLIPSE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =========================================================
        //  AUXILIARES DE UI
        // =========================================================
        static Label Chip(string texto, Color fore, Color back) => new()
        {
            Text = texto,
            ForeColor = fore,
            BackColor = back,
            Font = FormPrincipal.FontSmall,
            Padding = new Padding(7, 3, 7, 3),
            AutoSize = true
        };

        static (Color fore, Color back) ColoresImpacto(string impacto) => impacto switch
        {
            "Alto" => (FormPrincipal.ColorDeEstado(EstadoMetrica.Critico), FormPrincipal.ColorChipDeEstado(EstadoMetrica.Critico)),
            "Medio" => (FormPrincipal.ColorDeEstado(EstadoMetrica.Advertencia), FormPrincipal.ColorChipDeEstado(EstadoMetrica.Advertencia)),
            "Bajo" => (FormPrincipal.ColorDeEstado(EstadoMetrica.Normal), FormPrincipal.ColorChipDeEstado(EstadoMetrica.Normal)),
            _ => (FormPrincipal.TextDim, Color.FromArgb(226, 232, 240)),   // Ninguno
        };

        static string OrigenLegible(string tipoOrigen) => tipoOrigen switch
        {
            "HKCU" => "Registro (usuario)",
            "HKLM" => "Registro (equipo)",
            "HKCU32" => "Registro 32 bits (usuario)",
            "HKLM32" => "Registro 32 bits (equipo)",
            "Carpeta" => "Carpeta de inicio",
            "CarpetaTodosLosUsuarios" => "Carpeta de inicio (todos los usuarios)",
            "Tienda" => "Aplicación de la Tienda",
            _ => "Desconocido"
        };
    }
}