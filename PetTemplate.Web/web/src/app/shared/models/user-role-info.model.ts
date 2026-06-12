export class UserRoleInfoModel {
  id: number;
  summary: string;
  groupCode: string;
  checked: boolean = false;

  constructor(id: number, summary: string, groupCode: string) {
    this.id = id;
    this.summary = summary;
    this.groupCode = groupCode;
  }
}
