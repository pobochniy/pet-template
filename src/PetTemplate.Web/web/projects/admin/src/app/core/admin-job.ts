import type { AdminJob } from './admin.model';

/** Что запустили из админки — копия `src/PetTemplate.Shared/Enums/AdminJobKind.cs`, номера те же */
export enum AdminJobKind {
  ModelCheck = 2,
  DialogEvaluation = 3
}

/** Состояние запуска — копия `src/PetTemplate.Shared/Enums/AdminJobStatus.cs`, номера те же */
export enum AdminJobStatus {
  Running = 0,
  Done = 1,
  Failed = 2
}

/**
 * Как часто экран перечитывает идущий запуск (спец. admin, 15.3 и 16.3): он идёт фоном
 * минутами, и пять секунд — достаточно часто, чтобы видеть, что дело движется
 */
export const JOB_POLL_MS = 5000;

/** «идёт · 4 из 30», «готово», «сорвался» — одна строка состояния запуска */
export function jobState(job: AdminJob): string {
  switch (job.status) {
    case AdminJobStatus.Running:
      return `идёт · ${job.done + job.skipped + job.failed} из ${job.planned}`;
    case AdminJobStatus.Done:
      return 'готово';
    default:
      return 'сорвался';
  }
}
