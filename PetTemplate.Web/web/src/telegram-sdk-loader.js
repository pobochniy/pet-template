// Загрузчик Telegram WebApp SDK для production
// Этот файл загружается только в production сборке

(function() {
  const script = document.createElement('script');
  script.src = 'https://telegram.org/js/telegram-web-app.js';
  script.async = false;
  document.head.appendChild(script);
  
  console.log('[Telegram SDK] Loading official Telegram WebApp SDK');
})();
