// =========================================================
//  MantenimientoDatos.cs
// =========================================================
using System;
using System.Collections.Generic;

namespace AplicativoAnalizador.Datos.ModuloMantenimiento
{
    /// <summary>
    /// Resultado de ejecutar UNA sola tarea de mantenimiento.
    /// Ahora se trabaja en BYTES (antes en GB redondeado a 2 decimales,
    /// por eso carpetas de pocos MB aparecían como "0.00 GB").
    /// </summary>
    public class ResultadoTarea
    {
        public string Nombre { get; set; } = string.Empty;
        public long BytesLiberados { get; set; }
        public int ArchivosEliminados { get; set; }
        public int ElementosOmitidos { get; set; }   // en uso / sin permisos
        public bool Exito { get; set; } = true;
        public string Mensaje { get; set; } = string.Empty;

        // Compatibilidad con código que aún lea GB.
        public double GbLiberados => BytesLiberados / (1024.0 * 1024.0 * 1024.0);

        // Texto listo para mostrar: "0 B", "36.5 KB", "2.61 MB", "1.20 GB".
        public string TamanoLegible => FormatoTamano.Formatear(BytesLiberados);
    }

    /// <summary>
    /// Resultado consolidado de una corrida completa de mantenimiento.
    /// </summary>
    public class ResultadoMantenimiento
    {
        public List<ResultadoTarea> Tareas { get; set; } = new();
        public long TotalLiberadoBytes { get; set; }
        public double TotalLiberadoGb => TotalLiberadoBytes / (1024.0 * 1024.0 * 1024.0);
        public DateTime FechaEjecucion { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Convierte bytes a un texto legible con la unidad adecuada.
    /// </summary>
    public static class FormatoTamano
    {
        public static string Formatear(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";

            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.#} KB";

            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.##} MB";

            return $"{mb / 1024.0:0.##} GB";
        }
    }

    /// <summary>
    /// Catálogo estático con los nombres "oficiales" de cada tarea.
    /// </summary>
    public static class CatalogoTareasMantenimiento
    {
        public const string ArchivosTemporales = "Archivos temporales";
        public const string CarpetaTemp = "Carpeta Temp";
        public const string Prefetch = "Prefetch";
        public const string PapeleraReciclaje = "Papelera de reciclaje";
        public const string CacheWindowsUpdate = "Caché de Windows Update";

        public static readonly string[] Todas =
        {
            ArchivosTemporales,
            CarpetaTemp,
            Prefetch,
            PapeleraReciclaje,
            CacheWindowsUpdate,
        };
    }
}