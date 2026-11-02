# Чтение `workspaceId` в обход проверки прав — без общего теста

## Что уже сделано

- Состав пространства — `GET api/Users/GetProfiles`: пространство из заголовка
  `X-Workspace-Id`, `[AuthorizePermissions]` без прав — пускает только участника
  (`403` постороннему, `specs/authorization.md`, 5).
- Наружу едет `ProfileSummaryDto` (`id`, `userName`, `avatarId`), а не сущность `Profile`:
  ни почты, ни телефона, ни основного пространства про чужих
  (`UsersProfilesTests.MembersListHasNoContacts`).
- Списка «все пользователи системы» на клиенте нет; если он понадобится — только в админке
  («Аккаунты», `specs/admin.md`, 12).

## Что осталось

Общее правило не проверяется ничем, кроме ревью: метод, читающий `workspaceId` из параметра
без `[AuthorizePermissions]` (и без заголовка `X-Workspace-Id`), доверяет клиенту. Сейчас таких
законно три — `Workspace/SetPrimary` и `Workspace/Leave` (сервис сам сверяет членство)
и анонимные методы приглашения (пространство берётся из приглашения). Нужен тест по всем
контроллерам рефлексией, как `AdminWiringTests` (`tests/PetTemplate.Tests/Features/Admin`): параметр или свойство тела
`workspaceId` без `[AuthorizePermissions]` — только из явного белого списка.

Карточка чужого профиля и видимость полей — `profile-visibility.md`.
