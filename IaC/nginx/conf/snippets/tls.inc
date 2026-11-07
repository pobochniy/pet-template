# HTTPS server-блока. Сертификат один на все имена — example.com, www, adminka и grafana
# (IaC/issue-cert.sh); certbot кладёт его в ./certbot/conf, смонтированный сюда как /etc/nginx/ssl.
# `listen` — в самом сайте: default_server бывает только у одного
http2 on;

ssl_certificate     /etc/nginx/ssl/live/example.com/fullchain.pem;
ssl_certificate_key /etc/nginx/ssl/live/example.com/privkey.pem;
