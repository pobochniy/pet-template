// Иконка lucide поштучно: у пакета типы только на общий вход, а поштучный модуль отдаёт
// данные иконки по умолчанию (`dist/esm/icons/<имя>.mjs`). Зачем поштучно — icon.component.ts
declare module 'lucide/dist/esm/icons/*.mjs' {
  const icon: import('lucide').IconNode;
  export default icon;
}
