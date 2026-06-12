import { environment } from '../environments/environment';
import { AuthApiService } from './shared/api/auth-api.service';
import { AuthApiServiceMock } from './shared/api/auth-api.service.mock';
import { ProfileApiService } from './shared/api/profile-api.service';
import { TokenService } from './shared/api/token.service';
import { UserService } from './shared/services/user.service';
import { WalletApiService } from './shared/api/wallet-api.service';

export const serviceCollection = [
  { provide: AuthApiService, useClass: environment.useMockApi ? AuthApiServiceMock : AuthApiService },
  { provide: ProfileApiService },
  { provide: TokenService },
  { provide: UserService },
  { provide: WalletApiService },
];
