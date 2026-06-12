import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { EventEmitterService } from '../event-emitter.service';
import { CommonModule } from '@angular/common';
import { UserService } from '../../services/user.service';
import { AuthApiService } from '../../api/auth-api.service';

@Component({
  selector: 'top-nav',
  templateUrl: './top-nav.component.html',
  styleUrls: ['./top-nav.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class TopNavComponent {
  constructor(
    private eventEmitterService: EventEmitterService,
    public userService: UserService,
    private authService: AuthApiService,
    private router: Router
  ) {}

  toggleMenu() {
    this.eventEmitterService.onToggleMenuButtonClick();
  }

  async logOut() {
    await this.authService.logout();
    await this.router.navigateByUrl('/');
  }

  async goToProfile() {
    await this.router.navigateByUrl('/profile');
  }
}
