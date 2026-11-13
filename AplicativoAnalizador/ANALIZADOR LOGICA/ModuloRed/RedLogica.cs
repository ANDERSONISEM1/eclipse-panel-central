// =========================================================
//  RedLogica.cs — SIN VELOCIDAD, SIN IP PÚBLICA ✅
// =========================================================
using System;
using AplicativoAnalizador.Datos;
using AplicativoAnalizador.Modelo;

namespace AplicativoAnalizador.Logica
{
    /// <summary>
    /// CAPA DE LÓGICA — Procesa los datos reales leídos por RedDatos
    /// </summary>
    public class RedLogica
    {
        readonly RedDatos _datos = new();

        /// <summary>
        /// Lee y devuelve el estado completo de la red.
        /// </summary>
        public EstadoRed Verificar()
        {
            var lectura = _datos.LeerActual();

            return new EstadoRed
            {
                Conectado = lectura.ConectadoInternet,
                AdaptadorEstado = ConstruirTextoAdaptador(lectura),
                Verificado = lectura.UltimaVerificacion,
                NombreAdaptador = lectura.NombreAdaptador,
                TipoAdaptador = lectura.TipoAdaptador,
                BandasSoportadas = lectura.BandasSoportadas, // ✅ Bandas Wi-Fi
                IpLocal = lectura.IpLocal
                // ❌ QUITADOS: Velocidad e IP Pública
            };
        }

        private string ConstruirTextoAdaptador(LecturaRed lectura)
        {
            if (!lectura.ConectadoInternet || lectura.EstadoAdaptador != "Habilitado")
                return "Sin conexión";
            return $"{lectura.TipoAdaptador} — {lectura.EstadoAdaptador}";
        }
    }
}