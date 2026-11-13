# ECLIPSE — Monitoreo proactivo de la salud de equipos de cómputo

Proyecto Final de Seminario · Anderson Abimael Isem Cac (7690-22-9604)

**Aplicación en producción:** https://eclipse-panelcentral.duckdns.org

ECLIPSE es un agente para Windows 11 que mide CPU, RAM y disco cada 2 segundos, calcula un puntaje de salud (0–100), alerta en un panel web y limpia Temp/Prefetch en un clic. El backend (API + PostgreSQL) corre en Kubernetes sobre Google Cloud, con HTTPS (Let's Encrypt).

## El problema

La Paz Grupo Hospitalario (450 PCs) atendía la lentitud de forma reactiva: 126 tickets entre mayo y septiembre de 2026, con 383.7 min de impacto promedio por ticket, de los cuales 208.1 min eran tiempo sin detectar la falla.

## Resultados (datos reales)

| Indicador | Pretest | ECLIPSE | Meta |
|---|---|---|---|
| Detección | 208.1 min | 0.55 min | ≤ 1 min |
| Solución | 152.9 min | 1.40 min | ≤ 2 min |
| Impacto total por incidente | 383.7 min | 2.28 min | < 4 min |
| Puntaje de salud (tras atender) | no medido | 90.1 | ≥ 80 |
| Tickets por día de muestreo | 1.91 | 1.25 (−34.5 %) | reducción ≥ 30 % |

Muestra postest: 12 oct – 6 nov 2026, 405 equipos Windows 11 (10 tickets) vs 45 equipos Windows 10 de control (5 tickets). Limitaciones (ventana corta, grupos no aleatorios) en el informe PDF, sección 5.6.

## Arquitectura

```
Agentes Windows ──HTTPS──▶ Ingress (Traefik + cert-manager)
                              ├─▶ eclipse-web  (x2)  panel
                              └─▶ eclipse-api  (x2)  REST ──▶ eclipse-db (PostgreSQL 16, StatefulSet + PVC 5 Gi)
```

Todos los usuarios de la aplicación se almacenan en la base de datos del backend (tabla `usuarios`; contraseñas con PBKDF2-SHA256 + sal; sesiones con hash SHA-256 del token).

## Estructura del repositorio

```
AplicativoAnalizador/     # Agente Windows (WinForms .NET) + instalador (eclipse.iss)
EclipseApi/               # Backend REST (API) + Dockerfile
Eclipse-web/              # Panel web (HTML/JS) + Dockerfile + nginx.conf
db/init.sql               # Esquema PostgreSQL
docker-compose.yml        # Prueba local (valores de ejemplo, no de producción)
k8s/
  kustomization.yaml      # Punto de entrada: kubectl apply -k k8s/
  01-secret.example.yaml  # Plantilla con valores FALSOS
  02-postgres.yaml        # Service headless + StatefulSet PostgreSQL
  03-api.yaml             # Service + Deployment eclipse-api (2 réplicas)
  04-web.yaml             # Service + Deployment eclipse-web (2 réplicas)
  05-ingress.yaml         # Ingress Traefik + TLS
  06-issuer.yaml          # Issuer cert-manager (Let's Encrypt)
  init.sql                # Esquema (Kustomize lo convierte en el ConfigMap eclipse-initdb)
  _exportados-del-cluster/  # Copia de referencia exportada con kubectl
scripts/pruebas-seguridad.ps1   # Batería de 8 pruebas de caja negra
LICENSE                   # Apache-2.0
```

Imágenes: `andersonclay/eclipse-api:1.0.0` · `andersonclay/eclipse-web:1.0.0`

## Despliegue

Requisitos: clúster Kubernetes con Traefik y cert-manager, `kubectl`, namespace `aisemc`, y el dominio DuckDNS apuntando a la IP del Ingress.

```powershell
# 1. Namespace (si no existe)
kubectl create namespace aisemc

# 2. Secret real (NO se versiona; ver 01-secret.example.yaml)
kubectl create secret generic eclipse-secret -n aisemc `
  --from-literal=DB_CONNECTION='<cadena-de-conexion>' `
  --from-literal=AGENT_KEY='<clave-de-agentes>' `
  --from-literal=ADMIN_PASSWORD='<password-admin>' `
  --from-literal=POSTGRES_PASSWORD='<password-postgres>'

# 3. Todo lo demás (incluye el ConfigMap eclipse-initdb, generado desde k8s/init.sql)
kubectl apply -k k8s/

# 4. Verificar
kubectl get all,ingress,pvc,certificate,configmap -n aisemc
```

El certificado tarda 1–3 minutos en quedar `READY = True`.

El ConfigMap `eclipse-initdb` no se crea a mano: lo genera `kustomization.yaml` (`configMapGenerator`) a partir de `k8s/init.sql`, y PostgreSQL ejecuta ese esquema en su primer arranque con el volumen vacío.

## Verificación y resiliencia

```powershell
kubectl get pods -n aisemc -o wide                       # 2 API + 2 web + 1 DB, todos Running
kubectl scale deployment eclipse-api -n aisemc --replicas=2
kubectl delete pod <un-pod-de-eclipse-api> -n aisemc     # el servicio sigue respondiendo
curl.exe -I https://eclipse-panelcentral.duckdns.org     # HTTPS válido
```

Los pods usan `readinessProbe`/`livenessProbe`, corren como no-root, con `allowPrivilegeEscalation: false` y límites de CPU/memoria.

## Pruebas de seguridad

```powershell
.\scripts\pruebas-seguridad.ps1    # deja la salida en resultados-seguridad.txt
```

Hallazgos abiertos (documentados en el informe, sección 6.3): host nip.io sin TLS, base de datos de una sola réplica sin respaldo programado, imágenes con etiqueta mutable, sin NetworkPolicy, y `AGENT_KEY` compartida entre todos los agentes (mejora futura: clave por equipo con enrolamiento).

## Licencia

Apache License 2.0 — ver [LICENSE](LICENSE). Componentes de terceros con sus propias licencias (PostgreSQL, Kubernetes, Traefik, cert-manager).