import { invitationOf, launchInvitation, miniAppProof } from './mini-app.util';
import { LAUNCH_STORAGE_PREFIX } from './launch-params';
import { ExternalProviderEnum } from '../enums/external-provider.enum';

describe('mini-app.util', () => {
  afterEach(() => {
    sessionStorage.removeItem(LAUNCH_STORAGE_PREFIX + 'tg');
    sessionStorage.removeItem(LAUNCH_STORAGE_PREFIX + 'vkHash');
  });

  it('should read the Telegram signature captured from the fragment', () => {
    sessionStorage.setItem(LAUNCH_STORAGE_PREFIX + 'tg', 'user=1&hash=x');

    expect(miniAppProof('telegram')).toEqual({ provider: ExternalProviderEnum.Telegram, initData: 'user=1&hash=x' });
  });

  it('should have no proof outside the mini-app', () => {
    expect(miniAppProof('vk')).toBeUndefined();
  });

  it('should restore dashes of the invitation token', () => {
    expect(invitationOf('inv_0123456789abcdef0123456789ABCDEF')).toBe('01234567-89ab-cdef-0123-456789abcdef');
    expect(invitationOf('inv_01234567-89ab-cdef-0123-456789abcdef')).toBe('01234567-89ab-cdef-0123-456789abcdef');
    expect(invitationOf('task_1')).toBeNull();
    expect(invitationOf('inv_garbage')).toBeNull();
  });

  it('should take the Telegram invitation from start_param of initData', () => {
    const initData = 'user=1&start_param=inv_0123456789abcdef0123456789abcdef&hash=x';

    expect(launchInvitation({ provider: ExternalProviderEnum.Telegram, initData }))
      .toBe('01234567-89ab-cdef-0123-456789abcdef');
  });

  it('should take the VK invitation from the fragment', () => {
    sessionStorage.setItem(LAUNCH_STORAGE_PREFIX + 'vkHash', 'inv_0123456789abcdef0123456789abcdef');

    expect(launchInvitation({ provider: ExternalProviderEnum.Vk, vkLaunchParams: 'vk_user_id=1' }))
      .toBe('01234567-89ab-cdef-0123-456789abcdef');
  });
});
