#!/bin/bash

# Скрипт для подготовки виртуальной машины для проекта PetTemplate
# Запуск: bash setup-vm.sh — через bash, а не ./: `dotnet new` не сохраняет бит
# исполнения (спец. infrastructure, 13)

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

# Сборки клиента и админки: releases/<sha> и ссылки current/previous в них заводит
# выкатка (IaC/publish-static.sh, спец. infrastructure, 7)
ensure_dir "$HOME/frontend"
ensure_dir "$HOME/adminka"

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

# Проверка наличия nginx конфигурации: вход и сайты, которые он подключает (nginx/README.md)
if [ ! -f "$SCRIPT_DIR/nginx/conf/nginx.conf" ] || ! ls "$SCRIPT_DIR"/nginx/conf/sites/*.conf > /dev/null 2>&1; then
    error "Нет nginx/conf/nginx.conf или сайтов в nginx/conf/sites/ — отвезите каталог nginx/conf целиком"
    exit 1
else
    success "Найдена конфигурация nginx: nginx.conf и сайты в sites/"
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

# Бит исполнения: `dotnet new` его не сохраняет, а образ postgres неисполняемый *.sh
# из /docker-entrypoint-initdb.d не запускает, а подключает в свой shell — и `exit 0`
# в 02-exporter-user.sh (пароля нет) оборвал бы первичную инициализацию тома
chmod +x "$SCRIPT_DIR"/postgres/init/*.sh "$SCRIPT_DIR"/*.sh
success "Скрипты postgres/init и IaC исполняемые"

# Страховка срока окна админки (tech-debt/admin-perimeter.md): просроченное открытие
# закрывает не только запуск `Admin Access` по расписанию на раннере, но и cron самой VM —
# раннер лёг или GitHub отложил расписание, а окно всё равно закроется. Вывод — в журнал
# системы (`journalctl -t admin-access`), он ротируется сам. Запись одна: повторный запуск
# setup-vm.sh заменяет её, а не дописывает вторую
CRON_LINE="*/5 * * * * bash $SCRIPT_DIR/admin-access.sh expire 2>&1 | logger -t admin-access"
if command -v crontab >/dev/null 2>&1; then
    { crontab -l 2>/dev/null | grep -v 'admin-access.sh expire' || true; echo "$CRON_LINE"; } | crontab -
    success "cron закрывает просроченное окно админки раз в 5 минут"
else
    warning "crontab не найден — окно админки закроет только запуск Admin Access по расписанию"
fi

echo ""
echo "🔐 Шаг 5: Настройка переменных окружения..."
echo ""

# Проверка/создание .env файла для docker-compose
ENV_FILE="$SCRIPT_DIR/.env"
if [ ! -f "$ENV_FILE" ]; then
    # Из .env.example: один шаблон, а не копия текстом здесь — копия расходится первой
    cp "$SCRIPT_DIR/.env.example" "$ENV_FILE"
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
echo "1. Отредактируйте файл $ENV_FILE и установите безопасные пароли (POSTGRES_PASSWORD, APP_DB_PASSWORD, APP_DB_OWNER_PASSWORD, POSTGRES_EXPORTER_PASSWORD)"
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
