export class UserProfileModel {
  id?: string;
  userName?: string;
  email?: string;
  phone?: string;

  constructor(data?: any) {
    if (data) {
      this.id = data.id;
      this.userName = data.userName;
      this.email = data.email;
      this.phone = data.phone;
    }
  }
}
