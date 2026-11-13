// =========================================================
//  ConfigAgente.cs — Configuración del agente (NADA hardcodeado)
//  Orden de prioridad:
//    1) Variables de entorno: ECLIPSE_API_URL y ECLIPSE_AGENT_KEY
//    2) Archivo eclipse.config.json junto al .exe
// =========================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AplicativoAnalizador.Logica
{
    public class ConfigAgente
    {
        public string ApiUrl { get; set; } = "";
        public string AgentKey { get; set; } = "";
        public int IntervaloSegundos { get; set; } = 5;

        public bool EsValida =>
            !string.IsNullOrWhiteSpace(ApiUrl) && !string.IsNullOrWhiteSpace(AgentKey);

        public static ConfigAgente Cargar()
        {
            var cfg = new ConfigAgente();

            try
            {
                string ruta = Path.Combine(AppContext.BaseDirectory, "eclipse.config.json");
                if (File.Exists(ruta))
                {
                    var leida = JsonSerializer.Deserialize<ConfigAgente>(File.ReadAllText(ruta),
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                        });
                    if (leida != null) cfg = leida;
                }
                else
                {
                    Debug.WriteLine($"[Config] No existe {ruta}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Config] Error leyendo eclipse.config.json: {ex.Message}");
            }

            string? envUrl = Environment.GetEnvironmentVariable("ECLIPSE_API_URL");
            string? envKey = Environment.GetEnvironmentVariable("ECLIPSE_AGENT_KEY");
            if (!string.IsNullOrWhiteSpace(envUrl)) cfg.ApiUrl = envUrl;
            if (!string.IsNullOrWhiteSpace(envKey)) cfg.AgentKey = envKey;

            cfg.ApiUrl = (cfg.ApiUrl ?? "").Trim().TrimEnd('/');
            cfg.AgentKey = (cfg.AgentKey ?? "").Trim();
            cfg.IntervaloSegundos = Math.Clamp(cfg.IntervaloSegundos, 2, 300);
            return cfg;
        }
    }
}