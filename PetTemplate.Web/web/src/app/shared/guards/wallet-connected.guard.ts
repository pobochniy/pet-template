import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserService } from '../services/user.service';

export const walletConnectedGuard: CanActivateFn = () => {
  const userService = inject(UserService);
  const router = inject(Router);

  if (userService.isWalletConnected()) {
    return true;
  }

  return router.createUrlTree(['/profile', 'wallet']);
};
