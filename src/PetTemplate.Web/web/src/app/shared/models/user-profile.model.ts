import type { ApiSchema } from '../api/generated/schema';

/**
 * Участник пространства в списках — `ProfileSummaryDto` на сервере (алиас схемы OpenAPI).
 * Контактов нет: почта и телефон — только в своём профиле (спец. authorization, 5)
 */
export type UserProfileModel = ApiSchema<'ProfileSummaryDto'>;
