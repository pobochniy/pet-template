# Infrastructure as Code (IaC) для проекта PetTemplate

Эта папка содержит всю необходимую инфраструктуру для развертывания проекта PetTemplate на виртуальной машине.

## 📋 Содержимое

```
IaC/
├── docker-compose.yml       # Конфигурация Docker Compose (nginx, certbot, database)
├── nginx/
│   └── conf/
│       └── nginx.conf       # Конфигурация Nginx (reverse proxy)
├── setup-vm.sh              # Скрипт подготовки виртуальной машины
├── .env                     # Переменные окружения (создается автоматически)
├── frontend/                # Симлинк на ~/frontend (создается автоматически)
├── certbot/                 # Сертификаты Let's Encrypt (создается автоматически)
│   ├── www/
│   └── conf/
└── README.md                # Этот файл
```

## 🚀 Быстрый старт

### 1. Подготовка виртуальной машины

Скопируйте папку `IaC` на вашу виртуальную машину и запустите скрипт подготовки:

```bash
# Скопируйте папку IaC на VM
scp -r IaC user@your-vm:~

# Подключитесь к VM
ssh user@your-vm

# Перейдите в папку IaC
cd ~/IaC

# Сделайте скрипт исполняемым и запустите его
chmod +x setup-vm.sh
./setup-vm.sh
```

Скрипт автоматически:
- ✅ Проверит установку Docker и Docker Compose
- ✅ Создаст необходимые директории (`~/frontend`, `~/frontend-backup`)
- ✅ Создаст симлинк `IaC/frontend` → `~/frontend`
- ✅ Создаст Docker сеть `nginx-proxy-man`
- ✅ Создаст файл `.env` с дефолтными значениями
- ✅ Создаст директории для certbot

### 2. Настройка переменных окружения

Отредактируйте файл `.env` и установите безопасные пароли:

```bash
nano .env
```

```env
# Database configuration
MYSQL_ROOT_PASSWORD=your_secure_root_password
MYSQL_DATABASE=durakdb
MYSQL_USER=user_write
MYSQL_PASSWORD=your_secure_user_password

# Порты
DB_PORT=3320
NGINX_HTTP_PORT=80
NGINX_HTTPS_PORT=443
```

### 3. Настройка Nginx

Отредактируйте файл `nginx/conf/nginx.conf` и укажите ваш домен:

```bash
nano nginx/conf/nginx.conf
```

Замените `your-domain.com` на ваш домен в строках:
- `server_name your-domain.com www.your-domain.com;`
- `ssl_certificate /etc/nginx/ssl/live/your-domain.com/fullchain.pem;`
- `ssl_certificate_key /etc/nginx/ssl/live/your-domain.com/privkey.pem;`

### 4. Запуск инфраструктуры

```bash
# Запуск в фоновом режиме
docker-compose up -d

# Проверка статуса
docker-compose ps

# Просмотр логов
docker-compose logs -f
```

### 5. Настройка SSL сертификатов (опционально)

Если вы хотите использовать HTTPS с Let's Encrypt:

```bash
# Получение сертификата
docker-compose run --rm certbot certonly --webroot \
  --webroot-path=/var/www/certbot/ \
  -d your-domain.com \
  -d www.your-domain.com

# Автообновление сертификатов (добавьте в crontab)
0 0 * * * cd ~/IaC && docker-compose run --rm certbot renew && docker-compose restart webserver
```

### 6. Настройка GitHub Actions Runner

Установите self-hosted runner на VM:

```bash
# Создайте директорию для runner
mkdir ~/actions-runner && cd ~/actions-runner

# Скачайте runner (замените версию на актуальную)
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz

# Распакуйте
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz

# Настройте runner (получите токен в Settings → Actions → Runners)
./config.sh --url https://github.com/YOUR_USERNAME/PetTemplate \
  --token YOUR_TOKEN \
  --labels teslowe

# Запустите runner
./run.sh

# Для автозапуска как сервис:
sudo ./svc.sh install
sudo ./svc.sh start
```

## 🏗️ Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                    Internet (Port 80/443)                │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
                  ┌──────────────┐
                  │    Nginx     │ (webserver)
                  │  Reverse     │
                  │    Proxy     │
                  └──────┬───────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
    ┌────────┐    ┌──────────┐    ┌──────────┐
    │Frontend│    │   API    │    │  MySQL   │
    │  Files │    │  (dapi)  │    │   DB     │
    │~/frontend   │ :5050    │    │ :3306    │
    └────────┘    └──────────┘    └──────────┘
                         │               │
                         └───────┬───────┘
                                 │
                        nginx-proxy-man network
```

### Компоненты:

1. **Nginx (webserver)**
   - Обслуживает статические файлы фронтенда из `~/frontend`
   - Проксирует API запросы на контейнер `dapi`
   - Обрабатывает SSL/TLS (если настроен certbot)

2. **API контейнер (dapi)**
   - Создается через GitHub Actions
   - Имя контейнера: `dapi`
   - Порт: `5001:5050` (хост:контейнер)
   - Подключается к сети `nginx-proxy-man`

3. **MySQL Database (dbcontainer)**
   - База данных: `durakdb`
   - Пользователь: `user_write`
   - Порт: `3320:3306` (хост:контейнер)

4. **Certbot**
   - Автоматическое получение SSL сертификатов от Let's Encrypt
   - Хранит сертификаты в `./certbot/conf`

## 📦 Управление инфраструктурой

### Основные команды Docker Compose

```bash
# Запуск всех сервисов
docker-compose up -d

# Остановка всех сервисов
docker-compose down

# Перезапуск конкретного сервиса
docker-compose restart webserver

# Просмотр логов
docker-compose logs -f

# Просмотр логов конкретного сервиса
docker-compose logs -f webserver

# Проверка статуса
docker-compose ps

# Пересборка и перезапуск
docker-compose up -d --build
```

### Управление API контейнером (через GitHub Actions)

API контейнер (`dapi`) управляется через GitHub Actions и не входит в docker-compose.yml.

```bash
# Просмотр логов API
docker logs dapi

# Просмотр логов в реальном времени
docker logs -f dapi

# Перезапуск API (вручную, не рекомендуется)
docker restart dapi

# Проверка переменных окружения
docker inspect dapi | grep -A 10 "Env"
```

### Управление базой данных

```bash
# Подключение к MySQL
docker exec -it dbcontainer mysql -u user_write -p

# Создание бэкапа
docker exec dbcontainer mysqldump -u root -p durakdb > backup_$(date +%Y%m%d).sql

# Восстановление из бэкапа
docker exec -i dbcontainer mysql -u root -p durakdb < backup_20260303.sql
```

### Управление фронтендом

Фронтенд деплоится через GitHub Actions в директорию `~/frontend`.

```bash
# Просмотр файлов фронтенда
ls -la ~/frontend

# Откат на предыдущую версию (если есть бэкап)
rm -rf ~/frontend/* && cp -r ~/frontend-backup/* ~/frontend/

# Ручное обновление (не рекомендуется, используйте GitHub Actions)
# Скопируйте собранные файлы в ~/frontend
```

## 🔐 Безопасность

### Рекомендации:

1. **Используйте сильные пароли** для MySQL
2. **Настройте firewall** на VM:
   ```bash
   sudo ufw allow 22/tcp    # SSH
   sudo ufw allow 80/tcp    # HTTP
   sudo ufw allow 443/tcp   # HTTPS
   sudo ufw enable
   ```
3. **Ограничьте доступ к БД** - порт 3320 должен быть доступен только с localhost
4. **Регулярно обновляйте** Docker образы и систему
5. **Настройте автоматические бэкапы** БД
6. **Используйте SSL/TLS** для production окружения

### Настройка firewall для БД:

```bash
# Запретить внешний доступ к порту БД
sudo ufw deny 3320/tcp

# Разрешить доступ только с localhost (уже настроено в docker-compose)
```

## 🐛 Troubleshooting

### Nginx не запускается

```bash
# Проверьте логи
docker-compose logs webserver

# Проверьте конфигурацию
docker exec -it <webserver_container_id> nginx -t

# Проверьте, что порты 80/443 свободны
sudo netstat -tulpn | grep :80
sudo netstat -tulpn | grep :443
```

### База данных не запускается

```bash
# Проверьте логи
docker-compose logs database

# Проверьте, что порт 3320 свободен
sudo netstat -tulpn | grep :3320

# Проверьте права на директории
ls -la ./
```

### API контейнер не может подключиться к БД

```bash
# Проверьте, что контейнеры в одной сети
docker network inspect nginx-proxy-man

# Проверьте строку подключения в GitHub Secrets
# Должна быть: Server=dbcontainer;Port=3306;Database=durakdb;User=user_write;Password=...

# Проверьте логи API
docker logs dapi
```

### Фронтенд показывает 404

```bash
# Проверьте, что файлы есть в ~/frontend
ls -la ~/frontend

# Проверьте симлинк
ls -la ./frontend

# Проверьте конфигурацию nginx
docker exec -it <webserver_container_id> cat /etc/nginx/conf.d/nginx.conf
```

## 📚 Дополнительные ресурсы

- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [Nginx Documentation](https://nginx.org/en/docs/)
- [Let's Encrypt Documentation](https://letsencrypt.org/docs/)
- [GitHub Actions Self-Hosted Runners](https://docs.github.com/en/actions/hosting-your-own-runners)

## 🔄 Процесс деплоя

### Деплой API (через GitHub Actions):

1. Разработчик запускает workflow "Publish Api" в GitHub
2. GitHub Actions собирает .NET приложение
3. Создается Docker образ с временной меткой
4. Старый контейнер `dapi` останавливается и удаляется
5. Запускается новый контейнер `dapi` с новым образом
6. Автоматически удаляются старые образы (оставляются 5 последних)

### Деплой фронтенда (через GitHub Actions):

1. Разработчик запускает workflow "Publish Angular" в GitHub
2. GitHub Actions собирает Angular приложение
3. Создается бэкап текущей версии в `~/frontend-backup`
4. Новые файлы копируются в `~/frontend`
5. Nginx автоматически начинает отдавать новые файлы

## 📝 Checklist перед первым запуском

- [ ] Docker и Docker Compose установлены
- [ ] Запущен скрипт `setup-vm.sh`
- [ ] Отредактирован файл `.env` с безопасными паролями
- [ ] Отредактирован `nginx/conf/nginx.conf` с вашим доменом
- [ ] Запущена инфраструктура: `docker-compose up -d`
- [ ] Настроен GitHub Actions self-hosted runner с label `teslowe`
- [ ] Добавлены секреты в GitHub (см. `.github/workflows/SECRETS_SETUP.md`)
- [ ] Настроен firewall на VM
- [ ] (Опционально) Получены SSL сертификаты от Let's Encrypt

## 🎉 Готово!

После выполнения всех шагов ваша инфраструктура готова к работе. Используйте GitHub Actions для деплоя API и фронтенда.
