import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { ProfileModel } from '../../shared/models/profile.model';

@Component({
  selector: 'app-view',
  templateUrl: './view.component.html',
  styleUrl: './view.component.css',
  imports: []
})
export class ViewComponent {
  profile?: ProfileModel = undefined;
  userId: string | null;

  constructor(
    private service: ProfileApiService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.userId = route.snapshot.paramMap.get('userId');
  }

  async ngOnInit() {
    if (!this.userId) {
      await this.router.navigateByUrl('/');
      return;
    }

    this.profile = await this.service.view(this.userId);
    
    if (!this.profile) {
      await this.router.navigateByUrl('/');
    }
  }
}
