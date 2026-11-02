import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Section } from './sections';

/** Страница раздела. Пока у раздела нет экрана, на ней только заголовок */
@Component({
  selector: 'adm-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h2 class="h6">{{ section()?.title }}</h2>`
})
export class SectionComponent {
  private readonly data = toSignal(inject(ActivatedRoute).data);

  protected readonly section = computed(() => this.data()?.['section'] as Section | undefined);
}
