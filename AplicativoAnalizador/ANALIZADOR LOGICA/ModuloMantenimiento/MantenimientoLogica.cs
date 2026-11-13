// =========================================================
//  MantenimientoLogica.cs
// =========================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using AplicativoAnalizador.Datos.ModuloMantenimiento;

namespace AplicativoAnalizador.Logica.ModuloMantenimiento
{
    // En Windows este struct es de empaquetado 1 (20 bytes).
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;       // Tamaño total en bytes
        public long i64NumItems;   // Cantidad de elementos
    }

    public class MantenimientoLogica
    {
        // Contadores de lo que se va liberando en la sesión (en bytes).
        public long TotalLiberadoBytes { get; private set; }

        // Compatibilidad: algunos sitios (ReportService) esperan GB.
        public double TotalLiberadoSesion => TotalLiberadoBytes / (1024.0 * 1024.0 * 1024.0);

        // Rutas reales de las carpetas del sistema
        private readonly string _rutaArchivosTemporales = Path.GetTempPath();
        private readonly string _rutaCarpetaTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        private readonly string _rutaPrefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        private readonly string _rutaCacheWinUpdate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

        // Acumulador interno de una tarea
        private sealed class Conteo
        {
            public long Bytes;
            public int Borrados;
            public int Omitidos;
            public bool NoExiste;
        }

        // =====================================================
        //  ¿La app corre como administrador?
        // =====================================================
        public static bool EsAdministrador()
        {
            try
            {
                using var identidad = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identidad).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public double ObtenerEspacioLibreGb(string unidad = "C:")
        {
            try
            {
                var drive = new DriveInfo(unidad);
                return drive.IsReady
                    ? drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        // =====================================================
        //  EJECUTAR UNA TAREA
        // =====================================================
        public ResultadoTarea EjecutarTarea(string nombreTarea)
        {
            var resultado = new ResultadoTarea
            {
                Nombre = nombreTarea,
                Exito = true
            };

            try
            {
                var conteo = new Conteo();

                switch (nombreTarea)
                {
                    case CatalogoTareasMantenimiento.ArchivosTemporales:
                        BorrarContenido(_rutaArchivosTemporales, conteo);
                        break;

                    case CatalogoTareasMantenimiento.CarpetaTemp:
                        BorrarContenido(_rutaCarpetaTemp, conteo);
                        break;

                    case CatalogoTareasMantenimiento.Prefetch:
                        BorrarContenido(_rutaPrefetch, conteo);
                        break;

                    case CatalogoTareasMantenimiento.PapeleraReciclaje:
                        VaciarPapelera(conteo);
                        break;

                    case CatalogoTareasMantenimiento.CacheWindowsUpdate:
                        LimpiarWindowsUpdate(conteo);
                        break;
                }

                resultado.BytesLiberados = conteo.Bytes;
                resultado.ArchivosEliminados = conteo.Borrados;
                resultado.ElementosOmitidos = conteo.Omitidos;
                resultado.Mensaje = ConstruirMensaje(nombreTarea, conteo);
            }
            catch (Exception ex)
            {
                resultado.Exito = false;
                resultado.Mensaje = $"Error: {ex.Message}";
                resultado.BytesLiberados = 0;
            }

            TotalLiberadoBytes += resultado.BytesLiberados;
            return resultado;
        }

        private string ConstruirMensaje(string tarea, Conteo c)
        {
            if (c.NoExiste)
                return "La carpeta no existe en este equipo.";

            bool tarea_requiere_admin =
                tarea == CatalogoTareasMantenimiento.CarpetaTemp ||
                tarea == CatalogoTareasMantenimiento.Prefetch ||
                tarea == CatalogoTareasMantenimiento.CacheWindowsUpdate;

            if (c.Borrados == 0 && c.Omitidos > 0 && tarea_requiere_admin && !EsAdministrador())
                return "Requiere ejecutar la aplicación como administrador.";

            if (tarea == CatalogoTareasMantenimiento.PapeleraReciclaje)
            {
                if (c.Omitidos > 0)
                    return $"Papelera vaciada parcialmente. {c.Omitidos} elemento(s) no se pudieron borrar" +
                           (EsAdministrador() ? "." : " (prueba ejecutando como administrador).");
                if (c.Borrados == 0 && c.Bytes == 0)
                    return "La papelera ya estaba vacía.";
                return "Papelera vaciada correctamente.";
            }

            if (c.Omitidos > 0)
                return $"Completado. {c.Omitidos} elemento(s) en uso o protegidos se omitieron.";

            return "Completado";
        }

        // =====================================================
        //  BORRADO ROBUSTO DE CONTENIDO DE UNA CARPETA
        //  (no borra la carpeta raíz, solo lo que hay adentro)
        // =====================================================
        private void BorrarContenido(string rutaCarpeta, Conteo conteo)
        {
            if (string.IsNullOrWhiteSpace(rutaCarpeta) || !Directory.Exists(rutaCarpeta))
            {
                conteo.NoExiste = true;
                return;
            }

            // --- Archivos de esta carpeta ---
            string[] archivos;
            try
            {
                archivos = Directory.GetFiles(rutaCarpeta);
            }
            catch
            {
                conteo.Omitidos++;
                return;
            }

            foreach (var archivo in archivos)
            {
                try
                {
                    var info = new FileInfo(archivo);
                    long tamano = info.Length;

                    // Los archivos de solo lectura no se pueden borrar sin quitar el atributo.
                    if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                        info.Attributes = FileAttributes.Normal;

                    info.Delete();

                    // Solo se suma lo que REALMENTE se borró.
                    conteo.Bytes += tamano;
                    conteo.Borrados++;
                }
                catch
                {
                    conteo.Omitidos++; // en uso o sin permisos
                }
            }

            // --- Subcarpetas ---
            string[] subcarpetas;
            try
            {
                subcarpetas = Directory.GetDirectories(rutaCarpeta);
            }
            catch
            {
                conteo.Omitidos++;
                return;
            }

            foreach (var sub in subcarpetas)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(sub);

                    // Enlaces simbólicos / junctions: se elimina solo el enlace, no se entra.
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        dirInfo.Delete();
                        continue;
                    }
                }
                catch
                {
                    conteo.Omitidos++;
                    continue;
                }

                BorrarContenido(sub, conteo);

                // Si quedó vacía se elimina; si algo seguía en uso, simplemente se deja.
                try { Directory.Delete(sub, false); }
                catch { }
            }
        }

        // =====================================================
        //  PAPELERA DE RECICLAJE
        //
        //  Por qué antes no borraba:
        //   1) Las APIs de shell (SHEmptyRecycleBin) deben correr en un
        //      hilo STA. Si el mantenimiento corre desde Task.Run (MTA),
        //      la API falla o no hace nada.
        //   2) No se verificaba si realmente quedó vacía.
        //   3) No había plan B.
        //
        //  Ahora:
        //   - Se consulta antes y después (el espacio liberado es REAL).
        //   - Todo corre en un hilo STA dedicado.
        //   - Si algo queda, se borra directo el contenido de $Recycle.Bin.
        // =====================================================
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hWnd, string? pszRootPath, uint dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        // SHERB_NOCONFIRMATION (1) | SHERB_NOPROGRESSUI (2) | SHERB_NOSOUND (4)
        private const uint FlagsSilencioso = 1 | 2 | 4;

        // Ejecuta una función en un hilo STA y devuelve su resultado.
        private static T EjecutarEnSta<T>(Func<T> funcion)
        {
            T resultado = default!;
            Exception? error = null;

            var hilo = new Thread(() =>
            {
                try { resultado = funcion(); }
                catch (Exception ex) { error = ex; }
            });
            hilo.SetApartmentState(ApartmentState.STA);
            hilo.IsBackground = true;
            hilo.Start();
            hilo.Join();

            if (error != null) throw error;
            return resultado;
        }

        // Consulta cuánto hay en la papelera (todas las unidades).
        // Devuelve true si la consulta funcionó.
        private static bool ConsultarPapelera(out long bytes, out long items)
        {
            var info = new SHQUERYRBINFO
            {
                cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>()
            };

            int codigo = SHQueryRecycleBin(null, ref info);
            bytes = codigo == 0 ? info.i64Size : 0;
            items = codigo == 0 ? info.i64NumItems : 0;
            return codigo == 0;
        }

        private void VaciarPapelera(Conteo conteo)
        {
            // 1) Cuánto hay ANTES (en hilo STA)
            var antes = EjecutarEnSta(() =>
            {
                bool ok = ConsultarPapelera(out long b, out long i);
                return (ok, bytes: b, items: i);
            });

            // Si la consulta funcionó y está vacía, no hay nada que hacer.
            if (antes.ok && antes.items == 0)
                return;

            // 2) Intento principal: API oficial de Windows (en hilo STA)
            EjecutarEnSta(() => SHEmptyRecycleBin(IntPtr.Zero, null, FlagsSilencioso));

            // 3) Verificar cuánto quedó
            var despues = EjecutarEnSta(() =>
            {
                bool ok = ConsultarPapelera(out long b, out long i);
                return (ok, bytes: b, items: i);
            });

            // 4) Plan B: si todavía queda algo, borrar directo $Recycle.Bin
            if (despues.ok && despues.items > 0)
            {
                BorrarPapeleraDirecto();

                despues = EjecutarEnSta(() =>
                {
                    bool ok = ConsultarPapelera(out long b, out long i);
                    return (ok, bytes: b, items: i);
                });
            }

            // 5) Calcular lo REALMENTE liberado
            if (antes.ok && despues.ok)
            {
                conteo.Bytes += Math.Max(0, antes.bytes - despues.bytes);
                conteo.Borrados += (int)Math.Min(Math.Max(0, antes.items - despues.items), int.MaxValue);
                if (despues.items > 0)
                    conteo.Omitidos += (int)Math.Min(despues.items, int.MaxValue);
            }
            else if (antes.ok)
            {
                // No se pudo re-consultar: asumimos lo de antes.
                conteo.Bytes += antes.bytes;
                conteo.Borrados += (int)Math.Min(antes.items, int.MaxValue);
            }
        }

        // Borra el contenido de \$Recycle.Bin en cada disco fijo.
        // Sin admin solo se puede borrar la carpeta del usuario actual;
        // con admin se limpian también las de otros usuarios.
        private void BorrarPapeleraDirecto()
        {
            DriveInfo[] unidades;
            try { unidades = DriveInfo.GetDrives(); }
            catch { return; }

            foreach (var unidad in unidades)
            {
                try
                {
                    if (unidad.DriveType != DriveType.Fixed || !unidad.IsReady)
                        continue;

                    string rutaPapelera = Path.Combine(unidad.RootDirectory.FullName, "$Recycle.Bin");
                    if (!Directory.Exists(rutaPapelera))
                        continue;

                    // Cada subcarpeta es el SID de un usuario
                    foreach (var carpetaUsuario in Directory.GetDirectories(rutaPapelera))
                    {
                        try
                        {
                            var temporal = new Conteo();
                            BorrarContenido(carpetaUsuario, temporal);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // =====================================================
        //  CACHÉ DE WINDOWS UPDATE
        //  Los archivos de SoftwareDistribution\Download suelen estar
        //  bloqueados por los servicios de Windows Update. Se detienen,
        //  se limpia y se vuelven a iniciar. Requiere administrador.
        // =====================================================
        private void LimpiarWindowsUpdate(Conteo conteo)
        {
            bool admin = EsAdministrador();
            bool serviciosDetenidos = false;

            if (admin)
            {
                EjecutarNet("stop wuauserv");
                EjecutarNet("stop bits");
                serviciosDetenidos = true;
            }

            try
            {
                BorrarContenido(_rutaCacheWinUpdate, conteo);
            }
            finally
            {
                if (serviciosDetenidos)
                {
                    EjecutarNet("start bits");
                    EjecutarNet("start wuauserv");
                }
            }
        }

        private static bool EjecutarNet(string argumentos)
        {
            try
            {
                var psi = new ProcessStartInfo("net", argumentos)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proceso = Process.Start(psi);
                if (proceso == null) return false;
                return proceso.WaitForExit(30000) && proceso.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public void ReiniciarSesion()
        {
            TotalLiberadoBytes = 0;
        }
    }
}