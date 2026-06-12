import { Component, OnInit, inject } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { profileFormModel } from '../../shared/form-models/profile-form.model';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { FormValidationComponent } from '../../shared/form-validation/form-validation.component';
import { UserService } from '../../shared/services/user.service';
import { WalletComponent } from '../wallet/wallet.component';

@Component({
  selector: 'app-edit',
  templateUrl: './edit.component.html',
  styleUrl: './edit.component.css',
  imports: [ReactiveFormsModule, FormValidationComponent, RouterLink, WalletComponent]
})
export class EditComponent implements OnInit {
  public profileForm = profileFormModel;
  protected userService = inject(UserService);

  constructor(private service: ProfileApiService, private router: Router) {
  }

  async ngOnInit() {
    const profile = await this.service.details();
    if (!profile) return;

    this.userService.setProfile(profile);

    this.profileForm.setValue({
      userName: profile.userName,
      comment: profile.comment,
      isAdult: profile.isAdult,
      hasAcceptedTerms: profile.hasAcceptedTerms
    });
  }

  getAvatarUrl(): string {
    const profile = this.userService.profile();
    const avatarId = profile?.avatarId ?? 0;
    return `/avatars/${avatarId}.png`;
  }

  async onSubmit() {
    Object.keys(this.profileForm.controls).forEach(key => {
      this.profileForm.get(key)?.markAsDirty();
    });

    try {
      if (this.profileForm.valid) {
        await this.service.save(this.profileForm);
        await this.router.navigateByUrl('/');
      }
    } catch {
      alert('Возникли непредвиденные ошибки. Попробуйте ввести другие значения или сообщите программисту');
    }
  }

  maxLength = function (evt: any){
    if(evt.target.value > 9999) evt.target.value = 9999;
  }
}
