// =========================================================
    //  Modelo.cs
    // =========================================================

namespace AplicativoAnalizador.Modelo
{
    /// <summary>
    /// Representa un proceso activo del sistema. Corresponde a la clase
    /// documentada en la Tabla 13 y en el Diagrama de Clases (sección 3.9).
    /// </summary>
 

    /// <summary>
    /// Representa un programa configurado para iniciar junto con Windows.
    /// Corresponde a la Tabla 13 y al Diagrama de Clases (sección 3.9).
    /// </summary>
public class StartupItem
{
    public string Nombre { get; set; } = "";
    public string NombreRegistro { get; set; } = "";
    public string Editor { get; set; } = "";
    public string Impacto { get; set; } = "Bajo";
    public bool Habilitado { get; set; } = true;
    public string RutaOrigen { get; set; } = "";
    public string TipoOrigen { get; set; } = "";
    public string Comando { get; set; } = "";
    public bool EsProtegido { get; set; }
    public string RazonProtegido { get; set; } = "";
}

    /// <summary>
    /// Resultado de intentar deshabilitar o habilitar un programa de inicio.
    /// Siempre trae un motivo legible, tanto si tuvo éxito como si no.
    /// </summary>
    public class ResultadoOperacionInicio
    {
        public bool Exito { get; set; }
        public string Motivo { get; set; } = "";

        public static ResultadoOperacionInicio Ok(string motivo = "") => new() { Exito = true, Motivo = motivo };
        public static ResultadoOperacionInicio Fallo(string motivo) => new() { Exito = false, Motivo = motivo };
    }

    /// <summary>
    /// Representa una carpeta detectada por el analizador de disco, con su
    /// ruta y tamaño estimado. Corresponde a la Tabla 13 y al Diagrama de
    /// Clases (3.9).
    /// </summary>
    public class CarpetaPesada
    {
        public string Ruta { get; set; } = "";
        public double TamañoGb { get; set; }
    }

    /// <summary>
    /// Estados posibles de una métrica de monitoreo, según los indicadores
    /// documentados en la sección 1.9 del capítulo.
    /// </summary>
    public enum EstadoMetrica
    {
        Normal,
        Advertencia,
        Critico,
    }

    /// <summary>
    /// Resultado de clasificar una métrica (CPU, RAM o espacio en disco)
    /// según los umbrales de la sección 1.9. Corresponde a la clase
    /// MetricaEstado documentada en la Tabla 13 y el Diagrama de Clases (3.9).
    /// </summary>
    public class MetricaEstado
    {
        public string Tipo { get; set; } = "";
        public double Valor { get; set; }
        public EstadoMetrica Estado { get; set; }

        public string TextoEstado => Estado switch
        {
            EstadoMetrica.Critico => "CRÍTICO",
            EstadoMetrica.Advertencia => "ADVERTENCIA",
            _ => "NORMAL",
        };
    }

    /// <summary>
    /// Un componente individual dentro del desglose del puntaje de salud
    /// (por ejemplo, "CPU" aporta hasta 25 puntos).
    /// </summary>
    public class ComponenteSalud
    {
        public string Nombre { get; set; } = "";
        public int Puntos { get; set; }
        public int Maximo { get; set; }
    }

    /// <summary>
    /// Representa el puntaje de salud del sistema (0-100) junto con su
    /// desglose por componente. Corresponde a la clase PuntajeSalud
    /// documentada en la Tabla 13, el Diagrama de Clases (3.9) y la
    /// sección 3.11.
    /// </summary>
    public class PuntajeSalud
    {
        public int Total { get; set; }
        public string Estado => Total >= 80 ? "Saludable" : Total >= 50 ? "Regular" : "Crítico";
        public List<ComponenteSalud> Desglose { get; set; } = new();
    }

    /// <summary>
    /// Métrica individual dentro del bloque "sistema" del reporte exportado.
    /// </summary>
    public class MetricaReporte
    {
        public double Valor { get; set; }
        public string Estado { get; set; } = "";
    }

    public class ReporteEstadoSistema
    {
        public string EstadoGlobal { get; set; } = "";
        public MetricaReporte Cpu { get; set; } = new();
        public MetricaReporte Ram { get; set; } = new();
        public MetricaReporte Disco { get; set; } = new();
    }

    public class ReporteMantenimiento
    {
        public double EspacioLiberadoTotal { get; set; }
        public string UltimoMantenimiento { get; set; } = "";
    }

    /// <summary>
    /// Estructura completa del reporte JSON exportado desde el panel de
    /// Puntaje de salud. Corresponde a la Tabla 13 y al Diagrama de Clases
    /// (sección 3.9).
    /// </summary>
    public class ReporteSistema
    {
        public string Fecha { get; set; } = "";
        public ReporteEstadoSistema Sistema { get; set; } = new();
        public ReporteMantenimiento Mantenimiento { get; set; } = new();
        public List<ProcesoInfo> Procesos { get; set; } = new();
        public List<StartupItem> ProgramasInicio { get; set; } = new();
        public List<CarpetaPesada> CarpetasPesadas { get; set; } = new();
        public PuntajeSalud PuntajeSalud { get; set; } = new();
    }
    // EN Servicio.cs — BORRA la clase EstadoRed duplicada y deja esto:
     public class EstadoRed
    {
        public bool Conectado { get; set; }
        public string AdaptadorEstado { get; set; } = "";
        public DateTime Verificado { get; set; }
        public string NombreAdaptador { get; set; } = "";
        public string TipoAdaptador { get; set; } = "";
        public string BandasSoportadas { get; set; } = ""; // ✅ Bandas Wi-Fi
        public string IpLocal { get; set; } = "";
        // ❌ QUITADOS: VelocidadFormateada e IpPublica
    }
      public class ProcesoInfo
    {
        public string Nombre { get; set; } = "";
        public double Cpu { get; set; }      // Porcentaje de uso CPU
        public int RamMb { get; set; }       // RAM en MB
        public string Prioridad { get; set; } = "Normal";
        public bool EsCritico { get; set; }  // Verdadero = no se puede finalizar
        public bool EsAppUsuario { get; set; } // Aplicación del usuario vs sistema
    }
}