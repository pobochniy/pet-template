import { DestroyRef, PendingTasks, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { SessionContext, UserService } from './user.service';

/** Что получает загрузчик: пространство и пользователь, для которых он грузит, и отмена */
export interface ContextLoad<P> {
  readonly workspaceId: string;
  readonly userId: string | null;
  readonly params: P;
  readonly abortSignal: AbortSignal;
}

export interface ContextResourceOptions<T, P> {
  /**
   * Свои параметры экрана поверх контекста (право, фильтр). `undefined` — грузить
   * нечего: ресурс отдаёт `defaultValue`, запроса нет
   */
  readonly params?: () => P | undefined;
  readonly loader: (load: ContextLoad<P>) => Promise<T>;
  readonly defaultValue: T;
}

export type ContextResourceStatus = 'idle' | 'loading' | 'resolved' | 'error';

/** Чтение экрана: значение, состояние и ошибка — сигналами, перечитать — `reload()` */
export interface ContextResource<T> {
  /** Ответ для **текущего** контекста; пока он грузится или не пришёл — `defaultValue` */
  readonly value: Signal<T>;
  readonly status: Signal<ContextResourceStatus>;
  readonly error: Signal<unknown>;
  readonly isLoading: Signal<boolean>;
  /** Перечитать тот же контекст — после команды, которая его поменяла */
  reload(): void;
  /** Поправить значение на месте — ответ команды уже известен, перечитывать незачем */
  set(value: T): void;
}

/** Сигнал экрана, который сам сбрасывается в начальное значение при смене контекста вкладки */
export interface ContextSignal<T> extends Signal<T> {
  set(value: T): void;
}

interface Request<P> {
  readonly workspaceId: string;
  readonly userId: string | null;
  readonly params: P;
  readonly attempt: number;
}

interface State<T, P> {
  readonly request: Request<P> | undefined;
  readonly status: ContextResourceStatus;
  readonly value: T;
  readonly error: unknown;
}

/**
 * Чтение экрана, привязанное к контексту вкладки — пользователю и активному
 * пространству (`UserService.context`). Образец: `workspace/members`.
 *
 * Сменили пространство в шапке или вошли другим в соседней вкладке — прежний запрос
 * обрывается (`abortSignal` доходит до `HttpClient` через `BaseApiService`), значение
 * сразу сбрасывается в `defaultValue`, и грузится новое. Ответ про прошлый контекст
 * в экран не ляжет: значение отдаётся, только если оно получено для текущего запроса —
 * сверка синхронная, без окна между сменой и перезапуском. Без активного пространства
 * запроса нет вовсе (а без входа нет и пространства).
 *
 * Правило: **чтение — `contextResource`, команды — `async/await`**; после команды
 * экран зовёт `reload()`.
 *
 * Почему не `resource()` из `@angular/core`, хотя смысл тот же: его код (с `linkedSignal`)
 * сборщик кладёт в общий с оболочкой чанк — +5 КБ к initial, половина запаса бюджета
 * (`tech-debt/bundle.md`). Здесь — та же модель на `signal`/`computed`/`effect`, которые
 * в initial и так есть. Ожидание загрузки учитывается в `PendingTasks`, как у `resource()`:
 * `whenStable()` в спеках дожидается ответа
 */
export function contextResource<T, P = true>(options: ContextResourceOptions<T, P>): ContextResource<T> {
  const user = inject(UserService);
  const pendingTasks = inject(PendingTasks);
  const attempt = signal(0);

  const request = computed<Request<P> | undefined>(
    () => {
      const { userId, workspaceId } = user.context();
      if (workspaceId == null) return undefined;

      const params = options.params ? options.params() : (true as P);
      if (params === undefined) return undefined;

      return { userId, workspaceId, params, attempt: attempt() };
    },
    { equal: sameRequest }
  );

  const state = signal<State<T, P>>({ request: undefined, status: 'idle', value: options.defaultValue, error: undefined });

  /** Состояние, только если оно про текущий запрос: иначе — «грузится» с пустым значением */
  const current = computed<State<T, P>>(() => {
    const req = request();
    const known = state();
    if (known.request === req) return known;
    if (known.request && req && sameContext(known.request, req)) return { ...known, request: req, status: 'loading', error: undefined };

    return { request: req, status: req ? 'loading' : 'idle', value: options.defaultValue, error: undefined };
  });

  let controller: AbortController | null = null;

  effect(() => {
    const req = request();

    untracked(() => {
      controller?.abort();
      controller = null;

      if (req === undefined) {
        state.set({ request: undefined, status: 'idle', value: options.defaultValue, error: undefined });
        return;
      }

      const mine = (controller = new AbortController());
      // Перечитывание того же контекста держит прежнее значение на экране до ответа
      const previous = untracked(state);
      const keep = previous.request != null && previous.request.attempt !== req.attempt
        && sameContext(previous.request, req) ? previous.value : options.defaultValue;
      state.set({ request: req, status: 'loading', value: keep, error: undefined });

      const done = pendingTasks.add();
      options.loader({ workspaceId: req.workspaceId, userId: req.userId, params: req.params, abortSignal: mine.signal })
        .then(
          value => {
            if (!mine.signal.aborted && untracked(request) === req) {
              state.set({ request: req, status: 'resolved', value, error: undefined });
            }
          },
          (error: unknown) => {
            if (!mine.signal.aborted && untracked(request) === req) {
              state.set({ request: req, status: 'error', value: options.defaultValue, error });
            }
          }
        )
        .finally(done);
    });
  });

  inject(DestroyRef).onDestroy(() => controller?.abort());

  return {
    value: computed(() => current().value),
    status: computed(() => current().status),
    error: computed(() => current().error),
    isLoading: computed(() => current().status === 'loading'),
    reload: () => attempt.update(n => n + 1),
    set: value => state.set({ request: untracked(request), status: 'resolved', value, error: undefined })
  };
}

/**
 * Состояние экрана, привязанное к контексту: раскрытая строка, ждущее подтверждения
 * удаление, только что выданная ссылка. Сменили пространство — значение снова начальное,
 * без эффекта, который пришлось бы пропускать «в первый раз». Тот же смысл, что у
 * `linkedSignal({ source: context, computation: () => initial })`, но без его кода
 * в initial-бандле (см. `contextResource`)
 */
export function contextSignal<T>(initial: T): ContextSignal<T> {
  const context = inject(UserService).context;
  const state = signal<{ readonly context: SessionContext; readonly value: T }>({ context: untracked(context), value: initial });

  const read = computed(() => {
    const current = state();
    return current.context === context() ? current.value : initial;
  });

  return Object.assign(read, { set: (value: T) => state.set({ context: untracked(context), value }) });
}

function sameContext<P>(a: Request<P>, b: Request<P>): boolean {
  return a.workspaceId === b.workspaceId && a.userId === b.userId && Object.is(a.params, b.params);
}

function sameRequest<P>(a: Request<P> | undefined, b: Request<P> | undefined): boolean {
  if (a === undefined || b === undefined) return a === b;

  return sameContext(a, b) && a.attempt === b.attempt;
}
