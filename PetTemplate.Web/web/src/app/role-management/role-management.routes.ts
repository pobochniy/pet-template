import { Routes } from '@angular/router';
import { RoleManagementComponent } from './role-management.component';
import { RolesGuard } from '../roles.guard';
import { PermissionEnum } from '../shared/enums/permission.enum';

export const roleManagementRoutes: Routes = [
  {
    path: 'rolemanagement',
    component: RoleManagementComponent,
    canActivate: [RolesGuard],
    data: { allowedRoles: [PermissionEnum.permissionManagement] }
  }
];
