import {
  ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, Input, OnInit, inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';
import { I18nService } from '../i18n/i18n.service';
import { I18nKey } from '../i18n/generated/i18n-keys';
import { formatNumber } from '../i18n/format.util';
import {
  ServerValidationError, asServerValidationErrors, serverValidationText
} from '../core/server-validation.util';

/**
 * Ошибки одного поля формы.
 *
 * Вход — **ключ** подписи поля, а не готовый текст: тот же ключ ставится
 * в `<label>` родительского шаблона — одна константа на поле и в подписи,
 * и в ошибке.
 *
 * Серверные ошибки приходят сюда **сырыми** (`{code, params, text}`) и переводятся
 * на каждой отрисовке: резолвить их в строку при прикреплении нельзя — уже
 * показанные ошибки остались бы на старом языке после переключения.
 *
 * Перерисовку компонент запрашивает сам, подпиской на `events` контрола. Приложение
 * работает без Zone.js, а сам компонент — `OnPush`: ни `markAsDirty()` на отправке
 * формы, ни появление ошибок вход `model` не меняют (ссылка на контрол та же),
 * и без явного `markForCheck()` проверка сюда просто не доходила.
 */
@Component({
  selector: 'form-validation',
  templateUrl: './form-validation.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FormValidationComponent implements OnInit {
  @Input() model!: AbstractControl;

  /** Ключ подписи поля: `core.field.password` для общих, `{scope}.field.*` для своих */
  @Input() fieldKey!: I18nKey;

  protected readonly i18n = inject(I18nService);
  private readonly cdRef = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  protected get fieldLabel(): string {
    return this.i18n.t(this.fieldKey);
  }

  ngOnInit(): void {
    // `events` — все изменения контрола разом: значение, статус, dirty и touched.
    // Отдельных valueChanges/statusChanges мало: отправка пустой формы меняет
    // только dirty/touched, а именно по ним решается, показывать ли ошибку
    this.model?.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.cdRef.markForCheck());
  }

  /** Граница `Validators.min`/`max` — с разрядами языка интерфейса */
  protected number(value: number): string {
    return formatNumber(this.i18n.lang(), value);
  }

  /** Сырые ошибки сервера, прикреплённые к полю разбором 422 */
  protected serverErrors(): ServerValidationError[] {
    return asServerValidationErrors(this.model?.errors?.['serverError']);
  }

  protected serverErrorText(error: ServerValidationError): string {
    return serverValidationText(error, (code, params) => this.i18n.translateCode(code, params));
  }
}
