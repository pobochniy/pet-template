# Проксирование в API — внутри `location /api/`. Адрес — имя контейнера в сети nginx-proxy-man:
# его nginx разрешает на старте, поэтому сайты без контейнера dapi не поднимаются
proxy_pass              http://dapi:8080/api/;
proxy_http_version      1.1;
proxy_set_header        Upgrade $http_upgrade;
proxy_set_header        Connection keep-alive;
proxy_set_header        Host $host;
proxy_cache_bypass      $http_upgrade;
proxy_set_header        X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header        X-Forwarded-Proto $scheme;
