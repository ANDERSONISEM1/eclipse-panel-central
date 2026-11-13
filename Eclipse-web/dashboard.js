// =========================================================
// ECLIPSE — dashboard.js
// Lógica exclusiva de la página del panel (panel.html).
//
// ✅ Lista de equipos EN VIVO cada 2 s desde la API central.
// ✅ Todas las peticiones llevan el token de sesión (Bearer).
// ✅ API_BASE_URL vacío = ruta relativa (/api/...), nginx hace el proxy.
// ✅ Equipos "sin conexión" si pasan más de 30 s sin reportar.
// ✅ Si una petición de refresco falla, se conserva la tabla anterior.
// =========================================================

const API_BASE_URL = "";
const INTERVALO_REFRESCO_MS = 2000;
const UMBRAL_SIN_CONEXION_MS = 30000;

let equipos = [];
let filtroActivo = "todos";
let terminoBusqueda = "";
let intervaloRefresco = null;
let segundosDesdeUltimoRefresco = 0;

const btnSalir = document.getElementById("btnSalir");
const chipsWrap = document.getElementById("chips");
const buscarIp = document.getElementById("buscarIp");
const cuerpoTabla = document.getElementById("cuerpoTabla");
const tablaVacia = document.getElementById("tablaVacia");
const relojEstado = document.getElementById("relojEstado");
const topbarUsuario = document.getElementById("topbarUsuario");
const plantillaFila = document.getElementById("plantillaFila");
const plantillaReporte = document.getElementById("plantillaReporte");
const plantillaProceso = document.getElementById("plantillaProceso");

// Referencias del modal
const modalBackdrop = document.getElementById("modalBackdrop");
const modalCerrar = document.getElementById("modalCerrar");
const modalDot = document.getElementById("modalDot");
const modalHost = document.getElementById("modalHost");
const modalIp = document.getElementById("modalIp");
const modalBadge = document.getElementById("modalBadge");
const modalCuerpo = document.getElementById("modalCuerpo");

// =========================================================
// ARRANQUE — si no hay sesión, no se llega al panel
// =========================================================
document.addEventListener("DOMContentLoaded", () => {
  if (!sessionStorage.getItem("eclipse_token")) {
    window.location.href = "index.html";
    return;
  }
  topbarUsuario.textContent = sessionStorage.getItem("eclipse_usuario") || "admin";
  cargarEquipos();
  iniciarRefrescoAutomatico();
});

btnSalir.addEventListener("click", cerrarSesion);
chipsWrap.addEventListener("click", manejarClicFiltro);
buscarIp.addEventListener("input", manejarBusqueda);

modalCerrar.addEventListener("click", cerrarModal);
modalBackdrop.addEventListener("click", (evento) => {
  if (evento.target === modalBackdrop) cerrarModal();
});
document.addEventListener("keydown", (evento) => {
  if (evento.key === "Escape" && !modalBackdrop.hidden) cerrarModal();
});

// =========================================================
// SESIÓN
// =========================================================
function limpiarSesionLocal() {
  sessionStorage.removeItem("eclipse_token");
  sessionStorage.removeItem("eclipse_usuario");
}

// Se usa cuando el servidor dice 401 (token vencido o inválido)
function forzarCierreSesion() {
  detenerRefrescoAutomatico();
  limpiarSesionLocal();
  window.location.href = "index.html";
}

async function cerrarSesion() {
  detenerRefrescoAutomatico();
  try {
    await fetchApi("/api/auth/logout", { method: "POST" });
  } catch (_) { /* si falla, igual se cierra localmente */ }
  limpiarSesionLocal();
  window.location.href = "index.html";
}

// Todas las llamadas al servidor pasan por aquí: agrega el token
// y, si el servidor responde 401, saca al usuario al login.
async function fetchApi(ruta, opciones = {}) {
  const token = sessionStorage.getItem("eclipse_token");
  const resp = await fetch(`${API_BASE_URL}${ruta}`, {
    ...opciones,
    headers: { ...(opciones.headers || {}), Authorization: `Bearer ${token}` },
  });
  if (resp.status === 401) {
    forzarCierreSesion();
    throw new Error("Sesión no válida");
  }
  return resp;
}

// =========================================================
// CARGA Y REFRESCO DE LA LISTA (EN VIVO)
// =========================================================
async function cargarEquipos() {
  const nuevos = await obtenerEquipos();
  if (nuevos) {
    equipos = nuevos;
    renderizarTodo();
    segundosDesdeUltimoRefresco = 0;
    relojEstado.textContent = "Actualizado justo ahora";
  } else {
    relojEstado.textContent = "Sin respuesta del servidor…";
  }
}

function iniciarRefrescoAutomatico() {
  detenerRefrescoAutomatico();
  segundosDesdeUltimoRefresco = 0;

  intervaloRefresco = setInterval(async () => {
    const nuevos = await obtenerEquipos();
    if (nuevos) {
      equipos = nuevos;
      renderizarTodo();
      segundosDesdeUltimoRefresco = 0;
      relojEstado.textContent = "Actualizado justo ahora";
    } else {
      relojEstado.textContent = "Sin respuesta del servidor…";
    }
  }, INTERVALO_REFRESCO_MS);
}

function detenerRefrescoAutomatico() {
  if (intervaloRefresco) clearInterval(intervaloRefresco);
  intervaloRefresco = null;
}

// =========================================================
// 🔌 API REAL — consume la API central
// =========================================================
// Devuelve la lista, o null si hubo un fallo (así se conserva la tabla anterior).
async function obtenerEquipos() {
  try {
    const resp = await fetchApi("/api/equipos");
    if (!resp.ok) return null;
    const datos = await resp.json();
    return datos.map((e) => ({ ...e, ultimoReporte: new Date(e.ultimoReporte) }));
  } catch (err) {
    console.error("No se pudo conectar con la API de ECLIPSE.", err);
    return null;
  }
}

async function obtenerDetalleEquipo(equipoBase) {
  try {
    const resp = await fetchApi(`/api/equipos/${equipoBase.id}/detalle`);
    if (!resp.ok) return null;
    return await resp.json();
  } catch (err) {
    console.error("No se pudo obtener el detalle del equipo:", err);
    return null;
  }
}

async function obtenerAlertas(equipoBase) {
  try {
    const resp = await fetchApi(`/api/equipos/${equipoBase.id}/alertas?limite=8`);
    if (!resp.ok) return [];
    return await resp.json();
  } catch (err) {
    console.error("No se pudieron obtener las alertas:", err);
    return [];
  }
}

// =========================================================
// RENDER — TABLA
// =========================================================
function renderizarTodo() {
  actualizarContadores();
  renderizarTabla();
}

function actualizarContadores() {
  const conteo = { normal: 0, advertencia: 0, critico: 0 };
  equipos.forEach((e) => {
    if (conteo[e.estado] !== undefined) conteo[e.estado]++;
  });

  document.getElementById("numTodos").textContent = equipos.length;
  document.getElementById("numNormal").textContent = conteo.normal;
  document.getElementById("numAdvertencia").textContent = conteo.advertencia;
  document.getElementById("numCritico").textContent = conteo.critico;
}

function obtenerListaFiltrada() {
  const busqueda = terminoBusqueda.trim().toLowerCase();
  let lista = equipos;

  if (filtroActivo !== "todos") {
    lista = lista.filter((e) => e.estado === filtroActivo);
  }
  if (busqueda) {
    lista = lista.filter((e) => (e.ip || "").toLowerCase().includes(busqueda));
  }

  const orden = { critico: 0, advertencia: 1, normal: 2 };
  return [...lista].sort((a, b) => orden[a.estado] - orden[b.estado]);
}

function renderizarTabla() {
  const listaOrdenada = obtenerListaFiltrada();

  cuerpoTabla.innerHTML = "";
  tablaVacia.hidden = listaOrdenada.length > 0;

  listaOrdenada.forEach((equipo) => {
    cuerpoTabla.appendChild(crearFila(equipo));
  });
}

// =========================================================
// Clasificación POR MÉTRICA (mismos umbrales que la API)
// =========================================================
function estadoCpu(valor) {
  if (valor >= 95) return "critico";
  if (valor >= 85) return "advertencia";
  return "normal";
}

function estadoRam(valor) {
  if (valor >= 92) return "critico";
  if (valor >= 80) return "advertencia";
  return "normal";
}

function estadoDiscoLibre(valorLibre) {
  if (valorLibre < 10) return "critico";
  if (valorLibre < 20) return "advertencia";
  return "normal";
}

function pintarMetricaMini(fila, prefijo, valor, estado) {
  const valorEl = fila.querySelector(`.${prefijo}`);
  const fillEl = fila.querySelector(`.${prefijo}-fill`);

  valorEl.textContent = `${Math.round(valor)}%`;

  const pct = Math.min(100, Math.max(0, valor));
  fillEl.style.width = `${pct}%`;
  fillEl.dataset.estado = estado;
}

function estaSinConexion(equipo) {
  return Date.now() - equipo.ultimoReporte.getTime() > UMBRAL_SIN_CONEXION_MS;
}

function crearFila(equipo) {
  const nodo = plantillaFila.content.cloneNode(true);
  const fila = nodo.querySelector(".fila");

  fila.dataset.estado = equipo.estado;
  fila.querySelector(".fila-host").textContent = equipo.hostname;
  fila.querySelector(".fila-ip").textContent = equipo.ip;

  pintarMetricaMini(fila, "fila-cpu", equipo.cpu, estadoCpu(equipo.cpu));
  pintarMetricaMini(fila, "fila-ram", equipo.ram, estadoRam(equipo.ram));
  pintarMetricaMini(fila, "fila-disco", equipo.discoLibre, estadoDiscoLibre(equipo.discoLibre));

  const visto = fila.querySelector(".fila-visto");
  if (estaSinConexion(equipo)) {
    fila.style.opacity = "0.55";
    visto.textContent = `Sin conexión · ${tiempoRelativo(equipo.ultimoReporte)}`;
  } else {
    visto.textContent = tiempoRelativo(equipo.ultimoReporte);
  }

  fila.querySelector(".boton-ver").addEventListener("click", () => abrirModal(equipo));

  return nodo;
}

// =========================================================
// MODAL — REPORTE DE SALUD
// =========================================================
async function abrirModal(equipoBase) {
  const modalInner = modalBackdrop.querySelector(".modal");
  modalInner.dataset.estado = equipoBase.estado;
  modalDot.dataset.estado = equipoBase.estado;
  modalHost.textContent = equipoBase.hostname;
  modalIp.textContent = equipoBase.ip;
  modalBadge.textContent = textoEstado(equipoBase.estado);
  modalBadge.parentElement.dataset.estado = equipoBase.estado;
  modalCuerpo.innerHTML = '<p class="modal-cargando">Cargando reporte…</p>';
  modalCuerpo.scrollTop = 0;

  modalBackdrop.hidden = false;
  modalCerrar.focus();

  // El detalle y las alertas se piden al mismo tiempo
  const [detalle, alertas] = await Promise.all([
    obtenerDetalleEquipo(equipoBase),
    obtenerAlertas(equipoBase),
  ]);

  if (modalBackdrop.hidden) return;

  if (!detalle) {
    modalCuerpo.innerHTML = '<p class="modal-cargando">No se pudo cargar el reporte. Intenta de nuevo en unos segundos.</p>';
    return;
  }

  pintarReporte(detalle, alertas);
}

function pintarReporte(detalle, alertas) {
  const nodo = plantillaReporte.content.cloneNode(true);

  // Anillo de puntaje
  const puntaje = detalle.puntajeSalud;
  const colorPuntaje = colorDeEstadoPuntaje(puntaje.estado);
  const anillo = nodo.getElementById("anillo");
  anillo.style.setProperty("--pct", puntaje.total);
  anillo.style.setProperty("--color-anillo", colorPuntaje);
  nodo.getElementById("reportePuntajeValor").textContent = puntaje.total;
  const estadoTexto = nodo.getElementById("reportePuntajeEstado");
  estadoTexto.textContent = puntaje.estado;
  estadoTexto.style.color = colorPuntaje;

  // Procesos más pesados
  const tbodyProcesos = nodo.getElementById("reporteProcesos");
  const sinProcesos = nodo.getElementById("reporteSinProcesos");
  if (detalle.procesosDestacados.length === 0) {
    sinProcesos.hidden = false;
  } else {
    detalle.procesosDestacados.forEach((p) => {
      const fila = plantillaProceso.content.cloneNode(true);
      fila.querySelector(".proceso-nombre").textContent = p.nombre;
      fila.querySelector(".proceso-cpu").textContent = `${Number(p.cpu).toFixed(1)}%`;
      fila.querySelector(".proceso-ram").textContent = `${p.ramMb} MB`;
      tbodyProcesos.appendChild(fila);
    });
  }

  // Programas de inicio
  nodo.getElementById("reporteProgramasInicio").textContent =
    `${detalle.programasInicio.habilitados} de ${detalle.programasInicio.total} están habilitados al arrancar Windows.`;

  // Alertas
  pintarAlertas(nodo, alertas || []);

  modalCuerpo.innerHTML = "";
  modalCuerpo.appendChild(nodo);
}

// ---------------------------------------------------------
// Alertas (cambios de estado)
// ---------------------------------------------------------
function pintarAlertas(nodo, alertas) {
  const lista = nodo.getElementById("reporteAlertas");
  const sinAlertas = nodo.getElementById("reporteSinAlertas");

  if (alertas.length === 0) {
    sinAlertas.hidden = false;
    return;
  }

  alertas.forEach((a) => {
    const li = document.createElement("li");
    li.className = "alerta-item";

    const dot = document.createElement("span");
    dot.className = "alerta-dot";
    dot.dataset.estado = a.estadoNuevo;

    const cuerpo = document.createElement("div");

    const titulo = document.createElement("p");
    titulo.className = "alerta-titulo";
    titulo.textContent = a.estadoAnterior
      ? `${textoEstado(a.estadoAnterior)} → ${textoEstado(a.estadoNuevo)}`
      : textoEstado(a.estadoNuevo);

    const detalle = document.createElement("p");
    detalle.className = "alerta-detalle";
    detalle.textContent = a.detalle;

    const fecha = document.createElement("p");
    fecha.className = "alerta-fecha";
    fecha.textContent = formatearFechaCorta(new Date(a.fecha));

    cuerpo.append(titulo, detalle, fecha);
    li.append(dot, cuerpo);
    lista.appendChild(li);
  });
}

function colorDeEstadoPuntaje(estado) {
  if (estado === "Saludable") return "var(--ok)";
  if (estado === "Regular") return "var(--warn)";
  return "var(--crit)";
}

function cerrarModal() {
  modalBackdrop.hidden = true;
}

function textoEstado(estado) {
  return { normal: "NORMAL", advertencia: "ADVERTENCIA", critico: "CRÍTICO" }[estado] || estado;
}

// =========================================================
// FILTROS Y BÚSQUEDA
// =========================================================
function manejarClicFiltro(evento) {
  const chip = evento.target.closest(".chip");
  if (!chip) return;

  filtroActivo = chip.dataset.filtro;

  document.querySelectorAll(".chip").forEach((c) => {
    c.classList.remove("chip-activo");
    c.setAttribute("aria-selected", "false");
  });
  chip.classList.add("chip-activo");
  chip.setAttribute("aria-selected", "true");

  renderizarTabla();
}

function manejarBusqueda(evento) {
  terminoBusqueda = evento.target.value;
  renderizarTabla();
}

// =========================================================
// UTILIDADES
// =========================================================
function tiempoRelativo(fecha) {
  const segundos = Math.max(0, Math.round((Date.now() - fecha.getTime()) / 1000));
  if (segundos < 60) return `hace ${segundos}s`;
  const minutos = Math.round(segundos / 60);
  if (minutos < 60) return `hace ${minutos} min`;
  const horas = Math.round(minutos / 60);
  return `hace ${horas} h`;
}

function dosDigitos(n) {
  return String(n).padStart(2, "0");
}

function formatearFechaCorta(fecha) {
  return `${dosDigitos(fecha.getDate())}/${dosDigitos(fecha.getMonth() + 1)} ${dosDigitos(fecha.getHours())}:${dosDigitos(fecha.getMinutes())}`;
}