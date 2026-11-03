import { compareStrings, formatDate, weekdayNames } from './format.util';

/**
 * Главное здесь — что `en` уходит в `Intl` как `en-GB` (`formatters.ts`,
 * `INTL_LOCALES`). Голый `en` — это `en-US`: короткая дата становится
 * месяц-день, и `1/5` перестаёт читаться однозначно
 */
describe('format.util', () => {
  // 5 января 2026, 15:45 — день и месяц различимы, порядок в короткой дате виден
  const date = new Date(2026, 0, 5, 15, 45);

  describe('formatDate', () => {
    it('should use British conventions for en', () => {
      expect(formatDate('en', date, { dateStyle: 'short' })).toBe('05/01/2026');
      expect(formatDate('en', date, { dateStyle: 'medium' })).toBe('5 Jan 2026');
    });

    it('should use 24-hour time for en', () => {
      expect(formatDate('en', date, { timeStyle: 'short' })).toBe('15:45');
    });

    it('should put the day first for ru too', () => {
      expect(formatDate('ru', date, { dateStyle: 'short' })).toBe('05.01.2026');
    });

    it('should be an empty string for an empty or unparsable value', () => {
      expect(formatDate('en', null)).toBe('');
      expect(formatDate('en', undefined)).toBe('');
      expect(formatDate('en', '')).toBe('');
      expect(formatDate('en', 'не дата')).toBe('');
    });

    it('should accept a string and a timestamp', () => {
      expect(formatDate('en', '2026-01-05T15:45:00', { dateStyle: 'short' })).toBe('05/01/2026');
      expect(formatDate('en', date.getTime(), { dateStyle: 'short' })).toBe('05/01/2026');
    });
  });

  describe('weekdayNames', () => {
    it('should be indexed by Date.getDay(), Sunday first', () => {
      expect(weekdayNames('en', 'long')[0]).toBe('Sunday');
      expect(weekdayNames('en', 'long')[1]).toBe('Monday');
    });

    it('should follow the interface language', () => {
      expect(weekdayNames('ru', 'long')[1]).toBe('понедельник');
    });
  });

  describe('compareStrings', () => {
    it('should ignore case and sort numbers as numbers', () => {
      expect(compareStrings('en', 'apple', 'Apple')).toBe(0);
      expect(compareStrings('en', 'item 2', 'item 10')).toBeLessThan(0);
    });

    it('should follow the alphabet of the interface language', () => {
      expect(compareStrings('ru', 'ёлка', 'яблоко')).toBeLessThan(0);
    });
  });
});
