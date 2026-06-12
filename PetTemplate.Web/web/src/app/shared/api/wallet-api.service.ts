import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BaseApiService } from './base-api.service';
import { TokenService } from './token.service';

@Injectable()
export class WalletApiService extends BaseApiService {
  constructor(http: HttpClient, tokenService: TokenService) {
    super('Wallet', tokenService, http);
  }

  async connectWallet(walletAddress: string): Promise<void> {
    return this.post('Connect', { walletAddress });
  }

  async disconnectWallet(): Promise<void> {
    return this.post('Disconnect');
  }

}
