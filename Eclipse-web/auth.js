// =========================================================
// ECLIPSE — auth.js
// Lógica exclusiva de la página de login (index.html).
//
// ✅ El login valida contra la API central (POST /api/auth/login).
// ✅ API_BASE_URL vacío = ruta relativa (/api/...). nginx hace el proxy a la API.
// =========================================================

const API_BASE_URL = "";

const formLogin = document.getElementById("formLogin");
const loginError = document.getElementById("loginError");
const botonEntrar = formLogin.querySelector("button[type='submit']");

document.addEventListener("DOMContentLoaded", () => {
  if (sessionStorage.getItem("eclipse_token")) {
    window.location.href = "panel.html";
  }
});

formLogin.addEventListener("submit", manejarLogin);

async function manejarLogin(evento) {
  evento.preventDefault();
  loginError.hidden = true;

  const usuario = document.getElementById("loginUsuario").value.trim();
  const password = document.getElementById("loginPassword").value;

  botonEntrar.disabled = true;
  const textoOriginal = botonEntrar.textContent;
  botonEntrar.textContent = "Verificando…";

  const resultado = await autenticar(usuario, password);

  botonEntrar.disabled = false;
  botonEntrar.textContent = textoOriginal;

  if (!resultado.ok) {
    loginError.textContent = resultado.mensaje;
    loginError.hidden = false;
    return;
  }

  sessionStorage.setItem("eclipse_token", resultado.token);
  sessionStorage.setItem("eclipse_usuario", resultado.usuario || usuario);
  window.location.href = "panel.html";
}

async function autenticar(usuario, password) {
  try {
    const resp = await fetch(`${API_BASE_URL}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ usuario, password }),
    });
    const datos = await resp.json().catch(() => ({}));
    if (!resp.ok) {
      return { ok: false, mensaje: datos.error || "Usuario o contraseña incorrectos." };
    }
    return { ok: true, token: datos.token, usuario: datos.usuario };
  } catch (err) {
    console.error("No se pudo conectar con la API de ECLIPSE.", err);
    return { ok: false, mensaje: "No se pudo conectar con el servidor. Intenta de nuevo en unos segundos." };
  }
}