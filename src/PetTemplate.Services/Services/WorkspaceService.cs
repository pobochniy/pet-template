using Microsoft.EntityFrameworkCore;
using PetTemplate.Db.Entity;
using PetTemplate.Shared.Constants;
using PetTemplate.Shared.Dto;
using PetTemplate.Shared.Enums;
using PetTemplate.Shared.Exceptions;

namespace PetTemplate.Services.Services;

/// <summary>
/// Пространство и его состав (спец. authorization, 3, 7 и 8): создание, правка, основное
/// пространство, выход, удаление участника и фоновая чистка заброшенных.
///
/// Точки расширения для разделов проекта, заводящих содержимое пространства:
/// <list type="bullet">
/// <item>права раздела — в <see cref="PermissionEnum"/>; набор владельца берётся из него
/// целиком (<see cref="AllPermissions"/>), здесь ничего дописывать не нужно;</item>
/// <item>заготовки раздела, которые должны появиться вместе с пространством, — в <see cref="Add"/>,
/// тем же сохранением;</item>
/// <item>даты содержимого — в <see cref="LastActivity"/>, его удаление — в <see cref="PurgeContents"/>
/// до удаления самого пространства.</item>
/// </list>
/// </summary>
public class WorkspaceService(ApplicationContext db)
{
    /// <summary>
    /// Все права системы — набор владельца пространства. Его получает создатель
    /// и он же достаётся последнему оставшемуся участнику (спец. authorization, 8).
    /// Права разделов проекта попадают сюда сами, как только их допишут в перечисление
    /// </summary>
    private static readonly IReadOnlyList<PermissionEnum> AllPermissions = Enum
        .GetValues<PermissionEnum>()
        .Where(x => x != PermissionEnum.None)
        .ToArray();

    /// <summary>
    /// Заводит пространство: само пространство и все права создателю — одним
    /// <c>SaveChanges</c>, то есть одной транзакцией. Явная транзакция не нужна:
    /// идентификатор раздаёт не база, а мы сами, поэтому промежуточное сохранение
    /// ради <c>Id</c> ни к чему; порядок вставок под внешние ключи EF выстроит сам
    /// </summary>
    public async Task<Guid> Create(WorkspaceDto dto, Guid userId, CancellationToken ct = default)
    {
        // Спрашиваем до вставки прав: после неё у пользователя пространство уже есть,
        // и «единственное» не отличить от «второго»
        var hadWorkspaces = await db.UserInPermissions
            .AnyAsync(x => x.UserId == userId, ct);

        // Единственное пространство становится основным: открывать при входе больше нечего
        var primaryOf = hadWorkspaces ? null : await db.Profiles.SingleAsync(x => x.Id == userId, ct);

        var workspaceId = await Add(dto.Name.Trim(), userId, primaryOf, ct);
        await db.SaveChangesAsync(ct);

        return workspaceId;
    }

    /// <summary>
    /// То же, что <see cref="Create"/>, но <b>без</b> <c>SaveChanges</c> — сохраняет вызывающий.
    /// Нужно регистрации с пространством (спец. authorization, 3, п. 1): профиль ещё
    /// не в базе, и аккаунт с пространством уходят одним сохранением.
    /// <paramref name="primaryOf"/> — профиль, которому пространство становится основным;
    /// <c>null</c> — основное не меняется
    /// </summary>
    public async Task<Guid> Add(string name, Guid userId, Profile? primaryOf, CancellationToken ct = default)
    {
        var workspace = new Workspace
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await db.Workspaces.AddAsync(workspace, ct);

        // Создателю — все права в этом пространстве
        var allPermissions = AllPermissions
            .Select(p => new UserInPermission
            {
                WorkspaceId = workspace.Id,
                UserId = userId,
                PermissionId = p
            })
            .ToArray();

        await db.UserInPermissions.AddRangeAsync(allPermissions, ct);

        if (primaryOf != null)
        {
            primaryOf.PrimaryWorkspaceId = workspace.Id;
        }

        return workspace.Id;
    }

    /// <summary>
    /// Правка пространства — название (спец. authorization, 7.2). Право проверено
    /// атрибутом по заголовку, и пространство берётся оттуда же: чужое сюда не доедет
    /// </summary>
    public async Task Update(Guid workspaceId, WorkspaceDto dto, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.SingleAsync(x => x.Id == workspaceId, ct);

        workspace.Name = dto.Name.Trim();

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Меняет пространство, которое открывается при входе (спец. authorization, 7.1).
    /// Права здесь ни при чём — основное пространство это свойство пользователя,
    /// поэтому проверяем только членство: чужое или несуществующее → 400
    /// </summary>
    public async Task SetPrimary(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var isMember = await db.UserInPermissions
            .AnyAsync(x => x.UserId == userId && x.WorkspaceId == workspaceId, ct);

        if (!isMember)
        {
            throw new BadRequestException(WorkspaceMessages.WorkspaceUnavailable);
        }

        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);
        profile.PrimaryWorkspaceId = workspaceId;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Выход из пространства (спец. authorization, 8): у выходящего просто не остаётся прав
    /// в этом пространстве — членство и есть строки <see cref="UserInPermission"/>.
    ///
    /// Заодно решаются два вопроса, которые больше решить некому: кто останется
    /// с правом раздавать права (<see cref="TransferPermissions"/>) и что открывать
    /// при входе, если ушли из основного (<see cref="RepointPrimary"/>).
    ///
    /// Вышел последний — пространство удаляется сразу, тем же порядком, что фоновая
    /// чистка: передавать его некому, а полгода до чистки оно висело бы мёртвым
    /// </summary>
    public async Task Leave(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var membership = await db.UserInPermissions
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        if (membership.Count == 0)
        {
            throw new BadRequestException(WorkspaceMessages.NotAMember);
        }

        db.UserInPermissions.RemoveRange(membership);
        await ForgetPersonalFacts(userId, workspaceId, ct);

        await TransferPermissions(workspaceId, leavingUserId: userId, ct);
        await RepointPrimary(userId, workspaceId, ct);

        await db.SaveChangesAsync(ct);

        // Удаление аккаунта (AccountDeletion) зовёт выход внутри своей транзакции и только
        // из пространств, где кто-то остаётся. Снос открывает свою транзакцию сам,
        // поэтому удаляем только вне чужой
        if (db.Database.CurrentTransaction == null
            && !await db.UserInPermissions.AnyAsync(x => x.WorkspaceId == workspaceId, ct))
        {
            await Purge(workspaceId, ct);
        }
    }

    /// <summary>
    /// Удаление участника тем, кто раздаёт права. Отдельное явное действие, а не
    /// снятие всех галок на экране прав: пустой набор прав — это уже не участник,
    /// и промах мышью не должен выкидывать человека из пространства.
    ///
    /// Себя этой кнопкой не убирают: выход отвечает новым токеном и переводит
    /// указатель основного пространства, а удаление участника — ни того, ни другого.
    /// Поэтому пустым пространство отсюда не выходит: тот, кто удаляет, в нём остаётся,
    /// и удалять его сразу, как в <see cref="Leave"/>, не нужно
    /// </summary>
    public async Task RemoveMember(Guid actorId, Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        if (actorId == userId)
        {
            throw new BadRequestException(WorkspaceMessages.CannotRemoveSelf);
        }

        var membership = await db.UserInPermissions
            .Where(x => x.UserId == userId && x.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        if (membership.Count == 0)
        {
            throw new NotFoundException(WorkspaceMessages.MemberNotFound);
        }

        db.UserInPermissions.RemoveRange(membership);
        await ForgetPersonalFacts(userId, workspaceId, ct);

        // Убрать могли и единственного, кто раздавал права: пространство без такого
        // участника чинить потом будет некому
        await TransferPermissions(workspaceId, leavingUserId: userId, ct);
        await RepointPrimary(userId, workspaceId, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Свои пространства — с числом участников: подтверждение выхода по нему узнаёт
    /// последнего (спец. authorization, 7.2)
    /// </summary>
    public async Task<List<WorkspaceDto>> GetList(Guid userId, CancellationToken ct = default)
    {
        var workspaceIds = db.UserInPermissions
            .Where(x => x.UserId == userId)
            .Select(x => x.WorkspaceId);

        var workspaces = await db.Workspaces
            .Where(x => workspaceIds.Contains(x.Id))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new WorkspaceDto
            {
                Id = x.Id,
                Name = x.Name,
                MembersCount = x.UserPermissions.Select(p => p.UserId).Distinct().Count()
            })
            .ToListAsync(ct);

        return workspaces;
    }

    /// <summary>
    /// Фоновая чистка заброшенных пространств (спец. authorization, 8): под нож идёт то,
    /// в котором нет ни одного участника и внутри которого полгода ничего не происходило.
    /// Оба критерия обязательны.
    ///
    /// Удаление физическое, вместе со всем содержимым: так требует спека, и содержимое
    /// это всё равно никому не видно — доступ к пространству дают строки прав, а их нет.
    ///
    /// Пространство, из которого вышел последний, удаляется сразу (<see cref="Leave"/>) —
    /// чистка остаётся подстраховкой для пространств, опустевших иначе.
    ///
    /// Возвращает число удалённых пространств. Идемпотентно: повторный проход,
    /// в том числе на втором экземпляре API, просто ничего не находит
    /// </summary>
    public async Task<int> CleanupAbandoned(CancellationToken ct = default)
    {
        var abandoned = await db.Workspaces
            .Where(w => !db.UserInPermissions.Any(p => p.WorkspaceId == w.Id))
            .Select(w => new { w.Id, w.CreatedAt })
            .ToListAsync(ct);

        if (abandoned.Count == 0) return 0;

        var threshold = DateTime.UtcNow - WorkspaceConstants.AbandonedRetention;
        var ids = abandoned.Select(x => x.Id).ToList();
        var lastActivity = await LastActivity(ids, ct);

        var dead = abandoned
            .Where(x => Latest(lastActivity, x.Id, x.CreatedAt) < threshold)
            .Select(x => x.Id)
            .ToList();

        foreach (var id in dead)
        {
            await Purge(id, ct);
        }

        return dead.Count;
    }

    /// <summary>
    /// Ушедший из пространства уносит свою память в нём (спец. assistant, 12.5): личные
    /// умолчания — про него, а не про пространство, и вернись он по новому приглашению,
    /// ассистент начнёт с чистого листа. Общие факты пространства остаются — их
    /// записывали для всех. Тем же сохранением, что и снятие прав
    /// </summary>
    private async Task ForgetPersonalFacts(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var personal = await db.AssistantFacts
            .Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .ToListAsync(ct);
        db.AssistantFacts.RemoveRange(personal);
    }

    /// <summary>
    /// Сам порядок сноса пространства — без своей транзакции: <c>ExecuteDelete</c>
    /// уходит в базу сразу, и транзакцию держит вызывающий (<see cref="Purge"/>;
    /// удаление аккаунта, <see cref="AccountDeletion"/>, — в своей общей). Порядок обязателен: внешние ключи
    /// содержимого на пространство стоят <c>Restrict</c>, каскада от него нет.
    ///
    /// Точка расширения: раздел проекта дописывает удаление своего содержимого сюда,
    /// <b>до</b> удаления самого пространства. Если раздел индексирует содержимое
    /// в поиске, точки в Qdrant удаляет тоже он: <c>SearchIndex.Of</c> на свои сущности
    /// тем же сохранением, до их удаления (спец. search, 4 и 9)
    /// </summary>
    public async Task PurgeContents(Guid workspaceId, CancellationToken ct)
    {
        // Права приглашений уносит каскад со своего приглашения
        await db.WorkspaceInvitations.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);

        // Ассистент (спец. assistant, 12.1): сообщения ссылаются на планы и прогоны,
        // поэтому уходят первыми; прогоны — последними. Память пространства — общая
        // и личная его участников — уходит с ним (12.5)
        await db.AssistantMessages.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.AssistantFacts.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.AssistantPlans.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        await db.AssistantRuns.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);
        // Трассу прогонов уносит каскад с прогона. Строки расхода остаются: это деньги,
        // ушедшие поставщику (спец. assistant, 13.5); свой лимит — уходит
        await db.WorkspaceAssistantLimits.Where(x => x.WorkspaceId == workspaceId).ExecuteDeleteAsync(ct);

        // Указатель на основное пространство переживает членство только у того, кого
        // из пространства убрали мимо `Leave`; ссылку на удаляемое не должно остаться
        // ни у кого — внешний ключ этого и не позволит
        await db.Profiles
            .Where(x => x.PrimaryWorkspaceId == workspaceId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.PrimaryWorkspaceId, (Guid?)null), ct);

        await db.Workspaces.Where(x => x.Id == workspaceId).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Пространство не должно остаться без того, кто раздаёт права (спец. authorization, 8).
    /// Если <see cref="PermissionEnum.PermissionManagement"/> уносит последний, кто
    /// им владел, право переходит к оставшимся: одному — вместе со всеми остальными,
    /// он теперь и есть владелец; нескольким — только <c>PermissionManagement</c>,
    /// выбирать из них одного нам не по чему.
    ///
    /// Ничего не сохраняет: и выход, и удаление участника уходят одним
    /// <c>SaveChanges</c> вместе со снятием прав
    /// </summary>
    private async Task TransferPermissions(Guid workspaceId, Guid leavingUserId, CancellationToken ct)
    {
        var rest = await db.UserInPermissions
            .Where(x => x.WorkspaceId == workspaceId && x.UserId != leavingUserId)
            .Select(x => new { x.UserId, x.PermissionId })
            .ToListAsync(ct);

        // Пространство опустело — раздавать право некому: оно удаляется сразу в Leave
        if (rest.Count == 0) return;

        if (rest.Any(x => x.PermissionId == PermissionEnum.PermissionManagement)) return;

        var members = rest.Select(x => x.UserId).Distinct().ToList();

        IReadOnlyList<PermissionEnum> granted = members.Count == 1
            ? AllPermissions
            : [PermissionEnum.PermissionManagement];

        var missing = members
            .SelectMany(userId => granted
                .Where(permission => !rest.Any(x => x.UserId == userId && x.PermissionId == permission))
                .Select(permission => new UserInPermission
                {
                    WorkspaceId = workspaceId,
                    UserId = userId,
                    PermissionId = permission
                }));

        db.UserInPermissions.AddRange(missing);
    }

    /// <summary>
    /// Указатель на пространство, которое открывается при входе, не должен пережить
    /// членство в нём: переводим на следующее доступное, а если других нет —
    /// обнуляем, и пользователь увидит приветственную плашку (спец. authorization, 7.1).
    ///
    /// Ничего не сохраняет — правка уезжает общим <c>SaveChanges</c>
    /// </summary>
    private async Task RepointPrimary(Guid userId, Guid leftWorkspaceId, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(x => x.Id == userId, ct);

        if (profile.PrimaryWorkspaceId != leftWorkspaceId) return;

        // Снятые строки ещё в ChangeTracker, но в базе есть: спрашиваем без покинутого
        profile.PrimaryWorkspaceId = await db.UserInPermissions
            .Where(x => x.UserId == userId && x.WorkspaceId != leftWorkspaceId)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Последнее действие внутри пространства. Собираем по датам содержимого; своей
    /// колонки «последняя активность» у пространства нет, и заводить её ради одной суточной
    /// задачи не стоит — считать по факту дешевле, чем поддерживать на каждой записи.
    ///
    /// В ядре шаблона содержимого нет, кроме приглашений, а они к сроку очистки уже
    /// удалены (21 день против 180) и никого не спасают — источник оставлен, чтобы
    /// было видно, как дописывать свой. Точка расширения: раздел проекта добавляет
    /// сюда свой источник — запрос, сгруппированный по пространству, с максимумом даты
    /// </summary>
    private async Task<Dictionary<Guid, DateTime>> LastActivity(List<Guid> workspaceIds, CancellationToken ct)
    {
        // Каждый источник уже сгруппирован: максимум считает база, наружу едет
        // по одной строке на пространство
        var sources = new[]
        {
            db.WorkspaceInvitations
                .Where(x => workspaceIds.Contains(x.WorkspaceId))
                .GroupBy(x => x.WorkspaceId)
                .Select(g => new ActivityRow(g.Key, g.Max(x => x.CreatedAtUtc))),
            // Разговор с ассистентом — тоже жизнь пространства (спец. assistant, 12.1)
            db.AssistantMessages
                .Where(x => workspaceIds.Contains(x.WorkspaceId))
                .GroupBy(x => x.WorkspaceId)
                .Select(g => new ActivityRow(g.Key, g.Max(x => x.CreatedAtUtc)))
        };

        var latest = new Dictionary<Guid, DateTime>();

        foreach (var source in sources)
        {
            foreach (var row in await source.ToListAsync(ct))
            {
                if (!latest.TryGetValue(row.WorkspaceId, out var known) || known < row.At)
                {
                    latest[row.WorkspaceId] = row.At;
                }
            }
        }

        return latest;
    }

    /// <summary>
    /// Дата последней записи одного вида содержимого в одном пространстве
    /// </summary>
    private sealed record ActivityRow(Guid WorkspaceId, DateTime At);

    private static DateTime Latest(Dictionary<Guid, DateTime> lastActivity, Guid workspaceId, DateTime created)
    {
        // Пустое пространство живёт с момента создания: удалить его назавтра
        // после «создал и не успел позвать» было бы сюрпризом
        return lastActivity.TryGetValue(workspaceId, out var last) && last > created ? last : created;
    }

    /// <summary>
    /// Сносит пространство в своей транзакции: <c>ExecuteDelete</c> уходит в базу сразу,
    /// отдельным запросом, и без неё сбой на середине оставил бы половину пространства.
    ///
    /// Замка на пространство (как <c>WorkspaceLock</c> в Lad) в ядре нет: параллельно
    /// писать в пустое пространство некому. Раздел, у которого содержимое заводится
    /// без участника (фоновые задачи по расписанию), берёт его здесь, до транзакции
    /// </summary>
    private async Task Purge(Guid workspaceId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await PurgeContents(workspaceId, ct);

        await transaction.CommitAsync(ct);
    }
}
