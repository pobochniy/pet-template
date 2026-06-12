// Mock для Telegram WebApp API (только для разработки)
// В production этот скрипт не загружается, вместо него Telegram предоставляет свой SDK

if (!window.Telegram) {
  // Создаем mock данные пользователя
  const mockUser = {
    id: 123456789,
    first_name: 'DevUser',
    username: 'devuser',
    language_code: 'ru'
  };

  const authDate = Math.floor(Date.now() / 1000);
  
  // Формируем initData строку (как это делает Telegram)
  const initDataString = `user=${encodeURIComponent(JSON.stringify(mockUser))}&auth_date=${authDate}&hash=mock_hash_for_development`;

  // Создаем mock объект Telegram WebApp
  window.Telegram = {
    WebApp: {
      initData: initDataString,
      initDataUnsafe: {
        user: mockUser,
        auth_date: authDate,
        hash: 'mock_hash_for_development'
      },
      version: '7.0',
      platform: 'web',
      ready: () => console.log('[Mock Telegram] ready()'),
      expand: () => console.log('[Mock Telegram] expand()'),
      close: () => console.log('[Mock Telegram] close()')
    }
  };

  console.log('[Mock Telegram] Initialized with mock data:', mockUser);
}
