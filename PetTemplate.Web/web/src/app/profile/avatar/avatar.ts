import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ProfileApiService } from '../../shared/api/profile-api.service';
import { UserService } from '../../shared/services/user.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-avatar',
  imports: [CommonModule, RouterLink],
  templateUrl: './avatar.html',
  styleUrl: './avatar.css',
})
export class Avatar {
  private profileApi = inject(ProfileApiService);
  userService = inject(UserService);
  private router = inject(Router);

  availableAvatars = [0, 1, 2, 3, 4, 5, 6, 7];
  currentAvatarId = computed(() => {
    const profile = this.userService.profile();
    return profile?.avatarId ?? 0;
  });

  async ngOnInit() {
    // Загружаем профиль, если он еще не загружен
    if (!this.userService.profile()) {
      const profile = await this.profileApi.details();
      if (profile) {
        this.userService.setProfile(profile);
      }
    }
  }

  async selectAvatar(avatarId: number) {
    try {
      await this.profileApi.setAvatar(avatarId);
      
      const profile = this.userService.profile();
      console.log('Current profile before update:', profile);
      if (profile) {
        const updatedProfile = { ...profile, avatarId };
        console.log('Updated profile:', updatedProfile);
        this.userService.setProfile(updatedProfile);
        console.log('Profile after setProfile:', this.userService.profile());
      }
    } catch (error) {
      console.error('Error setting avatar:', error);
      alert('Ошибка при установке аватара');
    }
  }

  getAvatarUrl(id: number): string {
    return `/avatars/${id}.png`;
  }
}
