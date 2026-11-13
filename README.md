# ECLIPSE â€” Monitoreo proactivo de la salud de equipos de cÃ³mputo

Proyecto Final de Seminario Â· Anderson Abimael Isem Cac (7690-22-9604)

**AplicaciÃ³n en producciÃ³n:** https://eclipse-panelcentral.duckdns.org

ECLIPSE es un agente para Windows (10 y 11) que mide CPU, RAM y disco cada 2 segundos, calcula un puntaje de salud (0â€“100), alerta en un panel web y limpia Temp/Prefetch en un clic. El backend (API + PostgreSQL) corre en Kubernetes sobre Google Cloud, con HTTPS (Let's Encrypt). En el hospital se instalÃ³ solo en equipos con Windows 11 (ver "Resultados").

## El problema

La Paz Grupo Hospitalario (450 PCs) atendÃ­a la lentitud de forma reactiva: 126 tickets entre mayo y septiembre de 2026, con 383.7 min de impacto promedio por ticket, de los cuales 208.1 min eran tiempo sin detectar la falla.

## Resultados (datos reales)

| Indicador | Pretest | ECLIPSE | Meta |
|---|---|---|---|
| DetecciÃ³n | 208.1 min | 0.55 min | â‰¤ 1 min |
| SoluciÃ³n | 152.9 min | 1.40 min | â‰¤ 2 min |
| Impacto total por incidente | 383.7 min | 2.28 min | < 4 min |
| Puntaje de salud (tras atender) | no medido | 90.1 | â‰¥ 80 |
| Tickets por dÃ­a de muestreo | 1.91 | 1.25 (âˆ’34.5 %) | reducciÃ³n â‰¥ 30 % |

Muestra postest: 12 oct â€“ 6 nov 2026, 405 equipos Windows 11 con ECLIPSE (10 tickets) vs 45 equipos Windows 10 de control sin ECLIPSE (5 tickets). SegÃºn lo indicado por el hospital, los 45 equipos con Windows 10 se reemplazarÃ¡n por equipos nuevos y ya no reciben actualizaciones de seguridad, por lo que no se instalÃ³ el agente en ellos (decisiÃ³n operativa, no una limitaciÃ³n tÃ©cnica). Limitaciones (ventana corta, grupos no aleatorios) en el informe PDF, secciÃ³n 5.6.

## Arquitectura

```
Agentes Windows â”€â”€HTTPSâ”€â”€â–¶ Ingress (Traefik + cert-manager)
                              â”œâ”€â–¶ eclipse-web  (x2)  panel
                              â””â”€â–¶ eclipse-api  (x2)  REST â”€â”€â–¶ eclipse-db (PostgreSQL 16, StatefulSet + PVC 5 Gi)
```

Todos los usuarios de la aplicaciÃ³n se almacenan en la base de datos del backend (tabla `usuarios`; contraseÃ±as con PBKDF2-SHA256 + sal; sesiones con hash SHA-256 del token).

## Estructura del repositorio

```
AplicativoAnalizador/     # Agente Windows (WinForms .NET) + instalador (eclipse.iss)
EclipseApi/               # Backend REST (API) + Dockerfile
Eclipse-web/              # Panel web (HTML/JS) + Dockerfile + nginx.conf
db/init.sql               # Esquema PostgreSQL
docker-compose.yml        # Prueba local (valores de ejemplo, no de producciÃ³n)
k8s/
  kustomization.yaml      # Punto de entrada: kubectl apply -k k8s/
  01-secret.example.yaml  # Plantilla con valores FALSOS
  02-postgres.yaml        # Service headless + StatefulSet PostgreSQL
  03-api.yaml             # Service + Deployment eclipse-api (2 rÃ©plicas)
  04-web.yaml             # Service + Deployment eclipse-web (2 rÃ©plicas)
  05-ingress.yaml         # Ingress Traefik + TLS
  06-issuer.yaml          # Issuer cert-manager (Let's Encrypt)
  init.sql                # Esquema (Kustomize lo convierte en el ConfigMap eclipse-initdb)
  _exportados-del-cluster/  # Copia de referencia exportada con kubectl
scripts/pruebas-seguridad.ps1   # BaterÃ­a de 8 pruebas de caja negra
LICENSE                   # Apache-2.0
```

ImÃ¡genes: `andersonclay/eclipse-api:1.0.0` Â· `andersonclay/eclipse-web:1.0.0`

## Despliegue

Requisitos: clÃºster Kubernetes con Traefik y cert-manager, `kubectl`, namespace `aisemc`, y el dominio DuckDNS apuntando a la IP del Ingress.

```powershell
# 1. Namespace (si no existe)
kubectl create namespace aisemc

# 2. Secret real (NO se versiona; ver 01-secret.example.yaml)
kubectl create secret generic eclipse-secret -n aisemc `
  --from-literal=DB_CONNECTION='<cadena-de-conexion>' `
  --from-literal=AGENT_KEY='<clave-de-agentes>' `
  --from-literal=ADMIN_PASSWORD='<password-admin>' `
  --from-literal=POSTGRES_PASSWORD='<password-postgres>'

# 3. Todo lo demÃ¡s (incluye el ConfigMap eclipse-initdb, generado desde k8s/init.sql)
kubectl apply -k k8s/

# 4. Verificar
kubectl get all,ingress,pvc,certificate,configmap -n aisemc
```

El certificado tarda 1â€“3 minutos en quedar `READY = True`.

El ConfigMap `eclipse-initdb` no se crea a mano: lo genera `kustomization.yaml` (`configMapGenerator`) a partir de `k8s/init.sql`, y PostgreSQL ejecuta ese esquema en su primer arranque con el volumen vacÃ­o.

## VerificaciÃ³n y resiliencia

```powershell
kubectl get pods -n aisemc -o wide                       # 2 API + 2 web + 1 DB, todos Running
kubectl scale deployment eclipse-api -n aisemc --replicas=2
kubectl delete pod <un-pod-de-eclipse-api> -n aisemc     # el servicio sigue respondiendo
curl.exe -I https://eclipse-panelcentral.duckdns.org     # HTTPS vÃ¡lido
```

Los pods usan `readinessProbe`/`livenessProbe`, corren como no-root, con `allowPrivilegeEscalation: false` y lÃ­mites de CPU/memoria.

## Pruebas de seguridad

```powershell
.\scripts\pruebas-seguridad.ps1    # deja la salida en resultados-seguridad.txt
```

Hallazgos abiertos (documentados en el informe, secciÃ³n 6.3): HTTP sin redirección a HTTPS, sin cabeceras de seguridad, base de datos de una sola rÃ©plica sin respaldo programado, imÃ¡genes con etiqueta mutable, sin NetworkPolicy, y `AGENT_KEY` compartida entre todos los agentes (mejora futura: clave por equipo con enrolamiento).

## Licencia

Apache License 2.0 â€” ver [LICENSE](LICENSE). Componentes de terceros con sus propias licencias (PostgreSQL, Kubernetes, Traefik, cert-manager).
