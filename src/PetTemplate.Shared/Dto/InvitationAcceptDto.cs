namespace PetTemplate.Shared.Dto;

/// <summary>
/// Принятие приглашения уже вошедшим пользователем (спец. authorization, 6.3, сценарий А).
/// Токен — в теле, а не в query: query-параметры POST-запроса оседают в логах прокси
/// и сервера. Пространство в теле не передаётся и <c>X-Workspace-Id</c> здесь ни при чём:
/// куда зовут, знает сам токен
/// </summary>
public class InvitationAcceptDto
{
    public Guid Token { get; set; }
}
