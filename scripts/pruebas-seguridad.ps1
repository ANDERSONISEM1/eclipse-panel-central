# ECLIPSE - Bateria de pruebas de seguridad (caja negra)
# Uso: .\scripts\pruebas-seguridad.ps1   (guarda la salida en resultados-seguridad.txt)
Start-Transcript -Path .\resultados-seguridad.txt -Force | Out-Null
$U="https://eclipse-panelcentral.duckdns.org"
function T($m,$u,$b=$null,$h=@{}){
  try { $r=Invoke-WebRequest -Method $m -Uri $u -Body $b -ContentType "application/json" -Headers $h -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop; "$m $u -> $($r.StatusCode)" }
  catch { "$m $u -> $([int]$_.Exception.Response.StatusCode)" }
}
"1) Sin token (esperado 401)";            T GET "$U/api/equipos"
"2) Token falso (esperado 401)";          T GET "$U/api/equipos" $null @{Authorization="Bearer abc123"}
"3) Reporte sin clave (esperado 401)";    T POST "$U/api/agente/reporte" '{"identificador":"x"}'
"4) Reporte clave falsa (esperado 401)";  T POST "$U/api/agente/reporte" '{"identificador":"x"}' @{"X-Agent-Key"="falsa"}
"5) Inyeccion SQL (esperado 401, no 500)"; T POST "$U/api/auth/login" '{"usuario":"admin'' OR ''1''=''1","password":"x"}'
"6) HTTP dominio principal (308/301 = redirige)"; T GET "http://eclipse-panelcentral.duckdns.org/"
"7) HTTP dominio nip.io (informativa)";   T GET "http://eclipse.35.188.164.62.nip.io/"
"8) Cabeceras de seguridad presentes:";   (Invoke-WebRequest $U -UseBasicParsing).Headers.Keys
Stop-Transcript | Out-Null