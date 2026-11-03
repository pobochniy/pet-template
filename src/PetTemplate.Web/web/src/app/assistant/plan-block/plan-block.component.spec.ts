import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { PlanBlockComponent } from './plan-block.component';
import { I18nService } from '../../shared/i18n/i18n.service';
import { loadAllScopes } from '../../shared/i18n/i18n.testing';
import { AssistantPlan, AssistantPlanStatus, PlanOperation } from '../../shared/models/assistant.model';
import { assistantPlanDto } from '../../shared/api/dto.mock';

/**
 * Блок плана (спец. assistant, 5.1): строится из JSON плана, а не из прозы модели.
 * Видов операций в ядре нет — проверяем на видах тестового раздела, как сервер
 */
describe('PlanBlockComponent', () => {
  let fixture: ComponentFixture<PlanBlockComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlanBlockComponent],
      providers: [provideZonelessChangeDetection()]
    }).compileComponents();
    await loadAllScopes(TestBed.inject(I18nService));
  });

  function render(plan: AssistantPlan): HTMLElement {
    fixture = TestBed.createComponent(PlanBlockComponent);
    fixture.componentRef.setInput('plan', plan);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  const note = (index: number, title: string): PlanOperation => ({ index, kind: 'CreateNote', args: { title } });

  const rows = (el: HTMLElement) => Array.from(el.querySelectorAll('.plan-block__row')).map(x => x.textContent?.trim());

  it('should list operations in plan order, without marks while it is a proposal', () => {
    const el = render(assistantPlanDto({
      id: 'p1',
      status: AssistantPlanStatus.Pending,
      operations: { operations: [note(2, 'Шарлотка'), note(1, 'Рецепты')] }
    }));

    expect(rows(el)).toEqual(['Рецепты', 'Шарлотка']);
    expect(el.textContent).not.toContain('Готово');
    expect(el.textContent).not.toContain('✓');
  });

  /** Вида нет в словаре и текстовых аргументов нет — сухая строка лучше пропавшей операции */
  it('should fall back to the kind when there is nothing to show', () => {
    const el = render(assistantPlanDto({
      id: 'p1',
      status: AssistantPlanStatus.Pending,
      operations: { operations: [{ index: 1, kind: 'RenameNote', args: { count: 3 } }] }
    }));

    expect(rows(el)).toEqual(['RenameNote']);
  });

  /** Отчёт: галочка у прошедшего, крестик с причиной у отказавшего, без значка — недошедшее */
  it('should mark the report honestly', () => {
    const el = render(assistantPlanDto({
      id: 'p1',
      status: AssistantPlanStatus.Applied,
      operations: { operations: [note(1, 'Рецепты'), note(2, 'Шарлотка'), note(3, 'Пирог')] },
      result: {
        operations: [
          { index: 1, done: true, createdId: 'n1', error: null },
          { index: 2, done: false, createdId: null, error: 'нет права' }
        ]
      }
    }));

    expect(el.querySelector('.plan-block__title')?.textContent).toContain('Готово');
    const marks = Array.from(el.querySelectorAll('.plan-block__mark')).map(x => x.textContent?.trim());
    expect(marks).toEqual(['✓', '✗', '']);
    expect(el.querySelector('.plan-block__error')?.textContent).toContain('нет права');
  });

  it('should title a draft and fade a cancelled plan', () => {
    const draft = render(assistantPlanDto({ id: 'p1', status: AssistantPlanStatus.Draft, operations: { operations: [note(1, 'Рецепты')] } }));
    expect(draft.textContent).toContain('Пока собрал:');

    const cancelled = render(assistantPlanDto({ id: 'p2', status: AssistantPlanStatus.Cancelled, operations: { operations: [note(1, 'Рецепты')] } }));
    expect(cancelled.querySelector('.plan-block--cancelled')).not.toBeNull();
  });

  it('should fold a long plan and unfold it on click', () => {
    const operations = Array.from({ length: 12 }, (_, i) => note(i + 1, `Заметка ${i + 1}`));
    const el = render(assistantPlanDto({ id: 'p1', status: AssistantPlanStatus.Pending, operations: { operations } }));

    expect(rows(el).length).toBe(10);
    const more = el.querySelector('button') as HTMLButtonElement;
    expect(more.textContent).toContain('ещё 2');

    more.click();
    fixture.detectChanges();

    expect(rows(el).length).toBe(12);
    expect(el.querySelector('button')).toBeNull();
  });
});
