import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type { TonConnectUI } from '@tonconnect/ui';

@Injectable({ providedIn: 'root' })
export class TonConnectService {
  private readonly http = inject(HttpClient);
  private instance: TonConnectUI | null = null;

  private async getInstance(): Promise<TonConnectUI> {
    if (!this.instance) {
      const { TonConnectUI } = await import('@tonconnect/ui');
      this.instance = new TonConnectUI({
        manifestUrl: `${window.location.origin}/tonconnect-manifest.json`
      });
    }
    return this.instance;
  }

  connect(): Promise<string> {
    return new Promise(async (resolve, reject) => {
      let unsubscribe: (() => void) | undefined;
      try {
        const tonConnect = await this.getInstance();
        let settled = false;

        unsubscribe = tonConnect.onStatusChange(wallet => {
          if (settled) return;
          if (wallet?.account?.address) {
            settled = true;
            unsubscribe?.();
            resolve(wallet.account.address);
          }
        });

        await tonConnect.openModal();
      } catch (err) {
        unsubscribe?.();
        reject(err);
      }
    });
  }

  async disconnect(): Promise<void> {
    if (this.instance) {
      await this.instance.disconnect();
    }
  }

  async getBalance(address: string): Promise<number> {
    const url = `https://toncenter.com/api/v2/getAddressBalance?address=${encodeURIComponent(address)}`;
    const { result } = await firstValueFrom(
      this.http.get<{ result: string }>(url)
    );
    const nanoTon = parseFloat(result);
    return Math.round(nanoTon / 1e9 * 100) / 100;
  }
}
