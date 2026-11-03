import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorText } from './api-error';

describe('apiErrorText', () => {
  it('should show the server text as is', () => {
    const error = new HttpErrorResponse({ status: 400, error: { error: 'Модель не найдена' } });

    expect(apiErrorText(error)).toBe('Модель не найдена');
  });

  it('should fall back to the status when the body is empty', () => {
    const error = new HttpErrorResponse({ status: 404, statusText: 'Not Found', error: null });

    expect(apiErrorText(error)).toBe('404 Not Found');
  });
});
