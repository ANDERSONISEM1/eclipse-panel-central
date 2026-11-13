// =========================================================
//  ModeloHistorial.cs — Clases que viajan entre BD, Lógica y API
// =========================================================
using System;

namespace AplicativoAnalizador.Modelo
{
    /// <summary>Un resumen guardado en la tabla registros_estado.</summary>
    public class RegistroEstado
    {
        public double Cpu { get; set; }          // promedio de la ventana
        public double CpuMax { get; set; }       // pico de la ventana
        public double Ram { get; set; }
        public double DiscoLibre { get; set; }
        public string Estado { get; set; } = "normal";   // normal | advertencia | critico
        public int PuntajeSalud { get; set; }
        public int ProgramasHabilitados { get; set; }
        public int ProgramasTotal { get; set; }
        public int ProcesosPesados { get; set; }
        public string Origen { get; set; } = "periodico"; // periodico | cambio_estado
        public DateTime RegistradoEn { get; set; }
    }

    /// <summary>Un turno de 8 horas (viene de la vista vista_historial_8h).</summary>
    public class BloqueHistorial
    {
        public DateTime Inicio { get; set; }
        public double CpuProm { get; set; }
        public double CpuPico { get; set; }
        public double RamProm { get; set; }
        public double RamPico { get; set; }
        public double DiscoLibreMin { get; set; }
        public double PuntajeProm { get; set; }
        public long NumRegistros { get; set; }
    }

    /// <summary>Un cambio de estado (tabla alertas).</summary>
    public class AlertaEquipo
    {
        public DateTime Fecha { get; set; }
        public string? EstadoAnterior { get; set; }
        public string EstadoNuevo { get; set; } = "";
        public string Detalle { get; set; } = "";
    }

    public class UsuarioBd
    {
        public int Id { get; set; }
        public string Usuario { get; set; } = "";
        public string NombreCompleto { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public bool EsAdmin { get; set; }
        public bool Activo { get; set; }
        public int IntentosFallidos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
    }

    public class SesionInfo
    {
        public string Usuario { get; set; } = "";
        public DateTime Expira { get; set; }
    }
}