import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AlertsComponent } from './shared/alerts/alerts.component';
import { NavMenuComponent } from './shared/nav/menu/menu.component';
import { TopNavComponent } from './shared/nav/top-nav/top-nav.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, AlertsComponent, NavMenuComponent, TopNavComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('PetTemplate');
}
