import { ProfileModel } from './profile.model';
import { PermissionEnum } from '../enums/permission.enum';

export interface AuthenticationResultModel {
  token: string;
  profile: ProfileModel;
  permissions: PermissionEnum[];
}
