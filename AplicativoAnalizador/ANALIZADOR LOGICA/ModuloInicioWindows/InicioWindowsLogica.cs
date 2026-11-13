// =========================================================
//  InicioWindowsLogica.cs
//  Lee los programas de inicio con las MISMAS fuentes y NOMBRES que la
//  pestaña "Aplicaciones de arranque" del Administrador de tareas:
//    - Run (HKCU / HKLM, 64 y 32 bits)
//    - Carpetas de inicio (usuario y todos los usuarios)
//    - Apps de la Tienda INSTALADAS PARA EL USUARIO: se consultan con
//      Get-AppxPackage (Windows lee los manifiestos por nosotros, ya que
//      la carpeta WindowsApps está bloqueada para aplicaciones normales).
//      El nombre sale del DisplayName de la tarea de inicio (igual que
//      el Administrador de tareas) y el editor del PublisherDisplayName.
//  El estado se lee de donde Windows lo guarda.
//
//  Los métodos Habilitar/Deshabilitar se conservan para no romper
//  StartupService, pero la pantalla ya NO los usa (solo muestra).
// =========================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AplicativoAnalizador.Modelo;
using Microsoft.Win32;

namespace AplicativoAnalizador.Logica
{
    public class InicioWindowsLogica
    {
        // ---------- TIPOS DE ORIGEN ----------
        public const string OrigenHkcu = "HKCU";
        public const string OrigenHklm = "HKLM";
        public const string OrigenHkcu32 = "HKCU32";
        public const string OrigenHklm32 = "HKLM32";
        public const string OrigenCarpetaPersonal = "Carpeta";
        public const string OrigenCarpetaTodosLosUsuarios = "CarpetaTodosLosUsuarios";
        public const string OrigenTienda = "Tienda";

        public static bool RequiereAdmin(string tipoOrigen) =>
            tipoOrigen == OrigenHklm || tipoOrigen == OrigenHklm32 || tipoOrigen == OrigenCarpetaTodosLosUsuarios;

        // ---------- RUTAS DEL REGISTRO ----------
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunPath32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string AprobadoBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string TiendaEstado = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        record Fuente(RegistryHive Hive, string RunRuta, string AprobadoSub, string Tipo);

        static readonly Fuente[] FuentesRun =
        {
            new(RegistryHive.CurrentUser,  RunPath,   "Run",   OrigenHkcu),
            new(RegistryHive.CurrentUser,  RunPath32, "Run32", OrigenHkcu32),
            new(RegistryHive.LocalMachine, RunPath,   "Run",   OrigenHklm),
            new(RegistryHive.LocalMachine, RunPath32, "Run32", OrigenHklm32),
        };

        // ---------- IMPACTO ESTIMADO ----------
        // Windows mide el impacto real durante el arranque; aquí solo se estima.
        static readonly string[] ConocidosAltoImpacto =
        {
            "steam", "onedrive", "teams", "geforce", "nvidia",
            "adobe", "creative cloud", "epicgameslauncher", "docker",
            "msedge", "aplicativoanalizador"
        };

        static readonly string[] PalabrasProtegidas =
        {
            "securityhealth", "windows defender", "msmpeng", "windows security"
        };

        // ---------- CACHÉ DE APPS DE LA TIENDA ----------
        // Consultar Get-AppxPackage tarda unos segundos, así que la lista de
        // tareas (nombres, editores) se guarda 10 minutos. El ESTADO
        // (habilitado/deshabilitado) siempre se lee fresco del registro.
        record TareaTienda(string NombreCompleto, string Familia, string Paquete, string IdTarea,
                           bool HabilitadoManifiesto, string NombreTarea, string NombreApp,
                           string Editor, string Publicador);

        static readonly object _lockCache = new();
        static List<TareaTienda>? _cacheTienda;
        static DateTime _cacheTiendaHasta = DateTime.MinValue;
        static readonly TimeSpan VigenciaCache = TimeSpan.FromMinutes(10);

        // =====================================================
        //  ESCANEAR
        // =====================================================
        public List<StartupItem> Escanear()
        {
            var resultado = new List<StartupItem>();

            try { EscanearRegistro(resultado); }
            catch (Exception ex) { Debug.WriteLine($"[Inicio] Registro: {ex.Message}"); }

            try { EscanearCarpetas(resultado); }
            catch (Exception ex) { Debug.WriteLine($"[Inicio] Carpetas: {ex.Message}"); }

            try { EscanearTienda(resultado); }
            catch (Exception ex) { Debug.WriteLine($"[Inicio] Tienda: {ex.Message}"); }

            return resultado
                .OrderBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.TipoOrigen)
                .ToList();
        }

        // ---------- 1) Claves Run ----------
        void EscanearRegistro(List<StartupItem> resultado)
        {
            foreach (var f in FuentesRun)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(f.Hive, RegistryView.Registry64);
                    using var clave = baseKey.OpenSubKey(f.RunRuta, false);
                    if (clave == null) continue;

                    foreach (var nombreValor in clave.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(nombreValor)) continue;

                        string comando = clave.GetValue(nombreValor)?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(comando)) continue;

                        bool habilitado = LeerHabilitado(f.Hive, AprobadoBase + f.AprobadoSub, nombreValor);
                        string exe = ExtraerRutaEjecutable(comando);

                        string nombre = !string.IsNullOrEmpty(exe) && Path.HasExtension(exe)
                            ? Path.GetFileName(exe)
                            : nombreValor;

                        var (protegido, razon) = DetectarProteccion(nombre + " " + nombreValor + " " + comando);

                        resultado.Add(new StartupItem
                        {
                            Nombre = nombre,
                            NombreRegistro = nombreValor,
                            Editor = ObtenerEditor(exe),
                            Impacto = ClasificarImpacto(habilitado, nombre, exe),
                            Habilitado = habilitado,
                            RutaOrigen = f.RunRuta,
                            TipoOrigen = f.Tipo,
                            Comando = comando,
                            EsProtegido = protegido,
                            RazonProtegido = razon,
                        });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[Inicio] Run {f.Tipo}: {ex.Message}"); }
            }
        }

        // ---------- 2) Carpetas de inicio ----------
        void EscanearCarpetas(List<StartupItem> resultado)
        {
            var carpetas = new[]
            {
                (ruta: Environment.GetFolderPath(Environment.SpecialFolder.Startup),       comun: false),
                (ruta: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), comun: true),
            };

            foreach (var (carpeta, comun) in carpetas)
            {
                if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) continue;

                string tipo = comun ? OrigenCarpetaTodosLosUsuarios : OrigenCarpetaPersonal;
                var hive = comun ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                string rutaAprobado = AprobadoBase + "StartupFolder";

                string[] archivos;
                try { archivos = Directory.GetFiles(carpeta); }
                catch { continue; }

                foreach (var original in archivos)
                {
                    string archivo = original;
                    string archivoNombre = Path.GetFileName(archivo);

                    if (archivoNombre.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    bool esLnk = archivoNombre.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
                    string destino = esLnk ? (ResolverAccesoDirecto(archivo) ?? archivo) : archivo;
                    // El Administrador de tareas muestra el nombre del archivo tal cual.
                    string nombre = archivoNombre;

                    bool habilitado = LeerHabilitado(hive, rutaAprobado, archivoNombre);
                    var (protegido, razon) = DetectarProteccion(nombre + " " + destino);

                    resultado.Add(new StartupItem
                    {
                        Nombre = nombre,
                        NombreRegistro = archivoNombre,
                        Editor = ObtenerEditor(destino),
                        Impacto = ClasificarImpacto(habilitado, nombre, destino),
                        Habilitado = habilitado,
                        RutaOrigen = archivo,
                        TipoOrigen = tipo,
                        Comando = destino,
                        EsProtegido = protegido,
                        RazonProtegido = razon,
                    });
                }
            }
        }

        // ---------- 3) Apps de la Tienda (instaladas para este usuario) ----------
        void EscanearTienda(List<StartupItem> resultado)
        {
            var tareas = ObtenerTareasTienda();
            if (tareas == null)
            {
                EscanearTiendaRespaldo(resultado);   // PowerShell no disponible
                return;
            }

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tareas)
            {
                string clave = t.Familia + "|" + t.IdTarea;
                if (!vistos.Add(clave)) continue;

                // Mismo nombre que el Administrador de tareas: DisplayName de la tarea de inicio.
                string nombre = Primero(
                    ResolverRecurso(t.NombreCompleto, t.Paquete, t.NombreTarea),
                    ResolverRecurso(t.NombreCompleto, t.Paquete, t.NombreApp),
                    NombreDesdeFamilia(t.Familia));

                string editor = Primero(
                    ResolverRecurso(t.NombreCompleto, t.Paquete, t.Editor),
                    EditorDesdePublicador(t.Publicador),
                    "Desconocido");

                int? estadoGuardado = LeerEstadoTienda(t.Familia, t.IdTarea);
                bool habilitado;
                bool porDirectiva = false;
                if (estadoGuardado.HasValue)
                {
                    int e = estadoGuardado.Value;
                    habilitado = e == 2 || e == 4;
                    porDirectiva = e == 3 || e == 4;
                }
                else
                {
                    habilitado = t.HabilitadoManifiesto;
                }

                resultado.Add(new StartupItem
                {
                    Nombre = nombre,
                    NombreRegistro = t.IdTarea,
                    Editor = editor,
                    Impacto = habilitado ? "Bajo" : "Ninguno",
                    Habilitado = habilitado,
                    RutaOrigen = t.Familia,
                    TipoOrigen = OrigenTienda,
                    Comando = t.Familia,
                    EsProtegido = porDirectiva,
                    RazonProtegido = porDirectiva ? "Administrado por una directiva del sistema." : "",
                });
            }
        }

        // Solo si PowerShell falla: usa lo que Windows tiene guardado (nombre técnico).
        void EscanearTiendaRespaldo(List<StartupItem> resultado)
        {
            try
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var raiz = hkcu.OpenSubKey(TiendaEstado, false);
                if (raiz == null) return;

                foreach (var familia in raiz.GetSubKeyNames())
                {
                    using var kFamilia = raiz.OpenSubKey(familia, false);
                    if (kFamilia == null) continue;

                    foreach (var idTarea in kFamilia.GetSubKeyNames())
                    {
                        using var kTarea = kFamilia.OpenSubKey(idTarea, false);
                        if (kTarea?.GetValue("State") is not int e) continue;

                        bool habilitado = e == 2 || e == 4;
                        bool porDirectiva = e == 3 || e == 4;

                        resultado.Add(new StartupItem
                        {
                            Nombre = NombreDesdeFamilia(familia),
                            NombreRegistro = idTarea,
                            Editor = "Desconocido",
                            Impacto = habilitado ? "Bajo" : "Ninguno",
                            Habilitado = habilitado,
                            RutaOrigen = familia,
                            TipoOrigen = OrigenTienda,
                            Comando = familia,
                            EsProtegido = porDirectiva,
                            RazonProtegido = porDirectiva ? "Administrado por una directiva del sistema." : "",
                        });
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[Inicio] Respaldo Tienda: {ex.Message}"); }
        }

        // ---------- Consulta a Windows (Get-AppxPackage) con caché ----------
        static List<TareaTienda>? ObtenerTareasTienda()
        {
            lock (_lockCache)
            {
                if (_cacheTienda != null && DateTime.UtcNow < _cacheTiendaHasta)
                    return _cacheTienda;

                var lista = ConsultarTareasTienda();
                if (lista != null)
                {
                    _cacheTienda = lista;
                    _cacheTiendaHasta = DateTime.UtcNow + VigenciaCache;
                }
                // Si falla pero había caché anterior, se sigue usando.
                return lista ?? _cacheTienda;
            }
        }

        const string ScriptPowerShell = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'SilentlyContinue'
$salida = @()
foreach ($p in Get-AppxPackage) {
    if ($p.IsFramework -or $p.IsResourcePackage) { continue }
    $m = Get-AppxPackageManifest -Package $p
    if ($null -eq $m) { continue }
    $tareas = $m.SelectNodes(""//*[local-name()='StartupTask']"")
    if ($null -eq $tareas -or $tareas.Count -eq 0) { continue }
    $visual = $m.SelectSingleNode(""//*[local-name()='VisualElements']"")
    $appName = ''
    if ($null -ne $visual) { $appName = $visual.GetAttribute('DisplayName') }
    $pub = ''
    if ($null -ne $m.Package.Properties) { $pub = [string]$m.Package.Properties.PublisherDisplayName }
    foreach ($t in $tareas) {
        $salida += [pscustomobject]@{
            Full    = $p.PackageFullName
            Family  = $p.PackageFamilyName
            Name    = $p.Name
            TaskId  = $t.GetAttribute('TaskId')
            Enabled = $t.GetAttribute('Enabled')
            TaskName= $t.GetAttribute('DisplayName')
            AppName = $appName
            Pub     = $pub
            Cn      = $p.Publisher
        }
    }
}
ConvertTo-Json -InputObject @($salida) -Compress
";

        static List<TareaTienda>? ConsultarTareasTienda()
        {
            try
            {
                string codificado = Convert.ToBase64String(Encoding.Unicode.GetBytes(ScriptPowerShell));
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {codificado}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var proceso = Process.Start(psi);
                if (proceso == null) return null;

                // Se lee en paralelo para que el buffer no se llene y bloquee a PowerShell.
                var tareaErr = proceso.StandardError.ReadToEndAsync();
                var tareaOut = proceso.StandardOutput.ReadToEndAsync();

                if (!proceso.WaitForExit(30000))
                {
                    try { proceso.Kill(true); } catch { }
                    Debug.WriteLine("[Inicio] PowerShell tardó demasiado.");
                    return null;
                }

                string json = tareaOut.GetAwaiter().GetResult().Trim();
                if (string.IsNullOrWhiteSpace(json)) return null;

                using var doc = JsonDocument.Parse(json);
                var lista = new List<TareaTienda>();

                IEnumerable<JsonElement> elementos = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().ToList()
                    : new List<JsonElement> { doc.RootElement };

                foreach (var e in elementos)
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;

                    string idTarea = Texto(e, "TaskId");
                    if (string.IsNullOrWhiteSpace(idTarea)) continue;

                    lista.Add(new TareaTienda(
                        NombreCompleto: Texto(e, "Full"),
                        Familia: Texto(e, "Family"),
                        Paquete: Texto(e, "Name"),
                        IdTarea: idTarea,
                        HabilitadoManifiesto: string.Equals(Texto(e, "Enabled"), "true", StringComparison.OrdinalIgnoreCase),
                        NombreTarea: Texto(e, "TaskName"),
                        NombreApp: Texto(e, "AppName"),
                        Editor: Texto(e, "Pub"),
                        Publicador: Texto(e, "Cn")));
                }
                return lista;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Inicio] Get-AppxPackage: {ex.Message}");
                return null;
            }
        }

        static string Texto(JsonElement e, string propiedad) =>
            e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";

        static string Primero(params string?[] opciones) =>
            opciones.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o)) ?? "";

        // "AppUp.IntelGraphicsSoftware_8j3eq9eme6ctt" -> "IntelGraphicsSoftware"
        static string NombreDesdeFamilia(string familia)
        {
            string nombre = familia.Split('_')[0];
            int punto = nombre.LastIndexOf('.');
            return punto >= 0 && punto < nombre.Length - 1 ? nombre[(punto + 1)..] : nombre;
        }

        static string EditorDesdePublicador(string publicador)
        {
            foreach (var parte in (publicador ?? "").Split(','))
            {
                var p = parte.Trim();
                if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    return p[3..].Trim('"');
            }
            return "";
        }

        static int? LeerEstadoTienda(string familia, string idTarea)
        {
            try
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var clave = hkcu.OpenSubKey($@"{TiendaEstado}\{familia}\{idTarea}", false);
                if (clave?.GetValue("State") is int estado) return estado;
            }
            catch { }
            return null;
        }

        // =====================================================
        //  HABILITAR / DESHABILITAR (ya no se usan desde la pantalla)
        // =====================================================
        public ResultadoOperacionInicio Deshabilitar(StartupItem item) => Cambiar(item, false);
        public ResultadoOperacionInicio Habilitar(StartupItem item) => Cambiar(item, true);

        ResultadoOperacionInicio Cambiar(StartupItem item, bool habilitar)
        {
            if (item.EsProtegido)
                return ResultadoOperacionInicio.Fallo(item.RazonProtegido);

            if (item.TipoOrigen == OrigenTienda)
                return CambiarTienda(item, habilitar);

            if (!TryObtenerAprobado(item.TipoOrigen, out var hive, out var ruta))
                return ResultadoOperacionInicio.Fallo("Origen desconocido.");

            if (item.TipoOrigen == OrigenCarpetaPersonal || item.TipoOrigen == OrigenCarpetaTodosLosUsuarios)
            {
                if (!File.Exists(item.RutaOrigen))
                    return ResultadoOperacionInicio.Fallo("El acceso directo ya no existe.");
            }

            return EscribirHabilitado(hive, ruta, item.NombreRegistro, habilitar);
        }

        static bool TryObtenerAprobado(string tipo, out RegistryHive hive, out string ruta)
        {
            switch (tipo)
            {
                case OrigenHkcu: hive = RegistryHive.CurrentUser; ruta = AprobadoBase + "Run"; return true;
                case OrigenHkcu32: hive = RegistryHive.CurrentUser; ruta = AprobadoBase + "Run32"; return true;
                case OrigenHklm: hive = RegistryHive.LocalMachine; ruta = AprobadoBase + "Run"; return true;
                case OrigenHklm32: hive = RegistryHive.LocalMachine; ruta = AprobadoBase + "Run32"; return true;
                case OrigenCarpetaPersonal: hive = RegistryHive.CurrentUser; ruta = AprobadoBase + "StartupFolder"; return true;
                case OrigenCarpetaTodosLosUsuarios: hive = RegistryHive.LocalMachine; ruta = AprobadoBase + "StartupFolder"; return true;
                default: hive = RegistryHive.CurrentUser; ruta = ""; return false;
            }
        }

        // Windows: primer byte par = habilitado, impar = deshabilitado.
        static bool LeerHabilitado(RegistryHive hive, string ruta, string nombreValor)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var clave = baseKey.OpenSubKey(ruta, false);
                if (clave?.GetValue(nombreValor) is byte[] datos && datos.Length > 0)
                    return (datos[0] & 1) == 0;
            }
            catch { }
            return true;
        }

        static ResultadoOperacionInicio EscribirHabilitado(RegistryHive hive, string ruta, string nombreValor, bool habilitar)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var clave = baseKey.CreateSubKey(ruta, true);
                if (clave == null)
                    return ResultadoOperacionInicio.Fallo("No se pudo abrir la configuración de Windows.");

                var valor = new byte[12];
                if (habilitar)
                {
                    valor[0] = 0x02;
                }
                else
                {
                    valor[0] = 0x03;
                    BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(valor, 4);
                }

                clave.SetValue(nombreValor, valor, RegistryValueKind.Binary);

                return LeerHabilitado(hive, ruta, nombreValor) == habilitar
                    ? ResultadoOperacionInicio.Ok()
                    : ResultadoOperacionInicio.Fallo("Windows no aceptó el cambio.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                return ResultadoOperacionInicio.Fallo(
                    hive == RegistryHive.LocalMachine
                        ? "Este programa es del equipo: ejecuta Eclipse como administrador para cambiarlo."
                        : "Sin permisos para modificar la configuración de Windows.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacionInicio.Fallo($"Error: {ex.Message}");
            }
        }

        ResultadoOperacionInicio CambiarTienda(StartupItem item, bool habilitar)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using var clave = baseKey.CreateSubKey($@"{TiendaEstado}\{item.RutaOrigen}\{item.NombreRegistro}", true);
                if (clave == null)
                    return ResultadoOperacionInicio.Fallo("No se pudo abrir la configuración de la aplicación.");

                clave.SetValue("State", habilitar ? 2 : 1, RegistryValueKind.DWord);
                if (habilitar) clave.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);

                return (clave.GetValue("State") is int e && (e == 2) == habilitar)
                    ? ResultadoOperacionInicio.Ok()
                    : ResultadoOperacionInicio.Fallo("Windows no aceptó el cambio.");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
            {
                return ResultadoOperacionInicio.Fallo("Sin permisos para modificar esta aplicación.");
            }
            catch (Exception ex)
            {
                return ResultadoOperacionInicio.Fallo($"Error: {ex.Message}");
            }
        }

        // =====================================================
        //  RESOLVER NOMBRES "ms-resource:..." DE APPS DE LA TIENDA
        // =====================================================
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, BestFitMapping = false)]
        static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        static string CargarIndirecto(string fuente)
        {
            try
            {
                var sb = new StringBuilder(512);
                if (SHLoadIndirectString(fuente, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                    return sb.ToString();
            }
            catch { }
            return "";
        }

        // Convierte "ms-resource:Clave" en el texto real del idioma actual.
        // Prueba las formas en que los paquetes suelen declarar sus recursos.
        static string ResolverRecurso(string nombreCompleto, string paquete, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return "";

            if (!valor.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            {
                if (valor.StartsWith("@")) return CargarIndirecto(valor);
                return valor;   // ya es texto normal
            }

            var candidatos = new List<string> { $"@{{{nombreCompleto}?{valor}}}" };

            string clave = valor["ms-resource:".Length..];
            if (!clave.StartsWith("//"))
            {
                string c = clave.TrimStart('/');
                candidatos.Add($"@{{{nombreCompleto}?ms-resource://{paquete}/{c}}}");
                candidatos.Add($"@{{{nombreCompleto}?ms-resource://{paquete}/resources/{c}}}");
            }

            foreach (var candidato in candidatos)
            {
                string texto = CargarIndirecto(candidato);
                if (!string.IsNullOrWhiteSpace(texto)) return texto;
            }
            return "";
        }

        // =====================================================
        //  AUXILIARES
        // =====================================================
        static string ExtraerRutaEjecutable(string comando)
        {
            if (string.IsNullOrWhiteSpace(comando)) return "";
            comando = Environment.ExpandEnvironmentVariables(comando.Trim());

            if (comando.StartsWith("\""))
            {
                int fin = comando.IndexOf('"', 1);
                return fin > 0 ? comando.Substring(1, fin - 1) : comando.Trim('"');
            }

            int i = comando.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return comando.Substring(0, i + 4);

            return comando.Split(' ')[0];
        }

        static string? ResolverAccesoDirecto(string rutaLnk)
        {
            try
            {
                Type? tipoShell = Type.GetTypeFromProgID("WScript.Shell");
                if (tipoShell == null) return null;
                dynamic? shell = Activator.CreateInstance(tipoShell);
                dynamic acceso = shell!.CreateShortcut(rutaLnk);
                string destino = (string)acceso.TargetPath;
                return string.IsNullOrWhiteSpace(destino) ? null : destino;
            }
            catch { return null; }
        }

        static string ObtenerEditor(string rutaEjecutable)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rutaEjecutable) || !File.Exists(rutaEjecutable))
                    return "Desconocido";
                var info = FileVersionInfo.GetVersionInfo(rutaEjecutable);
                return !string.IsNullOrWhiteSpace(info.CompanyName) ? info.CompanyName! : "Desconocido";
            }
            catch { return "Desconocido"; }
        }

        static string ClasificarImpacto(bool habilitado, string nombre, string rutaEjecutable)
        {
            if (!habilitado) return "Ninguno";

            string baja = nombre.ToLowerInvariant();
            if (ConocidosAltoImpacto.Any(k => baja.Contains(k)))
                return "Alto";

            try
            {
                if (!string.IsNullOrEmpty(rutaEjecutable) && File.Exists(rutaEjecutable))
                {
                    double mb = new FileInfo(rutaEjecutable).Length / 1024.0 / 1024.0;
                    if (mb > 15) return "Medio";
                }
            }
            catch { }
            return "Bajo";
        }

        static (bool protegido, string razon) DetectarProteccion(string texto)
        {
            string baja = texto.ToLowerInvariant();
            if (PalabrasProtegidas.Any(p => baja.Contains(p)))
                return (true, "Componente de seguridad de Windows.");
            return (false, "");
        }
    }
}