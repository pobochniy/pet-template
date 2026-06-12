import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UserService } from '../shared/services/user.service';
import { AuthApiService } from '../shared/api/auth-api.service';

@Component({
  selector: 'app-home',
  standalone: true,
  templateUrl: './home.component.html',
  styleUrl: './home.component.css',
  imports: [RouterLink]
})
export class HomeComponent {
  userService = inject(UserService);
  private authService = inject(AuthApiService);
  private router = inject(Router);

  async logout() {
    await this.authService.logout();
    await this.router.navigateByUrl('/auth/login');
  }
}
