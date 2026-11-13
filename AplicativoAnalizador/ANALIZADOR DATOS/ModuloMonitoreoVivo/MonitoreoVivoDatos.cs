// =========================================================
    // MonitoreoVivoDatos.cs
    // =========================================================
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AplicativoAnalizador.Datos
{
    /// <summary>
    /// Snapshot crudo de los contadores del sistema, tal como se leyeron
    /// del sistema operativo, SIN ninguna clasificación de negocio
    /// (eso vive en la capa de Lógica: MonitoreoVivoLogica.cs).
    /// </summary>
    public class LecturaSistema
    {
        public double CpuPorcentaje { get; set; }
        public double RamPorcentaje { get; set; }
        public double RamUsadaMb { get; set; }
        public double RamTotalMb { get; set; }
        public double DiscoLibrePorcentaje { get; set; }
        public double DiscoLibreGb { get; set; }
        public double DiscoTotalGb { get; set; }
        public string UnidadDisco { get; set; } = "C:";
    }

    /// <summary>
    /// CAPA DE DATOS del módulo "Monitoreo en vivo".
    /// Se encarga EXCLUSIVAMENTE de leer valores reales del sistema
    /// operativo (CPU, RAM y disco), usando APIs nativas de Windows
    /// (P/Invoke a kernel32.dll) y System.IO.DriveInfo.
    /// No contiene reglas de negocio ni umbrales de "normal/advertencia/
    /// crítico": esa responsabilidad es de la capa de Lógica.
    /// </summary>
    public class MonitoreoVivoDatos
    {
        // ---- GetSystemTimes: permite calcular el % de CPU real del equipo ----
        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out FileTimeNativo idleTime, out FileTimeNativo kernelTime, out FileTimeNativo userTime);

        [StructLayout(LayoutKind.Sequential)]
        struct FileTimeNativo
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public long Valor => ((long)dwHighDateTime << 32) | dwLowDateTime;
        }

        // ---- GlobalMemoryStatusEx: permite leer el uso real de RAM ----
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        // Última lectura de tiempos de CPU, para calcular el delta entre ticks.
        // El % de CPU en Windows SIEMPRE se calcula comparando dos muestras
        // en el tiempo, no con una sola lectura instantánea.
        long _ultimoIdle;
        long _ultimoKernel;
        long _ultimoUser;
        bool _primeraLectura = true;

        readonly string _unidad;

        public MonitoreoVivoDatos(string unidad = "C:\\")
        {
            _unidad = unidad;
        }

        /// <summary>
        /// Lee el estado actual REAL del equipo (CPU, RAM, disco).
        /// Nota: la primera vez que se llama, CpuPorcentaje siempre da 0,
        /// porque necesita una segunda muestra para calcular el delta.
        /// A partir de la segunda llamada ya refleja el uso real.
        /// </summary>
        public LecturaSistema LeerActual()
        {
            double cpu = LeerCpu();
            double ram = LeerRam(out double ramUsadaMb, out double ramTotalMb);
            double discoLibrePct = LeerDiscoLibre(out double libreGb, out double totalGb);

            return new LecturaSistema
            {
                CpuPorcentaje = cpu,
                RamPorcentaje = ram,
                RamUsadaMb = ramUsadaMb,
                RamTotalMb = ramTotalMb,
                DiscoLibrePorcentaje = discoLibrePct,
                DiscoLibreGb = libreGb,
                DiscoTotalGb = totalGb,
                UnidadDisco = _unidad.TrimEnd('\\'),
            };
        }

        double LeerCpu()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return 0;

            long idleActual = idle.Valor;
            long kernelActual = kernel.Valor;
            long userActual = user.Valor;

            if (_primeraLectura)
            {
                _ultimoIdle = idleActual;
                _ultimoKernel = kernelActual;
                _ultimoUser = userActual;
                _primeraLectura = false;
                return 0;
            }

            long diffIdle = idleActual - _ultimoIdle;
            long diffKernel = kernelActual - _ultimoKernel;
            long diffUser = userActual - _ultimoUser;
            long diffTotal = diffKernel + diffUser;

            _ultimoIdle = idleActual;
            _ultimoKernel = kernelActual;
            _ultimoUser = userActual;

            if (diffTotal <= 0) return 0;

            double ocupado = diffTotal - diffIdle;
            double porcentaje = (ocupado / diffTotal) * 100.0;
            return Math.Clamp(porcentaje, 0, 100);
        }

        double LeerRam(out double usadaMb, out double totalMb)
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                usadaMb = 0;
                totalMb = 0;
                return 0;
            }

            totalMb = status.ullTotalPhys / 1024.0 / 1024.0;
            double disponibleMb = status.ullAvailPhys / 1024.0 / 1024.0;
            usadaMb = totalMb - disponibleMb;

            // Windows ya entrega directamente el % de memoria en uso (0-100)
            return status.dwMemoryLoad;
        }

        double LeerDiscoLibre(out double libreGb, out double totalGb)
        {
            try
            {
                var drive = new DriveInfo(_unidad);
                if (!drive.IsReady)
                {
                    libreGb = 0;
                    totalGb = 0;
                    return 0;
                }

                libreGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                totalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
                return totalGb > 0 ? (libreGb / totalGb) * 100.0 : 0;
            }
            catch
            {
                libreGb = 0;
                totalGb = 0;
                return 0;
            }
        }
    }
}