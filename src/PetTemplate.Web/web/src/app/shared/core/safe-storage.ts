/**
 * Единственная дверь в `localStorage` и `sessionStorage` — продукта и админки.
 *
 * Хранилище бывает закрыто целиком: приватный режим Safari, запрет сторонних данных,
 * стороннему фрейму mini-app VK его могут не дать вовсе — тогда уже **обращение**
 * к `window.localStorage` бросает `SecurityError`. Голый вызов в сервисе ронял бы старт
 * приложения, а `try` в каждом месте расходился: где-то его забывали. Здесь отказ
 * ловится один раз, и значение живёт в памяти страницы до перезагрузки — интерфейс
 * работает, просто ничего не переживает перезагрузку.
 *
 * Прямой `localStorage.`/`sessionStorage.` вне этого файла отбивает `npm run lint:storage`
 * (`tools/storage-lint.mjs`). Спеки готовят состояние как хотят — проверка их не читает.
 * Исключение — инлайн-скрипт `index.html`: он идёт до старта Angular и ловит отказ сам.
 */
export type StorageArea = 'local' | 'session';

/** Запасное хранилище на случай отказа — своё у каждой области, как у браузера */
const memory: Record<StorageArea, Map<string, string>> = { local: new Map(), session: new Map() };

function area(kind: StorageArea): Storage | null {
  try {
    return kind === 'local' ? window.localStorage : window.sessionStorage;
  } catch {
    return null;
  }
}

export const safeStorage = {
  get(key: string, kind: StorageArea = 'local'): string | null {
    try {
      const value = area(kind)?.getItem(key);
      if (value != null) return value;
    } catch {
      // Отказ на чтение — смотрим в память
    }

    return memory[kind].get(key) ?? null;
  },

  set(key: string, value: string, kind: StorageArea = 'local'): void {
    try {
      const storage = area(kind);
      if (storage) {
        storage.setItem(key, value);
        memory[kind].delete(key);
        return;
      }
    } catch {
      // Квота или запрет — значение остаётся в памяти страницы
    }

    memory[kind].set(key, value);
  },

  remove(key: string, kind: StorageArea = 'local'): void {
    memory[kind].delete(key);

    try {
      area(kind)?.removeItem(key);
    } catch {
      // Удалять нечего: туда и не писалось
    }
  },

  /** Битая или чужая запись — `null`: разбор не роняет приложение */
  getJson<T>(key: string, kind: StorageArea = 'local'): T | null {
    const raw = safeStorage.get(key, kind);
    if (raw == null) return null;

    try {
      return JSON.parse(raw) as T;
    } catch {
      return null;
    }
  },

  setJson(key: string, value: unknown, kind: StorageArea = 'local'): void {
    safeStorage.set(key, JSON.stringify(value), kind);
  }
};
