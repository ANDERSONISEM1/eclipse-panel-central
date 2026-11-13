// =========================================================
//  ECLIPSE API central — ASP.NET Core minimal API (.NET 8)
//  Config SOLO por variables de entorno:
//    DB_CONNECTION  (obligatoria)  cadena de conexión a PostgreSQL
//    AGENT_KEY      (obligatoria)  clave compartida con los agentes
//    ADMIN_PASSWORD (opcional)     crea el usuario "admin" si no existe
// =========================================================
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

var cadenaBd = Environment.GetEnvironmentVariable("DB_CONNECTION");
var agentKey = Environment.GetEnvironmentVariable("AGENT_KEY");
var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

if (string.IsNullOrWhiteSpace(cadenaBd) || string.IsNullOrWhiteSpace(agentKey))
{
    Console.Error.WriteLine("ERROR: faltan las variables de entorno DB_CONNECTION y/o AGENT_KEY.");
    return 1;
}

const int MaxIntentos = 5;
const int MinutosBloqueo = 5;
const int HorasSesion = 8;
const int ConfirmacionesCambio = 3;   // reportes seguidos para confirmar un cambio de estado

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddSingleton(NpgsqlDataSource.Create(cadenaBd));

var app = builder.Build();
var db = app.Services.GetRequiredService<NpgsqlDataSource>();
var claveAgenteHash = SHA256.HashData(Encoding.UTF8.GetBytes(agentKey));

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Error interno del servidor." });
}));

// ---------------------------------------------------------
//  Usuario admin (solo si ADMIN_PASSWORD existe y el usuario no existe)
// ---------------------------------------------------------
if (!string.IsNullOrEmpty(adminPassword))
{
    var (hashAdmin, saltAdmin) = Seguridad.CrearHash(adminPassword);
    for (int intento = 1; intento <= 30; intento++)
    {
        try
        {
            await using var cmd = db.CreateCommand(@"
                INSERT INTO usuarios (usuario, nombre_completo, password_hash, password_salt, es_admin)
                VALUES ('admin', 'Administrador', @h, @s, true)
                ON CONFLICT (usuario) DO NOTHING;");
            cmd.Parameters.AddWithValue("h", hashAdmin);
            cmd.Parameters.AddWithValue("s", saltAdmin);
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine("[Inicio] Usuario admin verificado.");
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Inicio] BD no lista (intento {intento}/30): {ex.Message}");
            await Task.Delay(2000);
        }
    }
}

// ---------------------------------------------------------
//  Funciones auxiliares
// ---------------------------------------------------------
static IResult NoAutorizado() =>
    Results.Json(new { error = "Sesión no válida o expirada." }, statusCode: 401);

static string? ObtenerToken(HttpRequest req)
{
    string cabecera = req.Headers.Authorization.ToString();
    if (cabecera.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var t = cabecera[7..].Trim();
        return t.Length > 0 ? t : null;
    }
    return null;
}

static double Limpiar(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 100) : 0;
static decimal Dec(double v) => (decimal)Math.Round(v, 2);
static string Recortar(string? s, int max)
{
    s = (s ?? "").Trim();
    return s.Length > max ? s[..max] : s;
}

// Mismos umbrales de siempre; el peor de los tres manda.
static string Clasificar(double cpu, double ram, double discoLibre)
{
    int c = cpu >= 95 ? 2 : cpu >= 85 ? 1 : 0;
    int r = ram >= 92 ? 2 : ram >= 80 ? 1 : 0;
    int d = discoLibre < 10 ? 2 : discoLibre < 20 ? 1 : 0;
    int peor = Math.Max(c, Math.Max(r, d));
    return peor == 2 ? "critico" : peor == 1 ? "advertencia" : "normal";
}

// ---------------------------------------------------------
//  PÚBLICOS
// ---------------------------------------------------------
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.MapPost("/api/auth/login", async (LoginReq req) =>
{
    const string msgMal = "Usuario o contraseña incorrectos.";
    var usuario = (req.Usuario ?? "").Trim();
    var password = req.Password ?? "";

    if (usuario.Length == 0 || usuario.Length > 50 || password.Length > 200)
        return Results.Json(new { error = msgMal }, statusCode: 401);

    bool existe = false, activo = false;
    int id = 0;
    string hash = "", salt = "";
    DateTime? bloqueadoHasta = null;

    await using (var cmd = db.CreateCommand(@"
        SELECT id, password_hash, password_salt, activo, bloqueado_hasta
        FROM usuarios WHERE usuario = @u;"))
    {
        cmd.Parameters.AddWithValue("u", usuario);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            existe = true;
            id = r.GetInt32(0);
            hash = r.GetString(1).Trim();
            salt = r.GetString(2).Trim();
            activo = r.GetBoolean(3);
            bloqueadoHasta = r.IsDBNull(4) ? null : r.GetDateTime(4);
        }
    }

    if (!existe || !activo)
    {
        Seguridad.CrearHash("relleno");   // gasta el mismo tiempo para no delatar si el usuario existe
        return Results.Json(new { error = msgMal }, statusCode: 401);
    }

    if (bloqueadoHasta.HasValue && bloqueadoHasta.Value.ToUniversalTime() > DateTime.UtcNow)
        return Results.Json(new { error = $"Cuenta bloqueada por intentos fallidos. Intenta de nuevo en {MinutosBloqueo} minutos." }, statusCode: 401);

    if (!Seguridad.Verificar(password, hash, salt))
    {
        await using var fallo = db.CreateCommand(@"
            UPDATE usuarios
            SET intentos_fallidos = intentos_fallidos + 1,
                bloqueado_hasta = CASE WHEN intentos_fallidos + 1 >= @max
                                       THEN now() + make_interval(mins => @min)
                                       ELSE bloqueado_hasta END
            WHERE id = @id;");
        fallo.Parameters.AddWithValue("max", MaxIntentos);
        fallo.Parameters.AddWithValue("min", MinutosBloqueo);
        fallo.Parameters.AddWithValue("id", id);
        await fallo.ExecuteNonQueryAsync();
        return Results.Json(new { error = msgMal }, statusCode: 401);
    }

    await using (var reset = db.CreateCommand("UPDATE usuarios SET intentos_fallidos = 0, bloqueado_hasta = NULL WHERE id = @id;"))
    {
        reset.Parameters.AddWithValue("id", id);
        await reset.ExecuteNonQueryAsync();
    }
    await using (var limpiar = db.CreateCommand("DELETE FROM sesiones WHERE expira_en < now();"))
    {
        await limpiar.ExecuteNonQueryAsync();
    }

    string token = Seguridad.GenerarToken();
    await using (var ses = db.CreateCommand(@"
        INSERT INTO sesiones (usuario_id, token_hash, expira_en)
        VALUES (@u, @h, now() + make_interval(hours => @horas));"))
    {
        ses.Parameters.AddWithValue("u", id);
        ses.Parameters.AddWithValue("h", Seguridad.HashToken(token));
        ses.Parameters.AddWithValue("horas", HorasSesion);
        await ses.ExecuteNonQueryAsync();
    }

    return Results.Ok(new { token, usuario });
});

app.MapPost("/api/agente/reporte", async (HttpRequest http, ReporteReq? body) =>
{
    // --- Clave del agente (comparación en tiempo constante) ---
    var clave = http.Headers["X-Agent-Key"].ToString();
    var claveHash = SHA256.HashData(Encoding.UTF8.GetBytes(clave));
    if (!CryptographicOperations.FixedTimeEquals(claveHash, claveAgenteHash))
        return Results.Json(new { error = "Clave de agente no válida." }, statusCode: 401);

    if (body == null || string.IsNullOrWhiteSpace(body.Identificador))
        return Results.Json(new { error = "Solicitud inválida: falta el identificador." }, statusCode: 400);

    string identificador = Recortar(body.Identificador, 64);
    string hostname = Recortar(string.IsNullOrWhiteSpace(body.Hostname) ? identificador : body.Hostname, 100);
    string ip = Recortar(body.Ip, 45);

    double cpu = Limpiar(body.Cpu), ram = Limpiar(body.Ram), disco = Limpiar(body.DiscoLibre);
    int puntaje = Math.Clamp(body.PuntajeSalud, 0, 100);
    int progHab = Math.Max(0, body.ProgramasHabilitados);
    int progTotal = Math.Max(0, body.ProgramasTotal);
    int pesados = Math.Max(0, body.ProcesosPesados);

    var procesos = (body.Procesos ?? new List<ProcesoReq>())
        .Take(5)
        .Select(p => new
        {
            nombre = Recortar(p.Nombre, 100),
            cpu = Math.Round(Limpiar(p.Cpu), 1),
            ramMb = Math.Max(0, p.RamMb),
        })
        .ToList();
    string procesosJson = JsonSerializer.Serialize(procesos);

    string estadoNuevo = Clasificar(cpu, ram, disco);

    await using var con = await db.OpenConnectionAsync();
    await using var tx = await con.BeginTransactionAsync();

    NpgsqlCommand Cmd(string sql) => new NpgsqlCommand(sql, con, tx);

    // 1) Upsert del equipo
    int equipoId;
    await using (var c = Cmd(@"
        INSERT INTO equipos (identificador, hostname, ip, ultimo_reporte)
        VALUES (@i, @h, @ip, now())
        ON CONFLICT (identificador) DO UPDATE
            SET hostname = EXCLUDED.hostname, ip = EXCLUDED.ip, ultimo_reporte = now()
        RETURNING id;"))
    {
        c.Parameters.AddWithValue("i", identificador);
        c.Parameters.AddWithValue("h", hostname);
        c.Parameters.AddWithValue("ip", ip);
        equipoId = Convert.ToInt32(await c.ExecuteScalarAsync());
    }

    // 2) Asegurar la fila de estado_vivo (primer reporte: arranca con el estado actual, sin alerta)
    await using (var c = Cmd(@"
        INSERT INTO estado_vivo (equipo_id, cpu, ram, disco_libre, estado, candidato, candidato_conteo)
        VALUES (@id, @cpu, @ram, @disco, @estado, @estado, 0)
        ON CONFLICT (equipo_id) DO NOTHING;"))
    {
        c.Parameters.AddWithValue("id", equipoId);
        c.Parameters.AddWithValue("cpu", Dec(cpu));
        c.Parameters.AddWithValue("ram", Dec(ram));
        c.Parameters.AddWithValue("disco", Dec(disco));
        c.Parameters.AddWithValue("estado", estadoNuevo);
        await c.ExecuteNonQueryAsync();
    }

    // 3) Bloquear la fila y aplicar el antirruido (3 reportes seguidos)
    string estadoActual, candidato;
    int conteo;
    await using (var c = Cmd("SELECT estado, candidato, candidato_conteo FROM estado_vivo WHERE equipo_id = @id FOR UPDATE;"))
    {
        c.Parameters.AddWithValue("id", equipoId);
        await using var r = await c.ExecuteReaderAsync();
        await r.ReadAsync();
        estadoActual = r.GetString(0);
        candidato = r.GetString(1);
        conteo = r.GetInt32(2);
    }

    string? estadoAnterior = null;
    if (estadoNuevo == estadoActual)
    {
        candidato = estadoActual;
        conteo = 0;
    }
    else
    {
        if (estadoNuevo == candidato) conteo++;
        else { candidato = estadoNuevo; conteo = 1; }

        if (conteo >= ConfirmacionesCambio)
        {
            estadoAnterior = estadoActual;
            estadoActual = estadoNuevo;
            candidato = estadoNuevo;
            conteo = 0;
        }
    }

    // 4) Actualizar estado_vivo
    await using (var c = Cmd(@"
        UPDATE estado_vivo SET
            cpu = @cpu, ram = @ram, disco_libre = @disco,
            estado = @estado, candidato = @cand, candidato_conteo = @conteo,
            puntaje = @puntaje, prog_hab = @ph, prog_total = @pt, pesados = @pes,
            procesos = @procesos, actualizado_en = now()
        WHERE equipo_id = @id;"))
    {
        c.Parameters.AddWithValue("id", equipoId);
        c.Parameters.AddWithValue("cpu", Dec(cpu));
        c.Parameters.AddWithValue("ram", Dec(ram));
        c.Parameters.AddWithValue("disco", Dec(disco));
        c.Parameters.AddWithValue("estado", estadoActual);
        c.Parameters.AddWithValue("cand", candidato);
        c.Parameters.AddWithValue("conteo", conteo);
        c.Parameters.AddWithValue("puntaje", puntaje);
        c.Parameters.AddWithValue("ph", progHab);
        c.Parameters.AddWithValue("pt", progTotal);
        c.Parameters.AddWithValue("pes", pesados);
        c.Parameters.Add(new NpgsqlParameter("procesos", NpgsqlDbType.Jsonb) { Value = procesosJson });
        await c.ExecuteNonQueryAsync();
    }

    async Task InsertarRegistro(string origen)
    {
        await using var c = Cmd(@"
            INSERT INTO registros_estado
                (equipo_id, cpu, cpu_max, ram, disco_libre, estado, puntaje_salud,
                 programas_inicio_habilitados, programas_inicio_total, procesos_pesados, origen)
            VALUES (@id, @cpu, @cpu, @ram, @disco, @estado, @puntaje, @ph, @pt, @pes, @origen);");
        c.Parameters.AddWithValue("id", equipoId);
        c.Parameters.AddWithValue("cpu", Dec(cpu));
        c.Parameters.AddWithValue("ram", Dec(ram));
        c.Parameters.AddWithValue("disco", Dec(disco));
        c.Parameters.AddWithValue("estado", estadoActual);
        c.Parameters.AddWithValue("puntaje", puntaje);
        c.Parameters.AddWithValue("ph", progHab);
        c.Parameters.AddWithValue("pt", progTotal);
        c.Parameters.AddWithValue("pes", pesados);
        c.Parameters.AddWithValue("origen", origen);
        await c.ExecuteNonQueryAsync();
    }

    // 5) Cambio confirmado: alerta + registro inmediato
    if (estadoAnterior != null)
    {
        await using (var c = Cmd(@"
            INSERT INTO alertas (equipo_id, estado_anterior, estado_nuevo, detalle)
            VALUES (@id, @ant, @nuevo, @detalle);"))
        {
            c.Parameters.AddWithValue("id", equipoId);
            c.Parameters.AddWithValue("ant", estadoAnterior);
            c.Parameters.AddWithValue("nuevo", estadoActual);
            c.Parameters.AddWithValue("detalle", Recortar($"CPU {cpu:0}% · RAM {ram:0}% · Disco libre {disco:0}%", 200));
            await c.ExecuteNonQueryAsync();
        }
        await InsertarRegistro("cambio_estado");
    }

    // 6) Resumen periódico si no hay registro en los últimos 30 min
    bool hayReciente;
    await using (var c = Cmd(@"
        SELECT EXISTS (SELECT 1 FROM registros_estado
                       WHERE equipo_id = @id AND registrado_en > now() - interval '30 minutes');"))
    {
        c.Parameters.AddWithValue("id", equipoId);
        hayReciente = (bool)(await c.ExecuteScalarAsync())!;
    }
    if (!hayReciente) await InsertarRegistro("periodico");

    await tx.CommitAsync();
    return Results.Ok(new { ok = true });
});

// ---------------------------------------------------------
//  PRIVADOS (Authorization: Bearer <token>)
// ---------------------------------------------------------
var privado = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
{
    var http = ctx.HttpContext;
    var token = ObtenerToken(http.Request);
    if (token == null) return NoAutorizado();

    var hash = Seguridad.HashToken(token);
    var usuario = await Sesiones.ValidarAsync(db, hash);
    if (usuario == null) return NoAutorizado();

    http.Items["tokenHash"] = hash;
    return await next(ctx);
});

privado.MapPost("/auth/logout", async (HttpContext http) =>
{
    if (http.Items["tokenHash"] is string hash)
    {
        Sesiones.Quitar(hash);
        await using var cmd = db.CreateCommand("DELETE FROM sesiones WHERE token_hash = @h;");
        cmd.Parameters.AddWithValue("h", hash);
        await cmd.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { ok = true });
});

privado.MapGet("/equipos", async () =>
{
    var lista = new List<object>();
    await using var cmd = db.CreateCommand(@"
        SELECT e.id, e.hostname, e.ip, v.cpu, v.ram, v.disco_libre, v.estado, e.ultimo_reporte
        FROM estado_vivo v
        JOIN equipos e ON e.id = v.equipo_id
        WHERE e.activo
        ORDER BY e.hostname;");
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        lista.Add(new
        {
            id = r.GetInt32(0),
            hostname = r.GetString(1),
            ip = r.IsDBNull(2) ? "" : r.GetString(2),
            cpu = (double)r.GetDecimal(3),
            ram = (double)r.GetDecimal(4),
            discoLibre = (double)r.GetDecimal(5),
            estado = r.GetString(6),
            ultimoReporte = r.GetDateTime(7),
        });
    }
    return Results.Ok(lista);
});

privado.MapGet("/equipos/{id:int}/detalle", async (int id) =>
{
    await using var cmd = db.CreateCommand(@"
        SELECT puntaje, prog_hab, prog_total, procesos::text
        FROM estado_vivo WHERE equipo_id = @id;");
    cmd.Parameters.AddWithValue("id", id);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync())
        return Results.Json(new { error = "Equipo no encontrado" }, statusCode: 404);

    int puntaje = r.GetInt32(0);
    var procesos = JsonSerializer.Deserialize<JsonElement>(r.GetString(3));

    return Results.Ok(new
    {
        puntajeSalud = new
        {
            total = puntaje,
            estado = puntaje >= 80 ? "Saludable" : puntaje >= 50 ? "Regular" : "Crítico",
        },
        procesosDestacados = procesos,
        programasInicio = new { habilitados = r.GetInt32(1), total = r.GetInt32(2) },
    });
});

privado.MapGet("/equipos/{id:int}/alertas", async (int id, int? limite) =>
{
    var lista = new List<object>();
    await using var cmd = db.CreateCommand(@"
        SELECT creada_en, estado_anterior, estado_nuevo, detalle
        FROM alertas WHERE equipo_id = @id
        ORDER BY creada_en DESC LIMIT @l;");
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("l", Math.Clamp(limite ?? 8, 1, 100));
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        lista.Add(new
        {
            fecha = r.GetDateTime(0),
            estadoAnterior = r.IsDBNull(1) ? null : r.GetString(1),
            estadoNuevo = r.GetString(2),
            detalle = r.IsDBNull(3) ? "" : r.GetString(3),
        });
    }
    return Results.Ok(lista);
});

app.Run();
return 0;

// =========================================================
//  Modelos
// =========================================================
record LoginReq(string? Usuario, string? Password);
record ProcesoReq(string? Nombre, double Cpu, int RamMb);
record ReporteReq(
    string? Identificador, string? Hostname, string? Ip,
    double Cpu, double Ram, double DiscoLibre, int PuntajeSalud,
    int ProgramasHabilitados, int ProgramasTotal, int ProcesosPesados,
    List<ProcesoReq>? Procesos);

// =========================================================
//  Seguridad: PBKDF2-SHA256 (600,000 vueltas) + tokens
//  Compatible con los hashes que ya tienes en la BD.
// =========================================================
static class Seguridad
{
    const int Iteraciones = 600_000;

    public static (string hash, string salt) CrearHash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iteraciones, HashAlgorithmName.SHA256, 32);
        return (Convert.ToHexString(hash).ToLowerInvariant(), Convert.ToHexString(salt).ToLowerInvariant());
    }

    public static bool Verificar(string password, string hashHex, string saltHex)
    {
        try
        {
            byte[] salt = Convert.FromHexString(saltHex.Trim());
            byte[] esperado = Convert.FromHexString(hashHex.Trim());
            byte[] calculado = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string GenerarToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

// =========================================================
//  Sesiones: única "memoria" de la API = caché de 30 s de sesiones válidas.
//  Si se pierde (reinicio / otra réplica), simplemente se consulta la BD.
// =========================================================
static class Sesiones
{
    const int SegundosCache = 30;
    static readonly ConcurrentDictionary<string, (string Usuario, DateTime Expira, DateTime RevalidarEn)> Cache = new();

    public static async Task<string?> ValidarAsync(NpgsqlDataSource db, string tokenHash)
    {
        var ahora = DateTime.UtcNow;
        if (Cache.TryGetValue(tokenHash, out var c) && ahora < c.RevalidarEn && ahora < c.Expira)
            return c.Usuario;

        try
        {
            await using var cmd = db.CreateCommand(@"
                SELECT u.usuario, s.expira_en
                FROM sesiones s
                JOIN usuarios u ON u.id = s.usuario_id
                WHERE s.token_hash = @h AND s.expira_en > now() AND u.activo = true
                LIMIT 1;");
            cmd.Parameters.AddWithValue("h", tokenHash);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                Cache.TryRemove(tokenHash, out _);
                return null;
            }

            string usuario = r.GetString(0);
            DateTime expira = r.GetDateTime(1).ToUniversalTime();

            if (Cache.Count > 500)
                foreach (var k in Cache.Where(x => x.Value.Expira < ahora).Select(x => x.Key).ToList())
                    Cache.TryRemove(k, out _);

            Cache[tokenHash] = (usuario, expira, ahora.AddSeconds(SegundosCache));
            return usuario;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Auth] Error validando sesión: {ex.Message}");
            return null;   // si la BD falla, se niega el acceso
        }
    }

    public static void Quitar(string tokenHash) => Cache.TryRemove(tokenHash, out _);
}