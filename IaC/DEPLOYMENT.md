# 🚀 Руководство по деплою проекта PetTemplate

## Быстрый старт

### 1. Подготовка виртуальной машины

```bash
# Скопируйте папку IaC на VM
scp -r IaC user@your-vm:/home/user/

# Подключитесь к VM
ssh user@your-vm

# Запустите скрипт подготовки
cd ~/IaC
chmod +x setup-vm.sh
./setup-vm.sh
```

### 2. Настройка переменных окружения

```bash
# Отредактируйте .env
nano ~/IaC/.env

# Установите безопасные пароли:
# - MYSQL_ROOT_PASSWORD
# - MYSQL_PASSWORD
```

### 3. Настройка Nginx

```bash
# Отредактируйте nginx.conf
nano ~/IaC/nginx/conf/nginx.conf

# Замените 'your-domain.com' на ваш домен
```

### 4. Запуск инфраструктуры

```bash
cd ~/IaC
docker-compose up -d
```

### 5. Настройка GitHub Actions Runner

```bash
mkdir ~/actions-runner && cd ~/actions-runner
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L \
  https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz
./config.sh --url https://github.com/YOUR_USERNAME/PetTemplate --token YOUR_TOKEN --labels teslowe
sudo ./svc.sh install
sudo ./svc.sh start
```

### 6. Добавление секретов в GitHub

1. Перейдите в Settings → Secrets and variables → Actions
2. Добавьте секрет `API_DB_CONNECTION_STRING`:
   ```
   Server=dbcontainer;Port=3306;Database=durakdb;User=user_write;Password=YOUR_PASSWORD;
   ```

### 7. Деплой через GitHub Actions

- **API**: Actions → Publish Api → Run workflow
- **Frontend**: Actions → Publish Angular → Run workflow

## 📚 Подробная документация

- **Инфраструктура**: [IaC/README.md](IaC/README.md)
- **GitHub Actions**: [.github/README.md](.github/README.md)
- **Настройка секретов**: [.github/workflows/SECRETS_SETUP.md](.github/workflows/SECRETS_SETUP.md)

## 🔧 Полезные команды

```bash
# Просмотр логов API
docker logs -f dapi

# Просмотр логов инфраструктуры
cd ~/IaC && docker-compose logs -f

# Проверка статуса
docker ps

# Откат API на предыдущую версию
# Actions → Rollback Api → Run workflow

# Откат фронтенда
rm -rf ~/frontend/* && cp -r ~/frontend-backup/* ~/frontend/
```

