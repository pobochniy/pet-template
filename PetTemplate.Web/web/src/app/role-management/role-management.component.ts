import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UsersApiService } from '../shared/api/users-api.service';
import { AlertsService } from '../shared/alerts/alerts.service';
import { UserService } from '../shared/core/user.service';
import { PermissionEnum } from '../shared/enums/permission.enum';
import { UserProfileModel } from '../shared/models/user-profile.model';
import { UserRoleInfoModel } from '../shared/models/user-role-info.model';

@Component({
  selector: 'app-role-management',
  templateUrl: './role-management.component.html',
  styleUrls: ['./role-management.component.css'],
  standalone: true,
  imports: [CommonModule, FormsModule],
  providers: [UsersApiService]
})
export class RoleManagementComponent implements OnInit {

  public roles?: UserRoleInfoModel[];
  public roleGroups: string[] = [];
  public profiles?: UserProfileModel[];
  public userId?: string;
  public userRoles?: number[] = [];

  constructor(
    private service: UsersApiService,
    public userService: UserService,
    private alertsService: AlertsService
  ) {}

  async ngOnInit() {
    this.fillRoles();

    for (const role of this.roles!) {
      if (!this.roleGroups.includes(role.groupCode)) {
        this.roleGroups.push(role.groupCode);
      }
    }

    this.profiles = await this.service.GetProfiles();
  }

  public async userSelected(val: any) {
    this.userRoles = [];

    if (val && this.userId != val) {
      this.userId = val;
      await this.checkRoles();
    }
  }

  public getRolesByGroupCode(group: string): UserRoleInfoModel[] {
    return this.roles!.filter(x => x.groupCode == group);
  }

  public CheckAllOptions(group: string) {
    const checkboxes = this.roles!.filter(x => x.groupCode == group);

    if (checkboxes.every(val => val.checked))
      checkboxes.forEach(val => { val.checked = false });
    else
      checkboxes.forEach(val => { val.checked = true });
  }

  public async saveRoles() {
    try {
      const checked = this.roles!.filter(x => x.checked).map(x => x.id);
      await this.service.setUserRoles(this.userId!, '', checked);
      await this.checkRoles();
      this.alertsService.push('success', 'Права успешно сохранены', 3000);
    } catch (error: any) {
      this.alertsService.push('danger', error?.message || 'Ошибка при сохранении прав', 5000);
    }
  }

  private async checkRoles() {
    const result = await this.service.getUserRoles(this.userId!, '');
    this.userRoles = result ?? [];
    this.roles!.forEach(x => { x.checked = this.userRoles!.includes(x.id) });
  }

  private fillRoles() {
    this.roles = [
      new UserRoleInfoModel(PermissionEnum.permissionManagement, 'Управление правами', 'permissionManagement'),
      new UserRoleInfoModel(PermissionEnum.organizationCrud, 'Редактирование организации', 'organization'),
      new UserRoleInfoModel(PermissionEnum.issueRead, 'Просмотр Issue', 'issue'),
      new UserRoleInfoModel(PermissionEnum.issueCrud, 'Редактирование, создание, удаление Issue', 'issue'),
      new UserRoleInfoModel(PermissionEnum.epicRead, 'Просмотр Epic', 'epic'),
      new UserRoleInfoModel(PermissionEnum.epicCrud, 'Редактирование, создание, удаление Epic', 'epic'),
      new UserRoleInfoModel(PermissionEnum.sprintRead, 'Просмотр Sprint', 'sprint'),
      new UserRoleInfoModel(PermissionEnum.sprintCrud, 'Редактирование, создание, удаление Sprint', 'sprint'),
      new UserRoleInfoModel(PermissionEnum.hourlyPayRead, 'Просмотр ставок', 'hourlyPay'),
      new UserRoleInfoModel(PermissionEnum.hourlyPayCrud, 'Редактирование ставок', 'hourlyPay'),
      new UserRoleInfoModel(PermissionEnum.finPeriodRead, 'Просмотр финансовых периодов', 'finPeriod'),
      new UserRoleInfoModel(PermissionEnum.finPeriodEdit, 'Редактирование финансовых периодов', 'finPeriod'),
    ];
  }

}
