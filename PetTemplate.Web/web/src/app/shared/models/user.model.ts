export class UserModel {
  id?: string;
  userName?: string;
  email?: string;
  permissionsByOrganization?: { [orgId: string]: number[] };
}
