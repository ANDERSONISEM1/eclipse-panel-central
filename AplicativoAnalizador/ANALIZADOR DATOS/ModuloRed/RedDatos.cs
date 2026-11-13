// =========================================================
//  RedDatos.cs — SIN IP PÚBLICA, SIN VELOCIDAD + BANDAS Wi-Fi ✅
// =========================================================
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AplicativoAnalizador.Datos
{
    /// <summary>
    /// Lectura cruda y real del estado de red.
    /// TODOS los valores se leen directamente del sistema operativo.
    /// NADA se simula ni se adivina.
    /// </summary>
    public class LecturaRed
    {
        public bool ConectadoInternet { get; set; }
        public string NombreAdaptador { get; set; } = "Sin adaptador";
        public string TipoAdaptador { get; set; } = "Desconocido";
        public string EstadoAdaptador { get; set; } = "Desconectado";
        public string BandasSoportadas { get; set; } = "Desconocido"; // ✅ Bandas Wi-Fi
        public string IpLocal { get; set; } = "Sin IP";
        public DateTime UltimaVerificacion { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// CAPA DE DATOS — Lee información real de red desde las APIs de Windows.
    /// </summary>
    public class RedDatos
    {
        private readonly string _servicioPrueba = "8.8.8.8"; // DNS de Google
        private readonly int _puertoPrueba = 53;

        /// <summary>
        /// Lee TODA la información de red en una sola pasada.
        /// </summary>
        public LecturaRed LeerActual()
        {
            var lectura = new LecturaRed
            {
                UltimaVerificacion = DateTime.Now
            };

            // 1. Detectar adaptador activo y su información
            var adaptadorActivo = BuscarAdaptadorActivo();
            if (adaptadorActivo != null)
            {
                lectura.NombreAdaptador = adaptadorActivo.Name;
                lectura.TipoAdaptador = TraducirTipo(adaptadorActivo.NetworkInterfaceType);
                lectura.EstadoAdaptador = adaptadorActivo.OperationalStatus == OperationalStatus.Up
                    ? "Habilitado"
                    : "Deshabilitado";
                lectura.BandasSoportadas = DetectarBandasWiFi(adaptadorActivo); // ✅ Bandas
                lectura.IpLocal = LeerIpLocal(adaptadorActivo);
            }
            else
            {
                lectura.EstadoAdaptador = "Sin conexión";
            }

            // 2. Verificar conexión REAL a internet
            lectura.ConectadoInternet = TieneConexionInternet();

            return lectura;
        }

        // Busca el adaptador que está activo y conectado
        private NetworkInterface? BuscarAdaptadorActivo()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    !ni.Description.Contains("Virtual") &&
                    !ni.Description.Contains("Hyper-V") &&
                    !ni.Description.Contains("VPN"));
        }

        // Lee la IP v4 local del adaptador activo
        private string LeerIpLocal(NetworkInterface adaptador)
        {
            try
            {
                var propiedades = adaptador.GetIPProperties();
                var ipV4 = propiedades.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                return ipV4 != null ? ipV4.Address.ToString() : "Sin IP";
            }
            catch
            {
                return "Sin IP";
            }
        }

        // ✅ DETECTA BANDAS Wi-Fi: 2.4 GHz, 5 GHz o AMBAS
        private string DetectarBandasWiFi(NetworkInterface adaptador)
        {
            try
            {
                string info = (adaptador.Name + " " + adaptador.Description).ToLower();

                bool tiene5GHz = info.Contains("802.11a") || info.Contains("802.11ac") ||
                                 info.Contains("802.11n") || info.Contains("802.11ax") ||
                                 info.Contains("ac") || info.Contains("ax") ||
                                 info.Contains("wifi 6") || info.Contains("wi-fi 6") ||
                                 info.Contains("dual band") || info.Contains("doble banda") ||
                                 info.Contains("/ac") || info.Contains("ax200") ||
                                 info.Contains("ax210") || info.Contains("9560") ||
                                 info.Contains("9260") || info.Contains("8265");

                bool tiene24GHz = info.Contains("802.11b") || info.Contains("802.11g") ||
                                  info.Contains("802.11n") || info.Contains("802.11ax") ||
                                  tiene5GHz;

                if (adaptador.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    return "No aplica (Cable)";

                if (tiene24GHz && tiene5GHz)
                    return "2.4 GHz + 5 GHz";
                else if (tiene5GHz)
                    return "5 GHz";
                else
                    return "2.4 GHz";
            }
            catch
            {
                return "Desconocido";
            }
        }

        // Verifica conexión real intentando llegar a un servidor confiable
        private bool TieneConexionInternet()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                using var cliente = new TcpClient();
                cliente.ReceiveTimeout = 1500;
                cliente.SendTimeout = 1500;

                try
                {
                    cliente.Connect(_servicioPrueba, _puertoPrueba);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // Traduce el tipo de adaptador a texto amigable
        private string TraducirTipo(NetworkInterfaceType tipo) => tipo switch
        {
            NetworkInterfaceType.Ethernet => "Ethernet",
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Loopback => "Bucle local",
            NetworkInterfaceType.Ppp => "Módem / PPP",
            _ => tipo.ToString()
        };
    }
}