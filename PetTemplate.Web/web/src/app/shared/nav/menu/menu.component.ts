import {Component, OnInit} from '@angular/core';
import {UserService} from '../../services/user.service';
import {EventEmitterService} from '../event-emitter.service';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'shared-nav-menu',
  templateUrl: './menu.component.html',
  styleUrls: ['./menu.component.css'],
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive]
})
export class NavMenuComponent implements OnInit {
  constructor(private eventEmitterService: EventEmitterService,
              public userService: UserService
  ) {
  }

  ngOnInit(): void {
    if (this.eventEmitterService.subsMenu == undefined) {
      this.eventEmitterService.subsMenu = this.eventEmitterService
        .invokeMenuToggleMenuFunction.subscribe(() => {
          this.toggleMenu();
        });
    }
  }

  isExpanded = false;

  toggleMenu() {
    this.isExpanded = !this.isExpanded;
  }
}
