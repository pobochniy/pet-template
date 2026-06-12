import { Injectable, computed, signal } from '@angular/core';
import { ProfileModel } from '../models/profile.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  private _profile = signal<ProfileModel | null>(null);

  get profile() {
    return this._profile.asReadonly();
  }

  readonly isWalletConnected = computed(() => !!this._profile()?.walletAddress);

  setProfile(profile: ProfileModel | null): void {
    this._profile.set(profile);
  }

  clearProfile(): void {
    this._profile.set(null);
  }

  get isAuthenticated(): boolean {
    return this._profile() !== null;
  }
}
