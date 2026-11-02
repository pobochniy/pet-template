import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { SessionResetService } from './session-reset.service';

describe('SessionResetService', () => {
  let service: SessionResetService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection()]
    });

    service = TestBed.inject(SessionResetService);
  });

  it('should run handlers one after another', async () => {
    const calls: string[] = [];
    service.register(async () => {
      await Promise.resolve();
      calls.push('first');
    });
    service.register(() => {
      calls.push('second');
    });

    await service.run();

    expect(calls).toEqual(['first', 'second']);
  });

  it('should keep cleaning up after a failing handler', async () => {
    const cleaned = jasmine.createSpy('cleaned');
    spyOn(console, 'error');
    service.register(() => Promise.reject(new Error('not saved')));
    service.register(cleaned);

    // Выход не должен срываться из-за неудачной уборки одного сервиса
    await expectAsync(service.run()).toBeResolved();
    expect(cleaned).toHaveBeenCalled();
  });

  it('should forget an unregistered handler', async () => {
    const handler = jasmine.createSpy('handler');
    const unregister = service.register(handler);

    unregister();
    await service.run();

    expect(handler).not.toHaveBeenCalled();
  });
});
