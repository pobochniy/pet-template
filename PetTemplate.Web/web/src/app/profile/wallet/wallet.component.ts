import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { WalletApiService } from '../../shared/api/wallet-api.service';
import { UserService } from '../../shared/services/user.service';
import { TonConnectService } from '../../shared/services/ton-connect.service';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  selector: 'app-wallet',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './wallet.component.html',
  styleUrl: './wallet.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WalletComponent implements OnInit {
  private walletApi = inject(WalletApiService);
  protected userService = inject(UserService);
  private tonConnect = inject(TonConnectService);
  private route = inject(ActivatedRoute);

  protected isLoadingBalance = signal(false);
  protected balanceError = signal<string | null>(null);
  protected balanceTon = signal<number | null>(null);
  protected isConnecting = signal(false);
  protected showReturnButton = signal(false);

  async ngOnInit() {
    // Check if this is a standalone wallet page
    const routePath = this.route.snapshot.routeConfig?.path;
    this.showReturnButton.set(routePath === 'wallet');
    
    if (this.userService.isWalletConnected()) {
      await this.loadBalance();
    }
  }

  get truncatedAddress(): string {
    const address = this.userService.profile()?.walletAddress;
    if (!address) return '';
    return `${address.slice(0, 6)}…${address.slice(-4)}`;
  }

  async connectWallet() {
    this.isConnecting.set(true);
    try {
      const address = await this.tonConnect.connect();
      
      // Check if user cancelled (no address returned)
      if (!address) {
        return;
      }
      
      await this.walletApi.connectWallet(address);
      const profile = this.userService.profile();
      if (profile) {
        this.userService.setProfile({ ...profile, walletAddress: address });
      }
      await this.loadBalance();
    } catch (error) {
      // user cancelled or connection error
      console.log('Wallet connection cancelled or failed:', error);
    } finally {
      this.isConnecting.set(false);
    }
  }

  async disconnect() {
    await this.tonConnect.disconnect();
    await this.walletApi.disconnectWallet();
    const profile = this.userService.profile();
    if (profile) {
      this.userService.setProfile({ ...profile, walletAddress: null });
    }
    this.balanceTon.set(null);
    this.balanceError.set(null);
  }

  async loadBalance() {
    const address = this.userService.profile()?.walletAddress;
    if (!address) return;

    this.isLoadingBalance.set(true);
    this.balanceError.set(null);
    try {
      const balance = await this.tonConnect.getBalance(address);
      this.balanceTon.set(balance);
    } catch {
      this.balanceError.set('Не удалось загрузить баланс');
    } finally {
      this.isLoadingBalance.set(false);
    }
  }
}
