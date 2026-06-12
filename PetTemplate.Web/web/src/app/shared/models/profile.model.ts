export interface ProfileModel {
  id: string | null;
  userName: string | null;
  avatarId: number | null;
  comment: string | null;
  isAdult: boolean;
  hasAcceptedTerms: boolean;
  walletAddress: string | null;
}
