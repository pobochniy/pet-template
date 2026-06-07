#!/bin/bash

# Скрипт для подготовки виртуальной машины для проекта Durak
# Запуск: chmod +x setup-vm.sh && ./setup-vm.sh

set -e  # Остановка при ошибке

echo "🚀 Начинаем подготовку виртуальной машины для проекта Durak..."
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
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    error "Docker Compose не установлен. Установите Docker Compose и повторите попытку."
    echo "Инструкция: https://docs.docker.com/compose/install/"
    exit 1
else
    if command -v docker-compose &> /dev/null; then
        success "Docker Compose установлен: $(docker-compose --version)"
    else
        success "Docker Compose установлен: $(docker compose version)"
    fi
fi

# Проверка прав на Docker
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

# Создание директорий для фронтенда
if [ ! -d "$HOME/frontend" ]; then
    mkdir -p "$HOME/frontend"
    success "Создана директория: $HOME/frontend"
else
    warning "Директория $HOME/frontend уже существует"
fi

if [ ! -d "$HOME/frontend-backup" ]; then
    mkdir -p "$HOME/frontend-backup"
    success "Создана директория: $HOME/frontend-backup"
else
    warning "Директория $HOME/frontend-backup уже существует"
fi

# Создание симлинка из IaC/frontend на ~/frontend
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
FRONTEND_LINK="$SCRIPT_DIR/frontend"

if [ -L "$FRONTEND_LINK" ]; then
    warning "Симлинк $FRONTEND_LINK уже существует"
elif [ -d "$FRONTEND_LINK" ]; then
    warning "Директория $FRONTEND_LINK существует (не симлинк). Пропускаем создание симлинка."
else
    ln -s "$HOME/frontend" "$FRONTEND_LINK"
    success "Создан симлинк: $FRONTEND_LINK -> $HOME/frontend"
fi

# Создание директорий для certbot
if [ ! -d "$SCRIPT_DIR/certbot/www" ]; then
    mkdir -p "$SCRIPT_DIR/certbot/www"
    success "Создана директория: $SCRIPT_DIR/certbot/www"
fi

if [ ! -d "$SCRIPT_DIR/certbot/conf" ]; then
    mkdir -p "$SCRIPT_DIR/certbot/conf"
    success "Создана директория: $SCRIPT_DIR/certbot/conf"
fi

echo ""
echo "🌐 Шаг 3: Создание Docker сети..."
echo ""

# Создание Docker сети nginx-proxy-man
if docker network ls | grep -q "nginx-proxy-man"; then
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

echo ""
echo "🔐 Шаг 5: Настройка переменных окружения..."
echo ""

# Проверка/создание .env файла для docker-compose
ENV_FILE="$SCRIPT_DIR/.env"
if [ ! -f "$ENV_FILE" ]; then
    cat > "$ENV_FILE" << 'EOF'
# Database configuration
MYSQL_ROOT_PASSWORD=change_me_root_password
MYSQL_DATABASE=durakdb
MYSQL_USER=user_write
MYSQL_PASSWORD=change_me_user_password

# Порты
DB_PORT=3320
NGINX_HTTP_PORT=80
NGINX_HTTPS_PORT=443
EOF
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
echo "1. Отредактируйте файл $ENV_FILE и установите безопасные пароли"
echo "2. Отредактируйте файл nginx/conf/nginx.conf и укажите ваш домен"
echo "3. Отредактируйте файл docker-compose.yml если нужно изменить конфигурацию"
echo "4. Запустите инфраструктуру: docker compose up -d"
echo "5. Настройте GitHub Actions self-hosted runner с label 'teslowe'"
echo "6. Добавьте секреты в GitHub (см. .github/workflows/SECRETS_SETUP.md)"
echo ""
echo "📚 Полезные команды:"
echo ""
echo "  # Запуск инфраструктуры"
echo "  docker-compose up -d"
echo ""
echo "  # Просмотр логов"
echo "  docker-compose logs -f"
echo ""
echo "  # Остановка инфраструктуры"
echo "  docker-compose down"
echo ""
echo "  # Проверка статуса контейнеров"
echo "  docker ps"
echo ""
echo "  # Просмотр логов API контейнера (после деплоя через GitHub Actions)"
echo "  docker logs dapi"
echo ""
echo "🎉 Готово! Удачи с деплоем!"
