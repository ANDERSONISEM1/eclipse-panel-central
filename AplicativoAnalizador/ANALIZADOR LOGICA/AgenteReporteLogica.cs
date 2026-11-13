// =========================================================
//  AgenteReporteLogica.cs — Envía el estado de esta PC a la API central
//
//  - Tick() se llama cada ~5 s desde un Timer de la UI: ahí solo se arma un
//    "snapshot" (rápido). El envío HTTP corre en segundo plano: la UI no se congela.
//  - Si la red o el servidor fallan, no se rompe nada: se reintenta con espera
//    creciente (5 s, 10 s ... máximo 30 s) y siempre se manda el dato más nuevo.
// =========================================================
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AplicativoAnalizador.Servicios;
using Microsoft.Win32;

namespace AplicativoAnalizador.Logica
{
    public class AgenteReporteLogica : IDisposable
    {
        static readonly JsonSerializerOptions OpcionesJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        readonly MonitoreoVivoLogica _monitor;
        readonly ProcessService _procesos;
        readonly StartupService _startup;
        readonly HealthScoreService _salud;
        readonly NetworkService _red;
        readonly ConfigAgente _config;
        readonly HttpClient _http;

        string _identificador = "";
        int _enviando;
        int _fallos;
        long _noEnviarHastaTicks;

        // La IP se busca aparte (Verificar() hace una prueba de red que puede tardar)
        volatile string _ip = "Sin IP";
        long _ipVigenteHastaTicks;
        int _ipBuscando;

        public bool Habilitado => _config.EsValida;
        public int IntervaloSegundos => _config.IntervaloSegundos;

        public AgenteReporteLogica(
            MonitoreoVivoLogica monitor,
            ProcessService procesos,
            StartupService startup,
            HealthScoreService salud,
            NetworkService red,
            ConfigAgente config)
        {
            _monitor = monitor;
            _procesos = procesos;
            _startup = startup;
            _salud = salud;
            _red = red;
            _config = config;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public void Iniciar()
        {
            if (!Habilitado)
            {
                Debug.WriteLine("[Agente] Sin configuración (ApiUrl / AgentKey). No se enviarán reportes.");
                return;
            }
            _identificador = ObtenerIdentificadorEquipo();
            RefrescarIpSiHaceFalta();
        }

        // Se llama desde el Timer de la UI (hilo de la interfaz)
        public void Tick()
        {
            if (!Habilitado) return;
            RefrescarIpSiHaceFalta();

            if (DateTime.UtcNow.Ticks < Volatile.Read(ref _noEnviarHastaTicks)) return;
            if (Interlocked.CompareExchange(ref _enviando, 1, 0) != 0) return;   // ya hay un envío en curso

            string json;
            try
            {
                json = ConstruirJson();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Agente] No se pudo armar el reporte: {ex.Message}");
                Interlocked.Exchange(ref _enviando, 0);
                return;
            }

            _ = Task.Run(async () =>
            {
                try { await EnviarAsync(json); }
                finally { Interlocked.Exchange(ref _enviando, 0); }
            });
        }

        string ConstruirJson()
        {
            double cpu = _monitor.Cpu, ram = _monitor.Ram, disco = _monitor.DiscoLibre;
            int habilitados = _startup.ContarHabilitados();
            int total = _startup.Programas.Count;
            int pesados = _procesos.ContarPesados();
            int puntaje = _salud.Calcular(cpu, ram, disco, habilitados, total, pesados).Total;

            var top = _procesos.Procesos
                .Where(p => !p.EsCritico)
                .OrderByDescending(p => p.Cpu)
                .Take(5)
                .Select(p => new { nombre = p.Nombre, cpu = p.Cpu, ramMb = p.RamMb })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                identificador = _identificador,
                hostname = Environment.MachineName,
                ip = _ip,
                cpu = Math.Round(cpu, 1),
                ram = Math.Round(ram, 1),
                discoLibre = Math.Round(disco, 1),
                puntajeSalud = puntaje,
                programasHabilitados = habilitados,
                programasTotal = total,
                procesosPesados = pesados,
                procesos = top,
            }, OpcionesJson);
        }

        async Task EnviarAsync(string json)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _config.ApiUrl + "/api/agente/reporte");
                req.Headers.Add("X-Agent-Key", _config.AgentKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    _fallos = 0;
                    return;
                }

                Debug.WriteLine($"[Agente] El servidor respondió {(int)resp.StatusCode}" +
                                ((int)resp.StatusCode == 401 ? " (AGENT_KEY incorrecta)" : ""));
                RegistrarFallo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Agente] Sin conexión con la API: {ex.Message}");
                RegistrarFallo();
            }
        }

        void RegistrarFallo()
        {
            _fallos++;
            int espera = Math.Min(_fallos * 5, 30);
            Volatile.Write(ref _noEnviarHastaTicks, DateTime.UtcNow.AddSeconds(espera).Ticks);
        }

        void RefrescarIpSiHaceFalta()
        {
            if (DateTime.UtcNow.Ticks < Volatile.Read(ref _ipVigenteHastaTicks)) return;
            if (Interlocked.CompareExchange(ref _ipBuscando, 1, 0) != 0) return;

            _ = Task.Run(() =>
            {
                try { _ip = _red.Verificar().IpLocal; }
                catch (Exception ex) { Debug.WriteLine($"[Agente] No se pudo leer la IP: {ex.Message}"); }
                finally
                {
                    Volatile.Write(ref _ipVigenteHastaTicks, DateTime.UtcNow.AddSeconds(60).Ticks);
                    Interlocked.Exchange(ref _ipBuscando, 0);
                }
            });
        }

        // MachineGuid de Windows: no cambia aunque cambie la IP o el nombre del equipo.
        static string ObtenerIdentificadorEquipo()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var clave = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (clave?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                    return guid;
            }
            catch { }
            return Environment.MachineName;
        }

        public void Dispose() => _http.Dispose();
    }
}