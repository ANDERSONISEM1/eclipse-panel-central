// =========================================================
//  Servicio.cs — COMPLETO, SIN ERRORES, CON DiskAnalyzerService
//  CORREGIDO: RAM ahora usa PrivateMemorySize64 (coincide con Admin Tareas)
//  ✅ LIMPIO: Eliminado método BajarPrioridad
// =========================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AplicativoAnalizador.Modelo;
using AplicativoAnalizador.Logica;
namespace AplicativoAnalizador.Servicios
{
    public class ProcessService
    {
        readonly List<ProcesoInfo> _procesos = new();
        static readonly HashSet<string> ProcesosCriticos = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "fontdrvhost", "spoolsv",
            "MemCompression", "WUDFHost", "RuntimeBroker", "SearchIndexer",
            "audiodg", "conhost"
        };
        public IReadOnlyList<ProcesoInfo> Procesos => _procesos;
        public async Task<IReadOnlyList<ProcesoInfo>> RefrescarAsync(int intervaloMs = 600)
        {
            var procesos = Process.GetProcesses();
            var muestraInicial = new Dictionary<int, TimeSpan>();
            foreach (var p in procesos)
            {
                try { muestraInicial[p.Id] = p.TotalProcessorTime; }
                catch { }
            }
            await Task.Delay(intervaloMs);
            int nucleos = Math.Max(1, Environment.ProcessorCount);
            var resultado = new List<ProcesoInfo>();
            foreach (var grupo in procesos.GroupBy(p => p.ProcessName))
            {
                double cpuTotal = 0;
                long ramTotal = 0;
                bool tieneVentana = false;
                bool esSesionSistema = false;
                string prioridad = "Normal";
                foreach (var p in grupo)
                {
                    try
                    {
                        if (muestraInicial.TryGetValue(p.Id, out var t0))
                        {
                            var deltaMs = (p.TotalProcessorTime - t0).TotalMilliseconds;
                            cpuTotal += deltaMs / (intervaloMs * nucleos) * 100.0;
                        }
                        // ✅ CAMBIO APLICADO: Coincide con Administrador de Tareas
                        ramTotal += p.PrivateMemorySize64;
                        if (!string.IsNullOrWhiteSpace(p.MainWindowTitle))
                            tieneVentana = true;
                        if (p.SessionId == 0)
                            esSesionSistema = true;
                        try { prioridad = MapPrioridad(p.PriorityClass); } catch { }
                    }
                    catch { }
                }
                bool esCritico = ProcesosCriticos.Contains(grupo.Key) || esSesionSistema;
                resultado.Add(new ProcesoInfo
                {
                    Nombre = grupo.Key,
                    Cpu = Math.Round(Math.Clamp(cpuTotal, 0, 100), 1),
                    RamMb = (int)(ramTotal / 1024 / 1024),
                    Prioridad = prioridad,
                    EsCritico = esCritico,
                    EsAppUsuario = tieneVentana && !esCritico,
                });
                foreach (var p in grupo) p.Dispose();
            }
            _procesos.Clear();
            _procesos.AddRange(resultado.OrderByDescending(p => p.Cpu));
            return _procesos;
        }
        static string MapPrioridad(ProcessPriorityClass clase) => clase switch
        {
            ProcessPriorityClass.RealTime or ProcessPriorityClass.High => "Alta",
            ProcessPriorityClass.AboveNormal => "Media-Alta",
            ProcessPriorityClass.Normal => "Normal",
            ProcessPriorityClass.BelowNormal => "Media-Baja",
            ProcessPriorityClass.Idle => "Baja",
            _ => "Normal"
        };
        public bool Finalizar(string nombreProceso)
        {
            var instancias = Process.GetProcessesByName(nombreProceso);
            if (instancias.Length == 0) return false;
            bool exito = true;
            foreach (var p in instancias)
            {
                try
                {
                    if (p.SessionId == 0) { exito = false; continue; }
                    p.Kill();
                    p.WaitForExit(2000);
                }
                catch { exito = false; }
                finally { p.Dispose(); }
            }
            return exito;
        }
        public int ContarPesados(double umbralCpu = 25.0) => _procesos.Count(p => p.Cpu >= umbralCpu);
    }

    public class StartupService
    {
        readonly InicioWindowsLogica _logica = new();
        List<StartupItem> _programas = new();
        public IReadOnlyList<StartupItem> Programas => _programas;
        public IReadOnlyList<StartupItem> Generar()
        {
            _programas = _logica.Escanear();
            return _programas;
        }
        public ResultadoOperacionInicio AlternarEstado(StartupItem item)
        {
            var resultado = item.Habilitado ? _logica.Deshabilitar(item) : _logica.Habilitar(item);
            if (resultado.Exito) item.Habilitado = !item.Habilitado;
            return resultado;
        }
        public int ContarHabilitados() => _programas.Count(p => p.Habilitado);
    }

       public class HealthScoreService
    {
        // ---------- REGLAS ÚNICAS (las usan el puntaje Y la pantalla) ----------
        public static EstadoMetrica EstadoInicio(int programasHabilitados) =>
            programasHabilitados >= 5 ? EstadoMetrica.Critico
            : programasHabilitados >= 3 ? EstadoMetrica.Advertencia
            : EstadoMetrica.Normal;

        public static EstadoMetrica EstadoProcesos(int procesosPesados) =>
            procesosPesados >= 3 ? EstadoMetrica.Critico
            : procesosPesados >= 1 ? EstadoMetrica.Advertencia
            : EstadoMetrica.Normal;

        public PuntajeSalud Calcular(double cpu, double ram, double discoLibre, int programasHabilitados, int totalProgramas, int procesosPesados)
        {
            int puntoCpu = cpu >= 95 ? 0 : cpu >= 85 ? 12 : 25;
            int puntoRam = ram >= 92 ? 0 : ram >= 80 ? 12 : 25;
            int puntoDisco = discoLibre < 10 ? 0 : discoLibre < 20 ? 10 : 20;

            int puntoInicio = EstadoInicio(programasHabilitados) switch
            {
                EstadoMetrica.Critico => 5,
                EstadoMetrica.Advertencia => 12,
                _ => 20,
            };

            int puntoProcesos = EstadoProcesos(procesosPesados) switch
            {
                EstadoMetrica.Critico => 2,
                EstadoMetrica.Advertencia => 6,
                _ => 10,
            };

            int total = Math.Clamp(puntoCpu + puntoRam + puntoDisco + puntoInicio + puntoProcesos, 0, 100);
            return new PuntajeSalud
            {
                Total = total,
                Desglose = new List<ComponenteSalud>
                {
                    new() { Nombre = "CPU", Puntos = puntoCpu, Maximo = 25 },
                    new() { Nombre = "RAM", Puntos = puntoRam, Maximo = 25 },
                    new() { Nombre = "Disco", Puntos = puntoDisco, Maximo = 20 },
                    new() { Nombre = "Programas de inicio", Puntos = puntoInicio, Maximo = 20 },
                    new() { Nombre = "Procesos", Puntos = puntoProcesos, Maximo = 10 },
                }
            };
        }
    }
    public class ReportService
    {
        readonly MonitoreoVivoLogica _monitor;
        public ReportService(MonitoreoVivoLogica monitor) => _monitor = monitor;
        public ReporteSistema Construir(
            string estadoGlobal,
            double totalLiberado,
            IReadOnlyList<ProcesoInfo> procesos,
            IReadOnlyList<StartupItem> programasInicio,
            IReadOnlyList<CarpetaPesada> carpetasPesadas,
            PuntajeSalud puntajeSalud)
        {
            return new ReporteSistema
            {
                Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Sistema = new ReporteEstadoSistema
                {
                    EstadoGlobal = estadoGlobal.Replace("● ", ""),
                    Cpu = new MetricaReporte { Valor = Math.Round(_monitor.Cpu, 1), Estado = _monitor.Clasificar("cpu", _monitor.Cpu).TextoEstado },
                    Ram = new MetricaReporte { Valor = Math.Round(_monitor.Ram, 1), Estado = _monitor.Clasificar("ram", _monitor.Ram).TextoEstado },
                    Disco = new MetricaReporte { Valor = Math.Round(_monitor.DiscoLibre, 1), Estado = _monitor.Clasificar("discoLibre", _monitor.DiscoLibre).TextoEstado },
                },
                Mantenimiento = new ReporteMantenimiento
                {
                    EspacioLiberadoTotal = Math.Round(totalLiberado, 2),
                    UltimoMantenimiento = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                },
                Procesos = procesos.OrderByDescending(p => p.Cpu).ToList(),
                ProgramasInicio = programasInicio.ToList(),
                CarpetasPesadas = carpetasPesadas.OrderByDescending(c => c.TamañoGb).ToList(),
                PuntajeSalud = puntajeSalud,
            };
        }
        public void GuardarComoJson(ReporteSistema reporte, string ruta)
        {
            var json = JsonSerializer.Serialize(reporte, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ruta, json);
        }
    }

    public class DiskAnalyzerService
    {
        readonly AnalizadorDiscoLogica _logica = new();
        public IReadOnlyList<CarpetaPesada> CarpetasPesadas => _logica.Resultados;
        public async Task<IReadOnlyList<CarpetaPesada>> EscanearAsync(IProgress<int>? progreso = null)
        {
            return await _logica.EscanearAsync(progreso);
        }
    }

    public class NetworkService
    {
        readonly RedLogica _logica = new();
        public EstadoRed Verificar()
        {
            return _logica.Verificar();
        }
    }
}