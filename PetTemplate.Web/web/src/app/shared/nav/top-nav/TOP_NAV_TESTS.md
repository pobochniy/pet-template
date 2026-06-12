# Тесты для функционала выпадающего списка организаций

## Обзор

Созданы комплексные тесты для проверки функционала выбора организации в верхнем меню приложения.

## ⚠️ Важно: Zoneless Change Detection

Все тесты настроены для работы с **Experimental Zoneless Change Detection**, который используется в приложении. Каждый тестовый модуль включает:

```typescript
import { provideExperimentalZonelessChangeDetection } from '@angular/core';

TestBed.configureTestingModule({
  providers: [
    provideExperimentalZonelessChangeDetection(),
    // ... other providers
  ]
});
```

Это критически важно, так как компоненты используют **signals** для реактивности вместо Zone.js.

## Файлы тестов

### 1. `top-nav.component.spec.ts` - Unit тесты компонента

**Покрытие:**
- ✅ Сценарий 1: Пользователь без организаций
- ✅ Сценарий 2: Пользователь с одной организацией
- ✅ Сценарий 3: Пользователь с несколькими организациями
- ✅ Сценарий 4: Сохранение выбранной организации
- ✅ Обработка изменения организации
- ✅ Поведение при выходе из системы
- ✅ Граничные случаи (null, undefined ответы от API)

**Ключевые тесты:**

#### Сценарий 1: Нет организаций
```typescript
it('should hide organization selector when user has no organizations')
it('should not call organization API when user has no organization IDs')
```

#### Сценарий 2: Одна организация
```typescript
it('should show organization selector')
it('should disable organization selector when only one organization')
it('should auto-select the only available organization')
```

#### Сценарий 3: Несколько организаций
```typescript
it('should enable organization selector when multiple organizations')
it('should filter organizations based on user permissions')
it('should auto-select first organization when no saved preference')
```

#### Сценарий 4: Сохранение выбора
```typescript
it('should restore saved organization from localStorage')
it('should fallback to first organization if saved org is not in available list')
```

### 2. `top-nav.component.integration.spec.ts` - Интеграционные тесты

**Покрытие:**
- ✅ Рендеринг DOM элементов
- ✅ Взаимодействие пользователя с UI
- ✅ CSS классы и стилизация
- ✅ Доступность (accessibility)

**Ключевые тесты:**

#### DOM рендеринг
```typescript
it('should not render organization selector in DOM') // нет организаций
it('should have disabled attribute on select element') // одна организация
it('should render only organizations user has access to') // фильтрация
```

#### Взаимодействие
```typescript
it('should call onOrganizationChange when user changes selection')
it('should update context service when organization changes')
it('should navigate to profile when profile button is clicked')
it('should logout when logout button is clicked')
```

#### Доступность
```typescript
it('should have title attribute on profile button')
it('should have title attribute on logout button')
it('should have proper button type attributes')
```

### 3. `organization-context.service.spec.ts` - Тесты сервиса контекста

**Покрытие:**
- ✅ Инициализация сервиса
- ✅ Установка/получение ID организации
- ✅ Очистка текущей организации
- ✅ Работа с Observable
- ✅ Персистентность через localStorage
- ✅ Синхронизация между экземплярами сервиса

**Ключевые тесты:**

#### Инициализация
```typescript
it('should initialize with null when localStorage is empty')
it('should initialize with saved value from localStorage')
```

#### Управление состоянием
```typescript
it('should set organization ID and save to localStorage')
it('should remove from localStorage when set to null')
it('should emit new value through observable')
```

#### Персистентность
```typescript
it('should persist organization ID across service recreation')
it('should sync with localStorage changes')
```

### 4. `organization.interceptor.spec.ts` - Тесты HTTP интерцептора

**Покрытие:**
- ✅ Добавление заголовка X-Organization-Id
- ✅ Обработка различных HTTP методов (GET, POST, PUT, DELETE)
- ✅ Множественные запросы
- ✅ Сохранение оригинальных параметров запроса
- ✅ Граничные случаи
- ✅ Интеграция с OrganizationContextService

**Ключевые тесты:**

#### Инъекция заголовка
```typescript
it('should add X-Organization-Id header when organization is set')
it('should not add X-Organization-Id header when organization is null')
it('should add header to POST/PUT/DELETE requests')
```

#### Множественные запросы
```typescript
it('should add header to all concurrent requests')
it('should handle organization changes between requests')
```

#### Сохранение данных
```typescript
it('should preserve original request body')
it('should preserve original request URL')
it('should preserve other existing headers')
```

## Запуск тестов

### Все тесты
```bash
ng test
```

### Конкретный файл
```bash
ng test --include='**/top-nav.component.spec.ts'
ng test --include='**/organization-context.service.spec.ts'
ng test --include='**/organization.interceptor.spec.ts'
```

### С покрытием кода
```bash
ng test --code-coverage
```

## Покрытие функционала

| Компонент | Тесты | Покрытие |
|-----------|-------|----------|
| TopNavComponent | 25+ | Unit + Integration |
| OrganizationContextService | 15+ | Unit |
| OrganizationInterceptor | 20+ | Unit |
| **Всего** | **60+** | **Полное** |

## Тестируемые требования

### ✅ Требование 1: Видимость выпадающего списка
- Скрыт, если нет организаций
- Показан, если есть хотя бы одна организация

### ✅ Требование 2: Состояние disabled
- Disabled, если одна организация
- Enabled, если две и более организаций

### ✅ Требование 3: Фильтрация организаций
- Показываются только организации из `UserService.getOrganizationIds()`
- Данные берутся из `OrganizationApiService.getList()`

### ✅ Требование 4: Сохранение выбора
- Сохранение в localStorage
- Восстановление после перезахода
- Автовыбор первой организации при отсутствии сохраненной

### ✅ Требование 5: HTTP заголовок
- Автоматическое добавление `X-Organization-Id` ко всем запросам
- Использование значения из OrganizationContextService

### ✅ Требование 6: Очистка при выходе
- Очистка выбранной организации при logout

## Граничные случаи

Все тесты покрывают следующие граничные случаи:
- ❌ Null/undefined ответы от API
- ❌ Пустой массив организаций
- ❌ Несоответствие сохраненной организации доступным
- ❌ Изменение организации между запросами
- ❌ Пустая строка в качестве ID организации

## Моки и зависимости

Все внешние зависимости замокированы:
- `UserService` - управление пользователем
- `OrganizationApiService` - API организаций
- `OrganizationContextService` - контекст организации
- `AuthApiService` - аутентификация
- `Router` - навигация
- `ChatService` - чат
- `EventEmitterService` - события

## Рекомендации по поддержке

1. **При добавлении нового функционала** - добавить соответствующие тесты
2. **При изменении логики** - обновить существующие тесты
3. **Перед коммитом** - убедиться, что все тесты проходят
4. **Минимальное покрытие** - 80% для новых компонентов

## Примеры использования

### Запуск тестов в watch режиме
```bash
ng test --watch
```

### Запуск с детальным выводом
```bash
ng test --browsers=ChromeHeadless --watch=false
```

### Генерация отчета о покрытии
```bash
ng test --no-watch --code-coverage
open coverage/index.html
```
