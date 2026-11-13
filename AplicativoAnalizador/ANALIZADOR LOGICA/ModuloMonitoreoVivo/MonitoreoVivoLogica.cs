// =========================================================
    // MonitoreoVivoLogica.cs
    // =========================================================
using System;
using System.Collections.Generic;
using AplicativoAnalizador.Datos;
using AplicativoAnalizador.Modelo;

namespace AplicativoAnalizador.Logica
{
    /// <summary>
    /// CAPA DE LÓGICA del módulo "Monitoreo en vivo".
    /// Aplica las reglas de negocio (umbrales de la sección 1.9) sobre los
    /// datos REALES que entrega MonitoreoVivoDatos, y mantiene el historial
    /// usado por la gráfica de actividad.
    ///
    /// Esta clase reemplaza al antiguo MonitorService simulado. Los umbrales
    /// son EXACTAMENTE los mismos que ya usaban Mantenimiento y Puntaje de
    /// salud (CPU normal &lt;85%, advertencia 85-94%, crítico &gt;=95%;
    /// RAM normal &lt;80%, advertencia 80-91%, crítico &gt;=92%; disco libre
    /// crítico &lt;10%, advertencia &lt;20%) para que el resto de la app siga
    /// funcionando sin cambios de comportamiento.
    /// </summary>
    public class MonitoreoVivoLogica
    {
        readonly MonitoreoVivoDatos _datos;
        readonly List<double> _historialCpu = new();
        const int MaxPuntosHistorial = 24;

        public double Cpu { get; private set; }
        public double Ram { get; private set; }
        public double RamUsadaMb { get; private set; }
        public double RamTotalMb { get; private set; }
        public double DiscoLibre { get; private set; }
        public double DiscoLibreGb { get; private set; }
        public double DiscoTotalGb { get; private set; }
        public string UnidadDisco { get; private set; } = "C:";

        public IReadOnlyList<double> HistorialCpu => _historialCpu;

        public MonitoreoVivoLogica(string unidadDisco = "C:\\")
        {
            _datos = new MonitoreoVivoDatos(unidadDisco);
            // Primera lectura "en vacío": inicializa el cálculo de delta de
            // CPU para que el primer Tick() real ya traiga un valor útil.
            _datos.LeerActual();
        }

        public MetricaEstado Clasificar(string tipo, double valor)
        {
            EstadoMetrica estado = tipo switch
            {
                "discoLibre" => valor < 10 ? EstadoMetrica.Critico
                               : valor < 20 ? EstadoMetrica.Advertencia
                               : EstadoMetrica.Normal,
                "cpu" => valor >= 95 ? EstadoMetrica.Critico
                        : valor >= 85 ? EstadoMetrica.Advertencia
                        : EstadoMetrica.Normal,
                "ram" => valor >= 92 ? EstadoMetrica.Critico
                        : valor >= 80 ? EstadoMetrica.Advertencia
                        : EstadoMetrica.Normal,
                _ => EstadoMetrica.Normal,
            };
            return new MetricaEstado { Tipo = tipo, Valor = valor, Estado = estado };
        }

        /// <summary>
        /// Lee el estado REAL actual del equipo (CPU, RAM, disco) y
        /// actualiza el historial para la gráfica de actividad. Se llama
        /// en cada "tick" del timer de monitoreo (cada 2 segundos).
        /// </summary>
        public (MetricaEstado cpu, MetricaEstado ram, MetricaEstado disco) Tick()
        {
            var lectura = _datos.LeerActual();

            Cpu = lectura.CpuPorcentaje;
            Ram = lectura.RamPorcentaje;
            RamUsadaMb = lectura.RamUsadaMb;
            RamTotalMb = lectura.RamTotalMb;
            DiscoLibre = lectura.DiscoLibrePorcentaje;
            DiscoLibreGb = lectura.DiscoLibreGb;
            DiscoTotalGb = lectura.DiscoTotalGb;
            UnidadDisco = lectura.UnidadDisco;

            _historialCpu.Add(Cpu);
            if (_historialCpu.Count > MaxPuntosHistorial)
                _historialCpu.RemoveAt(0);

            return (Clasificar("cpu", Cpu), Clasificar("ram", Ram), Clasificar("discoLibre", DiscoLibre));
        }


    }
}