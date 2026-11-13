# nginx sin privilegios (no-root), escucha en 8080
FROM nginxinc/nginx-unprivileged:1.27-alpine

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY index.html panel.html auth.js dashboard.js styles.css /usr/share/nginx/html/

EXPOSE 8080