// =========================================================
//  PuntajeSaludDatos.cs
//  Resumen de salud del sistema, pensado ÚNICAMENTE para el
//  reporte PDF. A diferencia de ReporteSistema (que trae TODOS
//  los procesos y TODOS los programas de inicio para el JSON),
//  esto solo trae lo importante para que el reporte no se
//  vuelva un volcado de datos sino algo legible de un vistazo.
// =========================================================
using System.Collections.Generic;

namespace AplicativoAnalizador.Modelo
{
    /// <summary>
    /// Un proceso "destacado" para el PDF (ej. top 5 por CPU).
    /// No es la lista completa de procesos.
    /// </summary>
    public class ProcesoDestacado
    {
        public string Nombre { get; set; } = "";
        public double Cpu { get; set; }
        public int RamMb { get; set; }
    }

    /// <summary>
    /// Un programa de inicio de alto impacto y habilitado, para
    /// mostrarlo como highlight en el PDF (no la lista completa).
    /// </summary>
    public class ProgramaInicioDestacado
    {
        public string Nombre { get; set; } = "";
        public string Editor { get; set; } = "";
    }

    /// <summary>
    /// Todo lo que necesita el PDF de salud del sistema, ya resumido:
    /// el puntaje y su desglose (viene de PuntajeSalud), un resumen
    /// rápido de CPU/RAM/Disco, y unos pocos highlights de procesos
    /// pesados y programas de inicio de alto impacto.
    /// </summary>
    public class PuntajeSaludDatos
    {
        public string Fecha { get; set; } = "";

        public int PuntajeTotal { get; set; }
        public string EstadoGeneral { get; set; } = "";
        public List<ComponenteSalud> Desglose { get; set; } = new();

        public double Cpu { get; set; }
        public string CpuEstado { get; set; } = "";
        public double Ram { get; set; }
        public string RamEstado { get; set; } = "";
        public double DiscoLibre { get; set; }
        public string DiscoEstado { get; set; } = "";

        public int ProgramasInicioHabilitados { get; set; }
        public int ProgramasInicioTotal { get; set; }

        public int ProcesosPesadosCantidad { get; set; }
        public List<ProcesoDestacado> ProcesosDestacados { get; set; } = new();

        public List<ProgramaInicioDestacado> ProgramasAltoImpacto { get; set; } = new();
    }
}