// =========================================================
//  AnalizadorDiscoLogica.cs — COMPATIBLE CON TU VERSIÓN ✅
// =========================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AplicativoAnalizador.Modelo;

namespace AplicativoAnalizador.Logica
{
    public class AnalizadorDiscoLogica
    {
        private readonly List<CarpetaPesada> _resultados = new();
        public IReadOnlyList<CarpetaPesada> Resultados => _resultados;

        public async Task<IReadOnlyList<CarpetaPesada>> EscanearAsync(IProgress<int>? progreso = null)
        {
            _resultados.Clear();
            var carpetasBase = ObtenerCarpetasUsuario();
            int total = carpetasBase.Count;
            int actual = 0;

            foreach (var carpeta in carpetasBase)
            {
                actual++;
                progreso?.Report(actual * 100 / Math.Max(total, 1));

                try
                {
                    double gb = await CalcularTamañoCarpetaAsync(carpeta);

                    if (gb >= 0.01) // 10 MB mínimo
                    {
                        _resultados.Add(new CarpetaPesada
                        {
                            Ruta = carpeta,
                            TamañoGb = Math.Round(gb, 1)
                        });
                    }
                }
                catch { }

                await Task.Delay(50);
            }

            return _resultados.OrderByDescending(c => c.TamañoGb).ToList();
        }

        private List<string> ObtenerCarpetasUsuario()
        {
            string usuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // ✅ Rutas COMPATIBLES con TODAS las versiones de .NET
            var rutas = new List<string>
            {
                Path.Combine(usuario, "Descargas"),      // Windows en español
                Path.Combine(usuario, "Downloads"),      // Windows en inglés
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
            };

            // ✅ Solo devolvemos las que realmente existen
            return rutas.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct().ToList();
        }

        private async Task<double> CalcularTamañoCarpetaAsync(string ruta)
        {
            return await Task.Run(() =>
            {
                try
                {
                    long bytes = CalcularTamañoRecursivo(ruta);
                    return bytes / (1024.0 * 1024 * 1024);
                }
                catch
                {
                    return 0;
                }
            });
        }

        private long CalcularTamañoRecursivo(string ruta)
        {
            long bytes = 0;
            try
            {
                foreach (var archivo in Directory.GetFiles(ruta, "*.*", SearchOption.TopDirectoryOnly))
                {
                    try { bytes += new FileInfo(archivo).Length; }
                    catch { }
                }
                foreach (var subdir in Directory.GetDirectories(ruta))
                {
                    try { bytes += CalcularTamañoRecursivo(subdir); }
                    catch { }
                }
            }
            catch { }
            return bytes;
        }
    }
}