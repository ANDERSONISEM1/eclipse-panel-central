// =========================================================
//  RedForm.cs — INTERLINEADO UNIFORME ENTRE TODAS LAS FILAS ✅
// =========================================================
using AplicativoAnalizador.Servicios;
using System.Drawing;
using System.Windows.Forms;

namespace AplicativoAnalizador.Presentacion
{
    public partial class FormPrincipal
    {
        readonly NetworkService _networkService = new();
        Label _netStatusVal = new(), _netStatusSub = new(), _netAdapterState = new();

        Panel _filaNombre = new(),
              _filaBandas = new(),
              _filaIp = new();

        Panel BuildViewRed()
        {
            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
            var title = new Label { Text = "Red básica", Font = FontDisplay, ForeColor = TextMain, AutoSize = true };
            var sub = new Label { Text = "Verifica tu conexión, datos del adaptador y configuración de red.", ForeColor = TextDim, AutoSize = true, Margin = new Padding(0, 2, 0, 12) };
            root.Controls.Add(title); root.Controls.Add(sub);

            var cols = new FlowLayoutPanel { AutoSize = true, WrapContents = false };

            // === PANEL IZQUIERDO: Conexión a internet ===
            var p1 = PanelBase("Conexión a internet");
            p1.Size = new Size(372, 220);
            _netStatusVal = new Label { Text = "VERIFICANDO...", Font = FontData, ForeColor = TextDim, Location = new Point(18, 48), AutoSize = true };
            _netStatusSub = new Label { Text = "Última verificación: --:--:--", Font = FontSmall, ForeColor = TextFaint, Location = new Point(18, 96), AutoSize = true };
            var btnCheck = MakeButton("↻ Verificar ahora");
            btnCheck.Location = new Point(18, 126);
            btnCheck.Click += (_, _) => VerificarRed();
            p1.Controls.Add(_netStatusVal); p1.Controls.Add(_netStatusSub); p1.Controls.Add(btnCheck);

            // === PANEL DERECHO: Adaptador de red — INTERLINEADO PERFECTO ✅ ===
            var p2 = PanelBase("Adaptador de red");
            p2.Size = new Size(540, 220);

            // ✅ MISMO ESPACIO ENTRE TODAS: cada una a 36px de distancia
            const int separacion = 36;
            int inicioY = 40; // Separación desde el título "Adaptador de red"

            _filaNombre = KvRowRed("Nombre", "Leyendo...", inicioY);
            _filaBandas = KvRowRed("Bandas Wi-Fi soportadas", "Leyendo...", inicioY + separacion);
            _filaIp     = KvRowRed("IP local", "Leyendo...", inicioY + separacion * 2);

            p2.Controls.Add(_filaNombre);
            p2.Controls.Add(_filaBandas);
            p2.Controls.Add(_filaIp);

            // ✅ Estado — MISMA separación
            int estadoY = inicioY + separacion * 3;
            var estadoWrap = new Panel { Size = new Size(504, 32), Location = new Point(18, estadoY) };
            estadoWrap.Controls.Add(new Label { Text = "Estado", ForeColor = TextDim, Location = new Point(0, 4), AutoSize = true });
            _netAdapterState = new Label { Text = "Leyendo...", ForeColor = TextDim, Location = new Point(280, 4), AutoSize = true };
            estadoWrap.Controls.Add(_netAdapterState);
            p2.Controls.Add(estadoWrap);

            cols.Controls.Add(p1);
            cols.Controls.Add(p2);
            root.Controls.Add(cols);

            VerificarRed();
            return root;
        }

        Panel KvRowRed(string etiqueta, string valor, int posicionY)
        {
            var fila = new Panel { Size = new Size(504, 32), Location = new Point(18, posicionY) };
            fila.Controls.Add(new Label { Text = etiqueta, ForeColor = TextDim, Location = new Point(0, 4), AutoSize = true });
            fila.Controls.Add(new Label { Text = valor, ForeColor = TextMain, Location = new Point(280, 4), AutoSize = true });
            return fila;
        }

        void VerificarRed()
        {
            var estado = _networkService.Verificar();

            _netStatusVal.Text = estado.Conectado ? "CONECTADO" : "SIN CONEXIÓN";
            _netStatusVal.ForeColor = estado.Conectado ? Green : Red;
            _netStatusSub.Text = "Última verificación: " + estado.Verificado.ToString("HH:mm:ss");
            _netAdapterState.Text = $"{estado.TipoAdaptador} — Habilitado";
            _netAdapterState.ForeColor = estado.Conectado ? Green : Red;

            _filaNombre.Controls[1].Text = estado.TipoAdaptador == "Wi-Fi" ? "Wi-Fi" : estado.NombreAdaptador;
            _filaBandas.Controls[1].Text = estado.BandasSoportadas;
            _filaIp.Controls[1].Text = estado.IpLocal;
        }
    }
}