-- =========================================================
-- ECLIPSE ÔÇö Base de datos eclipse_monitoreo (PostgreSQL 16)
-- Guardado: resumen cada 30 min + registro inmediato al cambiar de estado
-- + estado_vivo: ├║ltima foto de cada equipo (de aqu├¡ sale la tabla de la web)
-- =========================================================

-- 1) USUARIOS (un solo administrador)
CREATE TABLE usuarios (
    id SERIAL PRIMARY KEY,
    usuario VARCHAR(50) UNIQUE NOT NULL,
    nombre_completo VARCHAR(100),
    password_hash CHAR(64) NOT NULL,      -- PBKDF2-SHA256 en hex (32 bytes = 64 chars)
    password_salt CHAR(32) NOT NULL,      -- 16 bytes aleatorios en hex
    es_admin BOOLEAN NOT NULL DEFAULT false,
    activo BOOLEAN NOT NULL DEFAULT true,
    intentos_fallidos INT NOT NULL DEFAULT 0,
    bloqueado_hasta TIMESTAMPTZ,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 2) EQUIPOS
CREATE TABLE equipos (
    id SERIAL PRIMARY KEY,
    identificador VARCHAR(64) UNIQUE NOT NULL,   -- MachineGuid de Windows
    hostname VARCHAR(100) NOT NULL,
    ip VARCHAR(45),
    ubicacion VARCHAR(100),
    activo BOOLEAN NOT NULL DEFAULT true,
    ultimo_reporte TIMESTAMPTZ,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 3) REGISTROS DE ESTADO (resumen de cada ventana de 30 min)
--    Ô£à CORREGIDO: origen era VARCHAR(12) y 'cambio_estado' mide 13 caracteres.
CREATE TABLE registros_estado (
    id BIGSERIAL PRIMARY KEY,
    equipo_id INT NOT NULL REFERENCES equipos(id) ON DELETE CASCADE,
    cpu NUMERIC(5,2) NOT NULL,            -- promedio de la ventana
    cpu_max NUMERIC(5,2) NOT NULL,        -- pico de la ventana
    ram NUMERIC(5,2) NOT NULL,
    disco_libre NUMERIC(5,2) NOT NULL,
    estado VARCHAR(15) NOT NULL CHECK (estado IN ('normal','advertencia','critico')),
    puntaje_salud INT CHECK (puntaje_salud BETWEEN 0 AND 100),
    programas_inicio_habilitados INT,
    programas_inicio_total INT,
    procesos_pesados INT,
    origen VARCHAR(20) NOT NULL DEFAULT 'periodico'
        CHECK (origen IN ('periodico','cambio_estado')),
    registrado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_registros_equipo_fecha ON registros_estado(equipo_id, registrado_en DESC);
CREATE INDEX idx_registros_fecha ON registros_estado(registrado_en);

-- 4) ALERTAS (cambios de estado)
CREATE TABLE alertas (
    id BIGSERIAL PRIMARY KEY,
    equipo_id INT NOT NULL REFERENCES equipos(id) ON DELETE CASCADE,
    estado_anterior VARCHAR(15),
    estado_nuevo VARCHAR(15) NOT NULL,
    detalle VARCHAR(200),
    creada_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_alertas_equipo_fecha ON alertas(equipo_id, creada_en DESC);

-- 5) SESIONES (se guarda el hash del token, no el token)
CREATE TABLE sesiones (
    id SERIAL PRIMARY KEY,
    usuario_id INT NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
    token_hash CHAR(64) UNIQUE NOT NULL,  -- SHA-256 del token
    iniciado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expira_en TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_sesiones_expira ON sesiones(expira_en);

-- 6) ESTADO_VIVO (NUEVA): ├║ltima foto de cada equipo + antirruido de cambios de estado
CREATE TABLE estado_vivo (
    equipo_id INT PRIMARY KEY REFERENCES equipos(id) ON DELETE CASCADE,
    cpu NUMERIC(5,2) NOT NULL,
    ram NUMERIC(5,2) NOT NULL,
    disco_libre NUMERIC(5,2) NOT NULL,
    estado VARCHAR(15) NOT NULL DEFAULT 'normal'
        CHECK (estado IN ('normal','advertencia','critico')),
    candidato VARCHAR(15) NOT NULL DEFAULT 'normal'
        CHECK (candidato IN ('normal','advertencia','critico')),
    candidato_conteo INT NOT NULL DEFAULT 0,
    puntaje INT NOT NULL DEFAULT 0 CHECK (puntaje BETWEEN 0 AND 100),
    prog_hab INT NOT NULL DEFAULT 0,
    prog_total INT NOT NULL DEFAULT 0,
    pesados INT NOT NULL DEFAULT 0,
    procesos JSONB NOT NULL DEFAULT '[]'::jsonb,   -- m├íximo 5: [{nombre,cpu,ramMb}]
    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 7) VISTA: comportamiento por bloques de 8 horas (hora de Guatemala, UTC-6)
--    Bloques: 00:00-08:00, 08:00-16:00, 16:00-24:00
CREATE VIEW vista_historial_8h AS
SELECT
    equipo_id,
    date_bin('8 hours', registrado_en, TIMESTAMPTZ '2000-01-01 00:00:00-06') AS bloque_inicio,
    ROUND(AVG(cpu), 1)         AS cpu_prom,
    MAX(cpu_max)               AS cpu_pico,
    ROUND(AVG(ram), 1)         AS ram_prom,
    MAX(ram)                   AS ram_pico,
    MIN(disco_libre)           AS disco_libre_min,
    ROUND(AVG(puntaje_salud))  AS puntaje_prom,
    COUNT(*)                   AS num_registros
FROM registros_estado
GROUP BY equipo_id, bloque_inicio;