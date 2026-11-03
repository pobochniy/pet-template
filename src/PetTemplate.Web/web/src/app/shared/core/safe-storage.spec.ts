import { safeStorage } from './safe-storage';

describe('safeStorage', () => {
  const key = 'safe-storage-spec';

  afterEach(() => {
    safeStorage.remove(key);
    safeStorage.remove(key, 'session');
  });

  it('should read back what it wrote in the browser storage', () => {
    safeStorage.set(key, 'value');
    safeStorage.setJson(key, { a: 1 }, 'session');

    expect(localStorage.getItem(key)).toBe('value');
    expect(safeStorage.get(key)).toBe('value');
    expect(safeStorage.getJson<{ a: number }>(key, 'session')).toEqual({ a: 1 });
  });

  it('should answer null to a broken JSON record instead of throwing', () => {
    localStorage.setItem(key, '{broken');

    expect(safeStorage.getJson(key)).toBeNull();
  });

  // Фрейм mini-app VK, приватный режим: хранилище отказывает — значение живёт в памяти страницы
  it('should keep the value in memory when the storage refuses', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied', 'SecurityError');
    });

    expect(() => safeStorage.set(key, 'in-memory')).not.toThrow();
    expect(safeStorage.get(key)).toBe('in-memory');

    safeStorage.remove(key);
    expect(safeStorage.get(key)).toBeNull();
  });

  it('should keep local and session areas apart', () => {
    safeStorage.set(key, 'local');
    safeStorage.set(key, 'session', 'session');

    expect(safeStorage.get(key)).toBe('local');
    expect(safeStorage.get(key, 'session')).toBe('session');
  });
});
