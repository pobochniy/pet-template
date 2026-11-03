import { HttpErrorResponse } from '@angular/common/http';
import { ErrorTranslator, serverErrorMessage } from './server-error.util';

/** Какой текст отказа сервера видит человек: перевод кода, текст из тела или своя подпись */
describe('serverErrorMessage', () => {
  const translator: ErrorTranslator = {
    translateCode: code => code === 'auth.error.known' ? 'Переведено' : null
  };

  const response = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

  it('should translate a known code', () => {
    expect(serverErrorMessage(response(400, { code: 'auth.error.known', error: 'text' }), translator, 'fallback'))
      .toBe('Переведено');
  });

  it('should show the text of a 4xx refusal without a translated code', () => {
    expect(serverErrorMessage(response(400, { error: 'Текст отказа' }), translator, 'fallback')).toBe('Текст отказа');
  });

  /** Тело пятисотки — общий текст и traceId для журнала, а не объяснение человеку */
  it('should show the fallback on a server failure', () => {
    expect(serverErrorMessage(response(500, { error: 'Internal server error', traceId: 'abc' }), translator, 'fallback'))
      .toBe('fallback');
  });
});
