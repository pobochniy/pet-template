#!/bin/bash

# Скрипт для подготовки виртуальной машины для проекта PetTemplate
# Запуск: chmod +x setup-vm.sh && ./setup-vm.sh

set -e  # Остановка при ошибке

echo "🚀 Начинаем подготовку виртуальной машины для проекта PetTemplate..."
echo ""

# Цвета для вывода
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Функция для вывода успешного сообщения
success() {
    echo -e "${GREEN}✅ $1${NC}"
}

# Функция для вывода предупреждения
warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

# Функция для вывода ошибки
error() {
    echo -e "${RED}❌ $1${NC}"
}

# Создаёт каталог, если его нет
ensure_dir() {
    if [ ! -d "$1" ]; then
        mkdir -p "$1"
        success "Создана директория: $1"
    else
        warning "Директория $1 уже существует"
    fi
}

# Симлинк из IaC на каталог в домашней папке: туда пишут workflow выкатки,
# а nginx видит его томом из docker-compose.yml
ensure_link() {
    local link="$1" target="$2"
    if [ -L "$link" ]; then
        warning "Симлинк $link уже существует"
    elif [ -d "$link" ]; then
        warning "Директория $link существует (не симлинк). Пропускаем создание симлинка."
    else
        ln -s "$target" "$link"
        success "Создан симлинк: $link -> $target"
    fi
}

# Проверка, что скрипт запущен не от root
if [ "$EUID" -eq 0 ]; then
    error "Не запускайте этот скрипт от root. Используйте обычного пользователя."
    exit 1
fi

echo "📦 Шаг 1: Проверка установленных зависимостей..."
echo ""

# Проверка Docker
if ! command -v docker &> /dev/null; then
    error "Docker не установлен. Установите Docker и повторите попытку."
    echo "Инструкция: https://docs.docker.com/engine/install/"
    exit 1
else
    success "Docker установлен: $(docker --version)"
fi

# Проверка Docker Compose
if ! docker compose version &> /dev/null; then
    error "Docker Compose v2 не установлен. Установите плагин docker compose и повторите попытку."
    echo "Инструкция: https://docs.docker.com/compose/install/"
    exit 1
else
    success "Docker Compose установлен: $(docker compose version)"
fi

# Проверка прав на Docker: workflow на раннере зовут docker без sudo
if ! docker ps &> /dev/null; then
    error "У текущего пользователя нет прав на использование Docker."
    echo "Выполните: sudo usermod -aG docker $USER"
    echo "Затем перелогиньтесь и запустите скрипт снова."
    exit 1
else
    success "Права на Docker настроены корректно"
fi

echo ""
echo "📁 Шаг 2: Создание необходимых директорий..."
echo ""

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Сборки клиента и админки и их предыдущие версии (спец. infrastructure, 7)
ensure_dir "$HOME/frontend"
ensure_dir "$HOME/frontend-backup"
ensure_dir "$HOME/adminka"
ensure_dir "$HOME/adminka-backup"

ensure_link "$SCRIPT_DIR/frontend" "$HOME/frontend"
ensure_link "$SCRIPT_DIR/adminka" "$HOME/adminka"

# Создание директорий для certbot
ensure_dir "$SCRIPT_DIR/certbot/www"
ensure_dir "$SCRIPT_DIR/certbot/conf"

echo ""
echo "🌐 Шаг 3: Создание Docker сети..."
echo ""

# Создание Docker сети nginx-proxy-man: в ней nginx, база и контейнер API
if docker network ls --format '{{.Name}}' | grep -qx "nginx-proxy-man"; then
    warning "Docker сеть 'nginx-proxy-man' уже существует"
else
    docker network create nginx-proxy-man
    success "Создана Docker сеть: nginx-proxy-man"
fi

echo ""
echo "🔧 Шаг 4: Проверка конфигурационных файлов..."
echo ""

# Проверка наличия docker-compose.yml
if [ ! -f "$SCRIPT_DIR/docker-compose.yml" ]; then
    error "Файл docker-compose.yml не найден в $SCRIPT_DIR"
    exit 1
else
    success "Найден файл: docker-compose.yml"
fi

# Проверка наличия nginx конфигурации
if [ ! -f "$SCRIPT_DIR/nginx/conf/nginx.conf" ]; then
    error "Файл nginx/conf/nginx.conf не найден"
    exit 1
else
    success "Найден файл: nginx/conf/nginx.conf"
fi

# Скрипт, который заводит пользователя приложения в Postgres на пустом томе db-data:
# без него API останется без своей роли (IaC/DEPLOYMENT.md, «Пользователь приложения»)
if [ ! -f "$SCRIPT_DIR/postgres/init/01-app-user.sh" ]; then
    error "Файл postgres/init/01-app-user.sh не найден"
    exit 1
else
    success "Найден файл: postgres/init/01-app-user.sh"
fi

# Второй скрипт пустого тома — пользователь postgres-exporter: без него метрики базы молчат
# (спец. infrastructure, 10)
if [ ! -f "$SCRIPT_DIR/postgres/init/02-exporter-user.sh" ]; then
    error "Файл postgres/init/02-exporter-user.sh не найден"
    exit 1
else
    success "Найден файл: postgres/init/02-exporter-user.sh"
fi

echo ""
echo "🔐 Шаг 5: Настройка переменных окружения..."
echo ""

# Проверка/создание .env файла для docker-compose. Текст — тот же, что в .env.example:
# правите одно — правьте и другое
ENV_FILE="$SCRIPT_DIR/.env"
if [ ! -f "$ENV_FILE" ]; then
    cat > "$ENV_FILE" << 'EOF'
# Database configuration. Пароли читаются только при первом старте Postgres на пустом
# томе db-data (IaC/DEPLOYMENT.md, раздел 2)
POSTGRES_DB=pettemplatedb
POSTGRES_USER=postgres
POSTGRES_PASSWORD=change_me_postgres_password
# Пользователь приложения — под ним ходит API (секрет API_DB_CONNECTION_STRING), только DML
APP_DB_USER=pettemplate_app
APP_DB_PASSWORD=change_me_app_password
# Владелец базы — под ним выкатка накатывает миграции (секрет MIGRATIONS_DB_CONNECTION_STRING)
APP_DB_OWNER=pettemplate_owner
APP_DB_OWNER_PASSWORD=change_me_owner_password

# Порты
# DB_PORT=5432   # только если раскомментирован ports у database (на 127.0.0.1)
NGINX_HTTP_PORT=80
NGINX_HTTPS_PORT=443
EOF
    chmod 600 "$ENV_FILE"
    success "Создан файл .env с дефолтными значениями"
    warning "ВАЖНО: Отредактируйте файл $ENV_FILE и установите безопасные пароли!"
else
    warning "Файл .env уже существует. Проверьте его содержимое."
fi

echo ""
echo "📊 Шаг 6: Проверка системных требований..."
echo ""

# Проверка свободного места на диске
AVAILABLE_SPACE=$(df -h "$HOME" | awk 'NR==2 {print $4}')
success "Доступное место на диске: $AVAILABLE_SPACE"

# Проверка памяти
TOTAL_MEM=$(free -h | awk 'NR==2 {print $2}')
success "Общая память: $TOTAL_MEM"

echo ""
echo "✅ Подготовка виртуальной машины завершена!"
echo ""
echo "📝 Следующие шаги:"
echo ""
echo "1. Отредактируйте файл $ENV_FILE и установите безопасные пароли (POSTGRES_PASSWORD, APP_DB_PASSWORD)"
echo "2. Запустите инфраструктуру: docker compose up -d"
echo "3. Получите сертификат (IaC/DEPLOYMENT.md, раздел 4)"
echo "4. Настройте GitHub Actions self-hosted runner (IaC/DEPLOYMENT.md, раздел 5)"
echo "5. Добавьте секреты в GitHub (см. IaC/DEPLOYMENT.md, раздел «Секреты GitHub Actions»)"
echo ""
echo "📚 Полезные команды:"
echo ""
echo "  # Запуск инфраструктуры"
echo "  docker compose up -d"
echo ""
echo "  # Просмотр логов"
echo "  docker compose logs -f"
echo ""
echo "  # Остановка инфраструктуры (без -v: том базы db-data не трогать)"
echo "  docker compose down"
echo ""
echo "  # Проверка статуса контейнеров"
echo "  docker ps"
echo ""
echo "  # Просмотр логов API контейнера (после деплоя через GitHub Actions)"
echo "  docker logs dapi"
echo ""
echo "🎉 Готово! Удачи с деплоем!"
